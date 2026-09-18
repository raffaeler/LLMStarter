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
    private readonly Dictionary<string, AIFunction> _directTools = [];
    private readonly Dictionary<string, AIFunction> _agentTools = [];
    private readonly Dictionary<string, string> _agentNamesByFunction = [];
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

        _terminal.WriteLine("Start chatting, or type / to choose a command.");

        _terminal.ForegroundColor = EvenColor;
        stopwatch.Restart();
        List<McpServerRegistration> servers = [];
        foreach (McpProxy proxy in _mcpClientFactoryService.Proxies)
        {
            if (proxy.McpClient is null)
            {
                continue;
            }

            List<McpToolRegistration> serverTools = [];
            if (proxy.McpClient.ServerCapabilities.Tools is not null)
            {
                IList<McpClientTool> clientTools = await proxy.McpClient.ListToolsAsync();
                foreach (McpClientTool tool in clientTools)
                {
                    serverTools.Add(new McpToolRegistration(
                        proxy.Name,
                        proxy.McpClient.ServerInfo.Name,
                        (AIFunction)tool));
                }

                _terminal.WriteLine($"Tools for MCP {proxy.McpClient.ServerInfo.Name}: {clientTools.Count}");
            }

            string mcpSystemPrompt = string.Empty;
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
                    mcpSystemPrompt = systemPromptBuilder.ToString().Trim();
                }
            }

            servers.Add(new McpServerRegistration(
                proxy.Name,
                proxy.McpClient.ServerInfo.Name,
                serverTools,
                mcpSystemPrompt));
        }

        DeclarativeAgentToolPartition partition = DeclarativeAgentToolResolver.Resolve(
            _agentCatalog.GetAgents(),
            servers);
        foreach (McpToolRegistration tool in partition.DirectTools)
        {
            _directTools.Add(tool.Function.Name, tool.Function);
            _toolsToMcp[tool.Function.Name] = tool.ServerDisplayName;
        }

        IChatClient agentClient = _serviceProvider.GetRequiredKeyedService<IChatClient>("main");
        foreach (ResolvedDeclarativeAgent agent in partition.Agents)
        {
            AIFunction function = new DeclarativeAgentRunner(
                agentClient,
                agent,
                _terminal,
                _verboseState).CreateFunction();
            _agentTools.Add(agent.Descriptor.Name, function);
            _agentNamesByFunction.Add(function.Name, agent.Descriptor.Name);
            _toolsToMcp[function.Name] = $"declarative-agent:{agent.Descriptor.Name}";
        }

        string systemPrompt = partition.MainSystemPrompt;

        mcpLoadElapsed = stopwatch.Elapsed;
        stopwatch.Stop();
        _terminal.WriteLine($"Loading tools from the MCPs took: {mcpLoadElapsed.TotalMilliseconds}ms");
        _terminal.ForegroundColor = _defaultColor;
        _terminal.WriteLine();

        if (_directTools.Keys.Any(tool => tool.Contains("browse", StringComparison.OrdinalIgnoreCase)))
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

        _terminal.WriteLine(
            $"Main chat tools: {_directTools.Count}; declarative agents: {_agentTools.Count}");
        await ChatLoop(systemPrompt, cancellationToken);
        _lifetime.StopApplication();
    }

    private async Task ChatLoop(
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
            bool isNewUserTurn = !lastWasTool;
            _terminal.ForegroundColor = _defaultColor;
            if (isNewUserTurn)
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
            bool mustDelegate = isNewUserTurn && selectedAgents.Count > 0;
            IReadOnlyDictionary<string, AIFunction> selectedAgentTools =
                GetSelectedAgentTools(selectedAgents);
            IReadOnlyDictionary<string, AIFunction> availableTools =
                GetAvailableTools(selectedAgents);
            string? requiredAgentFunction = mustDelegate && selectedAgentTools.Count == 1
                ? selectedAgentTools.Keys.Single()
                : null;
            ChatOptions options = CreateChatOptions(
                availableTools.Values,
                requireTool: mustDelegate,
                requiredFunctionName: requiredAgentFunction);
            List<ChatMessage> requestMessages = BuildRequestMessages(
                systemPrompt,
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
                await ProcessToolRequest(
                    streamingManager.ToolCalls,
                    conversation,
                    availableTools,
                    cancellationToken);
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
                else if (selectedAgents.Remove(agent.Name))
                {
                    _terminal.WriteLine($"Agent '{agent.Name}' disabled.");
                }
                else
                {
                    selectedAgents.Clear();
                    selectedAgents.Add(agent.Name);
                    _terminal.WriteLine(
                        $"Agent '{agent.Name}' selected for delegation. Select it again to disable it, "
                        + "or select another agent to switch.");
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

    private IReadOnlyDictionary<string, AIFunction> GetAvailableTools(
        IReadOnlySet<string> selectedAgents)
    {
        Dictionary<string, AIFunction> tools = new(
            _directTools,
            StringComparer.OrdinalIgnoreCase);
        foreach (string agentName in selectedAgents)
        {
            if (_agentTools.TryGetValue(agentName, out AIFunction? agentTool))
            {
                tools.Add(agentTool.Name, agentTool);
            }
        }

        return tools;
    }

    private IReadOnlyDictionary<string, AIFunction> GetSelectedAgentTools(
        IReadOnlySet<string> selectedAgents)
    {
        Dictionary<string, AIFunction> tools = new(StringComparer.OrdinalIgnoreCase);
        foreach (string agentName in selectedAgents)
        {
            if (_agentTools.TryGetValue(agentName, out AIFunction? agentTool))
            {
                tools.Add(agentTool.Name, agentTool);
            }
        }

        return tools;
    }

    internal static ChatOptions CreateChatOptions(
        IEnumerable<AIFunction> tools,
        bool requireTool = false,
        string? requiredFunctionName = null)
    {
        ChatOptions options = new()
        {
            MaxOutputTokens = 5500,
            FrequencyPenalty = 0,
            PresencePenalty = 0,
            Tools = tools.Cast<AITool>().ToList(),
        };
        if (options.Tools.Count == 0)
        {
            options.ToolMode = ChatToolMode.None;
        }
        else if (requireTool)
        {
            options.ToolMode = requiredFunctionName is not null
                ? ChatToolMode.RequireSpecific(requiredFunctionName)
                : ChatToolMode.RequireAny;
        }

        return options;
    }

    private static List<ChatMessage> BuildRequestMessages(
        string systemPrompt,
        IEnumerable<ChatMessage> conversation)
    {
        List<string> systemSections = [];
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            systemSections.Add(systemPrompt);
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
        IList<ChatMessage> conversation,
        IReadOnlyDictionary<string, AIFunction> availableTools,
        CancellationToken cancellationToken)
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
            bool isAgent = _agentNamesByFunction.TryGetValue(functionName, out string? agentName);
            _terminal.ForegroundColor = isAgent ? ConsoleColor.DarkCyan : InternalColor;
            _terminal.WriteLine(isAgent
                ? $"[main] -> [agent:{agentName}] ({args})"
                : $"[main] -> [mcp:{mcp}] -> [tool:{functionName}] ({args})");

            if (!availableTools.TryGetValue(functionName, out AIFunction? tool))
            {
                _terminal.WriteLine($"Unknown function {functionName}");
                _terminal.ForegroundColor = _defaultColor;
                continue;
            }

            object? result;
            try
            {
                result = await tool.InvokeAsync(arguments, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                result = $"Tool '{functionName}' failed: {exception.Message}";
                if (_verboseState.Enabled)
                {
                    _terminal.WriteLine(exception.ToString());
                }
            }
            _terminal.WriteLine(isAgent
                ? $"[agent:{agentName}] -> [main] result: {result}"
                : $"[tool:{functionName}] -> [mcp:{mcp}] -> [main] result: {result}");
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
