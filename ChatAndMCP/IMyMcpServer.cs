using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ModelContextProtocol.Protocol;

namespace ChatAndMCP;

/// <summary>
/// A convenient common interface implemented
/// by all the MCP server implementations.
/// </summary>
public interface IMyMcpServer
{
    Implementation ServerInfo { get; }
    ServerCapabilities Capabilities { get; }
}
