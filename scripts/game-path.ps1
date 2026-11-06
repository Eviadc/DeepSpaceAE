function Get-DeepSpaceGameDir {
    param([string]$GameDir)
    if (!$GameDir) { $GameDir = $env:DEEPSPACEAE_GAME_DIR }
    $configPath = Join-Path (Split-Path $PSScriptRoot -Parent) '.deepspaceae/local.json'
    if (!$GameDir -and (Test-Path -LiteralPath $configPath)) {
        $GameDir = (Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json).gameDir
    }
    if (!$GameDir) {
        throw 'Set -GameDir, DEEPSPACEAE_GAME_DIR, or gameDir in .deepspaceae/local.json.'
    }
    return (Resolve-Path -LiteralPath $GameDir).Path
}
