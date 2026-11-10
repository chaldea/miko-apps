namespace MikoApp.FBox.Services;

/// <summary>Deployment settings. Credentials and production endpoints are never bundled.</summary>
public sealed class FBoxOptions
{
    public string ApiUrl { get; set; } = Environment.GetEnvironmentVariable("FBOX_API_URL") ?? "";
    public string IdentityUrl { get; set; } = Environment.GetEnvironmentVariable("FBOX_IDENTITY_URL") ?? "";
    public string ClientId { get; set; } = Environment.GetEnvironmentVariable("FBOX_CLIENT_ID") ?? "";
    public string ClientSecret { get; set; } = Environment.GetEnvironmentVariable("FBOX_CLIENT_SECRET") ?? "";
    public string SignalRVersion { get; set; } = Environment.GetEnvironmentVariable("FBOX_SIGNALR_VERSION") ?? "core";
    public string DataDirectory { get; set; } = Environment.GetEnvironmentVariable("FBOX_DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MikoFBox");
    public bool Demo { get; set; } = Environment.GetEnvironmentVariable("FBOX_DEMO") != "0";
}
