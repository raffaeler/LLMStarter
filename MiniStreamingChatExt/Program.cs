using System.ClientModel.Primitives;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
        InjectSecret();
        var p = new Program();
        var endpoint = p.GetAzureEndpoint();
        var secretKey = p.GetAzureSecretKey();
        var modelname = p.GetAzureModelName();

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

    private string GetAzureEndpoint()
        => Environment.GetEnvironmentVariable("AZURE_ENDPOINT") ?? throw new Exception("AZURE_ENDPOINT not found");

    private string GetAzureSecretKey()
        => Environment.GetEnvironmentVariable("AZURE_SECRET_KEY") ?? throw new Exception("AZURE_SECRET_KEY not found");

    private string GetAzureModelName()
        => Environment.GetEnvironmentVariable("AZURE_MODEL_NAME") ?? throw new Exception("AZURE_MODEL_NAME not found");
}
