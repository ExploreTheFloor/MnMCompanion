namespace MnMCompanion.Loot;

/// <summary>A party row of the ledger: <see cref="Act"/> (act_31 created, act_32 joined, act_33 disbanded, act_34 left, act_35 leader changed), whether the ledger's owner did it, and the member count after it.</summary>
public readonly record struct PartyChange(DateTime Time, string Act, bool ByOwner, int Count);

/// <summary>A purse someone else's autosplit divided, and the share of it that reached the ledger's owner (what their ledger says).</summary>
public readonly record struct DividedPurse(DateTime Time, long Purse, long Share);

/// <summary>
/// What the ledger leaves out about the owner's own purses. When the owner is in a group and their autosplit is on, a purse they loot is divided like
/// anyone's, and the game's chat says so ("… and receive 23 copper coins as your split"). The ledger doesn't: it writes the purse as the owner's,
/// whole, with no share, exactly as for a purse looted alone. So whether the owner was in a group has to be worked out, from the party rows
/// (<see cref="PartyChange"/>) and from the purses others divided with them (<see cref="DividedPurse"/>), and the share they were handed estimated
/// from what they got of those: coin the owner gets of a divided purse follows the same split every time, so the share of their own is the
/// purse times the fraction that reached them from the nearest purses of others.
/// </summary>
public sealed class GroupSplits
{
    /// <summary>A divided purse of someone else this close to a moment shows the owner was in a group then, whatever the party rows say.</summary>
    public static readonly TimeSpan Evidence = TimeSpan.FromMinutes(10);

    /// <summary>How many purses of others, each side, set the fraction for a purse; only ones this close count.</summary>
    private const int Neighbours = 12;
    private static readonly TimeSpan Reach = TimeSpan.FromHours(2);

    private readonly (DateTime At, bool In, int Count)[] _party;
    private readonly DividedPurse[] _divided;

    public GroupSplits(IEnumerable<PartyChange> party, IEnumerable<DividedPurse> divided)
    {
        var states = new List<(DateTime, bool, int)>();
        bool inGroup = false;
        int count = 0;
        foreach (var c in party.OrderBy(c => c.Time))
        {
            switch (c.Act)
            {
                case "act_31": inGroup = true; count = Math.Max(2, c.Count); break;
                // A join the ledger shows is one of a group the owner is in (joining someone's group writes only the owner's own row)
                case "act_32": inGroup = true; count = Math.Max(2, c.Count); break;
                case "act_33": inGroup = false; count = 0; break;
                case "act_34":
                    inGroup = !c.ByOwner && c.Count >= 2;
                    count = inGroup ? c.Count : 0;
                    break;
                default: continue;
            }
            states.Add((c.Time, inGroup, count));
        }
        _party = [.. states];
        _divided = [.. divided.Where(d => d.Purse > 0).OrderBy(d => d.Time)];
    }

    /// <summary>The owner was in a group at <paramref name="at"/>: the party rows say so, or a purse of someone else's was divided with them close by.</summary>
    public bool InGroup(DateTime at) => PartyAt(at).In || HasEvidence(at);

    /// <summary>
    /// What the owner was probably handed of a purse of <paramref name="purse"/> they looted at <paramref name="at"/>: the fraction that reached
    /// them of the nearest divided purses of others, or, with none near, an equal share of the party's members; 0 when neither is known.
    /// </summary>
    public long ShareOf(DateTime at, long purse)
    {
        double fraction = Fraction(at);
        return fraction <= 0 ? 0 : (long)Math.Round(purse * fraction, MidpointRounding.AwayFromZero);
    }

    private double Fraction(DateTime at)
    {
        if (_divided.Length > 0)
        {
            int i = FirstDividedAtOrAfter(at);
            long purse = 0, share = 0;
            for (int k = Math.Max(0, i - Neighbours), end = Math.Min(_divided.Length, i + Neighbours); k < end; k++)
            {
                if ((_divided[k].Time - at).Duration() > Reach) continue;
                purse += _divided[k].Purse;
                share += _divided[k].Share;
            }
            if (purse > 0) return (double)share / purse;
        }
        var p = PartyAt(at);
        return p.In && p.Count >= 2 ? 1.0 / p.Count : 0;
    }

    private (bool In, int Count) PartyAt(DateTime at)
    {
        int lo = 0, hi = _party.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (_party[mid].At <= at) lo = mid + 1; else hi = mid;
        }
        return lo == 0 ? (false, 0) : (_party[lo - 1].In, _party[lo - 1].Count);
    }

    private bool HasEvidence(DateTime at)
    {
        int i = FirstDividedAtOrAfter(at - Evidence);
        return i < _divided.Length && _divided[i].Time <= at + Evidence;
    }

    private int FirstDividedAtOrAfter(DateTime at)
    {
        int lo = 0, hi = _divided.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (_divided[mid].Time < at) lo = mid + 1; else hi = mid;
        }
        return lo;
    }
}
