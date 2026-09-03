using System.Net.WebSockets;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

using Speech.Audio;

namespace Speech.Realtime;

/// <summary>
/// Isolates Azure's real-time WebSocket event protocol from the console and
/// microphone code. The session accepts 24 kHz, mono, signed 16-bit PCM audio.
/// </summary>
internal sealed class RealtimeTranscriptionClient : IAsyncDisposable
{
    private static readonly TimeSpan CommitInterval = TimeSpan.FromSeconds(3);
    private readonly SpeechSettings _settings;
    private readonly ClientWebSocket _socket = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public RealtimeTranscriptionClient(SpeechSettings settings)
    {
        _settings = settings;
        _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        _socket.Options.CollectHttpResponseDetails = true;
        _socket.Options.SetRequestHeader("api-key", settings.ApiKey);
    }

    public async Task RunAsync(
        IAudioCapture microphone,
        Func<TranscriptionUpdate, Task> onUpdate,
        CancellationToken cancellationToken)
    {
        if (microphone.Format is not { SampleRate: 24_000, BitsPerSample: 16, Channels: 1 })
        {
            throw new NotSupportedException(
                "Azure Realtime transcription requires 24 kHz, 16-bit, mono PCM audio.");
        }

        Uri realtimeUri = CreateRealtimeUri();
        try
        {
            await _socket.ConnectAsync(realtimeUri, cancellationToken);
        }
        catch (WebSocketException exception)
        {
            throw new InvalidOperationException(
                $"Azure Realtime rejected the WebSocket handshake at '{realtimeUri}'. " +
                "Verify that AZURE_ENDPOINT is the Azure OpenAI resource root, " +
                "the deployment supports live transcription, and the API key belongs to that resource.",
                exception);
        }

        await ConfigureSessionAsync(cancellationToken);

        Task receiveTask = ReceiveUpdatesAsync(onUpdate, cancellationToken);
        Task sendTask = SendAudioAsync(microphone, cancellationToken);

        await Task.WhenAll(sendTask, receiveTask);
    }

    public async ValueTask DisposeAsync()
    {
        _sendLock.Dispose();

        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            await _socket.CloseOutputAsync(
                WebSocketCloseStatus.NormalClosure,
                "Console application is stopping.",
                CancellationToken.None);
        }

        _socket.Dispose();
    }

    private Uri CreateRealtimeUri()
    {
        string endpointPath = _settings.Endpoint.AbsolutePath.TrimEnd('/');
        string realtimePath = endpointPath.Equals("/openai/v1", StringComparison.OrdinalIgnoreCase)
            ? "/openai/v1/realtime"
            : string.IsNullOrEmpty(endpointPath)
                ? "/openai/v1/realtime"
                : throw new InvalidOperationException(
                    "AZURE_ENDPOINT must be the Azure OpenAI resource root or end with '/openai/v1'.");

        var builder = new UriBuilder(_settings.Endpoint)
        {
            Scheme = _settings.Endpoint.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
                ? "wss"
                : "ws",
            Path = realtimePath,
            Query = "intent=transcription",
        };

        return builder.Uri;
    }

    private async Task ConfigureSessionAsync(CancellationToken cancellationToken)
    {
        var transcription = new Dictionary<string, object?>
        {
            ["model"] = _settings.RealtimeDeployment,
            ["delay"] = _settings.TranscriptionDelay,
        };

        if (!string.IsNullOrWhiteSpace(_settings.TranscriptionLanguage))
        {
            transcription["language"] = _settings.TranscriptionLanguage;
        }

        await SendEventAsync(new
        {
            type = "session.update",
            session = new
            {
                type = "transcription",
                audio = new
                {
                    input = new
                    {
                        format = new { type = "audio/pcm", rate = 24_000 },
                        // The transcription endpoint emits completed transcript
                        // events for explicitly committed audio buffers.
                        turn_detection = (object?)null,
                        transcription,
                    },
                },
            },
        }, cancellationToken);

        while (true)
        {
            using JsonDocument configured = await ReceiveJsonAsync(cancellationToken);
            string eventType = GetEventType(configured.RootElement);
            if (eventType == "error")
            {
                throw CreateRealtimeException(configured.RootElement);
            }

            if (eventType == "session.updated")
            {
                return;
            }

            if (eventType != "session.created")
            {
                throw new InvalidOperationException(
                    $"Expected 'session.updated' but received '{eventType}'.");
            }
        }
    }

    private async Task SendAudioAsync(
        IAudioCapture microphone,
        CancellationToken cancellationToken)
    {
        var commitStopwatch = Stopwatch.StartNew();
        await foreach (AudioFrame frame in microphone.CaptureAsync(cancellationToken))
        {
            await SendEventAsync(new
            {
                type = "input_audio_buffer.append",
                audio = Convert.ToBase64String(frame.Data),
            }, cancellationToken);

            if (commitStopwatch.Elapsed >= CommitInterval)
            {
                await SendEventAsync(new { type = "input_audio_buffer.commit" }, cancellationToken);
                commitStopwatch.Restart();
            }
        }
    }

    private async Task ReceiveUpdatesAsync(
        Func<TranscriptionUpdate, Task> onUpdate,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using JsonDocument message = await ReceiveJsonAsync(cancellationToken);
            JsonElement root = message.RootElement;
            string eventType = GetEventType(root);

            switch (eventType)
            {
                case "conversation.item.input_audio_transcription.delta":
                    await onUpdate(new TranscriptionUpdate(
                        TranscriptionUpdateKind.Partial,
                        root.GetProperty("delta").GetString() ?? string.Empty));
                    break;

                case "conversation.item.input_audio_transcription.completed":
                    await onUpdate(new TranscriptionUpdate(
                        TranscriptionUpdateKind.Completed,
                        root.GetProperty("transcript").GetString() ?? string.Empty));
                    break;

                case "conversation.item.input_audio_transcription.failed":
                    throw CreateRealtimeException(root);

                case "error":
                    throw CreateRealtimeException(root);

                case "session.updated":
                case "input_audio_buffer.speech_started":
                case "input_audio_buffer.speech_stopped":
                    break;
            }
        }
    }

    private async Task SendEventAsync<T>(T payload, CancellationToken cancellationToken)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await _socket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task<JsonDocument> ReceiveJsonAsync(CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[4_096];
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;

        do
        {
            result = await _socket.ReceiveAsync(
                new ArraySegment<byte>(buffer),
                cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new WebSocketException("The Azure Realtime session closed unexpectedly.");
            }

            stream.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        return JsonDocument.Parse(stream.ToArray());
    }

    private static string GetEventType(JsonElement root) =>
        root.GetProperty("type").GetString()
        ?? throw new InvalidOperationException("Azure Realtime returned an event without a type.");

    private static InvalidOperationException CreateRealtimeException(JsonElement root)
    {
        string message = root.TryGetProperty("error", out JsonElement error) &&
            error.TryGetProperty("message", out JsonElement detail)
            ? detail.GetString() ?? "Unknown Azure Realtime error."
            : "Unknown Azure Realtime error.";

        return new InvalidOperationException($"Azure Realtime error: {message}");
    }
}
