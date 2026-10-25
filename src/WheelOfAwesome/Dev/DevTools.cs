using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WheelOfAwesome.Actions;
using WheelOfAwesome.Icons;
using WheelOfAwesome.Input;
using WheelOfAwesome.Models;
using WheelOfAwesome.Native;
using WheelOfAwesome.Services;
using WheelOfAwesome.UI;
using WheelOfAwesome.Wheel;
using MouseButton = WheelOfAwesome.Models.MouseButton;

namespace WheelOfAwesome.Dev;

/// <summary>
/// Developer-only modes: render the UI to PNGs (--render-ui), run checks (--selftest), and log fired actions for
/// scripted end-to-end tests (--live-test-log). Use with --config-dir so real settings are never touched.
/// </summary>
public static class DevTools
{
    public static void RenderUi(string dir)
    {
        Directory.CreateDirectory(dir);
        ConfigService.Load();
        var items = ConfigService.Current.Items;
        foreach (var item in items) _ = item.IconImage;
        WaitUntil(() => IconService.PendingCount == 0, 20000);
        foreach (var item in items) item.Refresh();

        var wheel = new WheelView();
        wheel.ApplyAppearance(ConfigService.Current.Appearance);
        wheel.Items = items.ToList();
        wheel.SetPointer(80, -40, 10);
        Render(wheel, Path.Combine(dir, "wheel.png"), Color.FromRgb(0x3A, 0x4A, 0x5E));

        wheel.ShowSliceLabels = true;
        wheel.SetPointer(-30, 80, 10);
        Render(wheel, Path.Combine(dir, "wheel-labels.png"), Color.FromRgb(0xE8, 0xEC, 0xF0));

        wheel.ShowSliceLabels = false;
        wheel.Items = items.Take(3).ToList();
        wheel.SetPointer(0, -50, 10);
        Render(wheel, Path.Combine(dir, "wheel-3.png"), Color.FromRgb(0x3A, 0x4A, 0x5E));

        var settings = new SettingsWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = -6000, Top = 0, ShowActivated = false };
        settings.Show();
        Wait(800);
        RenderWindow(settings, Path.Combine(dir, "settings-shortcuts.png"));
        settings.ItemsList.SelectedIndex = 4;
        Wait(300);
        RenderWindow(settings, Path.Combine(dir, "settings-app.png"));
        settings.ItemsList.SelectedIndex = 6;
        Wait(300);
        RenderWindow(settings, Path.Combine(dir, "settings-macro.png"));
        settings.Tabs.SelectedIndex = 1;
        Wait(400);
        RenderWindow(settings, Path.Combine(dir, "settings-trigger.png"));
        settings.Tabs.SelectedIndex = 2;
        Wait(400);
        RenderWindow(settings, Path.Combine(dir, "settings-general.png"));
        settings.Close();

        var picker = new IconPickerWindow("star", "#FFC83D") { WindowStartupLocation = WindowStartupLocation.Manual, Left = -6000, Top = 0, ShowActivated = false };
        picker.Show();
        Wait(600);
        RenderWindow(picker, Path.Combine(dir, "icon-picker.png"));
        picker.SearchBox.Text = "music";
        Wait(300);
        RenderWindow(picker, Path.Combine(dir, "icon-picker-search.png"));
        picker.Close();

        var apps = new AppPickerWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = -6000, Top = 0, ShowActivated = false };
        apps.Show();
        WaitUntil(() => apps.AppsList.Items.Count > 0, 15000);
        Wait(2500);
        RenderWindow(apps, Path.Combine(dir, "app-picker.png"));
        apps.Close();
    }

    public static void SelfTest(string reportFile)
    {
        var report = new StringBuilder();
        int pass = 0, fail = 0;
        void Check(string name, bool ok, string detail = "")
        {
            if (ok) pass++; else fail++;
            report.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? "  -- " + detail : "")}");
        }

        try { EngineTests(Check); }
        catch (Exception ex) { Check("engine tests crashed", false, ex.ToString()); }

        // Config
        var cfg = Defaults.Create();
        var back = ConfigService.Deserialize(ConfigService.Serialize(cfg));
        Check("config round-trip", back != null && back.Items.Count == cfg.Items.Count
            && back.Items[6].Steps[0].Modifiers == (Modifiers.Win | Modifiers.Shift) && back.Items[4].AppId == cfg.Items[4].AppId
            && back.Trigger.MouseButton == MouseButton.Middle);

        // Key names
        Check("combo display", KeyNames.Combo(Modifiers.Win | Modifiers.Shift, 0x53) == "Win + Shift + S", KeyNames.Combo(Modifiers.Win | Modifiers.Shift, 0x53));
        Check("combo display 2", KeyNames.Combo(Modifiers.Ctrl | Modifiers.Shift, 0x1B) == "Ctrl + Shift + Esc", KeyNames.Combo(Modifiers.Ctrl | Modifiers.Shift, 0x1B));

        // Icon library
        Check("lucide index", LucideIcons.All.Count > 2000, $"{LucideIcons.All.Count} icons");
        Check("lucide search", LucideIcons.Search("music").Any(), string.Join(",", LucideIcons.Search("music").Take(5).Select(i => i.Name)));
        var glyph = LucideIcons.Render("globe", Colors.White);
        Check("lucide render", glyph is DrawingImage di && ((DrawingGroup)di.Drawing).Children.Count == 2);

        // Installed apps + shell icons
        var sw = Stopwatch.StartNew();
        var apps = Shell.EnumerateInstalledApps();
        Check("installed apps", apps.Count > 10, $"{apps.Count} apps in {sw.ElapsedMilliseconds} ms; {apps.Count(a => a.ExePath != null)} with exe; e.g. {string.Join(", ", apps.Take(6).Select(a => a.Name))}");
        var calc = apps.FirstOrDefault(a => a.AppId.StartsWith("Microsoft.WindowsCalculator", StringComparison.OrdinalIgnoreCase));
        Check("calculator listed", calc != null, calc?.AppId ?? "");
        var chromeEntry = apps.FirstOrDefault(a => a.Name.Contains("Chrome", StringComparison.OrdinalIgnoreCase));
        if (chromeEntry != null) Check("chrome entry resolves exe", chromeEntry.ExePath != null, $"{chromeEntry.AppId} -> {chromeEntry.ExePath}");

        var notepadIcon = Shell.GetIcon(@"C:\Windows\System32\notepad.exe", 256);
        Check("exe icon", notepadIcon != null && notepadIcon.PixelWidth >= 48, $"{notepadIcon?.PixelWidth}px");
        if (calc != null)
        {
            var calcIcon = Shell.GetIcon(@"shell:AppsFolder\" + calc.AppId, 256);
            Check("store app icon", calcIcon != null && calcIcon.PixelWidth >= 48, $"{calcIcon?.PixelWidth}px");
        }

        // Chrome + windows
        Check("chrome found", ChromeLocator.Find() != null, ChromeLocator.Find() ?? "not installed (links fall back to the default browser)");
        var explorerWindows = WindowFocus.FindWindows(@"C:\Windows\explorer.exe", null);
        Check("window enumeration", true, $"{explorerWindows.Count} Explorer windows");

        // Favicons
        foreach (var site in new[] { "https://www.youtube.com", "https://github.com", "https://mail.google.com", "https://www.reddit.com" })
        {
            sw.Restart();
            var task = FaviconFetcher.DownloadCandidatesAsync(site);
            WaitUntil(() => task.IsCompleted, 20000);
            var best = task.IsCompletedSuccessfully ? FaviconFetcher.PickBest(task.Result) : null;
            Check($"favicon {new Uri(site).Host}", best != null, best == null ? "none" : $"{ImageLoader.PixelSize(best)}px in {sw.ElapsedMilliseconds} ms");
        }

        report.Insert(0, $"WheelOfAwesome self-test: {pass} passed, {fail} failed{Environment.NewLine}{Environment.NewLine}");
        File.WriteAllText(reportFile, report.ToString());
        Application.Current.Shutdown(fail == 0 ? 0 : 1);
    }

    private sealed class FakeHost : ITriggerHost
    {
        public readonly List<string> Calls = new();
        public void StartHoldTimer(int ms) => Calls.Add("timer");
        public void StopHoldTimer() { }
        public void OpenWheel(int x, int y) => Calls.Add("open");
        public void MovePointer(int x, int y) => Calls.Add("move");
        public void CloseWheel(bool fire) => Calls.Add(fire ? "fire" : "cancel");
        public void ReplayTap(TriggerConfig trigger) => Calls.Add("tap");
        public void SendMaskKey() => Calls.Add("mask");
    }

    private static void EngineTests(Action<string, bool, string> check)
    {
        (TriggerEngine, FakeHost) Make(TriggerConfig t)
        {
            var host = new FakeHost();
            return (new TriggerEngine(host) { Trigger = () => t, HoldMs = () => 150 }, host);
        }
        var mouse = new TriggerConfig { MouseButton = MouseButton.Middle };
        var combo = new TriggerConfig { MouseButton = MouseButton.None, Key = 0x20, Modifiers = Modifiers.Ctrl };

        var (e, h) = Make(mouse);
        bool s1 = e.OnMouseButton(MouseButton.Middle, true, 0, 0), s2 = e.OnMouseButton(MouseButton.Middle, false, 0, 0);
        check("tap is swallowed then replayed", s1 && s2 && string.Join(",", h.Calls) == "timer,tap", string.Join(",", h.Calls));

        (e, h) = Make(mouse);
        e.OnMouseButton(MouseButton.Middle, true, 0, 0); e.OnHoldTimer(); e.OnMouseMove(50, 0);
        bool up = e.OnMouseButton(MouseButton.Middle, false, 50, 0);
        check("hold, move, release fires", up && string.Join(",", h.Calls) == "timer,open,move,fire", string.Join(",", h.Calls));

        (e, h) = Make(mouse);
        e.OnMouseButton(MouseButton.Middle, true, 0, 0); e.OnHoldTimer();
        bool esc = e.OnKey(KeyNames.VK_ESCAPE, true, Modifiers.None, 0, 0);
        bool escUp = e.OnKey(KeyNames.VK_ESCAPE, false, Modifiers.None, 0, 0);
        bool trigUp = e.OnMouseButton(MouseButton.Middle, false, 0, 0);
        check("Esc cancels and eats the releases", esc && escUp && trigUp && string.Join(",", h.Calls) == "timer,open,cancel", string.Join(",", h.Calls));

        (e, h) = Make(mouse);
        e.OnMouseButton(MouseButton.Middle, true, 0, 0); e.OnHoldTimer();
        bool left = e.OnMouseButton(MouseButton.Left, true, 0, 0), leftUp = e.OnMouseButton(MouseButton.Left, false, 0, 0), mUp = e.OnMouseButton(MouseButton.Middle, false, 0, 0);
        bool nextLeft = e.OnMouseButton(MouseButton.Left, true, 0, 0);
        check("left click fires; later clicks pass", left && leftUp && mUp && !nextLeft && string.Join(",", h.Calls) == "timer,open,fire", string.Join(",", h.Calls));

        (e, h) = Make(combo);
        bool plain = e.OnKey(0x20, true, Modifiers.None, 0, 0);
        bool withCtrl = e.OnKey(0x20, true, Modifiers.Ctrl, 0, 0);
        bool repeat = e.OnKey(0x20, true, Modifiers.Ctrl, 0, 0);
        e.OnHoldTimer();
        bool release = e.OnKey(0x20, false, Modifiers.Ctrl, 0, 0);
        check("keyboard combo trigger", !plain && withCtrl && repeat && release && string.Join(",", h.Calls) == "timer,open,fire", string.Join(",", h.Calls));

        (e, h) = Make(new TriggerConfig { MouseButton = MouseButton.None, Key = 0x51, Modifiers = Modifiers.Alt });
        e.OnKey(0x51, true, Modifiers.Alt, 0, 0);
        check("Alt combo sends mask key", h.Calls.Contains("mask"), string.Join(",", h.Calls));

        (e, h) = Make(mouse);
        e.Enabled = false;
        check("paused passes everything", !e.OnMouseButton(MouseButton.Middle, true, 0, 0) && h.Calls.Count == 0, "");

        (e, h) = Make(mouse);
        check("other buttons pass when idle", !e.OnMouseButton(MouseButton.Left, true, 0, 0) && !e.OnKey(0x41, true, Modifiers.None, 0, 0), "");
    }

    public static void AttachLiveTestLog(WheelController controller, string file)
    {
        controller.Fired += item => File.AppendAllText(file, $"FIRED {item.Label}{Environment.NewLine}");
        controller.Tapped += () => File.AppendAllText(file, $"TAPPED{Environment.NewLine}");
        File.AppendAllText(file, $"READY{Environment.NewLine}");
    }

    private static void Render(FrameworkElement element, string file, Color background)
    {
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));
        element.UpdateLayout();
        Save(element, file, element.DesiredSize, background, 2);
    }

    private static void RenderWindow(Window window, string file)
    {
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        var bg = window.Background is SolidColorBrush b && b.Color.A > 0 ? b.Color
            : IsDark() ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3);
        Save(content, file, new Size(content.ActualWidth, content.ActualHeight), bg, 1);
    }

    private static bool IsDark()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
    }

    private static void Save(Visual visual, string file, Size size, Color background, double scale)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(background), null, new Rect(size));
            dc.DrawRectangle(new VisualBrush(visual) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(size));
        }
        var rtb = new RenderTargetBitmap((int)(size.Width * scale), (int)(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(file);
        enc.Save(fs);
    }

    public static void Wait(int ms)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    public static void WaitUntil(Func<bool> condition, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < timeoutMs) Wait(100);
    }
}
