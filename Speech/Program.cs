using System.ClientModel;
using System.ClientModel.Primitives;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Speech.Audio;
using Speech.Realtime;

namespace Speech;

internal class Program
{
    private const string SecretsFile = @"H:\ai\_demosecrets\llmstarter.json";
    private const string SecretKeyName = "east-us-2";

    static async Task Main(string[] args)
    {
        Utilities.SetSecretWithKey(SecretsFile, SecretKeyName, "AZURE_SECRET_KEY");
        SpeechSettings settings = SpeechSettings.FromEnvironment();

        var host = Host.CreateDefaultBuilder(args)
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug();
                logging.AddConsole();
            })
            .ConfigureServices(services =>
            {
                services.AddSingleton(settings);
                services.AddSingleton<ITextToSpeechClient>(_ =>
                    new Azure.AI.OpenAI.AzureOpenAIClient(
                        settings.Endpoint,
                        new ApiKeyCredential(settings.ApiKey),
                        new Azure.AI.OpenAI.AzureOpenAIClientOptions
                        {
                            NetworkTimeout = TimeSpan.FromMinutes(5),
                            RetryPolicy = new ClientRetryPolicy(3),
                        })
                    .GetAudioClient(settings.TextToSpeechDeployment)
                    .AsITextToSpeechClient());

                services.AddSingleton<IAudioCapture, NAudioMicrophoneCapture>();
                services.AddSingleton<IAudioPlayer, NAudioAudioPlayer>();
                services.AddSingleton<RealtimeTranscriptionClient>();
                services.AddSingleton<TextToSpeechService>();
                services.AddSingleton<ConsoleRenderer>();
                services.AddHostedService<SpeechDemoService>();
            })
            .Build();

        await host.RunAsync();
    }
}

internal sealed record SpeechSettings(
    Uri Endpoint,
    string ApiKey,
    string RealtimeDeployment,
    string TextToSpeechDeployment,
    string TextToSpeechVoice,
    string? TranscriptionLanguage,
    string TranscriptionDelay)
{
    public static SpeechSettings FromEnvironment() => new(
        new Uri(Utilities.GetEnv("AZURE_ENDPOINT")),
        Utilities.GetEnv("AZURE_SECRET_KEY"),
        Utilities.GetEnv("AZURE_REALTIME_MODEL_NAME"),
        Utilities.GetEnv("AZURE_TTS_MODEL_NAME"),
        Environment.GetEnvironmentVariable("AZURE_TTS_VOICE") ?? "alloy",
        Environment.GetEnvironmentVariable("AZURE_TRANSCRIPTION_LANGUAGE"),
        Environment.GetEnvironmentVariable("AZURE_TRANSCRIPTION_DELAY") ?? "medium");
}
