using System.IO;
using System.Text;
using WheelOfAwesome.Native;
using static WheelOfAwesome.Native.Win32;

namespace WheelOfAwesome.Actions;

/// <summary>Finds an app's existing top-level windows and brings one to the front.</summary>
public static class WindowFocus
{
    /// <summary>
    /// Focuses a window belonging to <paramref name="exePath"/> or <paramref name="appId"/>.
    /// If the app is already in front, cycles to its next window.
    /// </summary>
    public static bool TryFocus(string? exePath, string? appId)
    {
        if (string.IsNullOrEmpty(exePath) && string.IsNullOrEmpty(appId)) return false;
        var matches = FindWindows(exePath, appId);
        if (matches.Count == 0) return false;

        var fg = GetForegroundWindow();
        // Z-order: [0] is topmost. If it's already in front, rotate to the bottom-most one.
        var target = matches[0] == fg && matches.Count > 1 ? matches[^1] : matches[0];
        ForceForeground(target);
        return true;
    }

    public static List<IntPtr> FindWindows(string? exePath, string? appId)
    {
        var result = new List<IntPtr>();
        var pidMatches = new Dictionary<uint, bool>();
        var exe = string.IsNullOrEmpty(exePath) ? null : Path.GetFullPath(exePath);

        EnumWindows((hwnd, _) =>
        {
            if (!IsCandidate(hwnd)) return true;
            if (!string.IsNullOrEmpty(appId) && string.Equals(Shell.GetWindowAppId(hwnd), appId, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(hwnd);
                return true;
            }
            if (exe != null)
            {
                GetWindowThreadProcessId(hwnd, out var pid);
                if (!pidMatches.TryGetValue(pid, out var match))
                {
                    var path = GetProcessPath(pid);
                    pidMatches[pid] = match = path != null && string.Equals(path, exe, StringComparison.OrdinalIgnoreCase);
                }
                if (match) result.Add(hwnd);
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    /// <summary>Real, user-facing app windows: visible, unowned, titled, not a tool window, not cloaked on another desktop.</summary>
    private static bool IsCandidate(IntPtr hwnd)
    {
        if (!IsWindowVisible(hwnd) || GetWindow(hwnd, GW_OWNER) != IntPtr.Zero || GetWindowTextLength(hwnd) == 0) return false;
        var ex = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        if ((ex & WS_EX_TOOLWINDOW) != 0 && (ex & WS_EX_APPWINDOW) == 0) return false;
        if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
        var cls = new StringBuilder(64);
        GetClassName(hwnd, cls, cls.Capacity);
        return cls.ToString() is not ("Progman" or "WorkerW" or "Shell_TrayWnd");
    }

    /// <summary>
    /// Windows only lets the foreground app hand out focus. A tray app isn't foreground, so briefly attach to the
    /// foreground thread's input queue, which makes SetForegroundWindow succeed.
    /// </summary>
    public static void ForceForeground(IntPtr hwnd)
    {
        if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
        var fg = GetForegroundWindow();
        if (fg == hwnd) return;
        uint fgThread = GetWindowThreadProcessId(fg, out _);
        uint me = GetCurrentThreadId();
        bool attached = fgThread != me && AttachThreadInput(me, fgThread, true);
        try
        {
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached) AttachThreadInput(me, fgThread, false);
        }
    }
}
