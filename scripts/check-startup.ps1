<#
.SYNOPSIS
    Jvedio 启动自检：端口存活 + 页面/接口可达 + 今日日志无 SQL 列错误。

.DESCRIPTION
    「端口活着」不等于「程序能用」。列表查询列名写错时（历史上把 metadata_video.ExtraInfo
    写成 metadata.ExtraInfo），服务照样起、/api/health 照样 200，但主界面列表是空的、
    日志里只有一段 SQLite 栈。本脚本把三层一起查：

      1. TCP 端口在监听
      2. GET / 返回 200；/api/health ok=true 且 stale=false
      3. GET /api/videos 不返回 5xx（401 表示开了访问口令，属正常）
      4. 今日日志不含「列表查询列校验失败」「no such column」

    /api/health 只反映静态文件资源，不碰数据库，所以必须叠加第 3、4 项才挡得住这类故障。

.PARAMETER Port
    局域网服务端口。开发版 5201、安装版 5200。

.PARAMETER AppDir
    程序目录（含 data\ 的那一层）。给了才做日志检查；不给只查 HTTP 层。

.EXAMPLE
    pwsh -NoProfile -File .\scripts\check-startup.ps1 -Port 5201 -AppDir 'D:\app\Jvedio-dev'

.EXAMPLE
    pwsh -NoProfile -File .\scripts\check-startup.ps1 -Port 5200 -AppDir 'D:\app\Jvedio-5.4'
#>
param(
    [int]$Port = 5201,
    [string]$AppDir = '',
    [int]$TimeoutSeconds = 5
)

$ErrorActionPreference = 'Stop'
$base = "http://127.0.0.1:$Port"
$pass = New-Object System.Collections.Generic.List[string]
$fail = New-Object System.Collections.Generic.List[string]

function Get-Http([string]$url) {
    try {
        return Invoke-WebRequest -Uri $url -TimeoutSec $TimeoutSeconds -UseBasicParsing -SkipHttpErrorCheck
    } catch {
        return $null
    }
}

Write-Host "== Jvedio 启动自检 $base =="

# 1. 端口
if (Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue) {
    $pass.Add("端口 $Port 在监听")
} else {
    $fail.Add("端口 $Port 没有监听：程序没起来，或设置里没开局域网服务")
}

# 2. 页面与 health
$page = Get-Http "$base/"
if ($page -and $page.StatusCode -eq 200) {
    $pass.Add("GET / -> 200 ($($page.RawContentLength) 字节)")
} else {
    $fail.Add("GET / 失败" + $(if ($page) { "（HTTP $($page.StatusCode)）" } else { '（无响应）' }))
}

$health = Get-Http "$base/api/health"
if ($health -and $health.StatusCode -eq 200) {
    try {
        $h = $health.Content | ConvertFrom-Json
        if ($h.ok) {
            $pass.Add("health ok=true, servedFrom=$($h.servedFrom), stale=$($h.stale)")
            if ($h.stale) {
                $fail.Add('health stale=true：磁盘 Web\index.html 与内嵌资源不一致，重新部署这一份文件')
            }
        } else {
            $fail.Add("health ok=false：$($h.error)")
        }
    } catch {
        $fail.Add("health 返回不是合法 JSON：$($health.Content)")
    }
} else {
    $fail.Add('GET /api/health 失败或返回非 200')
}

# 3. 列表接口（真连数据库的那条路）
$videos = Get-Http "$base/api/videos?page=1&pageSize=3"
if ($videos) {
    if ($videos.StatusCode -ge 500) {
        $fail.Add("GET /api/videos -> HTTP $($videos.StatusCode)：列表查询服务端报错")
    } else {
        $pass.Add("GET /api/videos -> HTTP $($videos.StatusCode)（401 = 开了访问口令，属正常）")
    }
} else {
    $fail.Add('GET /api/videos 无响应')
}

# 4. 今日日志
if ($AppDir) {
    $dataDir = Join-Path $AppDir 'data'
    $logPath = $null
    if (Test-Path -LiteralPath $dataDir) {
        $logPath = Get-ChildItem -LiteralPath $dataDir -Directory -ErrorAction SilentlyContinue |
            ForEach-Object { Join-Path $_.FullName 'log' } |
            Where-Object { Test-Path -LiteralPath $_ } |
            Select-Object -First 1
    }
    if (-not $logPath) {
        $fail.Add("找不到日志目录：$dataDir\<用户>\log")
    } else {
        $today = Join-Path $logPath ((Get-Date).ToString('yyyy-MM-dd') + '.log')
        if (-not (Test-Path -LiteralPath $today)) {
            $fail.Add("今天还没有日志：$today（程序可能根本没启动）")
        } else {
            # 只检查「最后一次启动之后」的日志：同一份日志文件里可能还留着当天早些时候的
            # 崩溃记录（例如修复前那次启动），全量扫描会把历史错误当成本次失败。
            $lines = @(Get-Content -LiteralPath $today -Encoding UTF8)
            $start = 0
            for ($i = $lines.Count - 1; $i -ge 0; $i--) {
                if ($lines[$i] -match 'app init, version') { $start = $i; break }
            }
            $session = @($lines | Select-Object -Skip $start)
            $bad = @($session | Select-String -Pattern '列表查询列校验失败|no such column')
            if ($bad.Count -gt 0) {
                $fail.Add("本次启动的日志命中 SQL 列错误 $($bad.Count) 处：")
                $bad | Select-Object -First 5 | ForEach-Object { $fail.Add('    ' + $_.Line) }
            } else {
                $pass.Add("本次启动（最后 $($session.Count) 行日志）无 SQL 列错误")
            }
        }
    }
} else {
    Write-Host '  [SKIP] 未给 -AppDir，跳过日志检查'
}

$pass | ForEach-Object { Write-Host "  [OK]   $_" }
$fail | ForEach-Object { Write-Host "  [FAIL] $_" }

if ($fail.Count -gt 0) {
    Write-Host '== 自检失败 =='
    exit 1
}
Write-Host '== 自检通过 =='
