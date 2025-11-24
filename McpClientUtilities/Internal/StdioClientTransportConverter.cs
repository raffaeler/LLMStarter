using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using ModelContextProtocol.Client;

namespace McpClientUtilities.Internal;

internal class StdioClientTransportConverter : JsonConverter<StdioClientTransportOptions?>
{
    public override StdioClientTransportOptions? Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected StartObject, got {reader.TokenType}");
        }

        string? command = null;
        IList<string>? arguments = null;
        string? name = null;
        string? workingDirectory = null;
        Dictionary<string, string?>? environmentVariables = null;
        TimeSpan shutdownTimeout = TimeSpan.FromSeconds(5);

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                // End of object, break out
                break;
            }
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                var propertyName = reader.GetString();
                if (!reader.Read())
                {
                    throw new JsonException($"Unexpected end when reading property value for '{propertyName}'");
                }
                switch (propertyName)
                {
                    case "command":
                        command = reader.GetString();
                        break;

                    case "args":
                    case "arguments":
                        arguments = JsonSerializer.Deserialize<List<string>>(ref reader, options);
                        break;

                    case "name":
                        name = reader.GetString();
                        break;

                    case "workingDirectory":
                        workingDirectory = reader.GetString();
                        break;

                    case "environmentVariables":
                        environmentVariables = JsonSerializer.Deserialize<Dictionary<string, string?>>(ref reader, options);
                        break;

                    case "shutdownTimeout":
                        if (reader.TokenType == JsonTokenType.Number)
                        {
                            shutdownTimeout = TimeSpan.FromSeconds(reader.GetInt32());
                        }
                        else if (reader.TokenType == JsonTokenType.String)
                        {
                            var timeoutString = reader.GetString();
                            if (TimeSpan.TryParse(timeoutString, out var parsedTimeout))
                            {
                                shutdownTimeout = parsedTimeout;
                            }
                        }
                        break;
                    default:
                        // Skip unknown property
                        reader.Skip();
                        break;
                }
            }
        }

        if (command == null) return null;

        return new StdioClientTransportOptions()
        {
            Command = command,
            Arguments = arguments,
            Name = name,
            WorkingDirectory = workingDirectory,
            EnvironmentVariables = environmentVariables ?? new Dictionary<string, string?>(),
            ShutdownTimeout = shutdownTimeout
        };
    }

    public override void Write(Utf8JsonWriter writer, StdioClientTransportOptions? value, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}
