# Test helper: .\dev.ps1 <command> [name]
#   deploy <mod>     copy mods\<mod> into the game's UE4SS Mods folder
#   undeploy <mod>   remove it from the game
#   backup [label]   snapshot the saves into backups\
#   restore <name>   restore a snapshot, taking a safety snapshot first
#   backups          list snapshots
#   play / stop      launch through Steam or close the game
#   log [text]       show the current UE4SS log, optionally only lines containing text
#   bench [label]    record memory, disk and CPU until the game closes or Ctrl+C. Ctrl+F12 in game marks load start and end
param(
    [Parameter(Mandatory, Position = 0)]
    [ValidateSet('deploy', 'undeploy', 'backup', 'restore', 'backups', 'play', 'stop', 'log', 'bench')]
    [string]$Command,
    [Parameter(Position = 1)]
    [string]$Name
)
$ErrorActionPreference = 'Stop'

$GameExe = 'EchoesofAincrad-Win64-Shipping'
$AppId = 2244210

# The game can be in any Steam library, and libraryfolders.vdf says which one holds it.
function Find-GameFolder {
    $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam').SteamPath
    $vdf = Get-Content "$steam\steamapps\libraryfolders.vdf" -Raw
    foreach ($block in $vdf -split '(?="path")') {
        if ($block -match "`"$AppId`"" -and $block -match '^"path"\s+"([^"]+)"') {
            return "$($Matches[1] -replace '\\\\', '\')\steamapps\common\Echoes of Aincrad"
        }
    }
    throw 'Echoes of Aincrad was not found in any Steam library.'
}

$Win64 = "$(Find-GameFolder)\EchoesofAincrad\Binaries\Win64"
$UeLog = "$Win64\ue4ss\UE4SS.log"
$Saves = "$env:LOCALAPPDATA\EchoesofAincrad\Saved\SaveGames"
$Backups = "$PSScriptRoot\backups"

function Assert-GameClosed {
    if (Get-Process $GameExe -ErrorAction SilentlyContinue) { throw 'Close the game first.' }
}

function Save-Snapshot([string]$Label) {
    $dest = Join-Path $Backups ((Get-Date -Format 'yyyyMMdd-HHmmss') + $(if ($Label) { "-$Label" }))
    New-Item -ItemType Directory -Force "$dest\SaveGames" | Out-Null
    Copy-Item "$Saves\*" "$dest\SaveGames" -Recurse -Force
    "Saved $(Split-Path $dest -Leaf)"
}

# Launching overwrites UE4SS.log, so keep the previous run's copy.
function Save-Log {
    if (Test-Path $UeLog) {
        New-Item -ItemType Directory -Force "$PSScriptRoot\logs" | Out-Null
        $stamp = (Get-Item $UeLog).LastWriteTime.ToString('yyyyMMdd-HHmmss')
        Copy-Item $UeLog "$PSScriptRoot\logs\UE4SS-$stamp.log" -Force
    }
}

function Write-BenchSummary([string]$Csv) {
    $rows = @(Import-Csv $Csv)
    if (-not $rows) { return }
    $peak = { param($column) ($rows | ForEach-Object { [double]$_.$column } | Measure-Object -Maximum).Maximum }
    $minRam = ($rows | ForEach-Object { [double]$_.ram_free_mb } | Measure-Object -Minimum).Minimum
    "Peak video memory: $(& $peak 'vram_used_mb') MB"
    "Peak game commit: $(& $peak 'commit_mb') MB, peak working set: $(& $peak 'working_set_mb') MB"
    "Lowest free RAM: $minRam MB, peak disk read: $(& $peak 'disk_read_mb_s') MB/s"
    $marks = @(Get-Content $UeLog | Select-String '^\[([^\]]+)\] \[Lua\] \[DevTools\] mark' | ForEach-Object { [datetime]$_.Matches[0].Groups[1].Value })
    if ($marks.Count -ge 2) { "Load time: $([int]($marks[1] - $marks[0]).TotalSeconds) s" }
    "Saved $Csv"
}

switch ($Command) {
    'deploy' {
        Assert-GameClosed
        if (-not (Test-Path "$PSScriptRoot\mods\$Name\Scripts\main.lua")) { throw "No mod named '$Name'." }
        $target = "$Win64\ue4ss\Mods\$Name"
        if (Test-Path $target) { Remove-Item $target -Recurse -Force }
        Copy-Item "$PSScriptRoot\mods\$Name" "$Win64\ue4ss\Mods" -Recurse
        Copy-Item "$PSScriptRoot\shared\*" "$target\Scripts"
        "Deployed $Name"
    }
    'bench' {
        $game = Get-Process $GameExe -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $game) { throw 'Start the game first.' }
        New-Item -ItemType Directory -Force "$PSScriptRoot\logs" | Out-Null
        $csv = "$PSScriptRoot\logs\bench-$(Get-Date -Format 'yyyyMMdd-HHmmss')$(if ($Name) { "-$Name" }).csv"
        Set-Content $csv 'time,vram_used_mb,commit_mb,working_set_mb,ram_free_mb,disk_read_mb_s,cpu_percent'
        'Recording. Press Ctrl+C to stop early.'
        $lastTime = Get-Date
        $lastRead = (Get-CimInstance Win32_Process -Filter "ProcessId=$($game.Id)").ReadTransferCount
        $lastCpu = $game.TotalProcessorTime
        try {
            while ($process = Get-Process -Id $game.Id -ErrorAction SilentlyContinue) {
                Start-Sleep 3
                $info = Get-CimInstance Win32_Process -Filter "ProcessId=$($game.Id)"
                if (-not $info) { break }
                $now = Get-Date
                $seconds = ($now - $lastTime).TotalSeconds
                $readRate = ($info.ReadTransferCount - $lastRead) / 1MB / $seconds
                $cpu = ($process.TotalProcessorTime - $lastCpu).TotalSeconds / $seconds / [Environment]::ProcessorCount * 100
                $lastTime, $lastRead, $lastCpu = $now, $info.ReadTransferCount, $process.TotalProcessorTime
                $vram = [int](nvidia-smi --query-gpu=memory.used --format=csv,noheader,nounits | Select-Object -First 1)
                $ram = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory
                $row = '{0:HH:mm:ss},{1},{2},{3},{4},{5:F1},{6:F0}'
                Add-Content $csv ([string]::Format([Globalization.CultureInfo]::InvariantCulture, $row, $now, $vram,
                    [int]($process.PrivateMemorySize64 / 1MB), [int]($process.WorkingSet64 / 1MB), [int]($ram / 1024), $readRate, $cpu))
            }
        }
        finally { Write-BenchSummary $csv }
    }
    'undeploy' {
        Assert-GameClosed
        if (-not (Test-Path "$PSScriptRoot\mods\$Name")) { throw "No mod named '$Name'." }
        Remove-Item "$Win64\ue4ss\Mods\$Name" -Recurse -Force
        "Removed $Name"
    }
    'backup' {
        Assert-GameClosed
        Save-Snapshot $Name
    }
    'restore' {
        Assert-GameClosed
        $source = "$Backups\$Name\SaveGames"
        if (-not $Name -or -not (Test-Path "$source\SaveData.sav")) { throw "No snapshot named '$Name'." }
        Save-Snapshot 'before-restore'
        Copy-Item "$source\*" $Saves -Recurse -Force
        "Restored $Name"
    }
    'backups' {
        Get-ChildItem $Backups -Directory | Sort-Object Name | ForEach-Object Name
    }
    'play' {
        Assert-GameClosed
        Save-Log
        Start-Process "steam://rungameid/$AppId"
    }
    'stop' {
        Stop-Process -Name $GameExe -ErrorAction SilentlyContinue
        Start-Sleep 3
        Save-Log
    }
    'log' {
        if ($Name) { Get-Content $UeLog | Select-String -SimpleMatch $Name | ForEach-Object Line }
        else { Get-Content $UeLog -Tail 40 }
    }
}
