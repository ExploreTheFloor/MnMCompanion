using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using MnMCompanion.Loot;
using MnMCompanion.Update;

namespace MnMCompanion;

/// <summary>
/// The map of the zone the ledger says the character is in, from the community atlas (MnM Atlas, by ocombe, built on the community's maps).
/// The ledger names a zone on every row, so the map follows the character from zone to zone as soon as something is written in the new one.
/// It is shown in a web view (the system's own: WebKitGTK on Linux), which loads the atlas's own page for that one map, so the page contacts mnmatlas.com: it
/// sees which zone the player is in and their address. Because of that it stays off until the player says yes (<see cref="Settings.MapsOnline"/>); until then,
/// and when the web view isn't available, there is a button that opens the same page in their browser. The ledger has no positions, so there is no
/// "you are here"; the atlas's own markers (vendors, places, exits) are the point of it.
/// </summary>
internal sealed class MapPanel : Border, IMiniPage, IDisposable
{
    private static readonly string AtlasHost = new Uri(Atlas.Origin).Host;

    private readonly LootStore _store;
    private readonly SessionScope _session;
    private readonly Settings _settings;
    private readonly TextBlock _status;

    private readonly TextBlock _title = new() { FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Palette.Text, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _sub = new() { FontSize = 11, Foreground = Palette.Muted, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Button _open = new Button { Content = "Open in Atlas  ↗", Padding = new Thickness(9, 4, 9, 4), VerticalAlignment = VerticalAlignment.Center, IsVisible = false }.Styled("text");
    private readonly Grid _map = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly StackPanel _message = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8) };
    private NativeWebView? _web;

    private string? _openUrl;
    private string? _shownUrl;
    private string _seen = "";
    private bool _failed;

    public MapPanel(LootStore store, SessionScope session, Settings settings, TextBlock status)
    {
        _store = store;
        _session = session;
        _settings = settings;
        _status = status;
        Background = Ui.Window;
        Padding = new Thickness(12, 10, 12, 8);
        _open.Tip("Open this zone's page of the community atlas in your browser");
        _open.Click += (_, _) => OpenExternal(_openUrl);

        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(_title);
        titles.Children.Add(_sub);
        var head = new DockPanel();
        DockPanel.SetDock(_open, Dock.Right);
        head.Children.Add(_open);
        head.Children.Add(titles);

        var credit = new TextBlock
        {
            Text = "Map: MnM Atlas by ocombe, from the community's maps (CC BY-SA 4.0). A fan atlas, not an official map.",
            FontSize = 10, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
        };

        _map.Children.Add(_message);

        var root = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        DockPanel.SetDock(credit, Dock.Bottom);
        root.Children.Add(head);
        root.Children.Add(credit);
        root.Children.Add(_map);
        Child = root;
        session.Changed += () => { if (this.OnScreen()) Refresh(); };
    }

    public Control Element => this;

    public bool NeedsHistory => false;

    public void RefreshNow() => Refresh();

    public void Say(string text) => _status.Text = text;

    private string Signature() => $"{_store.CurrentZone?.Zone}|{_store.SelectionKey}|{_session.Key}|{_store.Version}|{_settings.MapsOnline}";

    public void Tick()
    {
        if (this.OnScreen() && Signature() != _seen) Refresh();
    }

    public void Refresh()
    {
        _seen = Signature();
        _status.Text = "";
        _openUrl = null;
        _open.IsVisible = false;
        if (_store.Selected is null) { Note("No Monsters and Memories ledger found."); return; }
        if (_store.CurrentZone is not { } zone)
        {
            _title.Text = "";
            _sub.Text = "";
            Note("The ledger hasn't named a zone yet.", "It does as soon as this character loots, sells or gathers something, and the map follows by itself.");
            return;
        }

        var name = ZoneMaps.DisplayName(zone.Zone);
        _title.Text = name;
        var events = _store.Events(_session.Day, _session.ActiveSession()).Where(e => ZoneMaps.Normalize(e.Zone) == ZoneMaps.Normalize(zone.Zone)).ToList();
        var here = LootSummary.Totals(events);
        var seen = zone.At.Date == DateTime.Today ? zone.At.ToString("HH:mm") : zone.At.ToString("ddd d MMM HH:mm");
        _sub.Text = $"last in this zone at {seen}" + (events.Count > 0 ? $" · looted here: {LootPanel.Plural(here.Mobs, "mob")}, {LootPanel.Plural(here.Items, "item")}" : "");

        _openUrl = Atlas.UrlFor(zone.Zone);
        _open.IsVisible = _openUrl is not null;
        var embed = Atlas.EmbedUrlFor(zone.Zone);
        if (embed is null) { Note($"The community atlas has no map of {name} yet."); return; }
        if (!_settings.MapsOnline) { AskFirst(); return; }
        ShowMap(embed);
    }

    // ------------------------------------------------------------------ what is shown instead of the map

    /// <summary>Replaces the map with a note (and, with <paramref name="more"/>, a second muted line).</summary>
    private void Note(string text, string? more = null)
    {
        if (_web is not null) _web.IsVisible = false;
        _message.IsVisible = true;
        _message.Children.Clear();
        _message.Children.Add(LootPanel.Line(text, Palette.Text));
        if (more is not null) _message.Children.Add(LootPanel.Line(more, Palette.Muted));
    }

    /// <summary>The first time, say what showing the map does before doing it.</summary>
    private void AskFirst()
    {
        Note("The map comes live from the community atlas.",
            "Showing it loads the atlas's website (mnmatlas.com), so that site sees which zone you are in and your internet address, just as if you opened it in a browser. Nothing from your ledger is sent.");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(LootPanel.TextAction("Show the map", () => { _settings.MapsOnline = true; _settings.Save(); Refresh(); }));
        if (_openUrl is not null) buttons.Children.Add(LootPanel.TextAction("Open it in my browser instead", () => OpenExternal(_openUrl)));
        _message.Children.Add(buttons);
    }

    // ------------------------------------------------------------------ the web view

    private void ShowMap(string url)
    {
        if (_failed) { Note("The map can't be shown in this window.", "Use “Open in Atlas” to see it in your browser."); return; }
        try
        {
            _web ??= MakeWebView();
            _message.IsVisible = false;
            _web.IsVisible = true;
            if (_shownUrl != url)
            {
                _shownUrl = url;
                _web.Navigate(new Uri(url));
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _failed = true;
            Note("The map can't be shown in this window.", (OperatingSystem.IsLinux() ? "It needs WebKitGTK (the webkit2gtk-4.1 package). " : "") + "Use “Open in Atlas” to see it in your browser.");
        }
    }

    private NativeWebView MakeWebView()
    {
        var web = new NativeWebView { Background = Ui.Window };
        web.EnvironmentRequested += (_, e) =>
        {
            e.EnableDevTools = false;
            // Its files (and the atlas's notes, which the atlas keeps in the browser) stay in this app's own folder (a test run's own, not the player's)
            if (e is GtkWebViewEnvironmentRequestedEventArgs gtk)
            {
                var folder = Path.Combine(_store.StateRoot, "webview");
                gtk.BaseDataDirectory = Path.Combine(folder, "data");
                gtk.BaseCacheDirectory = Path.Combine(folder, "cache");
            }
        };
        // It shows the atlas and nothing else: a link out opens in the player's own browser
        web.NavigationStarted += (_, e) =>
        {
            if (e.Request is not { } uri || uri.Scheme == "about" || IsAtlas(uri)) return;
            e.Cancel = true;
            OpenExternal(uri.AbsoluteUri);
        };
        web.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            OpenExternal(e.Request?.AbsoluteUri);
        };
        _map.Children.Insert(0, web);
        return web;
    }

    private static bool IsAtlas(Uri u) => u.Scheme == Uri.UriSchemeHttps && u.Host.Equals(AtlasHost, StringComparison.OrdinalIgnoreCase);

    private static void OpenExternal(string? uri)
    {
        if (uri is null || !Uri.TryCreate(uri, UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp)) return;
        Ui.Open(u.AbsoluteUri);
    }

    public void Dispose()
    {
        if (_web is not null) _map.Children.Remove(_web);
    }
}
