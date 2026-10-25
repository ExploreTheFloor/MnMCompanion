using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using MnMCompanion.Loot;
using MnMCompanion.Update;

namespace MnMCompanion;

/// <summary>
/// The map of the zone the ledger says the character is in, from the community atlas (MnM Atlas, by ocombe, built on the community's maps).
/// The ledger names a zone on every row, so the map follows the character from zone to zone as soon as something is written in the new one.
/// It is shown in a web view, which loads the atlas's own page for that one map, so the page contacts mnmatlas.com: it sees which zone the player
/// is in and their address. Because of that it stays off until the player says yes (<see cref="Settings.MapsOnline"/>); until then, and when
/// the web view isn't available, there is a button that opens the same page in their browser. The ledger has no positions, so there is no
/// "you are here"; the atlas's own markers (vendors, places, exits) are the point of it.
/// </summary>
internal sealed class MapPanel : Border, IMiniPage, IDisposable
{
    private static readonly string AtlasHost = new Uri(Atlas.Origin).Host;

    private readonly LootStore _store;
    private readonly SessionScope _session;
    private readonly Settings _settings;
    private readonly TextBlock _status;

    private readonly TextBlock _title = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Palette.Text, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _sub = new() { FontSize = 11, Foreground = Palette.Muted, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Button _open = new() { Style = (Style)Ui.Theme["TextButton"], Content = "Open in Atlas  ↗", Padding = new Thickness(9, 4, 9, 4), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly WebView2 _web = new() { Visibility = Visibility.Collapsed, DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x0F, 0x11, 0x15) };
    private readonly StackPanel _message = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8) };

    private string? _openUrl;
    private string? _shownUrl;
    private string _seen = "";
    private bool _ready, _failed;

    public MapPanel(LootStore store, SessionScope session, Settings settings, TextBlock status)
    {
        _store = store;
        _session = session;
        _settings = settings;
        _status = status;
        Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x11, 0x15));
        Padding = new Thickness(12, 10, 12, 8);
        _open.ToolTip = "Open this zone's page of the community atlas in your browser";
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

        var map = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        map.Children.Add(_web);
        map.Children.Add(_message);

        var root = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        DockPanel.SetDock(credit, Dock.Bottom);
        root.Children.Add(head);
        root.Children.Add(credit);
        root.Children.Add(map);
        Child = root;
        session.Changed += () => { if (IsVisible) Refresh(); };
    }

    public FrameworkElement Element => this;

    public bool NeedsHistory => false;

    public void RefreshNow() => Refresh();

    public void Say(string text) => _status.Text = text;

    private string Signature() => $"{_store.CurrentZone?.Zone}|{_store.SelectionKey}|{_session.Key}|{_store.Version}|{_settings.MapsOnline}";

    public void Tick()
    {
        if (IsVisible && Signature() != _seen) Refresh();
    }

    public void Refresh()
    {
        _seen = Signature();
        _status.Text = "";
        _openUrl = null;
        _open.Visibility = Visibility.Collapsed;
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
        _open.Visibility = _openUrl is null ? Visibility.Collapsed : Visibility.Visible;
        var embed = Atlas.EmbedUrlFor(zone.Zone);
        if (embed is null) { Note($"The community atlas has no map of {name} yet."); return; }
        if (!_settings.MapsOnline) { AskFirst(); return; }
        _ = ShowMapAsync(embed);
    }

    // ------------------------------------------------------------------ what is shown instead of the map

    /// <summary>Replaces the map with a note (and, with <paramref name="more"/>, a second muted line).</summary>
    private void Note(string text, string? more = null)
    {
        _web.Visibility = Visibility.Collapsed;
        _message.Visibility = Visibility.Visible;
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

    private async Task ShowMapAsync(string url)
    {
        try
        {
            if (_failed) { Note("The map can't be shown in this window.", "Use “Open in Atlas” to see it in your browser."); return; }
            _message.Visibility = Visibility.Collapsed;
            _web.Visibility = Visibility.Visible;
            if (!_ready) await StartAsync();
            if (_shownUrl != url)
            {
                _shownUrl = url;
                _web.CoreWebView2.Navigate(url);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _failed = true;
            Note("The map can't be shown in this window.", (ex is WebView2RuntimeNotFoundException ? "Windows' web view (Microsoft Edge WebView2) isn't installed. " : "") + "Use “Open in Atlas” to see it in your browser.");
        }
    }

    private async Task StartAsync()
    {
        // Its files (and the atlas's notes, which the atlas keeps in the browser) stay in this app's own folder (a test run's own, not the player's)
        var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(_store.StateRoot, "webview"));
        await _web.EnsureCoreWebView2Async(env);
        var web = _web.CoreWebView2;
        web.Settings.AreDevToolsEnabled = false;
        web.Settings.AreDefaultContextMenusEnabled = false;
        web.Settings.IsStatusBarEnabled = false;
        web.Settings.IsPasswordAutosaveEnabled = false;
        web.Settings.IsGeneralAutofillEnabled = false;
        // It shows the atlas and nothing else: a link out opens in the player's own browser
        web.NavigationStarting += (_, e) =>
        {
            if (e.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase) || IsAtlas(e.Uri)) return;
            e.Cancel = true;
            OpenExternal(e.Uri);
        };
        web.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            OpenExternal(e.Uri);
        };
        _ready = true;
    }

    private static bool IsAtlas(string uri) => Uri.TryCreate(uri, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && u.Host.Equals(AtlasHost, StringComparison.OrdinalIgnoreCase);

    private static void OpenExternal(string? uri)
    {
        if (uri is null || !Uri.TryCreate(uri, UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp)) return;
        try { Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    public void Dispose() => _web.Dispose();
}
