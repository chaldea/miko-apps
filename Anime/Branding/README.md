# Anime 应用图标

Android、iOS 和桌面使用统一的图标图案，并按各平台要求提供对应尺寸和格式。

- Android：提供五档密度的 PNG，自适应普通和圆形图标共用前景、背景图层。资源位于 `MikoApp.Anime.Android/Resources/`。
- iOS：图标位于 `MikoApp.Anime.iOS/Assets.xcassets/AppIcon.appiconset/`，使用白色背景，不含透明通道。
- 桌面：本目录的 `appicon.ico` 包含 16–256 像素尺寸，用于可执行文件；运行中的窗口也使用同一图案。
- 通用图片：本目录的 `appicon.png` 合成前景与背景，采用 Android 108dp 图层中央的 72dp 可视区域，透明处合成到白底，再等比生成各平台所需尺寸。

更新图标时应同步检查各平台资源，保持图案、比例与显示名称一致。

图标使用第三方素材，版权归原权利人所有，适用仓库 README 的第三方资源说明；项目代码的 MIT 许可不授予这些素材的使用权。
