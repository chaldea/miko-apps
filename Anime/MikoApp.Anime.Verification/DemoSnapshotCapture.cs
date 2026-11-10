using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MikoApp.Anime.Services;
using SkiaSharp;

namespace MikoApp.Anime.Verification;

/// <summary>Explicit maintenance command: read-only catalog capture, never requests a playable media URL.</summary>
internal static class DemoSnapshotCapture
{
    public static async Task RunAsync()
    {
        var output = Path.GetFullPath("Anime/MikoApp.Anime/Assets/Demo");
        var staging = Path.GetFullPath(Path.Combine("Anime/artifacts/demo-capture", DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss")));
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(output);
        using var api = new LanercClient();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        async Task<JsonNode> Read(string key, Func<Task<JsonNode>> fetch)
        {
            var file = Path.Combine(staging, key + ".json");
            if (File.Exists(file)) return JsonNode.Parse(await File.ReadAllTextAsync(file))!;
            await Task.Delay(800);
            var data = await fetch();
            await File.WriteAllTextAsync(file, data.ToJsonString());
            return data;
        }
        var home = await Read("home", () => api.HomeAsync());
        var rank = await Read("rank", () => api.RankAsync());
        var week = await Read("week", () => api.WeekAsync());
        var categories = home.Items("vod_list").ToArray();
        var catalog = new Dictionary<int, JsonNode>();
        var categoryIds = new Dictionary<int, int>();
        void Add(IEnumerable<JsonNode> rows, int category = 20)
        {
            foreach (var row in rows.Where(r => r.Int("id") > 0))
            {
                catalog[row.Int("id")] = row;
                categoryIds.TryAdd(row.Int("id"), category);
            }
        }
        foreach (var section in categories)
        {
            Add(section.Items("vods").Take(6), section.Int("sort_id"));
        }
        Add(home.Items("hot_list"));
        Add(rank.Items("rank_list").SelectMany(g => g.Items("vods").Take(2)));
        foreach (var day in new[] { "mon", "tue", "wed", "thu", "fri", "sat", "sun" }) Add(week["week_list"].Items(day).Take(1));
        foreach (var banner in home.Items("banner"))
            if (banner.Int("vod_id") > 0) catalog.TryAdd(banner.Int("vod_id"), new JsonObject { ["id"] = banner.Int("vod_id") });

        var images = new Dictionary<string, string>();
        var missingImages = new List<string>();
        async Task<string> Image(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return "res://Assets/poster-placeholder.svg";
            if (images.TryGetValue(url, out var existing)) return existing;
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
            var filename = "image-" + hash[..20].ToLowerInvariant() + ".webp";
            var file = Path.Combine(output, filename);
            try
            {
                if (!File.Exists(file))
                {
                    // Reuse validated image bytes already downloaded by the app, when available.
                    var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MikoAnime", "images", hash + ".imgcache");
                    byte[] bytes;
                    if (File.Exists(cache)) bytes = await File.ReadAllBytesAsync(cache);
                    else
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                        if (uri.Host.EndsWith(".doubanio.com", StringComparison.OrdinalIgnoreCase))
                        {
                            request.Headers.TryAddWithoutValidation("Referer", "https://douban.com");
                            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/127.0.0.0 Safari/537.36");
                        }
                        using var response = await http.SendAsync(request);
                        response.EnsureSuccessStatusCode();
                        bytes = await response.Content.ReadAsByteArrayAsync();
                    }
                    using var bitmap = SKBitmap.Decode(bytes) ?? throw new InvalidDataException("Invalid image");
                    var scale = Math.Min(1d, 720d / Math.Max(bitmap.Width, bitmap.Height));
                    using var resized = bitmap.Resize(new SKImageInfo(Math.Max(1, (int)(bitmap.Width * scale)), Math.Max(1, (int)(bitmap.Height * scale))), new SKSamplingOptions(SKFilterMode.Linear))!;
                    using var encoded = resized.Encode(SKEncodedImageFormat.Webp, 85);
                    await File.WriteAllBytesAsync(file, encoded.ToArray());
                }
                return images[url] = "res://Assets/Demo/" + filename;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException)
            {
                missingImages.Add(url);
                Console.WriteLine($"Image unavailable: {uri.Host}; bundled placeholder used.");
                return images[url] = "res://Assets/poster-placeholder.svg";
            }
        }
        var details = new JsonObject();
        var normalized = new Dictionary<int, JsonNode>();
        var count = 0;
        foreach (var id in catalog.Keys.Order())
        {
            var detail = await Read("detail-" + id, () => api.DetailAsync(id));
            var info = detail["video_play_info"] ?? throw new InvalidDataException("Missing detail: " + id);
            var title = Pick(info, "id", "vod_name", "vod_class", "vod_tag", "vod_year", "vod_total", "vod_score", "vod_blurb", "vod_sub", "vod_author", "vod_remarks", "vod_isend");
            title["id"] = id;
            title["vod_pic"] = await Image(info.Text("vod_pic", catalog[id].Text("vod_pic")));
            title["demo_class_id"] = categoryIds.GetValueOrDefault(id, 20);
            normalized[id] = title;
            var lines = new JsonArray();
            foreach (var line in detail.Items("video_play_list"))
            {
                var episodes = new JsonArray(line.Items("video").Select(ep => (JsonNode)new JsonObject
                {
                    ["title"] = ep.Text("title"), ["resource_id"] = "demo-video", ["can_play_without_login"] = true
                }).ToArray());
                lines.Add(new JsonObject { ["player"] = line.Text("player"), ["name"] = line.Text("name"), ["video"] = episodes });
            }
            details[id.ToString()] = new JsonObject { ["video_play_info"] = title.DeepClone(), ["video_play_list"] = lines };
            if (++count % 10 == 0 || count == catalog.Count) Console.WriteLine($"Captured details: {count}/{catalog.Count}");
        }
        JsonArray Rows(IEnumerable<JsonNode> rows) => new(rows.Where(row => normalized.ContainsKey(row.Int("id"))).Select(row => normalized[row.Int("id")].DeepClone()).ToArray());
        var sections = new JsonArray(categories.Select(c => (JsonNode)new JsonObject
        {
            ["sort_id"] = c.Int("sort_id"), ["sort_name"] = c.Text("sort_name"), ["type_class"] = c.Text("type_class"), ["type_year"] = c.Text("type_year"), ["vods"] = Rows(c.Items("vods"))
        }).ToArray());
        var banners = new JsonArray();
        foreach (var banner in home.Items("banner").Where(b => b.Int("vod_id") > 0))
            banners.Add(new JsonObject { ["vod_id"] = banner.Int("vod_id"), ["image"] = await Image(banner.Text("image")) });
        var days = new JsonObject();
        foreach (var day in new[] { "mon", "tue", "wed", "thu", "fri", "sat", "sun" }) days[day] = Rows(week["week_list"].Items(day));
        var snapshot = new JsonObject
        {
            ["captured_at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["home"] = new JsonObject { ["vod_list"] = sections, ["hot_list"] = Rows(home.Items("hot_list")), ["banner"] = banners },
            ["rank"] = new JsonObject { ["rank_list"] = new JsonArray(rank.Items("rank_list").Select(g => (JsonNode)new JsonObject { ["name"] = g.Text("name"), ["vods"] = Rows(g.Items("vods")) }).ToArray()) },
            ["week"] = new JsonObject { ["week_list"] = days },
            ["catalog"] = new JsonArray(normalized.Values.Select(v => v.DeepClone()).ToArray()), ["details"] = details
        };
        var json = snapshot.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        await File.WriteAllTextAsync(Path.Combine(output, "snapshot.json"), json);
        // Remove only this exporter's obsolete images inside the dedicated output directory.
        var keep = images.Values.Select(value => Path.GetFileName(value)).ToHashSet();
        foreach (var file in Directory.EnumerateFiles(output, "image-*.webp"))
            if (!keep.Contains(Path.GetFileName(file))) File.Delete(file);
        await File.WriteAllTextAsync(Path.Combine(staging, "image-report.json"), JsonSerializer.Serialize(new { downloaded = images.Count - missingImages.Count, missingImages }));
        Console.WriteLine($"Snapshot saved: {normalized.Count} titles, {images.Count - missingImages.Count} images, {missingImages.Count} placeholders.");
    }

    private static JsonObject Pick(JsonNode source, params string[] fields)
    {
        var result = new JsonObject();
        foreach (var key in fields) if (source[key] is { } value) result[key] = value.DeepClone();
        return result;
    }
}
