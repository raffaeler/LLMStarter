using System.ClientModel.Primitives;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using MiniStreamingChatExt.Helpers;

namespace MiniStreamingChatExt;

/*

This chat app needs the following environment variables:
- AZURE_MODEL_NAME
- AZURE_SECRET_KEY
- AZURE_ENDPOINT

The AZURE_SECRET_KEY can be injected from a file (see below)

*/

internal class Program
{
    static async Task Main(string[] args)
    {
        Utilities.InjectSecret();
        var endpoint = Utilities.GetAzureEndpoint();
        var secretKey = Utilities.GetAzureSecretKey();
        var modelname = Utilities.GetAzureModelName();

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

                services.AddTransient<CustomTool>();

                services.AddHostedService<ChatService>();
            });
        var app = host.Build();

        await app.RunAsync();
    }

}
