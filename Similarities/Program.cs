

using System.ClientModel.Primitives;
using System.ClientModel;
using System.Text;
using System.Text.Json;

using Azure.AI.OpenAI;

using OpenAI.Chat;
using OpenAI.Embeddings;

/*

This chat app needs the following environment variables:
- AZURE_EMBEDDINGS_MODEL_NAME
- AZURE_EMBEDDINGS_SECRET_KEY
- AZURE_EMBEDDINGS_ENDPOINT

The AZURE_EMBEDDINGS_SECRET_KEY can be injected from a file (see below)

*/


namespace Similarities;

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
        var secretFilename = @"H:\ai\_demosecrets\oai-raf-3.txt";
        if (File.Exists(secretFilename))
        {
            var secret = File
                    .ReadAllText(secretFilename)
                    .Trim();
            Environment.SetEnvironmentVariable("AZURE_EMBEDDINGS_SECRET_KEY", secret);
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
    private async Task Start()
    {
        Console.WriteLine("Similarities by Raffaele Rialdi");
        Console.WriteLine("");

        // read the required environment variables
        var endpoint = GetEmbeddingsAzureEndpoint();
        var secretKey = GetAzureSecretKey();
        var modelname = GetAzureEmbeddingsModelName();

        var clientOptions = new AzureOpenAIClientOptions()
        {
            NetworkTimeout = TimeSpan.FromSeconds(30),
            RetryPolicy = new ClientRetryPolicy(3),
        };

        AzureOpenAIClient client = new(
            new Uri(endpoint),
            new ApiKeyCredential(secretKey),
            clientOptions);
        EmbeddingClient embeddingClient = client.GetEmbeddingClient(modelname);

        var sentences = new List<string>()
        {
            //"cell phone",
            //"glue",
            //"moon",
            //"cherry",
            //"USB",
            //"screwdriver",
            //"C#",
            //"space ship",
            //"car",
            //"banana",
            //"drill",
            //"orange",
            //"Javascript",


            "cell phone",
            "glue to repair objects",
            "moon natural satellite",
            "cherry fruit or tree",
            "USB standard connector and protocol",
            "screwdriver tool",
            "C# programming language",
            "space ship to travel in space",
            "car to transport people",
            "banana fruit or tree",
            "power drill tool ",
            "orange fruit or tree",
            "Javascript programming language",
        };

        var response = await embeddingClient.GenerateEmbeddingsAsync(sentences);
        var result = response.Value;
        if (result == null)
        {
            var httpStatus = response.GetRawResponse().Status;
            Console.WriteLine($"The Embeddings request failed with error {httpStatus}");
            return;
        }

        Console.WriteLine($"Embeddings request information:");
        Console.WriteLine($"Model:{result.Model}");
        Console.WriteLine($"Tokens used. Input:{result.Usage.InputTokenCount} Total:{result.Usage.TotalTokenCount}");
        Console.WriteLine($"Embeddings/strings count: {result.Count}/{sentences.Count}");

        var embeddings = result.Select(r => r.ToFloats().ToArray())
                                .ToList();

        var similarities = new List<(string, string, float)>();
        for (int i = 0; i < embeddings.Count; i++)
        {
            for (int j = i + 1; j < embeddings.Count; j++)
            {
                var similarity = CalculateSimilarity(
                    embeddings[i],
                    embeddings[j]);

                similarities.Add((sentences[i], sentences[j], similarity));
            }
        }

        similarities = similarities.OrderByDescending(s => s.Item3).ToList();

        var color = Console.ForegroundColor;
        float threshold = 0.5f;
        int n = 0;
        foreach (var (doc1, doc2, score) in similarities)
        {
            if (score > threshold)
            {
                Console.ForegroundColor = ConsoleColor.Green;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
            }
            Console.Write($"{score,-15}");

            if (n % 2 == 0)
            {
                Console.ForegroundColor = ConsoleColor.White;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
            }

            Console.WriteLine($"{doc1,-40}{doc2,-40}");
            n++;
        }
    }

    // Method to calculate the similarity between two float arrays using cosine distance
    private float CalculateSimilarity(float[] vector1, float[] vector2)
    {
        if (vector1.Length != vector2.Length)
        {
            throw new ArgumentException("Vectors must have the same length");
        }
        float dotProduct = 0;
        float norm1 = 0;
        float norm2 = 0;
        for (int i = 0; i < vector1.Length; i++)
        {
            dotProduct += vector1[i] * vector2[i];
            norm1 += vector1[i] * vector1[i];
            norm2 += vector2[i] * vector2[i];
        }
        return dotProduct / (MathF.Sqrt(norm1) * MathF.Sqrt(norm2));
    }


    private string GetEmbeddingsAzureEndpoint()
    => Environment.GetEnvironmentVariable("AZURE_EMBEDDINGS_ENDPOINT") ?? throw new Exception("AZURE_EMBEDDINGS_ENDPOINT not found");

    private string GetAzureSecretKey()
        => Environment.GetEnvironmentVariable("AZURE_EMBEDDINGS_SECRET_KEY") ?? throw new Exception("AZURE_EMBEDDINGS_SECRET_KEY not found");

    private string GetAzureEmbeddingsModelName()
        => Environment.GetEnvironmentVariable("AZURE_EMBEDDINGS_MODEL_NAME") ?? throw new Exception("AZURE_EMBEDDINGS_MODEL_NAME not found");
}
