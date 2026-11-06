param()
. "$PSScriptRoot/env.ps1"
Push-Location $RepoRoot
try {
    Invoke-Checked dotnet @('restore', 'DeepSpaceAE.sln', '--locked-mode')
    Invoke-Checked dotnet @('build', 'DeepSpaceAE.sln', '-c', 'Release', '--no-restore', '--maxcpucount:2')
    Invoke-Checked dotnet @('test', 'tests/DeepSpaceAE.Tests/DeepSpaceAE.Tests.csproj', '-c', 'Release', '--no-build', '--no-restore', '--logger', 'trx;LogFileName=core-model.trx', '--results-directory', 'artifacts/test-results')
} finally { Pop-Location }
