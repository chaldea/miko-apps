using System.Text.Json.Nodes;

namespace MikoApp.Anime.Services;

/// <summary>Bundled public catalog and local account interactions. Never opens a network connection.</summary>
public sealed class DemoCatalog
{
    private static readonly Lazy<JsonNode> Snapshot = new(() =>
    {
        using var stream = typeof(App).Assembly.GetManifestResourceStream("MikoApp.Anime.Assets.Demo.snapshot.json")
            ?? throw new InvalidOperationException("Missing demo snapshot.");
        return JsonNode.Parse(stream)!;
    });
    private JsonObject? _user;
    private readonly Dictionary<int, List<JsonNode>> _comments = [];
    public static string CapturedAt => Snapshot.Value.Text("captured_at");
    public static IEnumerable<JsonNode> Titles => Snapshot.Value.Items("catalog");
    public void SignOut() => _user = null;
    public void Reset() { _user = null; _comments.Clear(); }

    public JsonNode Request(string path, JsonObject? body)
    {
        var uri = new Uri(new Uri("https://demo.invalid"), path);
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Split('=', 2))
            .ToDictionary(x => x[0], x => x.Length == 2 ? Uri.UnescapeDataString(x[1]) : "");
        string Q(string key) => query.GetValueOrDefault(key, "");
        int N(string key, int fallback = 0) => int.TryParse(Q(key), out var n) ? n : fallback;
        JsonArray Rows(IEnumerable<JsonNode> rows) => new(rows.Select(r => r.DeepClone()).ToArray());
        switch (uri.AbsolutePath)
        {
            case "/app/home": return Snapshot.Value["home"]!.DeepClone();
            case "/app/rank": return Snapshot.Value["rank"]!.DeepClone();
            case "/app/week": return Snapshot.Value["week"]!.DeepClone();
            case "/app/config": return new JsonObject { ["app_config"] = new JsonObject { ["guest_credential_enabled"] = false } };
            case "/app/vod/filter":
                var rows = Titles.Where(t => t.Int("demo_class_id") == N("class_id") &&
                    (Q("vod_class").Length == 0 || t.Text("vod_class").Contains(Q("vod_class"))) &&
                    (Q("year").Length == 0 || t.Text("vod_year") == Q("year")));
                rows = Q("sort_by") == "vod_score" ? rows.OrderByDescending(t => t.Number("vod_score")) : rows.OrderByDescending(t => t.Int("vod_year"));
                return new JsonObject { ["filter_vods"] = Rows(rows.Skip((Math.Max(1, N("page", 1)) - 1) * 18).Take(18)) };
            case "/app/vod/search": return new JsonObject { ["search_vods"] = Rows(Titles.Where(t => t.Text("vod_name").Contains(Q("keyword"), StringComparison.OrdinalIgnoreCase))) };
            case "/app/user/login":
            case "/app/v2/user/register":
                _user = new JsonObject { ["nick_name"] = body.Text("nick_name", "演示用户"), ["account"] = "viewer@example.invalid", ["email"] = true,
                    ["avatar"] = "res://Assets/avatar.jpg", ["qq_number"] = "", ["group"] = "演示账号", ["like_list"] = new JsonArray() };
                return Session();
            case "/app/user/info":
                RequireUser();
                if (body is not null)
                    foreach (var field in new[] { "nick_name", "qq_number" }) if (body[field] is { } value) _user![field] = value.DeepClone();
                return Session();
            case "/app/v2/user/register/code":
            case "/app/user/forgotcode":
                return new JsonObject { ["code_mask"] = "local-demo-code", ["expires_in"] = 300 };
            case "/app/user/forgotpwd":
                if (body.Text("code") != "123456") throw new LanercException("演示验证码为 123456。");
                return new JsonObject();
            case "/app/user/like":
                RequireUser();
                var ids = _user.Items("like_list").Select(x => int.Parse(x.ToString())).ToHashSet();
                return new JsonObject { ["like_list"] = Rows(Titles.Where(t => ids.Contains(t.Int("id")))) };
            case "/app/user/uplike":
                RequireUser(); _user!["like_list"] = body?["vids"]?.DeepClone() ?? new JsonArray(); return new JsonObject();
            case "/app/chat/comment":
                var comments = _comments.GetValueOrDefault(N("vid")) ?? [];
                return new JsonObject { ["vod_comment"] = new JsonObject { ["total"] = comments.Count, ["comment"] = Rows(comments.Skip((Math.Max(1, N("page", 1)) - 1) * 20).Take(20)) } };
            case "/app/chat/sendcomment":
                RequireUser();
                var id = body.Int("vod_id");
                if (!_comments.TryGetValue(id, out var list)) _comments[id] = list = [];
                list.Insert(0, new JsonObject { ["cid"] = list.Count + 1, ["nick_name"] = _user.Text("nick_name"), ["avatar"] = "res://Assets/avatar.jpg", ["created_at"] = "本地演示", ["content"] = body.Text("content") });
                return new JsonObject();
            case "/app/chat/barragev2": return new JsonObject { ["new_barrages"] = new JsonArray(), ["old_barrages"] = new JsonArray() };
            default:
                if (uri.AbsolutePath.StartsWith("/app/v2/vod/"))
                    return Snapshot.Value["details"]?[uri.Segments[^1]]?.DeepClone() ?? throw new LanercException("演示数据中暂无此番剧。");
                throw new LanercException("演示模式不连接服务器。请在系统设置中关闭演示模式后使用此功能。");
        }
    }
    private void RequireUser() { if (_user is null) throw new LanercException("请先登录演示账号。"); }
    private JsonObject Session() { RequireUser(); return new() { ["token"] = "local-demo-session", ["user"] = _user!.DeepClone() }; }
}
