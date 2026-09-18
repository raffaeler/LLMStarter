using ChatAndMultipleMcps.Declarative;

using Microsoft.Extensions.Configuration;

using YamlDotNet.Serialization;

namespace ChatAndMultipleMcps.Prompts;

/// <summary>Loads saved prompts from repository-relative .prompt.md files.</summary>
internal sealed class MarkdownPromptCatalog : IPromptCatalog
{
    internal const string DefaultDirectory = "./.prompts";

    private readonly IReadOnlyList<PromptDescriptor> _prompts;
    private readonly IReadOnlyDictionary<string, PromptDescriptor> _promptsByName;

    public MarkdownPromptCatalog(IConfiguration configuration)
        : this(
            MarkdownDeclarativeAgentCatalog.FindRepositoryRoot(
                Directory.GetCurrentDirectory(),
                AppContext.BaseDirectory),
            configuration["Prompts:Directory"] ?? DefaultDirectory)
    {
    }

    internal MarkdownPromptCatalog(string repositoryRoot, string relativeDirectory)
    {
        string promptsDirectory = ResolvePromptsDirectory(repositoryRoot, relativeDirectory);
        if (!Directory.Exists(promptsDirectory))
        {
            throw new DirectoryNotFoundException(
                $"The prompts directory '{promptsDirectory}' does not exist.");
        }

        _prompts = Directory
            .EnumerateFiles(promptsDirectory, "*.prompt.md", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .Select(ParsePrompt)
            .ToArray();

        string? duplicate = _prompts
            .GroupBy(static prompt => prompt.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1)
            ?.Key;
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Duplicate prompt name '{duplicate}'.");
        }

        _promptsByName = _prompts.ToDictionary(
            static prompt => prompt.Name,
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<PromptDescriptor> GetPrompts() => _prompts;

    public bool TryGetPrompt(string name, out PromptDescriptor prompt) =>
        _promptsByName.TryGetValue(name, out prompt!);

    internal static string ResolvePromptsDirectory(string repositoryRoot, string relativeDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeDirectory);

        if (Path.IsPathRooted(relativeDirectory))
        {
            throw new InvalidOperationException(
                "Prompts:Directory must be relative to the repository root.");
        }

        string fullRoot = Path.GetFullPath(repositoryRoot);
        string fullDirectory = Path.GetFullPath(Path.Combine(fullRoot, relativeDirectory));
        string rootPrefix = fullRoot.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!fullDirectory.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Prompts:Directory must resolve inside the repository root.");
        }

        return fullDirectory;
    }

    private static PromptDescriptor ParsePrompt(string path)
    {
        string text = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        string[] lines = text.Split('\n');
        if (lines.Length < 3 || lines[0].TrimStart('\uFEFF').Trim() != "---")
        {
            throw InvalidPrompt(path, "the file must start with YAML front matter delimited by '---'");
        }

        int closingDelimiter = Array.FindIndex(lines, 1, static line => line.Trim() == "---");
        if (closingDelimiter < 0)
        {
            throw InvalidPrompt(path, "the YAML front matter has no closing '---' delimiter");
        }

        Dictionary<object, object?> frontMatter;
        try
        {
            string yaml = string.Join('\n', lines[1..closingDelimiter]);
            frontMatter = new DeserializerBuilder()
                .Build()
                .Deserialize<Dictionary<object, object?>>(yaml) ?? [];
        }
        catch (Exception exception)
        {
            throw InvalidPrompt(path, $"the YAML front matter is invalid: {exception.Message}");
        }

        string? description = frontMatter
            .FirstOrDefault(pair => string.Equals(
                pair.Key?.ToString(),
                "description",
                StringComparison.OrdinalIgnoreCase))
            .Value?.ToString();
        if (string.IsNullOrWhiteSpace(description))
        {
            throw InvalidPrompt(path, "the required 'description' field is missing or empty");
        }

        string promptText = string.Join('\n', lines[(closingDelimiter + 1)..]).Trim();
        if (string.IsNullOrWhiteSpace(promptText))
        {
            throw InvalidPrompt(path, "the Markdown prompt body is empty");
        }

        string filename = Path.GetFileName(path);
        string name = filename[..^".prompt.md".Length];
        return new PromptDescriptor(name, description.Trim(), promptText, path);
    }

    private static InvalidOperationException InvalidPrompt(string path, string problem) =>
        new($"Invalid prompt '{path}': {problem}.");
}
