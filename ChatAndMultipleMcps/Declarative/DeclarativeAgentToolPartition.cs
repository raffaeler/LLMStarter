namespace ChatAndMultipleMcps.Declarative;

internal sealed record DeclarativeAgentToolPartition(
    IReadOnlyList<McpToolRegistration> DirectTools,
    IReadOnlyList<ResolvedDeclarativeAgent> Agents,
    string MainSystemPrompt);
