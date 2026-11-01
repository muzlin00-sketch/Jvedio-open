# 更新日志

本文件记录对外可见的变化。更详细的技术决策、踩过的坑与验证记录在 [MAINTAINING.md](MAINTAINING.md)，
跨模块陷阱清单在 [项目文档/踩坑约定.md](项目文档/踩坑约定.md)。

版本号沿用上游的 `5.4`，小改动不单独升版本。

## 未发布

### 新增

- **安装包**：`Jvedio-5.4-setup-win-x64.exe`，双击装完就能用。装到 `%LOCALAPPDATA%\Programs\Jvedio`，
  不需要管理员权限，开始菜单带快捷方式和卸载；**卸载不删 `data\`**（影片库和封面在里面）。
  便携版 zip 继续提供，两者内容一致。打包脚本 `scripts/build-installer.ps1`。
- **内置刮削器**：普通影片（电影 / 剧集 / 动画）按文件名自动刮削，源码内置豆瓣 / TMDB / Bangumi 三个源，
  不依赖插件。TMDB 需在设置里填自己申请的免费 API Key，豆瓣与 Bangumi 免 Key。
  命中即停止，未命中再回落到插件刮削器。
- **自带四个番号插件**：`plugins/crawlers/` 下的 bus（JavBus，默认启用）/ db（JavDB）/ library（JavLibrary）/
  fc2（FC2 PPV DB），来自上游插件仓库原样分发。首次启动会自动补好四条源记录，勾选即用；
  其余主题插件仍在插件中心安装。

### 修复

- **换网络后手机访问地址不更新**：`LanServer.BaseUrl` 以前在服务启动时算一次就缓存，换 Wi-Fi、
  插拔网线、开关 VPN、休眠唤醒换 IP 之后，设置页和提示里还是旧地址，手机连不上。现在改成每次读取时现算。
- **设置页显示的地址可能选到 VPN 网卡**：设置页原来用「连 8.8.8.8 看出口网卡」判断本机 IP，
  开着 VPN 时会拿到 VPN 地址。现在与局域网服务统一用同一套选择逻辑（优先私有网段、排除 /32 点对点地址）。
- **番号插件装上了却用不了**：全新安装的数据库里没有 `Servers` 配置，`VideoDownLoader.GetCrawlerServer`
  直接抛 `CrawlerNotFoundException`（提示像"没装插件"）。现在 `ServerConfig.Read()` 在没有记录时
  补一条 `crawler/bus` 默认源，用户改过之后照旧以数据库为准。
- **`SuperUtils` 的程序集版本是 `0.0.0.0`**：旧式 csproj 里的 `<AssemblyVersion>` 不生效，版本号改用
  `Properties/AssemblyInfo.cs` 声明为 `1.2.0.0`，与闭源 `CommonNet.dll` 及全部番号插件的引用一致。
- 刮削结果里的评分（`float` 属性）以前根本写不进实体，`Video.ParseDictInfo` 现在支持 `float` / `double` / `decimal` / 数字字符串。
- 设置窗口以前只有点「应用」才保存下载与刮削配置，直接关窗口会丢；现在关窗口也会落盘。
- 文件名解析修正：`让子弹飞 (2010).mp4` 这类带括号年份的文件，标题不再被截成 `让子弹飞 (`。
- `build.ps1 -Configuration Release` 以前必然失败（`SuperUtils` 引用路径写死 `Debug`），现已修正。

### 变更

- 移除 `Reference/` 下 4 个已废弃的闭源二进制（`JvedioLib` / `PInvoke` / `QueryEngine` / `UsnOperation`），
  它们自 2026-10-29 起已被自研实现取代，构建产物里本来就不带。
- 构建脚本不再对整个解决方案执行 `nuget restore`，避免 SDK 风格项目引发的 `MSB4066` 噪声。

## 5.4 维护版基线

- 手机网页版（内嵌 HTTP 服务 + 单文件 SPA）、访问口令鉴权、投屏、自定义播放器控件。
- 相似推荐（演员 / 导演 / 剧情 / 文件名多维度打分）。
- 番号识别白名单式后缀剥离（`ABC-123-ch`、`-HD` 会被剥离，`HRV-030A` 保留）。
- `SuperUtils` 由源码重建，USN 全盘扫描改为自研实现，详见 [项目文档/闭源依赖替换.md](项目文档/闭源依赖替换.md)。
