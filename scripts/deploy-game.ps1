param([string]$GameDir = 'G:\SteamLibrary\steamapps\common\Probably Stolen')
. "$PSScriptRoot/env.ps1"
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
$running = Get-Process | Where-Object { $_.Path -and $_.Path.StartsWith($GameDir + '\', [StringComparison]::OrdinalIgnoreCase) }
if ($running) { throw 'Close the game before deploying.' }
& "$PSScriptRoot/build-game.ps1" -GameDir $GameDir
& "$PSScriptRoot/package-game.ps1"
$revision = (& git -C $RepoRoot rev-parse --short=7 HEAD).Trim()
[xml]$manifest = Get-Content "$RepoRoot/assets/game/manifest.xml"
$label = $manifest.Manifest.GameVersions.Version
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
Write-Host "Deployed to $destination"
