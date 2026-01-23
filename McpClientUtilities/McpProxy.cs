using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Text;

using Microsoft.Extensions.Logging;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpClientUtilities;

public class McpProxy : IAsyncDisposable
{
    private readonly ILoggerFactory? _loggerFactory;
    private readonly ILogger<McpProxy>? _logger;

    public McpProxy(ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory?.CreateLogger<McpProxy>();
    }

    public string Name { get; private set; } = string.Empty;
    public string ErrorMessage { get; private set; } = string.Empty;
    public McpClient? McpClient { get; private set; }

    public ValueTask DisposeAsync()
    {
        if(McpClient == null)
        {
            return ValueTask.CompletedTask;
        }
        
        return McpClient.DisposeAsync();
    }

    public async Task<bool> Start(McpClientOptions mcpClientOptions,
        McpConfiguration mcpConfiguration, IClientTransport transport)
    {
        if (McpClient != null) return false;
        McpClient = await McpClient.CreateAsync(transport,
            mcpClientOptions, _loggerFactory, default);

        Name = mcpConfiguration.Name;
        ErrorMessage = string.Empty;
        return true;
    }

    public async Task<bool> Start(McpClientOptions mcpClientOptions,
        McpConfiguration mcpConfiguration,
        Stream? serverInput = null, Stream? serverOutput = null)
    {
        if (McpClient != null) return false;
     
        var transport = CreateTransport(mcpConfiguration, serverInput, serverOutput);
        if (transport == null)
        {
            return false;
        }

        McpClient = await McpClient.CreateAsync(transport,
            mcpClientOptions, _loggerFactory, default);
        
        Name = mcpConfiguration.Name;
        ErrorMessage = string.Empty;
        return true;
    }

    public async Task<bool> Start(McpClientOptions mcpClientOptions,
        McpConfiguration mcpConfiguration,
        Pipe? clientToServerPipe,
        Pipe? serverToClientPipe)
    {
        if (McpClient != null) return false;

        var transport = CreateTransport(mcpConfiguration, clientToServerPipe, serverToClientPipe);
        if (transport == null)
        {
            return false;
        }

        McpClient = await McpClient.CreateAsync(transport,
            mcpClientOptions, _loggerFactory, default);

        Name = mcpConfiguration.Name;
        ErrorMessage = string.Empty;
        return true;
    }

    private IClientTransport? CreateTransport(
        McpConfiguration mcpConfiguration,
        Pipe? clientToServerPipe = null,
        Pipe? serverToClientPipe = null)
    {
        Stream? serverInput = clientToServerPipe?.Writer.AsStream();
        Stream? serverOutput = serverToClientPipe?.Reader.AsStream();
        return CreateTransport(mcpConfiguration, serverInput, serverOutput);
    }


    /// <summary>
    /// Creates an appropriate client transport instance based
    /// on the specified MCP configuration.
    /// </summary>
    /// <param name="mcpConfiguration">The configuration object that specifies which client transport options to use. Must contain at least one valid
    /// transport option.</param>
    /// <param name="serverInput">An optional input stream to use for in-process client transport. Required if the in-process transport option is
    /// selected; otherwise, ignored.</param>
    /// <param name="serverOutput">An optional output stream to use for in-process client transport. Required if the in-process transport option is
    /// selected; otherwise, ignored.</param>
    /// <returns>An instance of a client transport corresponding to the selected transport option in the configuration, or null
    /// if no valid transport option is found.</returns>
    /// <exception cref="ArgumentNullException">Thrown if the in-process client transport option is selected and either serverInput or serverOutput is null.</exception>
    private IClientTransport? CreateTransport(
        McpConfiguration mcpConfiguration,
        Stream? serverInput = null,
        Stream? serverOutput = null)
    {
        IClientTransport? transport = null;
        if (mcpConfiguration.StdioClientTransportOptions != null)
        {
            transport = new StdioClientTransport(
                mcpConfiguration.StdioClientTransportOptions, _loggerFactory);
        }
        else if (mcpConfiguration.HttpClientTransportOptions != null)
        {
            transport = new HttpClientTransport(
                mcpConfiguration.HttpClientTransportOptions, _loggerFactory);
        }
        else if (mcpConfiguration.InProcClientTransportOptions != null)
        {
            if(serverInput == null || serverOutput == null)
            {
                throw new ArgumentNullException("serverInput and serverOutput must be provided for InProcClientTransport");
            }

            transport = new StreamClientTransport(
                serverInput,    //_clientToServerPipe.Writer.AsStream(),
                serverOutput,   //_serverToClientPipe.Reader.AsStream(),
                _loggerFactory);
        }
        else
        {
            ErrorMessage = $"No valid transport options found for MCP server: {mcpConfiguration.Name}";
            _logger?.LogWarning(ErrorMessage, mcpConfiguration.Name);
        }

        return transport;
    }


}
