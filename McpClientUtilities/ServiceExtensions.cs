using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.IO.Pipes;
using System.Text;

using McpClientUtilities;
using McpClientUtilities.Internal;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceExtensions
{
    /// <summary>
    /// Configures the server builder to use stream-based transport
    /// over the specified client-to-server and server-to-client pipes.
    /// </summary>
    /// <param name="builder">The server builder to configure.</param>
    /// <param name="clientToServer">The pipe used to receive data sent from the client to the server.</param>
    /// <param name="serverToClient">The pipe used to send data from the server to the client.</param>
    /// <returns>The same server builder instance.</returns>
    public static IMcpServerBuilder WithPipesStreamServerTransport(this IMcpServerBuilder builder)
    {
        InProcessPipes inProcessPipes = new();
        builder.Services.AddSingleton(inProcessPipes);

        Stream serverInputStream = inProcessPipes.ClientToServer.Reader.AsStream();
        Stream serverOutputStream = inProcessPipes.ServerToClient.Writer.AsStream();

        builder.WithStreamServerTransport(serverInputStream, serverOutputStream);
        return builder;
    }

}
