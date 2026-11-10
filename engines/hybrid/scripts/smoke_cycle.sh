#!/bin/bash
set -euo pipefail
engine_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project_dir="$(cd "$engine_dir/../.." && pwd)"
export BIDEUCHRE_DOTNET="${BIDEUCHRE_DOTNET:-${DOTNET_ROOT:+$DOTNET_ROOT/dotnet}}"
if [ -z "$BIDEUCHRE_DOTNET" ]; then
    if [ -x "$project_dir/.dotnet/dotnet" ]; then BIDEUCHRE_DOTNET="$project_dir/.dotnet/dotnet";
    else BIDEUCHRE_DOTNET="$(command -v dotnet || true)"; fi
fi
if [ ! -x "$BIDEUCHRE_DOTNET" ]; then echo "Set BIDEUCHRE_DOTNET to a .NET 10 SDK dotnet executable." >&2; exit 1; fi
export DOTNET_ROOT="$(cd "$(dirname "$BIDEUCHRE_DOTNET")" && pwd)"
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$project_dir/.dotnet-home}" DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
# Required smoke checks never silently skip missing tools or models. Build first.
exec "$engine_dir/.venv/bin/python" "$engine_dir/tests/integration/smoke_cycle.py"
