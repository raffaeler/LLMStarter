using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ChatAndMultipleMcps.McpServers.Time;

internal class TimeMcpServer
{
    private readonly ILogger<TimeMcpServer> _logger;

    public TimeMcpServer(
        ILogger<TimeMcpServer> logger)
    {
        _logger = logger;

        Implementation serverInfo = new()
        {
            Name = "Time MCP Server",
            Title = "Provides the tools needed to provide current date/time information",
            Version = "1.0.0",
        };

        ServerCapabilities capabilities = new() { /* ... */ };

        McpServerOptions = new()
        {
            ServerInfo = serverInfo,
            Capabilities = capabilities,
            ToolCollection = [McpServerTool.Create(GetTimeInfo)],
        };
    }

    public McpServerOptions McpServerOptions { get; }

    [McpServerTool(Name = "time_now")]
    [Description("""
        Use this tool obtain the current date, time and timezone.
        """)]
    [return: Description("The date, time and timezone information of the current user")]
    public async Task<string> GetTimeInfo(
        McpServer server)
    {
        var clientLogger = server
            .AsClientLoggerProvider()
            .CreateLogger(nameof(TimeMcpServer));

        clientLogger.LogInformation($"MCP {nameof(GetTimeInfo)}");

        _logger.LogInformation($"{nameof(GetTimeInfo)}");

        DateTimeOffset now = DateTimeOffset.Now;
        var dt = now.LocalDateTime;
        var timezone = now.Offset;
        return $"The current date is {dt.ToLongDateString()}. The current time is {dt.ToLongTimeString()}. The timezone is {timezone}";
    }

}

