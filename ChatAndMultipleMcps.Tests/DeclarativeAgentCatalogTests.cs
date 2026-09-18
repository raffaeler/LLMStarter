using Xunit;

namespace ChatAndMultipleMcps.Tests;

public sealed class DeclarativeAgentCatalogTests
{
    [Fact]
    public void LoadsStandardAgentMarkdown()
    {
        using TemporaryRepository repository = new();
        repository.WriteAgent("chemistry.agent.md", """
            ---
            description: Looks up chemical facts
            tools:
              - pubchem/*
              - time/time_now
            ---
            You are a chemistry expert.
            Use authoritative chemical data.
            """);

        MarkdownDeclarativeAgentCatalog catalog = new(repository.Path, "agents");

        DeclarativeAgentDescriptor agent = Assert.Single(catalog.GetAgents());
        Assert.Equal("chemistry", agent.Name);
        Assert.Equal("agent_chemistry", agent.FunctionName);
        Assert.Equal("Looks up chemical facts", agent.Description);
        Assert.Equal(["pubchem/*", "time/time_now"], agent.ToolSelectors);
        Assert.Contains("You are a chemistry expert.", agent.Instructions);
    }

    [Fact]
    public void SupportsNamedAgentAndCommaSeparatedTools()
    {
        using TemporaryRepository repository = new();
        repository.WriteAgent("ignored.agent.md", """
            ---
            name: Ingredient Specialist
            description: Identifies ingredients
            tools: pubchem/*, time/time_now
            ---
            Investigate the supplied ingredient.
            """);

        DeclarativeAgentDescriptor agent = Assert.Single(
            new MarkdownDeclarativeAgentCatalog(repository.Path, "agents").GetAgents());

        Assert.Equal("Ingredient Specialist", agent.Name);
        Assert.Equal("agent_ingredient_specialist", agent.FunctionName);
        Assert.Equal(["pubchem/*", "time/time_now"], agent.ToolSelectors);
    }

    [Fact]
    public void OmittingToolsSelectsAllTools()
    {
        using TemporaryRepository repository = new();
        repository.WriteAgent("general.agent.md", """
            ---
            description: General specialist
            ---
            Handle the delegated request.
            """);

        DeclarativeAgentDescriptor agent = Assert.Single(
            new MarkdownDeclarativeAgentCatalog(repository.Path, "agents").GetAgents());

        Assert.Equal(["*"], agent.ToolSelectors);
    }

    [Fact]
    public void RejectsPathsOutsideRepository()
    {
        using TemporaryRepository repository = new();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            MarkdownDeclarativeAgentCatalog.ResolveAgentsDirectory(repository.Path, "../agents"));

        Assert.Contains("inside the repository", exception.Message);
    }

    [Fact]
    public void RejectsAgentWithoutDescription()
    {
        using TemporaryRepository repository = new();
        repository.WriteAgent("invalid.agent.md", """
            ---
            tools: [pubchem/*]
            ---
            Instructions.
            """);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new MarkdownDeclarativeAgentCatalog(repository.Path, "agents"));

        Assert.Contains("description", exception.Message);
        Assert.Contains("invalid.agent.md", exception.Message);
    }

    private sealed class TemporaryRepository : IDisposable
    {
        public TemporaryRepository()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "LLMStarterTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(System.IO.Path.Combine(Path, "agents"));
        }

        public string Path { get; }

        public void WriteAgent(string filename, string contents) => File.WriteAllText(
            System.IO.Path.Combine(Path, "agents", filename),
            contents);

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
