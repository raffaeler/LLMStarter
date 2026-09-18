using ChatAndMultipleMcps.Declarative;
using ChatAndMultipleMcps.Prompts;

using ConsoleUtilities;
using Xunit;

namespace ChatAndMultipleMcps.Tests;

public sealed class ChatCommandMenuTests
{
    private readonly VerboseState _verboseState = new();
    private readonly HashSet<string> _selectedAgents = new(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Slash_OffersEveryCommand()
    {
        ChatCommandMenu menu = CreateMenu();

        IReadOnlyList<ConsoleCompletionItem> choices = Complete(menu, "/");

        Assert.Collection(
            choices,
            item => Assert.Equal("/system ", item.ReplacementText),
            item => Assert.Equal("/prompt ", item.ReplacementText),
            item => Assert.Equal("/agent ", item.ReplacementText),
            item => Assert.Equal("/verbose ", item.ReplacementText),
            item => Assert.Equal("/new", item.ReplacementText),
            item => Assert.Equal("/quit", item.ReplacementText));
    }

    [Fact]
    public void PromptCommand_OffersExistingPromptTemplates()
    {
        ChatCommandMenu menu = CreateMenu();

        IReadOnlyList<ConsoleCompletionItem> choices = Complete(menu, "/prompt ");

        Assert.Contains(choices, item => item.ReplacementText == "/prompt file");
        Assert.Contains(choices, item => item.ReplacementText == "/prompt summary");
        Assert.Contains(choices, item => item.ReplacementText == "/prompt elicit");
        Assert.All(choices, item => Assert.True(item.Submit));
    }

    [Fact]
    public void SystemCommand_OffersShowClearAndFreeTextEntry()
    {
        ChatCommandMenu menu = CreateMenu();

        IReadOnlyList<ConsoleCompletionItem> choices = Complete(menu, "/system ");

        Assert.Equal("/system", choices[0].ReplacementText);
        Assert.Equal("/system \"\"", choices[1].ReplacementText);
        Assert.True(choices[2].DismissAfterInsert);
    }

    [Fact]
    public void VerboseCommand_MarksCurrentValue()
    {
        ChatCommandMenu menu = CreateMenu();

        Assert.StartsWith("[X]", Complete(menu, "/verbose ")[0].DisplayText);

        _verboseState.Enabled = true;
        Assert.StartsWith("[X]", Complete(menu, "/verbose ")[1].DisplayText);
    }

    [Fact]
    public void AgentCommand_ExplainsThatNoAgentsAreConfigured()
    {
        ChatCommandMenu menu = CreateMenu();

        ConsoleCompletionItem choice = Assert.Single(Complete(menu, "/agent "));

        Assert.Contains("No declarative agents", choice.DisplayText);
    }

    [Fact]
    public void AgentCommand_OffersConfiguredAgentAndMarksSelection()
    {
        DeclarativeAgentDescriptor agent = new(
            "Chemistry",
            "agent_chemistry",
            "Answers chemistry questions",
            "Chemistry instructions",
            ["pubchem/*"],
            "chemistry.agent.md");
        ChatCommandMenu menu = new(
            _verboseState,
            CreatePromptCatalog(),
            new FixedCatalog(agent),
            _selectedAgents);

        ConsoleCompletionItem unselected = Assert.Single(Complete(menu, "/agent Chem"));
        Assert.StartsWith("[ ]", unselected.DisplayText);
        Assert.Equal("/agent Chemistry", unselected.ReplacementText);

        _selectedAgents.Add(agent.Name);
        ConsoleCompletionItem selected = Assert.Single(Complete(menu, "/agent Chem"));
        Assert.StartsWith("[X]", selected.DisplayText);
    }

    private ChatCommandMenu CreateMenu() => new(
        _verboseState,
        CreatePromptCatalog(),
        new EmptyCatalog(),
        _selectedAgents);

    private static IPromptCatalog CreatePromptCatalog() => new FixedPromptCatalog(
        new("file", "files available", "List files.", "file.prompt.md"),
        new("summary", "summarize a story", "Summarize it.", "summary.prompt.md"),
        new("elicit", "ask the user", "Ask the user.", "elicit.prompt.md"));

    private static IReadOnlyList<ConsoleCompletionItem> Complete(ChatCommandMenu menu, string text) =>
        menu.GetCompletions(new ConsoleCompletionRequest(text, text.Length));

    private sealed class EmptyCatalog : IDeclarativeAgentCatalog
    {
        public IReadOnlyList<DeclarativeAgentDescriptor> GetAgents() => [];
    }

    private sealed class FixedCatalog(params DeclarativeAgentDescriptor[] agents)
        : IDeclarativeAgentCatalog
    {
        public IReadOnlyList<DeclarativeAgentDescriptor> GetAgents() => agents;
    }

    private sealed class FixedPromptCatalog(params PromptDescriptor[] prompts) : IPromptCatalog
    {
        public IReadOnlyList<PromptDescriptor> GetPrompts() => prompts;

        public bool TryGetPrompt(string name, out PromptDescriptor prompt)
        {
            prompt = prompts.FirstOrDefault(candidate => string.Equals(
                candidate.Name,
                name,
                StringComparison.OrdinalIgnoreCase))!;
            return prompt is not null;
        }
    }
}
