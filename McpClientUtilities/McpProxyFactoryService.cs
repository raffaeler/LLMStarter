using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Text;

using McpClientUtilities.Internal;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpClientUtilities;

public class McpProxyFactoryService : IAsyncDisposable
{
    private readonly IServiceProvider? _serviceProvider;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly ILogger<McpProxyFactoryService>? _logger;
    private readonly string? _mcpConfigurationDirectory;

    private List<McpProxy> _proxies = [];

    public McpProxyFactoryService(
        IServiceProvider? serviceProvider,
        ILoggerFactory? loggerFactory,
        IConfiguration? configuration)
    {
        _serviceProvider = serviceProvider;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory?.CreateLogger<McpProxyFactoryService>();

        _mcpConfigurationDirectory = configuration?["mcpConfigurationDirectory"];
    }

    public IReadOnlyCollection<McpProxy> Proxies => _proxies;

    public async ValueTask DisposeAsync()
    {
        foreach (var proxy in _proxies)
        {
            await proxy.DisposeAsync();
        }

        _proxies.Clear();
    }

    /// <summary>
    /// Starts all MCP proxies using the provided configuration
    /// options and optional server input and output streams.
    /// </summary>
    /// <param name="mcpClientOptionsFunc">A function that asynchronously
    /// provides MCP client options for a given configuration.
    /// This function is invoked for each configuration to determine
    /// the options used when starting each proxy.</param>
    /// <param name="clientToServer">The Pipe used to send messages
    /// to the server.</param>
    /// <param name="serverToClient">The Pipe used to receive the messages
    /// from the server.</param>
    /// <returns>A task that represents the asynchronous operation
    /// of starting all proxies. The task completes when all proxies
    /// have been started.</returns>
    public async Task StartAll(
        Func<McpConfiguration, ValueTask<McpClientOptions>> mcpClientOptionsFunc)
    {
        if(_serviceProvider == null)
        {
            throw new InvalidOperationException("Service provider is not configured. You must either provide a ServiceProvider or call the overload taking the Streams");
        }

        var pipes = _serviceProvider.GetRequiredService<InProcessPipes>();
        var configurationDirectory = _mcpConfigurationDirectory
            ?? ".";

        var configurations = await McpClientUtilities.GetMcpConfigurations(
            _logger, configurationDirectory);

        // we always add the in-process configuration to create
        // one proxy (and MCP Client) for all the local functions
        configurations.Insert(0, McpConfiguration.InProcess);

        List<Task> tasks = new();
        foreach (var configuration in configurations)
        {
            var mcpClientOptions = await mcpClientOptionsFunc(configuration);
            McpProxy proxy = new(_loggerFactory);
            tasks.Add(proxy.Start(mcpClientOptions, configuration,
                pipes.ClientToServer, pipes.ServerToClient));
            _proxies.Add(proxy);
        }

        await Task.WhenAll(tasks);
    }


    /// <summary>
    /// Starts all MCP proxies using the provided configuration
    /// options and optional server input and output streams.
    /// </summary>
    /// <param name="mcpClientOptionsFunc">A function that asynchronously
    /// provides MCP client options for a given configuration.
    /// This function is invoked for each configuration to determine
    /// the options used when starting each proxy.</param>
    /// <param name="serverInput">An optional stream to use as the
    /// input for all started proxies</param>
    /// <param name="serverOutput">An optional stream to use as the
    /// output for all started proxies.</param>
    /// <returns>A task that represents the asynchronous operation
    /// of starting all proxies. The task completes when all proxies
    /// have been started.</returns>
    public async Task StartAll(
        Func<McpConfiguration, ValueTask<McpClientOptions>> mcpClientOptionsFunc,
        Stream? serverInput = null, Stream? serverOutput = null)
    {
        var configurationDirectory = _mcpConfigurationDirectory
            ?? ".";

        var configurations = await McpClientUtilities.GetMcpConfigurations(
            _logger, configurationDirectory);

        List<Task> tasks = new();
        foreach (var configuration in configurations)
        {
            var mcpClientOptions = await mcpClientOptionsFunc(configuration);
            McpProxy proxy = new(_loggerFactory);
            tasks.Add(proxy.Start(mcpClientOptions,
                configuration,
                serverInput, serverOutput));
        }

        await Task.WhenAll(tasks);
    }

}
