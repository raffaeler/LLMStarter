using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using ModelContextProtocol.Client;

namespace McpClientUtilities.Internal;

internal class HttpClientTransportJsonConverter : JsonConverter<HttpClientTransportOptions?>
{
    public override HttpClientTransportOptions? Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? endpoint = null;
        bool useStreamableHttp = false;
        string? name = null;
        TimeSpan connectionTimeout = TimeSpan.FromSeconds(30);
        Dictionary<string, string>? additionalHeaders = null;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                var propertyName = reader.GetString();
                reader.Read();
                switch (propertyName)
                {
                    case "url":
                    case "endpoint":
                        endpoint = reader.GetString();
                        break;
                    case "useStreamableHttp":
                        useStreamableHttp = reader.GetBoolean();
                        break;
                    case "name":
                        name = reader.GetString();
                        break;
                    case "connectionTimeout":
                        connectionTimeout = TimeSpan.FromSeconds(reader.GetInt32());
                        break;
                    case "additionalHeaders":
                        additionalHeaders = JsonSerializer.Deserialize<Dictionary<string, string>>(ref reader, options);
                        break;
                }
            }
        }

        if (endpoint == null ||
            !Uri.TryCreate(endpoint, UriKind.Absolute, out var _))
        {
            return null;
        }

        return new HttpClientTransportOptions()
        {
            Endpoint = new Uri(endpoint),
            TransportMode = useStreamableHttp
                ? HttpTransportMode.StreamableHttp
                : HttpTransportMode.AutoDetect,
            Name = name,
            ConnectionTimeout = connectionTimeout,
            AdditionalHeaders = additionalHeaders ?? new Dictionary<string, string>()
        };
    }
    public override void Write(Utf8JsonWriter writer, HttpClientTransportOptions? value, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}
