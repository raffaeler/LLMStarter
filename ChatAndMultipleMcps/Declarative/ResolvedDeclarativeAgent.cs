namespace ChatAndMultipleMcps.Declarative;

internal sealed record ResolvedDeclarativeAgent(
    DeclarativeAgentDescriptor Descriptor,
    IReadOnlyList<McpToolRegistration> Tools,
    string McpSystemPrompt);
