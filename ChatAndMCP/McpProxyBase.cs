using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace ChatAndMCP;

public abstract class McpProxyBase : IMcpProxy
{
    protected readonly ILoggerFactory _loggerFactory;
    protected readonly IServiceProvider _serviceProvider;
    private readonly ILogger<McpProxyBase> _logger;

    public McpProxyBase(ILoggerFactory loggerFactory,
        IServiceProvider serviceProvider)
    {
        _loggerFactory = loggerFactory;
        _serviceProvider = serviceProvider;
        _logger = _loggerFactory.CreateLogger<McpProxyBase>();
    }

    public IMcpClient? Client { get; protected set; }

    public virtual ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }


    protected ValueTask<ListRootsResult> RootsHandler(ListRootsRequestParams? listRootsRequestParams,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"[RootsHandler invoked]");
        var roots = new ListRootsResult()
        {
            Roots =
            [
                new Root()
                {
                    Uri = "my:/root1",
                    Name = "Root 1",
                }
            ],
        };
        return ValueTask.FromResult(roots);
    }

    protected ValueTask LoggingNotificationsHandler(
        JsonRpcNotification notification, CancellationToken cancellationToken)
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

    protected async ValueTask<CreateMessageResult> SamplingHandler(
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

        var (messages, chatOptions) = ToChatClientArguments(createMessageRequestParams);

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

        var result = ToCreateMessageResult(response);

        return result;
    }



    /// <summary>
    /// This comes from the ModelContextProtocol C# SDK
    /// I personally expect this to become an helper API exposed by the SDK
    /// </summary>
    internal static (IList<ChatMessage> Messages, ChatOptions? Options) ToChatClientArguments(
        CreateMessageRequestParams requestParams)
    {
        ArgumentNullException.ThrowIfNull(requestParams);

        ChatOptions? options = null;

        if (requestParams.MaxTokens is int maxTokens)
        {
            (options ??= new()).MaxOutputTokens = maxTokens;
        }

        if (requestParams.Temperature is float temperature)
        {
            (options ??= new()).Temperature = temperature;
        }

        if (requestParams.StopSequences is { } stopSequences)
        {
            (options ??= new()).StopSequences = stopSequences.ToArray();
        }

        List<ChatMessage> messages =
            (from sm in requestParams.Messages
             let aiContent = sm.Content.ToAIContent()
             where aiContent is not null
             select new ChatMessage(sm.Role == Role.Assistant ? ChatRole.Assistant : ChatRole.User, [aiContent]))
            .ToList();

        return (messages, options);
    }

    /// <summary>
    /// This comes from the ModelContextProtocol C# SDK
    /// I personally expect this to become an helper API exposed by the SDK
    /// </summary>
    internal static CreateMessageResult ToCreateMessageResult(ChatResponse chatResponse)
    {
        ArgumentNullException.ThrowIfNull(chatResponse);

        // The ChatResponse can include multiple messages, of varying modalities, but CreateMessageResult supports
        // only either a single blob of text or a single image. Heuristically, we'll use an image if there is one
        // in any of the response messages, or we'll use all the text from them concatenated, otherwise.

        ChatMessage? lastMessage = chatResponse.Messages.LastOrDefault();

        ContentBlock? content = null;
        //if (lastMessage is not null)
        //{
        //    foreach (var lmc in lastMessage.Contents)
        //    {
        //        if (lmc is DataContent dc && (dc.HasTopLevelMediaType("image") || dc.HasTopLevelMediaType("audio")))
        //        {
        //            content = dc.ToContent();
        //        }
        //    }
        //}

        return new()
        {
            Content = content ?? new TextContentBlock { Text = lastMessage?.Text ?? string.Empty },
            Model = chatResponse.ModelId ?? "unknown",
            Role = lastMessage?.Role == ChatRole.User ? Role.User : Role.Assistant,
            StopReason = chatResponse.FinishReason == ChatFinishReason.Length ? "maxTokens" : "endTurn",
        };
    }


    protected ValueTask<ElicitResult> ElicitationHandlerQA(
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
