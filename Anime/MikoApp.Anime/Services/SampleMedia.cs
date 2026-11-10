namespace MikoApp.Anime.Services;

public static class SampleMedia
{
    private static readonly Lazy<string> FilePath = new(() =>
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "miko-anime-sample-v1.mp4");
        using var source = typeof(App).Assembly.GetManifestResourceStream("MikoApp.Anime.Assets.sample.mp4")!;
        if (!File.Exists(path) || new FileInfo(path).Length != source.Length)
        { using var file = File.Create(path); source.CopyTo(file); }
        return path;
    });
    public static string Path => FilePath.Value;
}
