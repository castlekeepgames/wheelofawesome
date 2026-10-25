using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.Json.Serialization;
using System.Windows.Media;
using WheelOfAwesome.Icons;
using WheelOfAwesome.Input;
using WheelOfAwesome.Services;

namespace WheelOfAwesome.Models;

public enum ItemKind { Link, App, Macro }
public enum IconMode { Auto, Library, Custom }
public enum StepKind { Keys, Wait, Text }
public enum MouseButton { None, Left, Right, Middle, X1, X2 }

[Flags]
public enum Modifiers { None = 0, Ctrl = 1, Shift = 2, Alt = 4, Win = 8 }

public sealed class AppConfig : ObservableObject
{
    public const int MaxItems = 12;

    private int _holdThresholdMs = 150;
    private bool _startWithWindows = true;
    private bool _runElevated;
    private bool _welcomeShown;
    private bool _pauseInFullscreen = true;

    public int Version { get; set; } = 1;
    public TriggerConfig Trigger { get; set; } = new();
    public Appearance Appearance { get; set; } = new();

    public int HoldThresholdMs { get => _holdThresholdMs; set => Set(ref _holdThresholdMs, Math.Clamp(value, 50, 1000)); }
    public bool StartWithWindows { get => _startWithWindows; set => Set(ref _startWithWindows, value); }
    public bool RunElevated { get => _runElevated; set => Set(ref _runElevated, value); }
    public bool WelcomeShown { get => _welcomeShown; set => Set(ref _welcomeShown, value); }

    /// <summary>Leave the trigger alone while a full-screen app (game, video, slideshow) is in front.</summary>
    public bool PauseInFullscreen { get => _pauseInFullscreen; set => Set(ref _pauseInFullscreen, value); }

    public ObservableCollection<WheelItem> Items { get; set; } = new();

    /// <summary>Apps where the trigger is always left alone, matched by exe file name.</summary>
    public ObservableCollection<ExcludedApp> ExcludedApps { get; set; } = new();

    /// <summary>Wires collection-change notifications into the dirty tracking after load.</summary>
    public void Attach()
    {
        Items.CollectionChanged += OnCollectionChanged;
        foreach (var item in Items) item.Attach();
        ExcludedApps.CollectionChanged += (_, _) => { AppFilter.SetExcluded(ExcludedApps); RaiseAnyChanged(); };
        AppFilter.SetExcluded(ExcludedApps);
    }

    public ExcludedApp? FindExcluded(string exePath) =>
        ExcludedApps.FirstOrDefault(a => a.ExeName.Equals(System.IO.Path.GetFileName(exePath), StringComparison.OrdinalIgnoreCase));

    internal static void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (var item in e.NewItems.OfType<WheelItem>()) item.Attach();
        RaiseAnyChanged();
    }
}

public sealed class TriggerConfig : ObservableObject
{
    private MouseButton _mouseButton = MouseButton.Middle;
    private int _key;
    private Modifiers _modifiers;

    /// <summary>When not None, the trigger is this mouse button and Key/Modifiers are ignored.</summary>
    public MouseButton MouseButton { get => _mouseButton; set { if (Set(ref _mouseButton, value)) OnPropertyChanged(nameof(Display)); } }
    /// <summary>Virtual-key code for keyboard triggers.</summary>
    public int Key { get => _key; set { if (Set(ref _key, value)) OnPropertyChanged(nameof(Display)); } }
    public Modifiers Modifiers { get => _modifiers; set { if (Set(ref _modifiers, value)) OnPropertyChanged(nameof(Display)); } }

    [JsonIgnore] public bool IsMouse => MouseButton != MouseButton.None;
    [JsonIgnore] public string Display => IsMouse ? KeyNames.Mouse(MouseButton) : KeyNames.Combo(Modifiers, Key);
}

public sealed class Appearance : ObservableObject
{
    private int _radius = 170;
    private string _accent = "#4F8CFF";
    private bool _showSliceLabels;

    public int Radius { get => _radius; set => Set(ref _radius, Math.Clamp(value, 110, 300)); }
    public string Accent { get => _accent; set => Set(ref _accent, value); }
    public bool ShowSliceLabels { get => _showSliceLabels; set => Set(ref _showSliceLabels, value); }

    [JsonIgnore]
    public Color AccentColor
    {
        get
        {
            try { return (Color)ColorConverter.ConvertFromString(Accent); }
            catch { return Color.FromRgb(0x4F, 0x8C, 0xFF); }
        }
    }
}

public sealed class IconSpec : ObservableObject
{
    private IconMode _mode = IconMode.Auto;
    private string _glyph = "star";
    private string _color = "#FFFFFF";
    private string? _customFile;

    public IconMode Mode { get => _mode; set => Set(ref _mode, value); }
    /// <summary>Lucide icon name, used when Mode is Library.</summary>
    public string Glyph { get => _glyph; set => Set(ref _glyph, value); }
    public string Color { get => _color; set => Set(ref _color, value); }
    /// <summary>File name inside the app's icon folder, used when Mode is Custom.</summary>
    public string? CustomFile { get => _customFile; set => Set(ref _customFile, value); }
}

public sealed class ExcludedApp : ObservableObject
{
    private ImageSource? _icon;

    public string Name { get; set; } = "";
    /// <summary>What's matched: the exe file name, so it survives updates that move the app's folder.</summary>
    public string ExeName { get; set; } = "";
    public string Path { get; set; } = "";

    [JsonIgnore]
    public ImageSource Icon => _icon ??= (System.IO.File.Exists(Path) ? Native.Shell.GetIcon(Path, 48) : null) ?? IconService.Glyph("app-window", "#9AA4B2");
}

public sealed class MacroStep : ObservableObject
{
    private StepKind _kind = StepKind.Keys;
    private int _key;
    private Modifiers _modifiers;
    private int _ms = 100;
    private string _text = "";

    public StepKind Kind { get => _kind; set => Set(ref _kind, value); }
    public int Key { get => _key; set => Set(ref _key, value); }
    public Modifiers Modifiers { get => _modifiers; set => Set(ref _modifiers, value); }
    public int Ms { get => _ms; set => Set(ref _ms, Math.Clamp(value, 0, 60000)); }
    public string Text { get => _text; set => Set(ref _text, value ?? ""); }

    [JsonIgnore]
    public string Summary => Kind switch
    {
        StepKind.Keys => Key == 0 ? "(no keys)" : KeyNames.Combo(Modifiers, Key),
        StepKind.Wait => $"wait {Ms} ms",
        _ => $"type \"{Text}\"",
    };
}

public sealed class WheelItem : ObservableObject
{
    private string _label = "";
    private ItemKind _kind;
    private string _url = "";
    private string _target = "";
    private string _appId = "";
    private string _resolvedExe = "";
    private bool _focusIfRunning = true;
    private bool _attached;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Label { get => _label; set => Set(ref _label, value ?? ""); }
    public ItemKind Kind { get => _kind; set { if (Set(ref _kind, value)) Refresh(); } }

    /// <summary>Link: the URL to open in Chrome.</summary>
    public string Url { get => _url; set { if (Set(ref _url, value ?? "")) Refresh(); } }

    /// <summary>App: a file-system path (exe, shortcut, document or folder).</summary>
    public string Target { get => _target; set { if (Set(ref _target, value ?? "")) Refresh(); } }
    /// <summary>App: an AppUserModelID from the installed-apps list (launched via shell:AppsFolder).</summary>
    public string AppId { get => _appId; set { if (Set(ref _appId, value ?? "")) Refresh(); } }
    /// <summary>App: the exe behind <see cref="AppId"/> when known, used to find running windows.</summary>
    public string ResolvedExe { get => _resolvedExe; set => Set(ref _resolvedExe, value ?? ""); }
    public bool FocusIfRunning { get => _focusIfRunning; set => Set(ref _focusIfRunning, value); }

    public ObservableCollection<MacroStep> Steps { get; set; } = new();
    public IconSpec Icon { get; set; } = new();

    [JsonIgnore] public ImageSource IconImage => IconService.Get(this);

    [JsonIgnore]
    public string Subtitle => Kind switch
    {
        ItemKind.Link => string.IsNullOrWhiteSpace(Url) ? "Link: no URL yet" : Url,
        ItemKind.App => !string.IsNullOrEmpty(AppId) ? "Installed app" : string.IsNullOrEmpty(Target) ? "App: nothing chosen" : Target,
        _ => Steps.Count == 0 ? "Macro: no steps" : string.Join(", ", Steps.Select(s => s.Summary)),
    };

    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        Icon.PropertyChanged += (_, _) => Refresh();
        Steps.CollectionChanged += (s, e) => { AppConfig.OnCollectionChanged(s, e); Refresh(); };
        foreach (var step in Steps) step.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Subtitle));
        Steps.CollectionChanged += (_, e) =>
        {
            if (e.NewItems == null) return;
            foreach (var step in e.NewItems.OfType<MacroStep>()) step.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Subtitle));
        };
    }

    /// <summary>Re-evaluates the derived icon and subtitle (e.g. after an icon finished downloading).</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(IconImage));
        OnPropertyChanged(nameof(Subtitle));
    }
}
