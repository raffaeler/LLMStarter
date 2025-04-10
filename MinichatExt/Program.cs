using System.ClientModel.Primitives;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MinichatExt;

/*

This chat app needs the following environment variables:
- AZURE_MODEL_NAME
- AZURE_SECRET_KEY
- AZURE_ENDPOINT

The AZURE_SECRET_KEY can be injected from a file (see below)

*/


internal class Program
{
    static async Task Main(string[] args)
    {
        InjectSecret();
        var p = new Program();
        var endpoint = p.GetAzureEndpoint();
        var secretKey = p.GetAzureSecretKey();
        var modelname = p.GetAzureModelName();

        var host = Host.CreateDefaultBuilder(args)
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug(); // log to the Output Window
            })
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton<IChatClient>(sp =>
                     OpenAIClientExtensions.AsIChatClient(
                         new Azure.AI.OpenAI.AzureOpenAIClient(
                             new Uri(endpoint),
                             new System.ClientModel.ApiKeyCredential(secretKey),
                             new Azure.AI.OpenAI.AzureOpenAIClientOptions()
                             {
                                 NetworkTimeout = TimeSpan.FromMinutes(5),
                                 RetryPolicy = new ClientRetryPolicy(3),
                             }).GetChatClient(modelname)));

                services.AddHostedService<ChatService>();
            });
        var app = host.Build();
        
        await app.RunAsync();
    }

    private static void InjectSecret()
    {
        var secretFilename = @"H:\ai\_demosecrets\east-us-2.txt";
        if (File.Exists(secretFilename))
        {
            var secret = File
                    .ReadAllText(secretFilename)
                    .Trim();
            Environment.SetEnvironmentVariable("AZURE_SECRET_KEY", secret);
        }
    }

    private JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private string GetAzureEndpoint()
        => Environment.GetEnvironmentVariable("AZURE_ENDPOINT") ?? throw new Exception("AZURE_ENDPOINT not found");

    private string GetAzureSecretKey()
        => Environment.GetEnvironmentVariable("AZURE_SECRET_KEY") ?? throw new Exception("AZURE_SECRET_KEY not found");

    private string GetAzureModelName()
        => Environment.GetEnvironmentVariable("AZURE_MODEL_NAME") ?? throw new Exception("AZURE_MODEL_NAME not found");
}

public class ChatService : BackgroundService
{
    private readonly ILogger _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IChatClient _client;
    private AIFunction? _tool;

    public ChatService(
        ILogger<ChatService> logger,
        IHostApplicationLifetime lifetime,
        IChatClient client)
    {
        _logger = logger;
        _lifetime = lifetime;
        _client = client;
    }

    [Description("Reverse a string")]
    [return: Description("The reversed string")]
    public string ReverseString([Description("The string to reverse")] string text)
        => new string(text.Reverse().ToArray());

    /// <summary>
    /// Starts the chat
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine("Minichat using Microsoft.Extensions.AI by Raffaele Rialdi");
        Console.WriteLine("");
        Console.WriteLine("Start chatting or type 'exit' to quit");

        string? systemPrompt = GetOptionalSystemPrompt();

        _tool = AIFunctionFactory.Create(ReverseString);
        ChatOptions options = new()
        {
            MaxOutputTokens = 500,
            Temperature = 0.7f,
            TopP = 0.8f,
            FrequencyPenalty = 0,
            PresencePenalty = 0,
            Tools = [_tool],
        };

        if (options.Tools.Count > 0)
        {
            options.ToolMode = ChatToolMode.Auto;
            //options.AllowParallelToolCalls = true;
        }
        else
        {
            options.ToolMode = ChatToolMode.None;
        }

        await ChatLoop(options, systemPrompt);
        _lifetime.StopApplication();
    }

    /// <summary>
    /// Prompt the user for a system prompt
    /// or return null if the user provide
    /// an empty string.
    /// </summary>
    /// <returns></returns>
    private string? GetOptionalSystemPrompt()
    {
        Console.WriteLine("Type the system prompt or press enter to skip");
        Console.Write("System prompt: ");
        var systemPrompt = Console.ReadLine();
        return string.IsNullOrWhiteSpace(systemPrompt) ? null : systemPrompt;
    }

    /// <summary>
    /// The main chat loop
    /// </summary>
    /// <param name="options">The parameters for the model</param>
    /// <param name="systemprompt">The optional system prompt</param>
    /// <returns></returns>
    private async Task ChatLoop(
        ChatOptions options,
        string? systemprompt)
    {
        _logger.LogInformation("Entering the chat loop");
        List<ChatMessage> prompts = new();
        if (systemprompt != null)
        {
            prompts.Add(new ChatMessage(ChatRole.System, systemprompt));
        }

        string answer = "";
        bool lastWasTool = false;
        do
        {
            if (!lastWasTool)
            {
                Console.Write("You: ");
                var userMessage = Console.ReadLine();
                if (userMessage == "exit")
                {
                    Console.WriteLine("Goodbye!");
                    return;
                }

                prompts.Add(new ChatMessage(ChatRole.User, userMessage));
            }

            var response = await _client.GetResponseAsync(prompts, options);
            if (response == null)
            {
                Console.WriteLine("No response from the assistant");
                continue;
            }

            foreach (var message in response.Messages)
            {
                prompts.Add(message);

                if (response.FinishReason == ChatFinishReason.ContentFilter)
                {
                    // Content filtered by the model
                    answer = $"Answer was filtered";
                    lastWasTool = false;
                    // completion.Refusal?
                }
                else if (response.FinishReason == ChatFinishReason.Length)
                {
                    // Max tokens reached
                    answer = "AI: Max tokens reached";
                    lastWasTool = false;
                }
                else if (response.FinishReason == ChatFinishReason.Stop)
                {
                    // The completion is ready
                    // An answer is finally available
                    answer = GetAnswer(response);
                    Console.WriteLine($"AI: {answer}");
                    lastWasTool = false;
                }
                else if (response.FinishReason == ChatFinishReason.ToolCalls)
                {
                    // The model requested to invoke a tool
                    // Tools are the new name for Functions
                    answer = "AI: tool request";
                    await ProcessToolRequest(message, prompts);
                    lastWasTool = true;
                }
                else
                {
                    answer = $"AI: Finish reason: {response.FinishReason}";
                    lastWasTool = false;
                }
            }


        } while (true);
    }

    /// <summary>
    /// This method processes the tool request
    /// </summary>
    /// <param name="completion">The object from the model</param>
    /// <param name="prompts">The list of prompts (the state)</param>
    private async Task ProcessToolRequest(
        ChatMessage completion,
        IList<ChatMessage> prompts)
    {
        foreach (var toolCall in completion.Contents.OfType<FunctionCallContent>())
        {
            var functionName = toolCall.Name;
            var arguments = new AIFunctionArguments(toolCall.Arguments);

            Debug.WriteLine($"AI asked to invoke function {functionName}(...)");

            if (_tool == null) continue;
            var result = await _tool.InvokeAsync(arguments);

            ChatMessage responseMessage = new(ChatRole.Tool,
                [
                    new FunctionResultContent(toolCall.CallId, result)
                ]);

            prompts.Add(responseMessage);
        }
    }

    /// <summary>
    /// Extract the anser from the completion
    /// </summary>
    /// <param name="completion">The completion object obtained from the model</param>
    /// <returns>The string from the model</returns>
    private string GetAnswer(ChatResponse completion)
        => string.Join(string.Empty, completion.Messages.Select(m => m.Text));


}
