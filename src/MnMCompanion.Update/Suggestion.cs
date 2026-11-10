using System.Text;

namespace MnMCompanion.Update;

/// <summary>What a suggestion may carry: the words a player typed, tidied the way the service expects them (it refuses control characters and anything over the length).</summary>
public static class Suggestion
{
    /// <summary>The most the service takes; the window stops counting down at this.</summary>
    public const int MaxChars = 2000;

    /// <summary>Windows line ends become plain ones, control characters other than newlines and tabs go, the ends are trimmed, and anything past <see cref="MaxChars"/> is cut.</summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Length);
        foreach (var c in text.Replace("\r\n", "\n").Replace('\r', '\n'))
        {
            if (c is '\n' or '\t' || (!char.IsControl(c) && c != '\u007f')) sb.Append(c);
        }
        var cleaned = sb.ToString().Trim();
        return cleaned.Length > MaxChars ? cleaned[..MaxChars].TrimEnd() : cleaned;
    }
}
