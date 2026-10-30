#!/usr/bin/env bash
# Source this file; caches and SDK stay outside the checkout by default.
export DEEPSPACEAE_ENV_ROOT="${DEEPSPACEAE_ENV_ROOT:-/workspace/.deepspaceae}"
export DOTNET_ROOT="$DEEPSPACEAE_ENV_ROOT/dotnet"
export DOTNET_CLI_HOME="$DEEPSPACEAE_ENV_ROOT/cli"
export NUGET_PACKAGES="$DEEPSPACEAE_ENV_ROOT/nuget/packages"
# .NET 10 named mutexes and NuGet migrations need writable per-user state.
# Keep HOME intact; source this only for the development shell.
export XDG_RUNTIME_DIR="$DEEPSPACEAE_ENV_ROOT/run"
export XDG_DATA_HOME="$DEEPSPACEAE_ENV_ROOT/data"
mkdir -p "$XDG_RUNTIME_DIR" "$XDG_DATA_HOME" "$DOTNET_CLI_HOME" "$NUGET_PACKAGES"
chmod 700 "$XDG_RUNTIME_DIR"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export PATH="$DOTNET_ROOT:$PATH"
