using ChatAndMultipleMcps.Prompts;

using ModelContextProtocol.Server;

namespace ChatAndMultipleMcps.McpServers.PromptTemplates;

internal static class PromptTemplatesMcpServer
{
    public static IEnumerable<McpServerPrompt> CreatePrompts(IPromptCatalog promptCatalog)
    {
        foreach (PromptDescriptor prompt in promptCatalog.GetPrompts())
        {
            yield return McpServerPrompt.Create(
                () => prompt.Text,
                new()
                {
                    Name = prompt.Name,
                    Description = prompt.Description
                });
        }
    }
}
