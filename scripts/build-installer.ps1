# 用便携版发布包打一个安装包（Inno Setup）
#
# 用法：
#   pwsh -NoProfile -File .\scripts\build-installer.ps1
#   pwsh -NoProfile -File .\scripts\build-installer.ps1 -Version 5.4
#
# 前置：装了 Inno Setup 6（winget install JRSoftware.InnoSetup）。
# 输入：release\Jvedio-<版本>-portable-win-x64.zip
# 输出：release\Jvedio-<版本>-setup-win-x64.exe
#
# 安装包不签名，首次运行会有 SmartScreen 提示，README 里写了怎么过。

param(
    [string]$Version = '5.4',
    [string]$PortableZip = ''
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$releaseDir = Join-Path $repoRoot 'release'
$installerDir = Join-Path $repoRoot 'scripts\installer'
$stageDir = Join-Path $releaseDir '_stage_installer'

if (-not $PortableZip) {
    $PortableZip = Join-Path $releaseDir "Jvedio-$Version-portable-win-x64.zip"
}
if (-not (Test-Path -LiteralPath $PortableZip)) {
    throw "找不到便携版发布包：$PortableZip（先跑 scripts\repack-release.ps1）"
}

# 找 ISCC.exe：先看用户级安装，再看全局安装
$isccCandidates = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    'C:\Program Files\Inno Setup 6\ISCC.exe'
)
$iscc = $isccCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $iscc) {
    throw '找不到 ISCC.exe，请先安装 Inno Setup 6：winget install JRSoftware.InnoSetup'
}

Write-Host "[1/3] 解压便携版到暂存目录 ..."
if (Test-Path -LiteralPath $stageDir) {
    Remove-Item -LiteralPath $stageDir -Recurse -Force
}
Expand-Archive -LiteralPath $PortableZip -DestinationPath $stageDir -Force
$exePath = Join-Path $stageDir 'Jvedio.exe'
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "暂存目录里没有 Jvedio.exe，发布包结构不对：$stageDir"
}
$exeSize = (Get-Item -LiteralPath $exePath).Length
Write-Host "      Jvedio.exe = $exeSize 字节"

Write-Host "[2/3] 编译安装包 ..."
$issPath = Join-Path $installerDir 'Jvedio.iss'
& $iscc "/DSourceDir=$stageDir" "/DOutputDir=$releaseDir" "/DAppVersion=$Version" $issPath
if ($LASTEXITCODE -ne 0) {
    throw "ISCC 编译失败，退出码 $LASTEXITCODE"
}

Write-Host '[3/3] 校验产物 ...'
$setupPath = Join-Path $releaseDir "Jvedio-$Version-setup-win-x64.exe"
if (-not (Test-Path -LiteralPath $setupPath)) {
    throw "没有生成安装包：$setupPath"
}
$setupSize = (Get-Item -LiteralPath $setupPath).Length
Write-Host ("      安装包: {0}  ({1:N2} MB)" -f $setupPath, ($setupSize / 1MB))

Remove-Item -LiteralPath $stageDir -Recurse -Force
Write-Host '      已清理暂存目录'
