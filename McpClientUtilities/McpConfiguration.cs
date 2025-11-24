using System;
using System.Collections.Generic;
using System.Text;

using McpClientUtilities.Internal;

using ModelContextProtocol.Client;

namespace McpClientUtilities;

/// <summary>
/// Represents the configuration for a Model Context Protocol (MCP) client.
/// Beyond the name of the MCP server, the properties are mutually exclusive,
/// and represent the deserialized information coming from the JSON file.
/// </summary>
public record class McpConfiguration
{
    public required string Name { get; init; }
    public required HttpClientTransportOptions? HttpClientTransportOptions { get; init; }
    public required StdioClientTransportOptions? StdioClientTransportOptions { get; init; }
    internal InProcClientTransportOptions? InProcClientTransportOptions { get; init; }

    public static McpConfiguration InProcess => new McpConfiguration
    {
        Name = "InProcessMcpServer",
        InProcClientTransportOptions = new(),
        HttpClientTransportOptions = null,
        StdioClientTransportOptions = null,
    };
}
