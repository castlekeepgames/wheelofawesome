using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace WheelOfAwesome.Services;

public static class Elevation
{
    public static bool IsElevated { get; } = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    /// <summary>Starts a new elevated copy (UAC prompt) that waits for this one to exit. False if the user said no.</summary>
    public static bool RelaunchElevated(params string[] extraArgs)
    {
        var psi = new ProcessStartInfo(Paths.ExePath) { UseShellExecute = true, Verb = "runas" };
        foreach (var a in extraArgs) psi.ArgumentList.Add(a);
        psi.ArgumentList.Add("--wait-for-pid");
        psi.ArgumentList.Add(Environment.ProcessId.ToString());
        try
        {
            Process.Start(psi);
            return true;
        }
        catch (Win32Exception)
        {
            return false; // UAC prompt declined
        }
    }

    /// <summary>Restarts without admin rights (via Explorer, which runs unelevated).</summary>
    public static void RelaunchNormal(params string[] extraArgs)
    {
        // Explorer can't pass arguments through reliably, so the --wait-for-pid handshake isn't available here;
        // the new instance retries the single-instance lock for a few seconds instead.
        var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        psi.ArgumentList.Add(Paths.ExePath);
        Process.Start(psi);
    }
}
