using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using WheelOfAwesome.Icons;
using WheelOfAwesome.Native;

namespace WheelOfAwesome.UI;

public sealed class AppEntryView : INotifyPropertyChanged
{
    private ImageSource? _icon;

    public AppEntryView(AppEntry entry)
    {
        Entry = entry;
        _icon = IconService.Glyph("app-window", "#9AA4B2");
    }

    public AppEntry Entry { get; }
    public string Name => Entry.Name;
    public string Detail => Entry.ExePath ?? (Entry.AppId.Contains('!') ? "Microsoft Store app" : Entry.AppId);

    public ImageSource? Icon
    {
        get => _icon;
        set { _icon = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Searchable list of everything in the Start menu's "All apps", including Store apps.</summary>
public partial class AppPickerWindow : Window
{
    // The list rarely changes while the app runs; enumerate once per session.
    private static List<AppEntryView>? _cache;

    private ICollectionView? _view;

    public AppPickerWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => { SearchBox.Focus(); Load(); };
    }

    public AppEntry? PickedApp { get; private set; }
    public string? PickedPath { get; private set; }

    private void Load()
    {
        if (_cache != null) { Show(_cache); return; }
        IconService.RunOnShellThread(() =>
        {
            var apps = Shell.EnumerateInstalledApps()
                // Uninstallers, help files and web links in the Start menu aren't what anyone wants on a wheel.
                .Where(a => !a.Name.StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase) && !a.AppId.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                .ToList();
            Dispatcher.Invoke(() =>
            {
                _cache = apps.Select(a => new AppEntryView(a)).ToList();
                Show(_cache);
            });
            // Load icons progressively after the list is visible.
            foreach (var view in _cache!)
            {
                var icon = Shell.GetIcon(@"shell:AppsFolder\" + view.Entry.AppId, 48);
                if (icon != null) Dispatcher.BeginInvoke(() => view.Icon = icon);
            }
        });
    }

    private void Show(List<AppEntryView> apps)
    {
        LoadingText.Visibility = apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (apps.Count == 0) LoadingText.Text = "No apps found. Use “Browse for a file instead”.";
        _view = CollectionViewSource.GetDefaultView(apps);
        _view.Filter = o => Matches((AppEntryView)o);
        AppsList.ItemsSource = _view;
        if (!AppsList.Items.IsEmpty) AppsList.SelectedIndex = 0;
    }

    private bool Matches(AppEntryView app)
    {
        var q = SearchBox.Text.Trim();
        return q.Length == 0 || app.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _view?.Refresh();
        if (!AppsList.Items.IsEmpty) AppsList.SelectedIndex = 0;
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Arrow keys move through results while typing.
        if (e.Key == Key.Down && AppsList.SelectedIndex < AppsList.Items.Count - 1) { AppsList.SelectedIndex++; AppsList.ScrollIntoView(AppsList.SelectedItem); e.Handled = true; }
        else if (e.Key == Key.Up && AppsList.SelectedIndex > 0) { AppsList.SelectedIndex--; AppsList.ScrollIntoView(AppsList.SelectedItem); e.Handled = true; }
    }

    private void AppsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (AppsList.SelectedItem != null) Ok_Click(sender, e);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (AppsList.SelectedItem is not AppEntryView app) return;
        PickedApp = app.Entry;
        DialogResult = true;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Choose an app, shortcut or file",
            Filter = "Apps and shortcuts|*.exe;*.lnk;*.url;*.bat;*.cmd|All files|*.*",
            DereferenceLinks = false,
        };
        if (dlg.ShowDialog(this) != true) return;
        PickedPath = dlg.FileName;
        DialogResult = true;
    }
}
