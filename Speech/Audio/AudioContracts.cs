namespace Speech.Audio;

/// <summary>
/// Describes raw PCM frames exchanged with the real-time transcription service.
/// </summary>
internal sealed record AudioFormat(int SampleRate, int BitsPerSample, int Channels);

internal sealed record AudioFrame(byte[] Data);

internal interface IAudioCapture
{
    AudioFormat Format { get; }

    IAsyncEnumerable<AudioFrame> CaptureAsync(CancellationToken cancellationToken);
}

internal interface IAudioPlayer
{
    Task PlayAsync(
        ReadOnlyMemory<byte> audio,
        string mediaType,
        CancellationToken cancellationToken);
}
