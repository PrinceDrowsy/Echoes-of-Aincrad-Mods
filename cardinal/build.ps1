# Builds Cardinal: .\build.ps1 [build|test|publish|release|run] [args for run or test]
#   build     debug build of the solution
#   test      runs the tests; the ones that read the game use Cardinal's saved settings, or CARDINAL_TEST_SETTINGS
#   publish   single self-contained Cardinal.exe in cardinal\publish
#   release   publish, then the Nexus zips (Cardinal and Cardinal Mappings) and their SHA-256 in cardinal\release
#   run       build, then run Cardinal with the remaining arguments
# The SDK and every cache live under CARDINAL_TOOLS (default D:\dev-tools) and temp files under CARDINAL_SCRATCH
# (default obj\scratch here), so nothing lands in the user profile.
param(
    [ValidateSet('build', 'test', 'publish', 'release', 'run')]
    [string]$Command = 'build',
    [Parameter(ValueFromRemainingArguments)]
    [string[]]$Arguments
)
$ErrorActionPreference = 'Stop'

$tools = if ($env:CARDINAL_TOOLS) { $env:CARDINAL_TOOLS } else { 'D:\dev-tools' }
$scratch = if ($env:CARDINAL_SCRATCH) { $env:CARDINAL_SCRATCH } else { "$PSScriptRoot\obj\scratch" }
New-Item -ItemType Directory -Force $scratch | Out-Null

$env:DOTNET_ROOT = "$tools\dotnet"
$env:DOTNET_MULTILEVEL_LOOKUP = '0'
$env:DOTNET_CLI_HOME = "$tools\dotnet-home"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_NOLOGO = '1'
$env:NUGET_PACKAGES = "$tools\nuget"
$env:NUGET_HTTP_CACHE_PATH = "$tools\nuget-http"
$env:NUGET_PLUGINS_CACHE_PATH = "$tools\nuget-plugins"
$env:NUGET_SCRATCH = "$scratch\nuget-scratch"
$env:TMP = $scratch
$env:TEMP = $scratch

$dotnet = "$tools\dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) {
    throw "No .NET SDK at $dotnet. Install the .NET 10 SDK there with dotnet-install.ps1 -Channel 10.0 -InstallDir $tools\dotnet, or set CARDINAL_TOOLS."
}

$app = "$PSScriptRoot\src\Cardinal\Cardinal.csproj"
$out = "$PSScriptRoot\publish"

function Publish {
    # Publishing over an earlier single-file publish drops WPF's native DLLs, so always start empty.
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    & $dotnet publish $app -c Release -o $out -v q -nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    if (-not (Test-Path "$out\retoc.exe")) { Write-Warning 'retoc.exe was not found (tools\retoc, or set RetocExe), so this build cannot make mods.' }
    if (-not (Test-Path "$out\repak.exe")) { Write-Warning 'repak.exe was not found (tools\repak, or set RepakExe), so this build cannot make text mods.' }
    # retoc downloads Oodle beside itself when run; a copy left from testing must never ship.
    Get-ChildItem $out -Filter 'oo2core*.dll' | Remove-Item
    New-Item -ItemType Directory -Force "$out\licenses" | Out-Null
    Copy-Item "$PSScriptRoot\README.md", "$PSScriptRoot\Read me.txt", "$PSScriptRoot\THIRD_PARTY_NOTICES.md" $out
    Copy-Item "$PSScriptRoot\licenses\*" "$out\licenses"
    Copy-Item "$tools\dotnet\LICENSE.txt" "$out\licenses\dotnet-LICENSE.txt"
    Copy-Item "$tools\dotnet\ThirdPartyNotices.txt" "$out\licenses\dotnet-ThirdPartyNotices.txt"
    if (Test-Path "$PSScriptRoot\..\LICENSE") { Copy-Item "$PSScriptRoot\..\LICENSE" $out }
    else { Write-Warning 'No LICENSE at the repo root yet; add it before releasing.' }
}

# Each zip holds one folder, so players extract Cardinal anywhere and drop CardinalMappings into ue4ss\Mods as it is.
function Release {
    Publish
    foreach ($required in 'retoc.exe', 'repak.exe', 'LICENSE') {
        if (-not (Test-Path "$out\$required")) { throw "A release needs $required in $out." }
    }
    if ($bad = Get-ChildItem $out -Recurse -File | Where-Object { $_.Extension -in '.usmap', '.pdb', '.locres', '.uasset', '.utoc', '.ucas' -or $_.Name -like 'oo2core*' }) {
        throw "Game files, mappings, Oodle or debug files must never ship: $($bad.Name -join ', ')"
    }

    $version = (Select-Xml -Path $app -XPath '//Version').Node.InnerText
    $release = "$PSScriptRoot\release"
    $stage = "$scratch\release"
    foreach ($folder in $release, $stage) {
        if (Test-Path $folder) { Remove-Item $folder -Recurse -Force }
        New-Item -ItemType Directory $folder | Out-Null
    }
    Copy-Item $out "$stage\Cardinal" -Recurse

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    # Windows PowerShell's zip writer uses backslashes in entry names by default, which other extractors reject.
    [AppContext]::SetSwitch('Switch.System.IO.Compression.ZipFile.UseBackslash', $false)
    $zip = "$release\Cardinal-$version.zip"
    [IO.Compression.ZipFile]::CreateFromDirectory("$stage\Cardinal", $zip, 'Optimal', $true)
    # Packed like every other Lua mod, so it carries the shared scripts it needs.
    $mappings = & "$PSScriptRoot\..\dev.ps1" pack CardinalMappings $version $release
    $files = @("$out\Cardinal.exe", $zip, $mappings)
    $hashes = $files | ForEach-Object { "$((Get-FileHash $_ -Algorithm SHA256).Hash)  $(Split-Path $_ -Leaf)" }
    $hashes | Set-Content "$release\SHA256.txt" -Encoding Ascii
    $hashes
}

switch ($Command) {
    'build' { & $dotnet build "$PSScriptRoot\Cardinal.slnx" -v q -nologo }
    'test' { & $dotnet run --project "$PSScriptRoot\tests\Cardinal.Tests\Cardinal.Tests.csproj" -v q -- @Arguments }
    'publish' { Publish }
    'release' { Release }
    'run' {
        & $dotnet build $app -v q -nologo
        if ($LASTEXITCODE -eq 0) { & "$PSScriptRoot\src\Cardinal\bin\Debug\net10.0-windows\win-x64\Cardinal.exe" @Arguments }
    }
}
exit $LASTEXITCODE
