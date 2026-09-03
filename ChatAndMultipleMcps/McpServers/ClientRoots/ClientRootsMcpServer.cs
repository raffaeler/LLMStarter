using System.ComponentModel;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ChatAndMultipleMcps.McpServers.ClientRoots;

internal class ClientRootsMcpServer
{
    [McpServerTool(Name = "clientRoots_getRoots")]
    [Description("Gets the MCP client's configured roots through a multi round-trip request.")]
    [return: Description("The configured client root names and URIs.")]
#pragma warning disable MCP9005 // The SDK routes root discovery through MRTR for this tool invocation.
    public async Task<IEnumerable<string>> GetRoots(
        McpServer server,
        CancellationToken cancellationToken)
    {
        var rootsResult = await server.RequestRootsAsync(new(), cancellationToken);
        var roots = rootsResult.Roots
            .Select(root => $"{root.Name}: {root.Uri}")
            .ToArray();

        return roots.Length > 0
            ? roots
            : ["The client has no configured roots."];
    }
#pragma warning restore MCP9005
}
