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

    public McpClientApp(ILogger logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
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
        return ValueTask.FromResult(roots);
    }

    public ValueTask LoggingNotificationsHandler(
        JsonRpcNotification notification,
        CancellationToken cancellationToken)
    {
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

        return ValueTask.CompletedTask;
    }

    public async ValueTask<CreateMessageResult> SamplingHandler(
            CreateMessageRequestParams? createMessageRequestParams,
            IProgress<ProgressNotificationValue> progress,
            CancellationToken cancellationToken)
    {
        Console.WriteLine($"[SamplingHandler invoked]");

        if (createMessageRequestParams == null)
        {
            return new CreateMessageResult()
            {
                Model = string.Empty,
                Role = Role.Assistant,
                Content = new TextContentBlock()
                {
                    Text = "The input prompts to the model are missing",
                },
                StopReason = "endTurn", // "endTurn" or "stopSequence" or "stopToken"
            };
        }

        IChatClient summarySamplingClient = _serviceProvider
            .GetRequiredKeyedService<IChatClient>("SummarySamplingClient");

        var (messages, chatOptions) = createMessageRequestParams
            .ToChatClientArguments();

        var response = await summarySamplingClient.GetResponseAsync(messages, chatOptions, default);
        if (response.Messages.Count != 1)
        {
            return new CreateMessageResult()
            {
                Model = string.Empty,
                Role = Role.Assistant,
                Content = new TextContentBlock()
                {
                    Text = "Invalid LLM response: message count != 1",
                },

                StopReason = "endTurn", // "endTurn" or "stopSequence" or "stopToken"
            };
        }

        var result = response.ToCreateMessageResult();

        return result;
    }



    public ValueTask<ElicitResult> ElicitationHandlerQA(
        ElicitRequestParams? elicitRequestParams,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"[ElicitationHandlerQA invoked]");
        if (elicitRequestParams == null)
        {
            throw new McpException("ElicitationHandlerQA: elicitRequestParams is null");
        }

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

        return ValueTask.FromResult(result);
    }

}
