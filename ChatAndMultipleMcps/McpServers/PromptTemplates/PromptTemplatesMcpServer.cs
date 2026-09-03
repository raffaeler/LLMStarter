using System;
using ModelContextProtocol.Server;

namespace ChatAndMultipleMcps.McpServers.PromptTemplates;

internal static class PromptTemplatesMcpServer
{
    public static IEnumerable<McpServerPrompt> CreatePrompts()
    {
        foreach (var kvp in Prompts.PromptTemplates)
        {
            (string promptDescription, string promptText) = kvp.Value;
            yield return McpServerPrompt.Create(
                () => promptText,
                new()
                {
                    Name = kvp.Key,
                    Description = promptDescription
                });
        }
    }
}
