#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$repo_root/scripts/env.sh"
cd "$repo_root"
# The development console reuses test-only in-memory containers, not GameAdapter.
dotnet run --project tests/DeepSpaceAE.Tests/DeepSpaceAE.Tests.csproj --configuration Release --no-restore -- "$@"
