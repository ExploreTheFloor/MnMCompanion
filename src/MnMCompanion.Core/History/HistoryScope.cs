using MnMCompanion.Ledger;

namespace MnMCompanion.History;

/// <summary>
/// Whose history the History window shows: everyone on this PC, everyone on one server, or one character. A character always comes with
/// its server, because two servers can have a character of the same name.
/// </summary>
public sealed record HistoryScope
{
    public static readonly HistoryScope Everyone = new(null, null);

    public string? Server { get; }
    public string? Character { get; }

    private HistoryScope(string? server, string? character) => (Server, Character) = (server, character);

    /// <summary>No server means everyone; a server alone means everyone on it.</summary>
    public static HistoryScope Of(string? server, string? character = null) => server is null ? Everyone : new(server, character);

    public bool IsEveryone => Server is null;
    public bool IsOneCharacter => Character is not null;

    public bool Includes(LedgerSource source) =>
        (Server is null || string.Equals(source.Server, Server, StringComparison.OrdinalIgnoreCase)) &&
        (Character is null || string.Equals(source.Character, Character, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<LedgerSource> Pick(IEnumerable<LedgerSource> sources) => sources.Where(Includes).ToList();

    /// <summary>"Everyone", "Everyone on Alpha" or "Aldric · Alpha".</summary>
    public string Label => Server is null ? "Everyone"
        : Character is null ? "Everyone on " + LedgerLocator.Capitalize(Server)
        : $"{Character} · {LedgerLocator.Capitalize(Server)}";

    /// <summary>Changes when the scope does.</summary>
    public string Key => $"{Server}/{Character}";
}
