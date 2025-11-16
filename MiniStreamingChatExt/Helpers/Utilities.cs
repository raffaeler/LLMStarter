using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MiniStreamingChatExt.Helpers;

internal static class Utilities
{
    internal static void InjectSecret()
    {
        var secretFilename = @"H:\ai\_demosecrets\east-us-2.txt";
        if (File.Exists(secretFilename))
        {
            var secret = File
                    .ReadAllText(secretFilename)
                    .Trim();
            Environment.SetEnvironmentVariable("AZURE_SECRET_KEY", secret);
        }

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZURE_SECRET_KEY")))
        {
            Console.WriteLine($"The AZURE_SECRET_KEY environment variable has not been set");
            throw new Exception("AZURE_SECRET_KEY not set");
        }
    }

    internal static JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    internal static string GetAzureEndpoint()
        => Environment.GetEnvironmentVariable("AZURE_ENDPOINT")
            ?? throw new Exception("AZURE_ENDPOINT not found");

    internal static string GetAzureSecretKey()
        => Environment.GetEnvironmentVariable("AZURE_SECRET_KEY")
            ?? throw new Exception("AZURE_SECRET_KEY not found");

    internal static string GetAzureModelName()
        => Environment.GetEnvironmentVariable("AZURE_MODEL_NAME")
            ?? throw new Exception("AZURE_MODEL_NAME not found");
}


