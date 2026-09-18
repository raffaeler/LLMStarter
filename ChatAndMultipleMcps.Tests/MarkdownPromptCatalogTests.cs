using ChatAndMultipleMcps.Prompts;

using Xunit;

namespace ChatAndMultipleMcps.Tests;

public sealed class MarkdownPromptCatalogTests
{
    [Fact]
    public void LoadsPromptMarkdownAndUsesFilenameAsName()
    {
        using TemporaryRepository repository = new();
        repository.WritePrompt("chem1.prompt.md", """
            ---
            description: chemical data for aspirin
            ---
            Give me the chemical data for aspirin,
            including the CAS number.
            """);

        MarkdownPromptCatalog catalog = new(repository.Path, ".prompts");

        PromptDescriptor prompt = Assert.Single(catalog.GetPrompts());
        Assert.Equal("chem1", prompt.Name);
        Assert.Equal("chemical data for aspirin", prompt.Description);
        Assert.Contains("including the CAS number", prompt.Text);
        Assert.True(catalog.TryGetPrompt("CHEM1", out PromptDescriptor found));
        Assert.Same(prompt, found);
    }

    [Fact]
    public void RejectsPromptWithoutDescription()
    {
        using TemporaryRepository repository = new();
        repository.WritePrompt("invalid.prompt.md", """
            ---
            author: test
            ---
            Prompt text.
            """);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new MarkdownPromptCatalog(repository.Path, ".prompts"));

        Assert.Contains("description", exception.Message);
        Assert.Contains("invalid.prompt.md", exception.Message);
    }

    [Fact]
    public void RejectsPathsOutsideRepository()
    {
        using TemporaryRepository repository = new();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            MarkdownPromptCatalog.ResolvePromptsDirectory(repository.Path, "../.prompts"));

        Assert.Contains("inside the repository", exception.Message);
    }

    private sealed class TemporaryRepository : IDisposable
    {
        public TemporaryRepository()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "LLMStarterPromptTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(System.IO.Path.Combine(Path, ".prompts"));
        }

        public string Path { get; }

        public void WritePrompt(string filename, string contents) => File.WriteAllText(
            System.IO.Path.Combine(Path, ".prompts", filename),
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
