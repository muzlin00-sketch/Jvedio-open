# Jvedio 维护版构建说明

本项目是 [hitchao/Jvedio](https://github.com/hitchao/Jvedio) 的维护分支。
上游已于 **2026-04-14 归档为只读**，最后代码提交为 2023-12-18，最后发布版本 5.4（2023-09-29）。

> **本文定位（2026-11-01 起）**：只讲**构建环境**——需要什么、怎么打通、目录长什么样、哪些区域别碰。
> 其余内容都在各自文档里，本文不复述：
> - 构建步骤、打包发布、端口与验证 → [项目文档/部署.md](项目文档/部署.md)
> - 改过什么、当时怎么验证 → [项目文档/维护/历史改动.md](项目文档/维护/历史改动.md)
> - 闭源二进制现状与替换进度 → [项目文档/闭源依赖替换.md](项目文档/闭源依赖替换.md)
> - 功能现状与实现要点 → [项目文档/modules/](项目文档/modules/)
> - 踩过的坑与通用判据 → [项目文档/踩坑约定.md](项目文档/踩坑约定.md)
>
> 「按任务该读哪份」见 [AGENTS.md](AGENTS.md)。

## 快速开始

```powershell
# Debug 构建（默认）
pwsh -NoProfile -File .\build.ps1

# 全新重建
pwsh -NoProfile -File .\build.ps1 -Clean

# Release 构建
pwsh -NoProfile -File .\build.ps1 -Configuration Release
```

构建成功后产物位于 `Jvedio-WPF\Jvedio\bin\Debug\Jvedio.exe`。

## 环境要求

**不需要安装 Visual Studio。** 本机（Windows + .NET 10 SDK）已验证可用，构建链路如下：

| 组件 | 来源 | 说明 |
|---|---|---|
| MSBuild | 系统自带 `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe` | .NET Framework 版，非 `dotnet msbuild` |
| WPF 编译器 | 系统自带 `Microsoft.WinFX.targets` | 已随 .NET Framework 安装 |
| C# 编译器 | NuGet `Microsoft.Net.Compilers.Toolset 4.8.0` | 系统自带只有 C# 5.0，无法编译 `$""` 插值与 `using static` |
| net472 参考程序集 | NuGet `Microsoft.NETFramework.ReferenceAssemblies.net472 1.0.3` | 本机无 VS，缺 Targeting Pack |

两个 NuGet 工具包由 `build.ps1` 自动安装，并在 `Jvedio.csproj` 中通过 `CscToolPath` 与 `TargetFrameworkRootPath` 接入。

> 如果以后安装了 Visual Studio 2022（含 .NET Framework 4.7.2 SDK 与目标包），上述两处 override 会被条件判断自动跳过，不影响原生 VS 构建。

## 已修复的上游构建阻塞

上游仓库 clone 下来**无法直接编译**，本副本修复了以下 5 处：

1. **`Lib\` 目录不存在** —— `Jvedio.csproj` 中 `MediaInfoNET.dll`、`PInvoke.dll`、`QueryEngine.dll`、`UsnOperation.dll` 四个引用的 `HintPath` 写作 `Lib\...`，但这 4 个文件实际都在 `Reference\` 下。已改为 `Reference\...`。
2. **PreBuildEvent 硬编码作者本机路径** —— 原内容包含 `XCOPY "D:\SuperStudio\Newtonsoft.Json\..."`、`D:\SuperStudio\SuperUtils\...`、`D:\SuperStudio\SuperControls\...`。这些目录只存在于原作者机器上，XCOPY 失败导致构建中断。已删除，因为 `Reference\` 中已提供对应 DLL。
3. **`CommonNet.dll` 引用指向已删除的仓库** —— 原为 `..\..\..\Jvedio-Crawler\Common\bin\Debug\CommonNet.dll`，而 `hitchao/Jvedio-Crawler` 仓库已 404。已改为 `Reference\CommonNet.dll`（该文件在仓库中有提交）。
4. **`mkdir` 非幂等导致 PostBuildEvent 失败** —— 目录已存在时 `mkdir` 返回非零退出码，中断构建。已改为 `if not exist ... mkdir ...`。
5. **语言版本过低** —— 系统 MSBuild 默认调用 C# 5.0 编译器，全项目 100+ 处 `$""` 插值语法直接报 CS1056。已接入 Roslyn 并设 `LangVersion=7.3`。

## 构建环境注意事项

**NuGet 缓存目录权限异常。** 本机 `%TEMP%\NuGetScratch` 与 `%LOCALAPPDATA%\NuGet\v3-cache` 无法被 nuget.exe 写入（报 "Unable to obtain lock file access" / "访问被拒绝"），但对应目录的 ACL 本身正常。`build.ps1` 已通过重定向 `TEMP`、`NUGET_PACKAGES`、`NUGET_HTTP_CACHE_PATH`、`NUGET_SCRATCH` 到 `_buildtools\` 规避。

手动执行 nuget 命令时需先设置同样的环境变量，否则会失败。

## 目录说明

```
Jvedio-open/
├─ build.ps1                  # 一键构建（SuperUtils → Core → 主程序）
├─ scripts/repack-release.ps1 # 用构建产物重打便携版发布包
├─ _buildtools/               # nuget.exe 与各类缓存（可删除，脚本会重建，git 忽略）
├─ plugins/crawlers/          # 自带的四个番号插件（上游二进制，原样分发）
├─ Jvedio-WPF/                # 唯一在维护的子项目
│  ├─ Jvedio/                 # 主程序：191 个 .cs，约 26,500 行；35 个 .xaml
│  ├─ Jvedio.Core/            # 跨平台核心库（netstandard2.0，dotnet build）
│  ├─ Jvedio.SuperUtils/      # 自建 SuperUtils.dll 源码
│  ├─ Jvedio.Test/            # MSTest 测试（含 Appium UI 测试，依赖较重）
│  ├─ Document/               # 开发者文档、插件开发文档、Wiki 快照
│  └─ packages/               # NuGet 还原产物（git 忽略）
├─ 项目文档/                  # 总览、踩坑、设计规范、部署、模块与维护文档
├─ docs/images/               # README 用截图
└─ release/                   # 预编译的便携版发布包
```

## 维护边界建议

| 可维护 | 需绕行 | 基本不可维护 |
|---|---|---|
| UI/交互、扫描导入流程、NFO 解析、数据库层、任务调度、日志、局域网 API、**内置刮削器**（源码在 `Core/Crawler/Builtin/`） | 识别码规则（可在调用侧后处理）、图片路径策略 | 番号插件本体（上游第三方 DLL，要改只能自己重写）、`SuperControls.Style` 界面层、Everything 全盘扫描内核 |

## 更细的资料在哪

| 想知道什么 | 看这里 |
|---|---|
| 改过哪些东西、为什么、当时怎么验证的 | [项目文档/维护/历史改动.md](项目文档/维护/历史改动.md) |
| 上游 119 个未关闭 issue 的主题分布 | [项目文档/维护/上游issue.md](项目文档/维护/上游issue.md) |
| 闭源二进制还剩哪些、替换到哪一步 | [项目文档/闭源依赖替换.md](项目文档/闭源依赖替换.md) |
| 刮削怎么配、插件机制 | [项目文档/刮削与插件.md](项目文档/刮削与插件.md)、[项目文档/modules/刮削.md](项目文档/modules/刮削.md) |
| 构建、打包、发布 | [项目文档/部署.md](项目文档/部署.md)、`build.ps1`、`scripts/repack-release.ps1` |
