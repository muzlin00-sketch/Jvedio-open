<#
.SYNOPSIS
    一键构建 Jvedio（.NET Framework 4.7.2 / WPF）。

.DESCRIPTION
    本项目不提交 bin/obj/packages，依赖全部由 NuGet 还原：

      - Microsoft.NETFramework.ReferenceAssemblies.net472 -> net472 参考程序集
      - Microsoft.Net.Compilers.Toolset                   -> Roslyn C# 编译器
      - Jvedio-WPF/Jvedio/packages.config                 -> 主项目依赖

    构建顺序（不能颠倒）：
      1. Jvedio.SuperUtils  —— 自建 SuperUtils.dll（闭源库的源码替代）
      2. Jvedio.Core        —— netstandard2.0 核心库，SDK 风格项目，需要 dotnet CLI
      3. Jvedio             —— 主程序，通过 HintPath 引用上面两者的产物

.PARAMETER Configuration
    Debug（默认）或 Release。

.PARAMETER Clean
    构建前先执行 Rebuild。

.EXAMPLE
    pwsh -NoProfile -File .\build.ps1
    pwsh -NoProfile -File .\build.ps1 -Configuration Release
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$repoRoot = $PSScriptRoot
$sln = Join-Path $repoRoot 'Jvedio-WPF\Jvedio.sln'
$superUtilsProj = Join-Path $repoRoot 'Jvedio-WPF\Jvedio.SuperUtils\Jvedio.SuperUtils.csproj'
$coreProj = Join-Path $repoRoot 'Jvedio-WPF\Jvedio.Core\Jvedio.Core.csproj'
$proj = Join-Path $repoRoot 'Jvedio-WPF\Jvedio\Jvedio.csproj'
$packagesDir = Join-Path $repoRoot 'Jvedio-WPF\packages'
$toolsDir = Join-Path $repoRoot '_buildtools'
$nuget = Join-Path $toolsDir 'nuget.exe'

# NuGet 在默认 %TEMP% 与 %LOCALAPPDATA%\NuGet 下偶尔无法创建 scratch/cache，
# 统一重定向到仓库内的 _buildtools（已在 .gitignore 中忽略）。
$env:TEMP = Join-Path $toolsDir 'nugettmp'
$env:TMP = $env:TEMP
$env:NUGET_PACKAGES = Join-Path $toolsDir 'packages-global'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $toolsDir 'nuget-http-cache'
$env:NUGET_SCRATCH = Join-Path $toolsDir 'nuget-scratch'
foreach ($d in $env:TEMP, $env:NUGET_PACKAGES, $env:NUGET_HTTP_CACHE_PATH, $env:NUGET_SCRATCH) {
    if (-not (Test-Path -LiteralPath $d)) { New-Item -ItemType Directory -Force -Path $d | Out-Null }
}

# 1. 定位 .NET Framework MSBuild
#    优先 32 位：本项目 PlatformTarget 为 AnyCPU(MSIL)，64 位 MSBuild 会报 MSB3270。
$msbuildCandidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\MSBuild.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe')
)
$msbuild = $msbuildCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $msbuild) {
    throw '未找到 .NET Framework MSBuild（v4.0.30319）。请安装 .NET Framework 4.x 开发工具。'
}
Write-Host "[1/6] MSBuild: $msbuild"

# 2. 准备 nuget.exe
if (-not (Test-Path -LiteralPath $nuget)) {
    Write-Host '[2/6] 下载 nuget.exe ...'
    New-Item -ItemType Directory -Force -Path $toolsDir | Out-Null
    Invoke-WebRequest -Uri 'https://dist.nuget.org/win-x86-commandline/latest/nuget.exe' -OutFile $nuget -UseBasicParsing
} else {
    Write-Host '[2/6] nuget.exe 已存在'
}

# 3. 还原依赖 + 补齐构建工具包
#    只还原 packages.config 风格的 Jvedio.csproj；Core（SDK 风格 / PackageReference）的依赖由 dotnet build 自己还原。
#    nuget.exe 会先用 MSBuild 探测项目类型，探测失败时打一条 MSB4066（随后它自己会说明"仅还原 packages.config"），
#    这条不是构建错误，这里过滤掉，避免新人误以为构建失败。
Write-Host '[3/6] 还原 NuGet 包 ...'
$restoreOutput = & $nuget restore $proj -PackagesDirectory $packagesDir -NonInteractive 2>&1
$restoreExit = $LASTEXITCODE
$restoreOutput | Where-Object { $_ -notmatch 'MSB4066' } | ForEach-Object { Write-Host "      $_" }
if ($restoreExit -ne 0) { throw "nuget restore 失败（exit $restoreExit）" }

$extraPackages = @(
    @{ Id = 'Microsoft.NETFramework.ReferenceAssemblies.net472'; Version = '1.0.3' },
    @{ Id = 'Microsoft.Net.Compilers.Toolset'; Version = '4.8.0' }
)
foreach ($pkg in $extraPackages) {
    $expected = Join-Path $packagesDir "$($pkg.Id).$($pkg.Version)"
    if (-not (Test-Path -LiteralPath $expected)) {
        Write-Host "      安装 $($pkg.Id) $($pkg.Version)"
        & $nuget install $pkg.Id -Version $pkg.Version -OutputDirectory $packagesDir -NonInteractive
        if ($LASTEXITCODE -ne 0) { throw "安装 $($pkg.Id) 失败" }
    }
}

# 4. 构建 Jvedio.SuperUtils（自建 SuperUtils.dll）
if (Test-Path -LiteralPath $superUtilsProj) {
    Write-Host "[4/6] 构建 Jvedio.SuperUtils ($Configuration) ..."
    & $msbuild $superUtilsProj /t:Build /p:Configuration=$Configuration /v:m /nologo
    if ($LASTEXITCODE -ne 0) { throw "Jvedio.SuperUtils 构建失败（exit $LASTEXITCODE）" }
} else {
    throw "找不到 Jvedio.SuperUtils 项目：$superUtilsProj"
}

# 5. 构建 Jvedio.Core（netstandard2.0，SDK 风格，需要 dotnet CLI）
if (Test-Path -LiteralPath $coreProj) {
    $dotnetExe = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnetExe) {
        throw '构建 Jvedio.Core 需要 dotnet CLI（netstandard2.0 SDK 风格项目），未找到 dotnet。'
    }
    Write-Host "[5/6] 构建 Jvedio.Core ($Configuration) ..."
    & dotnet build $coreProj -c $Configuration --nologo -v:m
    if ($LASTEXITCODE -ne 0) { throw "Jvedio.Core 构建失败（exit $LASTEXITCODE）" }
} else {
    Write-Host '[5/6] 跳过 Jvedio.Core（项目不存在）'
}

# 6. 构建主项目
$target = if ($Clean) { 'Rebuild' } else { 'Build' }
Write-Host "[6/6] 构建主项目 ($Configuration, $target) ..."
& $msbuild $proj /t:$target /p:Configuration=$Configuration /v:m /nologo
if ($LASTEXITCODE -ne 0) { throw "构建失败（exit $LASTEXITCODE）" }

$output = Join-Path $repoRoot "Jvedio-WPF\Jvedio\bin\$Configuration"
$exe = Join-Path $output 'Jvedio.exe'
if (Test-Path -LiteralPath $exe) {
    Write-Host ''
    Write-Host "构建成功: $exe" -ForegroundColor Green
    Write-Host "输出目录: $output"
} else {
    throw "构建未产生 Jvedio.exe：$exe"
}
