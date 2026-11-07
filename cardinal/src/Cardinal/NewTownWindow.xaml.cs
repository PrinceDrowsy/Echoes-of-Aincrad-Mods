using System.Windows;

namespace Cardinal;

/// <summary>Asks for a new town's ID and name. The caller adds it, so every check happens in one place.</summary>
public partial class NewTownWindow : Window
{
    public int Id { get; private set; }
    public string TownName => NameBox.Text.Trim();

    public NewTownWindow(string copying, int nextId)
    {
        InitializeComponent();
        Intro.Text = $"A new town that starts as a copy of {copying}: same map, gate, shops and start points, with its own ID " +
                     "and name. Change anything else in its row once it's added, such as when the story opens it.";
        IdBox.Text = nextId.ToString("000");
        NameBox.Focus();
    }

    void AddClicked(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(IdBox.Text.Trim(), out var id) || id is <= 0 or > 999)
        {
            MessageBox.Show(this, "The town ID must be a whole number from 1 to 999.", "Cardinal", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (TownName.Length == 0)
        {
            MessageBox.Show(this, "The town needs an English name.", "Cardinal", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Id = id;
        DialogResult = true;
    }
}
