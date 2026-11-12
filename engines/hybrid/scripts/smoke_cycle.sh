#!/bin/bash
set -euo pipefail
engine_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project_dir="$(cd "$engine_dir/../.." && pwd)"
. "$engine_dir/scripts/dotnet.sh"
bideuchre_find_dotnet "$project_dir"
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$project_dir/.dotnet-home}" DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
# Required smoke checks never silently skip missing tools or models. Build first.
exec "$engine_dir/.venv/bin/python" "$engine_dir/tests/integration/smoke_cycle.py"
