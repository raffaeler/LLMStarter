using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

using McpClientUtilities;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
    private readonly ILogger _logger;
    private readonly IServiceProvider _serviceProvider;
    private string _mcpName;
    private static ConsoleColor _defaultColor = Console.ForegroundColor;
    private static ConsoleColor _internalColor = ConsoleColor.DarkGray;
    private static ConsoleColor _elicitColor = ConsoleColor.DarkYellow;

    public McpClientApp(ILogger logger, IServiceProvider serviceProvider, string mcpName)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _mcpName = mcpName;
    }

    public ValueTask<McpClientOptions> GetMcpClientOptions(McpConfiguration configuration)
    {
        Dictionary<string, Func<JsonRpcNotification, CancellationToken, ValueTask>> notificationHandlers = new();

        notificationHandlers[NotificationMethods.LoggingMessageNotification] =
            LoggingNotificationsHandler;

        Func<ListRootsRequestParams?,
            CancellationToken,
            ValueTask<ListRootsResult>>? rootsHandler = null; // RootsHandler;

        McpClientOptions clientOptions = new()
        {
            InitializationTimeout = TimeSpan.FromSeconds(30),

            ClientInfo = new Implementation()
            {
                Name = "Raf MCP Client",
                Version = "1.0.0",
            },

            Capabilities = new ClientCapabilities()
            {

                Roots = new()
                {
                    ListChanged = true,
                },

                //Experimental = ...,
            },

            //ProtocolVersion = "",

            Handlers = new McpClientHandlers()
            {
                NotificationHandlers = notificationHandlers,
                RootsHandler = rootsHandler,
                SamplingHandler = SamplingHandler,
                ElicitationHandler = ElicitationHandlerQA,
            },
        };

        return ValueTask.FromResult(clientOptions);
    }

    public ValueTask<ListRootsResult> RootsHandler(
        ListRootsRequestParams? listRootsRequestParams,
        CancellationToken cancellationToken)
    {
        Console.ForegroundColor = _internalColor;
        Console.WriteLine($"[RootsHandler invoked]");
        var roots = new ListRootsResult()
        {
            Roots =
            [
                new Root()
                {
                    //Uri = "https://formula1.com",
                    //Uri = "https://www.gptoday.com/",
                    Uri = "my://url",
                    Name = "some-name",
                }
            ],
        };

        Console.ForegroundColor = _defaultColor;
        return ValueTask.FromResult(roots);
    }

    public ValueTask LoggingNotificationsHandler(
        JsonRpcNotification notification,
        CancellationToken cancellationToken)
    {
        Console.ForegroundColor = _internalColor;

        Console.WriteLine($"[Notification received: {notification.Method}]");
        if (JsonSerializer.Deserialize<LoggingMessageNotificationParams>(notification.Params) is { } ln)
        {
            //Console.WriteLine($"[{ln.Level}] {ln.Logger} {ln.Data}");
            string serverMessage = ln.Data?.ToString() ?? string.Empty;
            string message = $"From {ln.Logger}: {serverMessage}";

            if (ln.Level == LoggingLevel.Debug)
            {
                _logger.LogDebug(message);
            }
            else if (ln.Level == LoggingLevel.Info)
            {
                _logger.LogInformation(message);
            }
            else if (ln.Level <= LoggingLevel.Warning)
            {
                _logger.LogWarning(message);
            }
            else
            {
                _logger.LogError(message);
            }
        }
        else
        {
            Console.WriteLine($"Received unexpected logging notification: {notification.Params}");
        }

        Console.ForegroundColor = _defaultColor;
        return ValueTask.CompletedTask;
    }

    public async ValueTask<CreateMessageResult> SamplingHandler(
            CreateMessageRequestParams? createMessageRequestParams,
            IProgress<ProgressNotificationValue> progress,
            CancellationToken cancellationToken)
    {
        Console.ForegroundColor = _internalColor;
        Console.WriteLine($"MCP:{_mcpName}:[SamplingHandler invoked]");

        if (createMessageRequestParams == null)
        {
            Console.ForegroundColor = _defaultColor;

            return new CreateMessageResult()
            {
                Model = string.Empty,
                Role = Role.Assistant,
                Content = 
                [
                    new TextContentBlock()
                    {
                        Text = "The input prompts to the model are missing",
                    },
                ],
                StopReason = "endTurn", // "endTurn" or "stopSequence" or "stopToken"
            };
        }

        IChatClient summarySamplingClient = _serviceProvider
            .GetRequiredKeyedService<IChatClient>("SummarySamplingClient");
        var clientMetadata = summarySamplingClient.GetService<ChatClientMetadata>();
        string? modelName = clientMetadata?.DefaultModelId;

        Console.ForegroundColor = _internalColor;
        Console.WriteLine($"MCP:{_mcpName} Sampling request using model:{modelName}");

        var (messages, chatOptions) = createMessageRequestParams
            .ToChatClientArguments();

        var response = await summarySamplingClient.GetResponseAsync(messages, chatOptions, default);
        if (response.Messages.Count != 1)
        {
            Console.ForegroundColor = _defaultColor;

            return new CreateMessageResult()
            {
                Model = string.Empty,
                Role = Role.Assistant,
                Content = 
                [
                    new TextContentBlock()
                    {
                        Text = "Invalid LLM response: message count != 1",
                    },
                ],
                StopReason = "endTurn", // "endTurn" or "stopSequence" or "stopToken"
            };
        }

        CreateMessageResult result = response.ToCreateMessageResult();
        var length = result.Content.Sum(c =>
        {
            if (c is TextContentBlock tcb)
            {
                return tcb.Text.Length;
            }
            return 0;
        });

        Console.WriteLine($"MCP:{_mcpName} ResponseLength:{length}");
        Console.ForegroundColor = _defaultColor;
        return result;
    }



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
