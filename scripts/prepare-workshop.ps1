param([ValidateSet('0','1','2')][string]$Visibility = '2')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$revision = (& git -C $repo rev-parse --short=7 HEAD).Trim()
[xml]$manifest = Get-Content -LiteralPath (Join-Path $repo 'assets/game/manifest.xml') -Raw -Encoding UTF8
$package = Join-Path $repo "artifacts/packages/DeepSpaceAE-$($manifest.Manifest.ModVersion)-100-Rev6-$revision.zip"
if (!(Test-Path -LiteralPath $package)) { throw 'Build and package the current committed revision first.' }
$stateDir = Join-Path $repo '.deepspaceae'
New-Item -ItemType Directory -Force -Path $stateDir | Out-Null
$vdfPath = Join-Path $stateDir 'workshop-item.local.vdf'
$publishedId = '0'
if (Test-Path -LiteralPath $vdfPath) {
    $old = Get-Content -LiteralPath $vdfPath -Raw
    if ($old -notmatch '"publishedfileid"\s+"(\d+)"') { throw 'Existing Workshop state has no valid item ID; inspect it before publishing.' }
    $publishedId = $Matches[1]
}
$stage = Join-Path $repo ('artifacts/workshop/' + $revision + '-' + [guid]::NewGuid().ToString('N').Substring(0,8))
Expand-Archive -LiteralPath $package -DestinationPath $stage
$content = Join-Path $stage 'DeepSpaceAE'
# Reuse the released controller sprite unchanged as the initial cover.
$preview = Join-Path $content 'preview.png'
Copy-Item -LiteralPath (Join-Path $content 'Textures/Items/core.png') -Destination $preview
$description = (Get-Content -LiteralPath (Join-Path $repo 'docs/workshop-description.txt') -Encoding UTF8) -join ' '
function ConvertTo-VdfString([string]$Value) { return $Value.Replace('\','\\').Replace('"','\"') }
$fields = [ordered]@{
    appid = '4348910'; publishedfileid = $publishedId; contentfolder = $content
    previewfile = $preview; visibility = $Visibility; title = 'DeepSpaceAE'
    description = $description; changenote = "$($manifest.Manifest.ModVersion): storage networks, terminals, drives, drawers and machine buses."
}
$lines = @('"workshopitem"','{')
foreach ($key in $fields.Keys) { $lines += '    "' + $key + '" "' + (ConvertTo-VdfString $fields[$key]) + '"' }
$lines += '}'
[IO.File]::WriteAllText($vdfPath, ($lines -join "`n") + "`n", (New-Object Text.UTF8Encoding($false)))
Write-Host "Prepared Workshop content: $content"
Write-Host "Upload configuration: $vdfPath"
Write-Host "Item ID: $publishedId; visibility: $Visibility"
