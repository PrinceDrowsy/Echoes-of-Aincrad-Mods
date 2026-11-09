using System.ComponentModel;

namespace Cardinal;

/// <summary>
/// One row of the table grid: its cells as shown, and which of them the open mod project changes. Added is set for a row
/// the project adds, whose Index is then the game row it copies.
/// </summary>
sealed class GridRow(int index, string[] cells) : INotifyPropertyChanged
{
    public int Index { get; } = index;
    public string[] Cells { get; } = cells;
    public bool[] Changed { get; } = new bool[cells.Length];
    public Mods.RowAddition? Added { get; set; }

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

/// <summary>One line on the Text tab: the language's text as the open mod project has it, next to the English.</summary>
sealed class TextRow(string ns, string key, string english, string text, bool changed, bool added = false) : INotifyPropertyChanged
{
    public string Namespace { get; } = ns;
    public string Key { get; } = key;
    public string English { get; } = english;

    /// <summary>Whether the open project adds this line rather than changing one of the game's.</summary>
    public bool Added { get; } = added;

    // Settable only because the grid's text column is two-way; TextEdited decides what the project keeps.
    public string Text
    {
        get => text;
        set
        {
            text = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
        }
    }

    public bool Changed
    {
        get => changed;
        set
        {
            changed = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Changed)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>One change in the Mod tab's list. Edit is the ModEdit, TextEdit, RowAddition or AddedText it shows.</summary>
sealed record ChangeRow(string Kind, string What, string? Original, string Value, object Edit);

/// <summary>Rows for the lists on the Merge and Installed mods tabs.</summary>
sealed record MergeEntry(int Order, string Name, string Version, int Changes, string Path);

sealed record ClashRow(string Change, string Values, string Winner);

sealed record InstalledRow(string Name, int Files, int Conflicts, int Unused);

sealed record ConflictRow(string Asset, string Outcome);

sealed record CultureItem(string Code, string Name);

sealed record StepRow(string Number, string Text, string Then, string Kind, string Settings, string Leads, string Id,
    string? TextKey = null, bool Edited = false);
