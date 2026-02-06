using System.Text.Unicode;

namespace Utils;

public static class MessageValidator
{
    private static bool ContainsEscapeSequences(string text)
    {
        foreach (char c in text)
        {
            if (c < 0x20 && c != '\t' && c != '\n' && c != '\r') return true;
            if (c == 0x7F) return true;
        }

        return false;
    }
    public static bool IsValidMessage(byte[] bytes, string text)
    {
        return Utf8.IsValid(bytes)
               && !string.IsNullOrEmpty(text)
               && !ContainsEscapeSequences(text);
    }
}