using System.IO;
using Microsoft.Win32;

namespace WheelOfAwesome.Actions;

public static class ChromeLocator
{
    private static string? _cached;

    public static string? Find()
    {
        if (_cached != null && File.Exists(_cached)) return _cached;
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe");
            if (key?.GetValue(null) is string path && File.Exists(path)) return _cached = path;
        }
        var guesses = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        }.Select(dir => Path.Combine(dir, @"Google\Chrome\Application\chrome.exe"));
        return _cached = guesses.FirstOrDefault(File.Exists);
    }
}
