namespace ChatAndMultipleMcps.Declarative;

internal interface IDeclarativeAgentCatalog
{
    IReadOnlyList<DeclarativeAgentDescriptor> GetAgents();
}
