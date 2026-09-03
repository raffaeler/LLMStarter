using System.Runtime.CompilerServices;
using System.Threading.Channels;

using NAudio.Wave;

namespace Speech.Audio;

/// <summary>
/// Captures small PCM frames so audio can be sent as it arrives rather than
/// accumulating an entire recording in memory.
/// </summary>
internal sealed class NAudioMicrophoneCapture : IAudioCapture
{
    private const int BufferMilliseconds = 100;
    private const int ChannelCapacity = 20;

    public AudioFormat Format { get; } = new(24_000, 16, 1);

    public async IAsyncEnumerable<AudioFrame> CaptureAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<AudioFrame>(new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
        });

        using var waveIn = new WaveIn
        {
            WaveFormat = new WaveFormat(Format.SampleRate, Format.BitsPerSample, Format.Channels),
            BufferMilliseconds = BufferMilliseconds,
        };

        waveIn.DataAvailable += OnDataAvailable;
        waveIn.RecordingStopped += OnRecordingStopped;
        using var registration = cancellationToken.Register(waveIn.StopRecording);

        try
        {
            waveIn.StartRecording();

            await foreach (AudioFrame frame in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return frame;
            }
        }
        finally
        {
            waveIn.DataAvailable -= OnDataAvailable;
            waveIn.RecordingStopped -= OnRecordingStopped;
            waveIn.StopRecording();
            channel.Writer.TryComplete();
        }

        void OnDataAvailable(object? sender, WaveInEventArgs eventArgs)
        {
            byte[] frame = eventArgs.Buffer[..eventArgs.BytesRecorded];
            if (!channel.Writer.TryWrite(new AudioFrame(frame)))
            {
                channel.Writer.TryComplete(new InvalidOperationException(
                    "Microphone frames could not be sent fast enough."));
            }
        }

        void OnRecordingStopped(object? sender, StoppedEventArgs eventArgs)
        {
            channel.Writer.TryComplete(eventArgs.Exception);
        }
    }
}
