using System.ClientModel.Primitives;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MiniStreamingChatExt;


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
    private static Func<string, Dictionary<string, object?>?> _toolsArgumentParser =
        static json => JsonSerializer.Deserialize<Dictionary<string, object?>>(json, AIJsonUtilities.DefaultOptions);

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

        var currentColor = Console.ForegroundColor;
        var evenColor = ConsoleColor.Yellow;
        var oddColor = ConsoleColor.Green;
        var otherColor = ConsoleColor.Cyan;

        string answer = "";
        bool lastWasTool = false;
        do
        {
            Console.ForegroundColor = currentColor;
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

            // Variables accumulating the small parts
            // of the completions while streaming
            ChatFinishReason? finishReason = default;
            ChatRole? streamedRole = default;
            StringBuilder contentBuilder = new();
            StringBuilder refusalBuilder = new();

            List<AIContent> toolCalls = [];
            //Dictionary<string, PartialFunctionInfo> toolsPartialInfo = [];

            IAsyncEnumerable<ChatResponseUpdate> streaming =
                _client.GetStreamingResponseAsync(prompts, options);

            int count = 0;
            await foreach (var update in streaming)
            {
                if (update == null)
                {
                    Console.WriteLine("No response from the assistant");
                    continue;
                }

                if (update.Role != null)
                    streamedRole = update.Role;

                if (update.FinishReason != null)
                    finishReason = update.FinishReason;

                // Alternate colors to highlight the streaming
                // of the completion.
                // Usually the model is fast enough and the streming
                // cannot be appreciated.
                foreach (AIContent content in update.Contents)
                {
                    if (content is TextContent textContent)
                    {
                        if (count++ % 2 == 0)
                            Console.ForegroundColor = evenColor;
                        else
                            Console.ForegroundColor = oddColor;

                        Debug.Assert(update.Text == textContent.Text);
                        contentBuilder.Append(textContent.Text);
                        Console.Write(textContent.Text);

                        if (content.AdditionalProperties != null &&
                            content.AdditionalProperties.TryGetValue(
                                "refusal", out object? refusal) &&
                            refusal is string refusalText &&
                            refusalText != null)
                        {
                            refusalBuilder.Append(refusalText);
                        }
                    }
                    else if (content is FunctionCallContent functionCallContent)
                    {
                        toolCalls.Add(functionCallContent);
                        //if (!toolsPartialInfo.TryGetValue(functionCallContent.CallId,
                        //    out PartialFunctionInfo? funcInfo) || funcInfo == null)
                        //{
                        //    funcInfo = new();
                        //    toolsPartialInfo[functionCallContent.CallId] = funcInfo;
                        //}

                        //if (!string.IsNullOrEmpty(functionCallContent.Name))
                        //{
                        //    funcInfo.Name = functionCallContent.Name;
                        //}

                        //if (functionCallContent.Arguments != null)
                        //{
                        //    foreach (var arg in functionCallContent.Arguments)
                        //    {
                        //        funcInfo.Arguments[arg.Key] = arg.Value;
                        //    }
                        //}

                        //if (functionCallContent.Exception != null)
                        //{
                        //    Debug.Assert(false);
                        //}
                    }
                    else if (content is DataContent dataContent)
                    {
                        // image
                        var blob = dataContent.Data;
                        var mediaType = dataContent.MediaType;
                        var uri = dataContent.Uri;
                        // ...
                    }
                    else if (content is FunctionResultContent functionResultContent)
                    {
                        //functionResultContent.CallId
                        //functionResultContent.Result
                        //functionResultContent.Exception
                        Debug.Assert(false);
                    }
                    else if (content is UsageContent usageContent)
                    {
                        // TODO
                        var usage = usageContent.Details;

                        Console.WriteLine();
                        Console.ForegroundColor = otherColor;
                        Console.WriteLine($"Usage: T = {usage.TotalTokenCount} = I({usage.InputTokenCount}) + O({usage.OutputTokenCount})");
                    }
                    else
                    {
                        throw new NotImplementedException(
                            $"Unsupported {content.GetType().FullName}");
                    }

                }
            }

            Console.ForegroundColor = currentColor;
            Console.WriteLine();

            //foreach (KeyValuePair<string, PartialFunctionInfo> funcInfo in toolsPartialInfo)
            //{
            //    toolCalls.Add(new FunctionCallContent(funcInfo.Key, funcInfo.Value.Name, funcInfo.Value.Arguments));
            //}


            var completion = contentBuilder.ToString();
            if (completion?.Length > 0)
                prompts.Add(new ChatMessage(ChatRole.Assistant, completion));
            if (toolCalls.Count > 0)
                prompts.Add(new ChatMessage(ChatRole.Assistant, toolCalls));


            if (finishReason == ChatFinishReason.ContentFilter)
            {
                // Content filtered by the model
                answer = $"Answer was filtered";
                lastWasTool = false;
                Console.WriteLine($"AI Refusal: {refusalBuilder.ToString()}");
            }
            else if (finishReason == ChatFinishReason.Length)
            {
                // Max tokens reached
                answer = "AI: Max tokens reached";
                lastWasTool = false;
            }
            else if (finishReason == ChatFinishReason.Stop)
            {
                // The completion is ready
                // An answer is finally available
                answer = completion ?? string.Empty;
                Debug.WriteLine($"AI: {answer}");
                lastWasTool = false;
            }
            else if (finishReason == ChatFinishReason.ToolCalls)
            {
                // The model requested to invoke a tool
                // Tools are the new name for Functions
                answer = "AI: tool request";
                await ProcessToolRequest(toolCalls, prompts);
                lastWasTool = true;
            }
            else
            {
                answer = $"AI: Finish reason: {finishReason}";
                lastWasTool = false;
            }
        }
        while (true);



    }

    /// <summary>
    /// This method processes the tool request
    /// </summary>
    /// <param name="completion">The object from the model</param>
    /// <param name="prompts">The list of prompts (the state)</param>
    private async Task ProcessToolRequest(
        IList<AIContent> toolContents,
        IList<ChatMessage> prompts)
    {
        foreach (var toolCall in toolContents.OfType<FunctionCallContent>())
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

//internal class PartialFunctionInfo
//{
//    public string Name { get; set; } = string.Empty;
//    public Dictionary<string, object?> Arguments { get; } = new();
//}