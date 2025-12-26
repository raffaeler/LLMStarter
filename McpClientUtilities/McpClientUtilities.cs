using System.Diagnostics;
using System.Text.Json;

using McpClientUtilities.Internal;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic;

using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

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

    /// <summary>
    /// This comes from the ModelContextProtocol C# SDK
    /// I personally expect this to become an helper API exposed by the SDK
    /// </summary>
    public static (IList<ChatMessage> Messages, ChatOptions? Options) ToChatClientArguments(this CreateMessageRequestParams? requestParams)
    {
        ArgumentNullException.ThrowIfNull(requestParams);

        ChatOptions? options = null;

        if (requestParams.MaxTokens is int maxTokens)
        {
            (options ??= new()).MaxOutputTokens = maxTokens;
        }

        if (requestParams.Temperature is float temperature)
        {
            (options ??= new()).Temperature = temperature;
        }

        if (requestParams.StopSequences is { } stopSequences)
        {
            (options ??= new()).StopSequences = stopSequences.ToArray();
        }

        List<ChatMessage> messages =
            (from sm in requestParams.Messages
             let aiContent = sm.Content.Select(s => s.ToAIContent()).ToList()
             where aiContent is not null
             select new ChatMessage(
                 sm.Role == Role.Assistant ? ChatRole.Assistant : ChatRole.User,
                 aiContent))
            .ToList();

        return (messages, options);
    }

    /// <summary>
    /// This comes from the ModelContextProtocol C# SDK
    /// I personally expect this to become an helper API exposed by the SDK
    /// </summary>
    public static CreateMessageResult ToCreateMessageResult(this ChatResponse chatResponse)
    {
        ArgumentNullException.ThrowIfNull(chatResponse);

        // The ChatResponse can include multiple messages, of varying modalities, but CreateMessageResult supports
        // only either a single blob of text or a single image. Heuristically, we'll use an image if there is one
        // in any of the response messages, or we'll use all the text from them concatenated, otherwise.

        ChatMessage? lastMessage = chatResponse.Messages.LastOrDefault();
        IList<AIContent> contents = lastMessage?.Contents ?? [];

        //if (lastMessage is not null)
        //{
        //    foreach (var lmc in lastMessage.Contents)
        //    {
        //        if (lmc is DataContent dc && (dc.HasTopLevelMediaType("image") || dc.HasTopLevelMediaType("audio")))
        //        {
        //            content = dc.ToContent();
        //        }
        //    }
        //}

        return new()
        {
            Content = contents.Select(c => c.ToContentBlock()).ToList(),
            Model = chatResponse.ModelId ?? "unknown",
            Role = lastMessage?.Role == ChatRole.User ? Role.User : Role.Assistant,
            StopReason = chatResponse.FinishReason == ChatFinishReason.Length ? "maxTokens" : "endTurn",
        };
    }



}
