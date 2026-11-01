# Jvedio 维护版构建说明

本目录是 [hitchao/Jvedio](https://github.com/hitchao/Jvedio) 的本地维护副本。
上游已于 **2026-04-14 归档为只读**，最后代码提交为 2023-12-18，最后发布版本 5.4（2023-09-29）。

> **本文定位（2026-10-27 起）**：构建环境原理、闭源依赖、上游 issue 分布与**历史改动记录**。
>
> 以下内容已迁出，以对应文档为准，本文同名章节视为历史记录，不再追改：
> - 部署、构建步骤、端口与验证 → [项目文档/部署.md](项目文档/部署.md)
> - 功能现状与实现要点 → [项目文档/modules/](项目文档/modules/)
> - 踩过的坑与通用判据 → [项目文档/踩坑约定.md](项目文档/踩坑约定.md)
> - UI 与手势参数 → [项目文档/设计规范.md](项目文档/设计规范.md)
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

## 本地维护阶段修复记录

在上述构建修复之后，又处理了以下问题（均经实测验证）：

### 功能缺陷

1. **识别码尾部字母未剥离** —— 上游 issue #358 / #443 反馈的「识别码多一个 C 导致同步失败」。
   实测确认 `JvedioLib.GetVID` 只剥离**带分隔符**的尾缀（`ABC-123-C` → `ABC-123`），
   字母**直接粘连**在数字上时原样保留（`ABC-123C` → `ABC-123C`），`U`/`UC` 等同样如此。

   修复方式（`Utils\Extern\JvedioLib.cs`）：**反转默认值**，让 `GetVID` 直接返回归一化结果，
   需要原始值的那一处单独命名为 `GetRawVID`。这样「忘记归一化」在设计上不可能发生。

   | 入口 | 行为 | 使用场景 |
   |---|---|---|
   | `GetVID(str)` | 提取 + 剥离尾部字母 | **默认**，绝大多数场景 |
   | `GetRawVID(str)` | 原样返回 | 仅 `SubSectionComparer`（用 `IndexOf` 推算分段序号，依赖长度一致） |

   实测（`ABC-123C`→`ABC-123`、`ABC-123UC`→`ABC-123`、`IPZ-229C`→`IPZ-229`；
   而 `CDE-456`、`HEYZO-1234`、`072214_001`、`N01234` 等合法识别码不受影响）。

### 构建质量

2. **`JvedioLib.dll` 的隐式复制依赖** —— 该 DLL 既不在 `<Reference>` 也不在 `<Content>`，
   仅靠 `PreBuildEvent` 的 XCOPY 复制；代码用 `Assembly.LoadFrom("JvedioLib.dll")` 相对路径加载。
   一旦换构建方式或 PreBuildEvent 失败，运行时直接抛 `DllLoadFailedException`。
   已改为正式的 `<None>` + `CopyToOutputDirectory` 声明。

3. **`app.manifest` 声明了部分信任** —— 原 `applicationRequestMinimum` +
   `PermissionSet ID="Custom" SameSite="site"` 把程序标记为「部分信任 + 允许跨站点」，
   导致编译期对 28 个强命名程序集报 MSB3177、以及 MSB3111。
   WPF 桌面程序始终完全信任运行，该声明既无必要也不正确，已移除。

4. **12 个 DLL 重复登记** —— 同时被 `<Reference>` 和 `<Content>` 声明，产生 12 条 MSB3178。
   已移除冗余的 `<Content>`（`<Reference>` 本身会复制到输出目录）。
   唯一例外 `MediaInfo.dll` 是原生 DLL，保留 `<Content>`。

5. **`System.ValueTuple` 版本冲突** —— 直接引用 4.5.0 包（4.0.3.0）高于 net472 内置的 4.0.2.0，
   产生 MSB3257/MSB3258。net472 已内置 ValueTuple，已移除该引用。

6. **未使用的 `System.Reactive`** —— 代码中零使用（无 `using`/`IObservable`/`Subject`），
   且其间接依赖触发 MSB3258，已移除。

7. **`PInvoke.dll` 架构不匹配** —— 该程序集是 x86 专用，而项目为 AnyCPU
   （需同时兼容 x64/x86 的 `SQLite.Interop`，故不能改为 x86）。
   主项目代码从未调用它，原 `<Reference>` 在 64 位进程下无法解析，
   是 MSB3270 / MSB3187 的来源。已改为 `<None>` 复制，保留文件供闭源 `QueryEngine` 运行时加载。

### 代码清理

8. **死代码文件** —— `WindowsDialog\Dialog_LoadPage.xaml.cs` 未纳入 csproj 编译
   （磁盘上有、`<Compile>` 里没有），且是无 XAML 配对的 `partial class`，
   `InitializeComponent()` 无法生成，任何引用都会编译失败。已移入 `_archive_removed\`。

9. **空壳 csproj** —— `Jvedio-WPF\Jvedio.csproj` 只声明 `AssemblyName=Jvedio`，
   无任何 `<Compile>`/`<Page>` 项，与真实工程同名易误认为入口。已移入 `_archive_removed\`。

### 修复后的告警状态

| 告警 | 修复前 | 修复后 |
|---|---|---|
| MSB3177（部分信任） | 28 | **0** |
| MSB3178（重复登记） | 12 | **0** |
| MSB3111（绑定重定向） | 1 | **0** |
| MSB3257 / MSB3258（ValueTuple） | 2 | **0** |
| MSB3270 / MSB3187（x86 架构） | 3 | 2（见下） |
| CS0612（过时 API，业务代码遗留） | 26 | 26（未处理） |
| CS4014（未 await，业务代码遗留） | 2 | 2（未处理） |

**剩余 2 条 MSB3270/MSB3187 无法在保留 `PInvoke.dll` 的前提下消除**：该文件是 x86 专用，
只要放在项目目录下就会被 MSBuild 扫描。实测 `ScanAllDrives()`（`QueryEngine` 的唯一入口）
**全项目零调用**，即这条依赖链在运行时永远不会触发。彻底消除需要删除 `QueryEngine`/`PInvoke`/
`UsnOperation` 三个文件及 `ScanAllDrives()` 方法，属功能范围变更，本次未做。

## 构建环境注意事项

**NuGet 缓存目录权限异常。** 本机 `%TEMP%\NuGetScratch` 与 `%LOCALAPPDATA%\NuGet\v3-cache` 无法被 nuget.exe 写入（报 "Unable to obtain lock file access" / "访问被拒绝"），但对应目录的 ACL 本身正常。`build.ps1` 已通过重定向 `TEMP`、`NUGET_PACKAGES`、`NUGET_HTTP_CACHE_PATH`、`NUGET_SCRATCH` 到 `_buildtools\` 规避。

手动执行 nuget 命令时需先设置同样的环境变量，否则会失败。

## 目录说明

```
Jvedio-src/
├─ build.ps1                  # 一键构建脚本
├─ _buildtools/               # nuget.exe 与各类缓存（可删除，脚本会重建）
├─ Jvedio-WPF/                # 唯一在维护的子项目
│  ├─ Jvedio/                 # 主程序：191 个 .cs，约 26,500 行；35 个 .xaml
│  ├─ Jvedio.Test/            # MSTest 测试（含 Appium UI 测试，依赖较重）
│  ├─ Document/               # 开发者文档、插件开发文档、Wiki 快照
│  └─ packages/               # NuGet 还原产物（git 忽略）
├─ Jvedio-Vue/                # 官网前端，与客户端无关
├─ Jvedio-Android/            # 空壳，仅一个 README
└─ Jvedio-Linux/              # 空壳，仅一个 README
```

## 闭源依赖（重要）

以下程序集**没有源码**，只能以二进制形式使用，无法修改其行为：

| 程序集 | 作用 | 影响 |
|---|---|---|
| `JvedioLib.dll` | 识别码提取 `Identify.GetVID`、`GetVideoType`、`IsCHS`、`IsHDV`；AES 加密 `Encrypt` | **核心能力**。已实测可用，见下文 |
| `SuperControls.Style.dll` | UI 控件库、主题、分页、插件框架 | 界面层改动受限于其公开 API |
| `SuperUtils.dll` | 工具库：ORM、IO、反射、网络 | 数据访问与工具方法受限于其公开 API |
| `QueryEngine.dll` | Everything 引擎封装（全盘扫描） | 全盘扫描功能依赖 |
| `PInvoke.dll`、`UsnOperation.dll` | USN 日志读取 | 增量扫描依赖 |

这些 DLL 是 GPL-3.0 项目的随附二进制，再分发授权存在不确定性；**本副本仅限自用，不做公开分发**。

> **`SuperUtils.dll` 的去向（2026-10-29 决定）**：改为用仓库内源码编译同名程序集替换闭源二进制。
> 因为 `SuperControls.Style.dll` 与 `CommonNet.dll` 本身以类型引用依赖它，文件级删除会让控件库加载失败。
> 已核实它未签名（无 `PublicKeyToken`），非强名称程序集按简单名绑定，自编版本可被正常绑定。
> 闭源库真实调用面仅 26 个类型 / 58 个成员。详见 [项目文档/闭源依赖替换.md](项目文档/闭源依赖替换.md)。

### 已验证：闭源识别码库工作正常

实测 `JvedioLib.dll` 的 `Identify.GetVID`（2026-10-24）：

| 输入 | 输出 VID | VideoType |
|---|---|---|
| `IPZ-229.mp4` | `IPZ-229` | 2 |
| `MIRD-190.mkv` | `MIRD-190` | 2 |
| `SSIS-001-C.mp4` | `SSIS-001` | 2 |
| `ABC-123C.wmv` | `ABC-123C` ⚠️ 未剥离尾部 `C` | 2 |
| `169bbs.com@IPZZ-882_[4K].mkv` | `IPZZ-882` | 2 |
| `FC2-PPV-1234567.mp4` | `FC2-1234567` | 1 |
| `no-id-video.mp4` | *(空)* | 0 |

`IsCHS("IPZ-229-C.mp4")` = True，`IsHDV(6GB)` = True，`IsHDV(500MB)` = False。

> ⚠️ `ABC-123C.wmv` 未剥离尾部 `C`，而 `SSIS-001-C` 正确剥离。这对应上游 issue #358 / #443 反馈的「识别码多一个 C 导致同步失败」。**根因在闭源 DLL 内，本副本无法直接修复**，只能在调用侧做后处理。

## 刮削器现状（最大功能缺口）

刮削能力全部由外部插件 DLL 提供，源码在 `hitchao/Jvedio-Plugin`（已归档，最后更新 2023-09-29）：

| 插件 | 站点 | 状态 |
|---|---|---|
| `bus` | Bus | 未验证，2023 年后站点大概率已变更 |
| `db` | DB | 上游 issue #410 反馈导演/评分失效 |
| `fc2` | FC2 | 未验证 |
| `library` | Library | 未验证 |

上游 issue #450（2026-02）明确反馈 javdb 已加强反爬导致刮削失败。**这类问题无法通过修改主程序解决**——插件是独立 DLL，需要重写爬虫并重新编译分发。

## 上游未关闭 issue 分布（119 个）

按主题粗分，供排期参考：

- **刮削/同步失败**（最多）：#181 #281 #283 #328 #343 #354 #361 #363 #364 #370 #371 #379 #393 #400 #403 #404 #410 #421 #438 #443 #450
- **图片/演员头像路径**：#204 #270 #336 #338 #361 #369 #413 #416 #436 #445
- **导入/扫描异常**：#297 #313 #335 #352 #380 #414 #444 #451
- **UI/性能/崩溃**：#280 #331 #365 #375 #399 #407 #435 #440 #448
- **功能建议**（分页记忆、批量编辑、导出等）：#241 #325 #346 #367 #430 #433 #434
- **识别码规则**：#286 #358 #424 #425

## 维护边界建议

| 可维护 | 需绕行 | 基本不可维护 |
|---|---|---|
| UI/交互、扫描导入流程、NFO 解析、数据库层、任务调度、日志、局域网 API | 识别码规则（可在调用侧后处理）、图片路径策略 | 刮削器（需重写爬虫 DLL）、Everything 全盘扫描内核 |

---

## 本轮改动记录（2026-10-24）

### 一、性能与稳定性

实测基准来自一个**约 2300 部影片**的库（有码类约 1350 + 短片类约 1000）、约 660 演员、约 200 类别、约 330 系列；封面图约 1350 张 BigPic + 1350 张 SmallPic + 580 张演员头像。

| 问题 | 位置 | 处理 |
|---|---|---|
| 交互路径上的 7 处强制 GC | `VieModel_Details.cs`、`Window_Details.xaml.cs`、`ImageSlide.cs`、`GifImage.cs`、`ImageCache.cs` | 全部移除。原实现在打开详情页、鼠标移出、切换预览图时调用 `GC.Collect()`，等于在 UI 线程上做全量阻塞回收 |
| 关联查询 N+1 | `AssociationMapper.cs`、`Video.cs`、`VieModel_VideoList.cs` | 改为一次性建双向邻接表 + BFS，新增 `GetAssociationDatasBatch()` 与 `Video.SetAssoBatch()`，渲染一页只查一次库 |
| 关联方向错误 | `AssociationMapper.cs` | 原实现只从 `MainDataID` 方向查找，导致 B 查不到 A。删除逻辑本来就按 `MainDataID or SubDataID` 双向删除，说明关联语义是无向的——已改为双向 |
| 图片缓存 key 冲突 | `ImageCache.cs` | key 加入 `DecodePixelWidth`（原来同一路径不同尺寸会互相串图）；`Clear()` 不再清空进程级 `MemoryCache.Default`（原来会连带清掉其他模块的缓存） |

> **实测结论（重要）**：`common_association` 表在真实库中为 **0 行**，即关联功能未被使用；标签表仅 3 条，`SetTagStamps` 实测 0.036 ms。因此 N+1 与标签优化对当前使用场景的**实际收益接近 0**，保留价值在于修正语义与防止未来踩坑。真正影响流畅度的是图片同步加载，见「已知未做」。

### 二、局域网 API（平板端地基）

新增 `Core/Lan/`（`LanServer.cs`、`HttpProtocol.cs`）与 `Entity/Dto/VideoDto.cs`，为平板端提供只读数据接口：

| 接口 | 说明 |
|---|---|
| `GET /api/libraries` | 影片库列表 |
| `GET /api/videos?page=&pageSize=&dbId=&keyword=&sort=` | 分页列表，含封面 URL、标签、类别、演员 |
| `GET /api/video/{dataId}` | 详情，含剧情、导演、片商、系列、演员详情 |
| `GET /image/{small\|big\|preview}/{dataId}` | 封面图 |
| `GET /image/actor/{actorId}` | 演员头像 |
| `GET /stream/{dataId}` | 视频流，**支持 HTTP Range**（拖动进度、断点续播） |

**关键技术决策**：不用 `HttpListener`。它在绑定 `+`（所有网卡）时需要管理员权限或预先注册 URL ACL，普通用户运行直接报「拒绝访问」（已实测复现）。改用 `TcpListener` 自行解析 HTTP，**无需任何特权**。

**实测结果**（真实库，普通用户权限）：

- 服务启动成功，监听 `0.0.0.0:5200`，对外地址 `http://192.168.3.32:5200`
- 列表：`TotalCount=2362`、`TotalPages=788`
- 详情：`IPZ-229` / 「制服美少女4本番 希島あいり」/ 6 个类别 / 演员「希島あいり」/ 导演「宇佐美忠則」
- 封面：small 13,836 字节、big 163,607 字节，均为合法 JPEG
- Range：`bytes=0-499` → 206、`bytes=50000000-50000999` → 206、开放区间 `bytes=100000000-` → 正确返回 `Content-Range: bytes 100000000-1613758983/1613758984`

**启用方式**：`app_configs.sqlite` 中 `WindowConfig.Settings` 的 `EnableLanServer`（默认 `false`）与 `LanServerPort`（默认 5200）。

> ⚠️ **需要手动放行防火墙**：首次启动时 Windows 会弹出「是否允许网络访问」，若误点取消，规则会被记为 **Block**。放行需管理员权限：
> ```powershell
> # 管理员 PowerShell
> New-NetFirewallRule -DisplayName 'Jvedio LAN Server' -Direction Inbound -Protocol TCP -LocalPort 5200 -Action Allow -Profile Private
> ```
> 未放行时本机可通过 `127.0.0.1` 访问，但平板等局域网设备连不上。

### 三、代码质量

| 项 | 处理前 | 处理后 |
|---|---|---|
| CS0612（过时 API） | 16 条 | **0** |
| CS4014（未 await） | 1 条 | **0** |
| MSB3270 / MSB3187（架构不匹配） | 2 条 | 2 条（无法消除，见下） |

- CS0612：`Movie`/`Rating`/`Jvedio4ToJvedio5` 等旧版类型仍被 NFO 解析、评分控件、4→5 迁移路径使用，属**不可删除的功能代码**。统一在 csproj 用 `<NoWarn>$(NoWarn);0612</NoWarn>` 抑制并注明原因。
- CS4014：`Jvedio4ToJvedio5.MoveDatabases` 中弹进度窗的 `Task.Run(() => Dispatcher.Invoke(...))` 改为 `_ = Dispatcher.BeginInvoke(...)`，语义更准确（原写法可能在迁移结束后才弹出窗口）。
- MSB3270/MSB3187 源于 `PInvoke.dll` 是 **x86-only**，而项目为 AnyCPU。该 DLL 仅被 `ScanAllDrives()` 使用，而该方法**无任何调用点**（`QueryEngine` → `PInvoke` → `UsnOperation` 整条链是死代码）。删除可清零警告，但属功能范围变更，暂未处理。

### 四、数据库并发保护

新增 `Core/DataBase/DataBaseGate.cs`，在 `BaseMapper<T>` 的全部读写入口加读写分离闸门：

- **读**（`Select`/`SelectList`/`SelectOne`/`SelectCount`/`SelectById`/`IsTableExists`）→ `EnterRead()`，可并发，列表与图片请求不互相排队
- **写**（`ExecuteNonQuery`/`InsertAndGetID`/`Init`/`Dispose`/`InitSqlite`）→ `EnterWrite()`，独占

用 `ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion)`，因为渲染循环里存在「读中读」的嵌套调用。

**实测**（真实库，40 路并发 25 个混合请求）：

| 指标 | 结果 |
|---|---|
| 并发 25 请求 | 927 ms，**25/25 成功** |
| 串行 30 次列表 | 67 ms（平均 2.2 ms/次），0 失败 |
| 列表 20 条 | 28 ms |
| 详情 | 8 ms |
| 大图（163 KB） | 7 ms |
| 进程状态 | 存活，内存 195 MB，无死锁 |

> 说明：本闸门只解决**同进程内**并发。跨进程（同时开两个 Jvedio）仍由 SQLite 文件锁处理。

### 五、启动假错误清零

原版每次启动都会写 3 条错误日志，本轮全部修掉：

| 错误 | 根因 | 修复 |
|---|---|---|
| `duplicate column name: PathExist` | `ALTER TABLE metadata ADD COLUMN PathExist` 在字段已存在时必然失败 | 执行前用 `PRAGMA table_info` 检查列是否存在（`MapperManager.IsAddColumnDuplicated`） |
| `duplicate column name: ImageUrl` | 同上，`actor_info.ImageUrl` | 同上 |
| `no such table: common_search_history` | 表名已改为复数 `common_search_histories`，但升级语句仍写单数；且 `SearchHistory` 实体根本没有 `TypeMode` 属性 | 删除这条无意义的遗留语句 |
| `未能找到路径"...\plugins\temp"` | `MovePlugins` 直接复制 `plugins\temp`，该目录只在插件更新时存在 | 复制前判断 `Directory.Exists` |

**验证**：启动新增 160 行日志，错误 **0 条**（修复前 3 条）。

### 六、async void 治理

全项目 32 个 `async void`，治理后剩 **26 个，全部受 `try-catch` 保护**（0 个裸奔）。

| 处理方式 | 数量 | 方法 |
|---|---|---|
| 改为 `async Task` + `SafeAsync.Fire` | 6 | `CheckServerStatus`、`AddItem`、`DeleteIDs`、`LoadVideoInfo`、`GetLabels`（`VieModel_Details` 与 `VieModel_Edit` 各一） |
| 保留 `async void`，方法体内加 `try-catch` | 26 | 见下 |

保留 `async void` 的原因分三种：

- **XAML 事件绑定**（签名必须是 `void`）：`DeleteFile`、`DeleteAllImage`、`DeleteImage`、`CreatePictureIndex`、`CreatePlayableIndex`、`RestoreDatabase`、`SaveInfo`、`StartServer`、`ShowActor`、`Window_Loaded`、`ExtraImageRadioButton_Click`、`ScreenShotRadioButton_Click`、`BaseWindow_ContentRendered`、`border_MouseLeftButtonDown`、`border2_MouseLeftButtonDown`
- **代码订阅 `TextChanged`**：`RefreshCandidate`
- **订阅闭源库事件**：`onSideTrigger`（订阅 `SuperControls.Style` 的 `OnSideTrigger`）

新增 `Core/Utils/SafeAsync.cs` 作为统一兜底：

- `SafeAsync.Fire(Func<Task>, tag)` — 发起后不关心结果，异常记日志不崩溃
- `SafeAsync.Run(Func<Task>)` — 等待并返回 `bool`
- `SafeAsync.Run<T>(Func<Task<T>>, fallback)` — 等待并返回结果

> **为什么重要**：`async void` 的异常不会进任何 `Task`，而是直接抛到 `SynchronizationContext`，最终触发 `AppDomain.UnhandledException` —— **程序崩溃**。原代码有 18 个 `async void` 完全没有 `try-catch`，其中 `Window_Loaded`（启动入口）也在内。
>
> `Window_Loaded` 的兜底额外加了 `MessageNotify.Error`，启动失败时让用户看见原因而不是静默退出。

### 七、实体分层：VideoRecord

新增 `Entity/Record/VideoRecord.cs`（155 行，**纯数据、零 WPF 依赖**）。

**为什么需要**：`Video : MetaData` 是「数据库映射 + WPF 绑定 + 运行时状态」三合一的胖实体，含 7 个不可序列化成员：

| 类别 | 成员 |
|---|---|
| `BitmapSource` | `SmallImage`、`BigImage`、`ViewImage` |
| `ObservableCollection` | `SeriesList`、`SubSectionList`、`PreviewImagePathList`、`PreviewImageList`、`AssociationList`、`TagStamp` |

这些让 `Video` 无法直接跨进程或跨平台传输。

**`VideoRecord` 的设计**：

- 42 个字段，与 `metadata` + `metadata_video` 两表的**全部可持久化列**一一对应
- 枚举以 `int` 存储（`DataType`、`VideoType`），避免跨程序集枚举版本耦合
- 提供 `Video.ToRecord()` / `Video.FromRecord()` 双向映射，字段完整无遗漏
- 派生属性 `HasSubSection` / `Playable` 为只读计算，不落库

**验证**：用脚本比对了 `Video`+`MetaData` 的全部可持久化属性（30 个）与 `VideoRecord` 字段，**全部覆盖**；`ToRecord` 赋值 38 个字段 + 2 个枚举转换，完整。

> 纠正了两处注释错误（原写成「0=未知 1=FC2 2=普通」），实际枚举为 `VideoType.Normal/UnCensored/Censored/Europe`、`DataType.Video/Picture/Game/Comics`。

### 八、抽取跨平台库 Jvedio.Core

新增 `Jvedio-WPF/Jvedio.Core/`，目标框架 **netstandard2.0**（28 KB）。

**设计边界**（写进 csproj 注释，防止后续误引）：

```
不得引用 SuperUtils / SuperControls.Style / JvedioLib（均为 net472 闭源程序集）
不得引用任何 System.Windows.* / WPF 类型
```

**纳入的代码**（源文件用 `link` 引用主项目，保持单一真相）：

| 类别 | 内容 |
|---|---|
| 枚举 | 19 个（DataType、VideoType、ImageType、PathType…） |
| 记录与 DTO | `VideoRecord`、`VideoDto`（含 `PagedResult<T>`、`ActorDto`、`LibraryDto`） |
| 基础设施 | `DataBaseGate`（读写锁）、`SafeAsync`（异步兜底）、`HttpProtocol`（HTTP 解析/响应） |
| 通用结构 | `ListNode`、`IDictKeys` |

**排除的文件及原因**（记录在 csproj 注释中）：

- `NotFoundException` —— 依赖闭源 `SuperControls.Style.LangManager` 做本地化
- `LanServer` —— 依赖 `Jvedio.Mapper` 与 Newtonsoft.Json，属应用层
- `Tables/Sqlite.cs` —— 建表 SQL 与具体数据库绑定
- 所有 `Config/*` —— 依赖 `AbstractConfig` 与 SQLite 读写，需先解耦配置存储

**遇到并解决的两个架构障碍**：

1. **MSB4041：SDK 风格项目与 MSBuild 2003 项目无法互相引用**
   主项目根节点带 `xmlns`（2003 格式），`Jvedio.Core` 是 SDK 风格。`ProjectReference` 直接失败。
   → 改为引用已构建的 DLL（`HintPath`），并在 `build.ps1` 中先构建 Core。

2. **CS0012：`netstandard` 类型在未引用的程序集中**
   net472 项目引用 netstandard2.0 库时，必须同时引用 `netstandard.dll` 门面程序集。
   → 从 NuGet 参考程序集包引入 `Facades\netstandard.dll`。

3. **CS0122：`HttpRequest`/`HttpResponse` 不可访问**
   这两个类原为 `internal`，跨程序集后主项目无法使用。
   → 改为 `public`（它们本就是协议层的公共契约）。

**关键验证——跨平台可用性实测**：

用一个 **net8.0 控制台项目**（模拟 Android / 平板端）引用 `Jvedio.Core`，全部通过：

```
枚举: DataType.Video=0, VideoType.Normal=0
VideoRecord: DataID=1971, VID=IPZ-229, HasSubSection=False
Clone 后一致: True
DTO: Tags=HD, Actors=希島あいり
DataBaseGate 读锁: OK
DataBaseGate 写锁: OK
SafeAsync: 未崩溃（异常已兜住）
=== 全部通过 ===
```

二进制验证：程序集目标框架为 `.NETStandard,Version=v2.0`，**零 WPF 引用**。

**接入方式**：主项目通过 `HintPath` 引用 `Jvedio.Core.dll`，`build.ps1` 已改为 5 步（第 4 步构建 Core）。运行时实测：DLL 正确复制到输出目录，启动 0 错误，API 返回 2362 部影片。

> `SafeAsync` 原直接调用 `App.Logger`，为迁入跨平台层改为注入式：`SafeAsync.ErrorHandler` 由桌面端 `App.Init()` 赋值，未注入时退回 `Console.Error`。

### 十、界面主题系统（莫兰迪浅绿 + 用户可自定义）

#### 10.1 为什么做 Token 体系

界面配色原本全部来自闭源 `SuperControls.Style.dll` 的 `DefaultColor.xaml`，
换主题只能改闭源库或全局搜替。

统计全项目实际引用的 **370 个 `DynamicResource` key**：

| 类别 | 数量 | 是否需要 Token 化 |
|---|---|---|
| 配色类（Background / Foreground / BorderBrush 结尾） | 35 | 需要 |
| 图标与文本资源（`OpenPath` / `Genre` / `Rating` 等） | 335 | 不需要 |

**结论：只有配色需要 token 化。** 图标本就该由主题库提供，纳入 token 体系只扩大维护面。

35 个配色 key 中，引用次数决定优先级：

```
Window.Foreground             112 次  ← 主文字/图标色
Window.Title.Background        36 次  ← 列表/标题栏背景
Window.Sub.Foreground          31 次  ← 次要文字
Window.Side.Background         27 次  ← 侧栏背景
Button.Selected.BorderBrush    19 次  ← 选中强调色
Window.Background              15 次  ← 窗口底色
Common.HighLight.Deep.Background 15 次
Menu.Background                10 次
Window.InnerDialog.Background   9 次
Control.Disabled.Background     8 次
```

这 10 个覆盖了 282 次引用，是观感的绝对主体。

#### 10.2 三层结构

新增 `CustomStyle/Tokens/`，四层字典：

| 文件 | 作用 |
|---|---|
| `Primitive.xaml` | 原始色值，只命名不表达用途 |
| `Semantic.xaml` | 语义别名 + 间距/圆角/字号/字重标度 |
| `Effects.xaml` | 磨砂玻璃模糊核、阴影、玻璃底色 |
| `Component.xaml` | 把 35 个闭源 key 映射到语义层 |

`Theme.xaml` 是唯一入口，`App.xaml` 只引用它。

**每个字典必须自己 merge 上游**：WPF 的 `StaticResource` 只能看到「自己的字典 + 自己 merge 进来的字典」，
看不到兄弟字典。少了这行会在启动时抛「无法找到名为 Primitive.* 的资源」。

#### 10.3 两个必须知道的实现陷阱

**陷阱一：运行时重建覆盖静态加载**

闭源库的 `ThemeSelectorDefault.InitThemes()` 会在启动后重建全部颜色资源，
把 `App.xaml` 里静态合并的字典全部覆盖掉。

→ 必须在 `InitTheme()` 末尾**再注入一次**（`Window_Main.ApplyDesignTokens()`）。

**陷阱二：替换被 `StaticResource` 引用的 key 会导致整个窗口白屏**

第一版实现遍历字典里所有 key 全部覆盖，结果窗口全白、`PrintWindow` 也抓不到内容。
二分定位后确认：某些 key 被控件模板以 `StaticResource` 引用，
运行期替换后模板无法重新解析，整个视觉树渲染失败。

→ 改为**白名单注入**：只覆盖项目中确认以 `DynamicResource` 引用的 23 个 key。
未在白名单内的 key 一律跳过。

> 这个坑排查成本很高：编译通过、进程正常、日志无错误、窗口句柄存在、DWM 未 cloaked，
> 唯一症状是截图全白。定位方法是「注释掉注入 → 界面恢复」的二分法。

#### 10.4 配色方案：莫兰迪浅绿

用户主用**浅色模式**（`ThemeIndex=1`），因此按浅色模式设计。

莫兰迪色系特征：低饱和（12%–28%）、明度接近、冷暖平衡。
主色选**灰调鼠尾草绿**（Dusty Sage `#87A48E`），因为绿色在低饱和时接近中性色，
在浅底上不会像蓝/紫那样发灰发脏。

| 用途 | 值 | 说明 |
|---|---|---|
| `bg.base` | `#F0F0EB` | 米灰画布，不用纯白避免刺眼 |
| `bg.subtle` | `#F7F7F3` | 标题栏/列表区 |
| `bg.raised` | `#FFFFFF` | 卡片/对话框 |
| `fg.primary` | `#3A3A35` | 深暖灰，不用纯黑 |
| `fg.secondary` | `#6E6E66` | 日期、副标题 |
| `accent` | `#87A48E` | **唯一**强调色（灰绿） |
| `accent.soft` | `#C9DBD0` | 选中态底色 |
| `accent.wash` | `#EDF3EF` | hover 底色 |
| `sidebar` | `#EDF3EF` | 侧栏/状态栏 |

浅色模式的层级方向与深色相反：**越上层越白**（base 最灰 → raised 纯白），
靠「白度递增 + 极浅边框」建立层次，而不是靠阴影。

#### 10.5 用户可自定义主题

新增 `Data/theme.json` + `Core/Theme/ThemeLoader.cs`。

用户只改 JSON 里的色值，重启即生效，无需改代码或重新编译。

```json
{
  "预设": "莫兰迪浅绿",
  "预设列表": { "莫兰迪浅绿": {...}, "莫兰迪浅粉": {...}, "莫兰迪浅蓝": {...}, "暖砂": {...} },
  "当前主题": { "accent": "#8095A3", "bg.base": "" },
  "界面": { "圆角": 6, "卡片圆角": 6, "阴影强度": 0.10 }
}
```

- `当前主题` 里填了值就用，留空则退回 `预设`
- 内置 4 套预设：莫兰迪浅绿 / 浅粉 / 浅蓝 / 暖砂
- 色值支持 `#RGB` / `#RRGGBB` / `#AARRGGBB`
- 任何一项解析失败只跳过该项，绝不导致启动失败

> 注意：`ThemeLoader` 同样受白名单约束，原因与 `ApplyDesignTokens` 相同。
> `阴影强度` 一项**不会**在运行期替换 `BlueDropShadowEffect`——
> 该 Effect 被卡片模板以 `StaticResource` 引用，替换同样会白屏。要调阴影请改 `Effects.xaml`。

**圆角 token 的真实生效范围**（实测确认，不是"定义了就算数"）：

`Semantic.xaml` 里定义的 `Radius.Sm/Md/Lg` 最初没有任何 XAML 引用，
即"定义了但没接线"，改 `theme.json` 的圆角不会生效。
已把 `ViewVideo.xaml` 的 4 处硬编码圆角改为 token 引用：

| 位置 | 原值 | 现引用 |
|---|---|---|
| 卡片主体（rootBorder） | `6` | `Card.CornerRadius` |
| 卡片主体（无模糊背景分支） | `6` | `Card.CornerRadius` |
| 封面图（只圆上两角） | `10 10 0 0` | `Card.CornerRadius.Top` |
| 标签角标 | `3` | `Radius.Sm` |

验证方法：把 `卡片圆角` 从 6 改成 14，重启后对两张截图做逐像素差分，
差异像素 35 个且集中在卡片左上角圆弧轮廓上——确认真正生效，而非只是写了配置。

#### 10.6 顺带修掉的两个真实 Bug

**Bug 1：封面图在独立机械盘上全部加载失败**

日志报 `BitmapImageFromFile/339 对路径"..."的访问被拒绝`，40 条/次启动。
但同进程用 `File.OpenRead` 能正常读取该文件，独立 WPF 测试也正常。

闭源 `ImageHelper.ReadImageFromFile` 内部以 `UriSource` 打开文件，
在 `I:` 盘（独立机械盘）上失败。

→ `ImageCache` 改为自己实现加载：`FileStream(FileShare.ReadWrite)` + `BitmapCacheOption.OnLoad`，
读完立即释放句柄。错误归零，封面正常显示。

**Bug 2：`loadFromRemoteSources` 缺失导致 `JvedioLib.dll` 静默失败**

日志报：
```
System.TypeInitializationException: "JvedioLib.Security.Identify" 的类型初始值设定项引发异常
 ---> FileLoadException: 未能加载文件或程序集"file:///E:\...\_devrun\JvedioLib.dll"
 ---> NotSupportedException: 尝试从一个网络位置加载程序集…请启用 loadFromRemoteSources 开关
```

`E:` 盘被 CLR 判定为「网络位置」，导致 VID 识别库加载失败。
表现是：界面正常，但凡是调用 `JvedioLib` 的功能静默失效——局域网 API 返回空响应、
识别码无法规范化。

→ `App.config` 的 `<runtime>` 加入 `<loadFromRemoteSources enabled="true" />`。
该开关只放开「加载」限制，不改变 CAS 沙盒语义。

#### 10.7 磨砂玻璃：为什么做在卡片上而不是侧栏

WPF 没有 CSS 的 `backdrop-filter`，`BlurEffect` 只能模糊**元素自身**，无法采样背后像素。
所以"磨砂玻璃"必须靠「下方有内容 + 上方半透明遮罩」这个组合才能成立。

**关键教训**：一开始把半透明加在侧栏上，取色验证发现完全没变化（`#EDF3EF` 前后一致）。
原因是侧栏背后就是纯色画布，没有可透出的内容——**半透明等于没透明**。

真正能成立的位置是**卡片信息区**：卡片 `Background` 本身就是一张大半径模糊的封面
（`rootBorder` 里的 `VisualBrush`），在它上面铺一层半透明白，就得到「模糊内容 + 半透明遮罩」的磨砂观感。

```xml
<Border Background="{DynamicResource Card.Info.Glass}"
        CornerRadius="{DynamicResource Card.CornerRadius.Bottom}"
        IsHitTestVisible="False" />
```

浓度调优（实测取色）：

| 不透明度 | 信息区取色 | 观感 |
|---|---|---|
| 0.72 (`#B8`) | `#F1F1EF` ~ `#FAFAF9` | 太亮，封面纹理被压住 |
| **0.55 (`#8C`)** | `#EAE7E5` ~ `#F6F6F4` | **纹理可见，文字清晰（采用）** |
| < 0.50 | — | 文字与封面混在一起，可读性差 |

验证方法：取信息区一行 7 个采样点，若颜色**各不相同**说明透出了下方内容（玻璃成立）；
若完全一致说明只是纯色遮罩（玻璃不成立）。

> 侧栏仍保留 `#E6EDF3EF`（90% 不透明）。当前没有背景图时看不出差别，
> 但用户设置背景图后，这 10% 的透出会让侧栏与背景产生联系，这是有意的设计余量。

#### 10.8 识别码等宽字体

`UI-REDESIGN.md` 的核心「非模板决策」：识别码用等宽字体，
让 `IPZ-229` / `MIRD-190` / `ABP-912` 在网格中形成**对齐的视觉列**。
扫视时眼睛可以沿固定位置快速定位编号，普通比例字体做不到。

实现：`ViewVideo.xaml` 的识别码 `TextBlock` 加 `FontFamily="{DynamicResource Font.Family.Code}"`，
值为 `Consolas, Cascadia Mono, Courier New`（按可用性降级）。
同时把日期字号从 15 降到 12、透明度 0.5→0.55，与识别码形成层级差
（原来看不出哪个是主信息）。

#### 10.9 其它界面调整

- **蓝色选中标识**：`PathGroupRadioButton` 的 `CheckedBrush` 原指向 `Button.Selected.BorderBrush`，
  该 key 在闭源主题里取自系统高亮色（Windows 蓝 `#0078D4`），不经过可覆盖的资源 key。
  已改为 `Semantic.Accent.Soft`（灰绿软色）。
- **蓝色发光阴影**：闭源 `BlueDropShadowEffect` 是蓝色辉光，与莫兰迪冲突。
  已在 `Effects.xaml` 覆盖为中性微投影（Opacity 0.10）。
- **卡片圆角**：10 → 6，媒体库应偏硬朗，大圆角是消费级 App 语汇。
- **卡片背景**：模糊封面背景不透明度降到 0.28，让封面成为唯一主角。
- **标签戳颜色**：HD 紫 `154,88,183` → 灰绿 `135,164,142`；
  Translated 绿 → 灰蓝 `128,149,163`；新加入 橙 `255,165,0` → 灰粉 `176,128,133`。
  （这三个颜色存在数据库 `common_tagstamp` 表，不在代码里）

### 十一、已知未做

1. **图片同步加载（当前最大流畅度瓶颈）**：`Video.SetImage` 在渲染循环里同步读盘解码，每张封面一次磁盘 IO + JPEG 解码，全部在 UI 线程。当前配置 `PageSize=10`，影响被限制在每页 10 张，但仍会卡。彻底解决需改为异步加载 + 占位图，涉及卡片模板与数据绑定改造，未在本轮实施。
2. **`Video` 仍兼作 UI 实体**：`VideoRecord` 已抽出纯数据层，但桌面端 UI 绑定的仍是 `Video`，尚未改为「`VideoRecord` 持有数据 + ViewModel 包装」。
3. **`Jvedio.Core` 覆盖范围有限**：Mapper、Config、Scan 等仍留在桌面端（依赖闭源 ORM 与 SQLite）。要让平板端真正复用业务逻辑，还需先把 `SuperUtils.dll` 的 ORM 替换为开源实现（如 `Dapper` + `Microsoft.Data.Sqlite`）。
4. **`JvedioLib.dll` 无法跨平台**：VID 识别逻辑仍锁在 net472 闭源 DLL 里，平板端若需本地识别须重写。
5. **主题的深色模式未适配**：Token 体系目前只写了浅色一套。若用户切到深色主题（`ThemeIndex=0`），
   会被莫兰迪浅色覆盖，观感可能不协调。要支持需按 `ThemeIndex` 分支加载不同的 Component 字典。
6. **闭源控件的系统色无法全部覆盖**：部分控件模板内部直接用 `SystemColors`，不经过任何资源 key。
   已知 `PathRadioButton` 的选中色已通过改 `CheckedBrush` 解决，但其它控件可能还有类似情况。
7. **`Data/theme.json` 缺少程序内编辑界面**：目前需手工编辑 JSON。可考虑在设置页加主题编辑器。
8. **侧栏选中竖条未做**：`UI-REDESIGN.md` 建议选中项左侧加 2px accent 竖条替代整块高亮背景。
   但 `PathRadioButton` 是闭源控件，模板在 DLL 内无法改结构，只能通过 `CheckedBrush` 改整块底色。
   当前整块底色效果已足够清晰，强行实现需重写控件模板，风险大于收益，未做。
9. **自适应列数无需改动**：`WrapPanel` 本身按容器宽度自动换行，列数由卡片宽度自然决定，
   已满足 `UI-REDESIGN.md` 的「自适应列数」要求。卡片宽度由 `VideoConfig.BigImage_Width`（250）控制。

### 十二、验证记录

每轮改动都经过实机验证：

| 验证项 | 结果 |
|---|---|
| 构建 | `build.ps1 -Clean` 全量重建 `EXIT: 0`，代码警告 0（仅剩 2 条 `PInvoke.dll` 架构警告） |
| Jvedio.Core 独立构建 | netstandard2.0，28 KB，0 警告 |
| Jvedio.Core 跨平台消费 | **net8.0 控制台实测通过**（枚举/记录/DTO/读写锁/异常兜底全部正常） |
| Jvedio.Core 二进制验证 | 目标框架 `.NETStandard,Version=v2.0`，零 WPF 引用 |
| VideoRecord 字段完整性 | 覆盖全部 30 个可持久化属性，`ToRecord` 赋值 38 字段 + 2 枚举转换 |
| 主题注入 | 23 个安全 key + 32 个用户自定义项，界面正常渲染（无白屏） |
| 主题加载器 | `Data/theme.json` 解析成功，4 套预设可选 |
| 封面图加载 | 错误从 40 条/次降至 **0 条**，封面正常显示 |
| 桌面端启动 | 正常，左侧统计 1352/56/667/207/329/126/207 全部正确 |
| 卡片渲染 | 封面图、识别码、日期、星级、标题、标签戳全部正常 |
| 局域网 API | 2362 部影片、788 页、详情/图片/Range 全部通过 |
| Range 请求 | `bytes=0-499`→206、`bytes=50000000-50000999`→206、开放区间正确 |
| 并发 | 25 请求 927ms 全成功，无死锁 |
| 启动日志 | 0 条非网络错误（修复前每次启动 3 条假错误 + 40 条图片错误） |

> **本轮踩坑记录（供后续参考）**：主题覆盖第一版导致整个窗口白屏。
> 排查过程：编译通过 → 进程正常 → 日志无错误 → 窗口句柄存在 → DWM 未 cloaked → 截图全白。
> 定位方法：注释掉注入调用，界面恢复 → 确认是运行时替换资源导致。
> 根因：部分 key 被控件模板以 `StaticResource` 引用，运行期替换后模板无法重新解析。
> 解法：白名单注入，只覆盖确认以 `DynamicResource` 引用的 key。

> 防火墙放行需管理员权限，测试环境未能自动化完成。首次运行若误点「取消」，规则会被记为 Block，需手动执行文档中的 `New-NetFirewallRule` 命令。

---

*本文档由维护过程实测生成，环境：Windows / .NET Framework 4.8 运行时 / .NET SDK 10.0.203，2026-10-24。*

### 六、手机网页版界面（本轮新增）

`Resources/Web/index.html` 单文件实现，无外部依赖（离线可用）。

**自适应方案**：不用断点阶梯，改 `clamp()` + `auto-fill` + `minmax()` 流式缩放。
实测 9 档尺寸（360→2560px）全部无横向溢出，列数 2→7 连续过渡，间距 8→20px 平滑缩放。

**新增 API**：

| 接口 | 说明 |
|---|---|
| `GET /api/random?count=&dbId=&strategy=` | 加权随机推荐（非 `ORDER BY RANDOM()`） |
| `GET /api/similar/{dataId}?count=` | 相似推荐：共同演员 → 同系列 → 同发行商 → 随机兜底 |
| `GET /api/filters` | 筛选维度与取值（分类/发行商/系列/导演/演员，含频次） |
| `POST /api/rate/{dataId}?grade=` | 设置评分（0 = 取消收藏） |
| `POST /api/watched/{dataId}` | 记录观看（ViewCount+1、写 ViewDate） |

`GET /api/videos` 增加 `view=favorite|recent`（与桌面端语义一致：收藏 = `Grade > 0`，
最近播放 = `ViewDate` 在 30 天内）与 `genres/studios/series/directors/actors` 筛选参数。

**随机推荐为什么不用 `ORDER BY RANDOM()`**：实测 3.9ms（2362 条），性能不是问题；
问题在于纯随机每次顺序完全无关，用户刷新两次会看到大量重复内容，且冷门热门同等对待。
改为「随机 offset 取样 + 去重」（实测 1.7ms），保证多样性的同时避免重复推送。

**播放器为什么自写控件**：`<video controls>` 的原生控件在移动端（Android Chrome / 鸿蒙）
进入全屏后几秒自动隐藏，进度条和快进随之消失——这是用户实际遇到的问题。
不引入 Plyr / video.js 的原因：手机通过局域网 IP 访问，离线时够不到 CDN，
内联又要 100KB+ 且定制成本不低于自写。自写控件层（约 260 行）可精确控制
进度条拖动、±10 秒、倍速、全屏，且全屏下控件依然可用（已实测）。

### 七、缺封面自动抽帧

短片（文件名不含识别码）通常没有配套封面，直接 404 会导致界面大片空白。

`LanServer.TryExtractCoverFrame()` 在封面缺失时调用 FFmpeg 抽帧：

- 跳过开头 30 秒（避开片头广告，实测这批素材广告多在 10-20 秒）
- 只在前 50% 时长内随机取点（后半段常是片尾）
- 结果缓存到 `Cache\cover\{dataId}.jpg`，同一视频只抽一次
- 并发保护：`ConcurrentDictionary` 保证同一条视频不会被多线程重复抽帧

实测三条缺封面记录全部成功（0.24-0.26 秒），抽出的帧为有效画面而非黑屏/广告。

### 八、本轮修复的两个真实缺陷

| 缺陷 | 原因 | 处理 |
|---|---|---|
| 所有错误响应都是 HTTP 200 | `WriteError` 调 `WriteJson` 时状态码硬编码 200，错误码只写在 body 里，客户端与 curl 无法靠状态码判断失败 | `WriteJson` 增加带 `status` 的重载，`WriteError` 改用之。实测 405/404/200 全部正确 |
| 无识别码的片子显示「未知编号」 | 短片 VID 为空，前端只认识别码 | `VideoSummaryDto` 增加 `FileName` 字段，前端按「识别码 → 标题 → 文件名」逐级兜底 |

### 九、数据一致性

桌面版「我的收藏」= `Grade > 0`（不是独立的收藏表，`FavoriteCount` 字段实际全为 0）。
手机端的收藏按钮即「打 5 星」，与桌面版共用同一字段，数据天然一致。

实测：手机端点收藏后，`Grade` 立即写入，桌面版「我的收藏」数量同步 +1。

### 十、全设备自适应与播放器修复（第二轮）

#### 10.1 右侧卡片被截断（真因是 vw 单位）

**现象**：桌面常规比例下，最右一列卡片贴边、视觉上像被切掉。

**根因**：所有流式尺寸用 `vw` 计算。`vw` 是「视口宽度的 1%」，**包含滚动条占的 15-17px**。
一旦出现纵向滚动条，按 `vw` 算出来的尺寸就比真实可用宽度大，最右列因此溢出。

**修复**：改用**容器查询单位 `cqi`**。

```css
.app {
  container-type: inline-size;   /* 声明查询容器 */
  container-name: app;
  --u: 1vw;                      /* 老浏览器兜底 */
  --card-min: clamp(136px, calc(9 * var(--u) + 100px), 200px);
}
@supports (container-type: inline-size) {
  .app { --u: 1cqi; }            /* 支持时用容器宽度单位 */
}
```

**踩到的坑（重要）**：变量必须定义在 `.app` 而不是 `:root`。
`cqi` 是「查询容器宽度的 1%」，`:root` 不在任何查询容器内，`cqi` 无参照，
整串变量会静默失效（`getPropertyValue` 返回空串），表现是所有宽度都退化成 1 列。

另外在 `html` 上加 `scrollbar-gutter: stable`，让有无滚动条时布局宽度一致，避免加载时跳动。

#### 10.2 小屏 320px 只有 1 列

`--card-min` 下限原为 140px，2 列需要 `140×2 + gap 8 = 288px`，
而 320px 视口减去 padding 后内容宽只有 283px，差 5px 凑不出 2 列。
下限降到 **136px**（`136×2 + 8 = 280 < 283`），320px 也能 2 列。

#### 10.3 顶栏过高

顶栏原有 4 行（logo / 搜索 / 库 / 视图标签），实测 193px：
桌面占 18%，横屏手机（568×320）占 **54%**，可用内容只剩一半。

**修复**：
1. 「库」与「视图标签」合并到一行（两者都是切换列表内容的控件，同排更符合直觉）
2. 横屏矮视口（`max-height: 560px` + `landscape`）额外压缩内边距、输入框高度、按钮尺寸

实测 193px → **144px**（-25%），横屏手机 54% → 41%。

#### 10.4 非全屏时进度条跑到画面顶部

`.vc` 用 `justify-content: space-between`，而非全屏时 `.vc-top` 是 `display:none`，
容器里只剩 `.vc-bottom` 一个流式子元素，`space-between` 把它推到了最上面。

**修复**：改用 `justify-content: flex-end` 让底部条自己贴底，
`.vc-top` 用 `margin-bottom: auto` 推回顶部。这样与顶栏是否隐藏无关。

#### 10.5 退出播放页卡死

原实现 `videoWrap.innerHTML = ''` 直接移除正在播放的 `<video>`。
浏览器会在解码线程上做清理，部分移动端（Android Chrome / 鸿蒙 WebView）会因此卡住。

**修复**（顺序是关键）：
1. 先切界面（`playerPage.classList.remove('show')`）——让用户立刻看到响应
2. `setTimeout(0)` 推迟到重绘之后再做释放
3. 规范释放：`pause()` → 移除 `src` → `srcObject = null` → `load()` → 再移除 DOM

#### 10.6 返回键行为

原来只监听 `popstate` 但从不 `pushState`，所以手机按返回键会直接退出整个网站，
而播放页还开着。现在 `openPlayer` 压入一条历史记录，
返回键即关闭播放页；主动点关闭时用 `history.back()` 回退，避免残留历史条目。

#### 10.7 全设备实测结果（16 种尺寸）

| 设备 | 尺寸 | 列数 | 卡片宽 | 右侧余量 | 横向溢出 |
|---|---|---|---|---|---|
| iPhone SE 竖 | 320×568 | 2 | 138 | 11px | 0 |
| 安卓小屏 | 360×780 | 2 | 157 | 11px | 0 |
| iPhone 14 竖 | 390×844 | 2 | 172 | 11px | 0 |
| iPhone ProMax | 430×932 | 2 | 191 | 12px | 0 |
| iPhone SE 横 | 568×320 | 3 | 168 | 14px | 0 |
| iPhone 14 横 | 844×390 | 4 | 188 | 19px | 0 |
| 折叠屏 | 600×960 | 3 | 178 | 15px | 0 |
| iPad 竖 | 768×1024 | 4 | 170 | 17px | 0 |
| iPad 横 | 1024×768 | 4 | 230 | 21px | 0 |
| iPad Air 横 | 1180×820 | 5 | 209 | 24px | 0 |
| 笔记本 | 1280×720 | 5 | 228 | 24px | 0 |
| 笔记本 768p | 1366×768 | 6 | 201 | 24px | 0 |
| MacBook | 1440×900 | 6 | 213 | 24px | 0 |
| 桌面 FHD | 1920×1080 | 7 | 227 | 97px | 0 |
| 桌面 2K | 2560×1440 | 7 | 227 | 417px | 0 |
| 超宽屏 | 3440×1440 | 7 | 227 | 857px | 0 |

#### 10.8 桌面版设置界面

在「设置 → 基本」新增「手机网页版」区块：

- 启动开关（`EnableLanServer`）与端口输入（`1024-65535`，默认 5200）
- 实时显示访问地址，带「复制」按钮
- 功能说明（能做什么、怎么用、防火墙提示）
- 端口只允许数字输入；非法值自动退回 5200
- 改动后提示「重启程序后生效」

IP 检测用「连一个外部地址」判断默认出口网卡，比遍历网卡更准（能自动排除虚拟网卡、VPN）。

### 十一、卡片网格重做（第三轮）

#### 11.1 三个真实问题

用户反馈「卡片不是动态排序的、要跟随浏览器比例动态排版、尽量显示完整」。

| 问题 | 根因 | 修复 |
|---|---|---|
| 卡片高度参差 | `align-self: start` 让每张卡独立定高，内容少的卡片（无日期/标题）矮一截 | 改 `height: 100%` 让同行等高，信息区 `flex: 1` 撑满剩余空间 |
| 封面被裁 | 素材实际比例 **0.735:1**，CSS 写的 `3/4`（0.75） | 改成 `735 / 1000`，与素材精确吻合 |
| 右侧留白不均 | `justify-content` 默认 `normal`，余量全堆右侧 | 加 `justify-content: center`，余量均分两侧 |

#### 11.2 封面比例是实测出来的

```
SmallPic 共 1349 张，随机抽样 500 张：
中位 0.7350  众数 0.73（450/500）  最小 0.7034  平均 0.7320
```

原来写 `aspect-ratio: 3/4`（0.75）会横向裁掉约 2%。改成 `735/1000` 后 `object-fit: cover`
几乎不产生裁剪，封面内容完整可见。

顺带记录素材规格差异（重要）：

| 目录 | 比例 | 张数 |
|---|---|---|
| `I:\封面截图\BigPic` | **1.48:1（横版）** | 1350 |
| `I:\封面截图\SmallPic` | **0.735:1（竖版）** | 1349 |

两者比例完全不同。列表页用 SmallPic（竖版，与卡片匹配）；
若误用 BigPic，横版图放进竖版框会被裁掉约 50%。

#### 11.3 试过但否掉的方案

| 方案 | 问题 |
|---|---|
| `minmax(MIN, 1fr)` + `auto-fill` | `1fr` 把列拉满，卡片宽度跳变；内容少的卡被撑出空白 |
| `minmax(MIN, MAX)` + `auto-fit` | 列数由上限 MAX 决定，MIN 失效——窄屏退化成 1 列，2560px 右侧空出 533px |

最终方案：`auto-fill + minmax(var(--card-min), 1fr)` + `justify-content: center`。
列数由 `--card-min` 唯一决定，卡片宽度随容器连续缩放，余量居中。

#### 11.4 缓存头（导致「改了看不到」的真凶）

服务端原来所有响应都不发缓存控制头，浏览器按启发式规则长期缓存 HTML。
**这是用户反复说「还是不对」的根本原因——他看到的其实是旧页面。**

修复：
- `WriteHeader` / `WriteBinary` 增加 `cacheControl` 参数
- HTML：`no-store, must-revalidate`（每次回源）
- 其它静态资源：`public, max-age=300, must-revalidate`（短期缓存）
- 不传参时默认 `no-store`，安全兜底

实测验证：`Cache-Control: no-store, must-revalidate` 已生效。

#### 11.5 最终实测（14 种宽度）

| 宽度 | 每行列数 | 卡片宽 | 卡片高 | 封面比例 | 左余量 | 右余量 | 对称 | 行内等高 | 溢出 |
|---|---|---|---|---|---|---|---|---|---|
| 320 | 2 | 138 | 230 | 0.735 | 11 | 11 | ✓ | ✓ | 0 |
| 390 | 2 | 172 | 276 | 0.735 | 12 | 11 | ✓ | ✓ | 0 |
| 480 | 2 | 215 | 335 | 0.735 | 13 | 13 | ✓ | ✓ | 0 |
| 600 | 3 | 178 | 342 | 0.735 | 15 | 15 | ✓ | ✓ | 0 |
| 768 | 4 | 170 | 334 | 0.735 | 17 | 17 | ✓ | ✓ | 0 |
| 900 | 4 | 201 | 377 | 0.735 | 19 | 19 | ✓ | ✓ | 0 |
| 1024 | 4 | 230 | 417 | 0.735 | 21 | 21 | ✓ | ✓ | 0 |
| 1180 | 5 | 209 | 389 | 0.735 | 23 | 24 | ✓ | ✓ | 0 |
| 1280 | 5 | 228 | 415 | 0.735 | 24 | 24 | ✓ | ✓ | 0 |
| 1366 | 6 | 201 | 377 | 0.735 | 24 | 24 | ✓ | ✓ | 0 |
| 1440 | 6 | 213 | 394 | 0.735 | 24 | 24 | ✓ | ✓ | 0 |
| 1600 | 7 | 202 | 380 | 0.735 | 24 | 24 | ✓ | ✓ | 0 |
| 1920 | 7 | 227 | 414 | 0.735 | 96 | 97 | ✓ | ✓ | 0 |
| 2560 | 7 | 227 | 414 | 0.735 | 416 | 417 | ✓ | ✓ | 0 |

列数跳变点实测：1560px（7→6）、1360px（6→5）、1120px（5→4）、720px（4→3）。
相邻档之间卡片宽度平滑变化（如 202→233、206→243），无突兀跳变。

### 十二、详情页 / 视图状态 / 剪贴板 / 提示组件（第四轮）

#### 12.1 卡片点击分区

用户要求：点封面直接播放，点文字区进详情页看介绍和多张截图。

```js
cover.addEventListener('click', e => { e.stopPropagation(); openPlayer(item.DataID); });
info.addEventListener('click',  e => { e.stopPropagation(); openDetail(item.DataID); });
```

要点：两个子区域各自绑事件并 `stopPropagation`，**不能**在 card 上绑一个统一监听
再判断 `e.target`——封面里还有 `<img>`、徽标等子元素，判断容易漏。

#### 12.2 详情页内容

参考桌面版详情窗口，包含：封面 + 标题 + 发行/导演/系列 + 时长/大小 + 观看数 +
播放按钮 + 评分收藏 + 简介 + 类别 + 标签 + 演员头像 + 内容截图 + 相似推荐。

宽屏下正文限制 `max-width: 900px` 并居中——详情页是阅读型页面，
一行拉满 1400px 会让眼睛来回扫。

#### 12.3 内容截图的三级来源

| 优先级 | 来源 | 说明 |
|---|---|---|
| 1 | `Cache/preview/{DataID}/N.jpg` | 本地已有截图，最快 |
| 2 | `/image/shot/{id}/{n}` | 服务端代理下载库里的远程截图并缓存 |
| 3 | `/image/frame/{id}/{n}` | 从视频抽帧（跳过片头，4 段均匀取点） |

**为什么必须走服务端代理，不能直接给前端远程 URL**（实测数据）：

- DMM 图片单张耗时 **0.8–4.6 秒**，直连时详情页出现一排空白灰块
- 部分图站有防盗链，浏览器带 Referer 才给图，手机直接请求会被 403
- 手机可能只连局域网没外网，直连 URL 必然失败

代理后：服务端下载一次落盘缓存，之后手机从局域网秒开。
实测 IPZ-229 的 12 张截图全部加载成功（`shotsOk: 12, shotsFailed: 0`）。

下载细节：带浏览器 UA + Referer 绕防盗链；先写 `.tmp` 再改名，
避免半截文件被当成有效缓存。

#### 12.4 视图状态不能退出（用户报的核心 bug）

**现象**：点了「我的收藏」，再点其它库或搜索，收藏条件还在，出不去。

**根因**：`reset()` 只重置分页，不清 `state.view`。
`state.view`（全部/收藏/最近播放）与 `state.dbId`（库）、`state.keyword`（搜索）
是**不同维度**的筛选，叠加后结果越来越窄。

**修复**：给 `reset(exitView)` 加参数，语义是「用户是否主动换了浏览范围」：

| 操作 | exitView | 理由 |
|---|---|---|
| 切换库 | `true` | 换了数据范围 |
| 搜索 | `true` | 搜索是全局的，不该被收藏限制 |
| 应用筛选 | `true` | 精细条件，叠加会出空结果 |
| 点视图标签 | `false` | view 由点击目标决定，不能清 |
| 随机推荐 | 主动清 view | 与收藏/最近播放互斥 |

`exitView` 时同时同步标签高亮，否则数据变了但「我的收藏」还亮着。

实测：全部 2,362 → 收藏 89 → 切库后自动回全部 2,362 → 收藏 89 → 搜索 MIDV 55（视图退出）。
随机推荐也正确退出视图并保持 active 可换批。

#### 12.5 复制地址失败（CLIPBRD_E_CANT_OPEN）

日志实证：
```
[E] [CopyLanAddress/417] 复制地址失败: OpenClipboard 失败
    (异常来自 HRESULT:0x800401D0 (CLIPBRD_E_CANT_OPEN))
```

Windows 剪贴板是**全局独占资源**，任何进程（输入法、剪贴板管理器、截图工具）
持有它时 `Clipboard.SetText` 会立刻抛异常。

修复：重试 8 次、每次 60ms（总计约 0.5 秒）。这个窗口足够跨过瞬时占用，
又不会让界面明显卡顿。实测单独调用一次即成功并读回一致。

#### 12.6 局域网启动弹窗太丑（自绘 Toast）

闭源库的 `MessageNotify` 是**独立窗口 + 固定配色**，与项目的莫兰迪浅绿 +
磨砂玻璃语言不搭，且位置跟随屏幕而非主窗口（多显示器会跑偏）。

新增 `Core/UserControls/Toast.xaml`：

- 颜色全部走项目 Token（`Glass.Surface.Deep` / `Semantic.Success` 等）
- 绝对定位于主窗口顶部居中，永不跑出窗口
- `IsHitTestVisible="False"`，绝不挡操作
- 同一时刻只显示一条，新提示顶掉旧的
- 动画只做位移 + 透明度（不用 BlurEffect，大面积模糊会掉帧）
- 自动沿 `Application.Current.MainWindow` 找根 Grid 挂载

调用：`Toast.Success("手机网页版已启动：http://192.168.3.32:5200")`

#### 12.7 详情页播放的层级问题

从详情页点播放时，必须先 `closeDetail()` 再开播放页。
两层都是 `position: fixed` 全屏，叠在一起会让返回键历史对不上
（点一次返回只退一层，用户以为没反应）。用 180ms 延迟错开两层过渡。

#### 12.8 本轮验证结果

| 项目 | 结果 |
|---|---|
| 封面点击 → 播放页 | ✓ |
| 文字区点击 → 详情页 | ✓ |
| 详情页 12 张截图加载 | ✓ 12/12 成功 |
| 详情页播放 → 播放页（详情自动关） | ✓ |
| 关闭播放页 → 回列表，video 释放 | ✓ videosLeft: 0 |
| 视图互斥（收藏/最近/库/搜索/随机） | ✓ 全部正确 |
| 控制台错误 | ✓ 0 |

### 十三、同类问题系统排查（第五轮）

用户问「同类问题都排查过了吗」，于是把所有「状态改了但界面/历史/资源没同步」的路径逐条过了一遍。
查出 **6 个同类缺陷**，全部修复。

#### 13.1 重置筛选不重载列表

`sheetReset` 只清 `state.filters` 和按钮高亮，不重新请求。
用户点「重置」看到列表没变，必须再点「应用」才生效。

修复：重置后直接 `reset(true)`（面板保持打开，方便继续调整）。

#### 13.2 历史记录堆积（三处）

**问题本质**：`closeXxx()` 里 `history.back()`，紧接着 `openXxx()` 里 `history.pushState()`，
形成「回退+前进」。部分移动浏览器会因此丢失历史栈，返回键彻底失效。

| 场景 | 原实现 | 修复 |
|---|---|---|
| 详情页 → 播放 | `closeDetail()` + `openPlayer()` | `openPlayer(id, true)` 用 `replaceState` |
| 播放页内点相似 | `closePlayer()` + `openPlayer()` | 新增 `switchPlayerVideo()`，只换内容不碰历史 |
| 详情页内连点相似 | 每次都 `pushState` | 已打开时改用 `replaceState` |

实测：列表→详情→播放→换片全程 `history.length` 保持 3，
按一次返回即回到列表。

#### 13.3 背景滚动锁泄漏

详情页、播放页、筛选面板都直接写 `body.style.overflow`。
多个浮层叠加时，先关闭的那个会把锁清掉，导致还有一层开着但背景能滚。

改为**引用计数**：

```js
var scrollLocks = 0;
function lockScroll()   { scrollLocks++; document.body.style.overflow = 'hidden'; }
function unlockScroll() { scrollLocks = Math.max(0, scrollLocks - 1);
                          if (scrollLocks === 0) document.body.style.overflow = ''; }
```

配套两处防重复：
- `openDetail` 已打开时不再 `lockScroll()`（否则锁计数永远归不了零）
- `closeDetail` / `closePlayer` 开头判断 `classList.contains('show')`，已关就直接 return

实测：连点两张卡、连击三次关闭按钮、打开后立刻关闭，overflow 均正确恢复。

#### 13.4 并发请求竞态（两处）

快速连点两张卡，两个 `/api/video/{id}` 并发飞行，先发的可能后返回，
把已经显示的新内容覆盖成旧的。

修复：请求令牌。

```js
var detailToken = 0;
var token = ++detailToken;
fetch(...).then(function (d) {
  if (token !== detailToken) return;   // 期间又点了别的，本次作废
  renderDetail(d);
});
```

`closeDetail` / `closePlayer` 也自增令牌，让飞行中的请求失效——
否则关闭后迟到的响应会往已清空的容器里塞回内容，表现为「关了又自己打开」。

实测：连点两卡，最终显示的是第二张（`t38-002-4k`），不是第一张。

#### 13.5 短片标题显示 `#4521`

详情页/播放页标题的回退链是 `VID → Title → '#' + DataID`，
但短片没有识别码、也没有 Title，于是显示成 `#4521`。

根因是 `ToDetail()` 漏传 `FileName`（`ToSummary()` 里有，复制字段时漏了）。

修复：
1. `ToDetail` 补 `FileName = s.FileName`
2. 前端五处标题统一回退链：`VID → Title → fileNameOf(FileName) → '#' + DataID`

涉及位置：详情页头、详情页封面 alt、播放页头、播放器控件标题、相似推荐卡片。

#### 13.6 详情页层级低于播放页

`.detail-page` 是 `z-index: 150`，`.player-page` 是 `200`。
详情页总是较后打开的一层，层级反了会看到播放页被压住一闪。
改为 `210`。

#### 13.7 死代码

`backToList()` 定义了但无人调用，且内部 `reset()` 不传参（会漏清 view）。
直接删除。

#### 13.8 本轮验证矩阵

| 测试 | 结果 |
|---|---|
| 连点两张卡（80ms 间隔） | 显示第二张 ✓ |
| 连点后关闭，overflow 恢复 | ✓ 可滚动 |
| 打开后 50ms 立刻关闭 | 迟到响应不渲染 ✓ |
| 三连击关闭按钮 | overflow 正常 ✓ |
| 详情→播放→换片→返回 | history 不堆积，一次返回回列表 ✓ |
| 详情页内连点 3 次相似 | history 保持 3 ✓ |
| 短片标题 | 显示文件名，不再是 `#4521` ✓ |
| 控制台错误 | 0 ✓ |

### 十四、详情页滚动与宽屏排版（第六轮）

用户反馈两点：详情页滚不动、电脑浏览器右侧空出一大片。

#### 14.1 详情页滚不动（根因：flex 子项 min-height）

`.detail-body` 已经是 `flex: 1; overflow-y: auto`，看起来没问题，
但实测 `scrollHeight === clientHeight`，滚动条永远不出现。

**根因**：flex 子项默认 `min-height: auto`，含义是「不能比内容更矮」。
内容一多就把容器撑破，`overflow-y: auto` 没有触发条件。

```css
.detail-body {
  flex: 1;
  min-height: 0;      /* ← 关键，缺这一行就滚不动 */
  overflow-y: auto;
}
```

这是 flex 布局的经典坑：**只要在 flex 子项上用 `overflow`，
就必须同时设 `min-height: 0`**（横向滚动则是 `min-width: 0`）。

实测修复前后：`maxScroll` 从 0 → 831px（1440×900 视口）。

#### 14.2 右侧空出一大片（三处独立原因）

排查时先用 `getBoundingClientRect()` 量出每个元素的实际宽度，
再逐个定位，不靠观感猜。

| 位置 | 问题 | 修复 |
|---|---|---|
| 正文容器 | `max-width: 900px`，1440 屏左右各空 270px | 放宽到 1400px |
| hero 信息区 | `flex: 1` 撑到 962px，内容只有几行短文本 | 宽屏 `max-width: 680px` |
| 播放按钮 | `flex: 1` 被拉成 700px 长条 | 宽屏固定 220px |

`max-width` 取值经过两轮修正：
- 900px → 1200px：仍空 120px，不够
- 1200px → 1400px：1440 屏只留 12/28px，基本铺满

hero 的 680px 也是修正过的：先写 760px，但 768px 屏上 760 ≈ 全宽，等于没设。

#### 14.3 相似推荐横向裁切

`.similar-row` 原本是 `overflow-x: auto` 的横向滚动条。
手机上是合理交互，但**桌面端既看不到滚动提示、也不好用鼠标拖**，
12 张卡被硬切掉右半张，看起来像排版坏了。

```css
@media (min-width: 720px) {
  .similar-row { display: grid; grid-template-columns: repeat(auto-fill, minmax(120px, 1fr)); }
}
@media (min-width: 1024px) {
  .similar-row { grid-template-columns: repeat(6, 1fr); }  /* 12 张正好 2 行 */
}
```

固定 6 列的原因：auto-fill 在 1024px 会排成 7 列，12 张变成「7+5」，
最后一行空一半，比横向滚动更难看。

#### 14.4 内容截图列数

`auto-fill` 在 1400px 排到 7 列（7+5 尾行），在 320px 又只剩 1 列。
改为：移动端下限 120px（320 屏能排 2 列），宽屏固定 6 列（12 张 2 行排满）。

下限从 140px 降到 120px 的原因：320 屏可用宽 273px，
`140×2+8 = 288 > 273` 放不下两列，`120×2+8 = 248` 可以。

#### 14.5 短片标题空白

hero 只渲染 `d.Title`，短片（无识别码、无标题）就只剩「大小/观看次数」两行，
右边一大片空白。改为 `d.Title || fileNameOf(d.FileName)`。

#### 14.6 六档宽度验证

| 宽度 | hero | 截图 | 相似推荐 | 可滚动 | 右侧溢出 |
|---|---|---|---|---|---|
| 320 | 258px | 2 列 125px | 横向滑 | ✓ | 无 |
| 390 | 328px | 2 列 160px | 横向滑 | ✓ | 无 |
| 768 | 680px | 6 列 111px | 5 列 | ✓ | 无 |
| 1024 | 680px | 6 列 154px | 6 列 | ✓ | 无 |
| 1440 | 680px | 6 列 215px | 6 列 | ✓ | 无 |
| 1920 | 680px | 6 列 215px | 6 列 | ✓ | 无 |

#### 14.7 排查方法备忘

这轮起初用「内容总高 718 < 容器 845」判断「不需要滚动」，
差点误判成没问题——**数据对不代表观感对**。
后来改成：先截图看实际画面，再量具体元素，两者对照才定位到
「hero 撑满 962px 但内容只有两行」这种纯数值看不出来的问题。

### 十五、滚动问题的完整排查（第七轮）

用户反馈「刚进详情页还是无法滚动，要加载完才能滚」。

#### 15.1 表面已修好，实测仍有延迟

上一轮加了 `min-height: 0` 后，静态测量 `canScroll: true`，
但用户体感仍然「要等一下才能滚」。说明静态测量不够，
必须做**时序采样**：点击后每隔 150/300/500/800/1200ms 记录
`scrollHeight` 与「此刻能否滚动」。

限速到 200KB/s 后复现出真实曲线：

```
t=1     845px   不能滚   (只有 loading)
t=300   865px   能滚     ← 已改善
t=800  1075px   能滚     ← 高度还在涨
```

#### 15.2 根因：相似推荐异步撑高

`similarBox` 初始是空 div，高度 0。
`/api/similar` 返回后渲染 12 张卡，撑出 444px，
页面总高才够滚动。

**修复：骨架占位**

先把标题和 **12 张**占位卡铺上（数量必须和真实结果一致），
数据到达后 `container.innerHTML = ''` 原地替换，不 append。

占位卡用 `aspect-ratio: 3/4` + 闪烁渐变，
高度与真实卡片（3:4 图 + 一行 VID）基本一致。

效果：

| 指标 | 修复前 | 修复后 |
|---|---|---|
| 可滚时间点 | t≈1000ms | t≈300ms |
| 高度跳变 | 230px | 9px |

#### 15.3 同类问题：另外两个滚动容器也没设 min-height: 0

顺着这条线索把**所有 `overflow-y: auto` 的容器**查了一遍，
发现还有两处同样的缺陷：

| 容器 | 问题 | 影响 |
|---|---|---|
| `.sheet-body` | `flex: 1` + `overflow-y: auto`，无 `min-height: 0` | 筛选面板滚不动（内容 2572px，容器 425px） |
| `.player-meta` | 有 `max-height: 34vh` 但无 `min-height: 0` | `max-height` 失效，内容撑破布局而非内部滚动 |

两处都补上 `min-height: 0` 后实测可滚
（筛选面板可滚 2147px，播放页元信息可滚 78px）。

**结论**：只要在 flex 子项上写 `overflow-y: auto`，
就必须同时写 `min-height: 0`；横向滚动则是 `min-width: 0`。
这是同一类错误的三种表现。

#### 15.4 排查方法

静态测 `scrollHeight > clientHeight` **不足以**发现这类问题——
它只能告诉你「现在能不能滚」，不能告诉你「什么时候才能滚」。

正确做法是时序采样：

```js
document.querySelector('#grid .card .card-info').click();
for (const t of [150, 300, 500, 800, 1200, 2000]) {
  await sleep(t - prev);
  sample(t);   // 记录 scrollHeight / canScroll / 子元素数量
}
```

配合 CDP 限速（`Network.emulateNetworkConditions`）才能复现
真实网络下的表现；本地 localhost 太快，问题根本不出现。

#### 15.5 验证结果

| 容器 | scrollHeight | clientHeight | 可滚动 |
|---|---|---|---|
| 详情页正文 | 1075 | 845 | ✓ 230px |
| 筛选面板 | 2572 | 425 | ✓ 2147px |
| 播放页元信息 | 316 | 238 | ✓ 78px |

---

## 十六、投屏到投影仪（DLNA）

### 16.1 为什么选 DLNA

需求是「手机选片，投到投影仪」。候选方案对比：

| 方案 | 结论 |
|---|---|
| DLNA / UPnP AVTransport | **采用**。投影仪自带，零安装，本机已有 HTTP+Range 推流 |
| Chromecast | 需要 protobuf + mDNS + Cast SDK，且国产投影仪基本不支持 |
| Miracast | 是整屏镜像，不是投单部片；且需要 Wi-Fi Direct 硬件支持 |
| 投影仪浏览器打开网页版 | 可行但遥控体验差，依赖投影仪浏览器能力 |

关键前提已经具备：库里视频以 **H.264 + AAC** 为主（抽样 20 个文件全部如此），
正是投影仪原生解码格式，**不需要 FFmpeg 转码**，直接推原文件即可。

### 16.2 协议链路

```
手机点「投屏」
  → POST /api/cast/play/{dataId}?device=设备名
  → SSDP 发现（若缓存为空）+ SOAP SetAVTransportURI + Play
  → 投影仪自己来 GET /stream/{dataId}（带 Range，可拖动）
  → 手机每秒 2 次轮询 /api/cast/status 显示进度
  → 点快进 → SOAP Seek(绝对时间)
```

只有三个动作真正必要，协议本身很小，**没有引入任何 NuGet 包**：
`SetAVTransportURI`（给地址）、`Play`/`Pause`/`Stop`、`Seek`。

不用现成库的原因：主流 .NET DLNA 库（Mono.Upnp 等）已停更多年，
会拖入 Sankara 依赖链，与本项目 netstandard2.0 + .NET Framework 4.7.2 组合
容易版本冲突，收益不抵成本。

### 16.3 文件与接口

| 文件 | 职责 |
|---|---|
| `Core/Lan/Dlna.cs` | SSDP 发现、设备描述解析、SOAP 控制 |
| `Core/Lan/LanServer.cs` | `/api/cast/*` 路由与处理 |
| `Resources/Web/index.html` | 投屏按钮、设备面板、遥控界面 |

接口：

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/api/cast/devices?refresh=1` | 列出可投屏设备（默认走缓存） |
| POST | `/api/cast/play/{dataId}?device=` | 投屏。不传 device 用当前/第一个设备 |
| POST | `/api/cast/pause` / `resume` / `stop` | 播放控制，`stop` 幂等 |
| POST | `/api/cast/seek?seconds=N` | 跳转（DLNA 只收绝对时间） |
| GET | `/api/cast/status` | 状态、进度、总时长 |

### 16.4 踩过的三个坑

#### 坑 1：投屏地址给错网卡（最隐蔽）

首次实现用 `GetLocalIpAddress()` 取「第一个非回环地址」，
本机装了 UID VPN，枚举到 `192.168.3.32/32`，于是投屏地址成了
`http://192.168.3.32:5200/stream/1971`，而投影仪在 `192.168.10.35`。

现象很有迷惑性：SOAP 指令**返回成功**，状态一直 `TRANSITIONING`，
进度恒为 `00:00:00`——因为投影仪根本拉不到流。

修法：新增 `GetLocalIpFor(remoteIp)`，按目标设备 IP 和本机掩码
算同网段地址；`GetLocalIpAddress()` 也改为优先选
私网段且掩码小于 /32 的地址。

**验证方法**：看返回的 `url` 是否与设备同网段，再看进度是否会推进。

#### 坑 2：`RelTime` 取错动作

`GetTransportInfo` 只返回 `CurrentTransportState`，
播放进度属于另一个动作 `GetPositionInfo`。
第一版在 `GetTransportInfo` 里找 `RelTime`，进度永远 `00:00:00`。
现在两个动作分别调用。

#### 坑 3：多网卡只发一次 SSDP 会漏设备

本机同时有以太网和 WLAN 时，默认路由只从其中一个网卡发广播。
`Discover()` 改为对每个本机 IPv4 各开一个 socket 分别发送。

### 16.5 前端交互结构（参考成熟视频站）

投屏**不关闭播放页**，而是把播放页切成「遥控器」形态：

- 视频区换成设备图标 + 设备名 + 播放状态
- 进度条与「后退 10s / 暂停 / 快进 10s」继续可用，遥控远端
- 底部「停止投屏」退出

如果投屏后直接关掉播放页，用户就没有任何控制入口，
只能跑去投影仪按遥控器——这正是 YouTube / Netflix / B 站
投屏后仍保留控制面板的原因。

#### 进度条拖拽

进度条支持**拖拽**和**点击跳转**，实现上有三个必须注意的点：

1. **拖动中不发请求，松手才发一次。**
   DLNA 的 `Seek` 是同步 SOAP 调用，每次约 100–300ms。
   如果每次 `pointermove` 都发 Seek，手指拖 2 秒会产生几十条指令，
   投影仪控制端口被压满，表现为卡死或断开。
   正确做法：拖动时只改本地进度条显示，`pointerup` 时发一次。
   实测拖动全程只产生 1 次 `/api/cast/seek`。

2. **拖动时必须跳过轮询。**
   `poll()` 每 2 秒把服务端位置写回进度条，
   拖着不放时会被旧位置拽回去，手感是「拖不动」。
   用一个 `dragging` 标志在轮询入口直接 return。

3. **window 上的监听必须能解绑。**
   `pointermove`/`pointerup` 挂 window 才能支持拖出进度条范围继续拖，
   但每次进入投屏页都会新增一份，进出几次后拖一次会发 N 次 seek。
   用 `castCleanup` 保存解绑函数，在进入投屏、停止投屏、关闭播放页时调用。

触摸区：进度条视觉高 4px，用上下 padding 撑到约 28px 触摸区，
并加 `touch-action: none`——否则手机上拖动会被浏览器当成滚动手势。


退出路径三条，全部实测：

| 操作 | 行为 |
|---|---|
| 点「停止投屏」 | 停投屏，恢复本机播放器，可接着在手机看 |
| 点播放页返回键 | 停投屏 + 关闭播放页 |
| 重新加载页面 | 读 `/api/cast/status`，以服务端为准同步按钮状态 |

`stopCast()` 的界面恢复**不依赖**后端成败：后端失败也要退出遥控界面，
否则用户会卡在一个控制不了任何东西的页面里。

### 16.6 实测记录（2026-10-26）

环境：投影仪 192.168.10.35（小明 Q2 Pro 等 5 个 DLNA 接收端），
本机 192.168.10.2。

| 项 | 结果 |
|---|---|
| 设备发现 | 5 个，2.3 秒 |
| 投屏 | `state=PLAYING`，投影仪建立到 5200 的连接 |
| 进度推进 | 00:00:55 → 00:01:00（5 秒真实推进） |
| 快进 600s | 跳到 00:10:01 |
| 拖拽到 50% | 松手后 01:00:36（总长 2:01:13），全程仅 1 次 seek |
| 点击进度条 75% | 跳到 01:30:52 |
| 退出后残留 seek | 0（监听已解绑） |
| 暂停 / 恢复 | `PAUSED_PLAYBACK` / `PLAYING` |
| 本机 video 释放 | 投屏后本机 video 元素数 = 0 |
| 返回键退出 | 服务端 `casting=false` |

注意：投屏会话存在内存里，**Jvedio 重启后丢失**。
投影仪那边可能还在放（它自己的缓冲），但服务端已无控制权，
前端刷新后会自动同步为「未投屏」。

### 16.7 排障

- 搜不到设备：确认投影仪开机、与本机同一 WiFi；投影仪侧需开启
  「投屏」或「DLNA」开关。
- 搜到但投屏失败：看 Jvedio 日志里 `DLNA {action} 失败` 的 SOAP 正文，
  里面通常带 `UPnPError` 错误码。
- 投屏成功但不动：先看 `/api/cast/status` 的 `state` 是否为
  `TRANSITIONING`，若是则多半是推流地址网段不对（见坑 1）。

---

## 十七、移动端播放手势

### 17.1 手势一览

| 手势 | 行为 |
|---|---|
| 双击左/右半屏 | 快退 / 快进 10 秒 |
| 单击 | 显示 / 隐藏控件 |
| **横向滑动** | 快进 / 快退，位置随滑动距离连续变化 |
| 垂直滑动 | 不拦截，交还页面滚动 |
| 拖动底部进度条 | 精确定位 |

### 17.2 横向滑动的三个关键设计

#### 按比例映射，不用固定像素

滑动量换算成时间的公式是：

```
Δ秒 = (水平位移 / 视频宽度) × 总时长
```

**不能**用「每秒 N 像素」这类固定换算。原因：2 小时的片子滑满屏只走
40 秒，20 分钟的片子滑满屏能滑完，同一个手势在长短片上体感完全不同。
按比例映射后，滑满屏永远等于走完整段，符合直觉。

#### 拖动中不改 `currentTime`

拖动期间只更新浮层文字和进度条宽度，**松手才 `video.currentTime = target`**。
每帧改 `currentTime` 会不断触发 seek 和解码，手机上表现为卡顿甚至花屏。

#### 方向判定与手势冲突

横向位移超过 12px 才开始判定；若垂直位移更大则放弃，
把事件交还页面（否则会和滚动打架）。

滑动完成后浏览器仍会补发一次 `click`，需要记录 `lastEndAt` 并
在 click 处理里拦掉，否则滑动结束瞬间控件会莫名切换显隐。

### 17.3 验证记录（2026-10-27）

素材：2:01:13 的片。

| 场景 | 结果 |
|---|---|
| 右滑 40% | 21s → 2931s（预期 2930） |
| 右滑 30% | 8s → 2191s（预期 2190） |
| 左滑 40% | 2943s → 35s，提示 `-48:30` |
| 拖动中 currentTime | 不变（只在松手时 seek） |
| 垂直滑动 | currentTime 不变，浮层不显示 |
| 滑动后 click | 未被误触发，`swiping` 类正常清除 |

---

## 十八、卡片点击区域（2026-10-27 修复）

### 18.1 问题

用户在手机端点卡片封面时，**只有点中图片中心才触发播放**；
点封面边缘、四角或图片加载中的空白，会跳到详情页或毫无反应。

### 18.2 根因

原来是给 `cover` 和 `info` 各绑一个 `click`：

```js
cover.addEventListener('click', ... openPlayer ...)
info.addEventListener('click', ... openDetail ...)
```

`cover` 的监听只覆盖它自己的盒模型。但点中封面时，
`e.target` 实际是里面的 `<img>` 或角标 `<span>`，
事件冒泡虽能到达 `cover`，可一旦出现以下情况就会漏：

- 图片未加载完，`<img>` 尺寸为 0，点到的是 `cover` 的 `background`
- 圆角 (`overflow: hidden` + `border-radius`) 裁掉了角上 1-2px
- 亚像素取整导致 `img` 与 `cover` 边界差 0.5px

点漏之后事件继续冒泡到 `#grid`，那里没有任何监听，
于是「没反应」；用户会误以为是页面刷新。

### 18.3 修法

统一绑在 `card` 上，用 **`cover.contains(e.target)`** 做几何归属判断：

```js
card.addEventListener('click', function (e) {
  e.stopPropagation();
  if (cover.contains(e.target)) openPlayer(item.DataID);
  else openDetail(item.DataID);
});
```

`contains` 判定的是「这个节点在不在封面子树里」，而不是
「点是否精确落在 cover 的盒子内」，因此图片、角标、内边距
任何位置都算封面。**只要在封面区域内，100% 是播放。**

同时补了移动端触摸属性，消除「点了像刷新」的另一来源：

```css
.card {
  touch-action: manipulation;   /* 去掉 300ms 延迟，禁用双击缩放 */
  -webkit-user-select: none;
  user-select: none;
  -webkit-touch-callout: none;  /* 长按不弹图片菜单 */
  -webkit-tap-highlight-color: transparent;
}
```

### 18.4 验证

用真实鼠标在封面 9 个位置（四角 + 四边中点 + 中心）逐一点击：

| 位置 | 结果 |
|---|---|
| 左上 / 上中 / 右上 | 播放 ✓ |
| 左中 / 中心 / 右中 | 播放 ✓ |
| 左下 / 下中 / 右下 | 播放 ✓ |

文字区 3 个位置（左上、中心、右下）→ 全部进详情页 ✓。

> 补充：卡片之间的 8.5px 间隙属于 `#grid` 本身，
> 点击那里不会触发卡片。这是刻意保留的——缝隙太窄，
> 若把点击算给最近卡片，反而容易误触相邻的片。

---

## 十九、访问口令（2026-10-27）

### 19.1 为什么不做账号体系

单机媒体库没有多用户场景。Jellyfin / Plex 的多账号是为了家庭共享，
本项目不需要，多账号只会扩大攻击面。

**只做一件事：一个访问口令，每台设备输一次。**

明确不做：注册、找回密码、验证码、口令强度规则、"记住我"勾选框、
登录有效期选项。这些在局域网自用场景里全是负担。

### 19.2 数据流

```
桌面端设置页 ──写入──> Settings.LanAccessPassword
                              │
                              ├─> 启动时 Auth.SetPassword()  (Window_Main.InitLanServer)
                              └─> 保存时 Auth.SetPassword()  (立即生效，无需重启)
                                        │
手机 GET /api/auth/status ──────────────┤
     ├ required=false ──> 直接启动，完全无感
     ├ authenticated=true ──> 直接启动
     └ 否则 ──> 显示登录遮罩
                    │
        POST /api/auth/login {password}
                    │
              <── {token} 存入 localStorage
                    │
        之后所有请求自动带令牌
```

### 19.3 令牌格式

自包含 HMAC 签名，无服务端会话表：

```
base64url(过期时间|随机数) + "." + base64url(HMAC-SHA256)
```

- 密钥由口令派生 → **改口令即让所有旧令牌失效**，不需要额外的"全部下线"逻辑
- 重启服务后旧令牌依然有效（因为密钥没变）
- 有效期 365 天：单机媒体库没必要频繁重登

三种携带方式，按优先级：

1. `Authorization: Bearer xxx`
2. 查询串 `?token=xxx` —— 给 `<img src>`、`<video src>` 用，它们无法带头部
3. `Cookie: jvedio_token=xxx`

### 19.4 投屏与鉴权的冲突

投影仪拉流时**无法携带登录 Cookie**。若 `/stream/` 强制校验，投屏必然坏掉。
若完全不校验，知道 DataID 就能下载整部片。

解法：投屏时签发**临时令牌**，绑定 dataId、10 分钟有效，拼在流地址上。

```csharp
if (_Auth.Enabled) {
    string castToken = _Auth.IssueCastToken(dataId);
    mediaUrl += "?cast=" + WebUtility.UrlEncode(castToken);
}
```

校验顺序：投屏临时令牌 → 普通访问令牌 → 拒绝。

### 19.5 防暴力破解

按 IP 计失败次数，连续 10 次锁 5 分钟。
口令比较用常量时间算法，避免通过响应时间差逐字节猜。

### 19.6 顺手修掉的一个严重 bug

调试端口时发现：**配置里所有 `int` 类型的项，改动从来都不生效。**

`AbstractConfig.Read()` 原本是：

```csharp
object value = dict[key];          // JSON 数字被读成 long
prop.SetValue(this, value, null);  // long -> int 抛异常
// catch { continue; }             // 静默吞掉
```

于是这些属性永远停留在构造函数里的默认值，日志里毫无线索。
表现就是"改了端口、改了缓存时间，重启后没变化"。

修法：赋值前先做类型转换。

```csharp
private static object ConvertForProperty(object value, Type propertyType)
{
    Type target = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
    if (target.IsInstanceOfType(value)) return value;
    if (target.IsEnum) return value is string
        ? Enum.Parse(target, (string)value, true)
        : Enum.ToObject(target, Convert.ToInt64(value));
    return Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
}
```

这是本次最有价值的发现——它解释了为什么 LAN 端口改成非 5200 一直不起作用。

### 19.7 验证结果（开发版 5201）

后端：

| 场景 | 期望 | 实际 |
|---|---|---|
| 未登录 status | required=true, authenticated=false | ✓ |
| 未登录 /api/libraries | 401 | ✓ |
| 错误口令登录 | 401 口令不正确 | ✓ |
| 正确口令登录 | 返回 token | ✓ |
| 带令牌 /api/libraries | 200 | ✓ |
| 带令牌 /api/videos | 200 | ✓ |
| 查询串令牌 | 200 | ✓ |
| 带令牌拉流 | 206 | ✓ |
| 无令牌拉流 | 401 | ✓ |
| 带令牌取图片 | 200 | ✓ |
| 伪造令牌 | 401 | ✓ |
| 带令牌 status | authenticated=true | ✓ |

前端：

| 场景 | 结果 |
|---|---|
| 需要口令时显示遮罩 | ✓ 且列表未加载 |
| 错误口令提示 | "口令不正确" ✓ |
| 正确口令进入 | 遮罩消失、30 张卡片加载 ✓ |
| 令牌写入 localStorage | 102 字符 ✓ |
| 刷新页面 | 自动登录、不再要求口令 ✓ |
| 图片地址带令牌 | ✓ |
| 视频地址带令牌并播放 | readyState=4，正常播放 ✓ |

### 19.8 开发版隔离约定

日常使用的版本**不动**，另建一个开发运行目录（例如 `D:\Jvedio-dev`）：
- 换一个端口（例如 5201），避免与日常版本冲突
- 独立的数据目录（程序目录下的 `data\`），互不影响
- 验证通过后才考虑合并回日常版本

> 注意：端口冲突时报的是"通常每个套接字地址只允许使用一次"，
> 但端口其实是空的——那是 19.6 的配置 bug 导致读到了默认的 5200。
> 遇到这个报错先确认 `Settings.LanServerPort` 的真实值，别急着查端口占用。

---

## 20. 内置刮削器：豆瓣 / TMDB / Bangumi（2026-11）

### 20.1 为什么做

番号类资源一直靠插件刮削器（`plugins/crawlers/*.dll`），但**普通影片（电影、剧集、动画）没有对应插件**：
插件都是按番号查的，普通影片只有文件名。以前的做法是手动填，或者装第三方插件。这一版把三个公开源做成**源码内置**，
不依赖任何闭源插件，装了就能用。

### 20.2 结构

```
Jvedio/Core/Crawler/Builtin/
├── BuiltinCrawlerQuery.cs   FileNameParser：文件名 → 标题 + 年份 + 是否单集
├── IBuiltinCrawler.cs       契约：Id / Name / IsEnabled / RequiresKey / GetInfo
├── BuiltinCrawlerManager.cs 调度：按顺序尝试，命中即返回；单集先问 Bangumi
├── BuiltinSupport.cs        JSON 取值 + HTTP 封装（复制 header，不污染插件请求）
├── DoubanCrawler.cs         豆瓣：suggest 搜索 + m.douban.com rexxar 详情
├── TmdbCrawler.cs           TMDB：search/movie|tv + 详情（append_to_response=credits）
└── BangumiCrawler.cs        Bangumi：老接口搜索 + v0 详情（infobox/tags 只在 v0 里有）
```

接入点在 `VideoDownLoader.GetInfo` 的**最前面**，并且必须在 `GetCrawlerServer` 之前——没装插件时
`GetCrawlerServer` 会抛 `CrawlerNotFoundException`，而普通影片本来就不该依赖番号插件。内置源没命中时自然回落到插件。

### 20.3 数据契约

返回 `Dictionary<string, object>`，键名 = `Video` 实体属性名，由 `Video.ParseDictInfo` 反射写入：
`Title / Plot / Outline / ReleaseDate / ReleaseYear / Duration / Director / ActorNames(List<string>) /
Genre / Country / Studio / Rating / BigImageUrl / SmallImageUrl / ExtraImageUrl / WebUrl / DataCode / WebType`。
`Title` 非空才算命中（与插件一致）。`DataCode` 存站点条目 id，便于以后做增量识别。

### 20.4 配置（设置 → 网络 → 内置刮削器）

`DownloadConfig` 新增：`EnableBuiltinCrawler`（总开关）、`EnableDoubanCrawler`、`EnableTmdbCrawler`、
`EnableBangumiCrawler`、`TmdbApiKey`、`TmdbLanguage`（默认 zh-CN）、`DoubanCookie`（可选）。

- TMDB 需要用户自己在 themoviedb.org 免费申请 v3 Key，**不预置任何 Key**；没填时 `TmdbCrawler.IsEnabled` 直接返回 false，静默跳过。
- 豆瓣免 Key，但频繁请求会要求验证码，可在设置里填登录后的 Cookie。
- 设置窗口以前只有点「应用」才落盘，这一版把 `ConfigManager.DownloadConfig.Save()` 补进了 `Window_Closing`。

### 20.5 匹配策略（改动前先看踩坑约定 #31）

1. 文件名解析：先取年份 → 再摘括号 → 分隔符转空格 → 剔除分辨率/编码/音轨噪声。
2. 候选打分：标题归一化后比较（相等 1.0 / 包含 0.8 / 字符集合重叠 ×0.7），年份对上加 0.35、错开减 0.15。
3. 阈值：豆瓣 0.5，Bangumi 0.7（片库更杂）；Bangumi 带年份时差两年以上直接排除。
4. 单集文件（`IsEpisode`）先问 Bangumi，因为豆瓣 suggest 只返回电影类条目。

### 20.6 验证记录（2026-11-01，开发版 5201）

用独立控制台工程直接编译内置刮削器源码（`_buildtools/crawlertest`，不入库）跑真实接口：

| 文件名 | 解析 | 结果 |
|---|---|---|
| `流浪地球2.2023.1080p.BluRay.x264-GROUP.mkv` | 流浪地球2 / 2023 | 豆瓣命中，173 分钟、8.3 分、郭帆、46 位演员 ✓ |
| `The.Matrix.1999.1080p.BluRay.x264.mkv` | The Matrix / 1999 | 豆瓣未命中、Bangumi 正确拒绝 1971 年同名短片 ✓（需配 TMDB Key 才能刮到） |
| `[VCB-Studio] 进击的巨人 [01][Ma10p_1080p][x265_flac].mkv` | 进击的巨人 / 无年份 | Bangumi 命中 2013 TV 版，8.2 分、荒木哲郎 ✓ |
| `让子弹飞 (2010).mp4` | 让子弹飞 / 2010 | 豆瓣命中，132 分钟、姜文 ✓ |

接口可达性：豆瓣 suggest / rexxar、Bangumi 老接口 + v0、`api.themoviedb.org`（返回 401 = 网络可达，只是没 Key）均正常。

### 20.7 已知限制

- 豆瓣 suggest 只覆盖电影类，**剧集（TV）在豆瓣侧基本搜不到**，单集文件靠 Bangumi。
- TMDB 的中文简介/片名覆盖率一般，缺失时回落原名。
- 番号类资源不走内置源（文件名解析会得到奇怪的标题），仍然由插件负责。


