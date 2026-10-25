using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using WheelOfAwesome.Native;

namespace WheelOfAwesome.Services;

/// <summary>
/// The app installs itself: run from anywhere (e.g. Downloads), it copies itself to a per-user Programs folder and
/// restarts from there, replacing (and closing) any older installed copy. It also registers in Settings → Apps so it
/// can be uninstalled like any other program.
/// </summary>
public static class Installer
{
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\WheelOfAwesome";
    private static string? _installDir;

    /// <summary>Overridable with --install-dir for testing.</summary>
    public static string InstallDir
    {
        get => _installDir ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "WheelOfAwesome");
        set => _installDir = Path.GetFullPath(value);
    }

    public static string InstalledExe => Path.Combine(InstallDir, "WheelOfAwesome.exe");

    public static bool IsInstalledCopy => string.Equals(Path.GetFullPath(Paths.ExePath), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Installs this exe if it isn't the installed copy, then starts the installed copy with the same arguments.
    /// Returns true when this process should exit because the installed copy has taken over.
    /// </summary>
    public static bool RelocateIfNeeded(string[] args, string instanceName)
    {
        if (IsInstalledCopy) return false;
        try
        {
            CloseInstalledCopy(instanceName);
            Directory.CreateDirectory(InstallDir);

            Exception? last = null;
            for (int i = 0; i < 20; i++)
            {
                try { File.Copy(Paths.ExePath, InstalledExe, overwrite: true); last = null; break; }
                catch (IOException ex) { last = ex; Thread.Sleep(250); }
                catch (UnauthorizedAccessException ex) { last = ex; Thread.Sleep(250); }
            }
            if (last != null) throw last;

            // Drop the "downloaded from the internet" mark so Windows doesn't show a security prompt at every sign-in.
            try { File.Delete(InstalledExe + ":Zone.Identifier"); } catch { }

            var psi = new ProcessStartInfo(InstalledExe) { UseShellExecute = false, WorkingDirectory = InstallDir };
            foreach (var a in args) psi.ArgumentList.Add(a);
            Process.Start(psi);
            Log.Info($"Installed {Paths.ExePath} to {InstalledExe}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Self-install failed; running from the current location", ex);
            MessageBox.Show(
                $"WheelOfAwesome couldn't install itself to\n{InstallDir}\n\n{ex.Message}\n\nIt will run from where it is for now. " +
                "If an older copy is running as administrator, exit it from its tray icon and try again.",
                "WheelOfAwesome", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    /// <summary>Asks the installed copy to save and quit, and waits for it (forcing it if it's an older build that can't be asked).</summary>
    public static void CloseInstalledCopy(string instanceName)
    {
        if (EventWaitHandle.TryOpenExisting(instanceName + "_exit", out var exit))
        {
            exit.Set();
            exit.Dispose();
        }
        var deadline = DateTime.UtcNow.AddSeconds(6);
        while (DateTime.UtcNow < deadline && InstalledProcesses().Count > 0) Thread.Sleep(100);
        foreach (var p in InstalledProcesses())
        {
            try { p.Kill(); p.WaitForExit(3000); } catch { }
        }
    }

    private static List<Process> InstalledProcesses()
    {
        var self = Environment.ProcessId;
        return Process.GetProcessesByName("WheelOfAwesome")
            .Where(p => p.Id != self && string.Equals(Win32.GetProcessPath((uint)p.Id), InstalledExe, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Lists the app in Settings → Apps → Installed apps, with a working Uninstall button.</summary>
    public static void RegisterUninstallEntry()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
            var exe = InstalledExe;
            key.SetValue("DisplayName", "WheelOfAwesome");
            key.SetValue("DisplayVersion", typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");
            key.SetValue("DisplayIcon", exe);
            key.SetValue("InstallLocation", InstallDir);
            key.SetValue("UninstallString", $"\"{exe}\" --uninstall");
            key.SetValue("QuietUninstallString", $"\"{exe}\" --uninstall --quiet");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", (int)(new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
            if (key.GetValue("InstallDate") == null) key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
        }
        catch (Exception ex)
        {
            Log.Error("Registering uninstall entry", ex);
        }
    }

    /// <summary>
    /// Removes the app: closes the running copy, start-up entries, the Apps entry and the exe (deleted just after this
    /// process exits). Optionally deletes the user's shortcuts and settings. Interactive unless <paramref name="quiet"/>.
    /// </summary>
    public static void Uninstall(string instanceName, bool quiet, bool dev)
    {
        bool deleteSettings = false;
        if (!quiet)
        {
            if (MessageBox.Show("Uninstall WheelOfAwesome from this PC?", "Uninstall WheelOfAwesome", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            deleteSettings = MessageBox.Show(
                "Also delete your shortcuts and settings?\n\nChoose No to keep them in case you reinstall later.",
                "Uninstall WheelOfAwesome", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        }

        CloseInstalledCopy(instanceName);
        if (!dev)
        {
            StartupService.Apply(startWithWindows: false, elevated: false, allowPrompt: true);
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false); } catch { }
        }

        Log.Info($"Uninstalled (settings deleted: {deleteSettings})");
        if (!quiet)
            MessageBox.Show(deleteSettings ? "WheelOfAwesome has been removed." : $"WheelOfAwesome has been removed. Your shortcuts are still saved in {Paths.Root}.",
                "Uninstall WheelOfAwesome", MessageBoxButton.OK, MessageBoxImage.Information);

        // A running exe can't delete itself, so a hidden PowerShell waits for this process to exit and then cleans up.
        // Only our own files are removed: the exe, then the folder if it's left empty.
        static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        var script = $"Wait-Process -Id {Environment.ProcessId} -Timeout 30 -ErrorAction SilentlyContinue; " +
                     $"Remove-Item -LiteralPath {Q(InstalledExe)} -Force -ErrorAction SilentlyContinue; " +
                     $"if (-not (Get-ChildItem -LiteralPath {Q(InstallDir)} -Force -ErrorAction SilentlyContinue)) {{ Remove-Item -LiteralPath {Q(InstallDir)} -Force -ErrorAction SilentlyContinue }}";
        if (deleteSettings && File.Exists(Paths.ConfigFile))
            script += $"; Remove-Item -LiteralPath {Q(Paths.Root)} -Recurse -Force -ErrorAction SilentlyContinue";
        var psi = new ProcessStartInfo("powershell.exe") { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-Command", script }) psi.ArgumentList.Add(a);
        Process.Start(psi);
    }
}
