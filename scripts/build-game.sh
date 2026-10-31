#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$repo_root/scripts/env.sh"
cd "$repo_root"
# Explicit reference path only; never download or copy game DLLs into the checkout.
: "${GameManagedDir:?Set GameManagedDir to the approved external 100-Rev6 Managed references directory}"
dotnet restore src/DeepSpaceAE.GameAdapter/DeepSpaceAE.GameAdapter.csproj --locked-mode
dotnet clean src/DeepSpaceAE.GameAdapter/DeepSpaceAE.GameAdapter.csproj -c Release --verbosity minimal
dotnet build src/DeepSpaceAE.GameAdapter/DeepSpaceAE.GameAdapter.csproj -c Release --no-restore \
    -p:GameManagedDir="$GameManagedDir" --maxcpucount:2

# Inspect method bodies and Harmony parameter/field bindings without loading game code.
mkdir -p "$repo_root/artifacts/validation"
rm -f "$repo_root/artifacts/validation/game-patch-audit.json"
dotnet restore tests/DeepSpaceAE.Tests/DeepSpaceAE.Tests.csproj --locked-mode
dotnet run --project tests/DeepSpaceAE.Tests/DeepSpaceAE.Tests.csproj --configuration Release --no-restore -- \
    --audit-game "$GameManagedDir/Assembly-CSharp.dll" \
    "$repo_root/src/DeepSpaceAE.GameAdapter/bin/Release/netstandard2.1/DeepSpaceAE.GameAdapter.dll" \
    "$repo_root/artifacts/validation/game-patch-audit.json"
