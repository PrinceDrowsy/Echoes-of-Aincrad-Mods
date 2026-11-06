using System.ComponentModel;

namespace Cardinal;

/// <summary>One row of the table grid: its cells as shown, and which of them the open mod project changes.</summary>
sealed class GridRow(int index, string[] cells) : INotifyPropertyChanged
{
    public int Index { get; } = index;
    public string[] Cells { get; } = cells;
    public bool[] Changed { get; } = new bool[cells.Length];

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Set(int column, string text, bool changed)
    {
        Cells[column] = text;
        Changed[column] = changed;
        // Cell bindings use paths like Cells[3], which refresh when the whole array is reported changed.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Cells)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Changed)));
    }
}

/// <summary>Rows for the lists on the Merge and Installed mods tabs.</summary>
sealed record MergeEntry(int Order, string Name, string Version, int Changes, string Path);

sealed record ClashRow(string Table, string Row, string Field, string Values, string Winner);

sealed record InstalledRow(string Name, int Files, int Conflicts, int Unused);

sealed record ConflictRow(string Asset, string Outcome);
