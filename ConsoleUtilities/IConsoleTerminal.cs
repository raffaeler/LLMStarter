namespace ConsoleUtilities;

/// <summary>
/// Abstracts the console operations used by interactive terminal applications.
/// </summary>
public interface IConsoleTerminal
{
    bool IsInputRedirected { get; }
    bool IsOutputRedirected { get; }
    int CursorLeft { get; }
    int CursorTop { get; }
    int WindowTop { get; }
    int WindowWidth { get; }
    int WindowHeight { get; }
    ConsoleColor ForegroundColor { get; set; }
    bool CursorVisible { get; set; }

    void Clear();
    ConsoleKeyInfo ReadKey(bool intercept);
    string? ReadLine();
    void SetCursorPosition(int left, int top);
    void Write(string? value);
    void WriteLine();
    void WriteLine(string? value);
}
