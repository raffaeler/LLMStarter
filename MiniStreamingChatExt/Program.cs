using System.ClientModel;
using System.ClientModel.Primitives;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using MiniStreamingChatExt.Helpers;

using OpenAI;

namespace MiniStreamingChatExt;

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


internal class Program
{
    private static string _secretsFile = @"H:\ai\_demosecrets\llmstarter.json";

    static async Task Main(string[] args)
    {
        // == Choose the client to use ==
        //var selectedClient = GetAzureClient();
        var selectedClient = GetOpenAIClient();
        //var selectedClient = GetDeepSeekClient();

        var host = Host.CreateDefaultBuilder(args)
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug(); // log to the Output Window
            })
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton<IChatClient>(sp => selectedClient);

                services.AddTransient<CustomTool>();

                services.AddHostedService<ChatService>();
            });
        var app = host.Build();

        await app.RunAsync();
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
