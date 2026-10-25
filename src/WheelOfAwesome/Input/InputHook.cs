using System.Runtime.InteropServices;
using WheelOfAwesome.Models;
using WheelOfAwesome.Services;
using static WheelOfAwesome.Native.Win32;
using MouseButton = WheelOfAwesome.Models.MouseButton;

namespace WheelOfAwesome.Input;

public sealed record CaptureResult(MouseButton Mouse, int Key, Modifiers Modifiers);

/// <summary>
/// Global low-level keyboard + mouse hooks. They live on their own thread with its own message loop, so a busy UI
/// thread can never delay system input or get the hook silently removed by Windows.
/// </summary>
public sealed class InputHook : IDisposable
{
    private readonly TriggerEngine _engine;
    private readonly HookProc _kbProc, _msProc;
    private readonly HashSet<int> _captureSwallowUps = new();
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _kbHook, _msHook;

    private Action<CaptureResult>? _capture;
    private bool _captureMouse;
    private Modifiers _captureMods;
    private int _captureLastMod;
    private bool _captureOtherKey;

    public InputHook(TriggerEngine engine)
    {
        _engine = engine;
        _kbProc = KeyboardProc;
        _msProc = MouseProc;
    }

    public void Install()
    {
        var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            var module = GetModuleHandle(null);
            _kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, _kbProc, module, 0);
            _msHook = SetWindowsHookEx(WH_MOUSE_LL, _msProc, module, 0);
            if (_kbHook == IntPtr.Zero || _msHook == IntPtr.Zero)
                Log.Error($"Installing input hooks failed (error {Marshal.GetLastWin32Error()})");
            ready.Set();
            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
            UnhookWindowsHookEx(_kbHook);
            UnhookWindowsHookEx(_msHook);
        })
        { IsBackground = true, Name = "InputHook", Priority = ThreadPriority.Highest };
        _thread.Start();
        ready.Wait(3000);
    }

    /// <summary>
    /// Records the next key combo (or, with <paramref name="allowMouse"/>, a non-left mouse button) instead of passing it on.
    /// The callback runs on the hook thread; marshal to the UI yourself.
    /// </summary>
    public void BeginCapture(bool allowMouse, Action<CaptureResult> done)
    {
        lock (_engine)
        {
            _engine.Reset();
            _captureMouse = allowMouse;
            _captureMods = Modifiers.None;
            _captureLastMod = 0;
            _captureOtherKey = false;
            _capture = done;
        }
    }

    public void CancelCapture()
    {
        lock (_engine) _capture = null;
    }

    private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        if (k.dwExtraInfo == InputSender.Signature) return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

        int msg = (int)wParam;
        bool down = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
        int vk = (int)k.vkCode;
        bool swallow;
        try
        {
            lock (_engine)
            {
                if (_capture != null)
                {
                    HandleCaptureKey(vk, down);
                    return 1;
                }
                if (!down && _captureSwallowUps.Remove(vk)) return 1;
                GetCursorPos(out var pt);
                swallow = _engine.OnKey(vk, down, HeldModifiers(), pt.X, pt.Y);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Keyboard hook", ex);
            swallow = false;
        }
        return swallow ? 1 : CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        var m = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
        if (m.dwExtraInfo == InputSender.Signature) return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

        int msg = (int)wParam;
        bool swallow = false;
        try
        {
            lock (_engine)
            {
                if (msg == WM_MOUSEMOVE)
                {
                    _engine.OnMouseMove(m.pt.X, m.pt.Y);
                }
                else
                {
                    var (button, down) = msg switch
                    {
                        WM_LBUTTONDOWN => (MouseButton.Left, true),
                        WM_LBUTTONUP => (MouseButton.Left, false),
                        WM_RBUTTONDOWN => (MouseButton.Right, true),
                        WM_RBUTTONUP => (MouseButton.Right, false),
                        WM_MBUTTONDOWN => (MouseButton.Middle, true),
                        WM_MBUTTONUP => (MouseButton.Middle, false),
                        WM_XBUTTONDOWN => ((m.mouseData >> 16) == 1 ? MouseButton.X1 : MouseButton.X2, true),
                        WM_XBUTTONUP => ((m.mouseData >> 16) == 1 ? MouseButton.X1 : MouseButton.X2, false),
                        _ => (MouseButton.None, false),
                    };
                    if (button != MouseButton.None)
                    {
                        if (!down && _captureSwallowUps.Remove(-(int)button)) return 1;
                        if (_capture != null && _captureMouse && down && button != MouseButton.Left)
                        {
                            _captureSwallowUps.Add(-(int)button);
                            FinishCapture(new CaptureResult(button, 0, Modifiers.None));
                            return 1;
                        }
                        if (_capture == null)
                            swallow = _engine.OnMouseButton(button, down, m.pt.X, m.pt.Y);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Mouse hook", ex);
            swallow = false;
        }
        return swallow ? 1 : CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private void HandleCaptureKey(int vk, bool down)
    {
        if (down)
        {
            _captureSwallowUps.Add(vk);
            var mod = KeyNames.ModifierOf(vk);
            if (mod != Modifiers.None)
            {
                _captureMods |= mod;
                _captureLastMod = vk;
                _captureOtherKey = false;
            }
            else
            {
                _captureOtherKey = true;
                FinishCapture(new CaptureResult(MouseButton.None, vk, _captureMods));
            }
            return;
        }

        _captureSwallowUps.Remove(vk);
        var released = KeyNames.ModifierOf(vk);
        if (released != Modifiers.None && !_captureOtherKey && vk == _captureLastMod)
        {
            // A modifier pressed and released on its own (e.g. just "Win") is a valid shortcut.
            FinishCapture(new CaptureResult(MouseButton.None, vk, _captureMods & ~released));
            return;
        }
        _captureMods &= ~released;
    }

    private void FinishCapture(CaptureResult result)
    {
        var done = _capture;
        _capture = null;
        done?.Invoke(result);
    }

    private static Modifiers HeldModifiers()
    {
        var mods = Modifiers.None;
        if (IsKeyDown(KeyNames.VK_CONTROL)) mods |= Modifiers.Ctrl;
        if (IsKeyDown(KeyNames.VK_SHIFT)) mods |= Modifiers.Shift;
        if (IsKeyDown(KeyNames.VK_MENU)) mods |= Modifiers.Alt;
        if (IsKeyDown(KeyNames.VK_LWIN) || IsKeyDown(KeyNames.VK_RWIN)) mods |= Modifiers.Win;
        return mods;
    }

    public void Dispose()
    {
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread?.Join(1000);
    }
}
