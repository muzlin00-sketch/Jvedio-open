# Jvedio 维护版

[![build](https://github.com/muzlin00-sketch/Jvedio-open/actions/workflows/build.yml/badge.svg)](https://github.com/muzlin00-sketch/Jvedio-open/actions/workflows/build.yml)
[![license](https://img.shields.io/badge/license-GPL--3.0-blue.svg)](LICENSE)
[![platform](https://img.shields.io/badge/platform-Windows%2010%2F11-lightgrey.svg)](#构建)

本地视频管理软件（WPF / .NET Framework 4.7.2）：扫描本地视频建立影片库、识别番号、管理标签与演员，
并内嵌一个自建 HTTP 服务，手机浏览器扫码即可浏览和播放。

本仓库是 [hitchao/Jvedio](https://github.com/hitchao/Jvedio)（GPL-3.0，上游已归档）的**修改版**，
在上游基础上做了较多重构与功能补充。改动记录见 [MAINTAINING.md](MAINTAINING.md)，
对外可见的变化见 [CHANGELOG.md](CHANGELOG.md)。

## 主要特性

- **影片库管理**：扫描目录、按番号自动分类、标签 / 演员 / 系列 / 发行商筛选，支持多个数据库。
- **手机网页版**：桌面端内嵌 HTTP 服务，手机扫码即用；支持列表、搜索、详情、播放、投屏，
  访问口令鉴权（每台设备输一次）。
- **播放器**：自定义控件栏、双击快进、横向滑动快进、倍速、全屏、断点续播。
- **相似推荐**：按演员 / 导演 / 剧情 / 文件名多维度打分，短片按系列标记（文件名前缀）匹配。
- **番号识别**：白名单式后缀剥离（`ABC-123-ch` / `-HD` 等标记会被去掉，`HRV-030A` 这类合法尾字母保留）。
- **内置刮削器**：普通影片（电影 / 剧集 / 动画）按文件名自动刮削，源码内置豆瓣 / TMDB / Bangumi 三个源，
  不依赖插件；TMDB 需自填免费 API Key，豆瓣与 Bangumi 免 Key。番号类资源仍走插件。
- **闭源依赖替换**：`SuperUtils` 由源码重建（`Jvedio-WPF/Jvedio.SuperUtils`），
  原先闭源的 USN 文件枚举改用自研实现，详见 [项目文档/闭源依赖替换.md](项目文档/闭源依赖替换.md)。

## 构建

需要：

- Windows 10/11
- .NET Framework 4.7.2 开发工具（提供 `MSBuild.exe`，路径 `%WINDIR%\Microsoft.NET\Framework\v4.0.30319`）
- .NET SDK 6.0+（构建 `Jvedio.Core`，netstandard2.0 项目）
- PowerShell 7（`pwsh`）
- 首次构建需要联网（NuGet 还原）

```powershell
pwsh -NoProfile -File .\build.ps1                  # Debug
pwsh -NoProfile -File .\build.ps1 -Configuration Release
```

产物在 `Jvedio-WPF\Jvedio\bin\<Configuration>\Jvedio.exe`。构建顺序由脚本保证：
`Jvedio.SuperUtils` → `Jvedio.Core` → `Jvedio`，手动构建时不要颠倒。

## 运行

直接运行 `Jvedio.exe`。首次启动会引导选择影片库目录；手机端在桌面端「设置 → 局域网」里开启服务后，
用手机扫码或访问 `http://<本机局域网IP>:<端口>` 即可。手机端排障见 [MOBILE-GUIDE.md](MOBILE-GUIDE.md)。

运行数据（数据库、封面、日志）在程序目录的 `data\` 下，不进仓库。

## 文档导航

| 想知道什么 | 看这里 |
|---|---|
| 项目结构、技术栈、路由对照 | [项目文档/项目总览.md](项目文档/项目总览.md) |
| 改代码前必看的跨模块陷阱 | [项目文档/踩坑约定.md](项目文档/踩坑约定.md) |
| 手机网页版 / 播放器 / 投屏 / 访问口令 / 写真库 | [项目文档/modules/](项目文档/modules/) |
| UI、配色、动效、手势参数 | [项目文档/设计规范.md](项目文档/设计规范.md)、[THEME-GUIDE.md](THEME-GUIDE.md) |
| 界面改版思路 | [UI-REDESIGN.md](UI-REDESIGN.md) |
| 构建环境、闭源依赖、历史决策与验证记录 | [MAINTAINING.md](MAINTAINING.md) |
| 打包与部署 | [项目文档/部署.md](项目文档/部署.md) |
| 插件格式（刮削器 / 皮肤） | [Jvedio-WPF/Document/Document.md](Jvedio-WPF/Document/Document.md) |
| 手机端怎么用、打不开怎么排查 | [MOBILE-GUIDE.md](MOBILE-GUIDE.md) |
| 怎么参与开发 | [CONTRIBUTING.md](CONTRIBUTING.md) |

## 刮削器与插件

刮削分两条路：

1. **内置刮削器（普通影片）**：源码在 `Jvedio-WPF/Jvedio/Core/Crawler/Builtin/`，内置豆瓣 / TMDB / Bangumi，
   按文件名解析标题与年份后自动匹配，无需安装任何插件。TMDB 需要在设置里填自己申请的免费 API Key，
   豆瓣与 Bangumi 免 Key。命中即停止，未命中再回落到插件。
2. **插件刮削器（番号类）**：程序启动时从 `plugins/crawlers/<插件名>/` 加载 DLL，
   插件需实现 `CommonNet.Crawler.ICrawler` / `Jvedio.CommonNet.Crawler.AbstractCrawler`。
   仓库**自带 JavBus 插件**（`plugins/crawlers/bus/`），并在没有配置记录时自动补一条默认源
   （`https://www.javbus.com/`），装好即能刮番号类资源，不需要额外下载。
   要改地址或加 Cookie，去「设置 → 刮削器」。
   上游另外还有 fc2 / library / db 三个番号插件和 5 套主题，在程序内「插件中心」安装：
   主界面右上角菜单 → 插件。插件列表来自上游的插件仓库，本项目只做分发，不修改插件本身，
   来源与授权见 [NOTICE.md](NOTICE.md)。

## 目录结构

| 路径 | 说明 |
|---|---|
| `Jvedio-WPF/Jvedio/` | 主程序（WPF）：界面、业务逻辑、内嵌 HTTP 服务、内置刮削器 |
| `Jvedio-WPF/Jvedio.Core/` | 跨平台核心库（netstandard2.0），不含 WPF 与闭源依赖 |
| `Jvedio-WPF/Jvedio.SuperUtils/` | 自建 `SuperUtils.dll` 源码（替代闭源二进制） |
| `Jvedio-WPF/Jvedio.Test/` | 测试工程（大文件样本不入库，见其 README） |
| `Jvedio-WPF/Document/` | 设计文档、插件示例、上游 Wiki |
| `plugins/crawlers/bus/` | 自带的 JavBus 番号插件（来自上游插件仓库，原样分发） |
| `项目文档/` | 项目总览、踩坑约定、设计规范、部署、模块文档 |
| `build.ps1` | 一键构建脚本 |
| `MAINTAINING.md` | 构建环境、闭源依赖、历史改动与验证记录 |
| `release/` | 预编译的便携版发布包 |

## 参与开发

见 [CONTRIBUTING.md](CONTRIBUTING.md)。简单说：`build.ps1` 跑通 → 改前读 `项目文档/踩坑约定.md` →
提交里写清"为什么" → PR 附验证步骤。CI 会跑一次 Release 构建。

## 许可证

[GPL-3.0](LICENSE)。本仓库为修改版，原始版权归 Jvedio 作者所有，
第三方二进制组件的来源与许可证见 [NOTICE.md](NOTICE.md)。

## 安全

发现安全问题请见 [SECURITY.md](SECURITY.md)，不要在公开 issue 里贴利用细节。

## 免责声明

本软件仅用于管理**你自己合法拥有**的本地媒体文件。项目不提供、不内置任何内容源，
刮削器插件由第三方提供，使用前请自行确认合法性与合规性。
