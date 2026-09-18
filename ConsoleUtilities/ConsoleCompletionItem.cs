namespace ConsoleUtilities;

/// <summary>
/// Describes a choice displayed by <see cref="ConsoleLineEditor"/>.
/// </summary>
/// <param name="DisplayText">Text displayed in the choices list.</param>
/// <param name="ReplacementText">Text that replaces the complete input line.</param>
/// <param name="Submit">Whether accepting the choice also submits the input.</param>
/// <param name="DismissAfterInsert">Whether to hide choices until the input changes.</param>
public sealed record ConsoleCompletionItem(
    string DisplayText,
    string ReplacementText,
    bool Submit = false,
    bool DismissAfterInsert = false);

/// <summary>
/// The current editor state supplied to a completion provider.
/// </summary>
public sealed record ConsoleCompletionRequest(string Text, int CursorPosition);
