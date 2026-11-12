param(
    [string]$Version = '1.0.5-skins3d.6',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts')
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(-[a-zA-Z0-9.-]+)?$') { throw 'Invalid version' }
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$buildRoot = Join-Path $outputRoot ('package-' + [Guid]::NewGuid().ToString('N'))
$publishRoot = Join-Path $buildRoot 'publish'
$stageRoot = Join-Path $buildRoot 'stage'
$pluginRoot = Join-Path $stageRoot 'addons/counterstrikesharp/plugins/CustomWeapons'
$configRoot = Join-Path $stageRoot 'addons/counterstrikesharp/configs/plugins/CustomWeapons'
$gamedataRoot = Join-Path $stageRoot 'addons/counterstrikesharp/gamedata'
dotnet publish (Join-Path $repoRoot 'src/CustomWeapons/CustomWeapons.csproj') -c Release --no-restore -o $publishRoot
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
New-Item -ItemType Directory -Force -Path $pluginRoot, $configRoot | Out-Null
New-Item -ItemType Directory -Force -Path $gamedataRoot | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot 'gamedata/customweapons.json') -Destination $gamedataRoot
Get-ChildItem -LiteralPath $publishRoot -File | Where-Object { $_.Extension -in '.dll', '.json', '.pdb' } |
    Copy-Item -Destination $pluginRoot
if (Test-Path (Join-Path $pluginRoot 'CounterStrikeSharp.API.dll')) { throw 'Do not redistribute the server API in the plugin folder' }
Copy-Item -LiteralPath (Join-Path $repoRoot 'configs/CustomWeapons.json') -Destination $configRoot
foreach ($file in 'README.md', 'CHANGELOG.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'WEBSITE-INTEGRATION.md') {
    Copy-Item -LiteralPath (Join-Path $repoRoot $file) -Destination $stageRoot
}
Copy-Item -LiteralPath (Join-Path $repoRoot 'licenses') -Destination $stageRoot -Recurse
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs') -Destination $stageRoot -Recurse
Copy-Item -LiteralPath (Join-Path $repoRoot 'configs') -Destination $stageRoot -Recurse
Copy-Item -LiteralPath (Join-Path $repoRoot 'sql') -Destination $stageRoot -Recurse
$archive = Join-Path $outputRoot "CustomWeapons-v$Version.zip"
Compress-Archive -Path (Join-Path $stageRoot '*') -DestinationPath $archive -Force
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    $prefix = 'addons/counterstrikesharp/plugins/CustomWeapons/'
    foreach ($required in 'CustomWeapons.dll', 'CustomWeapons.Core.dll', 'CustomWeapons.deps.json', 'CustomWeapons.runtimeconfig.json', 'MySqlConnector.dll', 'Microsoft.Extensions.Logging.Abstractions.dll', 'Microsoft.Extensions.DependencyInjection.Abstractions.dll') {
        if ($null -eq $zip.GetEntry($prefix + $required)) { throw "Missing dependency: $required" }
    }
    $entry = $zip.GetEntry('addons/counterstrikesharp/configs/plugins/CustomWeapons/CustomWeapons.json')
    if ($null -eq $entry) { throw 'Missing configuration' }
    if ($null -eq $zip.GetEntry('addons/counterstrikesharp/gamedata/customweapons.json')) { throw 'Missing economy gamedata' }
    foreach ($required in 'WEBSITE-INTEGRATION.md', 'sql/20261111_website_selections.sql') {
        if ($null -eq $zip.GetEntry($required)) { throw "Missing website setup file: $required" }
    }
    $reader = [IO.StreamReader]::new($entry.Open())
    try { $config = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    if ($config.Database.Password -ne '') { throw 'Never package real credentials' }
    if ($zip.Entries.FullName -match '(^|/)(data|obj|bin)/|CounterStrikeSharp.API.dll$') { throw 'Unexpected runtime data or build files in archive' }
} finally { $zip.Dispose() }
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath (Join-Path $outputRoot 'SHA256SUMS.txt') -Value "$hash  CustomWeapons-v$Version.zip" -Encoding utf8NoBOM
Write-Output $archive
