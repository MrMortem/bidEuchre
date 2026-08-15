#!/usr/bin/env bash
set -euo pipefail

project_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
runtime_id="${1:-linux-x64}"
dotnet_command="dotnet"

if [[ -x "$project_dir/.dotnet/dotnet" ]]; then
  dotnet_command="$project_dir/.dotnet/dotnet"
fi

export DOTNET_CLI_HOME="$project_dir/.dotnet-home"
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

mkdir -p "$DOTNET_CLI_HOME" "$project_dir/dist/$runtime_id"
"$dotnet_command" publish "$project_dir/src/BidEuchre.Desktop/BidEuchre.Desktop.csproj" \
  --configuration Release \
  --runtime "$runtime_id" \
  --self-contained true \
  --output "$project_dir/dist/$runtime_id" \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -m:1

echo "Native package created at $project_dir/dist/$runtime_id"
