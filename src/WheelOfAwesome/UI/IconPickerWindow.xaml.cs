using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WheelOfAwesome.Icons;

namespace WheelOfAwesome.UI;

public partial class IconPickerWindow : Window, INotifyPropertyChanged
{
    private const double CellWidth = 60;
    private List<LucideIcon> _results = new();
    private int _columns = 10;
    private Brush _tint = Brushes.White;

    public IconPickerWindow(string glyph, string color)
    {
        InitializeComponent();
        DataContext = this;
        PickedGlyph = glyph;
        PickedColor = color;
        ColorList.ItemsSource = SettingsWindow.Palette;
        Tint = new SolidColorBrush(IconService.ParseColor(color));
        ShowSelection();
        Filter();
        Loaded += (_, _) => SearchBox.Focus();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string PickedGlyph { get; private set; }
    public string PickedColor { get; private set; }

    public Brush Tint
    {
        get => _tint;
        private set { _tint = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Tint))); }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Filter();
    }

    private void Filter()
    {
        _results = LucideIcons.Search(SearchBox.Text).ToList();
        CountText.Text = _results.Count == 1 ? "1 icon" : $"{_results.Count:N0} icons";
        Layout();
    }

    private void Rows_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        int cols = Math.Max(4, (int)((Rows.ActualWidth - 30) / CellWidth));
        if (cols != _columns) { _columns = cols; Layout(); }
    }

    private void Layout() => Rows.ItemsSource = _results.Chunk(_columns).ToList();

    private void Cell_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not LucideIcon icon) return;
        PickedGlyph = icon.Name;
        ShowSelection();
        if (e.ClickCount >= 2) Ok_Click(sender, e);
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string color) return;
        PickedColor = color;
        Tint = new SolidColorBrush(IconService.ParseColor(color));
        ShowSelection();
    }

    private void ShowSelection()
    {
        var icon = LucideIcons.Find(PickedGlyph);
        SelectedGlyph.Text = icon?.Glyph ?? "";
        SelectedGlyph.Foreground = Tint;
        SelectedName.Text = icon?.Name ?? "Pick an icon";
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (LucideIcons.Find(PickedGlyph) == null) return;
        DialogResult = true;
    }
}
