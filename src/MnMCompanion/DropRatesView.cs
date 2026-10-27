using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MnMCompanion.Loot;
using MnMCompanion.Update;

namespace MnMCompanion;

/// <summary>
/// The Loot page's "Drop rates" tab: how often each item comes off each kind of monster, by monster (pick one to see its items) or
/// by item (pick one to see where it comes from). The shares are of looted corpses, with a 95% range; monsters with few
/// corpses are starred. Holds its own place, so sorting a table or switching tabs doesn't send the reader back to the list.
/// </summary>
internal sealed class DropRatesView : UserControl
{
    private readonly IReadOnlyList<MobDrops> _mobs;
    private readonly IReadOnlyList<DroppedItem> _items;
    private readonly ItemValues _values;
    private bool _byItem;
    private MobDrops? _mob;
    private DroppedItem? _item;
    private string _search = "";   // typed into the box over the By item list; kept when an item is opened and the reader comes back

    // What the community's players have for the monster or item on show (asked for once each, only if the player shares their own totals)
    private readonly Dictionary<string, CommunityDrops?> _communityDrops = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CommunityPrices?> _communityPrices = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _asking = new(StringComparer.OrdinalIgnoreCase);

    public DropRatesView(IReadOnlyList<MobDrops> mobs, ItemValues values)
    {
        _mobs = mobs;
        _items = DropRates.ByItem(mobs);
        _values = values;
        Build();
    }

    /// <summary>Opens one monster's drop table (for a picture); nothing happens if it isn't there.</summary>
    public void Open(string mob)
    {
        if (_mobs.FirstOrDefault(m => m.Mob.Equals(mob, StringComparison.OrdinalIgnoreCase)) is not { } found) return;
        _byItem = false;
        _item = null;
        _mob = found;
        Build();
    }

    private void Build()
    {
        var root = new StackPanel();
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        if (_mob is not null || _item is not null)
        {
            var back = new Button { Content = _byItem ? "←  All items" : "←  All monsters", Style = (Style)Ui.Theme["TextButton"], Padding = new Thickness(10, 4, 10, 4), HorizontalAlignment = HorizontalAlignment.Left };
            back.Click += (_, _) => { _mob = null; _item = null; Build(); };
            top.Children.Add(back);
        }
        else
        {
            var seg = new Border { CornerRadius = new CornerRadius(8), Background = (Brush)Ui.Theme["Subtle"], Padding = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Left };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var (text, byItem) in new[] { ("By monster", false), ("By item", true) })
            {
                var rb = new RadioButton { Content = new TextBlock { Text = text, Margin = new Thickness(12, 0, 12, 0) }, Style = (Style)Ui.Theme["Segment"], GroupName = "DropMode", IsChecked = byItem == _byItem };
                bool mode = byItem;
                rb.Checked += (_, _) => { if (_byItem != mode) { _byItem = mode; Build(); } };
                row.Children.Add(rb);
            }
            seg.Child = row;
            top.Children.Add(seg);
        }
        root.Children.Add(top);

        root.Children.Add(new TextBlock
        {
            Text = "The share of looted corpses an item came off, over every day in this ledger. A corpse nobody opened leaves no trace, an item left behind " +
                   "isn't recorded (so shares are minimums), and a corpse two players looted counts twice. * = fewer than " + DropRates.FewCorpses + " corpses: a rough guess.",
            FontSize = 11.5, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, MaxWidth = 820, Margin = new Thickness(8, 0, 0, 10),
        });

        root.Children.Add(_mob is not null ? MobDetail(_mob) : _item is not null ? ItemDetail(_item) : _byItem ? ItemList() : MobList());
        Content = root;
    }

    private static string Count(int corpses) => corpses.ToString("N0", CultureInfo.CurrentCulture) + (corpses < DropRates.FewCorpses ? " *" : "");

    private static string Range(double low, double high) => $"{low * 100:0}–{high * 100:0}%";

    private string Each(string item) =>
        _values.Of(item) is { Known: true } v ? (v.Estimated ? "~" : "") + LootSummary.Coin(v.Each) : "—";

    private FrameworkElement MobList()
    {
        if (_mobs.Count == 0) return Nothing();
        return Table.Build("drop-mobs", _mobs, new Col<MobDrops>[]
        {
            new("Monster", 0, m => m.Mob, m => m.Mob, Right: false),
            new("Zone", 104, m => m.Zone, m => m.Zone, Right: false),
            new("Corpses", 74, m => Count(m.Corpses), m => m.Corpses, Tip: "Looted corpses. * = too few for the shares to mean much"),
            new("Coin each", 100, m => m.Copper > 0 ? LootSummary.Coin((long)m.CopperEach) : "—", m => m.CopperEach),
            new("Items each", 90, m => m.ItemsEach.ToString("0.0", CultureInfo.CurrentCulture), m => m.ItemsEach),
            new("Likeliest item", 215, m => m.Drops.Count == 0 ? "—" : $"{m.Drops[0].Item}  {Fmt.Pct(m.Drops[0].Share)}", m => m.Drops.Count == 0 ? 0 : m.Drops[0].Share, Right: false),
        }, Build, defaultSort: 2, bar: m => m.Corpses, color: _ => Palette.Taken, click: m => { _mob = m; Build(); });
    }

    private FrameworkElement MobDetail(MobDrops m)
    {
        var panel = new StackPanel();
        panel.Children.Add(Title(m.Mob, m.Zone));
        panel.Children.Add(Line($"{m.Corpses:N0} looted corpses · {(m.Copper > 0 ? LootSummary.Coin((long)m.CopperEach) + " coin each" : "no coin")} · {m.ItemsEach:0.0} items each"));
        if (m.Few) panel.Children.Add(Line($"Only {m.Corpses:N0} corpses looted: the shares below are rough.", Palette.Gold));
        var community = CommunityZone(m);
        var theirs = community?.Items.ToDictionary(i => i.Item, i => i, StringComparer.OrdinalIgnoreCase);
        if (community is not null)
            panel.Children.Add(Line($"Community: {community.Contributors:N0} players looted this monster ({community.Corpses:N0} corpses between them). The Community column is the middle of their shares, so one player can't swing it."));
        var columns = new List<Col<DropItem>>
        {
            new("Item", 0, d => d.Item, d => d.Item, Right: false),
            new("Share", 80, d => Fmt.Pct(d.Share), d => d.Share, Tip: "Of this monster's looted corpses, how many gave the item"),
            new("Range", 90, d => Range(d.Low, d.High), d => d.Low, Tip: "The 95% range the true share lies in: wide when there are few corpses"),
            new("Corpses", 110, d => $"{d.Corpses:N0} of {m.Corpses:N0}", d => d.Corpses),
            new("Units", 70, d => $"{d.Units:N0}", d => d.Units),
            new("Each", 90, d => Each(d.Item), d => _values.Of(d.Item).Each, Tip: "What a vendor pays for one"),
        };
        if (theirs is not null)
            columns.Insert(2, new Col<DropItem>("Community", 96, d => theirs.TryGetValue(d.Item, out var c) ? Fmt.Pct(c.Share) : "—", d => theirs.TryGetValue(d.Item, out var c) ? c.Share : -1.0,
                Tip: "The middle of the community players' shares of this monster's corpses that gave the item. — = no player has it"));
        panel.Children.Add(Table.Build(theirs is null ? "drop-mob-items" : "drop-mob-items-community", m.Drops, columns.ToArray(), Build, defaultSort: 1, bar: d => d.Share, color: _ => Palette.Heal));
        return panel;
    }

    private FrameworkElement ItemList()
    {
        if (_items.Count == 0) return Nothing();
        return Table.Searchable(_items, i => i.Item, "Search items by name", () => _search, s => _search = s, rows => Table.Build("drop-items", rows, new Col<DroppedItem>[]
        {
            new("Item", 0, i => i.Item, i => i.Item, Right: false),
            new("Corpses", 78, i => $"{i.Corpses:N0}", i => i.Corpses, Tip: "Looted corpses it came off, in all"),
            new("Monsters", 80, i => $"{i.Sources.Count:N0}", i => i.Sources.Count, Tip: "Kinds of monster it has come off"),
            new("Likeliest source", 300, i => $"{i.Sources[0].Mob}  {Fmt.Pct(i.Sources[0].Share)}{(i.Sources[0].Few ? " *" : "")}", i => i.Sources[0].Share, Right: false),
            new("Each", 90, i => Each(i.Item), i => _values.Of(i.Item).Each, Tip: "What a vendor pays for one"),
        }, Build, defaultSort: 1, bar: i => i.Corpses, color: _ => Palette.You, click: i => { _item = i; Build(); }));
    }

    private FrameworkElement ItemDetail(DroppedItem item)
    {
        var panel = new StackPanel();
        panel.Children.Add(Title(item.Item, "comes off"));
        panel.Children.Add(Line($"{item.Corpses:N0} looted corpses in all · {item.Sources.Count:N0} kinds of monster · a vendor pays {Each(item.Item)}"));
        if (CommunityPrice(item.Item) is { } sold)
            panel.Children.Add(Line($"Community: vendors pay a middle price of {LootSummary.Coin(sold.Median)} for one ({LootSummary.Coin(sold.Low)} to {LootSummary.Coin(sold.High)} between players), from {sold.Contributors:N0} players."));
        panel.Children.Add(Table.Build("drop-item-sources", item.Sources, new Col<DropSource>[]
        {
            new("Monster", 0, s => s.Mob, s => s.Mob, Right: false),
            new("Zone", 110, s => s.Zone, s => s.Zone, Right: false),
            new("Share", 80, s => Fmt.Pct(s.Share), s => s.Share, Tip: "Of that monster's looted corpses, how many gave this item"),
            new("Range", 90, s => Range(s.Low, s.High), s => s.Low, Tip: "The 95% range the true share lies in: wide when there are few corpses"),
            new("Corpses", 120, s => $"{s.Corpses:N0} of {s.OfCorpses:N0}{(s.Few ? " *" : "")}", s => s.Corpses),
        }, Build, defaultSort: 2, bar: s => s.Share, color: _ => Palette.Heal));
        return panel;
    }

    /// <summary>What the community has for this monster in its zone, or null (not sharing, nobody has it yet, or still asking: the view is built again when the answer arrives).</summary>
    private CommunityDropZone? CommunityZone(MobDrops m)
    {
        if (CommunityAccess.Reader is not { Enabled: true } reader) return null;
        if (_communityDrops.TryGetValue(m.Mob, out var answer)) return answer?.Zones.FirstOrDefault(z => z.Zone.Equals(m.Zone, StringComparison.OrdinalIgnoreCase));
        if (_asking.Add("drop:" + m.Mob)) _ = AskDrops(reader, m);
        return null;
    }

    private async Task AskDrops(CommunityReader reader, MobDrops m)
    {
        _communityDrops[m.Mob] = await reader.DropsAsync(m.Mob);
        if (ReferenceEquals(_mob, m)) Build();
    }

    /// <summary>What the community says vendors pay for one of this item, or null.</summary>
    private CommunityPrice? CommunityPrice(string item)
    {
        if (CommunityAccess.Reader is not { Enabled: true } reader) return null;
        if (_communityPrices.TryGetValue(item, out var answer)) return answer?.Prices.FirstOrDefault(p => p.Direction == "sold");
        if (_asking.Add("price:" + item)) _ = AskPrice(reader, item);
        return null;
    }

    private async Task AskPrice(CommunityReader reader, string item)
    {
        _communityPrices[item] = await reader.PricesAsync(item);
        if (_item is not null && _item.Item.Equals(item, StringComparison.OrdinalIgnoreCase)) Build();
    }

    private static FrameworkElement Title(string name, string sub)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 2) };
        row.Children.Add(new TextBlock { Text = name, FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Palette.Text, FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI") });
        if (sub.Length > 0) row.Children.Add(new TextBlock { Text = "   " + sub, FontSize = 12, Foreground = Palette.Muted, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 3) });
        return row;
    }

    private static TextBlock Line(string text, Brush? colour = null) =>
        new() { Text = text, FontSize = 12, Foreground = colour ?? Palette.Muted, Margin = new Thickness(8, 0, 0, 8), TextWrapping = TextWrapping.Wrap };

    private static TextBlock Nothing() =>
        new() { Text = "Nothing looted yet.", Foreground = Palette.Muted, Margin = new Thickness(8, 10, 0, 6), FontSize = 12 };
}
