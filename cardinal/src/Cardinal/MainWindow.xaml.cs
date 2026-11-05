using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Cardinal.Maps;
using Cardinal.Tables;
using Microsoft.Win32;

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

    Settings settings = Settings.Load();
    Game? game;
    Table? table;
    FloorMap? floor;
    readonly Dictionary<(string Floor, string Layer), IReadOnlyList<MapMarker>> markerCache = [];
    readonly SemaphoreSlim gate = new(1, 1);

    public MainWindow()
    {
        InitializeComponent();
        PaksBox.Text = settings.PaksFolder ?? "";
        KeyBox.Text = settings.AesKey ?? "";
        MappingsBox.Text = settings.MappingsFile ?? "";
        OodleBox.Text = settings.OodleFile ?? "";
        Map.Hover += text => Status.Text = text;
        Closed += (_, _) => game?.Dispose();
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
            Status.Text = e.Message;
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
        var view = new ListCollectionView(opened.Tables.ToList());
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(TableSource.Group)));
        TableList.ItemsSource = view;
        FloorBox.ItemsSource = opened.Floors;
        FloorBox.SelectedIndex = 0;
        TablesTab.IsEnabled = MapTab.IsEnabled = true;
        Status.Text = $"Opened. {opened.Tables.Count} tables and {opened.Floors.Count} floors found.";
        Tabs.SelectedItem = TablesTab;
    }

    async void TableSelected(object sender, SelectionChangedEventArgs e)
    {
        if (game is null || TableList.SelectedItem is not TableSource source)
            return;
        if (await Run($"Reading {source.Name}...", () => source.Load(game)) is not { } loaded)
            return;
        table = loaded;
        Grid.Columns.Clear();
        for (var i = 0; i < loaded.Columns.Count; i++)
            Grid.Columns.Add(new DataGridTextColumn
            {
                Header = loaded.Columns[i].Replace("_", "__"),
                Binding = new Binding($"[{i}]"),
                MaxWidth = 420,
            });
        ShowRows();
        Status.Text = $"{source.Group} / {source.Name}";
    }

    void SearchChanged(object sender, TextChangedEventArgs e) => ShowRows();

    void ShowRows()
    {
        if (table is null)
            return;
        var search = SearchBox.Text.Trim();
        var rows = search.Length == 0
            ? table.Cells
            : table.Cells.Where(r => r.Any(c => c.Contains(search, StringComparison.OrdinalIgnoreCase))).ToList();
        Grid.ItemsSource = rows;
        TableInfo.Text = $"{table.Source}  |  {rows.Count} of {table.Rows.Count} rows, {table.Columns.Count} columns";
    }

    string? ChooseFolder(string title)
    {
        var dialog = new OpenFolderDialog { Title = title };
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }

    async void ExportTable(object sender, RoutedEventArgs e)
    {
        if (table is null || TableList.SelectedItem is not TableSource source || ChooseFolder("Export the table to") is not { } folder)
            return;
        var current = table;
        if (await Run("Exporting...", () => { Export.Table(current, folder, source.Name); return true; }))
            Status.Text = $"Saved {source.Name}.json and {source.Name}.csv to {folder}";
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

        LevelBox.ItemsSource = MapView.Levels(map).ToList();
        LevelBox.SelectedIndex = 0;
        Status.Text = $"{map.Id}: {map.Cells.Count} cells. Scroll to zoom, drag to pan, hover for details.";
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
