using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Speech.Audio;
using Speech.Realtime;

namespace Speech;

internal sealed class SpeechDemoService : BackgroundService
{
    private readonly ILogger<SpeechDemoService> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IAudioCapture _microphone;
    private readonly RealtimeTranscriptionClient _transcription;
    private readonly TextToSpeechService _textToSpeech;
    private readonly ConsoleRenderer _console;

    public SpeechDemoService(
        ILogger<SpeechDemoService> logger,
        IHostApplicationLifetime lifetime,
        IAudioCapture microphone,
        RealtimeTranscriptionClient transcription,
        TextToSpeechService textToSpeech,
        ConsoleRenderer console)
    {
        _logger = logger;
        _lifetime = lifetime;
        _microphone = microphone;
        _transcription = transcription;
        _textToSpeech = textToSpeech;
        _console = console;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.CancelKeyPress += OnCancelKeyPress;
        try
        {
            await _console.WriteStatusAsync(
                "Live transcription started. Type text and press Enter to hear it; type 'exit' to quit.");

            Task transcriptionTask = RunTranscriptionAsync(stoppingToken);

            await RunTextToSpeechLoopAsync(stoppingToken);
            await transcriptionTask;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Cancellation is the normal exit path for the console application.
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The speech demonstration stopped unexpectedly.");
            await _console.WriteStatusAsync($"Speech demonstration error: {exception.Message}");
        }
        finally
        {
            Console.CancelKeyPress -= OnCancelKeyPress;
            _lifetime.StopApplication();
        }
    }

    private async Task RunTranscriptionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _transcription.RunAsync(
                _microphone,
                _console.RenderTranscriptAsync,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancellation is the normal exit path for the transcription task.
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Live transcription stopped unexpectedly.");
            await _console.WriteStatusAsync($"Live transcription error: {exception.Message}");
        }
    }

    private async Task RunTextToSpeechLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Console.WriteLine();
            Console.Write("Speak text (or 'exit'): ");
            string? input = await Console.In.ReadLineAsync(cancellationToken);

            if (input is null || input.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                _lifetime.StopApplication();
                return;
            }

            if (string.IsNullOrWhiteSpace(input))
            {
                continue;
            }

            await _console.WriteStatusAsync("Generating speech...");
            try
            {
                await _textToSpeech.SpeakAsync(input, cancellationToken);
                await _console.WriteStatusAsync("Speech playback completed.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Text-to-speech request failed.");
                await _console.WriteStatusAsync($"Text-to-speech error: {exception.Message}");
            }
        }
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs eventArgs)
    {
        eventArgs.Cancel = true;
        _lifetime.StopApplication();
    }
}
