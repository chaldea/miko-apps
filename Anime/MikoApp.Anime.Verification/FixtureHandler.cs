using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using MikoApp.Anime.Services;

namespace MikoApp.Anime.Verification;

/// <summary>Isolated transport fixture. Never connects to a service or submits account changes.</summary>
internal sealed class FixtureHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path == "/api.json") return Json(new JsonObject { ["domain"] = "https://fixture.invalid/" });
        var query = request.RequestUri.Query.TrimStart('?').Split('&').Select(v => v.Split('=', 2)).ToDictionary(p => p[0], p => p.Length > 1 ? Uri.UnescapeDataString(p[1]) : "");
        if (!query.TryGetValue("sign", out var sign)) throw new InvalidOperationException("Missing signed query");
        var parts = sign.Split('-');
        if (parts.Length != 3 || sign != LanercProtocol.Sign(path, long.Parse(parts[0]), parts[1]) || parts[1].Length != 6) throw new InvalidOperationException("Invalid request signature");
        if (path == "/app/health") return Json(new JsonObject { ["status"] = true });
        if (path == "/app/user/login")
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
            if (body.Text("pwd") != LanercProtocol.Md5("fixture-password")) throw new InvalidOperationException("Password was not hashed");
            return Success(new JsonObject { ["token"] = "fixture-token", ["user"] = new JsonObject { ["nick_name"] = "追番同学", ["account"] = "viewer@example.invalid", ["email"] = true, ["avatar"] = "res://Assets/avatar.jpg", ["group"] = "普通用户", ["like_list"] = new JsonArray(1, 2) } });
        }
        if (path == "/app/user/like")
        {
            if (request.Headers.GetValues("Authorization").Single() != "fixture-token") throw new InvalidOperationException("Token must be raw");
            return Success(new JsonObject { ["like_list"] = new JsonArray(
                new JsonObject { ["id"] = 101, ["vod_name"] = "攻壳机动队 第二季", ["vod_pic"] = "res://Assets/thumbnail-1.jpg", ["vod_total"] = 26, ["vod_class"] = "科幻 · 战斗" },
                new JsonObject { ["id"] = 102, ["vod_name"] = "亚人", ["vod_pic"] = "res://Assets/thumbnail-2.jpg", ["vod_total"] = 13, ["vod_class"] = "悬疑 · 战斗" }) });
        }
        throw new InvalidOperationException($"Unexpected fixture request {path}");
    }
    private static HttpResponseMessage Success(JsonNode data) => Json(new JsonObject { ["code"] = 200, ["data"] = data });
    private static HttpResponseMessage Json(JsonNode data) => new(HttpStatusCode.OK) { Content = new StringContent(data.ToJsonString(), Encoding.UTF8, "application/json") };
}
