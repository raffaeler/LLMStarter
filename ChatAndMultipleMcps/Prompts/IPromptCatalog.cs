namespace ChatAndMultipleMcps.Prompts;

internal interface IPromptCatalog
{
    IReadOnlyList<PromptDescriptor> GetPrompts();

    bool TryGetPrompt(string name, out PromptDescriptor prompt);
}
