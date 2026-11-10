# FBox 验证记录

## 问题列表第 1 项：返回无效

已复现旧代码：设备列表点击进入监控页后，点击页头返回仍停在 `/monitor`，系统返回分发结果为 `false`。原因是 `PageHeader` 属于普通子组件，其继承的 `NavigationManager` 属性没有被路由系统赋值，原处理器通过空条件运算符静默跳过导航；自定义 `IonButton` 同时没有向系统返回链注册处理者。

共享页头已改为 `IonBackButton`，由该组件通过依赖注入取得导航服务，并注册最低优先级的系统返回处理。保留白色箭头与 48×48 点击区域，`BackHref` 作为无历史时的回退地址。Android 宿主原有 `HandleBackPressed()` 转发即可使用此注册，无需额外拦截系统按键。

验证结果：

- `--navigation` 与 `--navigation --ios` 通过：37 个页头路由逐个真实点击返回，嵌套历史、系统返回、Escape、根页面释放返回事件、分组弹窗先关闭、无历史回退均通过。
- 运行中的模拟器通过 MCP 点击设备行、点击返回，确认 DOM 回到设备列表；截图在 `artifacts/navigation-mcp/`。
- `dotnet build` 和 Android `SignAndroidPackage` 成功，FBox 没有编译警告；整仓仍有下述 3 条框架既有警告。
- 修复 APK 已通过保留数据的覆盖安装更新到连接的 Android 16 手机。解锁后实测通过：点击页头箭头从“新增监控点”返回监控页，再次点击返回设备列表；重新进入设备监控页，发送 Android `KEYCODE_BACK` 后回到设备列表。逐步截图已检查，保存在 `artifacts/navigation-android/06-resume.png` 至 `10-system-monitor-to-devices.png`。

日志：`artifacts/navigation-before.log`、`navigation-md.log`、`navigation-ios.log`、`navigation-solution-build.log`、`navigation-android-build.log`。

## 问题列表第 2 项：底部 Tab 图文间距过大

根因在应用样式：`GlobalStyles.cs` 的 `.ion-label` 规则设置 `MarginTop = 12px`、`MarginBottom = 12px`，同时作用于列表标签与 Tab 标签，覆盖了 Miko.Ionic 的 Tab 标签上边距 2px。加上图标下边距 2px，图文间距变成 14px，内容在 62px 高的按钮内发生压缩，图标顶部贴住按钮边缘。

已将该规则限定为 `.ion-item .ion-label`。列表项保留原间距，首页的 4 个 Tab 和设备详情的 5 个 Tab 使用组件默认间距。Miko.Ionic 原有的居中布局正常，无需修改框架。

验证结果：

- 通过运行中的 Miko Simulator MCP 检查 DOM 和盒模型，覆盖 Generic 390×844、iPhone SE 375×667、Pixel 7 412×915，以及底部安全区域。两个页面的所有 Tab 图文间距均为 4px，上下留白约 9.74px，图标恢复 24×24，不再压缩。
- Android 16 Pixel 7 模拟器安装包含程序集的 x64 APK 后，检查设备列表、监控页、更多页截图；Tab 点击切换和两次系统返回到设备列表正常。截图在 `artifacts/tabs-android/`。
- iOS 风格的首页与监控页离屏截图已检查，位于 `artifacts/tabs-after/ios-devices.png`、`ios-monitor.png`；已有 `--navigation --ios` 的 37 页导航回归也通过。MCP 切换设备后的截图出现黑屏，未将这些截图记为视觉验证通过；布局依据 MCP 盒模型，视觉依据 Android 模拟器和离屏渲染。
- `dotnet build` 与 Android `SignAndroidPackage` 成功，FBox 没有编译警告；整仓仍有下述 3 条框架既有警告。

日志与布局数据：`artifacts/tabs-mcp.log`、`tabs-after/*-metrics.json`、`tabs-navigation-ios.log`、`tabs-solution-build.log`、`tabs-android-emulator-build.log`。Android 模拟器打包使用 `-p:EmbedAssembliesIntoApk=true -p:RuntimeIdentifier=android-x64`。

## 构建

在仓库根目录执行 `dotnet build`：成功，0 错误。FBox 共享层、Desktop、Simulator、Android、iOS 和 Verification 均参与构建，FBox 自身没有编译警告。

整仓输出中有 3 条来自现有 Miko.Android 的警告：`AndroidVideoBackend.cs` 两处 CS8604，以及 `AndroidFeedbackServices.cs` 一处 CA1422。未修改框架子模块来屏蔽这些警告。当前 Windows 上的 iOS 构建结果不代表已经完成 Mac 签名或真机安装。

完整本地日志：`artifacts/solution-build.log`。

## 原生界面与交互

使用共享 App 和真实 Miko 渲染引擎，在离屏 Skia 表面上执行输入与命中测试。Material 和 iOS 风格各生成 **57 张截图**，合计 **114 张**；每张都有对应的布局与计算样式记录。逐页检查了截图联系表，并针对扫码页、iOS 首页、弹窗和历史筛选查看单图。

| 覆盖范围 | 检查内容 |
| --- | --- |
| 设备首页 | 默认组、收藏、报警筛选、搜索、操作菜单、删除确认、分组选择 |
| 设备表单 | 新增、编辑、分组、分享码、地址、必填与序列号验证 |
| 监控与报警 | 列表、详情、只读/离线写入状态、新增点、新增报警、当前/登记/历史切换 |
| 历史 | 时间表格、原生曲线、通道与日期筛选、空数据处理 |
| 设备设置 | 基本资料、系统参数、PLC、共享用户、远程页、VNC 连接表单 |
| 账号与全局设置 | 登录/注册/找回、账号管理、通知、安全、服务设置、反馈、萤石云、注销、协议 |
| 统一写 | 搜索目标、复选框、保存指定目标、写值确认、写入结果 |
| 原生扫码 | 无相机的中文提示、预览容器高度、手动输入入口 |
| 尺寸 | 390×844、360×740、1024×768，顶部 47/底部 34 的安全区域 |

发现并修复：iOS 搜索图标与占位文字重叠、选中标签文字对比不足、扫码容器高度归零。扫码权限和相机后端已在移动宿主注册，离屏环境仅验证无相机状态。

交互断言通过：设备/点/报警新增、无效序列号提示、搜索结果、取消删除保留设备、统一写保存具体目标、确认后修改目标值。布局检查通过：尺寸为有限值、页面内容没有折叠、点击点位于视口中且命中预期元素。

本地结果：`artifacts/verification/`、`artifacts/verification-ios/`、`artifacts/contact-*.jpg`、`artifacts/ios-contact-*.jpg`。运行方法见 README；这些可再生成的文件不纳入 Git。

## REST 与实时消息

`--protocol` 验证通过，使用本地 HTTP fixture，不访问真实服务：

- OAuth 表单正确编码特殊字符；并发 401 仅触发一次刷新；重复 401 仅重试一次并清除会话。
- 设备分组字段与监控读写权限位；只读/离线写入不发送请求。
- 设备状态数组推送、监控格式化值、读取超时提示。
- 报警的相等条件、阈值、地址标签、分组 UID。
- 历史通道 UID、毫秒时间戳、`rows/t/c` 数据解析。
- 统一写提交明确目标；演示批量写入成功与离线拒绝。
- 服务拒绝删除后仍保留本地设备；演示数据持久化。

日志：`artifacts/protocol.log`。尚未验证真实 OAuth/SignalR 服务器和实际设备写入。

## 运行中的模拟器

通过 localhost:5801 的标准 MCP 协议验证：设备列表查询返回 9 行，盒模型正常，日志没有应用运行异常；抓取默认 Android 设备、iPhone SE 竖屏/横屏截图并检查布局。预览检查结束后恢复为原设备和竖屏。

`devtools_get_computed_style` 返回服务端调用错误，未将其记为通过。计算样式改用离屏验证生成的 `.styles.txt` 检查。对应 JSON、日志和截图在 `artifacts/mcp/`。可用 Python 执行 `MikoApp.FBox.Verification/InspectSimulator.py` 重现检查。

## 仍需目标环境验收

真实服务账号联调、移动端相机/权限、VNC 服务器连接、Android 其他功能与 iOS 真机运行及发布签名。Android 页头返回与系统返回已按上文完成真机验证。云端 VNC 隧道和其他未移植的扩展功能列于 README 的“演示范围”。
