using System.Diagnostics;
using System.Text;
using System.Text.Json;

using ConsoleUtilities;
using McpClientUtilities;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace ChatAndMultipleMcps;

/// <summary>A background service handling the chat operations.</summary>
internal sealed class ChatService : BackgroundService
{
    private const bool AlternatedColors = false;

    private readonly IServiceProvider _serviceProvider;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly McpProxyFactoryService _mcpClientFactoryService;
    private readonly IConsoleTerminal _terminal;
    private readonly ConsoleLineEditor _lineEditor;
    private readonly VerboseState _verboseState;
    private readonly IDeclarativeAgentCatalog _agentCatalog;
    private readonly Dictionary<string, AIFunction> _tools = [];
    private readonly Dictionary<string, string> _toolsToMcp = [];
    private readonly ConsoleColor _defaultColor;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly ConsoleColor EvenColor = ConsoleColor.Yellow;
    private static readonly ConsoleColor OddColor = ConsoleColor.Green;
    private static readonly ConsoleColor InternalColor = ConsoleColor.DarkGray;
    private static readonly ConsoleColor UsageColor = ConsoleColor.Cyan;
    private static readonly ConsoleColor SystemColor = ConsoleColor.Blue;
    private static readonly ConsoleColor QuestionColor = ConsoleColor.Red;

    public ChatService(
        IServiceProvider serviceProvider,
        IHostApplicationLifetime lifetime,
        McpProxyFactoryService mcpFactoryService,
        IConsoleTerminal terminal,
        ConsoleLineEditor lineEditor,
        VerboseState verboseState,
        IDeclarativeAgentCatalog agentCatalog)
    {
        _serviceProvider = serviceProvider;
        _lifetime = lifetime;
        _mcpClientFactoryService = mcpFactoryService;
        _terminal = terminal;
        _lineEditor = lineEditor;
        _verboseState = verboseState;
        _agentCatalog = agentCatalog;
        _defaultColor = terminal.ForegroundColor;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _terminal.WriteLine("MCP powered chat, by Raffaele Rialdi");
        _terminal.ForegroundColor = EvenColor;
        _terminal.Write("Loading MCP Servers took: ");

        Stopwatch stopwatch = Stopwatch.StartNew();
        await _mcpClientFactoryService.StartAll(configuration =>
        {
            IChatClient samplingClient = _serviceProvider
                .GetRequiredKeyedService<IChatClient>("SummarySamplingClient");
            return new McpClientApp(samplingClient, _terminal).GetMcpClientOptions();
        });

        TimeSpan mcpLoadElapsed = stopwatch.Elapsed;
        stopwatch.Stop();
        _terminal.WriteLine($"{mcpLoadElapsed.TotalMilliseconds}ms");
        _terminal.ForegroundColor = _defaultColor;
        _terminal.WriteLine();

        string systemPrompt = string.Empty;
        _terminal.WriteLine("Start chatting, or type / to choose a command.");

        _terminal.ForegroundColor = EvenColor;
        stopwatch.Restart();
        foreach (McpProxy proxy in _mcpClientFactoryService.Proxies)
        {
            if (proxy.McpClient is null)
            {
                continue;
            }

            if (proxy.McpClient.ServerCapabilities.Tools is not null)
            {
                IList<McpClientTool> clientTools = await proxy.McpClient.ListToolsAsync();
                foreach (McpClientTool tool in clientTools)
                {
                    _tools[tool.Name] = (AIFunction)tool;
                    _toolsToMcp[tool.Name] = proxy.McpClient.ServerInfo.Name;
                }

                _terminal.WriteLine($"Tools for MCP {proxy.McpClient.ServerInfo.Name}: {clientTools.Count}");
            }

            if (proxy.McpClient.ServerCapabilities.Prompts is not null)
            {
                IList<McpClientPrompt> prompts = await proxy.McpClient.ListPromptsAsync();
                IEnumerable<McpClientPrompt> systemPrompts = prompts
                    .Where(prompt => prompt.Name.EndsWith("system", StringComparison.OrdinalIgnoreCase));

                StringBuilder systemPromptBuilder = new();
                foreach (McpClientPrompt prompt in systemPrompts)
                {
                    GetPromptResult system = await prompt.GetAsync(null);
                    string[] messages = system.Messages
                        .Select(message => message.Content)
                        .OfType<TextContentBlock>()
                        .Select(content => content.Text)
                        .ToArray();
                    systemPromptBuilder.AppendLine(string.Join(Environment.NewLine, messages));
                }

                if (systemPromptBuilder.Length > 0)
                {
                    systemPrompt += systemPromptBuilder.ToString();
                }
            }
        }

        mcpLoadElapsed = stopwatch.Elapsed;
        stopwatch.Stop();
        _terminal.WriteLine($"Loading tools from the MCPs took: {mcpLoadElapsed.TotalMilliseconds}ms");
        _terminal.ForegroundColor = _defaultColor;
        _terminal.WriteLine();

        if (_tools.Keys.Any(tool => tool.Contains("browse", StringComparison.OrdinalIgnoreCase)))
        {
            systemPrompt = "Use the browser when needed" + Environment.NewLine + systemPrompt;
        }

        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            _terminal.ForegroundColor = SystemColor;
            _terminal.WriteLine("Final System Prompt");
            _terminal.WriteLine(systemPrompt);
            _terminal.ForegroundColor = _defaultColor;
            _terminal.WriteLine();
        }

        ChatOptions options = new()
        {
            MaxOutputTokens = 5500,
            FrequencyPenalty = 0,
            PresencePenalty = 0,
            Tools = _tools.Values.OfType<AITool>().ToList(),
        };
        if (options.Tools.Count == 0)
        {
            options.ToolMode = ChatToolMode.None;
        }

        await ChatLoop(options, systemPrompt, cancellationToken);
        _lifetime.StopApplication();
    }

    private async Task ChatLoop(
        ChatOptions options,
        string initialSystemPrompt,
        CancellationToken cancellationToken)
    {
        IChatClient client = _serviceProvider.GetRequiredKeyedService<IChatClient>("main");
        string? modelName = client.GetService<ChatClientMetadata>()?.DefaultModelId;

        _terminal.WriteLine("Entering the chat loop. Type / to browse commands.");
        List<ChatMessage> conversation = [];
        HashSet<string> selectedAgents = new(StringComparer.OrdinalIgnoreCase);
        ChatCommandMenu commandMenu = new(_verboseState, _agentCatalog, selectedAgents);
        string systemPrompt = initialSystemPrompt;
        bool lastWasTool = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            _terminal.ForegroundColor = _defaultColor;
            if (!lastWasTool)
            {
                string? userMessage = _lineEditor.ReadLine("You: ", commandMenu.GetCompletions);
                if (userMessage is null)
                {
                    _terminal.WriteLine("Goodbye!");
                    return;
                }

                if (string.IsNullOrWhiteSpace(userMessage))
                {
                    continue;
                }

                if (userMessage.StartsWith('/'))
                {
                    CommandResult result = HandleCommand(
                        userMessage,
                        conversation,
                        selectedAgents,
                        ref systemPrompt,
                        out string? promptText);
                    if (result == CommandResult.Quit)
                    {
                        _terminal.WriteLine("Goodbye!");
                        return;
                    }

                    if (result == CommandResult.Handled)
                    {
                        continue;
                    }

                    userMessage = promptText!;
                }
                else if (Prompts.PromptTemplates.TryGetValue(
                    userMessage.ToLowerInvariant(),
                    out (string promptDescription, string promptText) template))
                {
                    // Preserve the old shorthand while /prompt provides discoverability.
                    userMessage = template.promptText;
                }

                _terminal.WriteLine("Using prompt:");
                _terminal.ForegroundColor = QuestionColor;
                _terminal.WriteLine(userMessage);
                _terminal.ForegroundColor = _defaultColor;
                conversation.Add(new ChatMessage(ChatRole.User, userMessage));
            }

            _terminal.WriteLine($"Using model:{modelName}");
            List<ChatMessage> requestMessages = BuildRequestMessages(
                systemPrompt,
                selectedAgents,
                conversation);
            IAsyncEnumerable<ChatResponseUpdate> streaming =
                client.GetStreamingResponseAsync(requestMessages, options, cancellationToken);

            Debug.WriteLine("=== Incoming Asynchronous Streaming updates ===");
            StreamingManager streamingManager = new();
            await streamingManager.ProcessIncomingStreaming(
                streaming,
                SerializerOptions,
                true,
                onOutOfBandMessage: _terminal.WriteLine,
                onToken: (token, isEven) =>
                {
                    _terminal.ForegroundColor = AlternatedColors && isEven ? EvenColor : OddColor;
                    _terminal.Write(token);
                },
                onUsage: usage =>
                {
                    _terminal.ForegroundColor = UsageColor;
                    _terminal.WriteLine(Environment.NewLine
                        + $"Usage: T={usage.TotalTokenCount} = "
                        + $"I({usage.InputTokenCount}) + "
                        + $"O({usage.OutputTokenCount}) + "
                        + $"A({usage.AdditionalCounts?.Select(count => count.Value).Sum() ?? 0})");
                });

            if (!string.IsNullOrEmpty(streamingManager.Completion))
            {
                conversation.Add(new ChatMessage(ChatRole.Assistant, streamingManager.Completion));
            }

            if (streamingManager.ToolCalls.Count > 0)
            {
                conversation.Add(new ChatMessage(ChatRole.Assistant, streamingManager.ToolCalls));
            }

            _terminal.ForegroundColor = _defaultColor;
            _terminal.WriteLine();

            lastWasTool = false;
            if (streamingManager.FinishReason == ChatFinishReason.ContentFilter)
            {
                _terminal.WriteLine($"AI Refusal: {streamingManager.RefusalMessage}");
            }
            else if (streamingManager.FinishReason == ChatFinishReason.Length)
            {
                _terminal.WriteLine("AI: Max tokens reached");
            }
            else if (streamingManager.FinishReason == ChatFinishReason.Stop)
            {
                Debug.WriteLine($"AI: {streamingManager.Completion}");
            }
            else if (streamingManager.FinishReason == ChatFinishReason.ToolCalls)
            {
                await ProcessToolRequest(streamingManager.ToolCalls, conversation);
                lastWasTool = true;
            }
            else
            {
                _terminal.WriteLine($"AI: Finish reason: {streamingManager.FinishReason}");
            }
        }
    }

    private CommandResult HandleCommand(
        string input,
        List<ChatMessage> conversation,
        HashSet<string> selectedAgents,
        ref string systemPrompt,
        out string? promptText)
    {
        promptText = null;
        string commandLine = input[1..];
        int separator = commandLine.IndexOf(' ');
        string command = separator < 0 ? commandLine : commandLine[..separator];
        string? argument = separator < 0 ? null : commandLine[(separator + 1)..];

        switch (command.ToLowerInvariant())
        {
            case "quit":
            case "exit":
                return CommandResult.Quit;

            case "new":
                conversation.Clear();
                _terminal.WriteLine("Starting a new chat.");
                return CommandResult.Handled;

            case "system":
                if (argument is null)
                {
                    _terminal.ForegroundColor = SystemColor;
                    _terminal.WriteLine(string.IsNullOrEmpty(systemPrompt)
                        ? "System prompt is empty."
                        : systemPrompt);
                    _terminal.ForegroundColor = _defaultColor;
                }
                else if (argument.Trim() == "\"\"")
                {
                    systemPrompt = string.Empty;
                    _terminal.WriteLine("System prompt cleared.");
                }
                else if (argument.Length > 0)
                {
                    systemPrompt = argument;
                    _terminal.WriteLine("System prompt updated.");
                }
                else
                {
                    _terminal.WriteLine("Select Show, Clear, or Set from the system menu.");
                }
                return CommandResult.Handled;

            case "prompt":
                if (argument is not null
                    && Prompts.PromptTemplates.TryGetValue(
                        argument.Trim().ToLowerInvariant(),
                        out (string promptDescription, string promptText) template))
                {
                    promptText = template.promptText;
                    return CommandResult.SendPrompt;
                }
                _terminal.WriteLine($"Unknown prompt '{argument}'. Select one from the /prompt menu.");
                return CommandResult.Handled;

            case "agent":
                DeclarativeAgentDescriptor? agent = _agentCatalog.GetAgents().FirstOrDefault(
                    candidate => string.Equals(candidate.Name, argument?.Trim(), StringComparison.OrdinalIgnoreCase));
                if (agent is null)
                {
                    _terminal.WriteLine(_agentCatalog.GetAgents().Count == 0
                        ? "No declarative agents are configured."
                        : $"Unknown agent '{argument}'. Select one from the /agent menu.");
                }
                else if (selectedAgents.Add(agent.Name))
                {
                    _terminal.WriteLine($"Agent '{agent.Name}' added to the context.");
                }
                else
                {
                    _terminal.WriteLine($"Agent '{agent.Name}' is already in the context.");
                }
                return CommandResult.Handled;

            case "verbose":
                if (string.Equals(argument?.Trim(), "on", StringComparison.OrdinalIgnoreCase))
                {
                    _verboseState.Enabled = true;
                    _terminal.WriteLine("Verbose logging is on.");
                }
                else if (string.Equals(argument?.Trim(), "off", StringComparison.OrdinalIgnoreCase))
                {
                    _verboseState.Enabled = false;
                    _terminal.WriteLine("Verbose logging is off.");
                }
                else
                {
                    _terminal.WriteLine("Select on or off from the /verbose menu.");
                }
                return CommandResult.Handled;

            default:
                _terminal.WriteLine($"Unknown command /{command}. Type / to browse commands.");
                return CommandResult.Handled;
        }
    }

    private List<ChatMessage> BuildRequestMessages(
        string systemPrompt,
        IReadOnlySet<string> selectedAgents,
        IEnumerable<ChatMessage> conversation)
    {
        List<string> systemSections = [];
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            systemSections.Add(systemPrompt);
        }

        foreach (DeclarativeAgentDescriptor agent in _agentCatalog.GetAgents()
            .Where(agent => selectedAgents.Contains(agent.Name)))
        {
            systemSections.Add(agent.Instructions);
        }

        List<ChatMessage> messages = [];
        if (systemSections.Count > 0)
        {
            messages.Add(new ChatMessage(
                ChatRole.System,
                string.Join(Environment.NewLine + Environment.NewLine, systemSections)));
        }
        messages.AddRange(conversation);
        return messages;
    }

    private async Task ProcessToolRequest(
        IList<AIContent> toolContents,
        IList<ChatMessage> conversation)
    {
        foreach (FunctionCallContent toolCall in toolContents.OfType<FunctionCallContent>())
        {
            string functionName = toolCall.Name;
            AIFunctionArguments arguments = new(toolCall.Arguments);
            string mcp = _toolsToMcp.TryGetValue(functionName, out string? serverName)
                ? serverName
                : "unknown";
            string args = string.Join(", ", arguments.Select(argument => $"{argument.Key}: {argument.Value}"));

            Debug.WriteLine($"Tool call start: {functionName}({args})");
            _terminal.ForegroundColor = InternalColor;
            _terminal.WriteLine($"Calling mcp:{mcp} tool:{functionName}({args})");

            if (!_tools.TryGetValue(functionName, out AIFunction? tool))
            {
                _terminal.WriteLine($"Unknown function {functionName}");
                _terminal.ForegroundColor = _defaultColor;
                continue;
            }

            object? result = await tool.InvokeAsync(arguments);
            _terminal.WriteLine($"mcp:{mcp} tool result:{result}");
            Debug.WriteLine($"Tool call end: {functionName}({args})");
            _terminal.ForegroundColor = _defaultColor;

            conversation.Add(new ChatMessage(
                ChatRole.Tool,
                [new FunctionResultContent(toolCall.CallId, result)]));
        }
    }

    private enum CommandResult
    {
        Handled,
        SendPrompt,
        Quit,
    }
}
