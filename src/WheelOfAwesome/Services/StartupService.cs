using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text;
using Microsoft.Win32;

namespace WheelOfAwesome.Services;

/// <summary>
/// "Start with Windows". Normally a per-user Run registry entry. With "run as administrator" on, Windows won't
/// auto-start elevated apps from Run, so a logon scheduled task with highest privileges is used instead.
/// </summary>
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "WheelOfAwesome";

    public static string Command => $"\"{Paths.ExePath}\" --startup";

    /// <summary>Applies the settings. Returns an error message for the user, or null.</summary>
    public static string? Apply(bool startWithWindows, bool elevated, bool allowPrompt)
    {
        try
        {
            if (startWithWindows && !elevated)
            {
                SetRunEntry(true);
                if (TaskExists()) DeleteTask(allowPrompt);
            }
            else if (startWithWindows && elevated)
            {
                SetRunEntry(false);
                if (!CreateTask(allowPrompt)) return "Couldn't register the admin start-up task (the permission prompt was declined).";
            }
            else
            {
                SetRunEntry(false);
                if (TaskExists()) DeleteTask(allowPrompt);
            }
            return null;
        }
        catch (Exception ex)
        {
            Log.Error("Applying start-up settings", ex);
            return "Couldn't update the start-with-Windows setting: " + ex.Message;
        }
    }

    public static bool RunEntryMatches()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return string.Equals(key?.GetValue(Name) as string, Command, StringComparison.OrdinalIgnoreCase);
    }

    private static void SetRunEntry(bool on)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) key.SetValue(Name, Command);
        else if (key.GetValue(Name) != null) key.DeleteValue(Name);
    }

    public static bool TaskExists() => RunSchtasks(new[] { "/Query", "/TN", Name }, elevate: false) == 0;

    private static bool CreateTask(bool allowPrompt)
    {
        var user = $"{Environment.UserDomainName}\\{Environment.UserName}";
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Starts WheelOfAwesome with administrator rights when you sign in.</Description></RegistrationInfo>
              <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{SecurityElement.Escape(user)}</UserId><Delay>PT3S</Delay></LogonTrigger></Triggers>
              <Principals><Principal id="Author"><UserId>{SecurityElement.Escape(user)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
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
        var file = Path.Combine(Path.GetTempPath(), "WheelOfAwesome-task.xml");
        File.WriteAllText(file, xml, Encoding.Unicode);
        try
        {
            if (!Elevation.IsElevated && !allowPrompt) return false;
            return RunSchtasks(new[] { "/Create", "/F", "/TN", Name, "/XML", file }, elevate: !Elevation.IsElevated) == 0;
        }
        finally
        {
            try { File.Delete(file); } catch { }
        }
    }

    private static void DeleteTask(bool allowPrompt)
    {
        if (!Elevation.IsElevated && !allowPrompt) return;
        RunSchtasks(new[] { "/Delete", "/F", "/TN", Name }, elevate: !Elevation.IsElevated);
    }

    private static int RunSchtasks(string[] args, bool elevate)
    {
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
            if (!elevate) { p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); }
            p.WaitForExit(15000);
            return p.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return -1; // UAC declined
        }
    }
}
