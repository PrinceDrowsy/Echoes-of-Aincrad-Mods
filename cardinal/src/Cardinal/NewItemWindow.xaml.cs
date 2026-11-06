using System.Windows;

namespace Cardinal;

/// <summary>Asks for a new item's ID, name and description. The caller adds it, so every check happens in one place.</summary>
public partial class NewItemWindow : Window
{
    public int Id { get; private set; }
    public string ItemName => NameBox.Text.Trim();
    public string Description => DescriptionBox.Text.Trim();

    public NewItemWindow(string kind, string copying, int nextId)
    {
        InitializeComponent();
        Intro.Text = $"A new {kind} that starts as a copy of {copying}: same icon, rarity, prices and effects, with its own " +
                     "ID, name and description. Change anything else in the Tables tab once it's added.";
        IdBox.Text = nextId.ToString();
        NameBox.Focus();
    }

    void AddClicked(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(IdBox.Text.Trim(), out var id) || id <= 0)
        {
            MessageBox.Show(this, "The ID must be a whole number above 0.", "Cardinal", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (ItemName.Length == 0 || Description.Length == 0)
        {
            MessageBox.Show(this, "The item needs an English name and description.", "Cardinal", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Id = id;
        DialogResult = true;
    }
}
