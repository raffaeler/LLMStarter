using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MiniStreamingChatExt;

public class ChatService : BackgroundService
{
    /// <summary>
    /// When true, the colors for odd and even tokens are alternated
    /// </summary>
    private const bool AlternatedColors = false;

    private readonly ILogger _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly CustomTool _customTool;
    private readonly IChatClient _client;
    private Dictionary<string, AIFunction> _tools = new();
    private static JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public ChatService( 
        ILogger<ChatService> logger,
        IHostApplicationLifetime lifetime,
        CustomTool customTool,
        IChatClient client)
    {
        _logger = logger;
        _lifetime = lifetime;
        _customTool = customTool;
        _client = client;
    }

    /// <summary>
    /// Starts the chat
    /// </summary>
    protected override async Task ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine("Minichat using Microsoft.Extensions.AI by Raffaele Rialdi");
        Console.WriteLine("");
        Console.WriteLine("Start chatting or type 'exit' to quit");

        string? systemPrompt = GetOptionalSystemPrompt();

        AIFunctionFactoryOptions ffOptions = new()
        {
            ConfigureParameterBinding = (parameter) =>
            {
                // This is the default behavior
                // The parameter name is used as the key
                // in the JSON object
                //binding.Name = parameter.Name;

                if (parameter.Name != "redact" ||
                    parameter.ParameterType != typeof(bool))
                {
                    return default;
                }

                return new()
                {
                    // redact is not sent to the model
                    ExcludeFromSchema = true,
                    BindParameter = (ParameterInfo p, AIFunctionArguments a) =>
                    {
                        if (a.Context == null ||
                            !a.Context.TryGetValue("redact",
                                out object? redactObj) ||
                            redactObj == null ||
                            redactObj is not bool)
                        {
                            return default;
                        }

                        var redact = (bool)redactObj;
                        return redact;
                    }
                };
            },
        };

        _tools[nameof(_customTool.ReverseString)] =
            AIFunctionFactory.Create(_customTool.ReverseString, ffOptions);
        _tools[nameof(_customTool.ToUpper)] =
            AIFunctionFactory.Create(_customTool.ToUpper, ffOptions);

        ChatOptions options = new()
        {
            MaxOutputTokens = 1500,
            //Temperature = 0.7f,   // not supported by gpt-5-nano
            //TopP = 0.8f,
            FrequencyPenalty = 0,
            PresencePenalty = 0,
            Tools = _tools.Values.OfType<AITool>().ToList(),
        };

        if (options.Tools.Count > 0)
        {
            options.ToolMode = ChatToolMode.Auto;
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
        var usageColor = ConsoleColor.Cyan;

        string answer = "";
        bool lastWasTool = false;
        do
        {
            Console.ForegroundColor = currentColor;
            if (!lastWasTool)
            {
                Console.Write("You: ");
                var userMessage = Console.ReadLine();
                if (userMessage == "quit" || userMessage == "exit")
                {
                    Console.WriteLine("Goodbye!");
                    return;
                }

                prompts.Add(new ChatMessage(ChatRole.User, userMessage));
            }

            IAsyncEnumerable<ChatResponseUpdate> streaming =
                _client.GetStreamingResponseAsync(prompts, options);

            Debug.WriteLine("=== Asynchronous updates ===");
            StreamingManager sm = new();
            await sm.ProcessIncomingStreaming(streaming, _options, true,
                onOutOfBandMessage: Console.WriteLine,
                onToken: (token, isEven) =>
                {
                    Console.ForegroundColor = AlternatedColors && isEven ? evenColor : oddColor;
                    Console.Write(token);
                },
                onUsage: usage =>
                {
                    Console.ForegroundColor = usageColor;
                    Console.WriteLine(Environment.NewLine +
                        $"Usage: T={usage.TotalTokenCount} = " +
                        $"I({usage.InputTokenCount}) + " +
                        $"O({usage.OutputTokenCount}) + " +
                        $"A({usage.AdditionalCounts?.Select(a => a.Value).Sum() ?? 0})");
                });

            if (sm.Completion?.Length > 0)
                prompts.Add(new ChatMessage(ChatRole.Assistant, sm.Completion));

            if (sm.ToolCalls.Count > 0)
                prompts.Add(new ChatMessage(ChatRole.Assistant, sm.ToolCalls));


            Console.ForegroundColor = currentColor;
            Console.WriteLine();

            lastWasTool = false;
            if (sm.FinishReason == ChatFinishReason.ContentFilter)
            {
                // Content filtered by the model
                answer = $"Answer was filtered";
                Console.WriteLine($"AI Refusal: {sm.RefusalMessage}");
            }
            else if (sm.FinishReason == ChatFinishReason.Length)
            {
                // Max tokens reached
                answer = "AI: Max tokens reached";
                Console.WriteLine($"AI: {answer}");
            }
            else if (sm.FinishReason == ChatFinishReason.Stop)
            {
                // The completion is ready
                // An answer is finally available
                answer = sm.Completion ?? string.Empty;
                Debug.WriteLine($"AI: {answer}");
            }
            else if (sm.FinishReason == ChatFinishReason.ToolCalls)
            {
                // The model requested to invoke a tool
                // Tools are the new name for Functions
                answer = "AI: tool request";
                await ProcessToolRequest(sm.ToolCalls, prompts);
                lastWasTool = true;
            }
            else
            {
                answer = $"AI: Finish reason: {sm.FinishReason}";
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
            arguments.Context = new Dictionary<object, object?>();
            arguments.Context["redact"] = true;

            Debug.WriteLine($"AI asked to invoke function {functionName}(...)");

            if (!_tools.TryGetValue(functionName, out AIFunction? _tool))
            {
                Console.WriteLine($"Unknown function {functionName}");
                continue;
            }

            var result = await _tool.InvokeAsync(arguments);

            ChatMessage responseMessage = new(ChatRole.Tool,
                [
                    new FunctionResultContent(toolCall.CallId, result)
                ]);

            prompts.Add(responseMessage);
        }
    }

}
