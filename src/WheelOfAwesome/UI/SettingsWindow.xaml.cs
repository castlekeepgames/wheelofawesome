using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using WheelOfAwesome.Actions;
using WheelOfAwesome.Icons;
using WheelOfAwesome.Input;
using WheelOfAwesome.Models;
using WheelOfAwesome.Services;
using MouseButton = WheelOfAwesome.Models.MouseButton;

namespace WheelOfAwesome.UI;

public partial class SettingsWindow : Window
{
    private const string DragFormat = "WheelOfAwesome.Item";

    public static readonly string[] Palette =
    {
        "#FFFFFF", "#4F8CFF", "#7B6CFF", "#B45CFF", "#FF5C9E", "#FF5A5A", "#FF8A3D", "#FFC83D", "#2ECC71", "#2EC4B6", "#9AA4B2",
    };

    private readonly AppConfig _config = ConfigService.Current;
    private Point _dragStart;
    private WheelItem? _dragItem;

    public SettingsWindow()
    {
        InitializeComponent();
        DataContext = _config;

        GlyphColors.ItemsSource = Palette;
        AccentColors.ItemsSource = Palette.Skip(1);
        Preview.Items = _config.Items;
        Preview.SliceClicked += i => ItemsList.SelectedIndex = i;

        ConfigPathText.Text = $"Saved in {Paths.ConfigFile}\nInstalled at {Paths.ExePath}";
        VersionText.Text = $"WheelOfAwesome {typeof(App).Assembly.GetName().Version?.ToString(3)}";
        UpdateElevationStatus();
        UpdatePauseButton();

        ObservableObject.AnyChanged += OnConfigChanged;
        IconService.Changed += RefreshPreview;
        ConfigService.Saved += OnSaved;
        Closed += (_, _) =>
        {
            ObservableObject.AnyChanged -= OnConfigChanged;
            IconService.Changed -= RefreshPreview;
            ConfigService.Saved -= OnSaved;
        };

        RefreshPreview();
        if (_config.Items.Count > 0) ItemsList.SelectedIndex = 0;
    }

    private void OnConfigChanged() => Dispatcher.BeginInvoke(RefreshPreview);

    private void OnSaved() => SavedText.Text = $"Saved {DateTime.Now:t}.";

    private void RefreshPreview()
    {
        Preview.ApplyAppearance(_config.Appearance);
        Preview.Items = _config.Items;
        CountText.Text = $"{_config.Items.Count} of {AppConfig.MaxItems}";
        AddButton.IsEnabled = _config.Items.Count < AppConfig.MaxItems;
        HoldHint.Text = $"Hold the {_config.Trigger.Display.ToLowerInvariant()} anywhere to open it.";
        NoExclusionsText.Visibility = _config.ExcludedApps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateTriggerWarning();
    }

    private void UpdateTriggerWarning()
    {
        var t = _config.Trigger;
        string? warning = null;
        if (!t.IsMouse && t.Key == 0)
            warning = "No trigger is set, so the wheel can't open.";
        else if (!t.IsMouse && t.Modifiers == Modifiers.None && (t.Key is >= 0x30 and <= 0x5A || t.Key == 0x20))
            warning = "A plain letter, number or Space will feel laggy while typing. A mouse button, CapsLock, an F-key or a combo works better.";
        else if (t.MouseButton == MouseButton.Right)
            warning = "Right-click still works with a quick tap, but right-drag (in games or drawing apps) will open the wheel instead.";
        TriggerWarning.Text = warning ?? "";
        TriggerWarning.Visibility = warning == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private WheelItem? Selected => ItemsList.SelectedItem as WheelItem;

    private void ItemsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Preview.Pinned = ItemsList.SelectedIndex;
        Preview.Selected = ItemsList.SelectedIndex;
    }

    // ---------- Adding & removing ----------

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var menu = AddButton.ContextMenu!;
        menu.PlacementTarget = AddButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private bool CanAdd()
    {
        if (_config.Items.Count < AppConfig.MaxItems) return true;
        MessageBox.Show(this, $"The wheel holds up to {AppConfig.MaxItems} shortcuts. Remove one to add another.", "WheelOfAwesome");
        return false;
    }

    private void AddItem(WheelItem item, int index = -1)
    {
        if (index < 0 || index > _config.Items.Count) index = _config.Items.Count;
        _config.Items.Insert(index, item);
        ItemsList.SelectedItem = item;
        ItemsList.ScrollIntoView(item);
    }

    private void AddLink_Click(object sender, RoutedEventArgs e)
    {
        if (!CanAdd()) return;
        AddItem(new WheelItem { Kind = ItemKind.Link, Label = "New link", Url = "" });
        Dispatcher.BeginInvoke(() => { UrlBox.Focus(); }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void AddApp_Click(object sender, RoutedEventArgs e)
    {
        if (!CanAdd()) return;
        var picker = new AppPickerWindow { Owner = this };
        if (picker.ShowDialog() != true) return;
        var item = new WheelItem { Kind = ItemKind.App };
        ApplyPick(item, picker);
        AddItem(item);
    }

    private void AddMacro_Click(object sender, RoutedEventArgs e)
    {
        if (!CanAdd()) return;
        var item = new WheelItem { Kind = ItemKind.Macro, Label = "New macro" };
        item.Steps.Add(new MacroStep { Kind = StepKind.Keys });
        AddItem(item);
        Dispatcher.BeginInvoke(() => LabelBox.SelectAll(), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not WheelItem item) return;
        var index = _config.Items.IndexOf(item);
        _config.Items.Remove(item);
        if (_config.Items.Count > 0) ItemsList.SelectedIndex = Math.Min(index, _config.Items.Count - 1);
    }

    // ---------- Link ----------

    private void UrlBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (Selected is not { Kind: ItemKind.Link } item) return;
        // Give fresh links a friendly name from the site.
        if ((item.Label is "" or "New link") && IconService.HostOf(item.Url) is { } host)
            item.Label = FriendlyName(host);
    }

    private static string FriendlyName(string host)
    {
        var parts = host.Split('.').Where(p => p is not ("www" or "m")).ToArray();
        var name = parts.Length >= 2 ? parts[^2] : parts.FirstOrDefault() ?? host;
        return name.Length == 0 ? host : char.ToUpper(name[0]) + name[1..];
    }

    // ---------- App ----------

    private void ChooseApp_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item) return;
        var picker = new AppPickerWindow { Owner = this };
        if (picker.ShowDialog() == true) ApplyPick(item, picker);
    }

    private static void ApplyPick(WheelItem item, AppPickerWindow picker)
    {
        if (picker.PickedApp is { } app)
        {
            item.Target = "";
            item.AppId = app.AppId;
            item.ResolvedExe = app.ExePath ?? "";
            item.Label = app.Name;
        }
        else if (picker.PickedPath is { } path)
        {
            SetPath(item, path);
        }
    }

    private void BrowseFile_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item) return;
        var dlg = new OpenFileDialog
        {
            Title = "Choose an app, shortcut or file",
            Filter = "Apps and shortcuts|*.exe;*.lnk;*.url;*.bat;*.cmd|All files|*.*",
            DereferenceLinks = false,
        };
        if (dlg.ShowDialog(this) == true) SetPath(item, dlg.FileName);
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item) return;
        var dlg = new OpenFolderDialog { Title = "Choose a folder" };
        if (dlg.ShowDialog(this) == true) SetPath(item, dlg.FolderName);
    }

    private static void SetPath(WheelItem item, string path)
    {
        item.AppId = "";
        item.ResolvedExe = "";
        item.Target = path;
        if (item.Label is "" or "New link" or "New macro" or "New app" || item.Kind != ItemKind.App)
            item.Label = Directory.Exists(path) ? new DirectoryInfo(path).Name : Path.GetFileNameWithoutExtension(path);
        item.Kind = ItemKind.App;
    }

    // ---------- Macro ----------

    private void AddStep_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item) return;
        var kind = Enum.Parse<StepKind>((string)((Button)sender).Tag);
        item.Steps.Add(new MacroStep { Kind = kind, Ms = 200 });
    }

    private void StepUp_Click(object sender, RoutedEventArgs e) => MoveStep(sender, -1);
    private void StepDown_Click(object sender, RoutedEventArgs e) => MoveStep(sender, +1);

    private void MoveStep(object sender, int delta)
    {
        if (Selected is not { } item || (sender as FrameworkElement)?.Tag is not MacroStep step) return;
        int i = item.Steps.IndexOf(step), j = i + delta;
        if (i < 0 || j < 0 || j >= item.Steps.Count) return;
        item.Steps.Move(i, j);
    }

    private void StepDelete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } item && (sender as FrameworkElement)?.Tag is MacroStep step) item.Steps.Remove(step);
    }

    // ---------- Icons ----------

    private void RefreshIcon_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } item) IconService.RefreshAuto(item);
    }

    private void PickGlyph_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item) return;
        var picker = new IconPickerWindow(item.Icon.Glyph, item.Icon.Color) { Owner = this };
        if (picker.ShowDialog() != true) return;
        item.Icon.Glyph = picker.PickedGlyph;
        item.Icon.Color = picker.PickedColor;
        item.Icon.Mode = IconMode.Library;
    }

    private void PickImage_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item) return;
        var dlg = new OpenFileDialog
        {
            Title = "Choose an icon image",
            Filter = "Images|" + string.Join(";", ImageLoader.Extensions.Select(x => "*" + x)),
        };
        if (dlg.ShowDialog(this) != true) return;
        var stored = IconService.ImportCustom(dlg.FileName);
        if (stored == null)
        {
            MessageBox.Show(this, "That image couldn't be read. Try a PNG, JPG, ICO or SVG.", "WheelOfAwesome");
            return;
        }
        item.Icon.CustomFile = stored;
        item.Icon.Mode = IconMode.Custom;
    }

    private void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string color) return;
        // The swatch template is shared; walk up to find which palette it belongs to.
        DependencyObject? d = (DependencyObject)sender;
        while (d != null && d is not ItemsControl) d = VisualTreeHelper.GetParent(d);
        if ((d as ItemsControl)?.Tag as string == "accent") _config.Appearance.Accent = color;
        else if (Selected is { } item) { item.Icon.Color = color; item.Icon.Mode = IconMode.Library; }
    }

    private void Test_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item) return;
        if (item.Kind == ItemKind.Macro)
        {
            // Give the user a moment to click into the app the macro is for.
            App.Current.Notify("Testing macro", $"\"{item.Label}\" runs in 3 seconds. Click into the app you want it to type into.");
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (_, _) => { timer.Stop(); ActionRunner.Run(item); };
            timer.Start();
            return;
        }
        ActionRunner.Run(item);
    }

    // ---------- Trigger ----------

    private void TriggerPreset_Click(object sender, RoutedEventArgs e)
    {
        var t = _config.Trigger;
        switch ((string)((Button)sender).Tag)
        {
            case "Middle": t.MouseButton = MouseButton.Middle; break;
            case "X1": t.MouseButton = MouseButton.X1; break;
            case "X2": t.MouseButton = MouseButton.X2; break;
            case "CapsLock": t.MouseButton = MouseButton.None; t.Modifiers = Modifiers.None; t.Key = 0x14; break;
            case "CtrlSpace": t.MouseButton = MouseButton.None; t.Modifiers = Modifiers.Ctrl; t.Key = 0x20; break;
        }
    }

    // ---------- Apps where the wheel stays out of the way ----------

    private void AddExcluded(string exePath)
    {
        if (_config.FindExcluded(exePath) == null) _config.ExcludedApps.Add(AppFilter.Describe(exePath));
    }

    private void RemoveExcluded_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ExcludedApp app) _config.ExcludedApps.Remove(app);
    }

    private void AddRunning_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = AddRunningButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        var apps = AppFilter.RunningApps().Where(a => _config.FindExcluded(a.Path) == null).ToList();
        if (apps.Count == 0) menu.Items.Add(new MenuItem { Header = "No other apps are open", IsEnabled = false });
        foreach (var app in apps)
        {
            var item = new MenuItem { Header = app.Name, ToolTip = app.Path, Icon = new Image { Source = app.Icon, Width = 18, Height = 18 } };
            item.Click += (_, _) => AddExcluded(app.Path);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void BrowseExcluded_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Choose the app (.exe) where the wheel shouldn't open", Filter = "Apps|*.exe" };
        if (dlg.ShowDialog(this) == true) AddExcluded(dlg.FileName);
    }

    // ---------- General ----------

    private void Startup_Click(object sender, RoutedEventArgs e)
    {
        if (App.Current.DevMode) return;
        var error = StartupService.Apply(_config.StartWithWindows, _config.RunElevated, allowPrompt: true);
        if (error != null) MessageBox.Show(this, error, "WheelOfAwesome");
    }

    private void Elevated_Click(object sender, RoutedEventArgs e)
    {
        UpdateElevationStatus();
        if (App.Current.DevMode) return;
        if (_config.StartWithWindows)
        {
            var error = StartupService.Apply(true, _config.RunElevated, allowPrompt: true);
            if (error != null)
            {
                MessageBox.Show(this, error, "WheelOfAwesome");
                _config.RunElevated = false;
                UpdateElevationStatus();
                return;
            }
        }
        if (_config.RunElevated != Elevation.IsElevated)
        {
            var verb = _config.RunElevated ? "with" : "without";
            if (MessageBox.Show(this, $"Restart WheelOfAwesome {verb} admin rights now?", "WheelOfAwesome", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                App.Current.RestartForElevation(_config.RunElevated);
        }
    }

    private void UpdateElevationStatus() =>
        ElevationStatus.Text = Elevation.IsElevated ? "Running as administrator right now." : "Running without admin rights right now.";

    private void OpenFolder_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(Paths.Root) { UseShellExecute = true });

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        // A separate process does the uninstall so it can close this one and then delete the exe.
        ConfigService.Flush();
        var psi = new ProcessStartInfo(Paths.ExePath) { UseShellExecute = false };
        foreach (var a in new[] { "--uninstall", "--config-dir", Paths.Root, "--install-dir", Installer.InstallDir }) psi.ArgumentList.Add(a);
        if (App.Current.DevMode) psi.ArgumentList.Add("--dev");
        Process.Start(psi);
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (App.Current.Controller is { } c) c.Paused = !c.Paused;
        UpdatePauseButton();
    }

    private void UpdatePauseButton() =>
        PauseButton.Content = App.Current.Controller?.Paused == true ? "Resume the wheel" : "Pause the wheel";

    // ---------- Drag & drop ----------

    private void ItemsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragItem = null;
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) != null) return;
        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is { DataContext: WheelItem item })
        {
            _dragItem = item;
            _dragStart = e.GetPosition(ItemsList);
        }
    }

    private void ItemsList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragItem == null || e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(ItemsList) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var item = _dragItem;
        _dragItem = null;
        DragDrop.DoDragDrop(ItemsList, new DataObject(DragFormat, item), DragDropEffects.Move);
        ItemsList.SelectedItem = item;
    }

    private void ItemsList_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DragFormat))
        {
            e.Effects = DragDropEffects.Move;
            var dragged = (WheelItem)e.Data.GetData(DragFormat);
            // Live reorder: the dragged row follows the pointer.
            if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is { DataContext: WheelItem over } && over != dragged)
            {
                int from = _config.Items.IndexOf(dragged), to = _config.Items.IndexOf(over);
                if (from >= 0 && to >= 0) _config.Items.Move(from, to);
            }
        }
        else if (e.Data.GetDataPresent(DataFormats.FileDrop) || ExtractUrl(e.Data) != null)
        {
            e.Effects = _config.Items.Count < AppConfig.MaxItems ? DragDropEffects.Copy : DragDropEffects.None;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void ItemsList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DragFormat)) return; // already moved during DragOver

        int insertAt = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is { DataContext: WheelItem over }
            ? _config.Items.IndexOf(over) + 1
            : _config.Items.Count;

        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            foreach (var file in files)
            {
                if (!CanAdd()) break;
                var item = file.EndsWith(".url", StringComparison.OrdinalIgnoreCase) && ReadInternetShortcut(file) is { } url
                    ? new WheelItem { Kind = ItemKind.Link, Url = url, Label = Path.GetFileNameWithoutExtension(file) }
                    : NewAppItem(file);
                AddItem(item, insertAt++);
            }
        }
        else if (ExtractUrl(e.Data) is { } url && CanAdd())
        {
            var host = IconService.HostOf(url);
            AddItem(new WheelItem { Kind = ItemKind.Link, Url = url, Label = host != null ? FriendlyName(host) : "Link" }, insertAt);
        }
    }

    private static WheelItem NewAppItem(string path)
    {
        var item = new WheelItem { Kind = ItemKind.App };
        SetPath(item, path);
        return item;
    }

    private static string? ExtractUrl(IDataObject data)
    {
        foreach (var format in new[] { "UniformResourceLocatorW", DataFormats.UnicodeText, DataFormats.Text })
        {
            if (!data.GetDataPresent(format)) continue;
            string? text = data.GetData(format) switch
            {
                string s => s,
                MemoryStream ms => System.Text.Encoding.Unicode.GetString(ms.ToArray()),
                _ => null,
            };
            text = text?.Trim('\0', ' ', '\r', '\n').Split('\n')[0].Trim();
            if (text != null && Uri.TryCreate(text, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https"))
                return text;
        }
        return null;
    }

    private static string? ReadInternetShortcut(string file)
    {
        try
        {
            return File.ReadLines(file).FirstOrDefault(l => l.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))?[4..];
        }
        catch { return null; }
    }

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null)
        {
            if (d is T t) return t;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }
}
