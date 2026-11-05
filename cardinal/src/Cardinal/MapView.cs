using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Cardinal.Maps;

namespace Cardinal;

/// <summary>Draws one grid level of a floor from above, with any loaded marker layers. Wheel zooms, dragging pans.</summary>
public sealed class MapView : FrameworkElement
{
    // Stands for cells that belong to no data layer.
    public const string NoLayer = "";

    public const string MainGrid = "MainGrid";
    const double LabelZoom = 0.02;

    FloorMap? floor;
    string grid = MainGrid;
    int level;
    readonly HashSet<string> visible = [];
    readonly Dictionary<string, IReadOnlyList<MapMarker>> markers = [];
    readonly Dictionary<string, Brush> colors = [];
    double scale = 0.005;
    Vector offset;
    Point? dragFrom;

    public event Action<string>? Hover;

    /// <summary>The floor's grids, MainGrid first. The others hold HLOD cells, the simplified scenery drawn in the distance.</summary>
    public static IEnumerable<string> Grids(FloorMap map) =>
        map.Cells.Select(c => c.Grid).Distinct().OrderBy(g => g != MainGrid).ThenBy(g => g);

    public static IEnumerable<int> Levels(FloorMap map, string grid) =>
        map.Cells.Where(c => c.Grid == grid).Select(c => c.Level).Distinct().Order();

    public void Show(FloorMap map, IReadOnlyDictionary<string, Brush> layerColors)
    {
        floor = map;
        markers.Clear();
        colors.Clear();
        foreach (var (name, brush) in layerColors)
            colors[name] = brush;
        grid = MainGrid;
        SetLevel(Levels(map, grid).FirstOrDefault());
    }

    public void SetGrid(string value)
    {
        grid = value;
        if (floor is not null)
            SetLevel(Levels(floor, grid).FirstOrDefault());
    }

    public void SetLevel(int value)
    {
        level = value;
        Fit();
    }

    public void SetVisible(string layer, bool shown)
    {
        if (shown)
            visible.Add(layer);
        else
            visible.Remove(layer);
        InvalidateVisual();
    }

    public void SetMarkers(string layer, IReadOnlyList<MapMarker> list)
    {
        markers[layer] = list;
        InvalidateVisual();
    }

    IEnumerable<MapCell> VisibleCells() =>
        floor?.Cells.Where(c => c.Grid == grid && c.Level == level
                                && (c.Layers.Count == 0 ? visible.Contains(NoLayer) : c.Layers.All(visible.Contains))) ?? [];

    void Fit()
    {
        // Coarser levels hold a few huge cells, so fitting to every level would shrink the view.
        var cells = floor?.Cells.Where(c => c.Grid == grid && c.Level == level).ToList() ?? [];
        if (cells.Count == 0 || ActualWidth == 0)
        {
            InvalidateVisual();
            return;
        }
        var minX = cells.Min(c => c.Center.X - c.Size / 2);
        var maxX = cells.Max(c => c.Center.X + c.Size / 2);
        var minY = cells.Min(c => c.Center.Y - c.Size / 2);
        var maxY = cells.Max(c => c.Center.Y + c.Size / 2);
        scale = Math.Min(ActualWidth / (maxX - minX), ActualHeight / (maxY - minY)) * 0.95;
        offset = new Vector(ActualWidth / 2 - (minX + maxX) / 2 * scale, ActualHeight / 2 - (minY + maxY) / 2 * scale);
        InvalidateVisual();
    }

    Point ToScreen(double x, double y) => new(x * scale + offset.X, y * scale + offset.Y);

    Point ToWorld(Point p) => new((p.X - offset.X) / scale, (p.Y - offset.Y) / scale);

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        if (info.PreviousSize.Width == 0)
            Fit();
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (floor is null)
            return;

        var gray = new SolidColorBrush(Color.FromRgb(120, 120, 128));
        foreach (var cell in VisibleCells())
        {
            var layer = cell.Layers.FirstOrDefault(l => colors.ContainsKey(l));
            var corner = ToScreen(cell.Center.X - cell.Size / 2, cell.Center.Y - cell.Size / 2);
            var size = cell.Size * scale;
            dc.DrawRectangle(layer is null ? gray : colors[layer], null, new Rect(corner.X + 0.5, corner.Y + 0.5, Math.Max(size - 1, 1), Math.Max(size - 1, 1)));
        }

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (var (layer, list) in markers.Where(m => visible.Contains(m.Key)))
        {
            var brush = colors.GetValueOrDefault(layer, Brushes.White);
            var pen = new Pen(brush, 1.5);
            foreach (var marker in list)
            {
                var at = ToScreen(marker.Location.X, marker.Location.Y);
                if (marker.RadiusMeters is { } meters)
                    dc.DrawEllipse(null, pen, at, meters * 100 * scale, meters * 100 * scale);
                dc.DrawEllipse(brush, null, at, 2.5, 2.5);
                if (scale >= LabelZoom && marker.Label.Length > 0)
                    dc.DrawText(new FormattedText(marker.Label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface("Segoe UI"), 11, Brushes.White, dpi), at + new Vector(5, -7));
            }
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        var at = e.GetPosition(this);
        var world = ToWorld(at);
        scale *= e.Delta > 0 ? 1.25 : 0.8;
        offset = new Vector(at.X - world.X * scale, at.Y - world.Y * scale);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        dragFrom = e.GetPosition(this);
        CaptureMouse();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        dragFrom = null;
        ReleaseMouseCapture();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var at = e.GetPosition(this);
        if (dragFrom is { } from)
        {
            offset += at - from;
            dragFrom = at;
            InvalidateVisual();
            return;
        }
        Hover?.Invoke(Describe(at));
    }

    string Describe(Point at)
    {
        var world = ToWorld(at);
        var marker = markers.Where(m => visible.Contains(m.Key)).SelectMany(m => m.Value)
            .Select(m => (Marker: m, Distance: (ToScreen(m.Location.X, m.Location.Y) - at).Length))
            .Where(m => m.Distance < 6)
            .OrderBy(m => m.Distance)
            .Select(m => m.Marker)
            .FirstOrDefault();
        if (marker is not null)
            return $"{marker.Layer}: {marker.Kind}  \"{marker.Label}\"  at {marker.Location.X:0}, {marker.Location.Y:0}, {marker.Location.Z:0}  in cell {marker.Cell}";

        var cell = VisibleCells().FirstOrDefault(c => Math.Abs(world.X - c.Center.X) <= c.Size / 2 && Math.Abs(world.Y - c.Center.Y) <= c.Size / 2);
        var position = $"X {world.X:0}, Y {world.Y:0}";
        return cell is null
            ? position
            : $"{position}  |  cell {cell.Package}, level {cell.Level} square ({cell.X}, {cell.Y}), {cell.Size / 100:0} m, layers: {(cell.Layers.Count == 0 ? "none" : string.Join(", ", cell.Layers))}";
    }
}
