# 重新打便携版发布包：从 bin\Release 取产物，替换 release\*.zip 里的同名文件。
#
# 用法（先构建 Release）：
#   pwsh -NoProfile -File .\build.ps1 -Configuration Release
#   pwsh -NoProfile -File .\scripts\repack-release.ps1
#
# 为什么这样打而不是直接 Compress-Archive 整个 bin\Release：
#   发布包里刻意不含 data\（运行数据）、*.pdb、*.xml 文档等，只保留运行必需的文件；
#   现有 zip 的条目清单就是这份"必需清单"，重打包时以它为基准覆盖，避免误把调试文件带进去。
[CmdletBinding()]
param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repoRoot = Split-Path -Parent $PSScriptRoot
$binDir = Join-Path $repoRoot "Jvedio-WPF\Jvedio\bin\$Configuration"
$zip = Join-Path $repoRoot 'release\Jvedio-5.4-portable-win-x64.zip'
$stage = Join-Path $repoRoot '_stage_release'

if (-not (Test-Path -LiteralPath $binDir)) {
    throw "找不到构建产物目录：$binDir（先跑 build.ps1 -Configuration $Configuration）"
}
if (-not (Test-Path -LiteralPath $zip)) {
    throw "找不到现有发布包：$zip"
}

$exe = Join-Path $binDir 'Jvedio.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "找不到 $exe" }

# 1. 解压现有包，保留其条目清单
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null
[System.IO.Compression.ZipFile]::ExtractToDirectory($zip, $stage)
Write-Host '[1/4] 已解压现有发布包到 _stage_release'

# 2. 用构建产物覆盖同名文件
$copied = 0
Get-ChildItem -LiteralPath $binDir -Recurse -File | ForEach-Object {
    $relative = $_.FullName.Substring($binDir.Length + 1)
    $target = Join-Path $stage $relative
    if (Test-Path -LiteralPath $target) {
        Copy-Item -LiteralPath $_.FullName -Destination $target -Force
        $script:copied++
    }
}
Write-Host "[2/4] 用 $Configuration 产物覆盖了 $copied 个文件"

# 3. 确认包里的 exe 确实是刚构建的那个
$stagedExe = Join-Path $stage 'Jvedio.exe'
if ((Get-Item -LiteralPath $stagedExe).Length -ne (Get-Item -LiteralPath $exe).Length) {
    throw '暂存目录的 Jvedio.exe 与构建产物大小不一致'
}
Write-Host ('[3/4] 校验通过，Jvedio.exe = ' + (Get-Item -LiteralPath $stagedExe).Length + ' 字节')

# 4. 重新打包
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $stage, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
Write-Host ('[4/4] 已重新打包: ' + (Get-Item -LiteralPath $zip).Length + ' 字节 -> ' + $zip)

$archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
Write-Host ('      包内条目数: ' + $archive.Entries.Count)
$archive.Dispose()

Write-Host ''
Write-Host '下一步：确认 zip 内容无误后提交，并在 GitHub 上把 zip 附加到 Release。'
