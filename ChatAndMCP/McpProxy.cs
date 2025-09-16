using System;
using System.Collections.Generic;
using System.IO.Pipelines;
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
using ModelContextProtocol.Server;

namespace ChatAndMCP;


/// <summary>
/// Starts and maintain the communication with one MCP Server.
/// It only exposes the client side which is a proxy to the MCP server functionalities.
/// This implementation activates the MCP server in-process using a pair of Pipes.
/// </summary>
internal class McpProxy : IAsyncDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<McpProxy> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IMyMcpServer _myMcpServer;

    private readonly Pipe _serverToClientPipe;
    private readonly Pipe _clientToServerPipe;

    private bool _isDisposed = false;

    private IMcpServer? _server;

    public IMcpClient? Client { get; private set; }
    public Task McpServerTask { get; private set; } = Task.CompletedTask;
    public Task McpClientTask { get; private set; } = Task.CompletedTask;

    public McpProxy(
        ILoggerFactory loggerFactory,
        IServiceProvider serviceProvider,
        IMyMcpServer myMcpServer)
    {
        _loggerFactory = loggerFactory;
        _serviceProvider = serviceProvider;
        _myMcpServer = myMcpServer;
        _logger = _loggerFactory.CreateLogger<McpProxy>();

        _serverToClientPipe = new();
        _clientToServerPipe = new();

    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (Client != null)
            await Client.DisposeAsync();

        if (_server != null)
            await _server.DisposeAsync();
    }

    /// <summary>
    /// Runs both the server and the client side of the MCP Server.
    /// Only the client will be accessible from this class.
    /// </summary>
    /// <param name="loggingLevel">The verbosity of the MCP server</param>
    /// <returns>A task that should never been waited in the main thread.
    /// It can be run as a fire-and-forget.</returns>
    public Task Start(LoggingLevel loggingLevel)
    {
        // We cannot wait for the MCP Server Task because it never ends.
        // Anyway, we have its task exposed as property to monitor it.
        _ = StartMcpServer();
        return StartMcpClient(loggingLevel);
    }

    private Task StartMcpServer()
    {
        McpServerOptions mcpServerOptions = new McpServerOptions()
        {
            ServerInfo = _myMcpServer.ServerInfo,
            Capabilities = _myMcpServer.Capabilities,
        };

        StreamServerTransport transport = new(
            _clientToServerPipe.Reader.AsStream(),
            _serverToClientPipe.Writer.AsStream());

        _server = McpServerFactory.Create(transport, mcpServerOptions, _loggerFactory, _serviceProvider);

        McpServerTask = _server.RunAsync();
        return McpServerTask;
    }

    private async Task StartMcpClient(LoggingLevel loggingLevel)
    {
        StreamClientTransport transport = new StreamClientTransport(
                _clientToServerPipe.Writer.AsStream(),
                _serverToClientPipe.Reader.AsStream());

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
                //NotificationHandlers = ...,

                //Roots = new()
                //{
                //    ListChanged = true,
                //    RootsHandler = ...,
                //},

                Sampling = new()
                {
                    SamplingHandler = SamplingHandler,
                },

                Elicitation = new()
                {
                    ElicitationHandler = ElicitationHandlerQA,
                },

                //Experimental = ...,
            },

            //ProtocolVersion = "",
        };


        Client = await McpClientFactory.CreateAsync(transport, clientOptions, _loggerFactory);

        McpClientTask = Client.SetLoggingLevel(loggingLevel);
        await McpClientTask;
    }

    private async ValueTask<CreateMessageResult> SamplingHandler(
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


    private ValueTask<ElicitResult> ElicitationHandlerQA(
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

