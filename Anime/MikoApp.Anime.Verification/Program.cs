using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using MikoApp.Anime.Services;
using MikoApp.Anime.Verification;

if (args.Contains("--navigation")) { NavigationVerification.Run(); return; }
if (args.Contains("--refresh-demo")) { await DemoSnapshotCapture.RunAsync(); return; }
if (args.Contains("--demo-mode")) { DemoModeVerification.Run(); return; }
if (args.Contains("--safe-area")) { SafeAreaVerification.Run(); return; }
if (Array.IndexOf(args, "--playback-source") is var playbackIndex && playbackIndex >= 0)
{
    if (playbackIndex + 1 >= args.Length) throw new ArgumentException("--playback-source requires a text file containing the media URL or local path.");
    Environment.ExitCode = PlaybackVerification.Run(File.ReadAllText(args[playbackIndex + 1]).Trim());
    return;
}

if (args.Contains("--live-images"))
{
    LiveImageVerification.Run(args.Contains("--allow-unavailable-images"));
    return;
}

if (args.Contains("--live-ui"))
{
    Environment.SetEnvironmentVariable("ANIME_OFFLINE", "0");
    using var app = new AppHarness("Anime/artifacts/live-ui");
    app.Navigate("/home");
    for (var attempt = 0; attempt < 60 && !app.Service<AnimeStore>().Initialized; attempt++) app.Pump(100);
    app.Pump(100); app.Capture("home");
    var store = app.Service<AnimeStore>();
    Require(!store.IsPreview, "Live UI loaded remote catalog");
    app.Navigate($"/detail/{store.Hot.First().Id}");
    app.Pump(1000); app.Capture("detail");
    if (app.Find("captcha-modal").Any(e => !e.HasClass("overlay-hidden"))) app.ClickText("ion-button", "关闭");
    app.ClickText("ion-segment-button", "评论"); app.Pump(300); app.Capture("comments");
    Console.WriteLine("Live native UI captured with read-only remote data.");
    return;
}

if (args.Contains("--live"))
{
    using var client = new LanercClient();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(110));
    var ct = timeout.Token;
    await client.DiscoverAsync(ct);
    Console.WriteLine($"Discovery healthy: {client.BaseAddress?.Host}");
    var home = await client.HomeAsync(ct);
    var section = home.Items("vod_list").First();
    var title = section.Items("vods").First();
    var id = title.Int("id");
    Console.WriteLine($"Home: {home.Items("banner").Count()} banners / {home.Items("vod_list").Count()} categories");
    var config = await client.ConfigAsync(ct);
    Console.WriteLine($"Config: guest={config["app_config"].Flag("guest_credential_enabled")}");
    var filter = await client.FilterAsync(section.Int("sort_id"), 1, "", "", false, ct);
    Console.WriteLine($"Filter: {filter.Items("filter_vods").Count()} titles");
    var search = await client.SearchAsync(title.Text("vod_name"), ct);
    Console.WriteLine($"Search: {search.Items("search_vods").Count()} matches");
    Console.WriteLine($"Rank: {(await client.RankAsync(ct)).Items("rank_list").Count()} groups");
    Console.WriteLine($"Week: {(await client.WeekAsync(ct))["week_list"] is JsonObject}");
    var detail = await client.DetailAsync(id, ct);
    Console.WriteLine($"Detail: {detail.Items("video_play_list").Count()} lines / {detail.Items("video_play_list").FirstOrDefault().Items("video").Count()} episodes");
    Console.WriteLine($"Comments: {(await client.CommentsAsync(id, 1, ct))["vod_comment"].Int("total")}");
    Console.WriteLine($"Danmaku response: {(await client.BarragesAsync(id, 1, ct)) is JsonObject}");
    return;
}

await ImageCacheVerification.RunAsync();
Console.WriteLine("Image verification passed (Douban headers, persistent cache, failed-response retry).");
if (args.Contains("--images")) return;

Require(LanercProtocol.Sign("/app/config", 1791612124, "wbjeru") == "1791612124-wbjeru-1a55bf2c78904d7323acca62bcad64d6", "APK signature vector");
Require(LanercProtocol.DecryptTxt("\"1n4aoOeW5/QPKutimGgc78mX2D6i2yUmM2KeDK5jnRY=\"") == "https://v3.yyghk.com/", "APK TXT vector");
Require(LanercProtocol.Md5("password") == "5f4dcc3b5aa765d61d8327deb882cf99", "Password MD5");
using (var aes = Aes.Create())
{
    aes.Key = Encoding.UTF8.GetBytes("8f81c2519e3b661834219e7142000093");
    var encoded = Convert.ToBase64String(aes.EncryptEcb(Encoding.UTF8.GetBytes("{\"ok\":true}"), PaddingMode.PKCS7));
    encoded = string.Concat(encoded.Select(c => c switch { '9' => '1', '1' => '5', '5' => '9', '+' => '/', '/' => '-', _ => c }));
    Require(LanercProtocol.DecodeJson(new JsonObject { ["code"] = 201, ["data"] = encoded }).Flag("ok"), "Business ECB substitution");
    Require(LanercProtocol.DecodeJson(new JsonObject { ["code"] = 200, ["data"] = new JsonObject { ["ok"] = true } }).Flag("ok"), "Plain 200 is not decrypted");
}
var url = "https://example.invalid/episode.m3u8";
var inner = new byte[] { 10, (byte)Encoding.UTF8.GetByteCount(url) }.Concat(Encoding.UTF8.GetBytes(url)).ToArray();
var outer = new byte[] { 8, 200, 1, 24, 0, 34, (byte)inner.Length }.Concat(inner).ToArray();
Require(LanercProtocol.DecodePlay(outer) == url, "Plain protobuf");
var nonce = RandomNumberGenerator.GetBytes(12); var cipher = new byte[inner.Length]; var tag = new byte[16];
using (var gcm = new AesGcm(Convert.FromHexString("5a31fe3201838a69e8f9c135f7905db25208fbc6bc3f0a9b017fc5139a451108"), 16)) gcm.Encrypt(nonce, inner, cipher, tag);
var encrypted = nonce.Concat(cipher).Concat(tag).ToArray();
var envelope = new byte[] { 8, 200, 1, 24, 1, 34, (byte)encrypted.Length }.Concat(encrypted).ToArray();
Require(LanercProtocol.DecodePlay(envelope) == url, "GCM protobuf");
envelope[^1] ^= 1;
try { LanercProtocol.DecodePlay(envelope); throw new Exception("Tampered GCM tag accepted"); } catch (CryptographicException) { }
try { LanercProtocol.DecodePlay([8, 200, 1, 34, 255]); throw new Exception("Truncation accepted"); } catch (FormatException) { }
Console.WriteLine("Protocol verification passed (signature, TXT, ECB, plaintext, protobuf, GCM authentication, truncation).");

var output = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? "Anime/artifacts/verification");
Environment.SetEnvironmentVariable("ANIME_OFFLINE", "1");
Environment.SetEnvironmentVariable("ANIME_DATA_DIR", Path.Combine(output, "state"));
using var harness = new AppHarness(output, ios: args.Contains("--ios"));
harness.Capture("01-splash");
harness.Navigate("/home"); harness.Capture("02-home");
harness.ClickText("ion-segment-button", "日漫"); harness.Capture("03-category");
harness.ClickText("ion-segment-button", "剧场版"); harness.Capture("04-movies");
harness.Navigate("/search"); harness.Capture("05-search-history");
var demoTitle = harness.Service<AnimeStore>().Hot.First();
harness.Enter("search-query", demoTitle.Title); harness.ClickClass("submit-search"); harness.Capture("06-search-results");
Require(harness.Text.Contains(demoTitle.Title), "Search results");
harness.Navigate("/trending"); harness.Capture("07-ranking");
harness.Navigate("/timetable"); harness.Capture("08-timetable");
harness.Navigate("/vip"); harness.Capture("09-welfare");
harness.Navigate("/user"); harness.Capture("10-guest-profile");
harness.Navigate($"/detail/{demoTitle.Id}"); harness.Pump(70); harness.Capture("11-detail");
harness.ClickClass("open-episodes"); harness.Capture("12-episodes"); harness.ClickClass("close-detail-panel");
harness.ClickClass("open-synopsis"); harness.Capture("13-synopsis"); harness.ClickClass("close-detail-panel");
harness.ClickText("ion-segment-button", "评论"); harness.Capture("14-comments");
harness.ClickClass("open-danmaku"); harness.Capture("15-danmaku");
harness.Enter("danmaku-input", "一起追番吧"); harness.ClickClass("send-danmaku");
harness.ClickText("ion-segment-button", "简介"); harness.ClickClass("enter-fullscreen"); harness.Resize(844,390); harness.Capture("16-fullscreen");
harness.ClickClass("fullscreen-danmaku"); harness.Capture("17-fullscreen-danmaku"); harness.ClickText("ion-button", "关闭"); harness.ClickClass("exit-fullscreen"); harness.Resize(390,844);
harness.Navigate("/history"); harness.Capture("18-history");
harness.Navigate("/downloads"); harness.Capture("19-downloads");
harness.Navigate("/favorites"); harness.Capture("20-guest-favorites");
harness.Navigate("/login"); harness.Capture("21-login");
harness.ClickClass("auth-submit"); Require(harness.Text.Contains("邮箱"), "Login validation");
harness.Navigate("/register"); harness.Capture("22-register");
harness.Navigate("/forgot"); harness.Capture("23-forgot");
harness.Navigate("/login"); harness.Enter("auth-account", "viewer@example.invalid"); harness.Enter("auth-password", "fixture-password"); harness.ClickClass("auth-submit"); harness.Pump(30);
Require(harness.Route == "/user", "Login routes to account page"); harness.Capture("24-signed-in");
harness.Navigate("/profile"); harness.Capture("25-profile");
harness.Navigate("/favorites"); harness.Capture("26-favorites");
harness.Navigate("/register"); harness.Resize(360,740); harness.Capture("28-small-register");
harness.Navigate("/home"); harness.Resize(1024,768); harness.Capture("27-desktop");
Console.WriteLine($"Native UI verification: {harness.Captures} captures in {output}");

static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
