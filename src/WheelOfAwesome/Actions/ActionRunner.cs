using System.Diagnostics;
using System.IO;
using WheelOfAwesome.Icons;
using WheelOfAwesome.Input;
using WheelOfAwesome.Models;
using WheelOfAwesome.Native;
using WheelOfAwesome.Services;

namespace WheelOfAwesome.Actions;

public static class ActionRunner
{
    /// <summary>Raised with a user-facing message when an action can't run.</summary>
    public static event Action<string>? Failed;

    public static void Run(WheelItem item)
    {
        Log.Info($"Fired '{item.Label}' ({item.Kind})");
        try
        {
            switch (item.Kind)
            {
                case ItemKind.Link: OpenLink(item.Url); break;
                case ItemKind.App: OpenApp(item); break;
                case ItemKind.Macro: RunMacro(item.Steps.ToList()); break;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Running '{item.Label}'", ex);
            Failed?.Invoke($"Couldn't run \"{item.Label}\": {ex.Message}");
        }
    }

    private static void OpenLink(string rawUrl)
    {
        var url = IconService.NormalizeUrl(rawUrl);
        if (url.Length == 0) throw new InvalidOperationException("this link has no URL yet");
        Win32.AllowSetForegroundWindow(-1);

        var chrome = ChromeLocator.Find();
        bool web = url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        if (chrome == null || !web)
        {
            // No Chrome (or a mailto:/other scheme): let Windows pick the handler.
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return;
        }

        // Passing a URL to chrome.exe opens it as a new tab in the most recent window.
        var psi = new ProcessStartInfo(chrome) { UseShellExecute = false };
        psi.ArgumentList.Add(url);
        Process.Start(psi);
        BringToFrontSoon(chrome);
    }

    private static void OpenApp(WheelItem item)
    {
        var target = Environment.ExpandEnvironmentVariables(item.Target ?? "");
        if (string.IsNullOrEmpty(item.AppId) && string.IsNullOrWhiteSpace(target))
            throw new InvalidOperationException("no app has been chosen");

        if (item.FocusIfRunning)
        {
            var exe = !string.IsNullOrEmpty(item.AppId) ? item.ResolvedExe : ExeFor(target);
            if (WindowFocus.TryFocus(exe, item.AppId.Length > 0 ? item.AppId : null)) return;
        }

        Win32.AllowSetForegroundWindow(-1);
        if (!string.IsNullOrEmpty(item.AppId))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", @"shell:AppsFolder\" + item.AppId) { UseShellExecute = true });
            return;
        }

        if (!File.Exists(target) && !Directory.Exists(target))
            throw new FileNotFoundException($"{target} doesn't exist anymore");

        var psi = new ProcessStartInfo(target) { UseShellExecute = true };
        if (File.Exists(target)) psi.WorkingDirectory = Path.GetDirectoryName(target) ?? "";
        Process.Start(psi);
    }

    /// <summary>The exe behind a path, if any (shortcuts are resolved); documents and folders return null.</summary>
    private static string? ExeFor(string target)
    {
        if (target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return target;
        if (target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var resolved = Shell.ResolveShortcutTarget(target);
            if (resolved?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true) return resolved;
        }
        return null;
    }

    /// <summary>Chrome sometimes opens the tab behind the current window; nudge it forward once it's there.</summary>
    private static void BringToFrontSoon(string exe)
    {
        Task.Run(async () =>
        {
            for (int i = 0; i < 6; i++)
            {
                await Task.Delay(150);
                var windows = WindowFocus.FindWindows(exe, null);
                if (windows.Count == 0) continue;
                var fg = Win32.GetForegroundWindow();
                if (!windows.Contains(fg)) WindowFocus.ForceForeground(windows[0]);
                return;
            }
        });
    }

    private static void RunMacro(List<MacroStep> steps)
    {
        Task.Run(() =>
        {
            try
            {
                WaitForModifiersReleased();
                foreach (var step in steps)
                {
                    switch (step.Kind)
                    {
                        case StepKind.Keys:
                            if (step.Key != 0 || step.Modifiers != Modifiers.None)
                                InputSender.KeyCombo(step.Modifiers, step.Key);
                            break;
                        case StepKind.Wait:
                            Thread.Sleep(step.Ms);
                            break;
                        case StepKind.Text:
                            InputSender.Text(step.Text);
                            break;
                    }
                    Thread.Sleep(15);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Macro", ex);
            }
        });
    }

    /// <summary>
    /// If the trigger was a keyboard combo, the user may still be holding Ctrl/Alt/etc. — those would merge into the
    /// macro's keys. Wait (briefly) until they're up.
    /// </summary>
    private static void WaitForModifiersReleased()
    {
        var sw = Stopwatch.StartNew();
        int[] keys = { KeyNames.VK_CONTROL, KeyNames.VK_SHIFT, KeyNames.VK_MENU, KeyNames.VK_LWIN, KeyNames.VK_RWIN };
        while (sw.ElapsedMilliseconds < 1500 && keys.Any(Win32.IsKeyDown)) Thread.Sleep(10);
        Thread.Sleep(40); // let the wheel's hide and the trigger release settle
    }
}
