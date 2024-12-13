

using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text;
using System.Text.Json;

using Azure.AI.OpenAI;

using OpenAI.Chat;

/*

This chat app needs the following environment variables:
- AZURE_MODEL_NAME
- AZURE_SECRET_KEY
- AZURE_ENDPOINT

The AZURE_SECRET_KEY can be injected from a file (see below)

*/

namespace Minichat;

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
        Console.WriteLine("Minichat by Raffaele Rialdi");
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

        if(tools.Count > 0)
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

    /// <summary>
    /// The main chat loop
    /// </summary>
    /// <param name="chatClient">The Azure chat client</param>
    /// <param name="options">The parameters for the model</param>
    /// <param name="systemprompt">The optional system prompt</param>
    /// <returns></returns>
    private Task ChatLoop(
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
                    return Task.CompletedTask;
                }

                prompts.Add(ChatMessage.CreateUserMessage(userMessage));
            }

            var response = chatClient.CompleteChat(
                prompts,
                options);

            var completion = response.Value;
            prompts.Add(ChatMessage.CreateAssistantMessage(completion));

            switch (completion.FinishReason)
            {
                // Content filtered by the model
                case ChatFinishReason.ContentFilter:
                    Console.WriteLine($"AI: (answer was filtered because: {completion.Refusal}");
                    break;

                // Max tokens reached
                case ChatFinishReason.Length:
                    Console.WriteLine("AI: Max tokens reached");
                    break;

                // The completion is ready
                // An answer is finally available
                case ChatFinishReason.Stop:
                    {
                        var answer = GetAnswer(completion);
                        if (!string.IsNullOrEmpty(answer))
                        {
                            Console.WriteLine($"AI: {answer}");
                            lastWasTool = false;
                        }
                    }
                    break;

                // The model requested to invoke a tool
                // Tools are the new name for Functions
                case ChatFinishReason.FunctionCall:
                case ChatFinishReason.ToolCalls:
                    Console.WriteLine("AI: tool request");
                    ProcessToolRequest(completion, prompts);
                    lastWasTool = true;
                    break;

                // other reasons
                default:
                    Console.WriteLine($"AI: Finish reason: {completion.FinishReason}");
                    break;
            }

        } while (true);
    }

    /// <summary>
    /// This method processes the tool request
    /// </summary>
    /// <param name="completion">The object from the model</param>
    /// <param name="prompts">The list of prompts (the state)</param>
    private void ProcessToolRequest(
        ChatCompletion completion,
        IList<ChatMessage> prompts)
    {
        foreach (var toolCall in completion.ToolCalls)
        {
            var functionName = toolCall.FunctionName;
            var functionArguments = toolCall.FunctionArguments
                .ToString();

            var arguments= JsonSerializer
                .Deserialize<ReverseStringArguments>(
                    functionArguments,
                    _jsonOptions);

            if(arguments == null || arguments.Text == null)
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

    /// <summary>
    /// Extract the anser from the completion
    /// </summary>
    /// <param name="completion">The completion object obtained from the model</param>
    /// <returns>The string from the model</returns>
    private string GetAnswer(ChatCompletion completion)
    {
        StringBuilder sb = new();
        foreach (var part in completion.Content)
        {
            sb.Append(part.Text);
        }

        return sb.ToString();

        // equivalent code:
        //return string.Join("",
        //    completion.Content.Select(part => part.Text));
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

