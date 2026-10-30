#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$repo_root/scripts/env.sh"
if [[ "$(uname -s)" != Linux || "$(uname -m)" != x86_64 ]]; then
    echo 'This installer supports Linux x64 only. Install SDK 10.0.403 for your platform from Microsoft.' >&2
    exit 1
fi
sdk_version=10.0.403
sdk_sha512=b7b3d8461cf98b07d2552ce9199702fa3ad010ac2671a5f8cd5136f7534e50f6855ba7bbb02b11494563f1d04156276a31cda1103b900e5eecd0d9473dc7b9c5
mkdir -p "$DOTNET_ROOT" "$DOTNET_CLI_HOME" "$NUGET_PACKAGES"
if [[ ! -x "$DOTNET_ROOT/dotnet" ]] || ! "$DOTNET_ROOT/dotnet" --list-sdks | grep -q "^$sdk_version "; then
    sdk_archive="$(mktemp /tmp/deepspaceae-sdk.XXXXXX.tar.gz)"
    trap 'rm -f "$sdk_archive"' EXIT
    curl --fail --show-error --silent --location \
        "https://builds.dotnet.microsoft.com/dotnet/Sdk/$sdk_version/dotnet-sdk-$sdk_version-linux-x64.tar.gz" \
        --output "$sdk_archive"
    printf '%s  %s\n' "$sdk_sha512" "$sdk_archive" | sha512sum --check --status
    tar -xzf "$sdk_archive" -C "$DOTNET_ROOT"
fi
cd "$repo_root"
test "$(dotnet --version)" = "$sdk_version"
dotnet restore DeepSpaceAE.sln --locked-mode
# Excluded from default solution compilation, but prepare explicit diagnostic builds too.
dotnet restore src/DeepSpaceAE.GameAdapter/DeepSpaceAE.GameAdapter.csproj --locked-mode
