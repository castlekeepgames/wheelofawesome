using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using WheelOfAwesome.Models;
using WheelOfAwesome.Native;
using WheelOfAwesome.Services;

namespace WheelOfAwesome.Icons;

/// <summary>
/// Resolves the image for a wheel item. Auto icons (favicons, app icons) are fetched in the background and cached
/// as PNGs in the icon folder; until they arrive a matching library glyph stands in.
/// </summary>
public static class IconService
{
    private static readonly Dictionary<string, ImageSource> Cache = new();
    private static readonly HashSet<string> Pending = new();
    private static readonly HashSet<string> Failed = new();

    /// <summary>Auto icons still downloading or extracting.</summary>
    public static int PendingCount => Pending.Count;
    private static readonly BlockingCollection<Action> ShellQueue = new();
    private static Thread? _shellThread;

    /// <summary>Raised on the UI thread when an icon finished loading and items should re-read their image.</summary>
    public static event Action? Changed;

    public static ImageSource Get(WheelItem item)
    {
        var key = KeyFor(item);
        if (Cache.TryGetValue(key, out var image)) return image;
        image = Resolve(item, key);
        Cache[key] = image;
        return image;
    }

    /// <summary>Forgets a cached auto icon so it's fetched again.</summary>
    public static void RefreshAuto(WheelItem item)
    {
        var key = AutoKey(item);
        if (key == null) return;
        try { File.Delete(AutoFile(key)); } catch { }
        Cache.Remove(key);
        Failed.Remove(key);
        Changed?.Invoke();
    }

    /// <summary>Copies a user-chosen image into the icon folder (so moving the original doesn't break it) and returns its file name.</summary>
    public static string? ImportCustom(string sourcePath)
    {
        var img = ImageLoader.LoadFile(sourcePath);
        if (img == null) return null;
        var name = $"custom_{Guid.NewGuid():N}{Path.GetExtension(sourcePath).ToLowerInvariant()}";
        File.Copy(sourcePath, Path.Combine(Paths.IconDir, name));
        return name;
    }

    public static ImageSource Glyph(string name, string color = "#FFFFFF") =>
        GetOrAdd($"L|{name}|{color}", () => LucideIcons.Render(name, ParseColor(color)));

    private static ImageSource GetOrAdd(string key, Func<ImageSource> make)
    {
        if (!Cache.TryGetValue(key, out var img)) Cache[key] = img = make();
        return img;
    }

    private static string KeyFor(WheelItem item) => item.Icon.Mode switch
    {
        IconMode.Library => $"L|{item.Icon.Glyph}|{item.Icon.Color}",
        IconMode.Custom => $"C|{item.Icon.CustomFile}",
        _ => AutoKey(item) ?? $"L|{PlaceholderGlyph(item)}|#FFFFFF",
    };

    private static string? AutoKey(WheelItem item)
    {
        switch (item.Kind)
        {
            case ItemKind.Link:
                var host = HostOf(item.Url);
                return host == null ? null : $"A|link|{host}";
            case ItemKind.App:
                var target = AppParsingName(item);
                return target == null ? null : $"A|app|{target.ToLowerInvariant()}";
            default:
                return null;
        }
    }

    private static string PlaceholderGlyph(WheelItem item) => item.Kind switch
    {
        ItemKind.Link => "globe",
        ItemKind.App => "app-window",
        _ => "keyboard",
    };

    private static ImageSource Resolve(WheelItem item, string key)
    {
        try
        {
            switch (item.Icon.Mode)
            {
                case IconMode.Library:
                    return LucideIcons.Render(item.Icon.Glyph, ParseColor(item.Icon.Color));
                case IconMode.Custom:
                    if (item.Icon.CustomFile != null && ImageLoader.LoadFile(Path.Combine(Paths.IconDir, item.Icon.CustomFile)) is { } custom)
                        return custom;
                    return Glyph("image-off");
            }

            if (key.StartsWith("A|"))
            {
                var file = AutoFile(key);
                if (File.Exists(file) && ImageLoader.LoadFile(file) is { } cached) return cached;
                if (!Failed.Contains(key) && Pending.Add(key))
                {
                    if (item.Kind == ItemKind.Link) FetchFavicon(item.Url, key, file);
                    else QueueAppIcon(AppParsingName(item)!, key, file);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Icon for '{item.Label}'", ex);
        }
        return Glyph(PlaceholderGlyph(item));
    }

    private static void FetchFavicon(string url, string key, string file)
    {
        var normalized = NormalizeUrl(url);
        var dispatcher = Application.Current.Dispatcher;
        Task.Run(async () =>
        {
            var downloads = await FaviconFetcher.DownloadCandidatesAsync(normalized);
            await dispatcher.InvokeAsync(() =>
            {
                Pending.Remove(key);
                var best = FaviconFetcher.PickBest(downloads);
                if (best == null)
                {
                    Failed.Add(key);
                    Log.Info($"Favicon: nothing usable for {normalized}");
                    return;
                }
                ImageLoader.SavePng(best, file, 128);
                Cache.Remove(key);
                Changed?.Invoke();
            });
        });
    }

    private static void QueueAppIcon(string parsingName, string key, string file)
    {
        EnsureShellThread();
        var dispatcher = Application.Current.Dispatcher;
        ShellQueue.Add(() =>
        {
            var bmp = Shell.GetIcon(parsingName, 256);
            dispatcher.InvokeAsync(() =>
            {
                Pending.Remove(key);
                if (bmp == null)
                {
                    Failed.Add(key);
                    return;
                }
                ImageLoader.SavePng(bmp, file, 128);
                Cache.Remove(key);
                Changed?.Invoke();
            });
        });
    }

    /// <summary>Shell icon extraction wants an STA thread; keep one around rather than blocking the UI.</summary>
    private static void EnsureShellThread()
    {
        if (_shellThread != null) return;
        _shellThread = new Thread(() =>
        {
            foreach (var work in ShellQueue.GetConsumingEnumerable())
            {
                try { work(); }
                catch (Exception ex) { Log.Error("Shell icon worker", ex); }
            }
        })
        { IsBackground = true, Name = "ShellIcons" };
        _shellThread.SetApartmentState(ApartmentState.STA);
        _shellThread.Start();
    }

    /// <summary>Runs work on the shell STA thread (used by the installed-apps picker).</summary>
    public static void RunOnShellThread(Action work)
    {
        EnsureShellThread();
        ShellQueue.Add(work);
    }

    public static string? AppParsingName(WheelItem item)
    {
        if (!string.IsNullOrEmpty(item.AppId)) return @"shell:AppsFolder\" + item.AppId;
        return string.IsNullOrWhiteSpace(item.Target) ? null : Environment.ExpandEnvironmentVariables(item.Target);
    }

    public static string NormalizeUrl(string url)
    {
        url = url.Trim();
        if (url.Length == 0) return url;
        return url.Contains("://") || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ? url : "https://" + url;
    }

    public static string? HostOf(string url)
    {
        return Uri.TryCreate(NormalizeUrl(url), UriKind.Absolute, out var u) && !string.IsNullOrEmpty(u.Host) ? u.Host.ToLowerInvariant() : null;
    }

    private static string AutoFile(string key)
    {
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key)))[..16].ToLowerInvariant();
        return Path.Combine(Paths.IconDir, $"auto_{hash}.png");
    }

    public static Color ParseColor(string color)
    {
        try { return (Color)ColorConverter.ConvertFromString(color); }
        catch { return Colors.White; }
    }
}
