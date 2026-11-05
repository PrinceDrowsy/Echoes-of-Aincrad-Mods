# Builds Cardinal: .\build.ps1 [build|publish|run] [args for run]
#   build     debug build of the solution
#   publish   single self-contained Cardinal.exe in cardinal\publish
#   run       build, then run Cardinal with the remaining arguments
# The SDK and every cache live under CARDINAL_TOOLS (default D:\dev-tools) and temp files under CARDINAL_SCRATCH
# (default obj\scratch here), so nothing lands in the user profile.
param(
    [ValidateSet('build', 'publish', 'run')]
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
switch ($Command) {
    'build' { & $dotnet build "$PSScriptRoot\Cardinal.slnx" -v q -nologo }
    'publish' {
        $out = "$PSScriptRoot\publish"
        & $dotnet publish $app -c Release -o $out -v q -nologo
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        New-Item -ItemType Directory -Force "$out\licenses" | Out-Null
        Copy-Item "$PSScriptRoot\README.md", "$PSScriptRoot\THIRD_PARTY_NOTICES.md" $out
        Copy-Item "$PSScriptRoot\licenses\*" "$out\licenses"
        Copy-Item "$tools\dotnet\LICENSE.txt" "$out\licenses\dotnet-LICENSE.txt"
        Copy-Item "$tools\dotnet\ThirdPartyNotices.txt" "$out\licenses\dotnet-ThirdPartyNotices.txt"
        if (Test-Path "$PSScriptRoot\..\LICENSE") { Copy-Item "$PSScriptRoot\..\LICENSE" $out }
        else { Write-Warning 'No LICENSE at the repo root yet; add it before releasing.' }
    }
    'run' {
        & $dotnet build $app -v q -nologo
        if ($LASTEXITCODE -eq 0) { & "$PSScriptRoot\src\Cardinal\bin\Debug\net10.0-windows\win-x64\Cardinal.exe" @Arguments }
    }
}
exit $LASTEXITCODE
