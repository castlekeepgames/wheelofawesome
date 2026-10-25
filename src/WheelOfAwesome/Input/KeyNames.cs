using System.Text;
using System.Windows.Input;
using WheelOfAwesome.Models;
using MouseButton = WheelOfAwesome.Models.MouseButton;

namespace WheelOfAwesome.Input;

public static class KeyNames
{
    public const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;
    public const int VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1, VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3, VK_LMENU = 0xA4, VK_RMENU = 0xA5;
    public const int VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    public const int VK_ESCAPE = 0x1B, VK_RETURN = 0x0D, VK_TAB = 0x09;

    private static readonly Dictionary<int, string> Special = new()
    {
        [0x08] = "Backspace", [0x09] = "Tab", [0x0D] = "Enter", [0x13] = "Pause", [0x14] = "CapsLock", [0x1B] = "Esc",
        [0x20] = "Space", [0x21] = "PageUp", [0x22] = "PageDown", [0x23] = "End", [0x24] = "Home",
        [0x25] = "Left", [0x26] = "Up", [0x27] = "Right", [0x28] = "Down", [0x2C] = "PrintScreen",
        [0x2D] = "Insert", [0x2E] = "Delete", [0x5D] = "Menu", [0x90] = "NumLock", [0x91] = "ScrollLock",
        [0x6A] = "Num *", [0x6B] = "Num +", [0x6D] = "Num -", [0x6E] = "Num .", [0x6F] = "Num /",
        [0xBA] = ";", [0xBB] = "=", [0xBC] = ",", [0xBD] = "-", [0xBE] = ".", [0xBF] = "/", [0xC0] = "`",
        [0xDB] = "[", [0xDC] = "\\", [0xDD] = "]", [0xDE] = "'",
        [0xAD] = "Mute", [0xAE] = "Volume Down", [0xAF] = "Volume Up",
        [0xB0] = "Next Track", [0xB1] = "Previous Track", [0xB2] = "Stop Media", [0xB3] = "Play/Pause",
        [0xA6] = "Browser Back", [0xA7] = "Browser Forward", [0xA8] = "Browser Refresh",
        [VK_LSHIFT] = "Left Shift", [VK_RSHIFT] = "Right Shift", [VK_LCONTROL] = "Left Ctrl", [VK_RCONTROL] = "Right Ctrl",
        [VK_LMENU] = "Left Alt", [VK_RMENU] = "Right Alt", [VK_LWIN] = "Win", [VK_RWIN] = "Right Win",
        [VK_SHIFT] = "Shift", [VK_CONTROL] = "Ctrl", [VK_MENU] = "Alt",
    };

    public static bool IsModifier(int vk) => ModifierOf(vk) != Modifiers.None;

    public static Modifiers ModifierOf(int vk) => vk switch
    {
        VK_SHIFT or VK_LSHIFT or VK_RSHIFT => Modifiers.Shift,
        VK_CONTROL or VK_LCONTROL or VK_RCONTROL => Modifiers.Ctrl,
        VK_MENU or VK_LMENU or VK_RMENU => Modifiers.Alt,
        VK_LWIN or VK_RWIN => Modifiers.Win,
        _ => Modifiers.None,
    };

    public static string Key(int vk)
    {
        if (Special.TryGetValue(vk, out var name)) return name;
        if (vk is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A) return ((char)vk).ToString();
        if (vk is >= 0x60 and <= 0x69) return $"Num {vk - 0x60}";
        if (vk is >= 0x70 and <= 0x87) return $"F{vk - 0x6F}";
        var key = KeyInterop.KeyFromVirtualKey(vk);
        return key == System.Windows.Input.Key.None ? $"Key 0x{vk:X2}" : key.ToString();
    }

    public static string Combo(Modifiers mods, int vk)
    {
        if (vk == 0 && mods == Modifiers.None) return "Not set";
        var sb = new StringBuilder();
        if (mods.HasFlag(Modifiers.Win)) sb.Append("Win + ");
        if (mods.HasFlag(Modifiers.Ctrl)) sb.Append("Ctrl + ");
        if (mods.HasFlag(Modifiers.Alt)) sb.Append("Alt + ");
        if (mods.HasFlag(Modifiers.Shift)) sb.Append("Shift + ");
        if (vk != 0) sb.Append(Key(vk));
        else sb.Length -= 3;
        return sb.ToString();
    }

    public static string Mouse(MouseButton b) => b switch
    {
        MouseButton.Middle => "Middle mouse button",
        MouseButton.Right => "Right mouse button",
        MouseButton.X1 => "Mouse Back button (side)",
        MouseButton.X2 => "Mouse Forward button (side)",
        MouseButton.Left => "Left mouse button",
        _ => "Not set",
    };
}
