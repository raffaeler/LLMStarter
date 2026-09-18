namespace ChatAndMultipleMcps.Declarative;

internal sealed record DeclarativeAgentDescriptor(
    string Name,
    string FunctionName,
    string Description,
    string Instructions,
    IReadOnlyList<string> ToolSelectors,
    string SourcePath);
