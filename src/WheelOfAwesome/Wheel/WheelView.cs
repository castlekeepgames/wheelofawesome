using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WheelOfAwesome.Icons;
using WheelOfAwesome.Models;

namespace WheelOfAwesome.Wheel;

/// <summary>
/// Draws the radial menu. Slice 0 is centred at 12 o'clock and slices run clockwise.
/// Used both by the overlay and (interactively) as the live preview in settings.
/// </summary>
public sealed class WheelView : FrameworkElement
{
    public const double RingMargin = 14;

    private static readonly Color Ring = Color.FromArgb(0xEE, 0x16, 0x18, 0x1E);
    private static readonly Color SliceFill = Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF);
    private static readonly Color Divider = Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF);
    private static readonly Color Hub = Color.FromArgb(0xF8, 0x0E, 0x10, 0x14);

    private IList<WheelItem> _items = Array.Empty<WheelItem>();
    private int _selected = -1;
    private double _pointerAngle = double.NaN;

    public WheelView()
    {
        SnapsToDevicePixels = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }

    public IList<WheelItem> Items
    {
        get => _items;
        set { _items = value; InvalidateVisual(); }
    }

    public double Radius { get; set; } = 170;
    public Color Accent { get; set; } = Color.FromRgb(0x4F, 0x8C, 0xFF);
    public bool ShowSliceLabels { get; set; }
    /// <summary>Preview mode: hover highlights a slice and clicking raises <see cref="SliceClicked"/>.</summary>
    public bool Interactive { get; set; }

    public event Action<int>? SliceClicked;

    /// <summary>Preview mode: the slice shown highlighted when the mouse isn't hovering one (the item being edited).</summary>
    public int Pinned { get; set; } = -1;

    public int Selected
    {
        get => _selected;
        set { if (_selected != value) { _selected = value; InvalidateVisual(); } }
    }

    public double InnerRadius => Radius * 0.40;
    public double Diameter => 2 * (Radius + RingMargin);

    public void ApplyAppearance(Appearance a)
    {
        Radius = a.Radius;
        Accent = a.AccentColor;
        ShowSliceLabels = a.ShowSliceLabels;
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>Picks the slice in the direction (dx, dy) from the centre; within the dead zone, nothing.</summary>
    public int HitTest(double dx, double dy, double deadZone)
    {
        int n = _items.Count;
        if (n == 0 || dx * dx + dy * dy < deadZone * deadZone)
        {
            _pointerAngle = double.NaN;
            return -1;
        }
        double angle = Math.Atan2(dx, -dy) * 180 / Math.PI; // 0 = up, clockwise
        if (angle < 0) angle += 360;
        _pointerAngle = angle;
        double span = 360.0 / n;
        return (int)Math.Floor((angle + span / 2) / span) % n;
    }

    public void SetPointer(double dx, double dy, double deadZone)
    {
        var before = _pointerAngle;
        Selected = HitTest(dx, dy, deadZone);
        if (!before.Equals(_pointerAngle)) InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(Diameter, Diameter);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!Interactive) return;
        var c = Diameter / 2;
        var p = e.GetPosition(this);
        double dx = p.X - c, dy = p.Y - c, dist = Math.Sqrt(dx * dx + dy * dy);
        Selected = dist <= Radius && dist >= InnerRadius * 0.5 ? HitTest(dx, dy, 0) : Pinned;
        _pointerAngle = double.NaN;
        Cursor = Selected >= 0 && Selected != Pinned ? Cursors.Hand : null;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (Interactive) Selected = Pinned;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (Interactive && Selected >= 0) SliceClicked?.Invoke(Selected);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double c = Diameter / 2;
        var center = new Point(c, c);
        double r = Radius, r0 = InnerRadius;
        int n = _items.Count;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // Hit-testable background for preview hover.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, Diameter, Diameter));

        // Soft shadow + ring body.
        for (int i = 4; i >= 1; i--)
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(10 + (4 - i) * 6), 0, 0, 0)), null, center, r + i * 3, r + i * 3);
        dc.DrawEllipse(new SolidColorBrush(Ring), new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), 1), center, r, r);

        if (n == 0)
        {
            DrawCenteredText(dc, "No shortcuts yet.\nOpen settings from the tray icon.", center, 13, Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF), r * 1.4, dpi, FontWeights.Normal);
            return;
        }

        double span = 360.0 / n;
        double gapDeg = n == 1 ? 0 : Math.Min(2.2, span * 0.08);
        var accentBrush = new SolidColorBrush(Accent);
        var sliceBrush = new SolidColorBrush(SliceFill);
        var dividerPen = new Pen(new SolidColorBrush(Divider), 1);

        for (int i = 0; i < n; i++)
        {
            double mid = i * span;
            double start = mid - span / 2 + gapDeg / 2, end = mid + span / 2 - gapDeg / 2;
            bool sel = i == _selected;
            var geo = n == 1 ? Annulus(center, r0 + 4, r - 5) : Sector(center, r0 + 4, r - 5, start, end);

            if (sel)
            {
                // Glow under the highlighted slice.
                var glow = new SolidColorBrush(Color.FromArgb(0x55, Accent.R, Accent.G, Accent.B));
                dc.DrawGeometry(glow, null, n == 1 ? Annulus(center, r0 + 1, r - 1) : Sector(center, r0 + 1, r - 1, start - 0.8, end + 0.8));
                dc.DrawGeometry(accentBrush, null, geo);
            }
            else
            {
                dc.DrawGeometry(sliceBrush, dividerPen, geo);
            }

            // Icon (and optional label) at the slice's middle radius.
            double rm = (r0 + r) / 2;
            double rad = mid * Math.PI / 180;
            double chord = 2 * rm * Math.Sin(Math.Min(span, 120) * Math.PI / 360);
            double iconSize = Math.Min(r * (sel ? 0.25 : 0.22), chord * 0.55);
            var iconCenter = new Point(c + rm * Math.Sin(rad), c - rm * Math.Cos(rad));
            if (ShowSliceLabels) iconCenter.Y -= iconSize * 0.22;

            var img = _items[i].IconImage;
            dc.DrawImage(img, new Rect(iconCenter.X - iconSize / 2, iconCenter.Y - iconSize / 2, iconSize, iconSize));

            if (ShowSliceLabels)
            {
                var labelPos = new Point(iconCenter.X, iconCenter.Y + iconSize / 2 + 9);
                DrawCenteredText(dc, _items[i].Label, labelPos, 10.5, Color.FromArgb(sel ? (byte)0xFF : (byte)0xC8, 0xFF, 0xFF, 0xFF), chord * 0.9, dpi, FontWeights.SemiBold, maxLines: 1);
            }
        }

        // Hub with the highlighted item's name.
        dc.DrawEllipse(new SolidColorBrush(Hub), new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), 1), center, r0 - 2, r0 - 2);

        if (!double.IsNaN(_pointerAngle))
        {
            // Small notch on the hub pointing where the cursor is.
            double a = _pointerAngle * Math.PI / 180;
            var tip = new Point(c + (r0 - 3) * Math.Sin(a), c - (r0 - 3) * Math.Cos(a));
            var l = new Point(c + (r0 - 13) * Math.Sin(a - 0.16), c - (r0 - 13) * Math.Cos(a - 0.16));
            var rr = new Point(c + (r0 - 13) * Math.Sin(a + 0.16), c - (r0 - 13) * Math.Cos(a + 0.16));
            var tri = new StreamGeometry();
            using (var ctx = tri.Open()) { ctx.BeginFigure(tip, true, true); ctx.LineTo(l, true, false); ctx.LineTo(rr, true, false); }
            dc.DrawGeometry(accentBrush, null, tri);
        }

        if (_selected >= 0 && _selected < n)
            DrawCenteredText(dc, _items[_selected].Label, center, Math.Max(12, r0 * 0.2), Colors.White, r0 * 1.6, dpi, FontWeights.SemiBold, maxLines: 3);
        else
            DrawCenteredText(dc, Interactive ? "Preview" : "Esc to cancel", center, 11, Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF), r0 * 1.6, dpi, FontWeights.Normal);
    }

    private static void DrawCenteredText(DrawingContext dc, string text, Point center, double size, Color color, double maxWidth, double dpi, FontWeight weight, int maxLines = 4)
    {
        if (string.IsNullOrEmpty(text)) return;
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal),
            size, new SolidColorBrush(color), dpi)
        {
            MaxTextWidth = Math.Max(10, maxWidth),
            TextAlignment = TextAlignment.Center,
            Trimming = TextTrimming.CharacterEllipsis,
            MaxLineCount = maxLines,
        };
        dc.DrawText(ft, new Point(center.X - ft.MaxTextWidth / 2, center.Y - ft.Height / 2));
    }

    private static Geometry Sector(Point c, double r0, double r1, double startDeg, double endDeg)
    {
        Point P(double r, double deg)
        {
            double a = deg * Math.PI / 180;
            return new Point(c.X + r * Math.Sin(a), c.Y - r * Math.Cos(a));
        }
        bool large = endDeg - startDeg > 180;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(P(r0, startDeg), true, true);
            ctx.LineTo(P(r1, startDeg), true, true);
            ctx.ArcTo(P(r1, endDeg), new Size(r1, r1), 0, large, SweepDirection.Clockwise, true, true);
            ctx.LineTo(P(r0, endDeg), true, true);
            ctx.ArcTo(P(r0, startDeg), new Size(r0, r0), 0, large, SweepDirection.Counterclockwise, true, true);
        }
        g.Freeze();
        return g;
    }

    private static Geometry Annulus(Point c, double r0, double r1)
    {
        var g = new CombinedGeometry(GeometryCombineMode.Exclude, new EllipseGeometry(c, r1, r1), new EllipseGeometry(c, r0, r0));
        g.Freeze();
        return g;
    }
}
