namespace MnMCompanion.Ledger;

/// <summary>
/// Notices which character the game is playing now. The game rewrites the ledger files of the character that is logged in, so a character
/// whose newest ledger file has been written since the last look is the one being played. It only reads file times (the same ones
/// <see cref="LedgerLocator.Find"/> reports), and the first look just remembers them, so what was written before the app started never counts.
/// </summary>
public sealed class ActiveCharacterWatch
{
    private readonly Dictionary<string, DateTime> _seen = new(StringComparer.OrdinalIgnoreCase);
    private bool _primed;

    /// <summary>
    /// Takes the sources as <see cref="LedgerLocator.Find"/> just returned them. Returns the character to switch to: the one written since the
    /// last call (the newest if several were), unless the one being shown (<paramref name="showing"/>, its folder) was written too, which means it
    /// is still being played and stays. Null most of the time. Call it on every look, even while not following, so the next change is the only thing
    /// it reacts to.
    /// </summary>
    public LedgerSource? Update(IReadOnlyList<LedgerSource> sources, string? showing)
    {
        var written = new List<LedgerSource>();
        foreach (var s in sources)
        {
            if (!s.HasLedger) continue;
            bool moved = _primed && (!_seen.TryGetValue(s.Directory, out var before) || s.LastActive > before);
            _seen[s.Directory] = s.LastActive;
            if (moved) written.Add(s);
        }
        _primed = true;
        if (written.Count == 0) return null;
        if (showing is not null && written.Any(w => string.Equals(w.Directory, showing, StringComparison.OrdinalIgnoreCase))) return null;
        return written.OrderByDescending(w => w.LastActive).First();
    }
}
