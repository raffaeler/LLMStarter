using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ChatAndMCP;

public class McpProxyInProc : McpProxyBase, IMcpProxy, IAsyncDisposable
{
    private readonly ILogger<McpProxyInProc> _logger;
    private readonly IMyMcpServer _myMcpServer;

    private readonly Pipe _serverToClientPipe;
    private readonly Pipe _clientToServerPipe;

    private bool _isDisposed = false;

    private McpServer? _server;

    public Task McpServerTask { get; private set; } = Task.CompletedTask;
    public Task McpClientTask { get; private set; } = Task.CompletedTask;

    public McpProxyInProc(
        ILoggerFactory loggerFactory,
        IServiceProvider serviceProvider,
        IMyMcpServer myMcpServer) : base(loggerFactory, serviceProvider)
    {
        _logger = _loggerFactory.CreateLogger<McpProxyInProc>();
        _myMcpServer = myMcpServer;

        _serverToClientPipe = new();
        _clientToServerPipe = new();
    }

    public override async ValueTask DisposeAsync()
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
        McpServerOptions mcpServerOptions = _myMcpServer.McpServerOptions;

        StreamServerTransport transport = new(
            _clientToServerPipe.Reader.AsStream(),
            _serverToClientPipe.Writer.AsStream());

        _server = McpServer.Create(transport, mcpServerOptions, _loggerFactory, _serviceProvider);

        McpServerTask = _server.RunAsync();
        return McpServerTask;
    }

    private async Task StartMcpClient(LoggingLevel loggingLevel)
    {
        StreamClientTransport transport = new StreamClientTransport(
                _clientToServerPipe.Writer.AsStream(),
                _serverToClientPipe.Reader.AsStream());

        Dictionary<string, Func<JsonRpcNotification, CancellationToken, ValueTask>> notificationHandlers = new();

        notificationHandlers[NotificationMethods.LoggingMessageNotification] =
            LoggingNotificationsHandler;

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
                RootsHandler = RootsHandler,
                SamplingHandler = SamplingHandler,
                ElicitationHandler = ElicitationHandlerQA,
            },
        };


        Client = await McpClient.CreateAsync(transport, clientOptions, _loggerFactory);
        McpClientTask = Client.SetLoggingLevel(loggingLevel);

        //Client.RegisterNotificationHandler(NotificationMethods.LoggingMessageNotification,
        //    LoggingNotificationsHandler);

        await McpClientTask;
    }

}
