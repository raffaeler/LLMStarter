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

internal class SummaryMcpServer
{
    private readonly ILogger<SummaryMcpServer> _logger;

    public SummaryMcpServer(ILogger<SummaryMcpServer> logger)
    {
        _logger = logger;

        Implementation serverInfo = new()
        {
            Name = "Summary MCP Server",
            Title = "Use a sampling client to make a summary of the given resource",
            Version = "1.0.0",
        };

        ServerCapabilities capabilities = new()
        {
            Tools = new() { ListChanged = false },
            Prompts = new() { ListChanged = false },
        };

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
    #pragma warning disable MCP9005 // The SDK routes sampling through MRTR for this tool invocation.
    public async Task<IEnumerable<string>> CreateSummary(
        McpServer server,
        [Description("Describe the desired summary style.")]
        string style,
        [Description("Specifies the length of the resulting document.")]
        string length,
        [Description("The document to sum up.")]
        string document,
        CancellationToken cancellationToken)
    {
        var documentPreview = document[..Math.Min(document.Length, 10)];
        _logger.LogInformation($"{nameof(CreateSummary)}: style={style}, length={length}, document={documentPreview}...");

        CreateMessageResult samplingResult = await server.SampleAsync(
            new CreateMessageRequestParams()
            {
                SystemPrompt = SystemPrompt,
                Messages =
                [
                    new SamplingMessage()
                    {
                        Role = Role.User,
                        Content =
                        [
                            new TextContentBlock()
                            {
                                Text = GetUserPrompt(style, length, document),
                            },
                        ],
                    },
                ],
                MaxTokens = 300,
                IncludeContext = ContextInclusion.ThisServer,
            },
            cancellationToken);

        var textContents = samplingResult.Content
            .OfType<TextContentBlock>()
            .Select(t => t.Text)
            .ToArray();

        return textContents.Length > 0
            ? textContents
            : ["The generated content is not textual"];
    }
    #pragma warning restore MCP9005

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
