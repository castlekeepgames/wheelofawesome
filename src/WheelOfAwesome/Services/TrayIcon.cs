using System.Windows;
using WinForms = System.Windows.Forms;

namespace WheelOfAwesome.Services;

public sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ToolStripMenuItem _pause;

    public event Action? OpenSettings;
    public event Action<bool>? PauseChanged;
    public event Action? Exit;

    public TrayIcon()
    {
        var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"))!.Stream;
        var menu = new WinForms.ContextMenuStrip { ShowImageMargin = false };
        menu.Items.Add("Settings…", null, (_, _) => OpenSettings?.Invoke()).Font = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold);
        _pause = new WinForms.ToolStripMenuItem("Pause wheel") { CheckOnClick = true };
        _pause.CheckedChanged += (_, _) =>
        {
            _icon!.Text = _pause.Checked ? "WheelOfAwesome (paused)" : "WheelOfAwesome";
            PauseChanged?.Invoke(_pause.Checked);
        };
        menu.Items.Add(_pause);
        menu.Items.Add("Open settings folder", null, (_, _) =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Paths.Root) { UseShellExecute = true }));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Exit?.Invoke());

        _icon = new WinForms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(stream, WinForms.SystemInformation.SmallIconSize),
            Text = "WheelOfAwesome",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == WinForms.MouseButtons.Left) OpenSettings?.Invoke(); };
    }

    public void Notify(string title, string text, bool warning = false) =>
        _icon.ShowBalloonTip(4000, title, text, warning ? WinForms.ToolTipIcon.Warning : WinForms.ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
