using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Cardinal.Maps;
using Cardinal.Mods;
using Cardinal.Tables;
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

    Settings settings = Settings.Load();
    Game? game;
    TableSource? source;
    Table? table;
    List<GridRow> rows = [];
    FloorMap? floor;
    readonly Dictionary<(string Floor, string Layer), IReadOnlyList<MapMarker>> markerCache = [];
    readonly SemaphoreSlim gate = new(1, 1);

    // Assets parsed for editing, by game file. Several item tables share one asset.
    readonly Dictionary<string, EditableAsset> assets = [];
    Usmap? mappings;

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
        settings = new Settings
        {
            PaksFolder = PaksBox.Text.Trim(),
            AesKey = KeyBox.Text.Trim(),
            MappingsFile = MappingsBox.Text.Trim(),
            OodleFile = OodleBox.Text.Trim(),
        };
        settings.Save();
        SetupProblems.Text = string.Join(Environment.NewLine, settings.Problems());
        if (SetupProblems.Text.Length > 0)
            return;

        var opened = await Run("Opening the game files...", () =>
        {
            var g = Game.Open(settings);
            return (Game: g, Tables: TableCatalog.Discover(g), Floors: FloorMap.Floors(g));
        });
        if (opened.Game is null)
            return;

        game?.Dispose();
        game = opened.Game;
        markerCache.Clear();
        assets.Clear();
        mappings = null;
        var view = new ListCollectionView(opened.Tables.ToList());
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(TableSource.Group)));
        TableList.ItemsSource = view;
        FloorBox.ItemsSource = opened.Floors;
        FloorBox.SelectedIndex = 0;
        TablesTab.IsEnabled = MapTab.IsEnabled = ModTab.IsEnabled = true;
        Status.Text = $"Opened. {opened.Tables.Count} tables and {opened.Floors.Count} floors found.";
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
        Status.Text = $"{selected.Group} / {selected.Name}";
        await CheckEditable(selected);
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
        ShowRows();
    }

    void ShowEdit(ModEdit edit, bool applied)
    {
        if (table is null || source?.Id != edit.Table)
            return;
        var column = table.Columns.ToList().IndexOf(edit.Field);
        var row = rows.FirstOrDefault(r => table.Rows[r.Index].Key == edit.Row);
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
            return "The row key can't be changed.";
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
        showingProject = false;
        ShowEdits();
    }

    void ShowEdits()
    {
        EditList.ItemsSource = project.Edits.ToList();
        var file = projectPath is null ? "not saved yet" : projectPath;
        ModInfo.Text = $"{project.Edits.Count} changes  |  builds as {project.FileName()}.utoc, .ucas and .pak  |  project: {file}";
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
        MarkDirty();
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
        var selected = EditList.SelectedItems.Cast<ModEdit>().ToList();
        if (selected.Count == 0)
        {
            Status.Text = "Select the changes to revert first.";
            return;
        }
        foreach (var edit in selected)
        {
            project.Remove(edit.Table, edit.Row, edit.Field);
            ShowEdit(edit, false);
        }
        MarkDirty();
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
        if (game is null)
            return;
        var existing = ModBuilder.Installed(settings, project);
        if (existing.Count > 0 && MessageBox.Show(this,
                $"{project.FileName()} is already installed in {ModBuilder.ModsFolder(settings)}. Replace it with this build?",
                "Cardinal", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        var (current, building) = (game, project);
        if (await Run("Building and installing the mod...", () => ModBuilder.Install(current, settings, building, true)) is { } files)
            Status.Text = $"Installed {string.Join(", ", files.Select(Path.GetFileName))}. Start the game to try it.";
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
