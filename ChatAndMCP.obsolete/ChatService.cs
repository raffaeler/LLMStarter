using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using ChatAndMCP.McpHelpers;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;


namespace ChatAndMCP;

/// <summary>
/// A background service handling the chat operations.
/// </summary>
internal class ChatService : BackgroundService
{
    /// <summary>
    /// When true, the colors for odd and even tokens are alternated
    /// </summary>
    private const bool AlternatedColors = false;

    private readonly ILogger _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHostApplicationLifetime _lifetime;
    //private readonly IChatClient _client;
    private readonly McpProxyFactoryService _mcpProxyFactoryService;
    private Dictionary<string, AIFunction> _tools = new();
    private Dictionary<string, AIFunction> _resources = new();
    private Dictionary<string, AIFunction> _prompts = new();
    private static JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    //private static Func<string, Dictionary<string, object?>?> _toolsArgumentParser =
    //    static json => JsonSerializer.Deserialize<Dictionary<string, object?>>(json, AIJsonUtilities.DefaultOptions);

    public ChatService(
        ILogger<ChatService> logger,
        IServiceProvider serviceProvider,
        IHostApplicationLifetime lifetime,
        //IChatClient client,
        McpProxyFactoryService mcpProxyFactoryService)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _lifetime = lifetime;
        //_client = client;
        _mcpProxyFactoryService = mcpProxyFactoryService;
    }

    /// <summary>
    /// Starts the chat
    /// </summary>
    protected override async Task ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        await _mcpProxyFactoryService.Start(LoggingLevel.Debug);

        Console.WriteLine("Minimal Chat also acting as MCP Client, by Raffaele Rialdi");
        Console.WriteLine("");
        Console.WriteLine("Start chatting or type 'exit' to quit");

        string? systemPrompt = GetOptionalSystemPrompt();

        foreach (var proxy in _mcpProxyFactoryService.McpProxies)
        {
            if (proxy.Client != null)
            {
                if (proxy.Client.ServerCapabilities.Tools != null)
                {
                    var clientTools = await proxy.Client.ListToolsAsync();
                    foreach (var c in clientTools)
                    {
                        _tools[c.Name] = (AIFunction)c;
                    }
                }

                if (proxy.Client.ServerCapabilities.Resources != null)
                {
                    //var resources = await proxy.Client.ListResourcesAsync();
                    //foreach(var r in resources)
                    //{
                    //    if (!_resources.ContainsKey(r.Name))
                    //        _resources[r.Name] = (AIFunction)r;
                    //}
                }

                if (proxy.Client.ServerCapabilities.Prompts != null)
                {
                    var prompts = await proxy.Client.ListPromptsAsync();
                    var prompt = prompts.SingleOrDefault(p => p.Name == "system");
                    if (prompt != null)
                    {
                        var system = await prompt.GetAsync(null);
                        if (system != null)
                        {
                            string[] messageArray = system.Messages
                                .Select(m => m.Content)
                                .OfType<TextContentBlock>()
                                .Where(t => t != null)
                                .Select(t => t.Text)
                                .ToArray();

                            systemPrompt += Environment.NewLine +
                                string.Join(Environment.NewLine, messageArray);
                        }
                    }
                }
            }
        }

        if (systemPrompt != null)
        {
            Console.WriteLine("Final System Prompt");
            Console.WriteLine(systemPrompt);
            Console.WriteLine();
        }

        //if (_inMemoryMcpService.Client != null)
        //{
        //    var clientTools = await _inMemoryMcpService.Client.ListToolsAsync();
        //    _tools = clientTools.ToDictionary(c => c.Name, c => (AIFunction)c);

        //    //var resources = await _inMemoryMcpService.Client.ListResourcesAsync();
        //    //_resources = resources.ToDictionary(r => r.Name, r => r)
        //}

        ChatOptions options = new()
        {
            MaxOutputTokens = 5500,
            Temperature = 0.7f,
            TopP = 0.8f,
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
        var client = _serviceProvider
            .GetRequiredKeyedService<IChatClient>("main");

        Console.WriteLine("Entering the chat loop.");
        Console.WriteLine("- type 'file' to send a prompt + document to the model.");
        Console.WriteLine("- type 'summary' to send a prompt + document to the model.");
        Console.WriteLine("- type 'elicit' to send a prompt about guessing a number.");
        Console.WriteLine("- type 'browse' to send a prompt about browsing with Playwright");
        Console.WriteLine("- type 'quit' or 'exit' to terminate the conversation.");
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
                else if (userMessage == "file")
                {
                    userMessage = Prompts.GetPromptAboutLocalFiles();
                    Console.WriteLine("Using prompt:");
                    Console.WriteLine(userMessage);
                }
                else if (userMessage == "summary")
                {
                    userMessage = Prompts.GetPromptWithDocument();
                    Console.WriteLine("Using prompt:");
                    Console.WriteLine(userMessage);
                }
                else if (userMessage == "elicit")
                {
                    userMessage = Prompts.GetPromptToElicitUser();
                    Console.WriteLine("Using prompt:");
                    Console.WriteLine(userMessage);
                }
                else if (userMessage == "browse")
                {
                    userMessage = Prompts.GetPromptToBrowseTheInternet();
                    Console.WriteLine("Using prompt:");
                    Console.WriteLine(userMessage);
                }

                prompts.Add(new ChatMessage(ChatRole.User, userMessage));
            }

            IAsyncEnumerable<ChatResponseUpdate> streaming =
                client.GetStreamingResponseAsync(prompts, options);

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
                answer = sm.Completion ?? string.Empty;
                Debug.WriteLine($"AI: {answer}");
            }
            else if (sm.FinishReason == ChatFinishReason.ToolCalls)
            {
                // The model requested to invoke a tool
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

            // we may provide additional context through custom argument binding
            //arguments.Context = new Dictionary<object, object?>();
            //arguments.Context["redact"] = true;

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
