using Microsoft.Extensions.DependencyInjection;
using Miko.Graphics;
using Miko.Hosting;
using Miko.Native;
using Silk.NET.Core;
using Silk.NET.Windowing;

namespace MikoApp.Anime.Desktop;

internal static class DesktopIcon
{
    public static void Configure(MikoAppContext context)
    {
        // The native window is attached before it loads; set its icon once it is initialized.
        context.Services.GetRequiredService<INativeHostContext>().HostAttached += host =>
        {
            if (host is IWindow window && !OperatingSystem.IsMacOS())
                window.Load += () => SetIcon(window, context.Services.GetRequiredService<IGraphicsFactory>());
        };
    }

    private static void SetIcon(IWindow window, IGraphicsFactory graphics)
    {
        using var stream = typeof(DesktopIcon).Assembly.GetManifestResourceStream("Anime.AppIcon.png")
            ?? throw new InvalidOperationException("Missing Anime app icon.");
        using var image = graphics.DecodeImage(stream)
            ?? throw new InvalidOperationException("Invalid Anime app icon.");
        var pixels = new byte[image.Width * image.Height * 4];
        if (!image.ReadPixels(pixels, ImagePixelFormat.Rgba8888))
            throw new InvalidOperationException("Unable to read Anime app icon pixels.");
        window.SetWindowIcon([new RawImage(image.Width, image.Height, pixels)]);
    }
}
