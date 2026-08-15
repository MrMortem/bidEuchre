#!/usr/bin/env bash
set -euo pipefail

project_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
dotnet_command="dotnet"

if [[ -x "$project_dir/.dotnet/dotnet" ]]; then
  dotnet_command="$project_dir/.dotnet/dotnet"
fi

export DOTNET_CLI_HOME="$project_dir/.dotnet-home"
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://127.0.0.1:5050}"

mkdir -p "$DOTNET_CLI_HOME"
"$dotnet_command" build "$project_dir/src/BidEuchre.Server/BidEuchre.Server.csproj" --nologo -m:1
exec "$dotnet_command" run --project "$project_dir/src/BidEuchre.Server/BidEuchre.Server.csproj" --no-build --no-launch-profile
