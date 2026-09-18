namespace ChatAndMultipleMcps;

internal sealed record McpServerRegistration(
    string Name,
    string DisplayName,
    IReadOnlyList<McpToolRegistration> Tools,
    string SystemPrompt);
