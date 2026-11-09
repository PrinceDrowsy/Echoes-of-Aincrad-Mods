using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Cardinal.Maps;
using Cardinal.Mods;
using Cardinal.Scripts;
using Cardinal.Tables;
using Cardinal.Text;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using UAssetAPI.Unversioned;

namespace Cardinal;

public partial class MainWindow : Window
{
    static readonly Color[] Palette =
    [
        Color.FromRgb(214, 190, 90), Color.FromRgb(60, 140, 70), Color.FromRgb(170, 110, 70), Color.FromRgb(100, 80, 140),
        Color.FromRgb(200, 120, 60), Color.FromRgb(120, 170, 210), Color.FromRgb(40, 105, 55), Color.FromRgb(140, 85, 60),
        Color.FromRgb(110, 170, 90), Color.FromRgb(180, 90, 120), Color.FromRgb(90, 140, 160), Color.FromRgb(160, 160, 80),
        Color.FromRgb(70, 90, 170), Color.FromRgb(200, 200, 200), Color.FromRgb(150, 60, 60), Color.FromRgb(60, 160, 140),
        Color.FromRgb(210, 150, 190), Color.FromRgb(120, 100, 60),
    ];
    static readonly Brush DeveloperBrush = new SolidColorBrush(Color.FromRgb(255, 140, 40));
    static readonly Brush ChangedBrush = new SolidColorBrush(Color.FromRgb(255, 236, 150));
    static readonly Brush TieBrush = new SolidColorBrush(Color.FromRgb(220, 40, 40));
    static readonly Brush OutrankedBrush = new SolidColorBrush(Color.FromRgb(60, 60, 66));

    Settings settings = Settings.Load();
    Game? game;
    IReadOnlyList<TableSource> sources = [];
    TableSource? source;
    Table? table;
    List<GridRow> rows = [];
    FloorMap? floor;
    readonly Dictionary<(string Floor, string Layer), IReadOnlyList<MapMarker>> markerCache = [];
    readonly SemaphoreSlim gate = new(1, 1);

    // Assets parsed for editing, by game file. Several item tables share one asset.
    readonly Dictionary<string, EditableAsset> assets = [];
    Usmap? mappings;

    // The last scan of Paks\~mods, shared by the Installed mods tab and the map. Null until something asks for it.
    ModConflicts? installed;

    // The unmodded game's text by culture, read when a language is first shown.
    readonly Dictionary<string, Locres> texts = new(StringComparer.OrdinalIgnoreCase);
    string? culture;
    List<TextRow> textRows = [];

    Script? script;
    StepText? stepText;

    ModProject project = new();
    string? projectPath;
    bool dirty;
    bool showingProject;

    public MainWindow()
    {
        InitializeComponent();
        PaksBox.Text = settings.PaksFolder ?? "";
        KeyBox.Text = settings.AesKey ?? "";
        MappingsBox.Text = settings.MappingsFile ?? "";
        OodleBox.Text = settings.OodleFile ?? "";
        // The game's languages are only known once its files are open; until then the box shows the current choice.
        choosingLanguage = true;
        LanguageBox.ItemsSource = new[] { new CultureItem(settings.TextLanguage, GameText.Name(settings.TextLanguage)) };
        LanguageBox.SelectedIndex = 0;
        choosingLanguage = false;
        Map.Hover += text => Status.Text = text;
        Closing += ConfirmClose;
        Closed += (_, _) => game?.Dispose();
        ShowProject();
    }

    // Game reads run one at a time off the UI thread; clicks made meanwhile wait their turn.
    async Task<T?> Run<T>(string message, Func<T> work)
    {
        await gate.WaitAsync();
        Cursor = System.Windows.Input.Cursors.Wait;
        Status.Text = message;
        try
        {
            return await Task.Run(work);
        }
        catch (Exception e)
        {
            Status.Text = e.Message.Split(Environment.NewLine)[0];
            MessageBox.Show(this, e.Message, "Cardinal", MessageBoxButton.OK, MessageBoxImage.Warning);
            return default;
        }
        finally
        {
            Cursor = null;
            gate.Release();
        }
    }

    void DetectPaks(object sender, RoutedEventArgs e)
    {
        var found = Settings.FindPaksFolder();
        if (found is null)
            Status.Text = "Echoes of Aincrad wasn't found in your Steam libraries. Choose the folder by hand.";
        else
            PaksBox.Text = found;
    }

    void BrowsePaks(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose EchoesofAincrad\\Content\\Paks" };
        if (dialog.ShowDialog(this) == true)
            PaksBox.Text = dialog.FolderName;
    }

    void BrowseMappings(object sender, RoutedEventArgs e) => Browse(MappingsBox, "Mappings|*.usmap");

    void BrowseOodle(object sender, RoutedEventArgs e) => Browse(OodleBox, "Oodle library|*.dll");

    void Browse(TextBox box, string filter)
    {
        var dialog = new OpenFileDialog { Filter = filter };
        if (dialog.ShowDialog(this) == true)
            box.Text = dialog.FileName;
    }

    async void DownloadOodle(object sender, RoutedEventArgs e)
    {
        if (await Run("Downloading Oodle...", () => Settings.DownloadOodle().GetAwaiter().GetResult()) is { } path)
        {
            OodleBox.Text = path;
            Status.Text = "Oodle downloaded.";
        }
    }

    async void OpenGame(object sender, RoutedEventArgs e)
    {
        settings.PaksFolder = PaksBox.Text.Trim();
        settings.AesKey = KeyBox.Text.Trim();
        settings.MappingsFile = MappingsBox.Text.Trim();
        settings.OodleFile = OodleBox.Text.Trim();
        settings.Save();
        SetupProblems.Text = string.Join(Environment.NewLine, settings.Problems());
        if (SetupProblems.Text.Length > 0)
            return;

        var opened = await Run("Opening the game files...", () =>
        {
            var g = Game.Open(settings);
            return (Game: g, Tables: TableCatalog.Discover(g), Floors: FloorMap.Floors(g), Cultures: GameText.Cultures(g),
                Scripts: Script.List(g));
        });
        if (opened.Game is null)
            return;

        game?.Dispose();
        game = opened.Game;
        markerCache.Clear();
        assets.Clear();
        mappings = null;
        installed = null;
        texts.Clear();
        culture = null;
        ModCellsBox.IsChecked = false;
        sources = opened.Tables;
        var view = new ListCollectionView(opened.Tables.ToList());
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(TableSource.Group)));
        TableList.ItemsSource = view;
        var scripts = new ListCollectionView(opened.Scripts.ToList());
        scripts.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ScriptSource.Folder)));
        ScriptList.ItemsSource = scripts;
        StepGrid.ItemsSource = null;
        script = null;
        stepText = null;
        FloorBox.ItemsSource = opened.Floors;
        FloorBox.SelectedIndex = 0;
        var cultures = opened.Cultures.Select(c => new CultureItem(c, GameText.Name(c))).ToList();
        CultureBox.ItemsSource = cultures;
        CultureBox.SelectedIndex = opened.Cultures.ToList().FindIndex(c => c == game.Language);
        choosingLanguage = true;
        LanguageBox.ItemsSource = cultures;
        LanguageBox.SelectedIndex = CultureBox.SelectedIndex;
        choosingLanguage = false;
        TablesTab.IsEnabled = ScriptsTab.IsEnabled = TextTab.IsEnabled = MapTab.IsEnabled = ModTab.IsEnabled = MergeTab.IsEnabled = InstalledTab.IsEnabled = true;
        ShowMergeList();
        Status.Text = $"Opened. {opened.Tables.Count} tables, {opened.Scripts.Count} scripts and {opened.Floors.Count} floors found.";
        Tabs.SelectedItem = TablesTab;
    }

    async void TableSelected(object sender, SelectionChangedEventArgs e)
    {
        if (game is null || TableList.SelectedItem is not TableSource selected)
            return;
        if (await Run($"Reading {selected.Name}...", () => selected.Load(game)) is not { } loaded)
            return;
        source = selected;
        table = loaded;
        Grid.Columns.Clear();
        for (var i = 0; i < loaded.Columns.Count; i++)
            Grid.Columns.Add(Column(loaded.Columns[i], i));
        ShowTable();
        NewItemButton.Visibility = ItemTemplate.Kinds([selected]).Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        NewTownButton.Visibility = selected.Id == TownTemplate.Table ? Visibility.Visible : Visibility.Collapsed;
        Status.Text = $"{selected.Group} / {selected.Name}";
        await CheckEditable(selected);
    }

    bool choosingLanguage;

    void LanguageSelected(object sender, SelectionChangedEventArgs e)
    {
        if (choosingLanguage || LanguageBox.SelectedItem is not CultureItem selected || selected.Code == settings.TextLanguage)
            return;
        settings.TextLanguage = selected.Code;
        OpenGame(sender, e);
    }

    async void ScriptSelected(object sender, SelectionChangedEventArgs e)
    {
        if (game is null || ScriptList.SelectedItem is not ScriptSource selected)
            return;
        var current = game;
        var known = stepText;
        var loaded = await Run($"Reading {selected.Name}...",
            () => (Script: Script.Load(current, selected), Text: known ?? StepText.For(current, current.Language)));
        if (loaded.Script is null)
            return;
        script = loaded.Script;
        stepText = loaded.Text;
        ShowSteps();
        Status.Text = $"{selected.Folder} / {selected.Name}";
    }

    void ScriptViewChanged(object sender, RoutedEventArgs e) => ShowSteps();

    // The story view tells each step in a sentence and leaves staging out unless asked; the technical view shows every step raw.
    void ShowSteps()
    {
        if (script is null || stepText is null)
            return;
        var technical = TechnicalBox.IsChecked == true;
        // The names switch only changes rows where Cardinal names a character itself, which are staging rows.
        NamesBox.IsEnabled = StagingBox.IsChecked == true && !technical;
        stepText.Edits = project.TextEdits.Where(t => t.Culture == game!.Language).ToDictionary(t => t.Key, t => t.Value);
        var sentences = stepText.Describe(script, NamesBox.IsChecked == true && NamesBox.IsEnabled);
        bool Shown(ScriptStep step) => technical || StagingBox.IsChecked == true || !sentences[step.Id].Staging;
        var shown = script.Steps.Where(Shown).ToList();
        // Shown steps are numbered 1, 2, 3 as listed, so "then" points at a row that's on screen.
        var numbers = shown.Select((s, i) => (s.Id, i + 1)).ToDictionary();
        int Number(ScriptStep step) => technical ? step.Number : numbers[step.Id];
        var cutscenes = shown.Where(s => s.Kind.Contains("LevelSequence")).ToList();
        var eventLines = technical || cutscenes.Count == 0 ? [] : stepText.EventLines(script);
        var rows = new List<StepRow>();
        foreach (var s in shown)
        {
            var sentence = sentences[s.Id];
            rows.Add(new StepRow(Number(s).ToString(), OneLine(sentence.Text), script.Then(s, Shown, Number), Script.KindName(s.Kind),
                Script.Settings(s), script.Leads(s), s.Id, sentence.TextKey, Edited(sentence.TextKey)));
            // With one cutscene the event's lines are its dialogue; with several there's no telling which says what.
            if (cutscenes is [var only] && only == s)
                rows.AddRange(eventLines.Select(line =>
                    new StepRow("", "      " + OneLine(line.Text), "", "", "", "", "", line.Key, Edited(line.Key))));
        }
        StepGrid.ItemsSource = rows;
        foreach (var column in new[] { WhatColumn, ThenColumn })
            column.Visibility = technical ? Visibility.Collapsed : Visibility.Visible;
        foreach (var column in new[] { KindColumn, SettingsColumn, LeadsColumn, IdColumn })
            column.Visibility = technical ? Visibility.Visible : Visibility.Collapsed;
        ScriptInfo.Text = (shown.Count == script.Steps.Count
            ? $"{script.Source.Package}: {script.Steps.Count} steps."
            : $"{script.Source.Package}: {shown.Count} of {script.Steps.Count} steps; the rest only stage the scene.")
            + (cutscenes.Count > 1 && eventLines.Count > 0 ? $" Its {cutscenes.Count} cutscenes share {eventLines.Count} lines of dialogue." : "");
    }

    // Subtitles break lines for the screen; in a row they read better as one.
    static string OneLine(string text) => text.Replace("\r\n", " ").Replace('\n', ' ');

    bool Edited(string? key) => key is not null && project.TextEdits.Any(t => t.Culture == game!.Language && t.Key == key);

    // Dialogue is edited where all text is, so a line changed here and in the Text tab is one change, in every language.
    void EditLine(object sender, RoutedEventArgs e)
    {
        if (StepGrid.SelectedItem is not StepRow { TextKey: { } key })
        {
            Status.Text = "Pick a line of dialogue to edit.";
            return;
        }
        TextSearchBox.Text = key;
        Tabs.SelectedItem = TextTab;
        Status.Text = $"{key}: double-click its text to change it, then come back to Scripts to see it in the scene.";
    }

    void TabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource == Tabs && Tabs.SelectedItem == ScriptsTab)
            ShowSteps();
    }

    void CopyStepId(object sender, RoutedEventArgs e)
    {
        if (StepGrid.SelectedItem is StepRow row)
        {
            Clipboard.SetText(row.Id);
            Status.Text = $"Copied the id of step {row.Number}.";
        }
    }

    void ScriptSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (ScriptList.ItemsSource is not ListCollectionView view)
            return;
        var find = ScriptSearchBox.Text.Trim();
        view.Filter = find.Length == 0
            ? null
            : item => item is ScriptSource s && (s.Folder + "/" + s.Name).Contains(find, StringComparison.OrdinalIgnoreCase);
    }

    static DataGridTextColumn Column(string header, int index)
    {
        var changed = new DataTrigger { Binding = new Binding($"Changed[{index}]"), Value = true };
        changed.Setters.Add(new Setter(DataGridCell.BackgroundProperty, ChangedBrush));
        changed.Setters.Add(new Setter(DataGridCell.ForegroundProperty, Brushes.Black));
        var style = new Style(typeof(DataGridCell));
        style.Triggers.Add(changed);
        return new DataGridTextColumn
        {
            Header = header.Replace("_", "__"),
            // Two-way only because a one-way column can't be edited at all. CellEdited decides what gets written back.
            Binding = new Binding($"Cells[{index}]"),
            CellStyle = style,
            MaxWidth = 420,
        };
    }

    // Rebuilds the grid rows from the unmodded table, then lays the project's changes on top.
    void ShowTable()
    {
        if (table is null || source is null)
            return;
        rows = table.Cells.Select((cells, i) => new GridRow(i, (string[])cells.Clone())).ToList();
        foreach (var edit in project.Edits.Where(e => e.Table == source.Id))
            ShowEdit(edit, true);
        foreach (var added in project.AddedRows.Where(r => r.Table == source.Id))
            if (AddedRow(added) is { } row)
                rows.Add(row);
        ShowRows();
    }

    // A row the project adds, shown as the game row it copies with the key and the project's values on top, all highlighted.
    GridRow? AddedRow(RowAddition added)
    {
        var from = table!.Rows.ToList().FindIndex(r => r.Key == added.From);
        if (from < 0)
            return null;
        var cells = (string[])table.Cells[from].Clone();
        cells[0] = added.Key;
        var columns = table.Columns.ToList();
        foreach (var (field, value) in added.Fields)
        {
            if (columns.IndexOf(field) is var column and > 0)
                cells[column] = value;
            if (columns.IndexOf(field + " (text)") is var textColumn and > 0)
                cells[textColumn] = project.FindAddedText(Game.TextNamespace, value)?.English ?? game?.Text(value) ?? "";
        }
        var row = new GridRow(from, cells) { Added = added };
        for (var column = 0; column < cells.Length; column++)
            row.Changed[column] = true;
        return row;
    }

    void ShowEdit(ModEdit edit, bool applied)
    {
        if (table is null || source?.Id != edit.Table)
            return;
        var column = table.Columns.ToList().IndexOf(edit.Field);
        var row = rows.FirstOrDefault(r => r.Added is null && table.Rows[r.Index].Key == edit.Row);
        if (column > 0 && row is not null)
            row.Set(column, applied ? edit.Value : table.Cells[row.Index][column], applied);
    }

    async Task CheckEditable(TableSource selected)
    {
        if (selected.Target is not { } target)
        {
            EditInfo.Text = "Read-only: Cardinal can't write this kind of table back yet.";
            return;
        }
        EditInfo.Text = "Checking whether this table can be edited...";
        if (!assets.ContainsKey(target.File))
        {
            var checkedAsset = await Run("Checking whether this table can be edited...", () =>
            {
                try
                {
                    return (Asset: EditableAsset.Load(settings, Mappings(), target.File), Error: (string?)null);
                }
                catch (Exception e)
                {
                    return (Asset: (EditableAsset?)null, Error: e.Message);
                }
            });
            if (checkedAsset.Asset is null)
            {
                if (source == selected)
                    EditInfo.Text = $"Read-only for now: {checkedAsset.Error}";
                return;
            }
            assets[target.File] = checkedAsset.Asset;
        }
        if (source == selected)
            EditInfo.Text = assets[target.File].Problem
                            ?? "Double-click a value to change it. Changed values are highlighted and collect in the Mod tab.";
    }

    Usmap Mappings() => mappings ??= new Usmap(settings.MappingsFile!);

    string? CantEdit(GridRow row, int column)
    {
        if (source?.Target is not { } target || table is null)
            return "This table is read-only.";
        if (!assets.TryGetValue(target.File, out var asset))
            return "Still checking whether this table can be edited.";
        if (asset.Problem is { } problem)
            return problem;
        if (column <= 0)
            return row.Added is null ? "The row key can't be changed." : "A new row's key can't be changed. Revert it in the Mod tab and add it again.";
        var name = table.Columns[column];
        if (table.Field(row.Index, name) is not JValue { Type: not JTokenType.Null })
            return $"{name} can't be edited here. Only single values can be: numbers, text, true or false, and choices.";
        return null;
    }

    void CellEditStarting(object sender, DataGridBeginningEditEventArgs e)
    {
        if (CantEdit((GridRow)e.Row.Item, Grid.Columns.IndexOf(e.Column)) is { } reason)
        {
            e.Cancel = true;
            Status.Text = reason;
        }
    }

    void CellEdited(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit || source?.Target is not { } target || table is null
            || e.EditingElement is not TextBox box)
            return;
        var row = (GridRow)e.Row.Item;
        var column = Grid.Columns.IndexOf(e.Column);
        var text = box.Text.Trim();
        if (text == row.Cells[column])
            return;
        if (row.Added is { } added)
        {
            EditAdded(row, added, target, column, text, box);
            return;
        }

        // The editor's text is what the binding writes into the row after this, so it's set to the value that should stay.
        var original = table.Cells[row.Index][column];
        var edit = new ModEdit(source.Id, table.Rows[row.Index].Key, table.Columns[column], text, original);
        if (ModEdit.Same(text, original))
        {
            project.Remove(edit.Table, edit.Row, edit.Field);
            box.Text = original;
            row.Set(column, original, false);
        }
        else if (assets[target.File].Check(target, edit) is { } problem)
        {
            box.Text = row.Cells[column];
            Status.Text = problem;
            MessageBox.Show(this, problem, "Cardinal", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        else
        {
            project.Set(edit);
            box.Text = text;
            row.Set(column, text, true);
        }
        MarkDirty();
        Status.Text = $"{edit.Field} in row {edit.Row}: {original} -> {text}";
    }

    // A value of a row the project adds goes into the addition itself, checked against a fresh copy of the row it starts from.
    void EditAdded(GridRow row, RowAddition added, EditTarget target, int column, string text, TextBox box)
    {
        var field = table!.Columns[column];
        var changed = added with { Fields = new Dictionary<string, string>(added.Fields) { [field] = text } };
        if (assets[target.File].Check(target, changed) is { } problem)
        {
            box.Text = row.Cells[column];
            Status.Text = problem;
            MessageBox.Show(this, problem, "Cardinal", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        project.SetRow(changed);
        row.Added = changed;
        box.Text = text;
        row.Set(column, text, true);
        MarkDirty();
        Status.Text = $"{field} in new row {added.Key}: {text}";
    }

    async void NewItemClicked(object sender, RoutedEventArgs e)
    {
        if (game is null || source is null || table is null)
            return;
        var cell = Grid.CurrentCell.IsValid ? Grid.CurrentCell : Grid.SelectedCells.FirstOrDefault();
        if (cell.Item is not GridRow { Added: null } selected)
        {
            Status.Text = "Select a cell in the item to copy first (one of the game's own, not a new one).";
            return;
        }
        var from = table.Rows[selected.Index];
        var copying = from.Text.GetValueOrDefault("ItemKey", $"item {from.Key}");
        var dialog = new NewItemWindow(source.Name, copying, ItemTemplate.NextId(table, project, source.Id)) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        var (current, adding, kind) = (game, project, source.Id);
        var item = new NewItem(kind, from.Key, dialog.Id, dialog.ItemName, dialog.Description);
        if (await Run("Adding the item...", () => ItemTemplate.Add(current, settings, adding, sources, item)) is not { } notes)
            return;
        MarkDirty();
        ShowTable();
        ShowText();
        Status.Text = $"Added {dialog.ItemName} as {kind} {dialog.Id}. Its new row is highlighted at the bottom of the table.";
        MessageBox.Show(this, string.Join(Environment.NewLine + Environment.NewLine, notes), "Cardinal", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    async void NewTownClicked(object sender, RoutedEventArgs e)
    {
        if (game is null || table is null)
            return;
        var cell = Grid.CurrentCell.IsValid ? Grid.CurrentCell : Grid.SelectedCells.FirstOrDefault();
        if (cell.Item is not GridRow { Added: null } selected)
        {
            Status.Text = "Select a cell in the town to copy first (one of the game's own, not a new one).";
            return;
        }
        var from = table.Rows[selected.Index];
        var copying = game.Text("AreaTitle_" + from.Fields["Name"]) ?? $"town {from.Fields["ID"]}";
        var dialog = new NewTownWindow(copying, TownTemplate.NextId(table, project)) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        var (current, adding) = (game, project);
        var town = new NewTown(from.Key, dialog.Id, dialog.TownName);
        if (await Run("Adding the town...", () => TownTemplate.Add(current, settings, adding, sources, town)) is not { } notes)
            return;
        MarkDirty();
        ShowTable();
        ShowText();
        Status.Text = $"Added {dialog.TownName} as town {dialog.Id:000}. Its new row is highlighted at the bottom of the table.";
        MessageBox.Show(this, string.Join(Environment.NewLine + Environment.NewLine, notes), "Cardinal", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    void SearchChanged(object sender, TextChangedEventArgs e) => ShowRows();

    void ShowRows()
    {
        if (table is null)
            return;
        var search = SearchBox.Text.Trim();
        var shown = search.Length == 0
            ? rows
            : rows.Where(r => r.Cells.Any(c => c.Contains(search, StringComparison.OrdinalIgnoreCase))).ToList();
        Grid.ItemsSource = shown;
        TableInfo.Text = $"{table.Source}  |  {shown.Count} of {table.Rows.Count} rows, {table.Columns.Count} columns";
    }

    async void CultureSelected(object sender, SelectionChangedEventArgs e)
    {
        if (game is null || CultureBox.SelectedItem is not CultureItem selected)
            return;
        var current = game;
        if (await Run($"Reading the {selected.Name} text...", () =>
            {
                Locres Text(string c) => texts.TryGetValue(c, out var t) ? t : GameText.Load(current, c);
                return (English: Text(GameText.English), Language: Text(selected.Code));
            }) is not { English: not null } loaded)
            return;
        texts[GameText.English] = loaded.English;
        texts[selected.Code] = loaded.Language;
        culture = selected.Code;
        ShowText();
        Status.Text = $"{selected.Name}: {loaded.Language.Count} lines. Double-click a line's text to change it.";
    }

    // Rebuilds the text rows from the unmodded game, with the project's text edits laid on top.
    void ShowText()
    {
        if (culture is null)
            return;
        var english = texts[GameText.English];
        textRows = texts[culture].Lines.Select(line =>
        {
            var edit = project.FindText(culture, line.Namespace, line.Key);
            return new TextRow(line.Namespace, line.Key, english.Get(line.Namespace, line.Key) ?? "", edit?.Value ?? line.Text, edit is not null);
        }).ToList();
        // Text the project adds comes first. A language without its own line shows the English, as the game does.
        textRows.InsertRange(0, project.AddedText.Select(added => new TextRow(added.Namespace, added.Key, added.English,
            added.Text.GetValueOrDefault(culture, added.English), true, true)));
        ShowTextRows();
    }

    void TextSearchChanged(object sender, TextChangedEventArgs e) => ShowTextRows();

    void ShowTextRows()
    {
        if (culture is null)
            return;
        var search = TextSearchBox.Text.Trim();
        var shown = search.Length == 0
            ? textRows
            : textRows.Where(r => r.Key.Contains(search, StringComparison.OrdinalIgnoreCase)
                                  || r.Text.Contains(search, StringComparison.OrdinalIgnoreCase)
                                  || r.English.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        TextGrid.ItemsSource = shown;
        ShowTextInfo();
    }

    void ShowTextInfo()
    {
        if (culture is null || TextGrid.ItemsSource is not List<TextRow> shown)
            return;
        TextInfo.Text = $"{GameText.Name(culture)}  |  {shown.Count} of {textRows.Count} lines  |  " +
                        $"{project.TextEdits.Count(t => t.Culture == culture)} changed by this project. " +
                        "A mod replaces a language's text whole, so two mods that change the same language need merging.";
    }

    void TextEdited(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit || culture is null || e.EditingElement is not TextBox box)
            return;
        var row = (TextRow)e.Row.Item;
        var text = box.Text;
        if (text == row.Text)
            return;
        if (row.Added)
        {
            try
            {
                TextEditing.SetAdded(project, culture, row.Namespace, row.Key, text == row.English && culture != GameText.English ? "" : text);
            }
            catch (EditException problem)
            {
                box.Text = row.Text;
                Status.Text = problem.Message;
                return;
            }
            MarkDirty();
            ShowTextInfo();
            Status.Text = $"{row.Key}: set in {GameText.Name(culture)} for the text your mod adds.";
            return;
        }
        var original = TextEditing.Set(project, texts[culture], culture, row.Namespace, row.Key, text);
        row.Changed = text != original;
        MarkDirty();
        // Re-filtering here would replace the rows mid-edit and drop the typed text, so only the counts update.
        ShowTextInfo();
        Status.Text = row.Changed ? $"{row.Key}: changed." : $"{row.Key}: back to the game's own text.";
    }

    async void ExportTranslation(object sender, RoutedEventArgs e)
    {
        if (culture is null)
            return;
        var dialog = new SaveFileDialog { Filter = "CSV|*.csv", FileName = $"{culture}.csv" };
        if (dialog.ShowDialog(this) != true)
            return;
        var (exporting, english, language, code) = (project, texts[GameText.English], texts[culture], culture);
        if (await Run("Exporting...", () => { TextEditing.ExportCsv(exporting, english, language, code, dialog.FileName); return true; }))
            Status.Text = $"Saved {GameText.Name(code)} next to its English to {dialog.FileName}. Translate the Text column, then import it.";
    }

    async void ImportTranslation(object sender, RoutedEventArgs e)
    {
        if (culture is null)
            return;
        var dialog = new OpenFileDialog { Filter = "CSV|*.csv" };
        if (dialog.ShowDialog(this) != true)
            return;
        var (importing, english, language, code) = (project, texts[GameText.English], texts[culture], culture);
        if (await Run("Importing...", () => TextEditing.ImportCsv(importing, english, language, code, dialog.FileName)) is not { } result)
            return;
        MarkDirty();
        ShowText();
        var report = $"Imported {result.Changed} changed lines into {GameText.Name(code)} ({result.Unchanged} already matched).";
        if (result.Missing.Count > 0)
            report += $"{Environment.NewLine}{Environment.NewLine}Not in the game, so skipped ({result.Missing.Count}):{Environment.NewLine}" +
                      string.Join(Environment.NewLine, result.Missing.Take(20)) + (result.Missing.Count > 20 ? Environment.NewLine + "..." : "");
        if (result.EnglishChanged.Count > 0)
            report += $"{Environment.NewLine}{Environment.NewLine}The English changed since this file was exported, so check these ({result.EnglishChanged.Count}):{Environment.NewLine}" +
                      string.Join(Environment.NewLine, result.EnglishChanged.Take(20)) + (result.EnglishChanged.Count > 20 ? Environment.NewLine + "..." : "");
        Status.Text = report.Split(Environment.NewLine)[0];
        MessageBox.Show(this, report, "Cardinal", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    string? ChooseFolder(string title)
    {
        var dialog = new OpenFolderDialog { Title = title };
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }

    async void ExportTable(object sender, RoutedEventArgs e)
    {
        if (table is null || source is null || ChooseFolder("Export the table to") is not { } folder)
            return;
        var (current, name) = (table, source.Name);
        if (await Run("Exporting...", () => { Export.Table(current, folder, name); return true; }))
            Status.Text = $"Saved {name}.json and {name}.csv to {folder}. Exports show the unmodded game.";
    }

    async void ExportAllTables(object sender, RoutedEventArgs e)
    {
        if (game is null || TableList.ItemsSource is not ListCollectionView view || ChooseFolder("Export every table to") is not { } folder)
            return;
        var sources = view.SourceCollection.Cast<TableSource>().ToList();
        if (await Run("Exporting every table...", () => Export.Tables(game, sources, folder)) is { } results)
        {
            var failed = results.Count(r => r.Error is not null);
            Status.Text = $"Exported {results.Count - failed} tables to {folder}" + (failed > 0 ? $", {failed} failed (see tables.json)" : "");
        }
    }

    void ShowProject()
    {
        showingProject = true;
        ModNameBox.Text = project.Name;
        ModVersionBox.Text = project.Version;
        ModAuthorBox.Text = project.Author;
        ModDescriptionBox.Text = project.Description;
        ModPriorityBox.Text = project.Priority.ToString();
        CheckResults.Visibility = Visibility.Collapsed;
        showingProject = false;
        ShowEdits();
    }

    void ShowEdits()
    {
        EditList.ItemsSource = project.AddedRows.Select(r => new ChangeRow("New row", r.Label, $"copy of {r.From}",
                string.Join(", ", r.Fields.Select(f => $"{f.Key} {f.Value}")), r))
            .Concat(project.AddedText.Select(t => new ChangeRow("New text", t.Label, null,
                t.English + (t.Text.Count > 1 ? $" (+{t.Text.Count - 1} languages)" : ""), t)))
            .Concat(project.Edits.Select(e => new ChangeRow("Value", e.Label, e.Original, e.Value, e)))
            .Concat(project.TextEdits.Select(e => new ChangeRow("Text", e.Label, e.Original, e.Value, e)))
            .ToList();
        var file = projectPath is null ? "not saved yet" : projectPath;
        // Changed text goes in a .pak of its own; values, new rows and new text keys need the .utoc and .ucas container too.
        var builds = project.HasContainer ? ".utoc, .ucas and .pak" : ".pak";
        ModInfo.Text = $"{project.Changes} changes  |  builds as {project.FileName()}{builds}  |  project: {file}";
        Title = $"Cardinal - {project.Name}{(dirty ? " *" : "")}";
    }

    void MarkDirty()
    {
        dirty = true;
        ShowEdits();
    }

    void ModDetailsChanged(object sender, TextChangedEventArgs e)
    {
        if (showingProject)
            return;
        project.Name = ModNameBox.Text.Trim();
        project.Version = ModVersionBox.Text.Trim();
        project.Author = ModAuthorBox.Text.Trim();
        project.Description = ModDescriptionBox.Text.Trim();
        if (int.TryParse(ModPriorityBox.Text.Trim(), out var priority) && priority is >= 1 and <= ModProject.MaxPriority)
        {
            project.Priority = priority;
            Status.Text = $"Builds as {project.FileName()}.";
        }
        else
            Status.Text = $"Priority must be a whole number from 1 to {ModProject.MaxPriority}. It stays at {project.Priority} until then.";
        MarkDirty();
    }

    async void CheckProject(object sender, RoutedEventArgs e)
    {
        if (game is null)
            return;
        if (project.Changes == 0)
        {
            Status.Text = "The project has no changes to check.";
            return;
        }
        var (current, checking) = (game, project);
        if (await Run("Checking every change against the game...", () => ModBuilder.Check(current, settings, checking)) is not { } problems)
            return;
        CheckResults.Visibility = Visibility.Visible;
        CheckResults.Text = problems.Count == 0
            ? $"All {checking.Changes} changes still fit the game."
            : string.Join(Environment.NewLine, problems.Select(p => (p.Blocks ? "Can't be made any more: " : "Original value changed: ") + p));
        Status.Text = problems.Count == 0 ? "Every change still fits the game." : $"The check found {problems.Count} things to look at.";
    }

    void CopyDiagnostics(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(Diagnostics.Report(settings));
        Status.Text = "Diagnostic info copied. It has no key and no paths on your PC, so it's safe to paste into a bug report.";
    }

    void ShowMergeList()
    {
        var entries = new List<MergeEntry>();
        var projects = new List<ModProject>();
        for (var i = 0; i < settings.MergeProjects.Count; i++)
        {
            var path = settings.MergeProjects[i];
            try
            {
                var loaded = ModProject.Load(path);
                projects.Add(loaded);
                entries.Add(new MergeEntry(i + 1, loaded.Name, loaded.Version, loaded.Changes, path));
            }
            catch (Exception ex) when (ex is ArgumentException or IOException)
            {
                entries.Add(new MergeEntry(i + 1, "(can't be read)", "", 0, path));
            }
        }
        MergeList.ItemsSource = entries;
        if (projects.Count < 2)
        {
            ClashList.ItemsSource = null;
            MergeInfo.Text = "Add at least two mod projects to merge.";
            return;
        }
        var (merged, clashes) = ModMerge.Merge(projects);
        ClashList.ItemsSource = clashes.Select(c => new ClashRow(c.Change,
            string.Join(", ", c.Values.Select(v => $"{v.Mod}: {v.Value}")), c.Winner)).ToList();
        MergeInfo.Text = $"{projects.Count} projects, {merged.Changes} changes, {clashes.Count} values decided by order. " +
                         $"Builds as {merged.FileName()}." +
                         (projects.Count < entries.Count ? " Projects that can't be read are left out." : "");
    }

    void AddMergeProjects(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Cardinal mod project|*.json", Multiselect = true };
        if (dialog.ShowDialog(this) != true)
            return;
        settings.MergeProjects.AddRange(dialog.FileNames.Where(f => !settings.MergeProjects.Contains(f, StringComparer.OrdinalIgnoreCase)));
        settings.Save();
        ShowMergeList();
    }

    void RemoveMergeProject(object sender, RoutedEventArgs e) => ChangeMergeList(i => settings.MergeProjects.RemoveAt(i), -1);

    void MoveMergeUp(object sender, RoutedEventArgs e) => ChangeMergeList(i => Swap(i, i - 1), -1);

    void MoveMergeDown(object sender, RoutedEventArgs e) => ChangeMergeList(i => Swap(i, i + 1), 1);

    void Swap(int a, int b)
    {
        if (b < 0 || b >= settings.MergeProjects.Count)
            return;
        (settings.MergeProjects[a], settings.MergeProjects[b]) = (settings.MergeProjects[b], settings.MergeProjects[a]);
    }

    void ChangeMergeList(Action<int> change, int move)
    {
        var index = MergeList.SelectedIndex;
        if (index < 0)
        {
            Status.Text = "Select a project in the list first.";
            return;
        }
        change(index);
        settings.Save();
        ShowMergeList();
        MergeList.SelectedIndex = Math.Clamp(index + move, 0, settings.MergeProjects.Count - 1);
    }

    // Every listed project, or a message about the first one that can't be read.
    (ModProject Merged, List<ModProject> Projects)? PrepareMerge()
    {
        try
        {
            var projects = settings.MergeProjects.Select(ModProject.Load).ToList();
            return (ModMerge.Merge(projects).Merged, projects);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            MessageBox.Show(this, ex.Message, "Cardinal", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
    }

    async void BuildMergedToFolder(object sender, RoutedEventArgs e)
    {
        if (game is null || PrepareMerge() is not { } merge || ChooseFolder("Build the merged patch into") is not { } folder)
            return;
        var current = game;
        if (await Run("Building the merged patch...", () => ModBuilder.Build(current, settings, merge.Merged, folder)) is { } files)
            Status.Text = $"Built {string.Join(", ", files.Select(Path.GetFileName))} in {folder}";
    }

    async void InstallMerged(object sender, RoutedEventArgs e)
    {
        if (game is null || PrepareMerge() is not { } merge)
            return;
        if (!ReplaceOk(merge.Merged))
            return;
        var current = game;
        if (await Run("Building and installing the merged patch...", () => ModBuilder.Install(current, settings, merge.Merged, true)) is not { } files)
            return;
        Status.Text = $"Installed {string.Join(", ", files.Select(Path.GetFileName))}. Start the game to try it.";

        var singles = merge.Projects.SelectMany(p => ModBuilder.Installed(settings, p)).ToList();
        if (singles.Count > 0 && MessageBox.Show(this,
                "The merged patch has every change from these mods, so their own builds in ~mods aren't needed any more:" +
                Environment.NewLine + string.Join(Environment.NewLine, singles.Select(Path.GetFileName)) +
                Environment.NewLine + Environment.NewLine + "Remove them?",
                "Cardinal", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            singles.ForEach(File.Delete);
            Status.Text += $" Removed {singles.Count} files the merged patch replaces.";
        }
        if (merge.Merged.AddedAssets.Count > 0)
            await Patch(Status.Text);
        await InstalledChanged();
    }

    async void PatchRegistry(object sender, RoutedEventArgs e)
    {
        await Patch(null);
        await InstalledChanged();
    }

    // Rebuilds the registry patch from what's installed now. before is the status it follows, such as an install's.
    async Task Patch(string? before)
    {
        if (await Run("Patching the asset registry...", () => RegistryPatch.Build(settings)) is not { } result)
            return;
        var done = result.Pak is null
            ? "No installed mod adds new assets, so no registry patch is needed."
            : $"Registry patch updated with {result.Added.Count} new assets.";
        Status.Text = before is null ? done : $"{before} {done}";
        if (result.Problems.Count > 0)
            MessageBox.Show(this, "These new assets couldn't be added, so the game won't find them:" + Environment.NewLine +
                string.Join(Environment.NewLine, result.Problems), "Cardinal", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    bool ReplaceOk(ModProject building) =>
        ModBuilder.Installed(settings, building) is not { Count: > 0 } earlier
        || MessageBox.Show(this, $"Already installed in {ModBuilder.ModsFolder(settings)}: {ModBuilder.Builds(earlier)}. Replace with this build?",
            "Cardinal", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    // Rescans only when the Installed mods tab or the map is already showing a scan; otherwise the next look scans afresh.
    async Task InstalledChanged()
    {
        if (installed is null)
            return;
        installed = null;
        if (InstalledList.ItemsSource is not null || ModCellsBox.IsChecked == true)
            await ScanMods();
    }

    async void ScanInstalled(object sender, RoutedEventArgs e) => await ScanMods();

    async Task<ModConflicts?> ScanMods()
    {
        if (await Run("Scanning installed mods...", () => ModConflicts.Scan(settings)) is not { } scanned)
            return null;
        installed = scanned;
        InstalledList.ItemsSource = scanned.Mods.Select(m =>
        {
            var involved = scanned.Conflicts.Where(c => c.Holders.Contains(m)).ToList();
            return new InstalledRow(m.Name, m.Assets.Count, involved.Count, involved.Count(c => c.Holders[0].Order > m.Order));
        }).ToList();
        ConflictList.ItemsSource = scanned.Conflicts.Select(c => new ConflictRow(c.Asset, c.Outcome())).ToList();
        InstalledInfo.Text = $"{scanned.Mods.Count} mods installed, {scanned.Conflicts.Count} game files with a conflict.";
        Status.Text = InstalledInfo.Text;
        ShowModCells();
        return scanned;
    }

    async void ModCellsToggled(object sender, RoutedEventArgs e)
    {
        if (ModCellsBox.IsChecked != true)
        {
            Map.SetModCells(null);
            ModLegend.Children.Clear();
            return;
        }
        if (installed is null && await ScanMods() is null)
            ModCellsBox.IsChecked = false;
        else
            ShowModCells();
    }

    // Colours each cell a mod replaces by the mod the game would load there; red where that's left to chance.
    void ShowModCells()
    {
        if (floor is null || installed is null || ModCellsBox.IsChecked != true)
            return;
        var brushes = installed.Mods.Select((m, i) => (m, (Brush)new SolidColorBrush(Palette[i % Palette.Length])))
            .ToDictionary(p => p.m, p => p.Item2);
        var cells = new Dictionary<string, ModCell>(StringComparer.OrdinalIgnoreCase);
        var counts = new Dictionary<Brush, int>();
        string? mainMap = null;
        foreach (var asset in installed.Mods.SelectMany(m => m.Assets))
        {
            var owners = installed.Owners(asset)!;
            if (asset.Equals(floor.MapAsset, StringComparison.OrdinalIgnoreCase))
                mainMap = owners.Outcome();
            if (floor.CellOf(asset) is not { } cell || cells.ContainsKey(cell))
                continue;
            var brush = owners.Winner switch
            {
                null => TieBrush,
                { Installed: true } winner => brushes[winner],
                _ => OutrankedBrush,
            };
            cells[cell] = new ModCell(owners.Outcome(), brush);
            counts[brush] = counts.GetValueOrDefault(brush) + 1;
        }
        Map.SetModCells(cells);

        ModLegend.Children.Clear();
        foreach (var (mod, brush) in brushes.Where(b => counts.ContainsKey(b.Value)))
            AddLegend(brush, $"{mod.Name}  ({counts[brush]})");
        if (counts.ContainsKey(TieBrush))
            AddLegend(TieBrush, $"Same priority, left to chance  ({counts[TieBrush]})");
        if (counts.ContainsKey(OutrankedBrush))
            AddLegend(OutrankedBrush, $"Outranked by the game, never used  ({counts[OutrankedBrush]})");
        if (cells.Count == 0)
            ModLegend.Children.Add(new TextBlock { Text = "No installed mod replaces cells on this floor.", Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap });
        if (mainMap is not null)
            ModLegend.Children.Add(new TextBlock { Text = $"This floor's main map is replaced: {mainMap}", TextWrapping = TextWrapping.Wrap });
    }

    void AddLegend(Brush brush, string label)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
        line.Children.Add(new Border { Width = 12, Height = 12, Margin = new Thickness(0, 0, 6, 0), Background = brush });
        line.Children.Add(new TextBlock { Text = label });
        ModLegend.Children.Add(line);
    }

    bool DiscardOk() =>
        !dirty || MessageBox.Show(this, "The mod project has unsaved changes. Discard them?", "Cardinal",
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    void ConfirmClose(object? sender, CancelEventArgs e) => e.Cancel = !DiscardOk();

    void Replace(ModProject next, string? path)
    {
        project = next;
        projectPath = path;
        dirty = false;
        ShowProject();
        ShowTable();
        ShowText();
    }

    void NewProject(object sender, RoutedEventArgs e)
    {
        if (DiscardOk())
            Replace(new ModProject(), null);
    }

    void OpenProject(object sender, RoutedEventArgs e)
    {
        if (!DiscardOk())
            return;
        var dialog = new OpenFileDialog { Filter = "Cardinal mod project|*.json" };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            Replace(ModProject.Load(dialog.FileName), dialog.FileName);
            Status.Text = $"Opened {dialog.FileName}";
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            MessageBox.Show(this, ex.Message, "Cardinal", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    void SaveProject(object sender, RoutedEventArgs e)
    {
        if (projectPath is null)
            SaveProjectAs(sender, e);
        else
            Save(projectPath);
    }

    void SaveProjectAs(object sender, RoutedEventArgs e)
    {
        var name = string.Concat(project.Name.Split(Path.GetInvalidFileNameChars()));
        var dialog = new SaveFileDialog { Filter = "Cardinal mod project|*.json", FileName = (name.Length > 0 ? name : "MyMod") + ".json" };
        if (dialog.ShowDialog(this) == true)
            Save(dialog.FileName);
    }

    void Save(string path)
    {
        try
        {
            project.Save(path);
            projectPath = path;
            dirty = false;
            ShowEdits();
            Status.Text = $"Saved {path}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Cardinal", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    void RevertSelected(object sender, RoutedEventArgs e)
    {
        var selected = EditList.SelectedItems.Cast<ChangeRow>().Select(r => r.Edit).ToList();
        if (selected.Count == 0)
        {
            Status.Text = "Select the changes to revert first.";
            return;
        }
        foreach (var change in selected)
            switch (change)
            {
                case ModEdit edit:
                    project.Remove(edit.Table, edit.Row, edit.Field);
                    ShowEdit(edit, false);
                    break;
                case TextEdit edit:
                    project.RemoveText(edit.Culture, edit.Namespace, edit.Key);
                    break;
                case RowAddition row:
                    project.RemoveRow(row.Table, row.Key);
                    break;
                case AddedText text:
                    project.RemoveAddedText(text.Namespace, text.Key);
                    break;
            }
        MarkDirty();
        ShowTable();
        ShowText();
        Status.Text = $"Reverted {selected.Count} changes.";
    }

    async void BuildToFolder(object sender, RoutedEventArgs e)
    {
        if (game is null || ChooseFolder("Build the mod into") is not { } folder)
            return;
        var (current, building) = (game, project);
        if (await Run("Building the mod...", () => ModBuilder.Build(current, settings, building, folder)) is { } files)
            Status.Text = $"Built {string.Join(", ", files.Select(Path.GetFileName))} in {folder}";
    }

    async void BuildAndInstall(object sender, RoutedEventArgs e)
    {
        if (game is null || !ReplaceOk(project))
            return;
        var (current, building) = (game, project);
        if (await Run("Building and installing the mod...", () => ModBuilder.Install(current, settings, building, true)) is not { } files)
            return;
        Status.Text = $"Installed {string.Join(", ", files.Select(Path.GetFileName))}. Start the game to try it.";
        if (building.AddedAssets.Count > 0)
            await Patch(Status.Text);
        await InstalledChanged();
    }

    async void FloorSelected(object sender, SelectionChangedEventArgs e)
    {
        if (game is null || FloorBox.SelectedItem is not string id)
            return;
        if (await Run($"Reading floor {id}...", () => FloorMap.Load(game, id)) is not { } map)
            return;

        floor = map;
        var colors = new Dictionary<string, Brush>();
        var worldLayers = map.Layers.Where(l => !l.Developer).ToList();
        for (var i = 0; i < worldLayers.Count; i++)
            colors[worldLayers[i].Name] = new SolidColorBrush(Palette[i % Palette.Length]);
        foreach (var layer in map.Layers.Where(l => l.Developer))
            colors[layer.Name] = DeveloperBrush;
        Map.Show(map, colors);

        LayerList.Children.Clear();
        AddLayerBox(MapView.NoLayer, "No layer (base world)", map.Cells.Count(c => c.Layers.Count == 0), null, true);
        foreach (var layer in map.Layers)
            AddLayerBox(layer.Name, layer.Developer ? $"{layer.Name} (developer)" : layer.Name, layer.CellCount, colors[layer.Name], !layer.Developer);

        GridBox.ItemsSource = MapView.Grids(map).ToList();
        GridBox.SelectedIndex = 0;
        Status.Text = $"{map.Id}: {map.Cells.Count} cells. Scroll to zoom, drag to pan, hover for details.";
        ShowModCells();
    }

    void GridSelected(object sender, SelectionChangedEventArgs e)
    {
        if (floor is null || GridBox.SelectedItem is not string grid)
            return;
        Map.SetGrid(grid);
        LevelBox.ItemsSource = MapView.Levels(floor, grid).ToList();
        LevelBox.SelectedIndex = 0;
        if (grid != MapView.MainGrid)
            Status.Text = $"{grid}: HLOD cells, the simplified scenery the game draws in the distance.";
    }

    void AddLayerBox(string layer, string label, int cells, Brush? color, bool shown)
    {
        var text = new StackPanel { Orientation = Orientation.Horizontal };
        text.Children.Add(new Border { Width = 12, Height = 12, Margin = new Thickness(0, 0, 6, 0), Background = color ?? Brushes.Gray });
        text.Children.Add(new TextBlock { Text = $"{label}  ({cells})" });
        var box = new CheckBox { Content = text, IsChecked = shown, Margin = new Thickness(0, 2, 0, 2), IsEnabled = cells > 0 };
        box.Checked += (_, _) => ShowLayer(layer, true);
        box.Unchecked += (_, _) => ShowLayer(layer, false);
        LayerList.Children.Add(box);
        Map.SetVisible(layer, shown && cells > 0);
    }

    async void ShowLayer(string layer, bool shown)
    {
        Map.SetVisible(layer, shown);
        if (!shown || game is null || floor is null || floor.Layers.FirstOrDefault(l => l.Name == layer) is not { Developer: true })
            return;
        var key = (floor.Id, layer);
        if (!markerCache.TryGetValue(key, out var list))
        {
            var map = floor;
            list = await Run($"Reading the {layer} layer...", () => map.LoadMarkers(game, layer));
            if (list is null)
                return;
            markerCache[key] = list;
            // Another floor may have been chosen while this one's markers were read.
            if (floor != map)
                return;
        }
        Map.SetMarkers(layer, list);
        Status.Text = $"{layer}: {list.Count} markers";
    }

    void LevelSelected(object sender, SelectionChangedEventArgs e)
    {
        if (LevelBox.SelectedItem is int level)
            Map.SetLevel(level);
    }

    async void ExportMap(object sender, RoutedEventArgs e)
    {
        if (game is null || floor is null || ChooseFolder($"Export floor {floor.Id} to") is not { } folder)
            return;
        var id = floor.Id;
        if (await Run($"Exporting floor {id}...", () => Export.Floor(game, id, folder)) is not null)
            Status.Text = $"Saved {id}.json, {id}-cells.csv and {id}-markers.csv to {folder}";
    }
}
