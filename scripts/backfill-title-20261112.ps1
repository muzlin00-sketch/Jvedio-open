# Backfill metadata.Title with the file name (extension stripped) for rows whose Title
# is NULL or blank.
#
# Why:
#   The video list sorts by metadata.Title but displays the file name when Title is empty
#   (Video.SetTitleAndDate). So "sort by title" looked completely random: every row had an
#   empty Title (short-clip library: 0 of 1004 rows had one), the ORDER BY compared empty
#   strings, and only the displayed fallback text had any meaning.
#   New imports now write the file name into Title at insert time
#   (ScanHelper.ParseVideo / ScanTask.InsertData); this script fixes the existing rows.
#
# What it does (metadata only -- metadata_video, tags, play records are untouched):
#   1. scans every row of metadata
#   2. for rows with Title NULL/blank, derives the base name from Path
#   3. with -Apply: backs up the database file, then updates all of them in ONE transaction
#
# Notes:
#   - dry-run by default, nothing is written unless -Apply is given
#   - idempotent: only blank titles are touched, so re-running is harmless
#   - the update re-checks "title is blank" in SQL, so a concurrent edit is never overwritten
#
# Usage:
#   pwsh -NoProfile -File .\scripts\backfill-title-20261112.ps1                     # preview
#   pwsh -NoProfile -File .\scripts\backfill-title-20261112.ps1 -Apply              # apply
#   pwsh -NoProfile -File .\scripts\backfill-title-20261112.ps1 -Apply -DbPath 'D:\...\app_datas.sqlite'
#
#   The database path may also come from the environment variable JvedioDbPath
#   (useful when the path contains non-ASCII characters -- see the encoding note below).
#
# ENCODING: this file must stay pure 7-bit ASCII. powershell.exe 5.1 decodes .ps1 files with
# the ANSI code page (GBK on this machine), so any non-ASCII byte here would be corrupted.

param(
    [string]$DbPath = 'D:\app\Jvedio-5.4\data\linmz\app_datas.sqlite',
    [string]$SQLiteDll = 'D:\app\Jvedio-5.4\System.Data.SQLite.dll',
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'

# Allow an explicit environment override (e.g. a path with CJK characters).
if (-not $PSBoundParameters.ContainsKey('DbPath') -and $env:JvedioDbPath) {
    $DbPath = $env:JvedioDbPath
}

if (-not (Test-Path -LiteralPath $DbPath)) { throw "database not found: $DbPath" }
if (-not (Test-Path -LiteralPath $SQLiteDll)) { throw "SQLite assembly not found: $SQLiteDll" }

# The program keeps the database open while running; -Apply must not race it.
$proc = Get-Process -Name 'Jvedio' -ErrorAction SilentlyContinue
if ($proc -and $Apply) {
    $proc | ForEach-Object { Write-Host ('  running: PID=' + $_.Id + '  ' + $_.Path) }
    throw 'Jvedio is running. Close it before -Apply (preview is fine).'
}

Add-Type -Path $SQLiteDll

# Preview opens read-only; -Apply opens read-write (after the backup).
$mode = if ($Apply) { 'ReadWrite' } else { 'ReadOnly' }
$conn = New-Object System.Data.SQLite.SQLiteConnection("Data Source=$DbPath;Version=3;Read Only=$([bool](-not $Apply));")
$conn.Open()

function Scalar([string]$sql) {
    $c = $conn.CreateCommand()
    $c.CommandText = $sql
    return $c.ExecuteScalar()
}

$blankSql = "Title IS NULL OR trim(Title) = ''"

try {
    Write-Host '=== scan ==='
    Write-Host ('  db                : ' + $DbPath)
    Write-Host ('  mode              : ' + $mode)
    Write-Host ('  metadata rows     : ' + (Scalar 'SELECT COUNT(*) FROM metadata'))
    Write-Host ('  blank title rows  : ' + (Scalar ("SELECT COUNT(*) FROM metadata WHERE " + $blankSql)))

    # Read every row once; classify in memory so the counts below are exact and the
    # preview can show what would change before anything is written.
    $c = $conn.CreateCommand()
    $c.CommandText = 'SELECT DataID, Path, Title FROM metadata ORDER BY DataID'
    $r = $c.ExecuteReader()

    $scanned = 0
    $alreadyTitled = 0
    $pending = New-Object System.Collections.ArrayList
    $skipped = 0

    while ($r.Read()) {
        $scanned++
        $dataId = $r.GetValue(0)
        $path = if ($r.IsDBNull(1)) { '' } else { [string]$r.GetValue(1) }
        $title = if ($r.IsDBNull(2)) { '' } else { [string]$r.GetValue(2) }

        if (-not [string]::IsNullOrWhiteSpace($title)) {
            $alreadyTitled++
            continue
        }

        $name = ''
        if (-not [string]::IsNullOrWhiteSpace($path)) {
            try { $name = [System.IO.Path]::GetFileNameWithoutExtension($path) } catch { $name = '' }
        }
        if ([string]::IsNullOrWhiteSpace($name)) {
            $skipped++
            continue
        }

        [void]$pending.Add([pscustomobject]@{ DataID = [long]$dataId; Title = $name; Path = $path })
    }
    $r.Close()

    Write-Host ''
    Write-Host '=== result ==='
    Write-Host ('  scanned           : ' + $scanned)
    Write-Host ('  to update         : ' + $pending.Count)
    Write-Host ('  skipped (no path) : ' + $skipped)
    Write-Host ('  already has title : ' + $alreadyTitled)

    if ($pending.Count -gt 0) {
        Write-Host ''
        Write-Host '=== first 20 rows to update ==='
        $pending | Select-Object -First 20 | ForEach-Object {
            Write-Host ('  DataID=' + $_.DataID + '  ->  ' + $_.Title)
        }
        if ($pending.Count -gt 20) { Write-Host ('  ... and ' + ($pending.Count - 20) + ' more') }
    }

    if (-not $Apply) {
        Write-Host ''
        Write-Host 'Preview only, nothing was written. Re-run with -Apply to execute.'
        return
    }

    $backup = $DbPath + '.bak-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
    Copy-Item -LiteralPath $DbPath -Destination $backup -Force
    Write-Host ''
    Write-Host ('=== database backed up: ' + $backup + ' ===')

    $tx = $conn.BeginTransaction()
    try {
        $updated = 0
        foreach ($item in $pending) {
            $u = $conn.CreateCommand()
            $u.Transaction = $tx
            # Re-check blankness in SQL: an edit made between the scan and now wins.
            $u.CommandText = "UPDATE metadata SET Title = @title WHERE DataID = @id AND ($blankSql)"
            [void]$u.Parameters.AddWithValue('@title', $item.Title)
            [void]$u.Parameters.AddWithValue('@id', $item.DataID)
            $updated += $u.ExecuteNonQuery()
        }
        $tx.Commit()
        Write-Host ('  updated rows      : ' + $updated)
        if ($updated -ne $pending.Count) {
            Write-Host ('  WARNING: ' + ($pending.Count - $updated) + ' row(s) changed while this script ran and were left alone')
        }
    }
    catch {
        $tx.Rollback()
        throw ('update failed, rolled back: ' + $_.Exception.Message)
    }

    Write-Host ''
    Write-Host '=== after ==='
    Write-Host ('  blank title rows  : ' + (Scalar ("SELECT COUNT(*) FROM metadata WHERE " + $blankSql)))
    Write-Host ''
    Write-Host 'Done. Restart Jvedio; the list now sorts by Title and every row shows its file name.'
}
finally {
    $conn.Close()
}
