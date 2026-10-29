param(
    [string]$FlowLauncherDirectory,
    [string]$Dotnet
)
$ErrorActionPreference = 'Stop'
if (-not $Dotnet) {
    $localSdk = Join-Path $PSScriptRoot '.dotnet\dotnet.exe'
    $Dotnet = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
}
if (-not $FlowLauncherDirectory) {
    $app = Get-ChildItem -LiteralPath "$env:LOCALAPPDATA\FlowLauncher" -Directory -Filter 'app-*' |
        Sort-Object { [version]($_.Name -replace '^app-', '') } -Descending | Select-Object -First 1
    if (-not $app) { throw 'Pass -FlowLauncherDirectory pointing to your Flow Launcher 2.x application directory.' }
    $FlowLauncherDirectory = $app.FullName
}
$reference = Join-Path $FlowLauncherDirectory 'Flow.Launcher.Plugin.dll'
if (-not (Test-Path -LiteralPath $reference)) { throw "Missing Flow plugin API: $reference" }
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.dotnet\home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& $Dotnet build "$PSScriptRoot\src\Flow.Launcher.Plugin.ChatGPT.csproj" -c Release "-p:FlowLauncherDirectory=$FlowLauncherDirectory" --output "$PSScriptRoot\artifacts\ChatGPTModels"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath "$PSScriptRoot\plugin.json", "$PSScriptRoot\LICENSE" -Destination "$PSScriptRoot\artifacts\ChatGPTModels"
$imageOutput = Join-Path $PSScriptRoot 'artifacts\ChatGPTModels\Images'
New-Item -ItemType Directory -Path $imageOutput -Force | Out-Null
Copy-Item -LiteralPath "$PSScriptRoot\Images\app.png" -Destination (Join-Path $imageOutput 'app.png') -Force
Copy-Item -LiteralPath "$PSScriptRoot\Images\NOTICE.txt" -Destination (Join-Path $imageOutput 'NOTICE.txt') -Force
Compress-Archive -Path "$PSScriptRoot\artifacts\ChatGPTModels\*" -DestinationPath "$PSScriptRoot\artifacts\Flow.Launcher.Plugin.ChatGPTModels.zip" -Force
Write-Output "Plugin package: $PSScriptRoot\artifacts\Flow.Launcher.Plugin.ChatGPTModels.zip"
