using System.Runtime.InteropServices;
using WheelOfAwesome.Models;
using WheelOfAwesome.Native;
using static WheelOfAwesome.Native.Win32;
using MouseButton = WheelOfAwesome.Models.MouseButton;

namespace WheelOfAwesome.Input;

/// <summary>Injects keyboard/mouse input. Everything is tagged so our own hook lets it through untouched.</summary>
public static class InputSender
{
    /// <summary>"WHOA" — stamped into dwExtraInfo of every event we inject.</summary>
    public const nuint Signature = 0x57484F41;

    /// <summary>An unassigned virtual key, sent to stop a lone Alt/Win release from opening a menu or Start.</summary>
    public const int MaskKey = 0xE8;

    private static readonly HashSet<int> ExtendedKeys = new()
    {
        0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2C, 0x2D, 0x2E, // nav cluster, arrows, PrintScreen
        0x5B, 0x5C, 0x5D, 0x6F, 0x90, KeyNames.VK_RCONTROL, KeyNames.VK_RMENU,
        0xA6, 0xA7, 0xA8, 0xA9, 0xAA, 0xAB, 0xAC, 0xAD, 0xAE, 0xAF, 0xB0, 0xB1, 0xB2, 0xB3,
    };

    public static void KeyCombo(Modifiers mods, int vk)
    {
        var inputs = new List<INPUT>();
        var modKeys = ModifierKeys(mods);
        foreach (var m in modKeys) inputs.Add(Key(m, down: true));
        if (vk != 0)
        {
            inputs.Add(Key(vk, down: true));
            inputs.Add(Key(vk, down: false));
        }
        // A lone Win/Alt tap is a valid combo (e.g. open Start); otherwise mask it so it doesn't.
        if (vk != 0 && (mods.HasFlag(Modifiers.Alt) || mods.HasFlag(Modifiers.Win)))
        {
            inputs.Add(Key(MaskKey, down: true));
            inputs.Add(Key(MaskKey, down: false));
        }
        for (int i = modKeys.Count - 1; i >= 0; i--) inputs.Add(Key(modKeys[i], down: false));
        Send(inputs);
    }

    public static void KeyTap(int vk) => Send(new List<INPUT> { Key(vk, true), Key(vk, false) });

    public static void Mask() => KeyTap(MaskKey);

    public static void Text(string text)
    {
        var inputs = new List<INPUT>();
        foreach (var ch in text.Replace("\r\n", "\n"))
        {
            if (ch == '\n') { inputs.Add(Key(KeyNames.VK_RETURN, true)); inputs.Add(Key(KeyNames.VK_RETURN, false)); continue; }
            if (ch == '\t') { inputs.Add(Key(KeyNames.VK_TAB, true)); inputs.Add(Key(KeyNames.VK_TAB, false)); continue; }
            inputs.Add(Unicode(ch, down: true));
            inputs.Add(Unicode(ch, down: false));
        }
        // Send in modest batches so long text doesn't overwhelm slow apps.
        for (int i = 0; i < inputs.Count; i += 40)
        {
            Send(inputs.GetRange(i, Math.Min(40, inputs.Count - i)));
            if (i + 40 < inputs.Count) Thread.Sleep(5);
        }
    }

    public static void MouseClick(MouseButton button)
    {
        var (down, up, data) = button switch
        {
            MouseButton.Left => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP, 0u),
            MouseButton.Right => (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP, 0u),
            MouseButton.Middle => (MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP, 0u),
            MouseButton.X1 => (MOUSEEVENTF_XDOWN, MOUSEEVENTF_XUP, 1u),
            MouseButton.X2 => (MOUSEEVENTF_XDOWN, MOUSEEVENTF_XUP, 2u),
            _ => (0u, 0u, 0u),
        };
        if (down == 0) return;
        Send(new List<INPUT> { Mouse(down, data), Mouse(up, data) });
    }

    private static List<int> ModifierKeys(Modifiers mods)
    {
        var list = new List<int>();
        if (mods.HasFlag(Modifiers.Ctrl)) list.Add(KeyNames.VK_LCONTROL);
        if (mods.HasFlag(Modifiers.Shift)) list.Add(KeyNames.VK_LSHIFT);
        if (mods.HasFlag(Modifiers.Alt)) list.Add(KeyNames.VK_LMENU);
        if (mods.HasFlag(Modifiers.Win)) list.Add(KeyNames.VK_LWIN);
        return list;
    }

    private static INPUT Key(int vk, bool down)
    {
        uint flags = down ? 0 : KEYEVENTF_KEYUP;
        if (ExtendedKeys.Contains(vk)) flags |= KEYEVENTF_EXTENDEDKEY;
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)vk, wScan = (ushort)MapVirtualKey((uint)vk, 0), dwFlags = flags, dwExtraInfo = Signature } },
        };
    }

    private static INPUT Unicode(char ch, bool down) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion { ki = new KEYBDINPUT { wVk = 0, wScan = ch, dwFlags = KEYEVENTF_UNICODE | (down ? 0 : KEYEVENTF_KEYUP), dwExtraInfo = Signature } },
    };

    private static INPUT Mouse(uint flags, uint data) => new()
    {
        type = INPUT_MOUSE,
        U = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags, mouseData = data, dwExtraInfo = Signature } },
    };

    private static void Send(List<INPUT> inputs)
    {
        if (inputs.Count == 0) return;
        var arr = inputs.ToArray();
        SendInput((uint)arr.Length, arr, Marshal.SizeOf<INPUT>());
    }
}
