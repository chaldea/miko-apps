using Microsoft.Extensions.DependencyInjection;
using Miko.Hosting;
using Miko.Ionic;
using Miko.Common;
using Miko.Native;
using MikoApp.FBox.Services;

namespace MikoApp.FBox;

public static class App
{
    public static MikoAppContext CreateContext(Action<MikoAppBuilder>? configure = null)
    {
        var builder = MikoAppBuilder.CreateDefault();
        builder.UseTitle("FBox · 设备管理");
        builder.UseSize(390, 844);
        builder.Services.AddMikoNative();
        builder.AddIonic(o => o.Theme = new()
        {
            Palette = new() { Primary = "#288ed0", BackgroundColor = "#f0f3f7", TextColor = "#303338", Danger = "#ff4778" },
            Toolbar = new() { Background = "#106bac", Color = Color.White, MinHeight = 52 },
            Content = new() { Background = "#f0f3f7" },
            Button = new() { SolidBackground = "#288ed0", BorderRadius = 4 },
            Tab = new() { BarBackground = Color.White, BarColorSelected = "#288ed0", BarHeight = 62, ButtonFontSize = 11, ButtonIconSize = 24 },
            Segment = new() { ButtonCheckedColor = "#288ed0", IndicatorColor = "#288ed0", IndicatorHeight = Length.Px(2), IndicatorBorderRadius = 0, IndicatorBoxShadow = [] },
        });
        builder.Services.AddSingleton<FBoxOptions>();
        builder.Services.AddSingleton<FBoxClient>();
        builder.Services.AddSingleton<FBoxRealtime>();
        builder.Services.AddSingleton<FBoxStore>();
        builder.AddResourceAssembly(typeof(App).Assembly);
        builder.AddStyleSheet(GlobalStyles.Create());
        builder.UseGeneratedRoutes();
        builder.UseDefaultLayout<MainLayout>();
        builder.EnableHotReload();
        configure?.Invoke(builder);
        return builder.Build();
    }

    public static void InitializeHotReload(MikoAppContext context) => MikoHotReloadHandler.Initialize(context.GetHotReloadService());
}
