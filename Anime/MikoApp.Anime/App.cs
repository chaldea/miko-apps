using Miko.Hosting;
using Miko.Graphics;
using Miko.Ionic;
using Miko.Components.Player;
using MikoApp.Anime.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MikoApp.Anime;

/// <summary>
/// Shared application configuration. Each platform startup project
/// (Desktop / Android / iOS) consumes the <see cref="MikoAppContext"/>
/// returned here and drives its own render loop.
/// </summary>
public static class App
{
    /// <summary>
    /// Builds an app context with the shared configuration.
    /// </summary>
    /// <param name="configure">
    /// Optional hook for a platform head to add head-specific services before the
    /// context is built — e.g. the simulator head enabling the MCP debug server via
    /// <c>builder.AddMikoMcpServer()</c>. Kept as a callback so the shared app assembly
    /// does not reference head-only packages (Miko.McpServer / Miko.Simulator).
    /// </param>
    public static MikoAppContext CreateContext(Action<MikoAppBuilder>? configure = null)
    {
        var builder = MikoAppBuilder.CreateDefault();

        builder.UseTitle("Anime · 追番时光");
        // Phone-portrait viewport showcases the Ionic mobile layout.
        builder.UseSize(390, 844);

        builder.AddIonic(options => options.Theme = AppTheme.Create());
        builder.Services.AddMikoPlayer();
        var dataDirectory = Environment.GetEnvironmentVariable("ANIME_DATA_DIR") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MikoAnime");
        var imageCacheDirectory = Environment.GetEnvironmentVariable("ANIME_IMAGE_CACHE_DIR") ??
            Path.Combine(dataDirectory, "images");
        builder.Services.AddSingleton(sp => new AnimeImageHandler(imageCacheDirectory, sp.GetRequiredService<IGraphicsFactory>()));
        builder.Services.AddSingleton(sp => new HttpClient(sp.GetRequiredService<AnimeImageHandler>(), disposeHandler: false)
        {
            Timeout = TimeSpan.FromSeconds(30),
        });
        builder.Services.AddSingleton<LanercClient>();
        builder.Services.AddSingleton<AnimeStore>();
        builder.Services.AddSingleton<AnimeNavigator>();
        builder.AddResourceAssembly(typeof(App).Assembly);
        builder.AddStyleSheet(GlobalStyles.Create());

        // Routes and the default layout are wired up by Miko.Razor.Compiler.
        builder.UseGeneratedRoutes();
        builder.UseDefaultLayout<MainLayout>();

        builder.EnableHotReload();

        configure?.Invoke(builder);

        return builder.Build();
    }

    /// <summary>
    /// Wires up hot reload. The hot-reload handler is generated (by
    /// Miko.Razor.Compiler) into this shared assembly because that is where
    /// EnableHotReload() is called, so it is initialized from here. Called by
    /// the desktop startup, which is where hot reload is supported.
    /// </summary>
    public static void InitializeHotReload(MikoAppContext context)
    {
        MikoHotReloadHandler.Initialize(context.GetHotReloadService());
    }
}
