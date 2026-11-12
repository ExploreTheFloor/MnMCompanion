namespace MnMCompanion.Display;

/// <summary>
/// The typeface the windows are drawn in. The player picks one from a short list (<see cref="Offered"/>), or none: then the program's own, Segoe UI
/// Variable where Windows has it. Only a name from the list is ever used (what is saved may say anything), and only if it is installed on this PC
/// (under Wine or Proton the list is shorter). Symbols (the icons in the title bars) and the code-style text in the notes keep their own fonts.
/// </summary>
public static class UiFonts
{
    /// <summary>The typefaces offered, in the order the Size and font window shows them (after "Default").</summary>
    public static readonly IReadOnlyList<string> Offered =
    [
        "Segoe UI", "Arial", "Calibri", "Verdana", "Tahoma", "Trebuchet MS", "Candara", "Corbel", "Georgia", "Cambria", "Bahnschrift", "Consolas", "Cascadia Mono",
    ];

    /// <summary>What the program is drawn in when nothing is chosen: the body text, and the larger figures and headings.</summary>
    public const string DefaultText = "Segoe UI Variable Text, Segoe UI";
    public const string DefaultDisplay = "Segoe UI Variable Display, Segoe UI";

    /// <summary>The offered typefaces that are installed here, in list order. <paramref name="installed"/> says whether a name is installed.</summary>
    public static IReadOnlyList<string> Available(Func<string, bool> installed) => Offered.Where(installed).ToList();

    /// <summary>
    /// The offered name that <paramref name="saved"/> means (any letter case, spaces around it ignored), or null for the program's own: for no choice, a name that is
    /// not offered, or one that is not installed here.
    /// </summary>
    public static string? Normalise(string? saved, Func<string, bool> installed) =>
        Offered.FirstOrDefault(n => string.Equals(n, saved?.Trim(), StringComparison.OrdinalIgnoreCase)) is { } name && installed(name) ? name : null;

    /// <summary>The font family list for body text: the chosen typeface, with Segoe UI behind it for anything it lacks; the program's own for null.</summary>
    public static string Text(string? chosen) => chosen is null ? DefaultText : chosen + ", Segoe UI";

    /// <summary>The font family list for the larger figures and headings.</summary>
    public static string Display(string? chosen) => chosen is null ? DefaultDisplay : chosen + ", Segoe UI";
}
