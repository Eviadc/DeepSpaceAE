param()
$ErrorActionPreference = 'Stop'
$version = (Get-Content "$PSScriptRoot/../global.json" -Raw | ConvertFrom-Json).sdk.version
$target = Join-Path $env:LOCALAPPDATA 'DeepSpaceAE\dotnet'
$installed = Join-Path $target 'dotnet.exe'
if ((Test-Path -LiteralPath $installed) -and ((& $installed --list-sdks) -match ('^' + [regex]::Escape($version) + ' '))) {
    . "$PSScriptRoot/env.ps1"
    Write-Host "SDK $version already installed at $target"
    return
}
$metadata = Invoke-RestMethod 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'
$sdk = @($metadata.releases | ForEach-Object { $_.sdks } | Where-Object version -eq $version)[0]
$file = @($sdk.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -like '*.zip' })[0]
if (!$file) { throw "No official win-x64 SDK $version" }
$archive = Join-Path $env:TEMP "deepspaceae-sdk-$version-win-x64.zip"
if (!(Test-Path -LiteralPath $archive)) { Invoke-WebRequest $file.url -OutFile $archive }
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash -ne $file.hash) { throw "SDK checksum mismatch: remove $archive and retry" }
New-Item -ItemType Directory -Force -Path $target | Out-Null
Expand-Archive -LiteralPath $archive -DestinationPath $target -Force
. "$PSScriptRoot/env.ps1"
dotnet --version
