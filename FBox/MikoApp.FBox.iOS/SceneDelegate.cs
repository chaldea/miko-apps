using Foundation;
using Miko.iOS;
using Miko.iOS.Native;
using Miko.iOS.Video;
using Miko.iOS.Camera;
using UIKit;

namespace MikoApp.FBox.iOS;

[Register("SceneDelegate")]
public class SceneDelegate : UIWindowSceneDelegate
{
    public override UIWindow? Window { get; set; }

    public override void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
    {
        if (scene is not UIWindowScene windowScene)
            return;

        // Newer iOS versions require every window to belong to a scene.
        // Reuse the shared app configuration; Miko.iOS drives rendering and touch input.
        // UseIosNative() supplies the Miko.Native capabilities and the platform services
        // components use, such as picker / reorder haptics.
        Window = new UIWindow(windowScene)
        {
            RootViewController = new MikoViewController(
                MikoApp.FBox.App.CreateContext(builder => { builder.UseIosNative(); builder.UseIosVideo(); builder.UseIosCamera(); }))
        };
        Window.MakeKeyAndVisible();
    }
}
