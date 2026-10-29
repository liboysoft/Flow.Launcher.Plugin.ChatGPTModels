param(
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9-]*$')][string]$GitHubOwner,
    [ValidatePattern('^[a-zA-Z0-9_.-]+$')][string]$Repository = 'Flow.Launcher.Plugin.ChatGPTModels'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$pluginPath = Join-Path $projectRoot 'plugin.json'
$plugin = Get-Content -LiteralPath $pluginPath -Raw | ConvertFrom-Json
$plugin.Website = "https://github.com/$GitHubOwner/$Repository"
$plugin | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $pluginPath -Encoding utf8
$manifest = [ordered]@{}
foreach ($field in @('ID','Name','Description','Author','Version','Language','Website','MinimumAppVersion')) {
    $manifest[$field] = $plugin.$field
}
$manifest.UrlDownload = "$($plugin.Website)/releases/download/v$($plugin.Version)/Flow.Launcher.Plugin.ChatGPTModels.zip"
$manifest.UrlSourceCode = "$($plugin.Website)/tree/main"
$manifest.IcoPath = "https://cdn.jsdelivr.net/gh/$GitHubOwner/$Repository@v$($plugin.Version)/Images/app.png"
$outputDirectory = Join-Path $projectRoot 'publishing'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$manifestPath = Join-Path $outputDirectory "ChatGPT Models-$($plugin.ID).json"
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8
Write-Output "Website configured: $($plugin.Website)"
Write-Output "Store submission manifest: $manifestPath"
