using ConsoleUtilities;

namespace ChatAndMultipleMcps;

internal sealed class ChatCommandMenu
{
    private static readonly CommandDefinition[] Commands =
    [
        new("system", "Show, set, or clear the system prompt", true),
        new("prompt", "Use a saved prompt", true),
        new("agent", "Enable a declarative agent as a tool", true),
        new("verbose", "Turn verbose logging on or off", true),
        new("new", "Start a new chat", false),
        new("quit", "Exit the process", false),
    ];

    private readonly VerboseState _verboseState;
    private readonly IDeclarativeAgentCatalog _agentCatalog;
    private readonly IReadOnlySet<string> _selectedAgents;

    public ChatCommandMenu(
        VerboseState verboseState,
        IDeclarativeAgentCatalog agentCatalog,
        IReadOnlySet<string> selectedAgents)
    {
        _verboseState = verboseState;
        _agentCatalog = agentCatalog;
        _selectedAgents = selectedAgents;
    }

    public IReadOnlyList<ConsoleCompletionItem> GetCompletions(ConsoleCompletionRequest request)
    {
        string text = request.Text;
        if (!text.StartsWith('/'))
        {
            return [];
        }

        int separator = text.IndexOf(' ');
        if (separator < 0)
        {
            string commandPrefix = text[1..];
            return Commands
                .Where(command => command.Name.StartsWith(commandPrefix, StringComparison.OrdinalIgnoreCase))
                .Select(command => new ConsoleCompletionItem(
                    $"/{command.Name,-8} {command.Description}",
                    $"/{command.Name}" + (command.HasArguments ? " " : string.Empty),
                    Submit: !command.HasArguments))
                .ToArray();
        }

        string commandName = text[1..separator];
        string argument = text[(separator + 1)..];
        return commandName.ToLowerInvariant() switch
        {
            "system" => GetSystemChoices(argument),
            "prompt" => GetPromptChoices(argument),
            "agent" => GetAgentChoices(argument),
            "verbose" => GetVerboseChoices(argument),
            _ => [],
        };
    }

    private static IReadOnlyList<ConsoleCompletionItem> GetSystemChoices(string argument)
    {
        if (argument.Length > 0)
        {
            return [];
        }

        return
        [
            new("Show current system prompt", "/system", Submit: true),
            new("[ ] Clear system prompt", "/system \"\"", Submit: true),
            new("Set a new system prompt...", "/system ", DismissAfterInsert: true),
        ];
    }

    private static IReadOnlyList<ConsoleCompletionItem> GetPromptChoices(string argument)
    {
        return Prompts.PromptTemplates
            .Where(prompt => prompt.Key.StartsWith(argument, StringComparison.OrdinalIgnoreCase))
            .Select(prompt => new ConsoleCompletionItem(
                $"{prompt.Key,-12} {prompt.Value.Item1}",
                $"/prompt {prompt.Key}",
                Submit: true))
            .ToArray();
    }

    private IReadOnlyList<ConsoleCompletionItem> GetVerboseChoices(string argument)
    {
        return new[] { false, true }
            .Select(enabled => new
            {
                Enabled = enabled,
                Name = enabled ? "on" : "off",
            })
            .Where(choice => choice.Name.StartsWith(argument, StringComparison.OrdinalIgnoreCase))
            .Select(choice => new ConsoleCompletionItem(
                $"[{(_verboseState.Enabled == choice.Enabled ? 'X' : ' ')}] {choice.Name}",
                $"/verbose {choice.Name}",
                Submit: true))
            .ToArray();
    }

    private IReadOnlyList<ConsoleCompletionItem> GetAgentChoices(string argument)
    {
        IReadOnlyList<DeclarativeAgentDescriptor> agents = _agentCatalog.GetAgents();
        if (agents.Count == 0)
        {
            return
            [
                new("No declarative agents configured", "/agent ", DismissAfterInsert: true),
            ];
        }

        return agents
            .Where(agent => agent.Name.StartsWith(argument, StringComparison.OrdinalIgnoreCase))
            .Select(agent => new ConsoleCompletionItem(
                $"[{(_selectedAgents.Contains(agent.Name) ? 'X' : ' ')}] {agent.Name,-12} {agent.Description}",
                $"/agent {agent.Name}",
                Submit: true))
            .ToArray();
    }

    private sealed record CommandDefinition(string Name, string Description, bool HasArguments);
}
