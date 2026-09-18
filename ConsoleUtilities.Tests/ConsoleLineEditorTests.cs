using ConsoleUtilities;
using System.Text;
using Xunit;

namespace ConsoleUtilities.Tests;

public sealed class ConsoleLineEditorTests
{
    [Fact]
    public void ReadLine_SelectsNestedChoicesWithArrowKeys()
    {
        FakeConsoleTerminal terminal = new(
            new ConsoleKeyInfo('/', ConsoleKey.Oem2, false, false, false),
            Key(ConsoleKey.DownArrow),
            Key(ConsoleKey.Enter),
            Key(ConsoleKey.Enter));
        ConsoleLineEditor editor = new(terminal);

        string? result = editor.ReadLine("You: ", Complete);

        Assert.Equal("/prompt file", result);
        Assert.True(terminal.CursorVisible);
    }

    [Fact]
    public void ReadLine_ReservesRowsWhenPromptStartsAtBottomOfWindow()
    {
        FakeConsoleTerminal terminal = new(
            new ConsoleKeyInfo('/', ConsoleKey.Oem2, false, false, false),
            Key(ConsoleKey.Enter))
        {
            CursorTopValue = 4,
            WindowHeightValue = 5,
        };
        ConsoleLineEditor editor = new(terminal);

        string? result = editor.ReadLine("You: ", request =>
        [
            new ConsoleCompletionItem("/quit  Exit", "/quit", Submit: true),
            new ConsoleCompletionItem("/new   New", "/new", Submit: true),
            new ConsoleCompletionItem("/help  Help", "/help", Submit: true),
        ]);

        Assert.Equal("/quit", result);
        Assert.True(terminal.BlankLinesWritten >= 3);
        Assert.True(terminal.WindowTop > 0);
    }

    [Fact]
    public void ReadLine_UsesSimpleInputWhenRedirected()
    {
        FakeConsoleTerminal terminal = new()
        {
            IsInputRedirectedValue = true,
            RedirectedLine = "/quit",
        };
        ConsoleLineEditor editor = new(terminal);

        string? result = editor.ReadLine("You: ", _ => throw new InvalidOperationException());

        Assert.Equal("/quit", result);
        Assert.StartsWith("You: ", terminal.Output.ToString());
    }

    private static IReadOnlyList<ConsoleCompletionItem> Complete(ConsoleCompletionRequest request)
    {
        return request.Text switch
        {
            "/" =>
            [
                new("/system", "/system "),
                new("/prompt", "/prompt "),
            ],
            "/prompt " =>
            [
                new("file", "/prompt file", Submit: true),
                new("summary", "/prompt summary", Submit: true),
            ],
            _ => [],
        };
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);

    private sealed class FakeConsoleTerminal(params ConsoleKeyInfo[] keys) : IConsoleTerminal
    {
        private readonly Queue<ConsoleKeyInfo> _keys = new(keys);

        public bool IsInputRedirectedValue { get; init; }
        public bool IsOutputRedirectedValue { get; init; }
        public int CursorLeftValue { get; private set; }
        public int CursorTopValue { get; set; }
        public int WindowTop { get; private set; }
        public int WindowWidthValue { get; init; } = 80;
        public int WindowHeightValue { get; init; } = 25;
        public string? RedirectedLine { get; init; }
        public int BlankLinesWritten { get; private set; }
        public StringBuilder Output { get; } = new();

        public bool IsInputRedirected => IsInputRedirectedValue;
        public bool IsOutputRedirected => IsOutputRedirectedValue;
        public int CursorLeft => CursorLeftValue;
        public int CursorTop => CursorTopValue;
        public int WindowWidth => WindowWidthValue;
        public int WindowHeight => WindowHeightValue;
        public ConsoleColor ForegroundColor { get; set; } = ConsoleColor.Gray;
        public bool CursorVisible { get; set; } = true;

        public void Clear()
        {
            CursorLeftValue = 0;
            CursorTopValue = 0;
            WindowTop = 0;
        }

        public ConsoleKeyInfo ReadKey(bool intercept) => _keys.Dequeue();
        public string? ReadLine() => RedirectedLine;

        public void SetCursorPosition(int left, int top)
        {
            CursorLeftValue = left;
            CursorTopValue = top;
        }

        public void Write(string? value)
        {
            Output.Append(value);
            CursorLeftValue += value?.Length ?? 0;
        }

        public void WriteLine()
        {
            Output.AppendLine();
            BlankLinesWritten++;
            CursorLeftValue = 0;
            CursorTopValue++;
            if (CursorTopValue >= WindowTop + WindowHeightValue)
            {
                WindowTop++;
            }
        }

        public void WriteLine(string? value)
        {
            Write(value);
            WriteLine();
        }
    }
}
