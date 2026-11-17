using System.Diagnostics;
using Miko.Components.Player;
using Miko.Common;
using Miko.Core.DomElements;
using Miko.Graphics;
using Miko.Hosting;
using Miko.Platform.Video;
using Miko.Styling;
using Miko.Windowing.Video;

namespace MikoApp.Anime.Verification;

internal static class PlaybackVerification
{
    public static int Run(string source)
    {
        var builder = MikoAppBuilder.CreateDefault();
        builder.Services.AddMikoPlayer();
        builder.UseSystemVideo();
        var context = builder.Build();
        var engine = context.Engine;
        using var player = new MikoPlayer { Engine = engine, Source = source, AutoPlay = true, Muted = true };
        var root = new DivElement { Style = new Style { Width = Length.Percent(100), Height = Length.Percent(100), BackgroundColor = Color.Black } };
        root.Children.Add(player.Build());
        using var surface = engine.Graphics.CreateSurface(960, 600);
        engine.Initialize(root, [], surface.Context, 960, 600);
        var video = root.FindByTagName("video").OfType<VideoElement>().Single();
        var timer = Stopwatch.StartNew();
        var frameCount = 0;
        while (timer.Elapsed < TimeSpan.FromSeconds(45))
        {
            engine.Render(surface.Context);
            if (video.Session?.FrameSource.AcquireCurrentFrame(GraphicsDevice.Software) is not null)
            {
                frameCount++;
                video.Session.FrameSource.ReleaseCurrentFrame();
            }
            if (video.Session?.State == VideoSessionState.Error || (frameCount > 5 && player.Controller.Position.TotalSeconds > 2)) break;
            Thread.Sleep(16);
        }
        Console.WriteLine($"state={player.Controller.State}, position={player.Controller.Position}, duration={player.Controller.Duration}, frames={frameCount}, error={player.Controller.Error}");
        Directory.CreateDirectory("Anime/artifacts/playback-fix");
        File.WriteAllBytes("Anime/artifacts/playback-fix/windows-playback.png", surface.EncodePng());
        var passed = frameCount > 5 && player.Controller.Position.TotalSeconds > 2 && player.Controller.Error is null;
        if (passed)
        {
            player.Pause();
            for (var i = 0; i < 30; i++) { engine.Render(surface.Context); Thread.Sleep(16); }
            AppHarness.Require(player.Controller.State == VideoSessionState.Paused, "Native playback pauses");
            player.Seek(TimeSpan.FromSeconds(10));
            player.Play();
            timer.Restart();
            while (timer.Elapsed < TimeSpan.FromSeconds(20) && player.Controller.Position.TotalSeconds < 10)
            { engine.Render(surface.Context); Thread.Sleep(16); }
            AppHarness.Require(player.Controller.Position.TotalSeconds >= 10 && player.Controller.Error is null, "Native playback seeks and resumes");
            Console.WriteLine("Native playback pause, seek and resume passed.");
        }
        engine.DisposeVideoSessions();
        (context.Services as IDisposable)?.Dispose();
        return passed ? 0 : 1;
    }
}
