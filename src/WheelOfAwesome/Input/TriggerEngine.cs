using WheelOfAwesome.Models;
using MouseButton = WheelOfAwesome.Models.MouseButton;

namespace WheelOfAwesome.Input;

/// <summary>What the engine asks its host to do. Kept as an interface so the state machine is testable without real input.</summary>
public interface ITriggerHost
{
    void StartHoldTimer(int ms);
    void StopHoldTimer();
    void OpenWheel(int x, int y);
    void MovePointer(int x, int y);
    /// <summary>Close the wheel; when <paramref name="fire"/> is true, run the highlighted slice.</summary>
    void CloseWheel(bool fire);
    /// <summary>The trigger was only tapped: give the original key/click back to the system.</summary>
    void ReplayTap(TriggerConfig trigger);
    void SendMaskKey();
}

/// <summary>
/// Hold-to-open state machine. Every input event goes through <see cref="OnKey"/>/<see cref="OnMouseButton"/>/<see cref="OnMouseMove"/>;
/// the return value says whether to swallow the event.
/// </summary>
public sealed class TriggerEngine
{
    private enum State { Idle, Pressed, Open }

    private readonly ITriggerHost _host;
    private readonly HashSet<string> _pendingUps = new();
    private State _state = State.Idle;
    private TriggerConfig? _active;
    private int _pressX, _pressY;
    private DateTime _openedAt;

    public TriggerEngine(ITriggerHost host) => _host = host;

    public Func<TriggerConfig> Trigger { get; set; } = () => new TriggerConfig();
    public Func<int> HoldMs { get; set; } = () => 150;
    public bool Enabled { get; set; } = true;
    public bool IsOpen => _state == State.Open;

    public bool OnKey(int vk, bool down, Modifiers heldModifiers, int cursorX, int cursorY)
    {
        var id = "K" + vk;
        if (!down && _pendingUps.Remove(id)) return true;
        if (!Enabled && _state == State.Idle) return false;

        var trigger = _state == State.Idle ? Trigger() : _active!;
        bool isTriggerKey = !trigger.IsMouse && trigger.Key != 0 && vk == trigger.Key;

        switch (_state)
        {
            case State.Idle:
                if (down && isTriggerKey && WithoutSelf(heldModifiers, vk) == trigger.Modifiers)
                {
                    Begin(trigger, cursorX, cursorY);
                    if (trigger.Modifiers.HasFlag(Modifiers.Alt) || trigger.Modifiers.HasFlag(Modifiers.Win)) _host.SendMaskKey();
                    return true;
                }
                return false;

            case State.Pressed:
                if (isTriggerKey)
                {
                    if (!down) { _host.StopHoldTimer(); _state = State.Idle; _host.ReplayTap(trigger); }
                    return true; // auto-repeat while held is swallowed too
                }
                return false;

            case State.Open:
                if (isTriggerKey)
                {
                    if (!down) Close(fire: true);
                    return true;
                }
                if (vk == KeyNames.VK_ESCAPE)
                {
                    if (down) { Close(fire: false); _pendingUps.Add(id); AddTriggerRelease(trigger); }
                    return true;
                }
                return false;
        }
        return false;
    }

    public bool OnMouseButton(MouseButton button, bool down, int x, int y)
    {
        var id = "M" + button;
        if (!down && _pendingUps.Remove(id)) return true;
        if (!Enabled && _state == State.Idle) return false;

        var trigger = _state == State.Idle ? Trigger() : _active!;
        bool isTriggerButton = trigger.IsMouse && button == trigger.MouseButton;

        // A second "down" of a mouse trigger means we missed its release (e.g. a UAC prompt ate it): start over.
        if (isTriggerButton && down && _state != State.Idle)
        {
            Reset();
        }

        switch (_state)
        {
            case State.Idle:
                if (isTriggerButton && down)
                {
                    Begin(trigger, x, y);
                    return true;
                }
                return false;

            case State.Pressed:
                if (isTriggerButton && !down)
                {
                    _host.StopHoldTimer();
                    _state = State.Idle;
                    _host.ReplayTap(trigger);
                    return true;
                }
                return false;

            case State.Open:
                if (isTriggerButton && !down)
                {
                    Close(fire: true);
                    return true;
                }
                if (down && button == MouseButton.Left)
                {
                    Close(fire: true);
                    _pendingUps.Add(id);
                    AddTriggerRelease(trigger);
                    return true;
                }
                if (down && button == MouseButton.Right)
                {
                    Close(fire: false);
                    _pendingUps.Add(id);
                    AddTriggerRelease(trigger);
                    return true;
                }
                if (down)
                {
                    // Other buttons are ignored while the wheel is up so they don't click through to the app below.
                    _pendingUps.Add(id);
                    return true;
                }
                return false;
        }
        return false;
    }

    public void OnMouseMove(int x, int y)
    {
        if (_state != State.Open) return;
        // Safety net: never leave a wheel up indefinitely if a release was lost.
        if ((DateTime.UtcNow - _openedAt).TotalSeconds > 30) { Close(fire: false); return; }
        _host.MovePointer(x, y);
    }

    public void OnHoldTimer()
    {
        _host.StopHoldTimer();
        if (_state != State.Pressed) return;
        _state = State.Open;
        _openedAt = DateTime.UtcNow;
        _host.OpenWheel(_pressX, _pressY);
    }

    /// <summary>Cancels anything in progress (e.g. when the app is paused or settings change).</summary>
    public void Reset()
    {
        _host.StopHoldTimer();
        if (_state == State.Open) _host.CloseWheel(fire: false);
        _state = State.Idle;
        _pendingUps.Clear();
    }

    private void Begin(TriggerConfig trigger, int x, int y)
    {
        _active = trigger;
        _pressX = x;
        _pressY = y;
        _state = State.Pressed;
        _host.StartHoldTimer(HoldMs());
    }

    private void Close(bool fire)
    {
        _state = State.Idle;
        _host.CloseWheel(fire);
    }

    /// <summary>The wheel was closed by a click while the trigger is still held: eat the trigger's eventual release.</summary>
    private void AddTriggerRelease(TriggerConfig trigger) =>
        _pendingUps.Add(trigger.IsMouse ? "M" + trigger.MouseButton : "K" + trigger.Key);

    private static Modifiers WithoutSelf(Modifiers held, int vk) => held & ~KeyNames.ModifierOf(vk);
}
