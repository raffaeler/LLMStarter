using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using ChatAndMCP.Helpers;

using Microsoft.Extensions.AI;

namespace ChatAndMCP;

/// <summary>
/// This class manages the streaming loop of the assistant answer.
/// There are several things that may happen:
/// - Normal completion
/// - Refusal because of safety restrictions
/// - Refusal because of max token limits
/// - Tool calls request
/// </summary>
internal class StreamingManager
{
    public string? Completion { get; private set; }
    public string? RefusalMessage { get; private set; }
    public ChatRole? StreamedRole { get; private set; } = default;
    public ChatFinishReason? FinishReason { get; private set; } = default;
    public List<AIContent> ToolCalls { get; private set; } = new();


    public async Task ProcessIncomingStreaming(
        IAsyncEnumerable<ChatResponseUpdate> streaming,
        JsonSerializerOptions? options,
        bool dumpRawObjects,
        Action<string> onOutOfBandMessage,
        Action<string, bool> onToken,
        Action<UsageDetails> onUsage)
    {
        FinishReason = default;
        StreamedRole = default;
        ToolCalls = [];
        StringBuilder contentBuilder = new();
        StringBuilder refusalBuilder = new();
        int count = 0;

        await foreach (var update in streaming)
        {
            if (update == null)
            {
                onOutOfBandMessage("No response from the assistant");
                continue;
            }

            // The partial update for the user can be captured by just
            // using the ToString method:
            // If the update contains any function related content or
            // anything that is not addressed to the user, it will
            // return an empty string which we can ignore.
            // var textChunk = update.ToString();
            // if (textChunk.Length > 0) { /* print/send to the user */ }

            await update.Dump(options);

            if (update.Role != null) StreamedRole = update.Role;
            if (update.FinishReason != null) FinishReason = update.FinishReason;

            // Alternate colors to highlight the streaming
            // of the completion.
            // Usually the model is fast enough and the streming
            // cannot be appreciated.
            foreach (AIContent content in update.Contents)
            {
                if (content is TextContent textContent)
                {
                    Debug.Assert(update.Text == textContent.Text);
                    contentBuilder.Append(textContent.Text);
                    onToken(textContent.Text, count++ % 2 == 0);

                    if (content.AdditionalProperties != null &&
                        content.AdditionalProperties.TryGetValue(
                            "refusal", out object? refusal) &&
                        refusal is string refusalText &&
                        refusalText != null)
                    {
                        refusalBuilder.Append(refusalText);
                    }
                }
                else if (content is FunctionCallContent functionCallContent)
                {
                    ToolCalls.Add(functionCallContent);
                }
                else if (content is DataContent dataContent)
                {
                    // images
                    var blob = dataContent.Data;
                    var mediaType = dataContent.MediaType;
                    var uri = dataContent.Uri;
                    // The Console does not support images :-)
                }
                else if (content is UsageContent usageContent)
                {
                    onUsage(usageContent.Details);
                }
                else
                {
                    // FunctionResultContent are not expected from the assistant
                    // and will throw as well
                    throw new NotImplementedException(
                        $"Unsupported {content.GetType().FullName}");
                }

            }
        }

        await streaming.Dump(options);

        Completion = contentBuilder.ToString();
        RefusalMessage = refusalBuilder.ToString();
    }

}
