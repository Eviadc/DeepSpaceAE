param()
. "$PSScriptRoot/env.ps1"
Push-Location $RepoRoot
try { Invoke-Checked python @('scripts/package-game.py') } finally { Pop-Location }
