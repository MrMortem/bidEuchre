#!/usr/bin/env bash
set -euo pipefail

project_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
dotnet_command="dotnet"
if [[ -x "$project_dir/.dotnet/dotnet" ]]; then
  dotnet_command="$project_dir/.dotnet/dotnet"
elif ! command -v "$dotnet_command" >/dev/null 2>&1; then
  runtime_id=""
  executable_name="BidEuchre"
  case "$(uname -s)-$(uname -m)" in
    Linux-x86_64) runtime_id="linux-x64" ;;
    Linux-aarch64|Linux-arm64) runtime_id="linux-arm64" ;;
    Darwin-x86_64) runtime_id="osx-x64" ;;
    Darwin-arm64) runtime_id="osx-arm64" ;;
    MINGW*|MSYS*|CYGWIN*) runtime_id="win-x64"; executable_name="BidEuchre.exe" ;;
  esac

  packaged_executable="$project_dir/dist/$runtime_id/$executable_name"
  if [[ -n "$runtime_id" && -x "$packaged_executable" ]]; then
    exec "$packaged_executable"
  fi

  echo "Bid Euchre needs the .NET SDK or a package created by ./package.sh." >&2
  exit 1
fi

export DOTNET_CLI_HOME="$project_dir/.dotnet-home"
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

mkdir -p "$DOTNET_CLI_HOME"
"$dotnet_command" build "$project_dir/src/BidEuchre.Desktop/BidEuchre.Desktop.csproj" --nologo -m:1
exec "$dotnet_command" run --project "$project_dir/src/BidEuchre.Desktop/BidEuchre.Desktop.csproj" --no-build
