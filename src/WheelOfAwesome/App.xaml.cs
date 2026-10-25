using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using WheelOfAwesome.Actions;
using WheelOfAwesome.Dev;
using WheelOfAwesome.Icons;
using WheelOfAwesome.Services;
using WheelOfAwesome.UI;
using WheelOfAwesome.Wheel;

namespace WheelOfAwesome;

public partial class App : Application
{
    private Mutex? _instanceMutex;
    private EventWaitHandle? _showSettingsSignal;
    private TrayIcon? _tray;
    private SettingsWindow? _settings;

    public WheelController? Controller { get; private set; }
    public bool DevMode { get; private set; }
    public static new App Current => (App)Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = new Args(e.Args);
        DevMode = args.Has("--dev");
        if (args.Value("--config-dir") is { } dir) Paths.Root = dir;

        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Error("Unhandled UI exception", ex.Exception);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Log.Error("Unhandled exception", ex.ExceptionObject as Exception);

        if (args.Value("--wait-for-pid") is { } pidText && int.TryParse(pidText, out var pid))
        {
            try { Process.GetProcessById(pid).WaitForExit(10000); } catch { }
        }

        // One-off developer modes that render or test and then exit.
        if (args.Value("--render-ui") is { } renderDir) { DevTools.RenderUi(renderDir); Shutdown(); return; }
        if (args.Value("--selftest") is { } reportFile) { DevTools.SelfTest(reportFile); return; }

        if (!AcquireSingleInstance())
        {
            Shutdown();
            return;
        }

        ConfigService.Load();
        var config = ConfigService.Current;

        if (config.RunElevated && !Elevation.IsElevated && !DevMode && !args.Has("--no-elevate"))
        {
            ReleaseSingleInstance();
            if (Elevation.RelaunchElevated(e.Args))
            {
                Shutdown();
                return;
            }
            // Declined: carry on without admin rights.
            if (!AcquireSingleInstance()) { Shutdown(); return; }
        }

        var overlay = new OverlayWindow();
        overlay.Prepare();
        Controller = new WheelController(Dispatcher, overlay);
        Controller.Start();

        _tray = new TrayIcon();
        _tray.OpenSettings += ShowSettings;
        _tray.PauseChanged += paused => Controller.Paused = paused;
        _tray.Exit += ExitApp;
        ActionRunner.Failed += msg => Dispatcher.BeginInvoke(() => _tray?.Notify("WheelOfAwesome", msg, warning: true));
        IconService.Changed += () => { foreach (var item in ConfigService.Current.Items) item.Refresh(); };

        if (!DevMode)
        {
            var error = StartupService.Apply(config.StartWithWindows, config.RunElevated, allowPrompt: false);
            if (error != null) Log.Info(error);
        }

        if (!config.WelcomeShown)
        {
            config.WelcomeShown = true;
            _tray.Notify("WheelOfAwesome is running", $"Hold the {config.Trigger.Display.ToLowerInvariant()} to open your wheel. Click the tray icon for settings.");
            if (!args.Has("--startup")) ShowSettings();
        }
        else if (args.Has("--settings"))
        {
            ShowSettings();
        }

        if (args.Value("--live-test-log") is { } liveLog) DevTools.AttachLiveTestLog(Controller, liveLog);
        Log.Info($"Started (elevated: {Elevation.IsElevated}, dev: {DevMode}, config: {Paths.Root})");
    }

    public void ShowSettings()
    {
        if (_settings == null)
        {
            _settings = new SettingsWindow();
            _settings.Closed += (_, _) => { _settings = null; ConfigService.Flush(); };
            _settings.Show();
        }
        if (_settings.WindowState == WindowState.Minimized) _settings.WindowState = WindowState.Normal;
        _settings.Activate();
        Native.Win32.SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(_settings).Handle);
    }

    /// <summary>Restarts with/without admin rights after the user flips that setting.</summary>
    public void RestartForElevation(bool elevated)
    {
        ConfigService.Flush();
        if (elevated)
        {
            ReleaseSingleInstance();
            if (Elevation.RelaunchElevated("--settings")) { ExitApp(); return; }
            AcquireSingleInstance();
            _tray?.Notify("WheelOfAwesome", "Admin mode stays off until you approve the prompt.", warning: true);
        }
        else
        {
            ReleaseSingleInstance();
            Elevation.RelaunchNormal();
            ExitApp();
        }
    }

    public void Notify(string title, string text, bool warning = false) => _tray?.Notify(title, text, warning);

    public void ExitApp()
    {
        ConfigService.Flush();
        Controller?.Dispose();
        _tray?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }

    private bool AcquireSingleInstance()
    {
        // Scoped to the config folder so a dev/test instance can run beside the real one.
        var scope = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(Paths.Root.ToLowerInvariant())))[..12];
        var name = $"WheelOfAwesome_{scope}";

        // A restarting instance may still be shutting down; give it a moment.
        for (int attempt = 0; attempt < 30; attempt++)
        {
            _instanceMutex = new Mutex(true, name + "_mutex", out var created);
            if (created)
            {
                _showSettingsSignal = new EventWaitHandle(false, EventResetMode.AutoReset, name + "_show");
                var signal = _showSettingsSignal;
                var thread = new Thread(() =>
                {
                    try
                    {
                        while (signal.WaitOne())
                            Dispatcher.BeginInvoke(ShowSettings);
                    }
                    catch (ObjectDisposedException) { }
                })
                { IsBackground = true };
                thread.Start();
                return true;
            }
            _instanceMutex.Dispose();
            _instanceMutex = null;
            if (attempt == 0 && EventWaitHandle.TryOpenExisting(name + "_show", out var existing))
            {
                // Already running: ask that copy to show its settings, unless we're a restart waiting our turn.
                if (!Environment.GetCommandLineArgs().Contains("--wait-for-pid") && !Environment.GetCommandLineArgs().Contains("--startup"))
                {
                    existing.Set();
                    existing.Dispose();
                    return false;
                }
                existing.Dispose();
            }
            Thread.Sleep(200);
        }
        return false;
    }

    private void ReleaseSingleInstance()
    {
        try { _instanceMutex?.ReleaseMutex(); } catch { }
        _instanceMutex?.Dispose();
        _instanceMutex = null;
        _showSettingsSignal?.Dispose();
        _showSettingsSignal = null;
    }

    private sealed class Args
    {
        private readonly string[] _args;
        public Args(string[] args) => _args = args;
        public bool Has(string name) => _args.Contains(name, StringComparer.OrdinalIgnoreCase);
        public string? Value(string name)
        {
            var i = Array.FindIndex(_args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < _args.Length ? _args[i + 1] : null;
        }
    }
}
