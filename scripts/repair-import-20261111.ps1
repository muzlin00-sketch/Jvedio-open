# 修复 2026-11-11 那次导入事故留下的脏数据（一次性，幂等）
#
# 背景：
#   旧 ORM 把继承来的列（DBId/Title/Path…）拼进 metadata_video 的 INSERT，
#   导致 11 条新记录只写进 metadata、没写进 metadata_video；
#   同时 DataType 被写成字符串 'Video'，被列表的 DataType='0' 过滤掉。
#   这 11 条在界面上永远看不到，且每次重扫都会再复制一份。
#
# 做的事（只碰 metadata / metadata_video，不碰收藏与播放记录）：
#   1. 删掉 metadata 里 DataType 不是整数的孤儿行（它们是坏行，重扫会重新导入）
#   2. 把 metadata_video.VID 的已知标记尾缀归一化（C/CH/CHS/CHT/U/UC/HD/FHD）
#
# 用法：
#   pwsh -NoProfile -File .\scripts\repair-import-20261111.ps1                  # 只预览
#   pwsh -NoProfile -File .\scripts\repair-import-20261111.ps1 -Apply           # 实际执行
#   pwsh -NoProfile -File .\scripts\repair-import-20261111.ps1 -Apply -DbPath 'D:\app\Jvedio-5.4\data\linmz\app_datas.sqlite'

param(
    [string]$DbPath = 'D:\app\Jvedio-5.4\data\linmz\app_datas.sqlite',
    [string]$SQLiteDll = 'D:\app\Jvedio-5.4\System.Data.SQLite.dll',
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $DbPath)) { throw "找不到数据库：$DbPath" }
if (-not (Test-Path -LiteralPath $SQLiteDll)) { throw "找不到 SQLite 程序集：$SQLiteDll" }

# 程序运行时数据库被占用，写操作必须先关程序
$running = Get-Process -Name 'Jvedio' -ErrorAction SilentlyContinue
if ($running -and $Apply) {
    $running | ForEach-Object { Write-Host ('  运行中: PID=' + $_.Id + '  ' + $_.Path) }
    throw 'Jvedio 正在运行，先关掉再执行 -Apply（只预览则不受影响）'
}

Add-Type -Path $SQLiteDll
$conn = New-Object System.Data.SQLite.SQLiteConnection("Data Source=$DbPath;Version=3;")
$conn.Open()

function Scalar([string]$sql) {
    $c = $conn.CreateCommand(); $c.CommandText = $sql
    return $c.ExecuteScalar()
}

# NormalizeVID 的等价实现（与 Jvedio.Identity.Identify.NormalizeVID 一致）
$TrailingLetterSuffixes = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
@('C', 'CH', 'CHS', 'CHT', 'U', 'UC', 'HD', 'FHD') | ForEach-Object { [void]$TrailingLetterSuffixes.Add($_) }

function Test-AsciiLetter([char]$ch) {
    return (($ch -ge 'A' -and $ch -le 'Z') -or ($ch -ge 'a' -and $ch -le 'z'))
}

function Get-NormalizedVID([string]$vid) {
    if ([string]::IsNullOrEmpty($vid)) { return $vid }
    $end = $vid.Length
    while ($end -gt 0 -and (Test-AsciiLetter $vid[$end - 1])) { $end-- }
    if ($end -eq $vid.Length -or $end -eq 0) { return $vid }
    if (-not [char]::IsDigit($vid[$end - 1])) { return $vid }
    $suffix = $vid.Substring($end)
    if (-not $TrailingLetterSuffixes.Contains($suffix.ToUpperInvariant())) { return $vid }
    return $vid.Substring(0, $end)
}

try {
    Write-Host '=== 当前状态 ==='
    Write-Host ('  metadata          : ' + (Scalar 'SELECT COUNT(*) FROM metadata'))
    Write-Host ('  metadata_video    : ' + (Scalar 'SELECT COUNT(*) FROM metadata_video'))
    $orphanSql = 'SELECT COUNT(*) FROM metadata m LEFT JOIN metadata_video v ON m.DataID=v.MVID WHERE v.MVID IS NULL'
    Write-Host ('  孤儿行            : ' + (Scalar $orphanSql))
    $badTypeSql = "SELECT COUNT(*) FROM metadata WHERE typeof(DataType) <> 'integer'"
    Write-Host ("  DataType 非整数   : " + (Scalar $badTypeSql))

    Write-Host ''
    Write-Host '=== 将被删除的孤儿行 ==='
    $c = $conn.CreateCommand()
    $c.CommandText = 'SELECT m.DataID, typeof(m.DataType), m.DataType, m.Path, m.FirstScanDate FROM metadata m LEFT JOIN metadata_video v ON m.DataID=v.MVID WHERE v.MVID IS NULL ORDER BY m.DataID'
    $r = $c.ExecuteReader()
    $orphans = @()
    while ($r.Read()) {
        $orphans += [pscustomobject]@{
            DataID = $r.GetValue(0); TypeOf = $r.GetValue(1); RawType = $r.GetValue(2)
            Path = $r.GetValue(3); ScanDate = $r.GetValue(4)
        }
    }
    $r.Close()
    $orphans | ForEach-Object { Write-Host ('  ' + $_.DataID + '  DataType=' + $_.TypeOf + ':' + $_.RawType + '  ' + $_.ScanDate + '  ' + $_.Path) }

    Write-Host ''
    Write-Host '=== 将被归一化的 VID ==='
    $c2 = $conn.CreateCommand()
    $c2.CommandText = 'SELECT MVID, DataID, VID FROM metadata_video WHERE VID IS NOT NULL AND VID <> ""'
    $r2 = $c2.ExecuteReader()
    $vidFixes = @()
    while ($r2.Read()) {
        $old = $r2.GetValue(2)
        $new = Get-NormalizedVID $old
        if ($new -ne $old) { $vidFixes += [pscustomobject]@{ MVID = $r2.GetValue(0); DataID = $r2.GetValue(1); Old = $old; New = $new } }
    }
    $r2.Close()
    if ($vidFixes.Count -eq 0) { Write-Host '  （无）' }
    $vidFixes | ForEach-Object { Write-Host ('  MVID=' + $_.MVID + '  ' + $_.Old + ' -> ' + $_.New) }

    if (-not $Apply) {
        Write-Host ''
        Write-Host '预览模式，未做任何修改。确认无误后加 -Apply 执行。'
        return
    }

    $backup = $DbPath + '.bak-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
    Copy-Item -LiteralPath $DbPath -Destination $backup -Force
    Write-Host ''
    Write-Host ('=== 已备份数据库：' + $backup + ' ===')

    $tx = $conn.BeginTransaction()
    try {
        $deleted = 0
        foreach ($o in $orphans) {
            $d = $conn.CreateCommand()
            $d.Transaction = $tx
            $d.CommandText = 'DELETE FROM metadata WHERE DataID=' + [long]$o.DataID
            $deleted += $d.ExecuteNonQuery()
        }
        $fixed = 0
        foreach ($v in $vidFixes) {
            $u = $conn.CreateCommand()
            $u.Transaction = $tx
            $u.CommandText = "UPDATE metadata_video SET VID='" + $v.New.Replace("'", "''") + "' WHERE MVID=" + [long]$v.MVID
            $fixed += $u.ExecuteNonQuery()
        }
        $tx.Commit()
        Write-Host ('  已删除孤儿行: ' + $deleted)
        Write-Host ('  已归一化 VID: ' + $fixed)
    }
    catch {
        $tx.Rollback()
        throw ('执行失败已回滚: ' + $_.Exception.Message)
    }

    Write-Host ''
    Write-Host '=== 执行后状态 ==='
    Write-Host ('  metadata          : ' + (Scalar 'SELECT COUNT(*) FROM metadata'))
    Write-Host ('  metadata_video    : ' + (Scalar 'SELECT COUNT(*) FROM metadata_video'))
    Write-Host ('  孤儿行            : ' + (Scalar $orphanSql))
    Write-Host ("  DataType 非整数   : " + (Scalar $badTypeSql))
    Write-Host ''
    Write-Host '下一步：启动程序，重新扫描那个目录，11 个文件会正常入库。'
}
finally {
    $conn.Close()
}
