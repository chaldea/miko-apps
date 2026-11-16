# Miko Apps Showcase

用于展示本仓库应用的独立静态网站。应用本身仍由 Miko 原生渲染；网站使用 HTML/CSS 和少量原生 JavaScript，无服务端、运行时依赖或外部字体请求。

支持分类、平台、关键词组合筛选，关键词匹配名称、介绍和标签。筛选状态保存在 URL，可复制分享和刷新恢复。卡片直接跳到 GitHub 上对应应用的源码目录。页面预生成全部卡片，禁用 JavaScript 时也能浏览。

## 本地预览

需要 Node.js 22 或更新版本。在仓库根目录执行：

```sh
cd showcase
npm ci
npm run dev
```

打开终端显示的 `http://127.0.0.1:4173/`。修改清单或页面后重新运行 `npm run build`，刷新浏览器即可。`npm run build` 本身仅依赖 Node 标准库，不需要 .NET、Miko 子模块、浏览器或联网；输出在 `showcase/dist/`。

## 应用清单

`apps.json` 是应用信息、分类、平台和封面配置的唯一来源。新增应用：

1. 添加应用源码目录，并在根目录 README 的应用表中登记。
2. 在 `apps.json` 的 `apps` 数组中添加记录，使用唯一的小写 `id`；`sourcePath` 是相对于仓库根目录的源码目录。
3. 放入两张实际应用截图到 `assets/screenshots/<id>/`，在 `cover.screenshots` 中配置相对路径。推荐 390 × 844 竖屏 PNG，顺序为主屏、副屏。
4. 设置名称、简介、标签、平台、分类、封面背景/强调色与英文封面短句。分类和平台筛选项自动从清单生成。
5. 运行 `npm run covers -- <id>` 生成封面，然后构建和验证；提交清单、截图和生成的 `assets/covers/<id>.png`。

`repository` 和 `sourceRef` 控制源码链接，默认指向本仓库的 `main`。构建时可通过 `GITHUB_REPOSITORY` 和 `SOURCE_REF` 覆盖；GitHub Actions 自动使用当前仓库和构建分支，因此 fork 不会链接回原仓库。

## 可复用封面工具

封面工具用两张真实原生截图合成带应用名称、主题色和设备边框的 **1440 × 1000 PNG**。它只处理本地文件，不生成或假造应用界面，不访问线上业务服务。封面图片可独立用于网站或 README。

首次使用需安装 Chromium（仅生成封面和浏览器验证需要）：

```sh
npx playwright install chromium
npm run covers                 # 使用已提交的截图，生成全部封面
npm run covers -- anime        # 只生成 Anime
npm run covers -- --help
```

也可复用已安装的 Chrome 或 Edge：设置环境变量 `PLAYWRIGHT_CHANNEL=chrome` 或 `msedge` 后运行封面或测试命令；未设置时使用 Playwright 安装的 Chromium。CI 固定使用 Chromium。

刷新原生截图并生成封面：

```sh
# 先按仓库 README 安装 .NET SDK 并初始化 miko 子模块
npm run covers -- --capture
npm run covers -- fbox --capture
```

`--capture` 按清单的 `capture.project` 调用现有应用的原生离屏验证程序，将 `capture.files` 指定的两张图复制到 `cover.screenshots`，随后合成封面。当前 Anime 与 FBox 的验证程序使用本地演示数据，并在 `artifacts/showcase-capture/` 为每次运行分配独立目录。完整原生验证失败时命令会失败，不会继续发布封面。需要能构建对应 Verification 项目的环境；Windows 已验证。

后续应用可以复用同样的验证程序约定（第一个位置参数为输出目录），或自行提供两张截图，省略 `capture` 配置。无需修改合成脚本。源截图、封面已纳入版本控制，普通站点构建及部署不需要运行原生应用。

## 验证

```sh
npm ci
npx playwright install chromium
npm test
```

测试覆盖清单校验、源码地址及 HTML 转义，以及真实浏览器中的分类/平台/关键词组合、空态重置、URL 恢复、键盘操作、无 JavaScript 浏览和 360/768/1440 宽度布局。测试服务特意运行于 `/miko-apps/`，检查 GitHub Pages 项目子路径上的图片、脚本和样式加载。

## GitHub Pages

工作流位于 `.github/workflows/showcase.yml`：

- PR 中修改站点时执行构建和测试，不部署。
- `main` 分支上的站点改动通过验证后，上传 `showcase/dist/` 并部署到 Pages。
- 可以在 Actions 页面手动运行；部署限制在 `main` 分支。
- 构建只有读取权限；部署单独授予 `pages: write` 和 `id-token: write`。

首次发布需在仓库 **Settings → Pages → Build and deployment → Source** 选择 **GitHub Actions**，然后合并到 `main` 或手动运行工作流。预期项目站点地址为 `https://chaldea.github.io/miko-apps/`，实际地址以部署任务输出为准。若仓库默认发布分支不是 `main`，同步修改工作流的 `push.branches`、部署条件和清单的 `sourceRef`。

站点资源均使用相对路径，同时兼容项目 Pages 子路径、域名根路径和自定义域名。不会部署应用源码、原始截图、演示数据、验证产物或 Node 依赖。

## 截图来源

`assets/screenshots/anime/` 来自 Anime 离线验证的首页及周排期；`assets/screenshots/fbox/` 来自 FBox 离线验证的设备及监控页面。截图中的第三方封面等内容沿用原应用的资源来源与权利范围；参见根目录 README 的第三方资源说明。生成器和网站代码遵循仓库 MIT 许可，不能据此推定截图内第三方素材亦为 MIT。
