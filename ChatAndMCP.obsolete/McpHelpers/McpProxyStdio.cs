using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace ChatAndMCP.McpHelpers;

internal class McpProxyStdio : McpProxyBase, IMcpProxy
{
    private readonly ILogger<McpProxyInProc> _logger;
    private readonly ExternalStdioMcp _externalStdioMcp;

    public McpProxyStdio(
        ILoggerFactory loggerFactory,
        IServiceProvider serviceProvider,
        ExternalStdioMcp externalStdioMcp) : base(loggerFactory, serviceProvider)
    {
        _logger = _loggerFactory.CreateLogger<McpProxyInProc>();
        _externalStdioMcp = externalStdioMcp;
    }

    public async Task Start()
    {
        StdioClientTransportOptions options = new()
        {
            Name = _externalStdioMcp.Name,
            Command = _externalStdioMcp.Command,
            Arguments = [.. _externalStdioMcp.Arguments],
        };

        StdioClientTransport transport = new(options, _loggerFactory);

        Dictionary<string, Func<JsonRpcNotification, CancellationToken, ValueTask>> notificationHandlers = new();

        notificationHandlers[NotificationMethods.LoggingMessageNotification] =
            LoggingNotificationsHandler;

        McpClientOptions clientOptions = new()
        {
            Capabilities = new ClientCapabilities()
            {

                //Roots = new()
                //{
                //    ListChanged = true,
                //},

                Sampling = new()
                {
                },

                //Experimental = ...,
            },

            Handlers = new McpClientHandlers()
            {
                NotificationHandlers = notificationHandlers,
                SamplingHandler = SamplingHandler,
                //ElicitationHandler = ElicitationHandlerQA,

            },
        };

        try
        {
            Client = await McpClient.CreateAsync(
                transport, clientOptions, _loggerFactory, default);
        }
        catch (Exception err)
        {
            _logger.LogError(err, "Error creating MCP client for {McpName}",
                _externalStdioMcp.Name);
            throw;
        }
    }
}
