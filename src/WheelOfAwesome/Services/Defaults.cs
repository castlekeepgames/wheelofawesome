using WheelOfAwesome.Models;

namespace WheelOfAwesome.Services;

/// <summary>The starter wheel shown on first run, so there's something to try immediately.</summary>
public static class Defaults
{
    private const int VK_S = 0x53, VK_ESCAPE = 0x1B;

    public static AppConfig Create()
    {
        var config = new AppConfig();
        config.Items.Add(Link("Google", "https://www.google.com"));
        config.Items.Add(Link("YouTube", "https://www.youtube.com"));
        config.Items.Add(Link("Gmail", "https://mail.google.com"));
        config.Items.Add(new WheelItem { Label = "File Explorer", Kind = ItemKind.App, Target = @"%WINDIR%\explorer.exe", FocusIfRunning = false });
        config.Items.Add(new WheelItem { Label = "Calculator", Kind = ItemKind.App, AppId = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App" });
        config.Items.Add(new WheelItem { Label = "Notepad", Kind = ItemKind.App, Target = @"%WINDIR%\System32\notepad.exe" });
        config.Items.Add(Macro("Screenshot", "scissors", new MacroStep { Kind = StepKind.Keys, Modifiers = Modifiers.Win | Modifiers.Shift, Key = VK_S }));
        config.Items.Add(Macro("Task Manager", "activity", new MacroStep { Kind = StepKind.Keys, Modifiers = Modifiers.Ctrl | Modifiers.Shift, Key = VK_ESCAPE }));
        return config;
    }

    private static WheelItem Link(string label, string url) => new() { Label = label, Kind = ItemKind.Link, Url = url };

    private static WheelItem Macro(string label, string glyph, params MacroStep[] steps)
    {
        var item = new WheelItem { Label = label, Kind = ItemKind.Macro };
        item.Icon.Mode = IconMode.Library;
        item.Icon.Glyph = glyph;
        foreach (var s in steps) item.Steps.Add(s);
        return item;
    }
}
