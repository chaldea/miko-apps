using System.Text.Json;

namespace MikoApp.Anime.Services;

public sealed class AnimeSettings
{
    public const string DefaultServerAddress = "http://uminainfo.cc/api.json";
    public static string DataDirectory => Environment.GetEnvironmentVariable("ANIME_DATA_DIR") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MikoAnime");
    private readonly string _path = Path.Combine(DataDirectory, "settings.json");
    public bool DemoMode { get; private set; } = true;
    public string ServerAddress { get; private set; } = DefaultServerAddress;
    public string? StorageError { get; private set; }

    public AnimeSettings()
    {
        try
        {
            if (File.Exists(_path) && JsonSerializer.Deserialize<SavedSettings>(File.ReadAllText(_path)) is { } saved)
            {
                ServerAddress = ValidateServer(saved.ServerAddress);
                DemoMode = saved.DemoMode;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or LanercException)
        { StorageError = "设置无法读取，已使用默认演示模式。"; }
        if (Environment.GetEnvironmentVariable("ANIME_OFFLINE") is { } value && value is "0" or "1") DemoMode = value == "1";
    }

    public void Save(bool demoMode, string? address)
    {
        var server = ValidateServer(address);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(new SavedSettings(demoMode, server)));
            File.Move(_path + ".tmp", _path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new LanercException("设置保存失败，请检查本机存储后重试。"); }
        DemoMode = demoMode; ServerAddress = server; StorageError = null;
    }

    public static string ValidateServer(string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            throw new LanercException("请输入有效的 HTTP 或 HTTPS 服务器地址。");
        if (!uri.AbsolutePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && (uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query)))
            throw new LanercException("请填写服务发现 JSON 地址，或 API 根地址（如 https://example.com）。");
        return uri.AbsoluteUri;
    }
    private sealed record SavedSettings(bool DemoMode, string ServerAddress);
}
