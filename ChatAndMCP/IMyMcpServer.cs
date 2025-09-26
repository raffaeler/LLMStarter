using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ModelContextProtocol.Server;

namespace ChatAndMCP;

/// <summary>
/// A convenient common interface implemented
/// by all the MCP server implementations.
/// </summary>
public interface IMyMcpServer
{
    McpServerOptions McpServerOptions { get; }
}
