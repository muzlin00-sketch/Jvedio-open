# NOTICE

本仓库包含来自多个来源的代码与二进制组件，各自的版权与许可证如下。

## 上游项目

- **Jvedio** — https://github.com/hitchao/Jvedio
  作者：hitchao 及贡献者
  许可证：GNU General Public License v3.0（见 [LICENSE](LICENSE)）
  本仓库是其修改版，改动记录见 [MAINTAINING.md](MAINTAINING.md)。
  按 GPL-3.0 要求，本仓库同样以 GPL-3.0 发布。

## 自建替代组件

- **SuperUtils**（`Jvedio-WPF/Jvedio.SuperUtils/Shim.cs`）
  原先由闭源二进制 `SuperUtils.dll` 提供，现已按公开 API 面重新实现，
  以同名程序集输出，供主程序与插件绑定。源码在本仓库内，随本仓库以 GPL-3.0 发布。

## 仓库内的第三方二进制（`Jvedio-WPF/Jvedio/Reference/`）

这些文件不是本仓库原创，保留各自许可证，仅作为构建/运行依赖分发：

| 文件 | 来源 | 许可证（以官方为准） |
|---|---|---|
| `MediaInfo.dll` / `MediaInfoNET.dll` | MediaArea MediaInfo | BSD-2-Clause / 见 MediaInfo 官网 |
| `SuperControls.Style.dll` | 见下方说明 | 随上游以 GPL-3.0 分发 |
| `CommonNet.dll` | 见下方说明 | 随上游以 GPL-3.0 分发 |
| `ICSharpCode.AvalonEdit.dll` | AvalonEdit | MIT |
| `Newtonsoft.Json.dll` | Newtonsoft.Json | MIT |
| `HtmlAgilityPack.dll` | HtmlAgilityPack | MIT |
| `MihaZupan.HttpToSocks5Proxy.dll` | HttpToSocks5Proxy | MIT |
| `SQLite.Interop.dll`（`Data/x64`、`Data/x86`） | System.Data.SQLite | Public Domain |

### 关于 `SuperControls.Style.dll` 与 `CommonNet.dll`

这两个程序集由上游项目 [hitchao/Jvedio](https://github.com/hitchao/Jvedio) 的作者提供，
在上游仓库中以 GPL-3.0 分发，本仓库**原样保留、未做任何修改**，仅因构建与运行需要：

- `SuperControls.Style.dll`：桌面端界面控件与主题库，主程序大量 XAML 直接依赖其中的类型与资源，
  源码未公开。移除它等于重写整个 UI 层，因此暂时保留。
- `CommonNet.dll`：刮削器插件的契约库，提供 `CommonNet.Crawler.ICrawler` /
  `Jvedio.CommonNet.Crawler.AbstractCrawler`。第三方插件在运行时按此契约绑定，
  仓库只包含契约本身，不包含任何站点插件的实现。

如果上游作者对这些二进制另有授权声明，以作者声明为准；届时会按声明替换或移除。
除此之外，本仓库自研的 `SuperUtils.dll`（`Jvedio-WPF/Jvedio.SuperUtils/`）已完全以源码形式提供。

其余 NuGet 依赖（EntityFramework、MySql.Data、BouncyCastle、ExCSS、Fare、FontAwesome.WPF 等）
在构建时由 NuGet 还原，不随仓库分发，许可证见各包页面。

## 文档与图片

`Jvedio-WPF/Document/` 与 `项目文档/` 下的设计文档、截图由本项目与上游作者编写，
随本仓库以 GPL-3.0 发布；引用的第三方商标、素材归各自所有者。
