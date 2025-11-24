using System;
using System.Diagnostics;
using System.Text.Json;

using Microsoft.Extensions.AI;

namespace ChatAndMCP.Helpers;

internal static class Diag
{
    public static async Task Dump(this IAsyncEnumerable<ChatResponseUpdate> response,
        JsonSerializerOptions? options = null)
    {
        var chatResponse = await response.ToChatResponseAsync();
        var messages = chatResponse.Messages;
        var json = JsonSerializer.Serialize(messages, options) +
            Environment.NewLine;
        await File.AppendAllTextAsync("log_sync.json", json);
    }

    public static async Task Dump(this ChatResponseUpdate update,
        JsonSerializerOptions? options = null)
    {
        var json = JsonSerializer.Serialize(update, options) +
            Environment.NewLine;
        await File.AppendAllTextAsync("log_async.json", json);

        var hasUserContent = update.Contents
            .Any(c => c is TextContent ||
                      c is TextReasoningContent ||
                      c is DataContent ||
                      c is UriContent ||
                      c is ErrorContent ||
                      c is UsageContent);

        if (hasUserContent)
        {
            Debug.WriteLine(json);
            Debug.WriteLine(string.Empty);
            Debug.WriteLine(string.Empty);
        }
    }

    public static void Dump2(this ChatResponseUpdate update,
        JsonSerializerOptions? options = null)
    {
#if DEBUG
        Debug.WriteLine($"Update: {update}");
        Debug.WriteLine($"  MessageId: {update.MessageId}");
        Debug.WriteLine($"  ResponseId: {update.ResponseId}");
        Debug.WriteLine($"  CreatedAt: {update.CreatedAt}");
        Debug.WriteLine($"  AuthorName: {update.AuthorName}");
        //Debug.WriteLine($"  ChatThreadId: {update.ChatThreadId}");
        Debug.WriteLine($"  ModelId: {update.ModelId}");
        Debug.WriteLine($"  Role: {update.Role}");
        Debug.WriteLine($"  FinishReason: {update.FinishReason}");
        Debug.WriteLine($"  Text: {update.Text}");
        Debug.WriteLine($"  Contents: {update.Contents.Count}");
        foreach (var content in update.Contents)
        {
            Debug.WriteLine($"    {content}");
            Debug.WriteLine($"      Type: {content.GetType()}");
            Debug.WriteLine($"      Properties: {content.AdditionalProperties?.Count}");
            if (content is TextContent textContent)
            {
                Debug.WriteLine($"        Text: {textContent.Text}");
            }
            else if (content is TextReasoningContent textReasoningContent)
            {
                Debug.WriteLine($"        Reasoning: {textReasoningContent.Text}");
            }
            else if (content is DataContent dataContent)
            {
                Debug.WriteLine($"        Image Data: {dataContent.Data.Length} bytes");
            }
            else if (content is UsageContent usageContent)
            {
                var usage = usageContent.Details;
                Debug.WriteLine($"        Usage: T = {usage.TotalTokenCount} = I({usage.InputTokenCount}) + O({usage.OutputTokenCount}) + A({usage.AdditionalCounts?.Select(a => a.Value).Sum() ?? 0})");
            }
            else if (content is FunctionCallContent functionCallContent)
            {
                var argsString = functionCallContent.Arguments == null
                    ? "(no arguments)"
                    : string.Join(", ", functionCallContent.Arguments
                        .Select(a => $"{a.Key}: {a.Value}"));

                Debug.WriteLine($"        CallId: {functionCallContent.CallId}");
                Debug.WriteLine($"        Function: {functionCallContent.Name}");
                Debug.WriteLine($"        Arguments: {argsString}");
            }
            else if (content is FunctionResultContent functionResultContent)
            {
                Debug.WriteLine($"        CallId: {functionResultContent.CallId}");
                Debug.WriteLine($"        Result: {functionResultContent.Result}");
                Debug.WriteLine($"        Exception: {functionResultContent.Exception}");
            }
            else
            {
                Debug.WriteLine($"        Unexpected {content.GetType().Name}");
            }
        }
        Debug.WriteLine($"  RawRepresentation: \r\n{JsonSerializer.Serialize(update.RawRepresentation, options)}");
        Debug.WriteLine(string.Empty);
        Debug.WriteLine(string.Empty);
#endif
    }
}
