using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using WheelOfAwesome.Models;

namespace WheelOfAwesome.Services;

public static class ConfigService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private static DispatcherTimer? _saveTimer;

    public static AppConfig Current { get; private set; } = new();
    public static bool IsFirstRun { get; private set; }

    /// <summary>Raised (on the UI thread) after the config has been written to disk.</summary>
    public static event Action? Saved;

    public static void Load()
    {
        AppConfig? config = null;
        try
        {
            if (File.Exists(Paths.ConfigFile))
                config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(Paths.ConfigFile), Options);
        }
        catch (Exception ex)
        {
            Log.Error("Config could not be read; backing it up and starting fresh", ex);
            try { File.Copy(Paths.ConfigFile, Paths.ConfigFile + ".broken", overwrite: true); } catch { }
        }

        if (config == null)
        {
            IsFirstRun = !File.Exists(Paths.ConfigFile);
            config = Defaults.Create();
        }

        Current = config;
        Current.Attach();
        if (IsFirstRun) SaveNow();

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };
        ObservableObject.AnyChanged += () => { _saveTimer.Stop(); _saveTimer.Start(); };
    }

    public static void SaveNow()
    {
        try
        {
            var json = JsonSerializer.Serialize(Current, Options);
            var tmp = Paths.ConfigFile + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, Paths.ConfigFile, overwrite: true);
            Saved?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error("Saving config failed", ex);
        }
    }

    public static void Flush()
    {
        if (_saveTimer?.IsEnabled == true)
        {
            _saveTimer.Stop();
            SaveNow();
        }
    }

    public static string Serialize(AppConfig config) => JsonSerializer.Serialize(config, Options);
    public static AppConfig? Deserialize(string json) => JsonSerializer.Deserialize<AppConfig>(json, Options);
}
