using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace WheelOfAwesome.Icons;

public sealed record LucideIcon(string Name, string Glyph, string Tags);

/// <summary>The bundled Lucide icon font (ISC licensed), with a name/tag index for search.</summary>
public static class LucideIcons
{
    public static readonly FontFamily Font = new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#lucide");

    private static List<LucideIcon>? _all;
    private static Dictionary<string, LucideIcon>? _byName;
    private static GlyphTypeface? _glyphTypeface;

    public static IReadOnlyList<LucideIcon> All
    {
        get
        {
            if (_all == null) Load();
            return _all!;
        }
    }

    public static LucideIcon? Find(string name)
    {
        if (_byName == null) Load();
        return _byName!.TryGetValue(name, out var icon) ? icon : null;
    }

    public static IEnumerable<LucideIcon> Search(string query)
    {
        query = query.Trim().ToLowerInvariant();
        if (query.Length == 0) return All;
        // Name matches first, then tag matches.
        var byName = All.Where(i => i.Name.Contains(query, StringComparison.Ordinal));
        var byTag = All.Where(i => !i.Name.Contains(query, StringComparison.Ordinal) && i.Tags.Contains(query, StringComparison.Ordinal));
        return byName.Concat(byTag);
    }

    private static void Load()
    {
        var list = new List<LucideIcon>();
        var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/lucide-icons.txt"));
        using (var reader = new StreamReader(info!.Stream))
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var parts = line.Split('\t');
                if (parts.Length < 2) continue;
                var cp = int.Parse(parts[1], NumberStyles.HexNumber);
                list.Add(new LucideIcon(parts[0], char.ConvertFromUtf32(cp), parts.Length > 2 ? parts[2] : ""));
            }
        }
        _all = list;
        _byName = list.ToDictionary(i => i.Name);
    }

    /// <summary>Renders an icon as a resolution-independent image, centred in its em square.</summary>
    public static ImageSource Render(string name, Color color)
    {
        var icon = Find(name) ?? Find("circle-help") ?? All[0];
        if (_glyphTypeface == null)
        {
            var typeface = new Typeface(Font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            typeface.TryGetGlyphTypeface(out _glyphTypeface);
        }

        const double em = 100;
        var group = new DrawingGroup();
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        // Transparent em square keeps every icon at the same scale and padding.
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, em, em))));

        int cp = char.ConvertToUtf32(icon.Glyph, 0);
        if (_glyphTypeface != null && _glyphTypeface.CharacterToGlyphMap.TryGetValue(cp, out var index))
        {
            var outline = _glyphTypeface.GetGlyphOutline(index, em, em);
            double advance = _glyphTypeface.AdvanceWidths[index] * em;
            double top = -_glyphTypeface.Baseline * em;
            double height = _glyphTypeface.Height * em;
            var box = new Rect(0, top, advance, height);
            // Centre the glyph's design box inside the em square.
            var transform = new TranslateTransform((em - box.Width) / 2 - box.X, (em - box.Height) / 2 - box.Y);
            var geometry = outline.Clone();
            geometry.Transform = transform;
            group.Children.Add(new GeometryDrawing(brush, null, geometry));
        }

        group.Freeze();
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }
}
