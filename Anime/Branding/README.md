# Anime 应用图标

图标提取自原版 `issues/anime/lanerc/lanerc.apk`（Lanerc 1.0.8）。APK 清单的 `android:icon` 为 `mipmap/ic_launcher`（`0x7f0c0010`），自适应图标 `res/BW.xml` 引用独立背景与前景，最高密度图层分别为 `res/Gx.png` 和 `res/as.png`（432 × 432）。

Android 的五档 PNG 直接保留 APK 原始字节，自适应普通/圆形图标继续使用同一套图层。`appicon.png` 将两层合成，取 Android 108dp 图层中央的 72dp 可视区域，透明处合成到白底，再等比生成各平台所需尺寸；iOS 图标不含透明通道。`appicon.ico` 包含 16–256 像素，用于桌面可执行文件，运行中的桌面窗口也使用同一图案。

重新提取：安装 Pillow 后运行 `python Anime/tools/extract_app_icon.py`，可额外传入 APK 路径。脚本的资源路径对应此版本 APK，版本变化时需重新检查清单和资源表。原图版权归原权利人所有。
