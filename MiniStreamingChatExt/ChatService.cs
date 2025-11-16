using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MiniStreamingChatExt;

public class ChatService : BackgroundService
{
    private readonly ILogger _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly CustomTool _customTool;
    private readonly IChatClient _client;
    private Dictionary<string, AIFunction> _tools = new();
    private static Func<string, Dictionary<string, object?>?> _toolsArgumentParser =
        static json => JsonSerializer.Deserialize<Dictionary<string, object?>>(json, AIJsonUtilities.DefaultOptions);

    public ChatService( 
        ILogger<ChatService> logger,
        IHostApplicationLifetime lifetime,
        CustomTool customTool,
        IChatClient client)
    {
        _logger = logger;
        _lifetime = lifetime;
        _customTool = customTool;
        _client = client;
    }

    /// <summary>
    /// Starts the chat
    /// </summary>
    protected override async Task ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine("Minichat using Microsoft.Extensions.AI by Raffaele Rialdi");
        Console.WriteLine("");
        Console.WriteLine("Start chatting or type 'exit' to quit");

        string? systemPrompt = GetOptionalSystemPrompt();

        AIFunctionFactoryOptions ffOptions = new()
        {
            ConfigureParameterBinding = (parameter) =>
            {
                // This is the default behavior
                // The parameter name is used as the key
                // in the JSON object
                //binding.Name = parameter.Name;

                if (parameter.Name != "redact" ||
                    parameter.ParameterType != typeof(bool))
                {
                    return default;
                }

                return new()
                {
                    // redact is not sent to the model
                    ExcludeFromSchema = true,
                    BindParameter = (ParameterInfo p, AIFunctionArguments a) =>
                    {
                        if (a.Context == null ||
                            !a.Context.TryGetValue("redact",
                                out object? redactObj) ||
                            redactObj == null ||
                            redactObj is not bool)
                        {
                            return default;
                        }

                        var redact = (bool)redactObj;
                        return redact;
                    }
                };
            },
        };

        _tools[nameof(_customTool.ReverseString)] =
            AIFunctionFactory.Create(_customTool.ReverseString, ffOptions);
        _tools[nameof(_customTool.ToUpper)] =
            AIFunctionFactory.Create(_customTool.ToUpper, ffOptions);

        ChatOptions options = new()
        {
            MaxOutputTokens = 500,
            Temperature = 0.7f,
            TopP = 0.8f,
            FrequencyPenalty = 0,
            PresencePenalty = 0,
            Tools = _tools.Values.OfType<AITool>().ToList(),
        };

        if (options.Tools.Count > 0)
        {
            options.ToolMode = ChatToolMode.Auto;
        }
        else
        {
            options.ToolMode = ChatToolMode.None;
        }

        await ChatLoop(options, systemPrompt);
        _lifetime.StopApplication();
    }

    /// <summary>
    /// Prompt the user for a system prompt
    /// or return null if the user provide
    /// an empty string.
    /// </summary>
    /// <returns></returns>
    private string? GetOptionalSystemPrompt()
    {
        Console.WriteLine("Type the system prompt or press enter to skip");
        Console.Write("System prompt: ");
        var systemPrompt = Console.ReadLine();
        return string.IsNullOrWhiteSpace(systemPrompt) ? null : systemPrompt;
    }

    /// <summary>
    /// The main chat loop
    /// </summary>
    /// <param name="options">The parameters for the model</param>
    /// <param name="systemprompt">The optional system prompt</param>
    /// <returns></returns>
    private async Task ChatLoop(
        ChatOptions options,
        string? systemprompt)
    {
        _logger.LogInformation("Entering the chat loop");
        List<ChatMessage> prompts = new();
        if (systemprompt != null)
        {
            prompts.Add(new ChatMessage(ChatRole.System, systemprompt));
        }

        var currentColor = Console.ForegroundColor;
        var evenColor = ConsoleColor.Yellow;
        var oddColor = ConsoleColor.Green;
        var otherColor = ConsoleColor.Cyan;

        string answer = "";
        bool lastWasTool = false;
        do
        {
            Console.ForegroundColor = currentColor;
            if (!lastWasTool)
            {
                Console.Write("You: ");
                var userMessage = Console.ReadLine();
                if (userMessage == "quit" || userMessage == "exit")
                {
                    Console.WriteLine("Goodbye!");
                    return;
                }

                prompts.Add(new ChatMessage(ChatRole.User, userMessage));
            }

            // Accumulating small updates/chunks while streaming
            ChatFinishReason? finishReason = default;
            ChatRole? streamedRole = default;
            StringBuilder contentBuilder = new();
            StringBuilder refusalBuilder = new();
            List<AIContent> toolCalls = [];

            IAsyncEnumerable<ChatResponseUpdate> streaming =
                _client.GetStreamingResponseAsync(prompts, options);

            Debug.WriteLine("=== Asynchronous updates ===");
            int count = 0;
            await foreach (var update in streaming)
            {
                if (update == null)
                {
                    Console.WriteLine("No response from the assistant");
                    continue;
                }

                // The partial update for the user can be captured by just
                // using the ToString method:
                // If the update contains any function related content or
                // anything that is not addressed to the user, it will
                // return an empty string which we can ignore.
                // var textChunk = update.ToString();
                // if (textChunk.Length > 0) { /* print/send to the user */ }

                await Diag.Dump(update);

                if (update.Role != null) streamedRole = update.Role;
                if (update.FinishReason != null) finishReason = update.FinishReason;

                // Alternate colors to highlight the streaming
                // of the completion.
                // Usually the model is fast enough and the streming
                // cannot be appreciated.
                foreach (AIContent content in update.Contents)
                {
                    if (content is TextContent textContent)
                    {
                        if (count++ % 2 == 0)
                            Console.ForegroundColor = evenColor;
                        else
                            Console.ForegroundColor = oddColor;

                        Debug.Assert(update.Text == textContent.Text);
                        contentBuilder.Append(textContent.Text);
                        Console.Write(textContent.Text);

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
                        toolCalls.Add(functionCallContent);
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
                        var usage = usageContent.Details;

                        Console.WriteLine();
                        Console.ForegroundColor = otherColor;
                        Console.WriteLine($"Usage: T = {usage.TotalTokenCount} = I({usage.InputTokenCount}) + O({usage.OutputTokenCount}) + A({usage.AdditionalCounts?.Select(a => a.Value).Sum() ?? 0})");
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

            await Diag.Dump(streaming);
            Console.ForegroundColor = currentColor;
            Console.WriteLine();

            var completion = contentBuilder.ToString();
            if (completion?.Length > 0)
                prompts.Add(new ChatMessage(ChatRole.Assistant, completion));
            if (toolCalls.Count > 0)
                prompts.Add(new ChatMessage(ChatRole.Assistant, toolCalls));


            if (finishReason == ChatFinishReason.ContentFilter)
            {
                // Content filtered by the model
                answer = $"Answer was filtered";
                lastWasTool = false;
                Console.WriteLine($"AI Refusal: {refusalBuilder.ToString()}");
            }
            else if (finishReason == ChatFinishReason.Length)
            {
                // Max tokens reached
                answer = "AI: Max tokens reached";
                lastWasTool = false;
            }
            else if (finishReason == ChatFinishReason.Stop)
            {
                // The completion is ready
                // An answer is finally available
                answer = completion ?? string.Empty;
                Debug.WriteLine($"AI: {answer}");
                lastWasTool = false;
            }
            else if (finishReason == ChatFinishReason.ToolCalls)
            {
                // The model requested to invoke a tool
                // Tools are the new name for Functions
                answer = "AI: tool request";
                await ProcessToolRequest(toolCalls, prompts);
                lastWasTool = true;
            }
            else
            {
                answer = $"AI: Finish reason: {finishReason}";
                lastWasTool = false;
            }
        }
        while (true);
    }


    /// <summary>
    /// This method processes the tool request
    /// </summary>
    /// <param name="completion">The object from the model</param>
    /// <param name="prompts">The list of prompts (the state)</param>
    private async Task ProcessToolRequest(
        IList<AIContent> toolContents,
        IList<ChatMessage> prompts)
    {
        foreach (var toolCall in toolContents.OfType<FunctionCallContent>())
        {
            var functionName = toolCall.Name;
            var arguments = new AIFunctionArguments(toolCall.Arguments);
            arguments.Context = new Dictionary<object, object?>();
            arguments.Context["redact"] = true;

            Debug.WriteLine($"AI asked to invoke function {functionName}(...)");

            if (!_tools.TryGetValue(functionName, out AIFunction? _tool))
            {
                Console.WriteLine($"Unknown function {functionName}");
                continue;
            }

            var result = await _tool.InvokeAsync(arguments);

            ChatMessage responseMessage = new(ChatRole.Tool,
                [
                    new FunctionResultContent(toolCall.CallId, result)
                ]);

            prompts.Add(responseMessage);
        }
    }

}
