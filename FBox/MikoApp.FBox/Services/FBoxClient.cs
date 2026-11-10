using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MikoApp.FBox.Models;

namespace MikoApp.FBox.Services;

/// <summary>FBox mobile REST contract; a 401 is retried once after a serialized token refresh.</summary>
public sealed class FBoxClient : IDisposable
{
    private readonly FBoxOptions _options;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private string? _refreshToken;
    public string? AccessToken { get; private set; }
    public string DeviceClientId { get; } = "PHONE_" + Guid.NewGuid().ToString("D");
    public bool Authenticated => !string.IsNullOrEmpty(AccessToken);

    public FBoxClient(FBoxOptions options) : this(options, new HttpClientHandler()) { }
    public FBoxClient(FBoxOptions options, HttpMessageHandler handler)
    {
        _options = options;
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };
    }

    public async Task LoginAsync(string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId)) throw new InvalidOperationException("请先设置服务地址和客户端 ID。");
        await TokenAsync(new() { ["username"] = username, ["password"] = password, ["grant_type"] = "password" }, ct);
    }

    private async Task TokenAsync(Dictionary<string, string> fields, CancellationToken ct)
    {
        fields["client_id"] = _options.ClientId;
        fields["client_secret"] = _options.ClientSecret;
        fields["scope"] = "openid offline_access fbox email profile";
        using var response = await _http.PostAsync(Endpoint("/core/connect/token", true), new FormUrlEncodedContent(fields), ct);
        if (!response.IsSuccessStatusCode) { Logout(); throw Error(response.StatusCode); }
        var data = await response.Content.ReadFromJsonAsync<JsonNode>(ct);
        AccessToken = data.Text("access_token");
        _refreshToken = data.Text("refresh_token");
        if (!Authenticated) throw new InvalidOperationException("登录服务没有返回有效令牌。");
    }

    public Task<JsonNode?> GetAsync(string path, bool identity = false, CancellationToken ct = default) => SendAsync(HttpMethod.Get, path, null, identity, ct);
    public Task<JsonNode?> PostAsync(string path, object? body = null, bool identity = false, CancellationToken ct = default) => SendAsync(HttpMethod.Post, path, body, identity, ct);
    public Task<JsonNode?> PutAsync(string path, object body, CancellationToken ct = default) => SendAsync(HttpMethod.Put, path, body, false, ct);
    public Task<JsonNode?> DeleteAsync(string path, CancellationToken ct = default) => SendAsync(HttpMethod.Delete, path, null, false, ct);

    public async Task<JsonNode?> SendAsync(HttpMethod method, string path, object? body, bool identity = false, CancellationToken ct = default)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var usedToken = AccessToken;
            using var request = new HttpRequestMessage(method, Endpoint(path, identity));
            request.Headers.Add("X-FBox-ClientId", DeviceClientId);
            if (Authenticated) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await _http.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0 && !string.IsNullOrEmpty(_refreshToken))
            {
                await _refresh.WaitAsync(ct);
                try
                {
                    if (AccessToken == usedToken)
                        await TokenAsync(new() { ["grant_type"] = "refresh_token", ["refresh_token"] = _refreshToken! }, ct);
                }
                finally { _refresh.Release(); }
                continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized) Logout();
                throw Error(response.StatusCode);
            }
            var text = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(text)) return null;
            try { return JsonNode.Parse(text); }
            catch (System.Text.Json.JsonException) { return JsonValue.Create(text); }
        }
        throw new InvalidOperationException("登录已过期，请重新登录。");
    }

    public Uri Endpoint(string path, bool identity = false)
    {
        var value = identity ? _options.IdentityUrl : _options.ApiUrl;
        if (!Uri.TryCreate(value.TrimEnd('/') + "/", UriKind.Absolute, out var root) || root.Scheme != "https")
            throw new InvalidOperationException("请在服务配置中填写有效的 HTTPS 地址。");
        return new Uri(root, path.TrimStart('/'));
    }

    private static Exception Error(HttpStatusCode status) => new InvalidOperationException(status switch
    {
        HttpStatusCode.Unauthorized => "登录已过期，请重新登录。",
        HttpStatusCode.Forbidden => "没有操作权限或连接密码错误。",
        HttpStatusCode.NotFound => "设备或请求的数据不存在。",
        HttpStatusCode.Conflict => "名称已存在或设备已经添加。",
        HttpStatusCode.BadRequest => "请求未通过验证，请检查填写内容。",
        _ => $"服务请求失败（{(int)status}），请稍后重试。"
    });

    public void Logout() { AccessToken = null; _refreshToken = null; }
    public void Dispose() { _http.Dispose(); _refresh.Dispose(); }
}
