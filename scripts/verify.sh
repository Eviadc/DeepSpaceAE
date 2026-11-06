#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$repo_root/scripts/env.sh"
cd "$repo_root"
dotnet restore DeepSpaceAE.sln --locked-mode
dotnet clean DeepSpaceAE.sln --configuration Release --verbosity minimal
dotnet build DeepSpaceAE.sln --configuration Release --no-restore --maxcpucount:2
dotnet test tests/DeepSpaceAE.Tests/DeepSpaceAE.Tests.csproj --configuration Release --no-build --no-restore \
    --logger 'trx;LogFileName=core-model.trx' --results-directory "$repo_root/artifacts/test-results"
