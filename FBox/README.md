# FBox for Miko

基于 `issues/fbox.md`、22 张参考截图及 `issues/fbox/fbox-mobile` 接口源码实现的原生 IoT 网关管理演示。页面使用 Miko Razor、Miko.Ionic 和 C# 样式；曲线通过原生 Canvas 绘制，扫码和 VNC 使用 Miko.Components，没有 WebView。

## 启动

在仓库根目录执行（.NET 版本见 `global.json`）：

```powershell
dotnet run --project FBox/MikoApp.FBox.Desktop
# 或设备预览，包含 localhost:5801 的 Miko MCP 调试服务
dotnet run --project FBox/MikoApp.FBox.Simulator
```

Windows 下重新构建前请先关闭正在运行的宿主，避免输出 DLL 被进程占用。

默认进入离线演示，打开 `SIM` 分组的 `100017120001` 可查看监控点、报警登记及历史数据。参考截图中的离线状态和 2026-11-03 历史记录被保留。添加设备、分组、监控点、报警、收藏和偏好会保存在本机。

演示文件为 `%LOCALAPPDATA%/MikoFBox/demo-state.json`（其他平台使用 `LocalApplicationData`）。用 `FBOX_DATA_DIR` 指定独立目录即可从初始数据体验，验证程序也会自动使用独立目录。密码、客户端密钥与登录令牌不会写入此文件。

## 项目

| 目录 | 用途 |
| --- | --- |
| `MikoApp.FBox` | 共享页面、组件、样式、状态、REST 与实时通信 |
| `MikoApp.FBox.Desktop` | 桌面启动程序，Debug 支持 F12 检查器 |
| `MikoApp.FBox.Android` | Android 26+，相机权限与 Camera2 扫码 |
| `MikoApp.FBox.iOS` | iOS 15+，相机、本地网络声明与原生相机后端 |
| `MikoApp.FBox.Simulator` | 手机/平板预览及 Miko MCP，端口 5801 |
| `MikoApp.FBox.Verification` | 原生离屏渲染、真实点击/输入、接口契约验证 |

Android 构建需要对应 .NET workload/Android SDK；iOS 真机打包需要 Mac、Xcode 和签名配置。主解决方案已包含上述项目。图标为脚本绘制的蓝底 F 字母，可用 `tools/GenerateIcons.ps1` 重新生成。

## 页面与行为

- 设备：分组展开、搜索、收藏和报警筛选、排序、添加、编辑、分享、删除确认。
- 监控：分组列表、搜索、订阅实时值、新增 Local LB/LW 点、地址标签、读写权限和离线写入保护。
- 报警：当前报警及确认、时间范围内的历史报警、登记列表、新增位/数值相等条件。
- 历史：选择数据通道与日期范围、表格和曲线切换；单次最多查询 500 条。
- 设备资料：别名、分组、备注、安装地址、系统参数、PLC 和共享用户。
- 分享与消息：分享码、有效期/权限、申请列表、批准和拒绝；本地演示分享码仅用于校验体验。
- 统一写：按名称和类型搜索、勾选具体目标、保存分组、确认批量写入。
- 账号与设置：登录、邮箱注册/找回请求、用户名/密码、注销申请、通知偏好、萤石云账号绑定、本地反馈、服务配置。
- 原生组件：移动端扫描二维码并提取 12 位序列号；可直达网络中的 VNC 连接、密码、画面和输入控制。桌面和模拟器未提供相机后端时展示手动输入入口。

## 连接真实服务

在“我的 → 服务配置”填写配置，或启动前设置环境变量。地址应为服务根地址，不带 `/api`、`/core` 等路径。此仓库没有内置生产地址或凭据。

| 变量 | 含义 |
| --- | --- |
| `FBOX_DEMO` | 默认 `1`；`0` 从未登录状态启动 |
| `FBOX_API_URL` | HTTPS 应用服务根地址 |
| `FBOX_IDENTITY_URL` | HTTPS 身份服务根地址 |
| `FBOX_CLIENT_ID` | OAuth 客户端 ID |
| `FBOX_CLIENT_SECRET` | 可选客户端密钥，仅在要求时提供 |
| `FBOX_SIGNALR_VERSION` | `core`（默认）或 `legacy` |
| `FBOX_DATA_DIR` | 本地数据目录 |

登录采用参考客户端的 OAuth password grant；服务端必须允许该授权方式。REST 请求附带 `X-FBox-ClientId`，401 触发一次串行化刷新，并仅重试一次。令牌仅存在内存中。SignalR Core 使用 `/push`，Legacy 使用 `/push/signalr` 的 `clientHub`；处理设备状态、报警数量和监控值推送。实时连接恢复后可点击监控页的刷新按钮重建订阅。

## 验证

```powershell
dotnet build
dotnet run --project FBox/MikoApp.FBox.Verification -- --protocol
dotnet run --project FBox/MikoApp.FBox.Verification -- --navigation
dotnet run --project FBox/MikoApp.FBox.Verification -- --navigation --ios
dotnet run --project FBox/MikoApp.FBox.Verification -- FBox/artifacts/verification
dotnet run --project FBox/MikoApp.FBox.Verification -- FBox/artifacts/verification-ios --ios
# Windows 下生成便于人工检查的联系表
./FBox/MikoApp.FBox.Verification/ContactSheets.ps1
# 模拟器运行时，使用 Python 标准库检查 MCP 与设备切换
python FBox/MikoApp.FBox.Verification/InspectSimulator.py
```

截图、布局和计算样式写入给定输出目录，每次使用独立的演示状态；`artifacts/` 不纳入 Git。界面验证覆盖全部页面，以及筛选、弹窗、小屏、桌面尺寸和安全区域。它通过 Miko 输入控制器执行真实命中测试、点击与键盘输入，验证新增、搜索、取消删除和统一写流程。接口验证使用可控 HTTP 响应，检查 OAuth 编码/刷新、权限位、失败操作、报警字段、历史通道、统一写目标和持久化。

`--navigation` 专门检查 37 个共用页头的页面：工具栏返回、系统返回、Escape、逐级出栈、弹窗优先关闭及无历史时的回退地址。结果输出到 `artifacts/navigation-md/` 或 `artifacts/navigation-ios/`。

模拟器启动后，可用 `FBox/.mcp.json` 配置 Miko MCP，读取 DOM、盒模型、截图、日志及切换设备。当前框架的计算样式 MCP 序列化可能返回错误；离屏验证仍会输出 `.styles.txt`，模拟器 F12 检查器也可用于查看样式。

## 演示范围

此项目实现参考截图的主要页面和设备管理流程，不包含 Angular 客户端的全部扩展功能。当前边界如下：

- 尚未使用真实服务账号联调；接口按参考代理源码与本地契约样本验证。服务端权限、字段差异、SignalR 部署及登录策略仍需在目标环境确认。
- VNC 支持直接 TCP 连接（局域网或已建立的 VPN），尚未接入原客户端的云端 WebSocket 隧道、服务器探测和异步开通流程。
- 不含摄像头云端播放、AI、手机/OIDC 登录、人机验证、邮箱链接回调、完整 PLC/地址标签编辑、固件维护和统一写执行记录。需要验证码的注册服务应使用其官方注册流程。
- 通知开关保存本地偏好，不注册系统推送；反馈保存在本机。萤石云页面只提供账号绑定。
- 移动端主项目已参与构建，但相机、手势、系统权限和 VNC 网络连接尚未在 Android/iOS 真机上测试；iOS 风格截图来自原生离屏宿主，不能代替真机验收。

构建和视觉检查结果见 [验证记录](VERIFICATION.md)。
