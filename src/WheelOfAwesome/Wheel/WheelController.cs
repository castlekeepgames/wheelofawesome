using System.Windows.Threading;
using WheelOfAwesome.Actions;
using WheelOfAwesome.Input;
using WheelOfAwesome.Models;
using WheelOfAwesome.Services;
using static WheelOfAwesome.Native.Win32;

namespace WheelOfAwesome.Wheel;

/// <summary>
/// Connects the trigger engine (which runs on the hook thread) to the overlay and actions (UI thread).
/// Engine calls arrive while the hook thread holds the engine lock, so everything here just posts work and returns.
/// </summary>
public sealed class WheelController : ITriggerHost, IDisposable
{
    private readonly Dispatcher _ui;
    private readonly OverlayWindow _overlay;
    private Timer? _holdTimer;
    private int _timerGeneration;
    private volatile int _px, _py;
    private int _pointerQueued;

    public WheelController(Dispatcher ui, OverlayWindow overlay)
    {
        _ui = ui;
        _overlay = overlay;
        Engine = new TriggerEngine(this)
        {
            Trigger = () => ConfigService.Current.Trigger,
            HoldMs = () => ConfigService.Current.HoldThresholdMs,
            StayOut = () => AppFilter.ShouldStayOut(ConfigService.Current.PauseInFullscreen),
        };
        Hook = new InputHook(Engine);
    }

    public TriggerEngine Engine { get; }
    public InputHook Hook { get; }

    /// <summary>Raised on the UI thread just before an item runs (used by the dev live test).</summary>
    public event Action<WheelItem>? Fired;

    /// <summary>Raised (on a pool thread) when a quick tap was handed back to the system.</summary>
    public event Action? Tapped;

    public bool Paused
    {
        get => !Engine.Enabled;
        set
        {
            lock (Engine)
            {
                Engine.Enabled = !value;
                if (value) Engine.Reset();
            }
        }
    }

    public void Start() => Hook.Install();

    public void StartHoldTimer(int ms)
    {
        _holdTimer?.Dispose();
        int generation = ++_timerGeneration;
        _holdTimer = new Timer(_ =>
        {
            lock (Engine)
            {
                if (generation == _timerGeneration) Engine.OnHoldTimer();
            }
        }, null, ms, Timeout.Infinite);
    }

    public void StopHoldTimer()
    {
        _timerGeneration++;
        _holdTimer?.Dispose();
        _holdTimer = null;
    }

    public void OpenWheel(int x, int y)
    {
        // Start from where the cursor is now, never a position left over from the previous time the wheel was open.
        GetCursorPos(out var pt);
        _px = pt.X;
        _py = pt.Y;
        _ui.BeginInvoke(() =>
        {
            _overlay.ShowAt(x, y);
            // The cursor may have moved during the hold (or been re-centred near a screen edge); pick from where it is.
            GetCursorPos(out var now);
            _px = now.X;
            _py = now.Y;
            _overlay.MovePointer(_px, _py);
        });
    }

    public void MovePointer(int x, int y)
    {
        _px = x;
        _py = y;
        // Coalesce: high-rate mice would otherwise flood the dispatcher.
        if (Interlocked.Exchange(ref _pointerQueued, 1) == 0)
            _ui.BeginInvoke(() =>
            {
                Interlocked.Exchange(ref _pointerQueued, 0);
                _overlay.MovePointer(_px, _py);
            });
    }

    public void CloseWheel(bool fire)
    {
        int x = _px, y = _py;
        _ui.BeginInvoke(() =>
        {
            if (fire) _overlay.MovePointer(x, y);
            var item = fire ? _overlay.SelectedItem : null;
            _overlay.HideWheel();
            if (item == null) return;
            Fired?.Invoke(item);
            ActionRunner.Run(item);
        });
    }

    public void ReplayTap(TriggerConfig trigger)
    {
        var mouse = trigger.MouseButton;
        var key = trigger.Key;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            if (mouse != MouseButton.None) InputSender.MouseClick(mouse);
            else InputSender.KeyTap(key);
            Tapped?.Invoke();
        });
    }

    public void SendMaskKey() => ThreadPool.QueueUserWorkItem(_ => InputSender.Mask());

    public void Dispose()
    {
        Hook.Dispose();
        _holdTimer?.Dispose();
    }
}
