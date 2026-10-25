using System.Diagnostics;
using System.IO;
using WheelOfAwesome.Models;
using static WheelOfAwesome.Native.Win32;

namespace WheelOfAwesome.Services;

/// <summary>
/// Decides when the wheel should stay out of the way: a full-screen app (game, video, slideshow) is in front, or the
/// app in front is on the user's "never open in" list. Called from the hook thread on every trigger press, so it only
/// does a handful of cheap Win32 calls.
/// </summary>
public static class AppFilter
{
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland",
    };

    private static readonly HashSet<string> ShellProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "StartMenuExperienceHost.exe", "SearchHost.exe", "SearchApp.exe", "ShellExperienceHost.exe", "ShellHost.exe", "LockApp.exe",
    };

    private static volatile HashSet<string> _excluded = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Takes a snapshot of the list, so the hook thread never reads a collection the UI is editing.</summary>
    public static void SetExcluded(IEnumerable<ExcludedApp> apps) =>
        _excluded = new HashSet<string>(apps.Select(a => a.ExeName).Where(n => n.Length > 0), StringComparer.OrdinalIgnoreCase);

    public static bool IsExcluded(string? exePath) =>
        !string.IsNullOrEmpty(exePath) && _excluded.Contains(Path.GetFileName(exePath));

    /// <summary>True when the trigger should be left alone for the app in front.</summary>
    public static bool ShouldStayOut(bool pauseInFullscreen)
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == Environment.ProcessId) return false;

        if (_excluded.Count > 0 && IsExcluded(AppPathFor(hwnd))) return true;
        return pauseInFullscreen && IsFullscreen(hwnd);
    }

    public static bool IsFullscreen(IntPtr hwnd)
    {
        if (ShellClasses.Contains(ClassName(hwnd))) return false;
        if (!GetWindowRect(hwnd, out var rect)) return false;
        var monitor = MonitorFromPoint(new POINT { X = (rect.Left + rect.Right) / 2, Y = (rect.Top + rect.Bottom) / 2 }, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info)) return false;
        var style = (long)GetWindowLongPtr(hwnd, GWL_STYLE);
        if (CoversMonitor(rect, info.rcMonitor, style, IsZoomed(hwnd))) return true;

        // Exclusive full-screen (older DirectX games) can report odd window sizes; the shell knows about those.
        return SHQueryUserNotificationState(out var state) == 0 && state is 3 or 4;
    }

    /// <summary>
    /// The window covers its whole monitor. A normal maximised window with a title bar doesn't count, even though it
    /// covers the screen when the taskbar is set to auto-hide.
    /// </summary>
    internal static bool CoversMonitor(RECT window, RECT monitor, long style, bool maximized)
    {
        bool covers = window.Left <= monitor.Left && window.Top <= monitor.Top && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;
        if (!covers) return false;
        bool hasTitleBar = (style & WS_CAPTION) == WS_CAPTION;
        return !(maximized && hasTitleBar);
    }

    /// <summary>
    /// The exe behind a window. Store apps all live inside ApplicationFrameHost.exe, so for those the hosted app's
    /// own process is found from its child window.
    /// </summary>
    public static string? AppPathFor(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        var path = PathFor(pid);
        if (path == null || !Path.GetFileName(path).Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase)) return path;

        string? hosted = null;
        EnumChildWindows(hwnd, (child, _) =>
        {
            GetWindowThreadProcessId(child, out var childPid);
            if (childPid == pid) return true;
            hosted = PathFor(childPid);
            return hosted == null;
        }, IntPtr.Zero);
        return hosted ?? path;
    }

    // Not cached: process IDs get reused, and the lookup only costs a few microseconds.
    private static string? PathFor(uint pid) => GetProcessPath(pid);

    /// <summary>Windows the user would think of as "an app", for the tray shortcut and the running-apps menu.</summary>
    public static bool IsUserApp(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || ShellClasses.Contains(ClassName(hwnd))) return false;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == Environment.ProcessId) return false;
        var path = AppPathFor(hwnd);
        return path != null && !ShellProcesses.Contains(Path.GetFileName(path));
    }

    public static ExcludedApp Describe(string exePath)
    {
        string name = Path.GetFileNameWithoutExtension(exePath);
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exePath);
            var described = (info.FileDescription ?? "").Trim();
            if (described.Length == 0) described = (info.ProductName ?? "").Trim();
            if (described.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) described = described[..^4];
            if (described.Length > 0) name = described;
        }
        catch
        {
            // Keep the file name.
        }
        return new ExcludedApp { Name = name, ExeName = Path.GetFileName(exePath), Path = exePath };
    }

    /// <summary>Distinct apps with a visible window right now, for "Add a running app".</summary>
    public static List<ExcludedApp> RunningApps()
    {
        var paths = new List<string>();
        EnumWindows((hwnd, _) =>
        {
            if (Actions.WindowFocus.IsCandidate(hwnd) && IsUserApp(hwnd) && AppPathFor(hwnd) is { } p &&
                !paths.Contains(p, StringComparer.OrdinalIgnoreCase))
                paths.Add(p);
            return true;
        }, IntPtr.Zero);
        return paths.Select(Describe).OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}

/// <summary>Remembers the last real app window in front, so the tray menu can act on it (the taskbar takes focus when you click the tray).</summary>
public sealed class ForegroundTracker : IDisposable
{
    private readonly WinEventProc _proc;
    private readonly IntPtr _hook;

    public ForegroundTracker()
    {
        _proc = (_, _, hwnd, _, _, _, _) => Consider(hwnd);
        _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _proc, 0, 0, WINEVENT_OUTOFCONTEXT);
        Consider(GetForegroundWindow());
    }

    public string? LastAppPath { get; private set; }

    private void Consider(IntPtr hwnd)
    {
        try
        {
            if (AppFilter.IsUserApp(hwnd)) LastAppPath = AppFilter.AppPathFor(hwnd);
        }
        catch
        {
            // Never let a focus change throw.
        }
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) UnhookWinEvent(_hook);
    }
}
