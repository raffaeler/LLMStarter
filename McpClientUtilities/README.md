# McpClientUtilities

This project contains utility classes and methods for the McpClient application. It provides common functionalities that can be reused across different parts of the application to enhance code maintainability and reduce redundancy.

## Features

### Configuration
The `ModelContextProtocol` Nuget package does not provide any way to manage the
configuration of the MCP servers configured in an application.
Similarly to how Claude Desktop, Visual Studio Code and ChatGPT Desktop applications do, this code enables to load the exact same JSON configuration files.

- `HttpClientTransportJsonConverter`: A custom coverter to load the configuration of an MCP Server using the HTTP Streaming transport.
- `StdioClientTransportConverter`: A custom coverter to load the configuration of an MCP Server using the Stdio transport.
- `InProcClientTransportOptions`: An internal class allowing to treat the in-process servers as they were external servers.
- `InProcessPipes`: A small service injected in the DI container to manage the Pipes used for in-process communication.
- `McpClientFactoryService` now renamed to `McpProxyFactoryService`: The service that is used to create the MCP clients wrapped by `McpProxy` instances. This class exposes a `StartAll` method that allow to return a different instance of `McpClientOptions` for each MCP Server. The MCP Servers are started in parallel to avoid delays when many servers are configured.
- `McpClientUtilities`: A static class exposing the methods to load the configuration from the JSON files.
- `McpConfiguration`: The class representing the configuration of the MCP servers.
- `McpProxy`: A wrapper around the `McpClient` that exposes the `McpClient` as well as the name of the server and an error string that is set if the server failed to start.
- `ServiceExtensions`: contains an extension method to register the `InProcessPipes` and call the `WithStreamServerTransport` with the proper streams.



### Typical configuration in an ASP.NET Core application

Please note that the `WithPipesStreamServerTransport` is defined in `ServiceExtensions` and automatically configures the Pipes used to communicate with the in-process MCP servers.

```json
builder.Services.AddSingleton<McpClientFactoryService>();

builder.Services
    .AddMcpServer()
    .WithPipesStreamServerTransport()
    .WithTools<LocalFilesMcpServer>()
    .WithTools<SummaryMcpServer>()
    .WithTools<AskUserMcpServer>()
    .WithTools<TimeMcpServer>();
```