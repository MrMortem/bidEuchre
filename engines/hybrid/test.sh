#!/bin/bash
set -euo pipefail
engine_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
"$engine_dir/build.sh" "$@"
cmake_bin="${HYBRID_CMAKE:-$engine_dir/.venv/bin/cmake}"
ctest_bin="$(dirname "$cmake_bin")/ctest"
project_dir="$(cd "$engine_dir/../.." && pwd)"
. "$engine_dir/scripts/dotnet.sh"
bideuchre_find_dotnet "$project_dir"
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$project_dir/.dotnet-home}" DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
# The baseline is EXCLUDE_FROM_ALL; explicitly build its registered CTest target.
"$cmake_bin" --build "$engine_dir/build" --target bideuchre-cpp-heuristic-tests --parallel 2
"$ctest_bin" --test-dir "$engine_dir/build" --output-on-failure
# Regenerate through the C# authority: replacement deals remain explicit inputs.
fresh="$(mktemp "$engine_dir/build/authority-fresh.XXXXXX")"
trap 'rm -f "$fresh"' EXIT
"$dotnet_command" run --project "$engine_dir/../../src/BidEuchre.Arena" -- export-fixtures --output "$fresh"
"$engine_dir/build/bideuchre-hybrid-replay" --fixtures "$fresh"
"$engine_dir/tests/integration/dotnet_discovery.sh" "$dotnet_command"

# Four independent production EngineProcessClient instances, actual frozen model
# and missing-model baseline. Fixed simulation counts keep this smoke run bounded.
export HYBRID_MODEL="$engine_dir/build/test-model/manifest.json"
export BIDEUCHRE_HYBRID_ENGINE="$engine_dir/run.sh"
BIDEUCHRE_HYBRID_ARGUMENTS="--max-simulations 7" "$dotnet_command" run --project "$engine_dir/../../tests/BidEuchre.Tests" -- --filter "Hybrid engine"
BIDEUCHRE_HYBRID_ARGUMENTS="--model /missing/model.json" "$dotnet_command" run --project "$engine_dir/../../tests/BidEuchre.Tests" -- --filter "Hybrid engine completes"
