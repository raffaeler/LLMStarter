using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ChatAndMCP.Server.AskUser;

internal class AskUserMcpServer : IMyMcpServer
{
    private readonly ILogger<AskUserMcpServer> _logger;

    public AskUserMcpServer(
        ILogger<AskUserMcpServer> logger)
    {
        _logger = logger;

        ServerInfo = new()
        {
            Name = "AskUser MCP Server",
            Title = "Provides the tool askuser_askquestion to interactively ask questions to the user",
            Version = "1.0.0",
        };

        Capabilities = new()
        {
            Tools = new ToolsCapability()
            {
                ToolCollection =
                [
                    McpServerTool.Create(AskQuestion),
                ],
            },

            Prompts = new PromptsCapability()
            {
                PromptCollection =
                [
                    McpServerPrompt.Create(ElicitSystemPrompt)
                ],
            }
        };
    }

    public Implementation ServerInfo { get; }
    public ServerCapabilities Capabilities { get; }


    [McpServerTool(Name = "askuser_askquestion")]
    [Description("""
        Use this tool to ask a question about their last request whenever you need more information or a clarification from the user.
        """)]
    [return: Description("The answer to the question, provided by the user")]
    public async Task<string> AskQuestion(IMcpServer mcpServer,
        [Description("The question asked to the user")]
        string question)
    {
        _logger.LogInformation($"{nameof(AskQuestion)}: question={question}");

        ElicitRequestParams elicitRequestParams = new()
        {
            Message = question,
            RequestedSchema = new ElicitRequestParams.RequestSchema()
            {
                Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>()
                {
                    ["answer"] = new ElicitRequestParams.StringSchema
                    {
                        Title = "answer",
                        MinLength = 1,
                        MaxLength = 300,
                    },
                },
            },
        };


        ElicitResult result = await mcpServer.ElicitAsync(elicitRequestParams, default);
        if (result.Action == "decline")
        {
            return "Error: the user declined to answer";
        }
        else if (result.Action == "cancel")
        {
            return "Error: the user canceled the request";
        }
        else if (result.Action != "accept")
        {
            return $"Error: unknown Action: {result.Action}";
        }

        Debug.Assert(result.Action == "accept");

        IDictionary<string, JsonElement>? resultDictionary = result.Content;

        if (resultDictionary == null ||
            !resultDictionary.TryGetValue("answer", out var answerElement))
        {
            return "Error: the answer is not textual";
        }

        string? answer = answerElement.GetString();
        if (answer == null)
        {
            return "Error: the answer is null text";
        }

        if (answer.Length == 0)
        {
            return "Error: the answer is empty";
        }

        return answer;
    }



    [McpServerPrompt(Name = "system")]
    [Description("The prompt needed to correctly use the Memory tools")]
    public string ElicitSystemPrompt() => """

            Use the tool 'askuser_askquestion' to interactively ask questions to the user, whenever you have multiple options to choose from.


            """;
    //The tool will return the answer provided by the user, giving you the opportunity to exactly identify the best possible answer to the user's original request.

}

