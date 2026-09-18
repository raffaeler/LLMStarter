using System.ComponentModel;

using ConsoleUtilities;
using Microsoft.Extensions.AI;

namespace ChatAndMultipleMcps;

internal sealed record McpToolRegistration(
    string ServerName,
    string ServerDisplayName,
    AIFunction Function);

internal sealed record McpServerRegistration(
    string Name,
    string DisplayName,
    IReadOnlyList<McpToolRegistration> Tools,
    string SystemPrompt);

internal sealed record ResolvedDeclarativeAgent(
    DeclarativeAgentDescriptor Descriptor,
    IReadOnlyList<McpToolRegistration> Tools,
    string McpSystemPrompt);

internal sealed record DeclarativeAgentToolPartition(
    IReadOnlyList<McpToolRegistration> DirectTools,
    IReadOnlyList<ResolvedDeclarativeAgent> Agents,
    string MainSystemPrompt);

/// <summary>
/// Resolves the tool names declared in Markdown before any chat starts. This is
/// the boundary that keeps agent-owned MCP schemas out of the main chat.
/// </summary>
internal static class DeclarativeAgentToolResolver
{
    public static DeclarativeAgentToolPartition Resolve(
        IReadOnlyList<DeclarativeAgentDescriptor> agents,
        IReadOnlyList<McpServerRegistration> servers)
    {
        List<ResolvedDeclarativeAgent> resolvedAgents = [];
        HashSet<McpToolRegistration> assignedTools = [];

        foreach (DeclarativeAgentDescriptor agent in agents)
        {
            List<McpToolRegistration> tools = [];
            foreach (string selector in agent.ToolSelectors)
            {
                McpToolRegistration[] matches = servers
                    .SelectMany(static server => server.Tools)
                    .Where(tool => Matches(selector, tool))
                    .ToArray();
                if (matches.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"Agent '{agent.Name}' in '{agent.SourcePath}' references unavailable tool selector '{selector}'.");
                }

                tools.AddRange(matches);
            }

            McpToolRegistration[] distinctTools = tools.Distinct().ToArray();
            EnsureUniqueFunctionNames(distinctTools, $"agent '{agent.Name}'");
            assignedTools.UnionWith(distinctTools);

            HashSet<string> serverNames = distinctTools
                .Select(static tool => tool.ServerName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            string mcpPrompt = JoinPrompts(servers
                .Where(server => serverNames.Contains(server.Name))
                .Select(static server => server.SystemPrompt));
            resolvedAgents.Add(new(agent, distinctTools, mcpPrompt));
        }

        McpToolRegistration[] directTools = servers
            .SelectMany(static server => server.Tools)
            .Where(tool => !assignedTools.Contains(tool))
            .ToArray();
        EnsureUniqueFunctionNames(directTools, "the main chat");

        HashSet<string> directServerNames = directTools
            .Select(static tool => tool.ServerName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string mainPrompt = JoinPrompts(servers
            .Where(server => server.Tools.Count == 0 || directServerNames.Contains(server.Name))
            .Select(static server => server.SystemPrompt));

        HashSet<string> mainNames = directTools
            .Select(static tool => tool.Function.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (ResolvedDeclarativeAgent agent in resolvedAgents)
        {
            if (!mainNames.Add(agent.Descriptor.FunctionName))
            {
                throw new InvalidOperationException(
                    $"Agent function '{agent.Descriptor.FunctionName}' conflicts with another main-chat tool.");
            }
        }

        return new(directTools, resolvedAgents, mainPrompt);
    }

    private static bool Matches(string selector, McpToolRegistration tool)
    {
        if (selector == "*")
        {
            return true;
        }

        int separator = selector.IndexOf('/');
        if (separator <= 0 || separator == selector.Length - 1)
        {
            return false;
        }

        string serverName = selector[..separator];
        string toolName = selector[(separator + 1)..];
        return string.Equals(serverName, tool.ServerName, StringComparison.OrdinalIgnoreCase)
            && (toolName == "*" || string.Equals(
                toolName, tool.Function.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static void EnsureUniqueFunctionNames(
        IEnumerable<McpToolRegistration> tools,
        string context)
    {
        string? duplicate = tools
            .GroupBy(static tool => tool.Function.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1)
            ?.Key;
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Tool name '{duplicate}' is provided more than once in {context}.");
        }
    }

    private static string JoinPrompts(IEnumerable<string> prompts) => string.Join(
        Environment.NewLine + Environment.NewLine,
        prompts.Where(static prompt => !string.IsNullOrWhiteSpace(prompt)));
}

/// <summary>
/// Runs one stateless declarative agent. The loop is deliberately explicit so
/// the sample shows exactly how an agent alternates model and tool calls.
/// </summary>
internal sealed class DeclarativeAgentRunner
{
    private const int MaximumModelTurns = 10;

    private readonly IChatClient _client;
    private readonly ResolvedDeclarativeAgent _agent;
    private readonly IConsoleTerminal _terminal;
    private readonly VerboseState _verboseState;
    private readonly IReadOnlyDictionary<string, McpToolRegistration> _tools;

    public DeclarativeAgentRunner(
        IChatClient client,
        ResolvedDeclarativeAgent agent,
        IConsoleTerminal terminal,
        VerboseState verboseState)
    {
        _client = client;
        _agent = agent;
        _terminal = terminal;
        _verboseState = verboseState;
        _tools = agent.Tools.ToDictionary(
            static tool => tool.Function.Name,
            StringComparer.OrdinalIgnoreCase);
    }

    public AIFunction CreateFunction() => AIFunctionFactory.Create(
        (Func<string, CancellationToken, Task<string>>)RunAsync,
        new AIFunctionFactoryOptions
        {
            Name = _agent.Descriptor.FunctionName,
            Description = _agent.Descriptor.Description,
        });

    private async Task<string> RunAsync(
        [Description("The complete task or question to delegate to this specialist agent.")] string request,
        CancellationToken cancellationToken)
    {
        string instructions = string.Join(
            Environment.NewLine + Environment.NewLine,
            new[] { _agent.Descriptor.Instructions, _agent.McpSystemPrompt }
                .Where(static prompt => !string.IsNullOrWhiteSpace(prompt)));
        List<ChatMessage> conversation =
        [
            new(ChatRole.System, instructions),
            new(ChatRole.User, request),
        ];

        ChatOptions options = new()
        {
            MaxOutputTokens = 5500,
            FrequencyPenalty = 0,
            PresencePenalty = 0,
            Tools = _agent.Tools.Select(static tool => (AITool)tool.Function).ToList(),
        };
        if (options.Tools.Count == 0)
        {
            options.ToolMode = ChatToolMode.None;
        }

        for (int turn = 1; turn <= MaximumModelTurns; turn++)
        {
            // A declarative agent with assigned tools must ground its answer in
            // at least one tool result. Later turns return to automatic mode so
            // the model can either call more tools or produce the final answer.
            options.ToolMode = turn == 1 && options.Tools.Count > 0
                ? ChatToolMode.RequireAny
                : ChatToolMode.Auto;
            ChatResponse response = await _client.GetResponseAsync(
                conversation,
                options,
                cancellationToken);
            conversation.AddRange(response.Messages);

            FunctionCallContent[] calls = response.Messages
                .SelectMany(static message => message.Contents)
                .OfType<FunctionCallContent>()
                .ToArray();
            if (calls.Length == 0)
            {
                if (!string.IsNullOrWhiteSpace(response.Text))
                {
                    return response.Text;
                }

                throw new InvalidOperationException(
                    $"Declarative agent '{_agent.Descriptor.Name}' returned no text or tool calls.");
            }

            foreach (FunctionCallContent call in calls)
            {
                object? result;
                if (!_tools.TryGetValue(call.Name, out McpToolRegistration? tool))
                {
                    result = $"Tool '{call.Name}' is not available to this agent.";
                }
                else
                {
                    _terminal.ForegroundColor = ConsoleColor.DarkCyan;
                    _terminal.WriteLine(
                        $"[agent:{_agent.Descriptor.Name}] -> [mcp:{tool.ServerName}] -> [tool:{call.Name}]");
                    _terminal.ForegroundColor = ConsoleColor.DarkGray;
                    try
                    {
                        result = await tool.Function.InvokeAsync(
                            new AIFunctionArguments(call.Arguments),
                            cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        result = $"Tool '{call.Name}' failed: {exception.Message}";
                        if (_verboseState.Enabled)
                        {
                            _terminal.WriteLine(exception.ToString());
                        }
                    }
                }

                conversation.Add(new ChatMessage(
                    ChatRole.Tool,
                    [new FunctionResultContent(call.CallId, result)]));
            }
        }

        throw new InvalidOperationException(
            $"Declarative agent '{_agent.Descriptor.Name}' exceeded {MaximumModelTurns} model turns.");
    }
}
