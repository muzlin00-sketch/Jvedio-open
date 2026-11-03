# 把便携版发布包部署到已安装目录（覆盖式升级）
#
# 用法：
#   pwsh -NoProfile -File .\scripts\deploy-install.ps1
#   pwsh -NoProfile -File .\scripts\deploy-install.ps1 -InstallDir 'D:\app\Jvedio-5.4'
#
# 原则：
#   1. 只覆盖发布包里有的文件，不删除安装目录里多出来的东西（旧 .bak、插件中心装的插件都保留）
#   2. data\ 与 Cache\ 完全不碰；覆盖前把整个目录（不含这两项）备份一份
#   3. 覆盖后逐项校验哈希，并确认 data\ 只多了 Data\theme.json（见 踩坑约定 第 35 条）

param(
    [string]$InstallDir = 'D:\app\Jvedio-5.4',
    [string]$Zip = ''
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $Zip) {
    $Zip = Join-Path $repoRoot 'release\Jvedio-5.4-portable-win-x64.zip'
}
foreach ($p in @($InstallDir, $Zip)) {
    if (-not (Test-Path -LiteralPath $p)) { throw "路径不存在：$p" }
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupDir = Join-Path (Split-Path -Parent $InstallDir) ("_backup-" + (Split-Path -Leaf $InstallDir) + "-$stamp")
$stageDir = Join-Path (Split-Path -Parent $InstallDir) '_stage_jvedio_new'

Write-Host '[0/6] 前置检查'
$running = Get-Process -Name 'Jvedio' -ErrorAction SilentlyContinue
if ($running) {
    $running | ForEach-Object { Write-Host ('      运行中: PID=' + $_.Id + '  ' + $_.Path) }
    throw 'Jvedio 还在运行，先关掉再部署'
}
Write-Host '      没有 Jvedio 进程在跑'

$dataDir = Join-Path $InstallDir 'data'

# data\ 里唯一允许变动的是 Data\theme.json（程序资源与用户数据目录同名，见踩坑约定 35），
# 所以快照两边都把它排除，否则「本来就有」也会被判成变化
$themeFile = Join-Path $dataDir 'theme.json'
function Get-DataSnapshot {
    Get-ChildItem -LiteralPath $dataDir -Recurse -File |
        Where-Object { $_.FullName -ne $themeFile } |
        Sort-Object FullName |
        ForEach-Object { $_.FullName + '|' + $_.Length }
}

$dataBefore = @(Get-DataSnapshot)
Write-Host ('      data\ 当前 ' + $dataBefore.Count + ' 个文件（不含 theme.json）')
$oldExe = Get-Item -LiteralPath (Join-Path $InstallDir 'Jvedio.exe')
Write-Host ('      当前 Jvedio.exe: ' + $oldExe.Length + ' 字节, ' + $oldExe.LastWriteTime)

Write-Host '[1/6] 备份安装目录（跳过 data 与 Cache）'
New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
$skip = @('data', 'Cache')
$copied = 0
foreach ($item in Get-ChildItem -LiteralPath $InstallDir -Force) {
    if ($skip -contains $item.Name) { continue }
    $dest = Join-Path $backupDir $item.Name
    if ($item.PSIsContainer) {
        Copy-Item -LiteralPath $item.FullName -Destination $dest -Recurse -Force
        $copied += (Get-ChildItem -LiteralPath $dest -Recurse -File).Count
    } else {
        Copy-Item -LiteralPath $item.FullName -Destination $dest -Force
        $copied += 1
    }
}
Write-Host ('      -> ' + $backupDir + '（' + $copied + ' 个文件）')

Write-Host '[2/6] 解压发布包到暂存'
if (Test-Path -LiteralPath $stageDir) { Remove-Item -LiteralPath $stageDir -Recurse -Force }
Expand-Archive -LiteralPath $Zip -DestinationPath $stageDir -Force
Write-Host ('      ' + (Get-ChildItem -LiteralPath $stageDir -Recurse -File).Count + ' 个文件')
$newExe = Get-Item -LiteralPath (Join-Path $stageDir 'Jvedio.exe')
Write-Host ('      新 Jvedio.exe: ' + $newExe.Length + ' 字节')

Write-Host '[3/6] 覆盖程序文件'
Copy-Item -Path (Join-Path $stageDir '*') -Destination $InstallDir -Recurse -Force

Write-Host '[4/6] 校验'
$checks = @('Jvedio.exe', 'Jvedio.Core.dll', 'SuperUtils.dll', 'Web\index.html',
            'plugins\crawlers\bus\main.json', 'plugins\crawlers\bus\BusCrawler.dll',
            'plugins\crawlers\db\DBCrawler.dll', 'plugins\crawlers\fc2\FC2Crawler.dll',
            'plugins\crawlers\library\LibraryCrawler.dll', 'x64\SQLite.Interop.dll')
$bad = @()
foreach ($c in $checks) {
    $src = Join-Path $stageDir $c
    $dst = Join-Path $InstallDir $c
    if (-not (Test-Path -LiteralPath $dst)) { $bad += "$c（缺失）"; continue }
    if ((Get-FileHash -LiteralPath $src -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $dst -Algorithm SHA256).Hash) {
        $bad += "$c（哈希不一致）"
    }
}
if ($bad.Count -gt 0) { throw ('校验失败: ' + ($bad -join '; ')) }
Write-Host ('      关键文件哈希一致：' + $checks.Count + ' 项')

$pluginDirs = Get-ChildItem -LiteralPath (Join-Path $InstallDir 'plugins\crawlers') -Directory |
    Select-Object -ExpandProperty Name
Write-Host ('      插件目录: ' + ($pluginDirs -join ', '))

$dataAfter = @(Get-DataSnapshot)
if (Compare-Object $dataBefore $dataAfter) {
    Compare-Object $dataBefore $dataAfter | ForEach-Object { Write-Host ('      差异 ' + $_.SideIndicator + ' ' + $_.InputObject) }
    throw 'data\ 内容发生变化（除 theme.json 外），立即停止并回滚'
}
Write-Host ('      data\ 未变化：' + $dataAfter.Count + ' 个文件')

Write-Host '[5/6] 清理暂存'
Remove-Item -LiteralPath $stageDir -Recurse -Force

Write-Host '[6/6] 完成'
Write-Host ('      安装目录: ' + $InstallDir)
Write-Host ('      回滚备份: ' + $backupDir)
Write-Host '      下一步：手动启动 Jvedio.exe，确认能进主界面、手机端 5200 端口可访问'
