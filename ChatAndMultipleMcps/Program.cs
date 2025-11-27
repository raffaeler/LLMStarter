
using System.ClientModel;
using System.ClientModel.Primitives;
using System.IO.Pipelines;

using ChatAndMultipleMcps.Helpers;
using ChatAndMultipleMcps.McpServers.AskUser;
using ChatAndMultipleMcps.McpServers.LocalFiles;
using ChatAndMultipleMcps.McpServers.Summary;

using McpClientUtilities;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using OpenAI;

/*
This app needs the following environment variables (see launchSettings.json):
- AZURE_MODEL_NAME
- AZURE_SECRET_KEY
- AZURE_ENDPOINT

Never store the keys in the same folder of the code!
The AZURE_SECRET_KEY is injected from the llmstarter.json file.

llmstarter.json file format (simple dictionary):
{
 "key1": "....secret...",
 "key2": "....secret..."
}
*/

namespace ChatAndMultipleMcps;

internal class Program
{
    private static string _secretsFile = @"H:\ai\_demosecrets\llmstarter.json";

    static async Task Main(string[] args)
    {
        IChatClient azureClient = GetAzureClient();
        IChatClient openaiClient = GetOpenAIClient();
        IChatClient deepseekClient = GetDeepSeekClient();

        IChatClient mainClient = azureClient;
        IChatClient summarySamplingClient = openaiClient;

        Console.WriteLine("Enter to continue without verbose logging");
        Console.WriteLine("V     to enable verbose logging");
        bool isVerbose = false;
        var key = Console.ReadKey();
        if (key.Key == ConsoleKey.V)
        {
            isVerbose = true;
        }
        Console.Clear();

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        if (isVerbose)
        {
            builder.Logging.AddConsole(options =>
            {
                options.LogToStandardErrorThreshold = LogLevel.Trace;
            });
        }

        builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

        #region configurations
        builder.Services.Configure<LocalFilesMcpServerConfiguration>(
            builder.Configuration.GetSection("LocalFilesMcpServer"));
        #endregion

        builder.Services.AddKeyedChatClient("main", mainClient);
        builder.Services.AddKeyedChatClient("SummarySamplingClient", summarySamplingClient);

        builder.Services.AddSingleton<McpClientFactoryService>();

        builder.Services
            .AddMcpServer()
            .WithPipesStreamServerTransport()
            .WithTools<LocalFilesMcpServer>()
            .WithTools<SummaryMcpServer>()
            .WithTools<AskUserMcpServer>();

        // ChatService manages the conversation with the interactive user
        builder.Services.AddHostedService<ChatService>();

        await builder.Build().RunAsync();
    }

    private static IChatClient GetAzureClient()
    {
        Utilities.SetSecretWithKey(_secretsFile, "east-us-2", "AZURE_SECRET_KEY");
        var azureEndpoint = Utilities.GetEnv("AZURE_ENDPOINT");
        var azureSecretKey = Utilities.GetEnv("AZURE_SECRET_KEY");
        var azureModelname = Utilities.GetEnv("AZURE_MODEL_NAME");
        var azureClient = new Azure.AI.OpenAI.AzureOpenAIClient(
            new Uri(azureEndpoint),
            new ApiKeyCredential(azureSecretKey),
            new Azure.AI.OpenAI.AzureOpenAIClientOptions()
            {
                NetworkTimeout = TimeSpan.FromMinutes(5),
                RetryPolicy = new ClientRetryPolicy(3),
            })
            .GetChatClient(azureModelname)
            .AsIChatClient();
        return azureClient;
    }

    private static IChatClient GetOpenAIClient()
    {
        Utilities.SetSecretWithKey(_secretsFile, "openai-raf", "OPENAI_SECRET_KEY");
        var openaiEndpoint = Utilities.GetEnv("OPENAI_ENDPOINT");
        var openaiSecretKey = Utilities.GetEnv("OPENAI_SECRET_KEY");
        var modelname = Utilities.GetEnv("OPENAI_MODEL_NAME");
        var openAIClient = new OpenAIClient(
            new ApiKeyCredential(openaiSecretKey),
            new OpenAIClientOptions()
            {
                Endpoint = new Uri(openaiEndpoint),
                NetworkTimeout = TimeSpan.FromMinutes(5),
                RetryPolicy = new ClientRetryPolicy(3),
            })
            .GetChatClient(modelname)
            .AsIChatClient();
        return openAIClient;
    }

    private static IChatClient GetDeepSeekClient()
    {
        Utilities.SetSecretWithKey(_secretsFile, "deepseek-raf", "DEEPSEEK_SECRET_KEY");
        var deepseekEndpoint = Utilities.GetEnv("DEEPSEEK_ENDPOINT");
        var deepseekSecretKey = Utilities.GetEnv("DEEPSEEK_SECRET_KEY");
        var deepseekModelname = Utilities.GetEnv("DEEPSEEK_MODEL_NAME");
        var deepseekClient = new OpenAIClient(
            new ApiKeyCredential(deepseekSecretKey),
            new OpenAIClientOptions()
            {
                Endpoint = new Uri(deepseekEndpoint),
                NetworkTimeout = TimeSpan.FromMinutes(5),
                RetryPolicy = new ClientRetryPolicy(3),
            })
            .GetChatClient(deepseekModelname)
            .AsIChatClient();
        return deepseekClient;
    }

}
