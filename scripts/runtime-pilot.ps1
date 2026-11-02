param(
    [string]$GameDir = 'G:\SteamLibrary\steamapps\common\Probably Stolen',
    [switch]$Launch,
    [switch]$UpdateOnly,
    [switch]$EngineerStart
)
. "$PSScriptRoot/env.ps1"
$gameRoot = (Resolve-Path -LiteralPath $GameDir).Path
$processes = Get-Process | Where-Object { $_.Path -and $_.Path.StartsWith($gameRoot + '\', [StringComparison]::OrdinalIgnoreCase) }
if ($processes) { throw 'Close the game before installing/arming the runtime pilot.' }
$gameManaged = Join-Path $gameRoot 'Probably Stolen_Data\Managed'
$productDir = Join-Path $gameRoot 'Mods\DeepSpaceAE'
$project = Join-Path $RepoRoot 'tests\DeepSpaceAE.RuntimeTests\DeepSpaceAE.RuntimeTests.csproj'
Invoke-Checked dotnet @('restore', $project, '--locked-mode')
Invoke-Checked dotnet @('build', $project, '-c', 'Release', '--no-restore', "-p:GameManagedDir=$gameManaged", "-p:GameModDir=$productDir")
$pilotDir = Join-Path $gameRoot 'Mods\DeepSpaceAE.RuntimeTests'
New-Item -ItemType Directory -Force -Path $pilotDir | Out-Null
if (!$UpdateOnly -and (Test-Path -LiteralPath (Join-Path $pilotDir 'run.json'))) { throw 'An unconsumed pilot is already armed. Inspect it before creating another.' }
Copy-Item -LiteralPath (Join-Path $RepoRoot 'tests\DeepSpaceAE.RuntimeTests\bin\Release\netstandard2.1\DeepSpaceAE.RuntimeTests.dll') -Destination $pilotDir -Force
Copy-Item -LiteralPath (Join-Path $RepoRoot 'tests\DeepSpaceAE.RuntimeTests\manifest.xml') -Destination $pilotDir -Force
if ($UpdateOnly) {
    Write-Host 'Updated only the test plugin; existing arm/result files retained.'
    if ($Launch) { Start-Process -FilePath (Join-Path $gameRoot 'Probably Stolen.exe') -WorkingDirectory $gameRoot }
    return
}
$runId = [guid]::NewGuid().ToString('N')
$output = Join-Path $RepoRoot ('artifacts\runtime-tests\' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $runId.Substring(0,8))
New-Item -ItemType Directory -Path $output | Out-Null
$saveDir = Join-Path $env:USERPROFILE 'AppData\LocalLow\Questing Goose Studio\Probably Stolen'
$saveFiles = Get-ChildItem -LiteralPath $saveDir -File | Where-Object { $_.Name -match '^save_\d+\.es3$|^saves_index\.es3$|^SaveFile\.es3$' }
$backup = Join-Path $output 'before-saves'
New-Item -ItemType Directory -Path $backup | Out-Null
foreach ($file in $saveFiles) { Copy-Item -LiteralPath $file.FullName -Destination $backup }
$existing = @(Get-ChildItem -LiteralPath $saveDir -File | ForEach-Object { if ($_.Name -match '^save_(\d+)\.es3$') { [int]$Matches[1] } })
$product = Get-Content -LiteralPath (Join-Path $productDir 'build-info.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$config = [ordered]@{ runId=$runId; outputDirectory=$output; productCommit=$product.sourceCommit; existingSlots=$existing; exitAfterRun=$true; engineerStart=[bool]$EngineerStart }
$json = $config | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText((Join-Path $pilotDir 'run.json'), $json, (New-Object Text.UTF8Encoding($false)))
[IO.File]::WriteAllText((Join-Path $output 'config.json'), $json, (New-Object Text.UTF8Encoding($false)))
$saveFiles | Select-Object Name,Length,LastWriteTimeUtc | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'before-save-metadata.json') -Encoding UTF8
Write-Host "Armed runtime pilot: $output"
Write-Host 'Enable DeepSpaceAE Runtime Pilot through the game mod manager if needed. It will request its declared file/reflection permissions.'
if ($Launch) {
    Start-Process -FilePath (Join-Path $gameRoot 'Probably Stolen.exe') -WorkingDirectory $gameRoot
    Write-Host 'Started the installed game. No existing save is selected by the pilot.'
}
