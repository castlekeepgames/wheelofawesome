using System.IO;

namespace WheelOfAwesome.Services;

public static class Paths
{
    private static string? _root;

    /// <summary>Overridden by --config-dir (used for development so a test run never touches real settings).</summary>
    public static string Root
    {
        get
        {
            if (_root == null)
            {
                _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WheelOfAwesome");
                Directory.CreateDirectory(_root);
            }
            return _root;
        }
        set
        {
            _root = Path.GetFullPath(value);
            Directory.CreateDirectory(_root);
        }
    }

    public static string ConfigFile => Path.Combine(Root, "config.json");
    public static string LogFile => Path.Combine(Root, "log.txt");

    public static string IconDir
    {
        get
        {
            var dir = Path.Combine(Root, "icons");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "WheelOfAwesome.exe");
}
