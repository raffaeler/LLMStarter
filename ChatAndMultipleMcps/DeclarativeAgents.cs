namespace ChatAndMultipleMcps;

internal sealed record DeclarativeAgentDescriptor(
    string Name,
    string Description,
    string Instructions);

internal interface IDeclarativeAgentCatalog
{
    IReadOnlyList<DeclarativeAgentDescriptor> GetAgents();
}

/// <summary>
/// Placeholder catalog. Declarative agents can be registered here when available.
/// </summary>
internal sealed class EmptyDeclarativeAgentCatalog : IDeclarativeAgentCatalog
{
    public IReadOnlyList<DeclarativeAgentDescriptor> GetAgents() => [];
}
