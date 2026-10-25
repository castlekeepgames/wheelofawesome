using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WheelOfAwesome.Models;
using WheelOfAwesome.Services;
using static WheelOfAwesome.Native.Win32;

namespace WheelOfAwesome.Wheel;

/// <summary>
/// A borderless, transparent, click-through window that never takes focus — so key macros land in the app
/// the user was working in. Positioned in physical pixels around the cursor.
/// </summary>
public sealed class OverlayWindow : Window
{
    private readonly WheelView _wheel = new();
    private readonly ScaleTransform _scale = new(1, 1);
    private IntPtr _hwnd;
    private int _centerX, _centerY;
    private double _dpiScale = 1;

    public OverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        Title = "WheelOfAwesome";
        Left = -32000;
        Top = -32000;

        _wheel.RenderTransformOrigin = new Point(0.5, 0.5);
        _wheel.RenderTransform = _scale;
        Content = _wheel;
        SizeToContent = SizeToContent.WidthAndHeight;

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            var ex = (long)GetWindowLongPtr(_hwnd, GWL_EXSTYLE);
            ex |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED;
            SetWindowLongPtr(_hwnd, GWL_EXSTYLE, (IntPtr)ex);
        };
        DpiChanged += (_, _) => { if (IsVisible) Place(); };
    }

    public WheelItem? SelectedItem
    {
        get
        {
            var items = _wheel.Items;
            return _wheel.Selected >= 0 && _wheel.Selected < items.Count ? items[_wheel.Selected] : null;
        }
    }

    /// <summary>Creates the native window up front so the first open is instant.</summary>
    public void Prepare()
    {
        Show();
        Hide();
    }

    public void ShowAt(int x, int y)
    {
        var config = ConfigService.Current;
        _wheel.ApplyAppearance(config.Appearance);
        _wheel.Items = config.Items.ToList();
        _wheel.Selected = -1;
        _wheel.SetPointer(0, 0, 1);

        // Keep the whole wheel on the monitor; if it has to shift, move the cursor to its centre so directions stay true.
        var monitor = MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST);
        GetDpiForMonitor(monitor, 0, out var dpiX, out _);
        _dpiScale = dpiX / 96.0;
        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref info);
        int half = (int)Math.Ceiling(_wheel.Radius * _dpiScale);
        var b = info.rcMonitor;
        int cx = Math.Clamp(x, b.Left + half, Math.Max(b.Left + half, b.Right - half));
        int cy = Math.Clamp(y, b.Top + half, Math.Max(b.Top + half, b.Bottom - half));
        if (cx != x || cy != y) SetCursorPos(cx, cy);
        _centerX = cx;
        _centerY = cy;

        Show();
        Place();
        Place(); // a second pass settles any DPI change from moving between monitors

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var grow = new DoubleAnimation(0.86, 1, TimeSpan.FromMilliseconds(110)) { EasingFunction = ease };
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(90)));
    }

    public void MovePointer(int x, int y)
    {
        double dead = _wheel.InnerRadius * 0.45 * _dpiScale;
        _wheel.SetPointer(x - _centerX, y - _centerY, dead);
    }

    public void HideWheel()
    {
        BeginAnimation(OpacityProperty, null);
        Hide();
    }

    private void Place()
    {
        if (_hwnd == IntPtr.Zero) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        int size = (int)Math.Ceiling(_wheel.Diameter * dpi.DpiScaleX);
        SetWindowPos(_hwnd, HWND_TOPMOST, _centerX - size / 2, _centerY - size / 2, size, size, SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }
}
