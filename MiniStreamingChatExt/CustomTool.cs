using System.ComponentModel;

namespace MiniStreamingChatExt;

public class CustomTool
{
    [Description("Reverse a string")]
    [return: Description("The reversed string")]
    public string ReverseString([Description("The string to reverse")] string text)
        => new string(text.Reverse().ToArray());


    [Description("Make the string uppercase")]
    [return: Description("The uppercase string")]
    public string ToUpper(
        // this parameter will not be sent to the model!
        bool redact,
        [Description("The string whose case is to be changed")] string text)
    {
        if (redact) return new string('*', text.Length);
        return text.ToUpper();
    }
}