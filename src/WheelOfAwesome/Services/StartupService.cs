using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text;
using System.Xml.Linq;
using Microsoft.Win32;

namespace WheelOfAwesome.Services;

/// <summary>
/// "Start with Windows", done with a Task Scheduler logon task for the current user.
/// Not the registry Run list: when the app is started from inside a packaged (MSIX) app, its registry writes are
/// redirected into that app's private hive and Windows never sees the entry. Tasks are registered by the Task
/// Scheduler service, so they're always real. A normal task needs no admin rights; with "run as administrator" on,
/// the same task runs with highest privileges (creating that one needs a permission prompt once).
/// </summary>
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "WheelOfAwesome";

    /// <summary>Applies the settings. Returns an error message for the user, or null.</summary>
    public static string? Apply(bool startWithWindows, bool elevated, bool allowPrompt)
    {
        try
        {
            // Older versions used the Run list; clean it up so there's a single start-up path.
            RemoveRunEntry();

            if (!startWithWindows)
            {
                if (TaskExists()) DeleteTask(allowPrompt);
                return null;
            }

            if (TaskMatches(elevated) || CreateTask(elevated, allowPrompt)) return null;

            if (!elevated)
            {
                // Task Scheduler refused (e.g. blocked by policy): the Run list is better than nothing.
                SetRunEntry();
                Log.Info("Start-up task couldn't be created; using the Run registry entry instead");
                return null;
            }
            return "Couldn't register the admin start-up task (the permission prompt was declined).";
        }
        catch (Exception ex)
        {
            Log.Error("Applying start-up settings", ex);
            return "Couldn't update the start-with-Windows setting: " + ex.Message;
        }
    }

    private static void SetRunEntry()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.SetValue(Name, $"\"{Paths.ExePath}\" --startup");
    }

    private static void RemoveRunEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(Name) != null) key.DeleteValue(Name);
    }

    public static bool TaskExists() => RunSchtasks(new[] { "/Query", "/TN", Name }, elevate: false, out _) == 0;

    /// <summary>The task is already registered for this exe at the right privilege level, so nothing needs rewriting.</summary>
    private static bool TaskMatches(bool elevated)
    {
        if (RunSchtasks(new[] { "/Query", "/TN", Name, "/XML" }, elevate: false, out var xml) != 0) return false;
        try
        {
            var doc = XDocument.Parse(xml);
            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            var command = doc.Descendants(ns + "Command").FirstOrDefault()?.Value.Trim('"');
            var runLevel = doc.Descendants(ns + "RunLevel").FirstOrDefault()?.Value ?? "LeastPrivilege";
            var wanted = elevated ? "HighestAvailable" : "LeastPrivilege";
            return string.Equals(command, Paths.ExePath, StringComparison.OrdinalIgnoreCase) && runLevel == wanted;
        }
        catch
        {
            return false;
        }
    }

    private static bool CreateTask(bool elevated, bool allowPrompt)
    {
        var user = $"{Environment.UserDomainName}\\{Environment.UserName}";
        var description = elevated
            ? "Starts WheelOfAwesome with administrator rights when you sign in."
            : "Starts WheelOfAwesome when you sign in.";
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>{description}</Description></RegistrationInfo>
              <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{SecurityElement.Escape(user)}</UserId><Delay>PT3S</Delay></LogonTrigger></Triggers>
              <Principals><Principal id="Author"><UserId>{SecurityElement.Escape(user)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>{(elevated ? "HighestAvailable" : "LeastPrivilege")}</RunLevel></Principal></Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>4</Priority>
              </Settings>
              <Actions Context="Author"><Exec><Command>{SecurityElement.Escape(Paths.ExePath)}</Command><Arguments>--startup</Arguments></Exec></Actions>
            </Task>
            """;
        var file = Path.Combine(Path.GetTempPath(), $"WheelOfAwesome-task-{Environment.ProcessId}.xml");
        File.WriteAllText(file, xml, Encoding.Unicode);
        try
        {
            var args = new[] { "/Create", "/F", "/TN", Name, "/XML", file };
            // A normal task can be created as the user. An admin task, or replacing one an admin created, needs elevation.
            bool needsAdmin = elevated && !Elevation.IsElevated;
            if (!needsAdmin && RunSchtasks(args, elevate: false, out _) == 0) return true;
            if (!allowPrompt || Elevation.IsElevated) return false;
            return RunSchtasks(args, elevate: true, out _) == 0;
        }
        finally
        {
            try { File.Delete(file); } catch { }
        }
    }

    private static void DeleteTask(bool allowPrompt)
    {
        var args = new[] { "/Delete", "/F", "/TN", Name };
        if (RunSchtasks(args, elevate: false, out _) == 0) return;
        // Only an admin-level task needs the prompt to remove.
        if (allowPrompt && !Elevation.IsElevated) RunSchtasks(args, elevate: true, out _);
    }

    private static int RunSchtasks(string[] args, bool elevate, out string output)
    {
        output = "";
        var psi = new ProcessStartInfo("schtasks.exe")
        {
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            UseShellExecute = elevate,
        };
        if (elevate) psi.Verb = "runas";
        else { psi.RedirectStandardOutput = true; psi.RedirectStandardError = true; }
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi)!;
            if (!elevate)
            {
                var stderr = p.StandardError.ReadToEndAsync();
                output = p.StandardOutput.ReadToEnd();
                stderr.Wait();
            }
            p.WaitForExit(15000);
            return p.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return -1; // UAC declined
        }
    }
}
