using System.Text;

namespace ConsoleUtilities;

/// <summary>
/// A reusable single-line editor with arrow-key choice selection.
/// </summary>
public sealed class ConsoleLineEditor
{
    private const int MaximumVisibleChoices = 10;
    private readonly IConsoleTerminal _terminal;

    public ConsoleLineEditor(IConsoleTerminal terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        _terminal = terminal;
    }

    /// <summary>
    /// Reads a line and displays choices supplied by application-owned completion logic.
    /// </summary>
    public string? ReadLine(
        string prompt,
        Func<ConsoleCompletionRequest, IReadOnlyList<ConsoleCompletionItem>> completionProvider)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(completionProvider);

        if (_terminal.IsInputRedirected || _terminal.IsOutputRedirected)
        {
            _terminal.Write(prompt);
            return _terminal.ReadLine();
        }

        try
        {
            return ReadInteractiveLine(prompt, completionProvider);
        }
        catch (Exception exception) when (
            exception is IOException
            or PlatformNotSupportedException
            or ArgumentOutOfRangeException
            or InvalidOperationException)
        {
            // Some hosts expose a Console but do not support cursor positioning.
            _terminal.Write(prompt);
            return _terminal.ReadLine();
        }
    }

    private string? ReadInteractiveLine(
        string prompt,
        Func<ConsoleCompletionRequest, IReadOnlyList<ConsoleCompletionItem>> completionProvider)
    {
        StringBuilder input = new();
        int inputCursor = 0;
        int selectedIndex = 0;
        string? dismissedText = null;
        int regionTop = _terminal.CursorTop;
        int reservedRows = 0;
        bool originalCursorVisible = _terminal.CursorVisible;

        try
        {
            _terminal.CursorVisible = false;

            while (true)
            {
                string text = input.ToString();
                IReadOnlyList<ConsoleCompletionItem> choices = dismissedText == text
                    ? []
                    : completionProvider(new ConsoleCompletionRequest(text, inputCursor));

                if (choices.Count == 0)
                {
                    selectedIndex = 0;
                }
                else
                {
                    selectedIndex = Math.Clamp(selectedIndex, 0, choices.Count - 1);
                }

                Render(prompt, text, inputCursor, choices, selectedIndex, ref regionTop, ref reservedRows);
                ConsoleKeyInfo key = _terminal.ReadKey(intercept: true);

                if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key == ConsoleKey.C)
                {
                    CompleteRender(prompt, text, regionTop, reservedRows);
                    return null;
                }

                switch (key.Key)
                {
                    case ConsoleKey.UpArrow when choices.Count > 0:
                        selectedIndex = selectedIndex == 0 ? choices.Count - 1 : selectedIndex - 1;
                        break;

                    case ConsoleKey.DownArrow when choices.Count > 0:
                        selectedIndex = (selectedIndex + 1) % choices.Count;
                        break;

                    case ConsoleKey.Tab when choices.Count > 0:
                    case ConsoleKey.Enter when choices.Count > 0:
                        ConsoleCompletionItem choice = choices[selectedIndex];
                        input.Clear();
                        input.Append(choice.ReplacementText);
                        inputCursor = input.Length;
                        selectedIndex = 0;
                        dismissedText = choice.DismissAfterInsert ? input.ToString() : null;
                        if (choice.Submit)
                        {
                            CompleteRender(prompt, input.ToString(), regionTop, reservedRows);
                            return input.ToString();
                        }
                        break;

                    case ConsoleKey.Enter:
                        CompleteRender(prompt, text, regionTop, reservedRows);
                        return text;

                    case ConsoleKey.Escape:
                        dismissedText = text;
                        selectedIndex = 0;
                        break;

                    case ConsoleKey.Backspace when inputCursor > 0:
                        input.Remove(inputCursor - 1, 1);
                        inputCursor--;
                        dismissedText = null;
                        break;

                    case ConsoleKey.Delete when inputCursor < input.Length:
                        input.Remove(inputCursor, 1);
                        dismissedText = null;
                        break;

                    case ConsoleKey.LeftArrow when inputCursor > 0:
                        inputCursor--;
                        break;

                    case ConsoleKey.RightArrow when inputCursor < input.Length:
                        inputCursor++;
                        break;

                    case ConsoleKey.Home:
                        inputCursor = 0;
                        break;

                    case ConsoleKey.End:
                        inputCursor = input.Length;
                        break;

                    default:
                        if (!char.IsControl(key.KeyChar))
                        {
                            input.Insert(inputCursor, key.KeyChar);
                            inputCursor++;
                            dismissedText = null;
                        }
                        break;
                }
            }
        }
        finally
        {
            _terminal.CursorVisible = originalCursorVisible;
        }
    }

    private void Render(
        string prompt,
        string text,
        int inputCursor,
        IReadOnlyList<ConsoleCompletionItem> choices,
        int selectedIndex,
        ref int regionTop,
        ref int reservedRows)
    {
        int visibleChoiceCount = Math.Min(
            choices.Count,
            Math.Min(MaximumVisibleChoices, Math.Max(1, _terminal.WindowHeight - 1)));
        int desiredRows = 1 + visibleChoiceCount;
        ReserveRows(desiredRows, ref regionTop, ref reservedRows);

        int width = Math.Max(1, _terminal.WindowWidth);
        ClearRegion(regionTop, reservedRows, width);

        (string visibleInput, int visibleCursor) = GetVisibleInput(prompt, text, inputCursor, width);
        _terminal.SetCursorPosition(0, regionTop);
        _terminal.Write(Clip(prompt + visibleInput, width));

        if (visibleChoiceCount > 0)
        {
            int firstChoice = Math.Clamp(
                selectedIndex - visibleChoiceCount + 1,
                0,
                Math.Max(0, choices.Count - visibleChoiceCount));

            for (int row = 0; row < visibleChoiceCount; row++)
            {
                int choiceIndex = firstChoice + row;
                string marker = choiceIndex == selectedIndex ? "> " : "  ";
                _terminal.SetCursorPosition(0, regionTop + row + 1);
                _terminal.Write(Clip(marker + choices[choiceIndex].DisplayText, width));
            }
        }

        int cursorLeft = Math.Clamp(prompt.Length + visibleCursor, 0, width - 1);
        _terminal.SetCursorPosition(cursorLeft, regionTop);
    }

    private void ReserveRows(int desiredRows, ref int regionTop, ref int reservedRows)
    {
        if (reservedRows >= desiredRows)
        {
            return;
        }

        int rowsToAdd = desiredRows - Math.Max(1, reservedRows);
        if (reservedRows == 0)
        {
            rowsToAdd = desiredRows - 1;
            regionTop = _terminal.CursorTop;
        }
        else
        {
            _terminal.SetCursorPosition(0, regionTop + reservedRows - 1);
        }

        for (int row = 0; row < rowsToAdd; row++)
        {
            _terminal.WriteLine();
        }

        if (rowsToAdd > 0)
        {
            regionTop = Math.Max(_terminal.WindowTop, _terminal.CursorTop - desiredRows + 1);
        }

        reservedRows = desiredRows;
    }

    private void CompleteRender(string prompt, string text, int regionTop, int reservedRows)
    {
        int width = Math.Max(1, _terminal.WindowWidth);
        ClearRegion(regionTop, Math.Max(1, reservedRows), width);
        _terminal.SetCursorPosition(0, regionTop);
        _terminal.Write(Clip(prompt + text, width));
        _terminal.WriteLine();
    }

    private void ClearRegion(int regionTop, int rows, int width)
    {
        string blankLine = new(' ', Math.Max(1, width - 1));
        for (int row = 0; row < rows; row++)
        {
            _terminal.SetCursorPosition(0, regionTop + row);
            _terminal.Write(blankLine);
        }
    }

    private static (string Text, int Cursor) GetVisibleInput(
        string prompt,
        string text,
        int cursor,
        int terminalWidth)
    {
        int availableWidth = Math.Max(1, terminalWidth - prompt.Length);
        int firstCharacter = cursor >= availableWidth ? cursor - availableWidth + 1 : 0;
        firstCharacter = Math.Min(firstCharacter, Math.Max(0, text.Length - availableWidth));
        int length = Math.Min(availableWidth, text.Length - firstCharacter);
        return (text.Substring(firstCharacter, length), cursor - firstCharacter);
    }

    private static string Clip(string value, int width)
    {
        int maximumLength = Math.Max(1, width - 1);
        return value.Length <= maximumLength ? value : value[..maximumLength];
    }
}
