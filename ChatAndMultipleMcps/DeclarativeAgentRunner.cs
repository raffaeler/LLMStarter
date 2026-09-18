using System.ComponentModel;

using ChatAndMultipleMcps.Declarative;

using ConsoleUtilities;
using Microsoft.Extensions.AI;

namespace ChatAndMultipleMcps;

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
