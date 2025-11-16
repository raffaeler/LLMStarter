using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using ModelContextProtocol.Protocol;

namespace ChatAndMCP.McpHelpers;

internal class McpProxyFactoryService : IAsyncDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<McpProxyFactoryService> _logger;
    private readonly IEnumerable<IMyMcpServer> _mcpServers;
    private readonly IEnumerable<ExternalStdioMcp> _externalStdioMcps;
    private List<IMcpProxy> _mcpProxies = [];

    public McpProxyFactoryService(
        ILoggerFactory loggerFactory,
        IServiceProvider serviceProvider,
        IEnumerable<IMyMcpServer> mcpServers,
        IEnumerable<ExternalStdioMcp> externalStdioMcps)
    {
        _loggerFactory = loggerFactory;
        _serviceProvider = serviceProvider;
        _logger = _loggerFactory.CreateLogger<McpProxyFactoryService>();
        _mcpServers = mcpServers;
        _externalStdioMcps = externalStdioMcps;
    }

    public IReadOnlyCollection<IMcpProxy> McpProxies => _mcpProxies;

    public async ValueTask DisposeAsync()
    {
        foreach (var proxy in _mcpProxies)
        {
            await proxy.DisposeAsync();
        }
    }

    public Task Start(LoggingLevel loggingLevel)
    {
        List<Task> tasks = new();

        // in-process mcp servers
        foreach (var mcp in _mcpServers)
        {
            McpProxyInProc proxy = new(_loggerFactory, _serviceProvider, mcp);
            _mcpProxies.Add(proxy);
            tasks.Add(proxy.Start(loggingLevel));
        }

        // external stdio mcp servers
        foreach (var extMcp in _externalStdioMcps)
        {
            if (!extMcp.IsEnabled) continue;

            McpProxyStdio proxy = new(_loggerFactory, _serviceProvider, extMcp);
            _mcpProxies.Add(proxy);
            tasks.Add(proxy.Start());
        }

        //=> Task.WhenAll(StartMcpServers(), StartMcpClient(loggingLevel));
        return Task.WhenAll(tasks);
    }
}
