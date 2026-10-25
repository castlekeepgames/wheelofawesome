using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WheelOfAwesome.Input;
using WheelOfAwesome.Models;
using MouseButton = WheelOfAwesome.Models.MouseButton;

namespace WheelOfAwesome.UI;

/// <summary>
/// Click, then press a shortcut. Capture goes through the global hook, so system combos like Win+Shift+S are
/// recorded instead of firing.
/// </summary>
public sealed class HotkeyBox : Button
{
    public static readonly DependencyProperty VkProperty = DependencyProperty.Register(
        nameof(Vk), typeof(int), typeof(HotkeyBox), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((HotkeyBox)d).UpdateText()));

    public static readonly DependencyProperty ModsProperty = DependencyProperty.Register(
        nameof(Mods), typeof(Modifiers), typeof(HotkeyBox), new FrameworkPropertyMetadata(Modifiers.None, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((HotkeyBox)d).UpdateText()));

    public static readonly DependencyProperty MouseProperty = DependencyProperty.Register(
        nameof(Mouse), typeof(MouseButton), typeof(HotkeyBox), new FrameworkPropertyMetadata(MouseButton.None, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((HotkeyBox)d).UpdateText()));

    public static readonly DependencyProperty AllowMouseProperty = DependencyProperty.Register(
        nameof(AllowMouse), typeof(bool), typeof(HotkeyBox), new PropertyMetadata(false));

    private bool _capturing;

    public HotkeyBox()
    {
        HorizontalContentAlignment = HorizontalAlignment.Left;
        MinWidth = 200;
        Padding = new Thickness(12, 6, 12, 6);
        Loaded += (_, _) => UpdateText();
        LostKeyboardFocus += (_, _) => Cancel();
        Unloaded += (_, _) => Cancel();
    }

    public int Vk { get => (int)GetValue(VkProperty); set => SetValue(VkProperty, value); }
    public Modifiers Mods { get => (Modifiers)GetValue(ModsProperty); set => SetValue(ModsProperty, value); }
    public MouseButton Mouse { get => (MouseButton)GetValue(MouseProperty); set => SetValue(MouseProperty, value); }
    public bool AllowMouse { get => (bool)GetValue(AllowMouseProperty); set => SetValue(AllowMouseProperty, value); }

    protected override void OnClick()
    {
        base.OnClick();
        if (_capturing) { Cancel(); return; }
        var hook = App.Current.Controller?.Hook;
        if (hook == null) return;
        _capturing = true;
        Content = AllowMouse ? "Press a key, combo or mouse button…  (click here to cancel)" : "Press a key or combo…  (click here to cancel)";
        hook.BeginCapture(AllowMouse, result => Dispatcher.BeginInvoke(() => Apply(result)));
    }

    private void Apply(CaptureResult result)
    {
        if (!_capturing) return;
        _capturing = false;
        if (result.Mouse != MouseButton.None)
        {
            Mouse = result.Mouse;
            Vk = 0;
            Mods = Modifiers.None;
        }
        else
        {
            Mouse = MouseButton.None;
            Mods = result.Modifiers;
            Vk = result.Key;
        }
        UpdateText();
        // Keep keyboard focus away from the button so Space/Enter don't re-trigger capture.
        Keyboard.ClearFocus();
    }

    private void Cancel()
    {
        if (!_capturing) return;
        _capturing = false;
        App.Current.Controller?.Hook.CancelCapture();
        UpdateText();
    }

    private void UpdateText()
    {
        if (_capturing) return;
        Content = Mouse != MouseButton.None ? KeyNames.Mouse(Mouse) : KeyNames.Combo(Mods, Vk);
    }
}
