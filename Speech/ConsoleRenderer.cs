using Speech.Realtime;

namespace Speech;

/// <summary>
/// Serializes transcript/status writes from independent microphone and command tasks.
/// </summary>
internal sealed class ConsoleRenderer
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private string _partialTranscript = string.Empty;

    public async Task RenderTranscriptAsync(TranscriptionUpdate update)
    {
        await _writeLock.WaitAsync();
        try
        {
            if (update.Kind == TranscriptionUpdateKind.Partial)
            {
                _partialTranscript += update.Text;
                WriteReplacingLine($"Listening: {_partialTranscript}");
                return;
            }

            ClearLine();
            Console.WriteLine($"Transcript: {update.Text}");
            _partialTranscript = string.Empty;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task WriteStatusAsync(string message)
    {
        await _writeLock.WaitAsync();
        try
        {
            ClearLine();
            Console.WriteLine(message);
            if (_partialTranscript.Length > 0)
            {
                Console.Write($"Listening: {_partialTranscript}");
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static void WriteReplacingLine(string text)
    {
        if (Console.IsOutputRedirected)
        {
            Console.WriteLine(text);
            return;
        }

        ClearLine();
        Console.Write(text);
    }

    private static void ClearLine()
    {
        if (Console.IsOutputRedirected)
        {
            return;
        }

        Console.Write('\r');
        Console.Write(new string(' ', Math.Max(Console.WindowWidth - 1, 1)));
        Console.Write('\r');
    }
}
