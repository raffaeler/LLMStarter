using System.ClientModel.Primitives;
using System.Text.Json;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

namespace MinichatExt;

internal class Program
{
    static async Task Main(string[] args)
    {
        // Inject the secret from a file into the environment variable
        Utilities.SetSecretWithKey(@"H:\ai\_demosecrets\llmstarter.json",
            "east-us-2", "AZURE_SECRET_KEY");

        var endpoint = Utilities.GetEnv("AZURE_ENDPOINT");
        var secretKey = Utilities.GetEnv("AZURE_SECRET_KEY");
        var modelname = Utilities.GetEnv("AZURE_MODEL_NAME");

        var host = Host.CreateDefaultBuilder(args)
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug(); // log to the Output Window
            })
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton<IChatClient>(sp =>
                     OpenAIClientExtensions.AsIChatClient(
                         new Azure.AI.OpenAI.AzureOpenAIClient(
                             new Uri(endpoint),
                             new System.ClientModel.ApiKeyCredential(secretKey),
                             new Azure.AI.OpenAI.AzureOpenAIClientOptions()
                             {
                                 NetworkTimeout = TimeSpan.FromMinutes(5),
                                 RetryPolicy = new ClientRetryPolicy(3),
                             }).GetChatClient(modelname)));

                services.AddHostedService<ChatService>();
            });
        var app = host.Build();
        
        await app.RunAsync();
    }
}
