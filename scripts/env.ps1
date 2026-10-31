param()
$ErrorActionPreference = 'Stop'
$script:RepoRoot = Split-Path $PSScriptRoot -Parent
$requiredSdk = (Get-Content (Join-Path $RepoRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
$localDotnet = Join-Path $env:LOCALAPPDATA 'DeepSpaceAE\dotnet\dotnet.exe'
if (Test-Path -LiteralPath $localDotnet) {
    $env:DOTNET_ROOT = Split-Path $localDotnet
    $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
function Invoke-Checked {
    param([string]$Command, [string[]]$Arguments)
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command failed (exit $LASTEXITCODE)" }
}
Push-Location $RepoRoot
try {
    $actualSdk = & dotnet --version
    if ($LASTEXITCODE -ne 0 -or $actualSdk -ne $requiredSdk) { throw "Install .NET SDK $requiredSdk; see scripts/install-env.ps1" }
} finally { Pop-Location }
