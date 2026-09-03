using Microsoft.Extensions.AI;

using Speech.Audio;

namespace Speech;

/// <summary>
/// Requests TTS through MEAI and delegates device-specific playback to IAudioPlayer.
/// </summary>
internal sealed class TextToSpeechService
{
    private readonly ITextToSpeechClient _client;
    private readonly IAudioPlayer _player;
    private readonly SpeechSettings _settings;

    public TextToSpeechService(
        ITextToSpeechClient client,
        IAudioPlayer player,
        SpeechSettings settings)
    {
        _client = client;
        _player = player;
        _settings = settings;
    }

    public async Task SpeakAsync(string text, CancellationToken cancellationToken)
    {
        TextToSpeechResponse response = await _client.GetAudioAsync(
            text,
            new TextToSpeechOptions
            {
                VoiceId = _settings.TextToSpeechVoice,
                AudioFormat = "pcm",
            },
            cancellationToken);

        DataContent audio = response.Contents.OfType<DataContent>().FirstOrDefault()
            ?? throw new InvalidOperationException("The TTS service returned no audio.");

        await _player.PlayAsync(
            audio.Data,
            audio.MediaType ?? "audio/l16",
            cancellationToken);
    }
}
