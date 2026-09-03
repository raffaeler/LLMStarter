using System;
using System.Collections.Generic;
using System.Text.Json;

using Microsoft.Extensions.AI;

using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace ChatAndMultipleMcps;

/// <summary>
/// This is the instance maintaining the communications
/// between the app and all the MCP clients.
/// </summary>
internal class McpClientApp
{
    private readonly IChatClient _samplingClient;
    private static ConsoleColor _defaultColor = Console.ForegroundColor;
    private static ConsoleColor _internalColor = ConsoleColor.DarkGray;
    private static ConsoleColor _elicitColor = ConsoleColor.DarkYellow;

    public McpClientApp(IChatClient samplingClient)
    {
        _samplingClient = samplingClient;
    }

#pragma warning disable MCP9005 // MRTR sampling and roots use the SDK's deprecated protocol types.
    public ValueTask<McpClientOptions> GetMcpClientOptions()
    {
        McpClientOptions clientOptions = new()
        {
            InitializationTimeout = TimeSpan.FromSeconds(30),

            ClientInfo = new Implementation()
            {
                Name = "Raf MCP Client",
                Version = "1.0.0",
            },

            Handlers = new McpClientHandlers()
            {
                RootsHandler = RootsHandler,
                SamplingHandler = _samplingClient.CreateSamplingHandler(),
                ElicitationHandler = ElicitationHandlerQA,
            },
        };
        return ValueTask.FromResult(clientOptions);
    }
#pragma warning restore MCP9005

#pragma warning disable MCP9005 // MRTR roots use the SDK's deprecated protocol types.
    public ValueTask<ListRootsResult> RootsHandler(
        ListRootsRequestParams? listRootsRequestParams,
        CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(new ListRootsResult()
        {
            Roots =
            [
                new Root()
                {
                    Uri = "my://url",
                    Name = "some-name",
                }
            ],
        });
    }

#pragma warning restore MCP9005

    public ValueTask<ElicitResult> ElicitationHandlerQA(
        ElicitRequestParams? elicitRequestParams,
        CancellationToken cancellationToken)
    {
        Console.ForegroundColor = _internalColor;
        Console.WriteLine($"[ElicitationHandlerQA invoked]");
        if (elicitRequestParams == null)
        {
            Console.ForegroundColor = _defaultColor;
            throw new McpException("ElicitationHandlerQA: elicitRequestParams is null");
        }

        Console.ForegroundColor = _elicitColor;
        Console.WriteLine($"Elicitation Request: {elicitRequestParams.Message}");
        Console.WriteLine("Type your answer:");
        var answerText = Console.ReadLine();


        ElicitResult result = new()
        {
            Action = "accept",
            Content = new Dictionary<string, JsonElement>()
            {
                ["answer"] = (JsonElement)JsonSerializer.Deserialize($"""
                    "{answerText}"
                    """, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonElement)))!,
            },
        };

        Console.ForegroundColor = _defaultColor;
        return ValueTask.FromResult(result);
    }

}
