using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using ModelContextProtocol.Protocol;

namespace ChatAndMCP;

internal class InMemoryMcpService : IAsyncDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<InMemoryMcpService> _logger;
    private readonly IEnumerable<IMyMcpServer> _mcpServers;
    private List<McpProxy> _mcpProxies = [];

    public InMemoryMcpService(
        ILoggerFactory loggerFactory,
        IServiceProvider serviceProvider,
        IEnumerable<IMyMcpServer> mcpServers)
    {
        _loggerFactory = loggerFactory;
        _serviceProvider = serviceProvider;
        _logger = _loggerFactory.CreateLogger<InMemoryMcpService>();
        _mcpServers = mcpServers;
    }

    public IReadOnlyCollection<McpProxy> McpProxies => _mcpProxies;

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
        foreach (var mcp in _mcpServers)
        {
            McpProxy proxy = new(_loggerFactory, _serviceProvider, mcp);
            _mcpProxies.Add(proxy);
            tasks.Add(proxy.Start(loggingLevel));
        }
        //=> Task.WhenAll(StartMcpServers(), StartMcpClient(loggingLevel));
        return Task.WhenAll(tasks);
    }
}
