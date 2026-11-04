# Test helper: .\dev.ps1 <command> [name]
#   deploy <mod>     copy mods\<mod> into the game's UE4SS Mods folder
#   undeploy <mod>   remove it from the game
#   backup [label]   snapshot the saves into backups\
#   restore <name>   restore a snapshot, taking a safety snapshot first
#   backups          list snapshots
#   play / stop      launch through Steam or close the game
#   log [text]       show the current UE4SS log, optionally only lines containing text
param(
    [Parameter(Mandatory, Position = 0)]
    [ValidateSet('deploy', 'undeploy', 'backup', 'restore', 'backups', 'play', 'stop', 'log')]
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

switch ($Command) {
    'deploy' {
        Assert-GameClosed
        if (-not (Test-Path "$PSScriptRoot\mods\$Name\Scripts\main.lua")) { throw "No mod named '$Name'." }
        $target = "$Win64\ue4ss\Mods\$Name"
        if (Test-Path $target) { Remove-Item $target -Recurse -Force }
        Copy-Item "$PSScriptRoot\mods\$Name" "$Win64\ue4ss\Mods" -Recurse
        "Deployed $Name"
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
