using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MnMCompanion.Ledger;

/// <summary>
/// The small encodings the game's ledger uses. Most strings in a row are base64 (an encoding, not encryption); a few are
/// plain (an item's name and its 24-hex id), and coin comes either as digits "pp,gp,sp,cp" or as words.
/// </summary>
public static class LedgerText
{
    /// <summary>
    /// Base64 → text when the whole value decodes to printable UTF-8, else the value as it was. Plain words that happen to be
    /// valid base64 ("None") and hex ids are left alone.
    /// </summary>
    public static string Decode(string s)
    {
        if (s.Length < 4 || s.Length % 4 != 0) return s;
        foreach (var c in s)
            if (!(char.IsAsciiLetterOrDigit(c) || c is '+' or '/' or '=')) return s;
        if (s.All(char.IsAsciiLetter) && s.Length <= 8) return s;
        if (s.All(char.IsAsciiHexDigit)) return s;
        try
        {
            var text = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(s));
            return text.Length == 0 || text.Any(char.IsControl) ? s : text;
        }
        catch (Exception ex) when (ex is FormatException or DecoderFallbackException or ArgumentException) { return s; }
    }

    /// <summary>"name_YSBy…" → "a risen knight", "zone_dmFs…" → "valeofzintar", "ref_57073" → "57073": a short prefix, then maybe base64.</summary>
    public static string Prefixed(string s)
    {
        int us = s.IndexOf('_');
        return us is > 0 and < 8 ? Decode(s[(us + 1)..]) : Decode(s);
    }

    /// <summary>"0,0,1,15" (platinum, gold, silver, copper) → 115 copper; "" or anything else → 0.</summary>
    public static long Packed(string digits)
    {
        var parts = digits.Split(',');
        if (parts.Length != 4) return 0;
        long total = 0;
        long[] scale = [1_000_000, 10_000, 100, 1];
        for (int i = 0; i < 4; i++)
        {
            if (!long.TryParse(parts[i].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return 0;
            total += n * scale[i];
        }
        return total;
    }

    private static readonly Regex Words = new(@"(\d+)\s*(platinum|gold|silver|copper)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>"0 platinum 0 gold 2 silver 71 copper" → 271. A hundred of each coin make the next, as the game counts it.</summary>
    public static long Coin(string text)
    {
        long total = 0;
        foreach (Match m in Words.Matches(text))
        {
            if (!long.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) continue;
            total += char.ToLowerInvariant(m.Groups[2].Value[0]) switch { 'p' => n * 1_000_000, 'g' => n * 10_000, 's' => n * 100, _ => n };
        }
        return total;
    }
}
