using Microsoft.Extensions.Configuration;

using YamlDotNet.Serialization;

namespace ChatAndMultipleMcps;

internal sealed record DeclarativeAgentDescriptor(
    string Name,
    string FunctionName,
    string Description,
    string Instructions,
    IReadOnlyList<string> ToolSelectors,
    string SourcePath);

internal interface IDeclarativeAgentCatalog
{
    IReadOnlyList<DeclarativeAgentDescriptor> GetAgents();
}

/// <summary>
/// Loads standard .agent.md files. The YAML front matter describes the agent,
/// while the Markdown body is the system prompt used by its private chat loop.
/// </summary>
internal sealed class MarkdownDeclarativeAgentCatalog : IDeclarativeAgentCatalog
{
    internal const string DefaultDirectory = "./agents";

    private readonly IReadOnlyList<DeclarativeAgentDescriptor> _agents;

    public MarkdownDeclarativeAgentCatalog(IConfiguration configuration)
        : this(
            FindRepositoryRoot(Directory.GetCurrentDirectory(), AppContext.BaseDirectory),
            configuration["DeclarativeAgents:Directory"] ?? DefaultDirectory)
    {
    }

    internal MarkdownDeclarativeAgentCatalog(string repositoryRoot, string relativeDirectory)
    {
        string agentsDirectory = ResolveAgentsDirectory(repositoryRoot, relativeDirectory);
        if (!Directory.Exists(agentsDirectory))
        {
            throw new DirectoryNotFoundException(
                $"The declarative agents directory '{agentsDirectory}' does not exist.");
        }

        _agents = Directory
            .EnumerateFiles(agentsDirectory, "*.agent.md", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .Select(ParseAgent)
            .ToArray();

        EnsureUnique(_agents, static agent => agent.Name, "agent name");
        EnsureUnique(_agents, static agent => agent.FunctionName, "agent function name");
    }

    public IReadOnlyList<DeclarativeAgentDescriptor> GetAgents() => _agents;

    internal static string ResolveAgentsDirectory(string repositoryRoot, string relativeDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeDirectory);

        if (Path.IsPathRooted(relativeDirectory))
        {
            throw new InvalidOperationException(
                "DeclarativeAgents:Directory must be relative to the repository root.");
        }

        string fullRoot = Path.GetFullPath(repositoryRoot);
        string fullDirectory = Path.GetFullPath(Path.Combine(fullRoot, relativeDirectory));
        string rootPrefix = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!fullDirectory.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "DeclarativeAgents:Directory must resolve inside the repository root.");
        }

        return fullDirectory;
    }

    internal static string FindRepositoryRoot(params string[] startingPaths)
    {
        foreach (string startingPath in startingPaths.Where(static path => !string.IsNullOrWhiteSpace(path)))
        {
            DirectoryInfo? current = new(Path.GetFullPath(startingPath));
            while (current is not null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, ".git"))
                    || File.Exists(Path.Combine(current.FullName, "LLMStarter.slnx")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        throw new InvalidOperationException(
            "Could not locate the repository root containing .git or LLMStarter.slnx.");
    }

    private static DeclarativeAgentDescriptor ParseAgent(string path)
    {
        string text = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        string[] lines = text.Split('\n');
        if (lines.Length < 3 || lines[0].TrimStart('\uFEFF').Trim() != "---")
        {
            throw InvalidAgent(path, "the file must start with YAML front matter delimited by '---'");
        }

        int closingDelimiter = Array.FindIndex(lines, 1, static line => line.Trim() == "---");
        if (closingDelimiter < 0)
        {
            throw InvalidAgent(path, "the YAML front matter has no closing '---' delimiter");
        }

        string yaml = string.Join('\n', lines[1..closingDelimiter]);
        string instructions = string.Join('\n', lines[(closingDelimiter + 1)..]).Trim();
        if (string.IsNullOrWhiteSpace(instructions))
        {
            throw InvalidAgent(path, "the Markdown instruction body is empty");
        }

        Dictionary<object, object?> frontMatter;
        try
        {
            frontMatter = new DeserializerBuilder()
                .Build()
                .Deserialize<Dictionary<object, object?>>(yaml)
                ?? [];
        }
        catch (Exception exception)
        {
            throw InvalidAgent(path, $"the YAML front matter is invalid: {exception.Message}");
        }

        string? name = GetString(frontMatter, "name");
        name = string.IsNullOrWhiteSpace(name) ? GetDefaultName(path) : name.Trim();

        string? description = GetString(frontMatter, "description");
        if (string.IsNullOrWhiteSpace(description))
        {
            throw InvalidAgent(path, "the required 'description' field is missing or empty");
        }

        IReadOnlyList<string> selectors = GetToolSelectors(frontMatter, path);
        return new DeclarativeAgentDescriptor(
            name,
            CreateFunctionName(name),
            description.Trim(),
            instructions,
            selectors,
            path);
    }

    private static IReadOnlyList<string> GetToolSelectors(
        Dictionary<object, object?> frontMatter,
        string path)
    {
        object? value = GetValue(frontMatter, "tools");
        if (value is null)
        {
            return ["*"];
        }

        IEnumerable<string> values = value switch
        {
            string text => text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            IEnumerable<object> sequence => sequence.Select(item => item?.ToString() ?? string.Empty),
            _ => throw InvalidAgent(path, "'tools' must be a string or YAML sequence"),
        };

        string[] selectors = values
            .Select(static selector => selector.Trim())
            .Where(static selector => selector.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return selectors.Length == 0 ? ["*"] : selectors;
    }

    private static string? GetString(Dictionary<object, object?> values, string key) =>
        GetValue(values, key)?.ToString();

    private static object? GetValue(Dictionary<object, object?> values, string key) =>
        values.FirstOrDefault(pair => string.Equals(
            pair.Key?.ToString(), key, StringComparison.OrdinalIgnoreCase)).Value;

    private static string GetDefaultName(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        return name.EndsWith(".agent", StringComparison.OrdinalIgnoreCase)
            ? name[..^".agent".Length]
            : name;
    }

    private static string CreateFunctionName(string name)
    {
        Span<char> buffer = stackalloc char[name.Length];
        int written = 0;
        bool previousWasSeparator = false;
        foreach (char character in name)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                buffer[written++] = char.ToLowerInvariant(character);
                previousWasSeparator = false;
            }
            else if (!previousWasSeparator && written > 0)
            {
                buffer[written++] = '_';
                previousWasSeparator = true;
            }
        }

        string slug = new string(buffer[..written]).Trim('_');
        if (slug.Length == 0)
        {
            throw new InvalidOperationException($"Agent name '{name}' cannot form a valid function name.");
        }

        return $"agent_{slug}";
    }

    private static void EnsureUnique(
        IEnumerable<DeclarativeAgentDescriptor> agents,
        Func<DeclarativeAgentDescriptor, string> selector,
        string label)
    {
        string? duplicate = agents
            .GroupBy(selector, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1)
            ?.Key;
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Duplicate declarative {label} '{duplicate}'.");
        }
    }

    private static InvalidOperationException InvalidAgent(string path, string problem) =>
        new($"Invalid declarative agent '{path}': {problem}.");
}
