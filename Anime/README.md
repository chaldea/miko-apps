# Anime

基于 Miko.Ionic 的原生动漫应用。UI 由 Razor + SkiaSharp 绘制，视频使用 `Miko.Components.Player` 和系统解码器。

应用显示名称统一为 Anime。Android、iOS 和桌面使用统一的应用图标，平台适配和资源说明见 [图标说明](Branding/README.md)。

## 运行

在仓库根目录执行：

```powershell
dotnet build
dotnet run --project Anime/MikoApp.Anime.Desktop
# 带设备选择、横竖屏和 MCP 检查工具
dotnet run --project Anime/MikoApp.Anime.Simulator
```

默认开启演示模式，无需联网。在“我的 → 系统设置”切换演示模式，点击“保存设置”生效并记住选择。关闭后显示服务器地址，默认 `http://uminainfo.cc/api.json`，支持服务发现、备用入口和健康检查；也可以填写其他发现 JSON 地址或 API 根地址。自定义地址失败不会连接默认服务，在线失败也不会自动回退到演示内容。

诊断时可用环境变量覆盖启动模式（`1` 为演示，`0` 为在线；正常使用无需设置）：

```powershell
$env:ANIME_OFFLINE = '1'
dotnet run --project Anime/MikoApp.Anime.Desktop
Remove-Item Env:ANIME_OFFLINE
```

## 页面与行为

详情页使用 Miko 标准路由 `/detail/{Id:int}`，例如 `/detail/123`；页面通过 `[Parameter]` 接收 ID。列表、轮播、推荐和观看记录均直接调用 `NavigationManager.NavigateTo`，返回时由 Miko 的历史栈恢复原来的番剧，无需全局选中项或自定义导航服务。

- 三栏导航：首页、福利、我的；开屏自动进入首页。
- 首页轮播、日漫/剧场版筛选与分页、搜索与搜索历史、排行榜、周排期。
- 详情、分线路选集、简介面板、评论分页、收藏、原生视频播放、横屏全屏、弹幕颜色与位置面板。
- 登录、验证码注册、找回密码、个人资料、退出登录、云端收藏、观看历史、离线缓存和消息空态。
- 福利、下载、消息等尚未完整接入的功能保留 UI，并明确提示当前可用范围。

API 客户端负责服务发现、健康检查、账户与验证码、收藏、评论、弹幕和权益接口。HTTP 重定向关闭，错误保留 HTTP 状态、业务码、消息及重试时间。权益方法仅供后续接入，UI 不调用付费或兑换操作。

`AnimeStore` 管理目录和会话，设置保存至 LocalApplicationData/MikoAnime/settings.json，观看/搜索历史按演示模式和服务器分别保存至 `library-*.json`；可用 `ANIME_DATA_DIR` 指定隔离目录。默认在线服务兼容读取旧 `library.json` 中的真实观看记录。密码和 token 不写入磁盘，切换模式或服务器会取消旧请求并退出登录，避免凭证跨服务器发送。

演示模式在 API 客户端统一使用本地数据，账号、资料、收藏、评论仅在本机模拟；邮箱验证码固定为 `123456` 且不发送邮件。演示账号与评论仅保留本次会话，观看和搜索历史保留在本地。所有分集共用 `Assets/sample.mp4`，不保存真实媒体 URL 或视频文件。

游客播放根据配置和分集标志进入验证码流程，使用 `expires_in` 管理有效期。弹幕的颜色与位置尚未接入云端同步，界面明确标注“仅在本机显示”。

## 项目

| 项目 | 用途 |
| --- | --- |
| MikoApp.Anime | 共享页面、样式、协议、数据服务和内置资源 |
| MikoApp.Anime.Desktop | 桌面宿主，系统视频后端 |
| MikoApp.Anime.Simulator | 设备模拟器，MCP 位于 localhost:5800 |
| MikoApp.Anime.Android | Android 宿主与原生视频后端 |
| MikoApp.Anime.iOS | iOS 宿主与原生视频后端 |
| MikoApp.Anime.Verification | 应用内的原生视觉/交互及协议验证工具 |

## 验证

```powershell
# 详情直达、不同 ID 切换、返回恢复和海报点击导航
dotnet run --project Anime/MikoApp.Anime.Verification -- --navigation
# 本地演示数据：协议、原生点击、输入、弹窗、播放及截图
dotnet run --project Anime/MikoApp.Anime.Verification
# iOS 组件模式的同一套视觉交互验证
dotnet run --project Anime/MikoApp.Anime.Verification -- Anime/artifacts/verification-ios --ios
# 真实只读接口探测；不登录、不发布评论、不修改资料
dotnet run --project Anime/MikoApp.Anime.Verification -- --live
# 真实目录、详情、游客验证码和评论的原生截图；不提交验证码
dotnet run --project Anime/MikoApp.Anime.Verification -- --live-ui
# 图片请求头、并发/内存复用、磁盘复用、损坏文件和错误响应验证
dotnet run --project Anime/MikoApp.Anime.Verification -- --images
# 真实列表封面与隔离的冷/热缓存验证；默认要求所有图片可达
dotnet run --project Anime/MikoApp.Anime.Verification -- --live-images
# 诊断模式：允许非豆瓣图片站不可达，逐项报告，不把占位图算作成功
dotnet run --project Anime/MikoApp.Anime.Verification -- --live-images --allow-unavailable-images
# 我的页面安全区：Android/iOS、游客/登录态、横竖屏及无安全区
dotnet run --project Anime/MikoApp.Anime.Verification -- --safe-area
# 演示模式、原图比例、离线零请求、设置持久化和服务器切换隔离
dotnet run --project Anime/MikoApp.Anime.Verification -- --demo-mode
# 显式维护命令：从真实只读 API 刷新小规模演示目录和原始图片，不获取视频
dotnet run --project Anime/MikoApp.Anime.Verification -- --refresh-demo
# 原生系统播放器诊断；文件内容为媒体 URL 或本地视频路径（建议时长超过 12 秒）
dotnet run --project Anime/MikoApp.Anime.Verification -- --playback-source <媒体地址文本文件>
```

截图、布局框和计算样式写入 `Anime/artifacts/`（Git 忽略）。默认视觉验证使用内置演示数据完成登录和收藏状态，不使用或保存真实账户；模式切换验证使用隔离 HTTP fixture 模拟在线服务。接口验证覆盖请求签名、响应处理、数据完整性校验及异常数据拒绝。

2026-11-09 验证：根目录构建及 Desktop/Simulator/Android/iOS 项目编译通过，应用 Razor 无警告。Android 宿主的旧平台 API 和 Miko 平台项目仍存在原有编译警告；iOS 真机打包/签名及移动端实际播放需要对应设备和 Apple 构建环境。Android/iOS 组件模式各完成 28 个原生截图状态检查。桌面系统解码器已播放内置视频；未使用真实账号验证鉴权播放或远程写操作。

线上只读探测成功覆盖发现、首页、配置、筛选、搜索、排行、周表、详情、评论、弹幕。豆瓣图片源要求请求携带 Referer 与浏览器 User-Agent；应用图片客户端现已补齐请求头，并将成功图片缓存在 LocalApplicationData/MikoAnime/images（可用 `ANIME_IMAGE_CACHE_DIR` 指定目录）。当前进程内仍复用 Miko 的解码位图缓存；磁盘缓存上限为 128 MiB，失败响应不会缓存。模拟器 MCP 的 DOM/盒模型可用，当前框架的计算样式工具返回内部错误，验证工具直接导出原生计算样式作为补充；页面视觉以独立原生渲染截图交叉检查。

### 封面与缓存修复（2026-11-09）

API 的 `vod_pic` 提供完整图片地址，可直接用于图片加载。对 `doubanio.com` 及其子域，请求使用 `Referer: https://douban.com` 与 Chrome 127 User-Agent。验证中，默认请求返回 418，补齐这组请求头后同一地址返回 200/WebP。

`AnimeImageHandler` 接入 Miko 默认 `ResourceManager` 的 HTTP 传输。缓存以完整 URL 的 SHA-256 为键，先读磁盘，命中时不发网络请求；写入前确认图片可以解码，临时文件写完后原子替换。缓存按最近访问时间淘汰；损坏缓存会重新下载，HTTP 错误及返回 200 的无效图片不会缓存。目录不可写时仍显示下载结果。已有内存缓存还会合并并发请求，页面切换复用同一张位图。

本次线上验证：精选 21/21、分类 18/18、搜索 6/6、排行 67/68、周表 5/5、历史 1/1 封面解码成功。新建加载器并禁止图片网络后，109 个不同 URL 全部从磁盘恢复，图片网络请求为 0。排行榜剩余的 `lain.bgm.tv/pic/cover/l/d8/1e/575559_v6ks9.jpg` 在当前网络持续连接超时，使用占位图；此第三方可达性问题仍保留在诊断报告中。截图与日志位于 `Anime/artifacts/image-fix/` 和 `Anime/artifacts/image-fix-verification.log`。

内置番剧数据于 2026-11-10 从真实 API 更新，精简为 28 部番剧和 31 张原图（约 1.8 MB，含 JSON），覆盖首页、日漫/剧场版、三个排行分组、七天排期和真实分集结构。原图等比缩小至最长边不超过 720 像素，保留完整画面；封面、排行、历史与横幅按图像固有比例显示。旧截图封面、缩略图及过期目录已移除。开屏插画使用第三方素材。完整说明见 [演示资源说明](DEMO_ASSETS.md)。

2026-11-10 演示模式验证：默认离线、设置持久化、自定义 API/发现地址、会话与历史隔离、切换时取消旧请求及在线失败行为均通过；演示页面、登录、收藏和评论的 API/图片网络请求为 0，图片布局宽高比与原图一致。设置与模式验证输出 12 个状态，Android/iOS 组件模式完整回归各 28 个截图状态；另检查两种模式下的移动端、桌面及轮播切换 6 个状态，确认相邻横幅不越界。结果位于 `Anime/artifacts/demo-mode/` 和 `Anime/artifacts/demo-regression*/`。

### 播放故障排查与我的页面安全区（2026-11-10）

测试账号能够登录并获取播放地址，但服务返回的 `txmov2.a.yximgs.com` MP4 在当次测试的直连出口发生 TLS 握手重置，Windows 报错 `0xC00D0006`。使用可访问的 MP4 与 HLS 源时，Windows 系统后端均可渲染画面、推进进度、暂停和跳转后继续播放。

出口复查修正了此前“外部媒体源故障”的结论：本机 `127.0.0.1:7890` 对视频域名实际走 DIRECT；用独立临时代理确认更换出口后，旧地址及重新解析的同一地址均返回 206 和有效 MP4 文件头（各读取 4096 字节）。源文件仍然可用，失败与当次测试的出口/路径有关，具体限制规则尚未证实。详见 [播放访问限制排查](PLAYBACK_INVESTIGATION.md)。临时代理已停止；此项未标为应用内播放已修复。

“我的”页的账号摘要现位于 `IonHeader`/`IonToolbar`，自动避开状态栏和刘海；下方列表由 `IonContent` 滚动，并留出左右安全边距。实际 Android API 36 模拟器和两种 Ionic 模式的原生布局验证通过，覆盖游客/登录态、横竖屏、零安全区和账号入口点击。截图在 `Anime/artifacts/safe-area-fix/`。
