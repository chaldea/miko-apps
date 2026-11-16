using System.Text.Json;

namespace MikoApp.FBox.Services;

/// <summary>Deployment settings. Credentials and production endpoints are never bundled.</summary>
public sealed class FBoxOptions
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public FBoxOptions(string? dataDirectory = null)
    {
        DataDirectory = dataDirectory ?? Environment.GetEnvironmentVariable("FBOX_DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MikoFBox");
        var saved = LoadSaved();
        ApiUrl = Environment.GetEnvironmentVariable("FBOX_API_URL") ?? saved?.ApiUrl ?? "";
        IdentityUrl = Environment.GetEnvironmentVariable("FBOX_IDENTITY_URL") ?? saved?.IdentityUrl ?? "";
        ClientId = Environment.GetEnvironmentVariable("FBOX_CLIENT_ID") ?? saved?.ClientId ?? "";
        ClientSecret = Environment.GetEnvironmentVariable("FBOX_CLIENT_SECRET") ?? "";
        SignalRVersion = Environment.GetEnvironmentVariable("FBOX_SIGNALR_VERSION") ?? saved?.SignalRVersion ?? "core";
        Demo = Environment.GetEnvironmentVariable("FBOX_DEMO") is { } demo ? demo != "0" : saved?.Demo ?? true;
    }

    public string ApiUrl { get; set; }
    public string IdentityUrl { get; set; }
    public string ClientId { get; set; }
    public string ClientSecret { get; set; }
    public string SignalRVersion { get; set; }
    public string DataDirectory { get; }
    public bool Demo { get; set; }

    public void Save()
    {
        Directory.CreateDirectory(DataDirectory);
        var path = Path.Combine(DataDirectory, "app-settings.json");
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new SavedSettings(Demo, ApiUrl, IdentityUrl, ClientId, SignalRVersion), JsonOptions));
        File.Move(temp, path, true);
    }

    private SavedSettings? LoadSaved()
    {
        try
        {
            var path = Path.Combine(DataDirectory, "app-settings.json");
            return File.Exists(path) ? JsonSerializer.Deserialize<SavedSettings>(File.ReadAllText(path)) : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private sealed record SavedSettings(bool? Demo, string? ApiUrl, string? IdentityUrl, string? ClientId, string? SignalRVersion);
}
