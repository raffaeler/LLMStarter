using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace McpClientUtilities.Internal;

/// <summary>
/// This class represents the stream-based transport options
/// for in-process MCP Servers.
/// </summary>
internal class InProcClientTransportOptions
{
    public string Class { get; set; } = string.Empty;
}
