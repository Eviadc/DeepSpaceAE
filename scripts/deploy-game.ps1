param([string]$GameDir)
. "$PSScriptRoot/env.ps1"
. "$PSScriptRoot/game-path.ps1"
$GameDir = Get-DeepSpaceGameDir $GameDir
$running = Get-Process | Where-Object { $_.Path -and $_.Path.StartsWith($GameDir + '\', [StringComparison]::OrdinalIgnoreCase) }
if ($running) { throw 'Close the game before deploying.' }
& "$PSScriptRoot/build-game.ps1" -GameDir $GameDir
& "$PSScriptRoot/package-game.ps1"
$revision = (& git -C $RepoRoot rev-parse --short=7 HEAD).Trim()
[xml]$manifest = Get-Content -LiteralPath "$RepoRoot/assets/game/manifest.xml" -Raw -Encoding UTF8
# Archive names use the verified API/build target, while GameVersions uses the
# game's separate Unity Application.version for the Mods menu's compatibility UI.
$label = '100-Rev6'
$package = Join-Path $RepoRoot "artifacts/packages/DeepSpaceAE-$($manifest.Manifest.ModVersion)-$label-$revision.zip"
$stage = Join-Path $RepoRoot ('artifacts/deploy/' + [guid]::NewGuid().ToString('N'))
Expand-Archive -LiteralPath $package -DestinationPath $stage
$running = Get-Process | Where-Object { $_.Path -and $_.Path.StartsWith($GameDir + '\', [StringComparison]::OrdinalIgnoreCase) }
if ($running) { throw 'Game started during build; close it before deploying.' }
$mods = Join-Path $GameDir 'Mods'
New-Item -ItemType Directory -Force -Path $mods | Out-Null
$destination = Join-Path $mods 'DeepSpaceAE'
if (Test-Path -LiteralPath $destination) {
    $backup = Join-Path $RepoRoot ('artifacts/mod-backups/' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    New-Item -ItemType Directory -Force -Path $backup | Out-Null
    Copy-Item -LiteralPath $destination -Destination $backup -Recurse
    Write-Host "Previous mod backed up to $backup"
}
Copy-Item -Path "$stage/DeepSpaceAE/*" -Destination (New-Item -ItemType Directory -Force -Path $destination).FullName -Recurse -Force
# These standalone bus states were retired; installed directional states remain.
foreach ($retired in @('item-storage-bus-off.png', 'import-bus-off.png', 'export-bus-off.png')) {
    $retiredPath = Join-Path $destination ('Textures/Items/' + $retired)
    if (Test-Path -LiteralPath $retiredPath) { Remove-Item -LiteralPath $retiredPath }
}
Write-Host "Deployed to $destination"
