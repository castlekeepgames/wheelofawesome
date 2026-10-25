using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows.Media;
using WheelOfAwesome.Services;

namespace WheelOfAwesome.Icons;

/// <summary>
/// Finds the best icon a site publishes: &lt;link rel="icon"/"apple-touch-icon"&gt; tags first (largest wins),
/// then /apple-touch-icon.png and /favicon.ico. Talks only to the site itself.
/// </summary>
public static partial class FaviconFetcher
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5 };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,image/*;q=0.9,*/*;q=0.8");
        return client;
    }

    [GeneratedRegex(@"<link\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LinkTag();

    [GeneratedRegex(@"\b(rel|href|sizes|type)\s*=\s*(""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.IgnoreCase)]
    private static partial Regex Attr();

    private sealed record Candidate(Uri Url, int Score);

    private const int PreferredTier = 10000;

    /// <summary>Downloads candidate icons (off the UI thread) and returns the raw bytes of the best one.</summary>
    public static async Task<List<(byte[] Data, bool Svg, int Score)>> DownloadCandidatesAsync(string pageUrl)
    {
        var results = new List<(byte[], bool, int)>();
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var page) || (page.Scheme != "http" && page.Scheme != "https"))
            return results;

        var candidates = new List<Candidate>();
        var baseUri = page;
        try
        {
            using var resp = await Http.GetAsync(page, HttpCompletionOption.ResponseHeadersRead);
            baseUri = resp.RequestMessage?.RequestUri ?? page; // after redirects
            if (resp.IsSuccessStatusCode)
            {
                var html = await ReadLimitedStringAsync(resp, 768 * 1024);
                candidates.AddRange(ParseLinks(html, baseUri));
            }
        }
        catch (Exception ex)
        {
            Log.Info($"Favicon: page fetch failed for {page.Host}: {ex.Message}");
        }

        var root = new Uri(baseUri.GetLeftPart(UriPartial.Authority));
        candidates.Add(new Candidate(new Uri(root, "/apple-touch-icon.png"), 150));
        candidates.Add(new Candidate(new Uri(root, "/favicon.ico"), 20));

        // Signed-out apps often redirect elsewhere (mail.google.com -> accounts/workspace). The icon the user
        // expects is the original host's own, so those candidates form a preferred tier.
        if (!string.Equals(baseUri.Host, page.Host, StringComparison.OrdinalIgnoreCase))
        {
            var original = new Uri(page.GetLeftPart(UriPartial.Authority));
            candidates.Add(new Candidate(new Uri(original, "/apple-touch-icon.png"), PreferredTier + 150));
            candidates.Add(new Candidate(new Uri(original, "/favicon.ico"), PreferredTier + 20));
        }

        foreach (var c in candidates.GroupBy(c => c.Url.AbsoluteUri).Select(g => g.OrderByDescending(x => x.Score).First()).OrderByDescending(c => c.Score))
        {
            try
            {
                using var resp = await Http.GetAsync(c.Url);
                if (!resp.IsSuccessStatusCode) continue;
                var bytes = await resp.Content.ReadAsByteArrayAsync();
                if (bytes.Length == 0 || bytes.Length > 4 * 1024 * 1024) continue;
                var type = resp.Content.Headers.ContentType?.MediaType ?? "";
                if (type.StartsWith("text/html")) continue;
                bool svg = type.Contains("svg") || c.Url.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
                results.Add((bytes, svg, c.Score));
                if (results.Count >= 3) break;
            }
            catch
            {
                // Try the next candidate.
            }
        }
        return results;
    }

    /// <summary>Decodes the downloaded candidates (UI thread) and keeps the sharpest one.</summary>
    public static ImageSource? PickBest(List<(byte[] Data, bool Svg, int Score)> downloads)
    {
        var preferred = downloads.Where(d => d.Score >= PreferredTier).ToList();
        if (preferred.Count > 0 && PickLargest(preferred) is { } fromOriginalHost) return fromOriginalHost;
        return PickLargest(downloads.Where(d => d.Score < PreferredTier).ToList());
    }

    private static ImageSource? PickLargest(List<(byte[] Data, bool Svg, int Score)> downloads)
    {
        ImageSource? best = null;
        int bestSize = 0;
        foreach (var (data, svg, _) in downloads)
        {
            try
            {
                var img = ImageLoader.Load(new MemoryStream(data), svg);
                if (img == null) continue;
                var size = ImageLoader.PixelSize(img);
                if (size > bestSize) { best = img; bestSize = size; }
                if (bestSize >= 96) break;
            }
            catch
            {
                // Undecodable; ignore.
            }
        }
        return best;
    }

    private static IEnumerable<Candidate> ParseLinks(string html, Uri baseUri)
    {
        foreach (Match tag in LinkTag().Matches(html))
        {
            string? rel = null, href = null, sizes = null, type = null;
            foreach (Match a in Attr().Matches(tag.Value))
            {
                var value = a.Groups[3].Success ? a.Groups[3].Value : a.Groups[4].Success ? a.Groups[4].Value : a.Groups[5].Value;
                switch (a.Groups[1].Value.ToLowerInvariant())
                {
                    case "rel": rel = value.ToLowerInvariant(); break;
                    case "href": href = System.Net.WebUtility.HtmlDecode(value); break;
                    case "sizes": sizes = value.ToLowerInvariant(); break;
                    case "type": type = value.ToLowerInvariant(); break;
                }
            }
            if (rel == null || href == null || !rel.Contains("icon") || rel.Contains("mask-icon")) continue;
            if (!Uri.TryCreate(baseUri, href, out var url) || (url.Scheme != "http" && url.Scheme != "https")) continue;

            int score = rel.Contains("apple-touch-icon") ? 180 : 32;
            if (sizes != null)
            {
                if (sizes == "any") score = 300;
                else
                {
                    var m = Regex.Match(sizes, @"(\d+)x(\d+)");
                    if (m.Success) score = int.Parse(m.Groups[1].Value);
                }
            }
            if (type?.Contains("svg") == true || url.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) score = Math.Max(score, 250);
            // Huge icons are fine but don't need to beat a crisp 192px one on download size.
            if (score > 512) score = 400;
            yield return new Candidate(url, score);
        }
    }

    private static async Task<string> ReadLimitedStringAsync(HttpResponseMessage resp, int maxBytes)
    {
        await using var stream = await resp.Content.ReadAsStreamAsync();
        var buffer = new byte[maxBytes];
        int total = 0, read;
        while (total < maxBytes && (read = await stream.ReadAsync(buffer.AsMemory(total, maxBytes - total))) > 0) total += read;
        return System.Text.Encoding.UTF8.GetString(buffer, 0, total);
    }
}
