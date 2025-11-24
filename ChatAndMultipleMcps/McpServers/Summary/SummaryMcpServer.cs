using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ChatAndMultipleMcps.McpServers.Summary;

internal class SummaryMcpServer// : IMyMcpServer
{
    private readonly ILogger<SummaryMcpServer> _logger;

    public SummaryMcpServer(
        ILogger<SummaryMcpServer> logger)
    {
        _logger = logger;

        Implementation serverInfo = new()
        {
            Name = "Summary MCP Server",
            Title = "Use a sampling client to make a summary of the given resource",
            Version = "1.0.0",
        };

        ServerCapabilities capabilities = new() { /* ... */ };

        McpServerOptions = new()
        {
            ServerInfo = serverInfo,
            Capabilities = capabilities,
            ToolCollection =
            [
                McpServerTool.Create(CreateSummary),
            ],
            PromptCollection =
            [
                McpServerPrompt.Create(GetSummaryPrompt)
            ],
        };
    }

    public McpServerOptions McpServerOptions { get; }

    [McpServerTool(Name = "summary_createSummary")]
    [Description("Creates a summary of the given text")]
    [return: Description("A summary generated of the provided text")]
    public async Task<IEnumerable<string>> CreateSummary(McpServer server,
        [Description("Describe the desired summary style.")]
        string style,
        [Description("Specifies the length of the resulting document.")]
        string length,
        [Description("The document to sum up.")]
        string document)
    {
        var clientLogger = server
            .AsClientLoggerProvider()
            .CreateLogger(nameof(SummaryMcpServer));

        clientLogger.LogInformation($"MCP {nameof(CreateSummary)}: style={style}, length={length}, document={document.Substring(0, 10)}...");

        _logger.LogInformation($"{nameof(CreateSummary)}: style={style}, length={length}, document={document.Substring(0, 10)}...");
        CreateMessageResult result = await server.SampleAsync(
            new CreateMessageRequestParams()
            {
                SystemPrompt = SystemPrompt,
                Messages =
            [
                new SamplingMessage()
                {
                    Role = Role.User,
                    Content =new TextContentBlock()
                    {
                        Text = GetUserPrompt(style, length, document)
                    },
                },
            ],
                MaxTokens = 300,
                Temperature = 0.7f,
                IncludeContext = ContextInclusion.ThisServer,
            }, CancellationToken.None);

        var textContent = result.Content as TextContentBlock;
        if (textContent == null)
        {
            return ["The generated content is not textual"];
        }

        return [textContent.Text];
    }

    /*
     This is an alternative implementation of the same tool

    [McpServerTool(Name = "summary_createSummary")]
    [Description("Creates a summary of the given text")]
    [return: Description("A summary generated of the provided text")]
    public async Task<IEnumerable<string>> CreateSummary2(IMcpServer mcpServer,
    [Description("Describe the desired summary style.")]
        string style,
    [Description("Specifies the length of the resulting document.")]
        string length,
    [Description("The document to sum up.")]
        string document)
    {
        _logger.LogInformation($"{nameof(CreateSummary)}: style={style}, length={length}, document={document.Substring(0, 10)}...");

        ChatOptions options = new()
        {
            MaxOutputTokens = 300,
            Temperature = 0.7f,
        };

        var messages = GetSummaryPrompt(style, length, document);

        IChatClient samplingClient = mcpServer.AsSamplingChatClient();
        var response = await samplingClient.GetResponseAsync(messages, options, default);
        var textContent = response?.Messages?.FirstOrDefault();

        if (textContent == null)
        {
            return ["The generated content is not textual"];
        }

        return [textContent.Text];
    }

    */

    private string SystemPrompt => """
        You are an assistant specialized in creating summaries.
        """;

    private string GetUserPrompt(string style, string length, string document)
        => $"""
            Create a summary of the following text having the following characteristics
            - Style: {style}
            - Length: {length}
            Document:
            {document}
         """;

    [McpServerPrompt(Name = "summary_prompt"),
        Description("A prompt used to request the creation of a summary")]
    public IEnumerable<ChatMessage> GetSummaryPrompt(string style, string length, string document)
    {
        return
        [
            new ChatMessage(ChatRole.System, SystemPrompt),
            new ChatMessage(ChatRole.User, GetUserPrompt(style, length, document)),
        ];
    }

}
