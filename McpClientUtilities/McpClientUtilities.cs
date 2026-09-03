using System.Text.Json;

using McpClientUtilities.Internal;

using Microsoft.Extensions.Logging;

using ModelContextProtocol.Client;

namespace McpClientUtilities;

public static class McpClientUtilities
{
    /// <summary>
    /// This method returns the configuration for all the
    /// MCP servers defined in the JSON files located in the specified folder.
    /// </summary>
    /// <param name="folder">The folder where the configuration files are located.</param>
    /// <returns>The list of all the configurations</returns>
    public static async Task<IList<McpConfiguration>> GetMcpConfigurations(
        ILogger? logger, string folder)
    {
        JsonSerializerOptions options = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,

            Converters =
            {
                new HttpClientTransportJsonConverter(),
                new StdioClientTransportConverter(),
            }
        };

        var fullFolderPath = Path.GetFullPath(folder);
        List<McpConfiguration> result = [];
        foreach (var file in Directory.EnumerateFiles(fullFolderPath, "*.json",
            SearchOption.TopDirectoryOnly))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                JsonElement serversElement;

                if (!root.TryGetProperty("servers", out serversElement) &&
                    !root.TryGetProperty("mcpServers", out serversElement))
                {
                    // skip file with unexpected root element
                    continue;
                }

                foreach (var serverElementProperty in serversElement.EnumerateObject())
                {
                    var name = serverElementProperty.Name;
                    var serverElement = serverElementProperty.Value;

                    HttpClientTransportOptions? httpOptions = null;
                    StdioClientTransportOptions? stdioOptions = null;
                    InProcClientTransportOptions? inProcOptions = null;
                    stdioOptions = ReadStdioSchema(logger, options, serverElement);
                    if (stdioOptions == null)
                    {
                        httpOptions = ReadHttpClientTransortSchema(logger, options, serverElement);
                        if (httpOptions == null)
                        {
                            inProcOptions = ReadInProcSchema(logger, options, serverElement);
                            if (inProcOptions == null)
                            {
                                logger?.LogWarning("No valid MCP schema found in file: {FileName}", file);
                                continue;
                            }
                        }
                    }

                    var descriptor = new McpConfiguration()
                    {
                        Name = name,
                        HttpClientTransportOptions = httpOptions,
                        StdioClientTransportOptions = stdioOptions,
                        InProcClientTransportOptions = inProcOptions,
                    };

                    result.Add(descriptor);
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error loading MCP from file: {FileName}", file);
                throw;
            }
        }

        return result;
    }

    private static HttpClientTransportOptions? ReadHttpClientTransortSchema(
        ILogger? logger,
        JsonSerializerOptions options,
        JsonElement serverElement)
    {
        if (serverElement.ValueKind != JsonValueKind.Object)
        {
            logger?.LogWarning("Expected an object for 'servers' or 'mcpServers', but got: {ValueKind}", serverElement.ValueKind);
            return default;
        }

        return serverElement.Deserialize<HttpClientTransportOptions?>(options);
    }

    private static StdioClientTransportOptions? ReadStdioSchema(
        ILogger? logger,
        JsonSerializerOptions options,
        JsonElement serverElement)
    {
        if (serverElement.ValueKind != JsonValueKind.Object)
        {
            logger?.LogWarning("Expected an object for 'servers' or 'mcpServers', but got: {ValueKind}", serverElement.ValueKind);
            return default;
        }

        return serverElement.Deserialize<StdioClientTransportOptions?>(options);
    }

    private static InProcClientTransportOptions? ReadInProcSchema(
        ILogger? logger,
        JsonSerializerOptions options,
        JsonElement serverElement)
    {
        if (serverElement.ValueKind != JsonValueKind.Object)
        {
            logger?.LogWarning("Expected an object for 'servers' or 'mcpServers', but got: {ValueKind}", serverElement.ValueKind);
            return default;
        }

        return serverElement.Deserialize<InProcClientTransportOptions?>(options);
    }

}
