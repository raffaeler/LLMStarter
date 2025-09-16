using System.ClientModel.Primitives;

using ChatAndMCP.Server.AskUser;
using ChatAndMCP.Server.LocalFiles;
using ChatAndMCP.Server.Summary;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChatAndMCP;

internal class Program
{
    static async Task Main(string[] args)
    {
        Utilities.InjectSecret();
        var endpoint = Utilities.GetAzureEndpoint();
        var secretKey = Utilities.GetAzureSecretKey();
        var modelname = Utilities.GetAzureModelName();


        var builder = Host.CreateApplicationBuilder();
        builder.Logging.AddConsole(options =>
        {
            options.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

        #region configurations
        builder.Services.Configure<LocalFilesMcpServerConfiguration>(
            builder.Configuration.GetSection("LocalFilesMcpServer"));
        #endregion


        #region main client configuration
        // the main client is the one used by the ChatService
        // to interact with the LLM
        var mainAzureOpenAIClient = new Azure.AI.OpenAI.AzureOpenAIClient(
            new Uri(endpoint),
            new System.ClientModel.ApiKeyCredential(secretKey),
            new Azure.AI.OpenAI.AzureOpenAIClientOptions()
            {
                NetworkTimeout = TimeSpan.FromMinutes(5),
                RetryPolicy = new ClientRetryPolicy(3),
            });

        var mainIChatClient = mainAzureOpenAIClient.GetChatClient(modelname)
            .AsIChatClient();
        builder.Services.AddKeyedChatClient("main", mainIChatClient);
        #endregion

        #region sampling client configuration
        // The sampling client is used by the MCP servers
        // to use an LLM provided by the client
        var summarySamplingModelName = modelname;   // this can be a different model!
        var summarySamplingAzureOpenAIClient = new Azure.AI.OpenAI.AzureOpenAIClient(
            new Uri(endpoint),
            new System.ClientModel.ApiKeyCredential(secretKey),
            new Azure.AI.OpenAI.AzureOpenAIClientOptions()
            {
                NetworkTimeout = TimeSpan.FromMinutes(5),
                RetryPolicy = new ClientRetryPolicy(3),
            });

        var samplingIChatClient = summarySamplingAzureOpenAIClient.GetChatClient(summarySamplingModelName).AsIChatClient();
        builder.Services.AddKeyedChatClient("SummarySamplingClient", samplingIChatClient);
        #endregion

        // MCP servers
        builder.Services.AddSingleton<IMyMcpServer, LocalFilesMcpServer>();
        builder.Services.AddSingleton<IMyMcpServer, SummaryMcpServer>();
        builder.Services.AddSingleton<IMyMcpServer, AskUserMcpServer>();

        // InMemoryMcpService is used to talk in-process with the MCP servers
        builder.Services.AddSingleton<InMemoryMcpService>();


        // ChatService manages the conversation with the interactive user
        builder.Services.AddHostedService<ChatService>();

        await builder.Build().RunAsync();
    }

}
