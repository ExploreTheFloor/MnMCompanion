using MnMCompanion.History;
using MnMCompanion.Loot;

namespace MnMCompanion;

/// <summary>
/// What the History window is looking at: a <see cref="HistoryScope"/> (everyone, one server, one character) and the loot store that reads
/// those characters' ledgers. The store is the window's own, not the small window's, so the two can look at different characters.
/// </summary>
internal sealed class HistoryFocus
{
    public HistoryFocus(LootStore loot, HistoryScope scope)
    {
        Loot = loot;
        Scope = scope;
        Apply();
    }

    public LootStore Loot { get; }

    public HistoryScope Scope { get; private set; }

    /// <summary>Changes when the scope does or the characters in it do (a new one appeared).</summary>
    public string Key => Scope.Key + "|" + Loot.SelectionKey;

    public void Set(HistoryScope scope)
    {
        Scope = scope;
        Apply();
    }

    /// <summary>Looks for characters again (a new one may have started a ledger) and reads the ones the scope covers. True when that set changed.</summary>
    public bool Sync()
    {
        Loot.RescanSources();
        return Apply();
    }

    private bool Apply()
    {
        var picked = Scope.Pick(Loot.Sources);
        if (string.Join("|", picked.Select(s => s.Directory)) == Loot.SelectionKey) return false;
        Loot.Select(picked);
        return true;
    }
}
