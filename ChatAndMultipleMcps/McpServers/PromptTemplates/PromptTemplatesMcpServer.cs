using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

using Microsoft.Extensions.Logging;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ChatAndMultipleMcps.McpServers.PromptTemplates;

internal class PromptTemplatesMcpServer
{
    private readonly ILogger<PromptTemplatesMcpServer> _logger;

    public PromptTemplatesMcpServer(ILogger<PromptTemplatesMcpServer> logger)
    {
        _logger = logger;

        Implementation serverInfo = new()
        {
            Name = "PromptTemplates MCP Server",
            Title = "Provides the templates of the most common prompts",
            Version = "1.0.0",
        };

        ServerCapabilities capabilities = new()
        {
            Prompts = new() { ListChanged = false },
        };

        McpServerOptions = new()
        {
            ServerInfo = serverInfo,
            Capabilities = capabilities,
            ToolCollection = [],
            PromptCollection =
            [
                //McpServerPrompt.Create(Prompt1, new(){ })
            ],
        };

        foreach (var kvp in Prompts.PromptTemplates)
        {
            (string promptDescription, string promptText) = kvp.Value;
            //var promptMethod = this.GetType().GetMethod(kvp.Key, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            //if(promptMethod != null)
            {
                McpServerPrompt prompt = McpServerPrompt.Create(
                    //(Func<string>)Delegate.CreateDelegate(typeof(Func<string>), this, promptMethod),
                    //Prompt1,
                    () => promptText,
                    new()
                    {
                        Name = kvp.Key,
                        Description = promptDescription
                    });
                McpServerOptions.PromptCollection.Add(prompt);
            }
        }

        McpServerOptions.Handlers.ListPromptsHandler = ListPromptsHandler;
    }

    public McpServerOptions McpServerOptions { get; }

    private ValueTask<ListPromptsResult> ListPromptsHandler(
        RequestContext<ListPromptsRequestParams> requestContext,
        CancellationToken cancellationToken = default)
    {
        var result = new ListPromptsResult()
        {
            Prompts = [],
        };

        foreach (var kvp in Prompts.PromptTemplates)
        {
            string name = kvp.Key;
            (string promptDescription, string promptText) = kvp.Value;

            // Prepare the prompt
            McpServerPrompt prompt = McpServerPrompt.Create(
                () => promptText,
                new()
                {
                    Name = kvp.Key,
                    Description = promptDescription
                });

            // Prepare the metadata that otherwise should be
            // specified in the attributes
            result.Prompts.Add(new Prompt()
            {
                Name = name,
                Description = promptDescription,
                Arguments = [],
                Title = name,
                McpServerPrompt = prompt,
            });
        }

        return ValueTask.FromResult(result);
    }

    [McpServerPrompt(Name = "prompts_test")]
    [Description("prompt_description")]
    public string Prompt1()
    {
        return "Hello, world";
    }

}
