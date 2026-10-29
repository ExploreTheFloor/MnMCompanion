using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MnMCompanion.Loot;
using MnMCompanion.Recipes;
using MnMCompanion.Update;

namespace MnMCompanion;

/// <summary>
/// The Recipes page: every tradeskill recipe of the community wiki, one skill at a time, in order of level in stretches of 25 ("Level 26–50"), with a search box
/// over everything (what a recipe makes, its ingredients, its kind, its notes, or a range of levels such as 50-75). A row shows the level, the name and the ingredients;
/// clicking it opens the rest the wiki lists (the station, the slot, notes) and a button to copy it.
/// The recipes are not in the ledger. They come from the wiki's public tradeskills data, which the page loads by itself the first time it is shown
/// (<see cref="Settings.RecipesOnline"/>, on unless the player switched it off in the ⓘ menu), keeps on this PC and asks for again, when the page is opened, once the kept copy is a day old.
/// A level has a colour by how far into its skill it is (<see cref="RecipeColors"/>) and the words a search found are marked in the rows.
/// A picture-taking run reads a file instead (<c>--recipes</c>) and never asks the wiki.
/// </summary>
internal sealed class RecipesPanel : Border, IMiniPage
{
    /// <summary>A search across every skill shows this many rows at most; the rest is "narrow the search".</summary>
    private const int MaxSearchRows = 150;

    /// <summary>A long list is drawn in pieces so the page never stops to draw it: this many elements at once, then this many more each time the window has a moment.</summary>
    private const int FirstBatch = 40, NextBatch = 40;

    private readonly Settings _settings;
    private readonly TextBlock _status;
    private readonly string? _file;

    private readonly TextBlock _sub = new() { FontSize = 11, Foreground = Palette.Muted, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Button _update = new() { Style = (Style)Ui.Theme["IconButton"], Content = "", FontFamily = (FontFamily)Ui.Theme["Icons"], Width = 30, Height = 26, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly TextBox _search = new() { Style = (Style)Ui.Theme["Field"], FontSize = 12.5, Padding = new Thickness(0, 2, 22, 2), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock _hint = new() { Text = "Search recipes, ingredients, levels (50-75)", Foreground = Palette.Muted, FontSize = 12, Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
    private readonly Button _clear = new() { Style = (Style)Ui.Theme["IconButton"], Content = "", FontFamily = (FontFamily)Ui.Theme["Icons"], Width = 24, Height = 22, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 3, 0), Visibility = Visibility.Collapsed, ToolTip = "Clear the search" };
    private readonly Button _skillButton = new() { Style = (Style)Ui.Theme["TextButton"], HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 5, 10, 5), FontSize = 12.5, FontWeight = FontWeights.SemiBold, Focusable = false };
    private readonly FrameworkElement _controls;
    private readonly StackPanel _body = new() { Margin = new Thickness(0, 6, 0, 6) };
    private readonly ScrollViewer _scroll;
    private readonly DispatcherTimer _typing;

    /// <summary>Which stretches of levels the player opened or closed (by skill and title). One that was never touched is open: a skill reads from its lowest level to its highest.</summary>
    private readonly Dictionary<string, bool> _bands = [];
    private readonly HashSet<object> _open = new(ReferenceEqualityComparer.Instance);

    private RecipeBook? _book;
    private DateTime _fetchedUtc;
    private bool _cacheRead;
    private string? _skill;
    private string _query = "";
    private bool _fetching;
    private DateTime _lastTryUtc = DateTime.MinValue;
    private string _failure = "";
    private string _seen = "";

    /// <summary>The rows not yet drawn (see <see cref="FirstBatch"/>), and which drawing of the page they belong to: a newer drawing makes an older one's rows void.</summary>
    private readonly List<Func<FrameworkElement>> _later = [];
    private int _generation;

    /// <summary>Where the list was scrolled to when a stretch of levels was opened or closed: put back once every row is drawn, so the list doesn't jump.</summary>
    private double? _restoreOffset;

    /// <param name="file">A recipe document to read instead of the kept copy and the wiki (for a picture or a test); null for the real thing.</param>
    public RecipesPanel(Settings settings, TextBlock status, string? file = null)
    {
        _settings = settings;
        _status = status;
        _file = file;
        _skill = string.IsNullOrWhiteSpace(settings.RecipeSkill) ? null : settings.RecipeSkill;
        Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x11, 0x15));
        Padding = new Thickness(12, 10, 12, 8);

        var title = new TextBlock { Text = "Recipes", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Palette.Text };
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(title);
        titles.Children.Add(_sub);
        _update.ToolTip = "Fetch the newest list from the community wiki now";
        _update.Click += (_, _) => _ = FetchAsync();
        var head = new DockPanel();
        DockPanel.SetDock(_update, Dock.Right);
        head.Children.Add(_update);
        head.Children.Add(titles);

        var searchBox = new Grid();
        Grid.SetColumn(searchBox, 1);
        searchBox.Children.Add(_search);
        searchBox.Children.Add(_hint);
        searchBox.Children.Add(_clear);
        _search.ToolTip = "Every word has to be in the recipe: what it makes, an ingredient, its skill or kind, a note. Any order, any case, part of a word is enough. Two numbers with a dash (50-75) are a range of levels.";
        _typing = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(180) };
        _typing.Tick += (_, _) => { _typing.Stop(); Typed(); };
        _search.TextChanged += (_, _) =>
        {
            _hint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            _clear.Visibility = _search.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            _typing.Stop();
            _typing.Start();
        };
        _clear.Click += (_, _) => { _search.Text = ""; _search.Focus(); };

        // The skill picker and the search box share a row: the list gets the height
        _skillButton.ToolTip = "Which tradeskill to show";
        _skillButton.Click += (_, _) => OpenSkillMenu();
        _skillButton.Margin = new Thickness(0, 0, 6, 0);
        _skillButton.VerticalAlignment = VerticalAlignment.Stretch;
        var controls = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        controls.Children.Add(_skillButton);
        controls.Children.Add(searchBox);
        _controls = controls;

        var credit = new TextBlock
        {
            Text = "Recipes: the Monsters & Memories community wiki (a fan wiki; may be incomplete). A level is the skill the wiki lists the recipe as trivial at.",
            FontSize = 10, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
        };

        _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _body };
        var top = new StackPanel();
        top.Children.Add(head);
        top.Children.Add(_controls);
        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(credit, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(credit);
        root.Children.Add(_scroll);
        Child = root;
    }

    public FrameworkElement Element => this;

    public bool NeedsHistory => false;

    public void Say(string text) => _status.Text = text;

    /// <summary>Called when the page is shown and whenever the ledger changes (nothing here depends on it): it redraws only if something it shows has changed, so the list stays where the player scrolled it.</summary>
    public void Refresh()
    {
        ReadKept();
        if (_body.Children.Count == 0 || Signature() != _seen) Build();
        AutoFetch();
    }

    /// <summary>Draws now, every row, and asks nobody: for a picture.</summary>
    public void RefreshNow()
    {
        ReadKept();
        Build();
        Flush();
    }

    public void Tick()
    {
        if (!IsVisible) return;
        if (Signature() != _seen) Build();
        AutoFetch();
    }

    /// <summary>For a picture: shows a skill and a search without typing (a skill name that isn't one shows all of them).</summary>
    public void Show(string? skill, string? search)
    {
        ReadKept();
        _skill = skill is { Length: > 0 } && _book?.SkillNamed(skill) is { } s ? s.Name : null;
        _query = search ?? "";
        _search.Text = _query;
        _typing.Stop();
        Build();
        Flush();
    }

    private string Signature() => $"{_settings.RecipesOnline}|{_book?.Recipes.Count}|{_fetching}|{_failure}";

    // ------------------------------------------------------------------ the list

    private void ReadKept()
    {
        if (_cacheRead) return;
        _cacheRead = true;
        if (RecipeCache.Load(_file) is { } kept)
        {
            _book = kept.Book;
            _fetchedUtc = kept.FetchedUtc;
        }
        ForgetUnknownSkill();
    }

    /// <summary>The skill remembered from last time may not be in this list (or be spelt another way): keep it only if it is.</summary>
    private void ForgetUnknownSkill()
    {
        if (_skill is null) return;
        _skill = _book?.SkillNamed(_skill)?.Name;
    }

    /// <summary>Asks the wiki when the player has said it may be asked and there is no list yet or it is a day old (and not again within the hour of a try).</summary>
    private void AutoFetch()
    {
        if (_file is not null || !_settings.RecipesOnline || _fetching) return;
        if (_book is not null && !RecipeCache.IsStale(_fetchedUtc)) return;
        if (DateTime.UtcNow - _lastTryUtc < TimeSpan.FromHours(1)) return;
        _ = FetchAsync();
    }

    private async Task FetchAsync()
    {
        if (_fetching || _file is not null) return;
        _fetching = true;
        _lastTryUtc = DateTime.UtcNow;
        _failure = "";
        Build();
        try
        {
            using var client = new RecipeWikiClient(AppVersion.Current());
            var result = await client.FetchAsync();
            if (result.Ok && RecipeBook.TryParse(result.Json) is { } book)
            {
                if (!_settings.ReadOnly) RecipeCache.Save(result.Json);
                _book = book;
                _fetchedUtc = DateTime.UtcNow;
                ForgetUnknownSkill();
                _status.Text = $"Loaded {book.Recipes.Count:N0} recipes from the wiki.";
            }
            else _failure = result.Ok ? "The wiki's answer wasn't a recipe list." : result.Message;
        }
        finally { _fetching = false; }
        Build();
    }

    // ------------------------------------------------------------------ drawing

    /// <summary>Redraws the page: the first rows now, the rest as the window has time (an older drawing's unfinished rows are dropped).</summary>
    private void Build()
    {
        _generation++;
        _later.Clear();
        Draw();
        if (_later.Count > 0) ScheduleMore();
        else RestoreScroll();
    }

    /// <summary>Redraws after a stretch of levels was opened or closed, and puts the list back where it was scrolled to.</summary>
    private void Rebuild()
    {
        _restoreOffset = _scroll.VerticalOffset;
        Build();
    }

    private void RestoreScroll()
    {
        if (_restoreOffset is not { } offset) return;
        _restoreOffset = null;
        _scroll.UpdateLayout();
        _scroll.ScrollToVerticalOffset(offset);
    }

    /// <summary>Adds a row to the page: at once while the page is short, otherwise to be drawn a little later.</summary>
    private void Emit(Func<FrameworkElement> make)
    {
        if (_later.Count == 0 && _body.Children.Count < FirstBatch) _body.Children.Add(make());
        else _later.Add(make);
    }

    private void ScheduleMore()
    {
        int generation = _generation;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => AddMore(generation));
    }

    private void AddMore(int generation)
    {
        if (generation != _generation || _later.Count == 0) return;
        int take = Math.Min(NextBatch, _later.Count);
        for (int i = 0; i < take; i++) _body.Children.Add(_later[i]());
        _later.RemoveRange(0, take);
        if (_later.Count > 0) ScheduleMore();
        else RestoreScroll();
    }

    /// <summary>Draws every row still waiting, now.</summary>
    private void Flush()
    {
        _generation++;   // a batch already scheduled has nothing left to do
        foreach (var make in _later) _body.Children.Add(make());
        _later.Clear();
        RestoreScroll();
    }

    private void Draw()
    {
        _seen = Signature();
        _body.Children.Clear();
        _update.Visibility = _file is null && _settings.RecipesOnline ? Visibility.Visible : Visibility.Collapsed;
        _update.IsEnabled = !_fetching;
        _controls.Visibility = _book is null ? Visibility.Collapsed : Visibility.Visible;

        if (_book is not { } book)
        {
            _sub.Text = _fetching ? "loading from the community wiki…" : "";
            ShowNoList();
            return;
        }
        _sub.Text = $"{book.Recipes.Count:N0} recipes in {book.Skills.Count} skills · community wiki · "
                    + (_fetching ? "updating…" : _file is null ? $"loaded {RecipeCache.Age(_fetchedUtc)}" : "from a file");
        _skillButton.Content = SkillPicker(_skill);
        if (_failure.Length > 0) _body.Children.Add(Note("Couldn't update the list: " + _failure + " The copy kept on this PC is shown.", Palette.Taken));

        bool searching = NameSearch.Words(_query).Length > 0;
        _found = NameSearch.Words(_query).Where(w => !LevelWord.IsMatch(w)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (_skill is null && !searching) ShowSkills(book);
        else if (_skill is not null) ShowSkill(book, _skill, searching);
        else ShowEverywhere(book);
    }

    /// <summary>No list yet: it is being loaded, or loading failed, or the player switched loading off (the way back is here and in the ⓘ menu).</summary>
    private void ShowNoList()
    {
        var box = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 24, 8, 8) };
        if (_fetching)
        {
            box.Children.Add(LootPanel.Line("Loading the recipes from the community wiki…", Palette.Text));
        }
        else if (!_settings.RecipesOnline)
        {
            box.Children.Add(LootPanel.Line("Loading the recipes is switched off.", Palette.Text));
            box.Children.Add(LootPanel.Line(
                "Turning it on loads the community wiki's public tradeskills list (monstersandmemories.wiki) and keeps it on this PC, refreshing it at most once a day. That site sees your internet address, as if you opened it in a browser; nothing from your ledger or your game is sent.", Palette.Muted));
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
            buttons.Children.Add(LootPanel.TextAction("Turn it on", () => { _settings.RecipesOnline = true; _settings.Save(); _ = FetchAsync(); }));
            buttons.Children.Add(LootPanel.TextAction("Open the wiki instead", () => OpenWiki()));
            box.Children.Add(buttons);
        }
        else
        {
            box.Children.Add(LootPanel.Line(_failure.Length > 0 ? "Couldn't load the recipes: " + _failure : "The recipe list isn't loaded yet.", _failure.Length > 0 ? Palette.Taken : Palette.Text));
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
            buttons.Children.Add(LootPanel.TextAction("Try again", () => _ = FetchAsync()));
            buttons.Children.Add(LootPanel.TextAction("Open the wiki instead", () => OpenWiki()));
            box.Children.Add(buttons);
        }
        _body.Children.Add(box);
    }

    /// <summary>All skills, no search: one row each, to choose from.</summary>
    private void ShowSkills(RecipeBook book)
    {
        _body.Children.Add(Note("Choose a skill, or search every recipe above.", Palette.Muted));
        foreach (var skill in book.Skills)
        {
            var s = skill;
            var color = RecipeColors.OfSkill(s.Name);
            var row = new ClickRow { Margin = new Thickness(0, 2, 0, 2), Background = Brushes.Transparent, Cursor = Cursors.Hand, Activate = () => Choose(s.Name) };
            System.Windows.Automation.AutomationProperties.SetName(row, s.Name);
            var normal = RecipeColors.Solid(color, 0x16);
            var back = new Border { CornerRadius = new CornerRadius(7), Background = normal };
            row.Children.Add(back);
            row.Children.Add(new Border { Width = 3, CornerRadius = new CornerRadius(1.5), Background = RecipeColors.Solid(color), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 7, 0, 7) });
            var dock = new DockPanel { Margin = new Thickness(14, 6, 10, 6) };
            var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(right, Dock.Right);
            right.Children.Add(new TextBlock { Text = Plural(s.Count, "recipe"), Foreground = Palette.Text, FontSize = 12, TextAlignment = TextAlignment.Right });
            if (s.LevelRange.Length > 0) right.Children.Add(LevelRangeText(s));
            dock.Children.Add(right);
            var left = new StackPanel();
            left.Children.Add(new TextBlock { Text = s.Name, Foreground = Palette.Text, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            if (s.Station.Length > 0) left.Children.Add(new TextBlock { Text = s.Station, Foreground = RecipeColors.Label, FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis });
            dock.Children.Add(left);
            row.Children.Add(dock);
            row.MouseLeftButtonUp += (_, _) => row.Activate?.Invoke();
            Hover(row, back, normal, RecipeColors.Solid(color, 0x2E));
            _body.Children.Add(row);
        }
    }

    /// <summary>"levels 5–300", the two ends in the colours of their levels.</summary>
    private static TextBlock LevelRangeText(Skill s)
    {
        var text = new TextBlock { FontSize = 10.5, TextAlignment = TextAlignment.Right, Foreground = Palette.Muted };
        text.Inlines.Add(new System.Windows.Documents.Run("levels ") { Foreground = Palette.Muted });
        if (s.LowLevel is { } lo && s.HighLevel is { } hi && lo != hi)
        {
            text.Inlines.Add(new System.Windows.Documents.Run(lo.ToString()) { Foreground = RecipeColors.Solid(RecipeColors.OfLevel(lo)), FontWeight = FontWeights.SemiBold });
            text.Inlines.Add(new System.Windows.Documents.Run("–") { Foreground = Palette.Muted });
            text.Inlines.Add(new System.Windows.Documents.Run(hi.ToString()) { Foreground = RecipeColors.Solid(RecipeColors.OfLevel(hi)), FontWeight = FontWeights.SemiBold });
        }
        else text.Inlines.Add(new System.Windows.Documents.Run(s.LevelRange) { Foreground = RecipeColors.Solid(RecipeColors.OfLevel(s.LowLevel)), FontWeight = FontWeights.SemiBold });
        return text;
    }

    /// <summary>The skill picker's face: the skill's colour as a dot, its name, an arrow.</summary>
    private static FrameworkElement SkillPicker(string? skill)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 9, Height = 9, Margin = new Thickness(0, 1, 8, 0), VerticalAlignment = VerticalAlignment.Center,
            Fill = skill is null ? Palette.Muted : RecipeColors.Solid(RecipeColors.OfSkill(skill)),
        });
        row.Children.Add(new TextBlock { Text = skill ?? "All skills", VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = "▾", FontSize = 10, Margin = new Thickness(9, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Palette.Muted });
        return row;
    }

    /// <summary>One skill: what it needs, then its recipes in stretches of levels (the matches only, while searching).</summary>
    private void ShowSkill(RecipeBook book, string name, bool searching)
    {
        var all = book.Of(name);
        var found = RecipeSearch.Filter(all, _query);
        if (searching)
            _body.Children.Add(Note(found.Count == 0 ? $"No {name} recipe matches “{_query.Trim()}”." : $"{found.Count:N0} of {all.Count:N0} {name} recipes match.", Palette.Muted));
        if (found.Count == 0)
        {
            if (searching) _body.Children.Add(Centre(LootPanel.TextAction("Search every skill", () => Choose(null))));
            return;
        }
        var bands = RecipeLevels.Bands(found);
        string Key(LevelBand b) => name + "|" + b.Title;
        bool IsOpen(int index) => searching || !_bands.TryGetValue(Key(bands[index]), out var chosen) || chosen;

        if (!searching)
        {
            // What the skill needs, and a button to open or close every stretch at once
            var top = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
            if (bands.Count > 1)
            {
                bool allOpen = Enumerable.Range(0, bands.Count).All(IsOpen);
                var toggle = LootPanel.TextAction(allOpen ? "Close all" : "Open all", () =>
                {
                    foreach (var band in bands) _bands[Key(band)] = !allOpen;
                    Rebuild();
                });
                toggle.Margin = new Thickness(6, 2, 0, 0);
                toggle.Padding = new Thickness(9, 3, 9, 3);
                toggle.FontSize = 11;
                toggle.VerticalAlignment = VerticalAlignment.Top;
                toggle.ToolTip = "Open or close every stretch of levels";
                DockPanel.SetDock(toggle, Dock.Right);
                top.Children.Add(toggle);
            }
            if (SkillInfo(book.SkillNamed(name)!) is { } info) top.Children.Add(info);
            if (top.Children.Count > 0) _body.Children.Add(top);
        }
        if (found.Count == 1 && searching) _open.Add(found[0]);   // one answer: show all of it
        for (int i = 0; i < bands.Count; i++)
        {
            var band = bands[i];
            bool open = IsOpen(i);
            var key = Key(band);
            var accent = RecipeColors.OfLevel(band.From);
            Emit(() => BandHeader(band.Title, band.Recipes.Count, open, searching ? null : () => { _bands[key] = !open; Rebuild(); }, accent));
            if (open)
                foreach (var recipe in band.Recipes) Emit(() => RecipeRow(recipe));
        }
    }

    /// <summary>A search with no skill chosen: the matches of every skill, a heading each.</summary>
    private void ShowEverywhere(RecipeBook book)
    {
        var found = RecipeSearch.Filter(book.Recipes, _query);
        if (found.Count == 0)
        {
            _body.Children.Add(Note($"No recipe matches “{_query.Trim()}”.", Palette.Muted));
            return;
        }
        _body.Children.Add(Note($"{found.Count:N0} recipes match" + (found.Count > MaxSearchRows ? $"; the first {MaxSearchRows} are shown. Narrow the search, or choose a skill." : "."), Palette.Muted));
        if (found.Count == 1) _open.Add(found[0]);   // one answer: show all of it
        int shown = 0;
        foreach (var group in found.GroupBy(r => r.Skill).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (shown >= MaxSearchRows) break;
            var skill = group.Key;
            int count = group.Count();
            var accent = RecipeColors.OfSkill(skill);
            Emit(() => BandHeader(skill, count, open: true, () => Choose(skill), accent, chevron: "›"));
            foreach (var recipe in RecipeLevels.Sorted(group))
            {
                if (shown++ >= MaxSearchRows) break;
                Emit(() => RecipeRow(recipe));
            }
        }
    }

    // ------------------------------------------------------------------ pieces

    private static TextBlock Note(string text, Brush brush) =>
        new() { Text = text, Foreground = brush, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 2, 4) };

    private static FrameworkElement Centre(FrameworkElement element)
    {
        element.HorizontalAlignment = HorizontalAlignment.Center;
        return element;
    }

    private static string Plural(int n, string word) => LootPanel.Plural(n, word);

    /// <summary>What the skill needs, from the wiki: where it is done, the tools, who teaches it.</summary>
    private static FrameworkElement? SkillInfo(Skill skill)
    {
        var lines = new List<(string Label, string Text)>();
        if (skill.Station.Length > 0) lines.Add(("Made at", skill.Station));
        if (skill.Tools.Length > 0) lines.Add(("Tools", skill.Tools));
        if (skill.Trainers.Length > 0) lines.Add(("Taught by", skill.Trainers));
        if (lines.Count == 0) return null;
        var text = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 2, 6), Foreground = Palette.Muted };
        for (int i = 0; i < lines.Count; i++)
        {
            if (i > 0) text.Inlines.Add(new System.Windows.Documents.LineBreak());
            text.Inlines.Add(new System.Windows.Documents.Run(lines[i].Label + "  ") { Foreground = RecipeColors.Label });
            text.Inlines.Add(new System.Windows.Documents.Run(lines[i].Text) { Foreground = Palette.Text });
        }
        return text;
    }

    /// <summary>"LEVEL 26–50 · 14 recipes": a heading for a stretch of levels (or a skill); clicking it folds the stretch away or opens the skill.</summary>
    private static FrameworkElement BandHeader(string title, int count, bool open, Action? click, Color accent, string? chevron = null)
    {
        var row = new ClickRow { Margin = new Thickness(0, 10, 0, 3), Background = Brushes.Transparent, Cursor = click is null ? Cursors.Arrow : Cursors.Hand, Activate = click };
        System.Windows.Automation.AutomationProperties.SetName(row, title);
        var dock = new DockPanel { Margin = new Thickness(0, 0, 4, 0) };
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(right, Dock.Right);
        // The count in a small pill of the heading's colour, then the arrow
        right.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(8), Background = RecipeColors.Solid(accent, 0x22), Padding = new Thickness(7, 1, 7, 1), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = Plural(count, "recipe"), Foreground = RecipeColors.Solid(accent), FontSize = 10.5 },
        });
        if (click is not null) right.Children.Add(new TextBlock { Text = chevron ?? (open ? "▾" : "▸"), Foreground = RecipeColors.Solid(accent), FontSize = 11, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        dock.Children.Add(right);
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new Border { Width = 4, Height = 14, CornerRadius = new CornerRadius(2), Background = RecipeColors.Solid(accent), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        left.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), Foreground = Palette.Text, FontSize = 11.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        dock.Children.Add(left);
        var line = new Border { Height = 1, Background = RecipeColors.Solid(accent, 0x44), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, -3) };
        row.Children.Add(dock);
        row.Children.Add(line);
        row.Height = 22;
        row.MouseLeftButtonUp += (_, _) => row.Activate?.Invoke();
        return row;
    }

    private static string LevelLabel(Recipe r) =>
        r.LevelText is { Length: > 0 } text && text is not ("-" or "??") ? text : "?";

    /// <summary>A recipe: its level, its name and what it takes; a click opens the wiki's other notes about it.</summary>
    private FrameworkElement RecipeRow(Recipe r)
    {
        var row = new ClickRow { Margin = new Thickness(0, 2, 0, 2), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        System.Windows.Automation.AutomationProperties.SetName(row, r.Title);
        var back = new Border { CornerRadius = new CornerRadius(7), Background = (Brush)Ui.Theme["Subtle"] };
        row.Children.Add(back);

        // The level's colour (how far into the skill it is) as a stripe at the row's edge and a tinted pill round the number
        var accent = RecipeColors.OfLevel(r.Level);
        row.Children.Add(new Border { Width = 3, CornerRadius = new CornerRadius(1.5), Background = RecipeColors.Solid(accent, r.Level.HasValue ? (byte)0xE0 : (byte)0x50), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 7, 0, 7) });

        var grid = new Grid { Margin = new Thickness(11, 6, 10, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bool known = r.Level.HasValue || LevelLabel(r) != "?";
        var pill = new Border
        {
            Width = 42, Height = 24, CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Background = RecipeColors.Solid(accent, r.Level.HasValue ? (byte)0x26 : (byte)0x18),
            ToolTip = known ? "The skill level the wiki lists this recipe as trivial at" : "The wiki doesn't list a level for this recipe yet",
            Child = new TextBlock
            {
                Text = LevelLabel(r), FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = RecipeColors.Solid(accent),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        grid.Children.Add(pill);

        var text = new StackPanel();
        Grid.SetColumn(text, 1);
        // The name, with the words a search found marked, and "×4" when it makes several
        var name = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        AddText(name.Inlines, r.Result, Palette.Text);
        if (r.ResultQty > 1) name.Inlines.Add(new System.Windows.Documents.Run($" ×{r.ResultQty}") { Foreground = Palette.Gold });
        text.Children.Add(name);
        if (r.Ingredients.Count > 0)
        {
            // What it takes: the amounts in gold, the names a little lighter than the commas between them
            var takes = new TextBlock { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0), Foreground = Palette.Muted };
            for (int i = 0; i < r.Ingredients.Count; i++)
            {
                var ingredient = r.Ingredients[i];
                if (i > 0) takes.Inlines.Add(new System.Windows.Documents.Run(", ") { Foreground = Palette.Muted });
                takes.Inlines.Add(new System.Windows.Documents.Run(ingredient.Qty + " ") { Foreground = Palette.Gold, FontWeight = FontWeights.SemiBold });
                AddText(takes.Inlines, ingredient.Name, RecipeColors.Ingredient);
                if (ingredient.Or is { Length: > 0 } alternative)
                {
                    takes.Inlines.Add(new System.Windows.Documents.Run(" or ") { Foreground = Palette.Muted });
                    AddText(takes.Inlines, alternative, RecipeColors.Ingredient);
                }
            }
            text.Children.Add(takes);
        }
        else text.Children.Add(new TextBlock { Text = "ingredients not on the wiki yet", Foreground = Palette.Muted, FontSize = 11, FontStyle = FontStyles.Italic, Margin = new Thickness(0, 2, 0, 0) });

        // What a click opens is only built when it is first opened: most rows never are
        var more = new StackPanel { Margin = new Thickness(0, 5, 0, 0), Visibility = Visibility.Collapsed };
        bool filled = false;
        void Open(bool show)
        {
            if (show && !filled)
            {
                FillDetails(more, r);
                filled = true;
            }
            more.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show) _open.Add(r); else _open.Remove(r);
        }
        if (_open.Contains(r)) Open(true);
        text.Children.Add(more);
        grid.Children.Add(text);
        row.Children.Add(grid);

        row.Activate = () => Open(more.Visibility != Visibility.Visible);
        row.MouseLeftButtonUp += (_, _) => row.Activate?.Invoke();
        Hover(row, back);
        return row;
    }

    /// <summary>What a click opens: the kind of recipe, whatever else the wiki lists about it, and a button to copy it.</summary>
    private void FillDetails(StackPanel box, Recipe r)
    {
        void Line(string label, string value)
        {
            var t = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0) };
            t.Inlines.Add(new System.Windows.Documents.Run(label + "  ") { Foreground = RecipeColors.Label });
            AddText(t.Inlines, value, Palette.Text);
            box.Children.Add(t);
        }
        if (r.Group.Length > 0) Line("Kind", r.Group);
        foreach (var note in r.Notes) Line(note.Label, note.Text);
        var copy = new Button { Content = "Copy this recipe", Style = (Style)Ui.Theme["TextButton"], HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(9, 3, 9, 3), FontSize = 11, Focusable = false };
        copy.Click += (_, e) =>
        {
            e.Handled = true;
            _status.Text = ClipboardText.Copy(r.Text()) ? $"Copied {r.Result} to the clipboard." : "Couldn't use the clipboard just now.";
        };
        box.Children.Add(copy);
    }

    private static void Hover(FrameworkElement row, Border back, Brush? normal = null, Brush? hover = null)
    {
        var rest = normal ?? (Brush)Ui.Theme["Subtle"];
        var lit = hover ?? (Brush)Ui.Theme["Hover"];
        row.MouseEnter += (_, _) => back.Background = lit;
        row.MouseLeave += (_, _) => back.Background = rest;
    }

    private static readonly System.Text.RegularExpressions.Regex LevelWord = new(@"^\d{1,3}(\s*-\s*\d{1,3})?$", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>The words of the search that are looked for in the text (a level or a range of levels isn't), marked in the rows.</summary>
    private string[] _found = [];

    /// <summary>
    /// Adds <paramref name="text"/> to a line of text, with every place a word of the search appears marked (a tinted background), so it is plain why a recipe
    /// is in the list. With no search it is one plain piece.
    /// </summary>
    private void AddText(System.Windows.Documents.InlineCollection inlines, string text, Brush foreground)
    {
        if (_found.Length == 0 || text.Length == 0)
        {
            inlines.Add(new System.Windows.Documents.Run(text) { Foreground = foreground });
            return;
        }
        var marks = new List<(int Start, int End)>();
        foreach (var word in _found)
            for (int at = text.IndexOf(word, StringComparison.OrdinalIgnoreCase); at >= 0; at = text.IndexOf(word, at + word.Length, StringComparison.OrdinalIgnoreCase))
                marks.Add((at, at + word.Length));
        if (marks.Count == 0)
        {
            inlines.Add(new System.Windows.Documents.Run(text) { Foreground = foreground });
            return;
        }
        marks.Sort();
        int pos = 0;
        foreach (var (start, end) in marks)
        {
            if (end <= pos) continue;
            int from = Math.Max(start, pos);
            if (from > pos) inlines.Add(new System.Windows.Documents.Run(text[pos..from]) { Foreground = foreground });
            inlines.Add(new System.Windows.Documents.Run(text[from..end]) { Foreground = Palette.Text, Background = RecipeColors.Found });
            pos = end;
        }
        if (pos < text.Length) inlines.Add(new System.Windows.Documents.Run(text[pos..]) { Foreground = foreground });
    }

    // ------------------------------------------------------------------ choosing

    private void Typed()
    {
        if (_search.Text == _query) return;
        _query = _search.Text;
        _scroll.ScrollToTop();
        Build();
    }

    private void Choose(string? skill)
    {
        _skill = skill;
        _settings.RecipeSkill = skill;
        _settings.Save();
        _scroll.ScrollToTop();
        Build();
    }

    private void OpenSkillMenu()
    {
        if (_book is not { } book) return;
        var menu = new ContextMenu { PlacementTarget = _skillButton, Placement = PlacementMode.Bottom, MaxHeight = 420 };
        var all = new MenuItem { Header = $"All skills ({book.Recipes.Count:N0})", IsCheckable = true, IsChecked = _skill is null };
        all.Click += (_, _) => Choose(null);
        menu.Items.Add(all);
        menu.Items.Add(new Separator());
        foreach (var skill in book.Skills)
        {
            var name = skill.Name;
            var item = new MenuItem { Header = $"{name} ({skill.Count:N0})", IsCheckable = true, IsChecked = string.Equals(_skill, name, StringComparison.OrdinalIgnoreCase) };
            item.Click += (_, _) => Choose(name);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private static void OpenWiki()
    {
        try { Process.Start(new ProcessStartInfo(RecipeWiki.PageUrl) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }
}
