namespace ConsoleUtilities;

/// <summary>
/// Adapts <see cref="Console"/> to <see cref="IConsoleTerminal"/>.
/// </summary>
public sealed class SystemConsoleTerminal : IConsoleTerminal
{
    public bool IsInputRedirected => Console.IsInputRedirected;
    public bool IsOutputRedirected => Console.IsOutputRedirected;
    public int CursorLeft => Console.CursorLeft;
    public int CursorTop => Console.CursorTop;
    public int WindowTop => Console.WindowTop;
    public int WindowWidth => Console.WindowWidth;
    public int WindowHeight => Console.WindowHeight;
    public ConsoleColor ForegroundColor
    {
        get => Console.ForegroundColor;
        set => Console.ForegroundColor = value;
    }
    public bool CursorVisible
    {
        get => OperatingSystem.IsWindows() ? Console.CursorVisible : true;
        set
        {
            if (OperatingSystem.IsWindows())
            {
                Console.CursorVisible = value;
            }
        }
    }

    public void Clear() => Console.Clear();
    public ConsoleKeyInfo ReadKey(bool intercept) => Console.ReadKey(intercept);
    public string? ReadLine() => Console.ReadLine();
    public void SetCursorPosition(int left, int top) => Console.SetCursorPosition(left, top);
    public void Write(string? value) => Console.Write(value);
    public void WriteLine() => Console.WriteLine();
    public void WriteLine(string? value) => Console.WriteLine(value);
}
