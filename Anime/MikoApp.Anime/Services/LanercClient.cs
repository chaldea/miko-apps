using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace MikoApp.Anime.Services;

/// <summary>Asynchronous Lanerc client. All documented operations are available; writes run only on explicit UI actions.</summary>
public sealed class LanercClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _discovery = new(1);
    public Uri? BaseAddress { get; private set; }
    public string? Token { get; private set; }
    public LanercClient() : this(new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All }) { }
    public LanercClient(HttpMessageHandler handler) => _http = new(handler) { Timeout = TimeSpan.FromSeconds(20) };
    public void SignOut() => Token = null;

    public async Task DiscoverAsync(CancellationToken cancellationToken = default)
    {
        await _discovery.WaitAsync(cancellationToken);
        try
        {
            if (BaseAddress is not null) return;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(55));
            var ct = deadline.Token;
            foreach (var source in new[] { "Ahttp://uminainfo.cc/api.json", "https://anime999x-1366475786.cos.ap-guangzhou.myqcloud.com/apis.json" })
            {
                try
                {
                    var address = source;
                    if (source.StartsWith('A'))
                    {
                        var uri = new UriBuilder(source[1..]);
                        uri.Host = LanercProtocol.Nonce(System.Security.Cryptography.RandomNumberGenerator.GetInt32(4, 8), "abcdefghijklmnopqrstuvwxyz0123456789") + "." + uri.Host;
                        address = uri.Uri.AbsoluteUri;
                    }
                    var data = await DiscoveryJsonAsync(address, ct);
                    if (await AcceptBaseAsync(data.Text("domain"), ct)) return;
                }
                catch (Exception ex) when (IsDiscoveryFailure(ex, ct)) { }
            }
            foreach (var resolver in new[] { "https://doh.pub/", "https://dns.alidns.com/", "https://doh.360.cn/", "https://1.12.12.12/", "https://223.5.5.5/", "https://dns.google/" })
            foreach (var name in new[] { "dohx.lanerc.moe", "doh.lanercdns1.cc", "doh.uminadns2.cc" })
            {
                try
                {
                    var data = await DiscoveryJsonAsync($"{resolver}resolve?name={name}&type=txt", ct);
                    foreach (var answer in data.Items("Answer").Where(a => a.Int("type") == 16))
                        if (await AcceptBaseAsync(LanercProtocol.DecryptTxt(answer.Text("data")), ct)) return;
                }
                catch (Exception ex) when (IsDiscoveryFailure(ex, ct)) { }
            }
            throw new LanercException("暂时无法连接服务，请检查网络后重试。");
        }
        finally { _discovery.Release(); }
    }
    private static bool IsDiscoveryFailure(Exception ex, CancellationToken ct) => !ct.IsCancellationRequested &&
        ex is HttpRequestException or TaskCanceledException or FormatException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException or LanercException;

    private async Task<JsonNode> DiscoveryJsonAsync(string url, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        using var response = await _http.GetAsync(url, timeout.Token);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(timeout.Token)) ?? new JsonObject();
    }
    private async Task<bool> AcceptBaseAsync(string value, CancellationToken ct)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)) return false;
        var health = new Uri(uri, "/app/health");
        var sign = LanercProtocol.Sign(health.AbsolutePath, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), LanercProtocol.Nonce());
        var data = await DiscoveryJsonAsync(health + "?sign=" + sign, ct);
        if (!data.Flag("status")) return false;
        BaseAddress = new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
        return true;
    }

    private async Task<HttpResponseMessage> SendAsync(string path, JsonObject? body, bool auth, bool protobuf, CancellationToken ct)
    {
        await DiscoverAsync(ct);
        var uri = new Uri(BaseAddress!, path);
        var sign = LanercProtocol.Sign(uri.AbsolutePath, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), LanercProtocol.Nonce());
        using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post,
            uri + (string.IsNullOrEmpty(uri.Query) ? "?" : "&") + "sign=" + sign);
        if (auth) request.Headers.TryAddWithoutValidation("Authorization", Token ?? "");
        if (body is not null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        if (protobuf)
        {
            request.Headers.TryAddWithoutValidation("Accept", "application/x-protobuf");
            request.Headers.TryAddWithoutValidation("User-Agent", "Dart/3.9");
        }
        return await _http.SendAsync(request, ct);
    }
    private static long? RetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("x-retry-after-ms", out var values) && long.TryParse(values.FirstOrDefault(), out var ms)) return ms;
        if (response.Headers.RetryAfter?.Delta is { } delta) return (long)delta.TotalMilliseconds;
        if (response.Headers.RetryAfter?.Date is { } date) return Math.Max(0, (long)(date - DateTimeOffset.UtcNow).TotalMilliseconds);
        return null;
    }
    public async Task<JsonNode> RequestAsync(string path, JsonObject? body = null, bool auth = false, CancellationToken ct = default)
    {
        using var response = await SendAsync(path, body, auth, false, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);
        JsonNode? json;
        try { json = JsonNode.Parse(raw); }
        catch (System.Text.Json.JsonException) { throw new LanercException("服务器返回了无法识别的响应", (int)response.StatusCode, retryAfterMs: RetryAfter(response)); }
        if (!response.IsSuccessStatusCode) throw new LanercException(json.Text("msg", $"网络请求失败（{(int)response.StatusCode}）"), (int)response.StatusCode, json.Int("code"), RetryAfter(response));
        try { return LanercProtocol.DecodeJson(json ?? new JsonObject(), (int)response.StatusCode); }
        catch (LanercException ex) { throw new LanercException(ex.Message, ex.Status, ex.Code, RetryAfter(response)); }
    }
    private async Task<string> PlayAsync(string path, JsonObject body, bool auth, CancellationToken ct)
    {
        using var response = await SendAsync(path, body, auth, true, ct);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new LanercException($"播放请求失败（{(int)response.StatusCode}）", (int)response.StatusCode, retryAfterMs: RetryAfter(response));
        if (response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
        {
            var json = JsonNode.Parse(bytes);
            throw new LanercException(json.Text("msg", "请登录后播放"), (int)response.StatusCode, json.Int("code"), RetryAfter(response));
        }
        return LanercProtocol.DecodePlay(bytes);
    }
    private async Task<JsonNode> SessionAsync(string path, JsonObject? body, bool auth, CancellationToken ct)
    {
        var data = await RequestAsync(path, body, auth, ct);
        var token = data.Text("token");
        if (string.IsNullOrWhiteSpace(token)) throw new LanercException("登录响应缺少凭证，请重试。");
        Token = token;
        return data;
    }
    private static string Q(string s) => Uri.EscapeDataString(s);
    public Task<JsonNode> ConfigAsync(CancellationToken ct = default) => RequestAsync("/app/config?platform=android", ct: ct);
    public Task<JsonNode> HomeAsync(CancellationToken ct = default) => RequestAsync("/app/home", ct: ct);
    public Task<JsonNode> FilterAsync(int classId, int page, string genre, string year, bool byScore, CancellationToken ct = default) => RequestAsync($"/app/vod/filter?page={page}&class_id={classId}&vod_class={Q(genre)}&year={Q(year)}&sort_by={(byScore ? "vod_score" : "")}", ct: ct);
    public Task<JsonNode> SearchAsync(string keyword, CancellationToken ct = default) => RequestAsync("/app/vod/search?keyword=" + Q(keyword), ct: ct);
    public Task<JsonNode> RankAsync(CancellationToken ct = default) => RequestAsync("/app/rank", ct: ct);
    public Task<JsonNode> WeekAsync(CancellationToken ct = default) => RequestAsync("/app/week", ct: ct);
    public Task<JsonNode> DetailAsync(int id, CancellationToken ct = default) => RequestAsync($"/app/v2/vod/{id}", ct: ct);
    public Task<string> ResolveAsync(string resourceId, CancellationToken ct = default) => PlayAsync("/app/v2/play", new() { ["resource_id"] = resourceId }, !string.IsNullOrEmpty(Token), ct);
    public Task<string> ResolveGuestAsync(string resourceId, string credential, CancellationToken ct = default) => PlayAsync("/app/v2/guest/play", new() { ["resource_id"] = resourceId, ["guest_credential"] = credential }, false, ct);
    public Task<string> ResolveLegacyAsync(string vid, string player, string sign, string auth = "com.clggjv.xcjfmd.ffo", CancellationToken ct = default) => PlayAsync("/app/proxyx4x", new() { ["vid"] = vid, ["player"] = player, ["sign"] = sign, ["auth"] = string.IsNullOrEmpty(auth) ? "com.clggjv.xcjfmd.ffo" : auth }, !string.IsNullOrEmpty(Token), ct);
    public Task<JsonNode> CaptchaAsync(CancellationToken ct = default) => RequestAsync("/app/v2/guest/captcha", new(), ct: ct);
    public Task<JsonNode> GuestCredentialAsync(string challenge, int selected, CancellationToken ct = default) => RequestAsync("/app/v2/guest/credential", new() { ["challenge_id"] = challenge, ["selected"] = selected.ToString() }, ct: ct);
    public Task<JsonNode> LoginAsync(string account, string password, CancellationToken ct = default) => SessionAsync("/app/user/login", new() { ["account"] = account, ["pwd"] = LanercProtocol.Md5(password) }, false, ct);
    public Task<JsonNode> RegisterCodeAsync(string account, CancellationToken ct = default) => RequestAsync("/app/v2/user/register/code", new() { ["account"] = account }, ct: ct);
    public Task<JsonNode> RegisterAsync(string nickname, string password, string qq, string mask, string code, CancellationToken ct = default) => SessionAsync("/app/v2/user/register", new() { ["nick_name"] = nickname, ["pwd"] = LanercProtocol.Md5(password), ["qq_number"] = qq, ["code_mask"] = mask, ["code"] = code }, false, ct);
    public Task<JsonNode> UserAsync(CancellationToken ct = default) => SessionAsync("/app/user/info", null, true, ct);
    public Task<JsonNode> UpdateProfileAsync(string nickname, string qq, CancellationToken ct = default) => RequestAsync("/app/user/info", new() { ["nick_name"] = nickname, ["qq_number"] = qq }, true, ct);
    public Task<JsonNode> UpdatePasswordAsync(string pwd, string rePwd, CancellationToken ct = default) => RequestAsync("/app/user/info", new() { ["pwd"] = LanercProtocol.Md5(pwd), ["re_pwd"] = LanercProtocol.Md5(rePwd) }, true, ct);
    public Task<JsonNode> MailCodeAsync(CancellationToken ct = default) => RequestAsync("/app/user/getcode", new() { ["type"] = 1 }, true, ct);
    public Task<JsonNode> BindMailAsync(string code, string mask, CancellationToken ct = default) => RequestAsync("/app/user/mail", new() { ["code"] = code, ["code_mask"] = mask }, true, ct);
    public Task<JsonNode> ForgotCodeAsync(string mail, CancellationToken ct = default) => RequestAsync("/app/user/forgotcode", new() { ["mail"] = mail }, true, ct);
    public Task<JsonNode> ForgotPasswordAsync(string code, string password, string mask, CancellationToken ct = default) => RequestAsync("/app/user/forgotpwd", new() { ["code"] = code, ["pwd"] = LanercProtocol.Md5(password), ["code_mask"] = mask }, true, ct);
    public Task<JsonNode> QuestionsAsync(CancellationToken ct = default) => RequestAsync("/app/user/question", auth: true, ct: ct);
    public Task<JsonNode> AnswerAsync(string id, string answer, CancellationToken ct = default) => RequestAsync("/app/user/question", new() { ["question_id"] = id, ["answer"] = answer }, true, ct);
    public Task<JsonNode> LikesAsync(CancellationToken ct = default) => RequestAsync("/app/user/like", auth: true, ct: ct);
    public Task<JsonNode> SetLikesAsync(IEnumerable<int> ids, CancellationToken ct = default) => RequestAsync("/app/user/uplike", new() { ["vids"] = new JsonArray(ids.Distinct().Select(i => (JsonNode?)JsonValue.Create(i)).ToArray()) }, true, ct);
    public Task<JsonNode> SetGroupAsync(int gid, CancellationToken ct = default) => RequestAsync("/app/user/setgroup", new() { ["gid"] = gid }, true, ct);
    public Task<JsonNode> AddPointAsync(string card, CancellationToken ct = default) => RequestAsync("/app/user/addpoint", new() { ["card"] = card }, true, ct);
    public Task<JsonNode> WelfareAsync(int type, CancellationToken ct = default) => RequestAsync("/app/user/welfare", new() { ["type"] = type }, true, ct);
    public Task<JsonNode> CoinsAsync(CancellationToken ct = default) => RequestAsync("/app/user/coins", new(), true, ct);
    public Task<JsonNode> CommentsAsync(int vid, int page = 1, CancellationToken ct = default) => RequestAsync($"/app/chat/comment?vid={vid}&page={page}", ct: ct);
    public Task<JsonNode> SendCommentAsync(int id, string content, int parent = 0, CancellationToken ct = default) => RequestAsync("/app/chat/sendcomment", new() { ["content"] = content, ["vod_id"] = id, ["parent_id"] = parent }, true, ct);
    public Task<JsonNode> ReportCommentAsync(int cid, string title, CancellationToken ct = default) => RequestAsync("/app/report/comment", new() { ["cid"] = cid, ["vodname"] = title }, true, ct);
    public Task<JsonNode> BarragesAsync(int vid, int episode, CancellationToken ct = default) => RequestAsync($"/app/chat/barragev2?vid={vid}&vod={episode}", ct: ct);
    // Color/position wire types are intentionally caller-supplied: their enums were not recovered.
    public Task<JsonNode> SendBarrageAsync(int id, int episode, int second, string content, JsonNode color, JsonNode position, CancellationToken ct = default) => RequestAsync("/app/chat/sendbarrage", new() { ["video_id"] = id, ["video_url"] = episode, ["second"] = second, ["content"] = content, ["color"] = color.DeepClone(), ["position"] = position.DeepClone() }, true, ct);
    public Task<JsonNode> ReportBarrageAsync(int bid, string title, CancellationToken ct = default) => RequestAsync("/app/report/barrage", new() { ["bid"] = bid, ["vodname"] = title }, true, ct);
    public void Dispose() { _http.Dispose(); _discovery.Dispose(); }
}
