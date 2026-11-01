# 参与开发

感谢愿意一起维护。这个项目是 WPF（.NET Framework 4.7.2）桌面端 + 内嵌 HTTP 服务 + 单文件手机网页，
改动前请先花五分钟看下面三节，能省掉大部分返工。

## 1. 环境

| 需要 | 说明 |
|---|---|
| Windows 10/11 | 桌面端只能在 Windows 上构建 |
| .NET Framework 4.7.2 开发工具 | 提供 `%WINDIR%\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe` |
| .NET SDK 6.0+ | 构建 `Jvedio.Core`（netstandard2.0，SDK 风格项目） |
| PowerShell 7 | 构建脚本用 `pwsh` 运行 |
| 网络 | 首次构建要还原 NuGet 包 |

## 2. 构建

```powershell
pwsh -NoProfile -File .\build.ps1                       # Debug
pwsh -NoProfile -File .\build.ps1 -Configuration Release
```

产物：`Jvedio-WPF\Jvedio\bin\<Configuration>\Jvedio.exe`。

构建顺序由脚本保证，**不要颠倒**：`Jvedio.SuperUtils` → `Jvedio.Core` → `Jvedio`。
手动单独构建主项目时，如果改过 `Jvedio.Core` 里的文件，必须先重建 Core，否则主程序引用的还是旧 DLL，
表现为"代码改了但行为没变"。

运行数据（数据库、封面、日志）在程序目录的 `data\` 下，**不要提交**。

## 3. 改动前先读

| 你要做的事 | 先读 |
|---|---|
| 任何代码改动 | [项目文档/踩坑约定.md](项目文档/踩坑约定.md)（跨模块陷阱清单，多数坑都在这里） |
| 手机网页版 / 播放器 / 投屏 | [项目文档/modules/](项目文档/modules/) 对应模块文档 |
| UI / 配色 / 动效 | [项目文档/设计规范.md](项目文档/设计规范.md)、[THEME-GUIDE.md](THEME-GUIDE.md) |
| 构建环境、闭源依赖、历史决策 | [MAINTAINING.md](MAINTAINING.md) |
| 项目结构、技术栈、路由对照 | [项目文档/项目总览.md](项目文档/项目总览.md) |

## 4. 提交约定

- 一个提交做一件事，提交信息写**为什么**改，而不只是改了什么。
- 修 bug 时说明复现条件；改匹配/识别类逻辑时，把用到的文件名用例写进提交信息或测试。
- 不要把个人绝对路径、口令、API Key、真实影片库数据写进代码、文档、提交或 issue。
- 不提交 `bin/`、`obj/`、`packages/`、`_buildtools/`、`data/`、`*.sqlite`（`.gitignore` 已覆盖）。

## 5. 提交 PR

1. Fork 并建分支，分支名建议 `fix/xxx`、`feat/xxx`。
2. 本地跑通 `build.ps1 -Configuration Release`。
3. 如果改了 UI 或手机端页面，在移动视口/真机上实际点一遍，不能只看代码。
4. 在 PR 描述里写：改了什么、为什么改、怎么验证的。
5. CI（`.github/workflows/build.yml`）会跑一次 Release 构建，红了先修。

## 6. 报告问题

用 issue 模板，尽量带上：

- 版本（`Jvedio.exe` 文件属性里的版本号）
- 复现步骤，最好有截图或日志（`data\<用户名>\logs\`）
- 涉及手机端时说明手机型号、浏览器、是否同一局域网

安全相关问题不要开公开 issue，见 [SECURITY.md](SECURITY.md)。

## 7. 写刮削器

- 普通影片的刮削走**内置源**：实现 `Core/Crawler/Builtin/IBuiltinCrawler`，参考 `DoubanCrawler`。
  返回值是"键名 = `Video` 实体属性名"的字典，`Title` 非空才算命中。
- 番号类站点走**插件**：实现 `CommonNet.Crawler.ICrawler`，格式见
  [Jvedio-WPF/Document/Document.md](Jvedio-WPF/Document/Document.md)。仓库不收录第三方站点插件。
- 新增匹配规则时，务必同时更新 `项目文档/踩坑约定.md` 里对应的条目。

## 8. 代码风格

- C# 7.3（`LangVersion` 固定），不要用 C# 8+ 语法。
- 4 空格缩进，Allman 大括号；文件内 `using` 按 System → 第三方 → 本项目排序。
- 公共类型和方法写中文 XML 注释，说明"为什么这么写"，坑点优先于表面描述。
- 改动保持最小：不顺手重排无关代码，不把未完成的改动混进 PR。
