param([string]$GameDir = 'G:\SteamLibrary\steamapps\common\Probably Stolen', [string]$GameManagedDir)
. "$PSScriptRoot/env.ps1"
if (!$GameManagedDir) { $GameManagedDir = Join-Path $GameDir 'Probably Stolen_Data\Managed' }
$GameManagedDir = (Resolve-Path -LiteralPath $GameManagedDir).Path
Push-Location $RepoRoot
try {
    $project = 'src/DeepSpaceAE.GameAdapter/DeepSpaceAE.GameAdapter.csproj'
    $audit = Join-Path $RepoRoot 'artifacts/validation/game-patch-audit.json'
    if (Test-Path -LiteralPath $audit) { Remove-Item -LiteralPath $audit }
    Invoke-Checked dotnet @('restore', $project, '--locked-mode')
    Invoke-Checked dotnet @('build', $project, '-c', 'Release', '--no-restore', '--maxcpucount:2', "-p:GameManagedDir=$GameManagedDir")
    Invoke-Checked dotnet @('restore', 'tests/DeepSpaceAE.Tests/DeepSpaceAE.Tests.csproj', '--locked-mode')
    Invoke-Checked dotnet @('run', '--project', 'tests/DeepSpaceAE.Tests/DeepSpaceAE.Tests.csproj', '-c', 'Release', '--no-restore', '--', '--audit-game', "$GameManagedDir/Assembly-CSharp.dll", "$RepoRoot/src/DeepSpaceAE.GameAdapter/bin/Release/netstandard2.1/DeepSpaceAE.GameAdapter.dll", $audit)
} finally { Pop-Location }
