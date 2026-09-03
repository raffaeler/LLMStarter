using System.Runtime.ExceptionServices;

using NAudio.Wave;

namespace Speech.Audio;

/// <summary>
/// Plays Azure TTS PCM output and WAV output from compatible providers.
/// </summary>
internal sealed class NAudioAudioPlayer : IAudioPlayer
{
    public async Task PlayAsync(
        ReadOnlyMemory<byte> audio,
        string mediaType,
        CancellationToken cancellationToken)
    {
        using var audioStream = new MemoryStream(audio.ToArray(), writable: false);
        using WaveStream reader = CreateReader(audioStream, mediaType);
        using var output = new WaveOut();
        var stopped = new TaskCompletionSource<Exception?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        output.PlaybackStopped += (_, eventArgs) =>
            stopped.TrySetResult(eventArgs.Exception);
        output.Init(reader);
        output.Play();

        using var registration = cancellationToken.Register(output.Stop);
        Exception? exception = await stopped.Task.WaitAsync(cancellationToken);
        if (exception is not null)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }

    private static WaveStream CreateReader(Stream audio, string mediaType)
    {
        if (mediaType.Equals("audio/wav", StringComparison.OrdinalIgnoreCase) ||
            mediaType.Equals("audio/x-wav", StringComparison.OrdinalIgnoreCase))
        {
            return new WaveFileReader(audio);
        }

        if (mediaType.Equals("audio/l16", StringComparison.OrdinalIgnoreCase))
        {
            // Azure's PCM response is signed 16-bit, mono audio at 24 kHz.
            return new RawSourceWaveStream(audio, new WaveFormat(24_000, 16, 1));
        }

        throw new NotSupportedException(
            $"The console player supports WAV and PCM audio, not '{mediaType}'.");
    }
}
