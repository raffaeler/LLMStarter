namespace Speech.Realtime;

internal enum TranscriptionUpdateKind
{
    Partial,
    Completed,
}

internal sealed record TranscriptionUpdate(TranscriptionUpdateKind Kind, string Text);
