

using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text;
using System.Text.Json;

using Azure.AI.OpenAI;
using Azure.AI.OpenAI.Chat;

using OpenAI.Chat;

/*

This chat app needs the following environment variables:
- AZURE_MODEL_NAME
- AZURE_SECRET_KEY
- AZURE_ENDPOINT

The AZURE_SECRET_KEY can be injected from a file (see below)

*/

namespace MiniStreamingChat;

internal class Program
{
    static async Task Main(string[] args)
    {
        InjectSecret();
        var p = new Program();
        await p.Start();
    }

    private static void InjectSecret()
    {
        var secretFilename = @"H:\ai\_demosecrets\east-us-2.txt";
        if (File.Exists(secretFilename))
        {
            var secret = File
                    .ReadAllText(secretFilename)
                    .Trim();
            Environment.SetEnvironmentVariable("AZURE_SECRET_KEY", secret);
        }
    }

    private JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Starts the chat
    /// </summary>
    private Task Start()
    {
        Console.WriteLine("Minichat using streaming by Raffaele Rialdi");
        Console.WriteLine("");
        Console.WriteLine("Start chatting or type 'exit' to quit");

        // read the required environment variables
        var endpoint = GetAzureEndpoint();
        var secretKey = GetAzureSecretKey();
        var modelname = GetAzureModelName();

        var clientOptions = new AzureOpenAIClientOptions()
        {
            NetworkTimeout = TimeSpan.FromSeconds(30),
            RetryPolicy = new ClientRetryPolicy(3),
        };

        AzureOpenAIClient client = new(
            new Uri(endpoint),
            new ApiKeyCredential(secretKey),
            clientOptions);
        ChatClient chatClient = client.GetChatClient(modelname);

        string? systemPrompt = GetOptionalSystemPrompt();

        List<ChatTool> tools = new();

        var reverseStringToolDefinition =
            ChatTool.CreateFunctionTool(
                functionName: "ReverseString",
                functionDescription: "Reverse the string provided as input",
                functionParameters: BinaryData.FromString(
                    FunctionJsonReverseParameters),
                functionSchemaIsStrict: false);

        // Comment this line to disable the tool
        tools.Add(reverseStringToolDefinition);
        // =====================================

        ChatCompletionOptions options = new()
        {
            MaxOutputTokenCount = 500,
            Temperature = 0.7f,
            TopP = 0.8f,
            FrequencyPenalty = 0,
            PresencePenalty = 0,
        };

        if (tools.Count > 0)
        {
            options.ToolChoice = ChatToolChoice.CreateAutoChoice();
            //options.AllowParallelToolCalls = true;
        }

        foreach (var tool in tools)
        {
            options.Tools.Add(tool);
        }

        return ChatLoop(chatClient, options, systemPrompt);
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

#pragma warning disable AOAI001 // Experimental
    /// <summary>
    /// The main chat loop
    /// </summary>
    /// <param name="chatClient">The Azure chat client</param>
    /// <param name="options">The parameters for the model</param>
    /// <param name="systemprompt">The optional system prompt</param>
    /// <returns></returns>
    private async Task ChatLoop(
        ChatClient chatClient,
        ChatCompletionOptions options,
        string? systemprompt)
    {
        List<ChatMessage> prompts = new();
        if (systemprompt != null)
        {
            prompts.Add(ChatMessage.CreateSystemMessage(
                systemprompt));
        }

        var currentColor = Console.ForegroundColor;
        var evenColor = ConsoleColor.Yellow;
        var oddColor = ConsoleColor.Green;

        bool lastWasTool = false;
        do
        {
            if (!lastWasTool)
            {
                Console.Write("You: ");
                var userMessage = Console.ReadLine();
                if (userMessage == "exit")
                {
                    Console.WriteLine("Goodbye!");
                    return;
                }

                prompts.Add(ChatMessage.CreateUserMessage(userMessage));
            }

            // Variables accumulating the small parts
            // of the completions while streaming
            ChatFinishReason? finishReason = default;
            ChatMessageRole? streamedRole = default;
            StringBuilder contentBuilder = new();
            StringBuilder refusalBuilder = new();
            Dictionary<int, string> toolCallIdsByIndex = [];
            Dictionary<int, string> toolNamesByIndex = [];
            Dictionary<int, StringBuilder> toolArgumentsBuilders = [];

            AsyncCollectionResult<StreamingChatCompletionUpdate> response =
                chatClient.CompleteChatStreamingAsync(prompts, options);

            int count = 0;
            await foreach (var update in response)
            {
                if (update.Role != null)
                    streamedRole = update.Role;

                if (update.FinishReason != null)
                    finishReason = update.FinishReason;

                // Alternate colors to highlight the streaming
                // of the completion.
                // Usually the model is fast enough and the streming
                // cannot be appreciated.
                if (count++ % 2 == 0)
                    Console.ForegroundColor = evenColor;
                else
                    Console.ForegroundColor = oddColor;

                foreach (ChatMessageContentPart contentPart in update.ContentUpdate)
                {
                    contentBuilder.Append(contentPart.Text);
                    refusalBuilder.Append(contentPart.Refusal);

                    // TODO: stream image
                    Console.Write(contentPart.Text);
                }

                Console.ForegroundColor = currentColor;

                foreach (StreamingChatToolCallUpdate toolCallUpdate in update.ToolCallUpdates)
                {
                    if (!string.IsNullOrEmpty(toolCallUpdate.ToolCallId))
                    {
                        toolCallIdsByIndex[toolCallUpdate.Index] =
                            toolCallUpdate.ToolCallId;
                    }

                    if (!string.IsNullOrEmpty(toolCallUpdate.FunctionName))
                    {
                        toolNamesByIndex[toolCallUpdate.Index] = toolCallUpdate.FunctionName;
                    }

                    var arguments = toolCallUpdate.FunctionArgumentsUpdate.ToString();
                    if (!string.IsNullOrEmpty(arguments))
                    {
                        if (!toolArgumentsBuilders.TryGetValue(
                            toolCallUpdate.Index, out StringBuilder? sb) || sb == null)
                        {
                            sb = new();
                            toolArgumentsBuilders[toolCallUpdate.Index] = sb;
                        }

                        sb.Append(arguments);
                    }
                }
            }

            Console.WriteLine();

            List<ChatToolCall> toolCalls = [];
            foreach (KeyValuePair<int, string> indexToIdPair in toolCallIdsByIndex)
            {
                var binaryData = BinaryData.FromString(
                    toolArgumentsBuilders[indexToIdPair.Key].ToString());

                toolCalls.Add(ChatToolCall.CreateFunctionToolCall(
                    indexToIdPair.Value,
                    toolNamesByIndex[indexToIdPair.Key],
                    binaryData));
            }


            var completion = contentBuilder.ToString();
            if(completion?.Length > 0)
                prompts.Add(ChatMessage.CreateAssistantMessage(completion));
            if (toolCalls.Count > 0)
                prompts.Add(ChatMessage.CreateAssistantMessage(toolCalls));

            switch (finishReason)
            {
                // Content filtered by the model
                case ChatFinishReason.ContentFilter:
                    Console.WriteLine($"AI: (answer was filtered because: {refusalBuilder.ToString()}");
                    break;

                // Max tokens reached
                case ChatFinishReason.Length:
                    Console.WriteLine("AI: Max tokens reached");
                    break;


                // The 'Stop' reason is still emitted but can be ignored.
                // When streaming, we already printed the parts
                // of the answer as they arrived, therefore
                // we don't need to print it again
                //
                case ChatFinishReason.Stop:
                    lastWasTool = false;
                    break;

                // The model requested to invoke a tool
                // Tools are the new name for Functions
                case ChatFinishReason.FunctionCall:
                case ChatFinishReason.ToolCalls:
                    Console.WriteLine("AI: tool request");
                    ProcessToolRequest(toolCalls, prompts);
                    lastWasTool = true;
                    break;

                // other reasons
                default:
                    Console.WriteLine($"AI: Finish reason: {finishReason}");
                    break;
            }

        } while (true);
    }
#pragma warning restore AOAI001 // Experimental

    /// <summary>
    /// This method processes the tool request
    /// </summary>
    /// <param name="completion">The object from the model</param>
    /// <param name="prompts">The list of prompts (the state)</param>
    private void ProcessToolRequest(
        IList<ChatToolCall> toolCalls,
        IList<ChatMessage> prompts)
    {
        foreach (var toolCall in toolCalls)
        {
            var functionName = toolCall.FunctionName;
            var functionArguments = toolCall.FunctionArguments
                .ToString();

            var arguments = JsonSerializer
                .Deserialize<ReverseStringArguments>(
                    functionArguments,
                    _jsonOptions);

            if (arguments == null || arguments.Text == null)
            {
                Console.WriteLine("AI: Error in tool request");
                return;
            }

            Console.WriteLine($"AI asked to invoke function {functionName}({arguments.Text})");

            // Invoke the function

            var result = InvokeFunction(
                functionName,
                arguments.Text);

            prompts.Add(ChatMessage.CreateToolMessage(
                toolCall.Id, result));
        }
    }

    /// <summary>
    /// Invoke a function given its name and arguments
    /// </summary>
    /// <param name="functionName">The function name</param>
    /// <param name="functionArguments">The json string containing the arguments</param>
    /// <returns>The result from the invocation</returns>
    private string InvokeFunction(
        string functionName,
        string functionArguments)
    {
        switch (functionName)
        {
            case "ReverseString":
                return new string(functionArguments.Reverse().ToArray());
            //case "uppercase":
            //    return functionArguments.ToUpper();
            //case "lowercase":
            //    return functionArguments.ToLower();
            default:
                return $"Function {functionName} not found";
        }
    }

    private string GetAzureEndpoint()
        => Environment.GetEnvironmentVariable("AZURE_ENDPOINT") ?? throw new Exception("AZURE_ENDPOINT not found");

    private string GetAzureSecretKey()
        => Environment.GetEnvironmentVariable("AZURE_SECRET_KEY") ?? throw new Exception("AZURE_SECRET_KEY not found");

    private string GetAzureModelName()
        => Environment.GetEnvironmentVariable("AZURE_MODEL_NAME") ?? throw new Exception("AZURE_MODEL_NAME not found");

    /// <summary>
    /// This is the full Tool definition as defined by OpenAI.
    /// The Azure OpenAI library requires a subset of this
    /// </summary>
#pragma warning disable CS0414
    private string FunctionJsonReverse = """
          "name": "ReverseString",
          "description": "Reverse the string provided as input",
          "isStrict": true,
          "parameters": {
            "type": "object",
            "properties": {
              "text": {
                "type": "string",
                "description": "A string provided by the user"
              }
            },
            "required": [
              "text"
            ]
          },
          "output": {
            "type": "string",
            "description": "The reversed string"
          }
        }
        
        """
#pragma warning restore CS0414 // Add readonly modifier
;

    /// <summary>
    /// The Azure library requires a portion of the JSON
    /// schema defined in the previous string.
    /// </summary>
    private string FunctionJsonReverseParameters = """
        {
            "type": "object",
            "properties": {
                "text": {
                    "type": "string",
                    "description": "A string provided by the user"
                }
            },
            "required": [
                "text"
            ],
            "additionalProperties": false
        }
        
        """;

    /// <summary>
    /// This class simplifies the deserialization of the
    /// arguments passed from the model to the function.
    /// It reflects the arguments specified in the json above.
    /// </summary>
    private class ReverseStringArguments
    {
        public string Text { get; set; } = string.Empty;
    }

}
