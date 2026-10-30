#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$repo_root/scripts/env.sh"
cd "$repo_root"
# Explicit reference path only; never download or copy game DLLs into the checkout.
: "${GameManagedDir:?Set GameManagedDir to the approved external 100-REV3 Managed references directory}"
dotnet restore src/DeepSpaceAE.GameAdapter/DeepSpaceAE.GameAdapter.csproj --locked-mode
dotnet clean src/DeepSpaceAE.GameAdapter/DeepSpaceAE.GameAdapter.csproj -c Release --verbosity minimal
dotnet build src/DeepSpaceAE.GameAdapter/DeepSpaceAE.GameAdapter.csproj -c Release --no-restore \
    -p:GameManagedDir="$GameManagedDir" --maxcpucount:2
