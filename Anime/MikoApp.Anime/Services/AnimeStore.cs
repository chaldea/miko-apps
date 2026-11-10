using System.Text.Json.Nodes;
using MikoApp.Anime.Models;
using Miko.Components.Player.Danmaku;
using Miko.Common;

namespace MikoApp.Anime.Services;

/// <summary>Selected catalog/session with separate demo and server-local watch/search history.</summary>
public sealed class AnimeStore
{
    private readonly LanercClient _api;
    private readonly Dictionary<int, AnimeTitle> _titles = [];
    private readonly HashSet<int> _favorites = [];
    private readonly List<WatchRecord> _history = [];
    private string _statePath = "";
    public bool IsPreview => _api.DemoMode;
    public bool Initialized { get; private set; }
    public string? ConnectionError { get; private set; }
    public string? StorageError { get; private set; }
    public string? Notice { get; private set; }
    public bool GuestEnabled { get; private set; }
    public UserProfile Profile { get; private set; } = new("游客", "", "res://Assets/avatar.jpg", false);
    public string Qq { get; private set; } = "";
    public string Group { get; private set; } = "普通用户";
    public int Coins { get; private set; }
    public IReadOnlyList<AnimeCategory> Categories { get; private set; } = [];
    public IReadOnlyList<AnimeTitle> Hot { get; private set; } = [];
    public IReadOnlyList<FeaturedAnime> Banners { get; private set; } = [];
    public IReadOnlyList<AnimeTitle> Titles => _titles.Values.ToArray();
    public IReadOnlyList<AnimeTitle> Favorites => _favorites.Select(Find).OfType<AnimeTitle>().ToArray();
    public IReadOnlyList<WatchRecord> History => _history.OrderByDescending(h => h.WatchedAt).ToArray();
    public List<string> SearchHistory { get; } = [];
    public event Action? Changed;

    public AnimeStore(LanercClient api)
    {
        _api = api;
        ResetCatalog();
    }
    public void ApplySettings(bool demoMode, string? server, AnimeSettings settings)
    {
        var normalized = AnimeSettings.ValidateServer(server);
        var changed = settings.DemoMode != demoMode || settings.ServerAddress != normalized;
        settings.Save(demoMode, normalized);
        if (!changed) return;
        _api.ResetConfiguration();
        ResetCatalog(); Changed?.Invoke();
    }
    private void ResetCatalog()
    {
        _titles.Clear(); _favorites.Clear(); _history.Clear(); SearchHistory.Clear();
        Categories = []; Hot = []; Banners = []; Notice = null; GuestEnabled = false;
        Initialized = false; ConnectionError = StorageError = null;
        Profile = new("游客", "", "res://Assets/avatar.jpg", false); Qq = ""; Group = "普通用户"; Coins = 0;
        var scope = IsPreview ? "demo" : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(_api.ServerAddress)))[..16];
        _statePath = Path.Combine(AnimeSettings.DataDirectory, "library-" + scope + ".json");
        if (IsPreview)
        {
            Cache(DemoCatalog.Titles.Select(t => ParseCurrentTitle(t)));
            ApplyHome(new DemoCatalog().Request("/app/home", null));
        }
        LoadLocal();
    }
    public Task InitializeAsync(CancellationToken ct = default) => Initialized ? Task.CompletedTask : RefreshAsync(ct);
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        ConnectionError = null;
        try
        {
            var home = await _api.HomeAsync(ct);
            ApplyHome(home);
            Initialized = true; Changed?.Invoke();
            try
            {
                var config = await _api.ConfigAsync(ct);
                GuestEnabled = config["app_config"].Flag("guest_credential_enabled");
                Notice = config["app_config"].Text("notice");
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { ConnectionError = "配置加载失败：" + Error(ex); }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { ConnectionError = Error(ex); Initialized = true; }
        Changed?.Invoke();
    }
    private void ApplyHome(JsonNode home)
    {
        Categories = home.Items("vod_list").Select(s => new AnimeCategory(s.Int("sort_id"), s.Text("sort_name"),
            Split(s.Text("type_class")), Split(s.Text("type_year")), s.Items("vods").Select(v => ParseCurrentTitle(v, s.Text("sort_name"))).ToArray())).ToArray();
        Hot = home.Items("hot_list").Select(v => ParseCurrentTitle(v)).ToArray();
        Banners = home.Items("banner").Select(v => new FeaturedAnime(v.Int("vod_id"), v.Text("image"))).ToArray();
        Cache(Categories.SelectMany(c => c.Titles).Concat(Hot));
    }
    private AnimeTitle ParseCurrentTitle(JsonNode value, string kind = "日漫") => ParseTitle(value, kind) with { Preview = IsPreview };
    private static string[] Split(string value) => ["全部", .. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    public AnimeTitle? Find(int id) => _titles.GetValueOrDefault(id);
    public void Cache(IEnumerable<AnimeTitle> items) { foreach (var item in items.Where(t => t.Id > 0)) _titles[item.Id] = item; }
    public static AnimeTitle ParseTitle(JsonNode v, string kind = "日漫") => new(v.Int("id"), v.Text("vod_name"), kind,
        v.Text("vod_class", v.Text("vod_tag")), v.Int("vod_year"), 0, 0, v.Int("vod_total"), v.Number("vod_score"),
        System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(v.Text("vod_blurb", v.Text("vod_sub")), "<[^>]+>", "")),
        v.Text("vod_pic", "res://Assets/poster-placeholder.svg"), "") { Author = v.Text("vod_author"), Remarks = v.Text("vod_remarks", v.Int("vod_isend") == 1 ? $"全 {v.Int("vod_total")} 集" : $"更新至 {v.Int("vod_total")} 集") };

    public async Task<IReadOnlyList<AnimeTitle>> FilterAsync(AnimeCategory category, int page, string genre, string year, bool rating, CancellationToken ct)
    {
        var data = await _api.FilterAsync(category.Id, page, genre == "全部" ? "" : genre, year == "全部" ? "" : year, rating, ct);
        var result = data.Items("filter_vods").Select(v => ParseCurrentTitle(v, category.Name)).ToArray();
        Cache(result); return result;
    }
    public async Task<IReadOnlyList<AnimeTitle>> SearchAsync(string keyword, CancellationToken ct)
    {
        keyword = keyword.Trim(); if (keyword.Length == 0) return [];
        SearchHistory.Remove(keyword); SearchHistory.Insert(0, keyword);
        if (SearchHistory.Count > 40) SearchHistory.RemoveRange(40, SearchHistory.Count - 40);
        SaveLocal();
        var data = await _api.SearchAsync(keyword, ct);
        var result = data.Items("search_vods").Select(v => ParseCurrentTitle(v)).ToArray();
        Cache(result); return result;
    }
    public void ClearSearch() { SearchHistory.Clear(); SaveLocal(); }
    public async Task<IReadOnlyList<RankGroup>> RankAsync(CancellationToken ct)
    {
        var data = await _api.RankAsync(ct);
        var groups = data.Items("rank_list").Select(g => new RankGroup(g.Text("name"), g.Items("vods").Select(v => ParseCurrentTitle(v)).ToArray())).ToArray();
        Cache(groups.SelectMany(g => g.Titles)); return groups;
    }
    public async Task<IReadOnlyList<IReadOnlyList<AnimeTitle>>> WeekAsync(CancellationToken ct)
    {
        var data = await _api.WeekAsync(ct);
        var days = new[] { "mon", "tue", "wed", "thu", "fri", "sat", "sun" }.Select(day => (IReadOnlyList<AnimeTitle>)data["week_list"].Items(day).Select(v => ParseCurrentTitle(v)).ToArray()).ToArray();
        Cache(days.SelectMany(d => d)); return days;
    }
    public async Task<AnimeDetail> DetailAsync(int id, CancellationToken ct)
    {
        var data = await _api.DetailAsync(id, ct);
        var title = ParseCurrentTitle(data["video_play_info"] ?? throw new FormatException("详情缺少视频信息"));
        Cache([title]);
        return new(title, data.Items("video_play_list").Select(line => new PlayLine(line.Text("player"), line.Text("name"), line.Items("video").Select(e => new Episode(e.Text("title"), e.Text("resource_id"), e.Flag("can_play_without_login"))).ToArray())).ToArray());
    }
    public async Task LoginAsync(string account, string password, CancellationToken ct) => ApplySession(await _api.LoginAsync(account, password, ct));
    public void ApplySession(JsonNode data)
    {
        var user = data["user"] ?? throw new FormatException("用户资料为空");
        Profile = new(user.Text("nick_name"), user.Flag("email") ? user.Text("account") : "未激活", user.Text("avatar", "res://Assets/avatar.jpg"), true);
        Qq = user.Text("qq_number"); Group = user.Text("group", "普通用户"); Coins = user.Int("coins");
        _favorites.Clear();
        foreach (var item in user.Items("like_list")) if (int.TryParse(item.ToString(), out var id)) _favorites.Add(id);
        Changed?.Invoke();
    }
    public void SignOut() { _api.SignOut(); Profile = new("游客", "", "res://Assets/avatar.jpg", false); Qq = ""; Coins = 0; _favorites.Clear(); Changed?.Invoke(); }
    public async Task UpdateProfileAsync(string nickname, string qq, CancellationToken ct)
    {
        RequireLogin(); await _api.UpdateProfileAsync(nickname, qq, ct);
        Profile = Profile with { Nickname = nickname }; Qq = qq; Changed?.Invoke();
    }
    public void RequireLogin() { if (!Profile.SignedIn) throw new LanercException("请先登录账号。"); }
    public bool IsFavorite(int id) => _favorites.Contains(id);
    public async Task LoadFavoritesAsync(CancellationToken ct)
    {
        RequireLogin(); var data = await _api.LikesAsync(ct);
        var titles = data.Items("like_list").Select(v => ParseCurrentTitle(v)).ToArray();
        Cache(titles); _favorites.Clear(); foreach (var title in titles) _favorites.Add(title.Id);
    }
    public async Task ToggleFavoriteAsync(int id, CancellationToken ct)
    {
        RequireLogin();
        await LoadFavoritesAsync(ct);
        var next = new HashSet<int>(_favorites); if (!next.Remove(id)) next.Add(id);
        await _api.SetLikesAsync(next, ct);
        _favorites.Clear(); _favorites.UnionWith(next); Changed?.Invoke();
    }
    public void RecordWatch(int id, int episode)
    {
        _history.RemoveAll(h => h.AnimeId == id); _history.Insert(0, new(id, episode, DateTimeOffset.Now));
        if (_history.Count > 100) _history.RemoveRange(100, _history.Count - 100);
        SaveLocal();
    }
    public void RemoveHistory(int id) { _history.RemoveAll(h => h.AnimeId == id); SaveLocal(); }
    public void ClearHistory() { _history.Clear(); SaveLocal(); }
    public async Task<IReadOnlyList<DanmakuItem>> DanmakuAsync(int id, int episode, CancellationToken ct)
    {
        if (Find(id)?.Preview == true) return [];
        var data = await _api.BarragesAsync(id, episode, ct);
        return data.Items("new_barrages").Concat(data.Items("old_barrages")).Select(b =>
        {
            var color = Color.White;
            try { var s = b.Text("bcolor"); if (s.StartsWith('#')) color = Color.FromHex(s); } catch (Exception) { }
            return new DanmakuItem(TimeSpan.FromSeconds(Math.Max(0, b.Number("second"))), b.Text("content"), DanmakuMode.Scroll, color);
        }).ToArray();
    }
    public static string Error(Exception ex) => ex switch
    {
        OperationCanceledException => "连接超时，请稍后重试。",
        HttpRequestException => "网络连接失败，请检查网络后重试。",
        LanercException e when e.RetryAfterMs is > 0 => $"{e.Message} 请在 {Math.Ceiling(e.RetryAfterMs.Value / 1000d)} 秒后重试。",
        LanercException => ex.Message,
        _ => "数据加载失败，请重试。"
    };
    private void LoadLocal()
    {
        try
        {
            var source = _statePath;
            if (!File.Exists(source) && !IsPreview && _api.ServerAddress == AnimeSettings.DefaultServerAddress)
                source = Path.Combine(AnimeSettings.DataDirectory, "library.json");
            if (!File.Exists(source)) return;
            var state = JsonNode.Parse(File.ReadAllText(source));
            foreach (var s in state.Items("searches")) SearchHistory.Add(s.ToString());
            foreach (var h in state.Items("history"))
            {
                if (h.Flag("preview") != IsPreview) continue;
                var title = ParseCurrentTitle(h["title"]!);
                if (IsPreview && !_titles.ContainsKey(title.Id)) continue;
                // Keep the richer bundled data for preview IDs.
                if (!title.Preview || !_titles.ContainsKey(title.Id)) Cache([title]);
                if (DateTimeOffset.TryParse(h.Text("watched_at"), out var time)) _history.Add(new(title.Id, h.Int("episode", 1), time));
            }
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException or InvalidOperationException)
        { StorageError = "本地历史记录无法读取。"; }
    }
    private void SaveLocal()
    {
        try
        {
            var rows = new JsonArray();
            foreach (var h in _history)
            {
                if (Find(h.AnimeId) is not { } t) continue;
                rows.Add(new JsonObject { ["episode"] = h.Episode, ["watched_at"] = h.WatchedAt.ToString("O"), ["preview"] = t.Preview,
                    ["title"] = new JsonObject { ["id"] = t.Id, ["vod_name"] = t.Title, ["vod_pic"] = t.Poster, ["vod_total"] = t.Episodes, ["vod_class"] = t.Genre, ["vod_remarks"] = t.Status, ["vod_blurb"] = t.Description, ["vod_year"] = t.Year, ["vod_score"] = t.Rating, ["vod_author"] = t.Author } });
            }
            var state = new JsonObject { ["searches"] = new JsonArray(SearchHistory.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()), ["history"] = rows };
            Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
            File.WriteAllText(_statePath + ".tmp", state.ToJsonString()); File.Move(_statePath + ".tmp", _statePath, true); StorageError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StorageError = "本地记录暂时无法保存。"; }
    }
}
