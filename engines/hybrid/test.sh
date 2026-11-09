#!/bin/bash
set -euo pipefail
engine_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
"$engine_dir/build.sh" "$@"
# The baseline is EXCLUDE_FROM_ALL; explicitly build its registered CTest target.
cmake --build "$engine_dir/build" --target bideuchre-cpp-heuristic-tests --parallel 2
ctest --test-dir "$engine_dir/build" --output-on-failure
# Regenerate through the C# authority: replacement deals remain explicit inputs.
fresh="$(mktemp "$engine_dir/build/authority-fresh.XXXXXX")"
trap 'rm -f "$fresh"' EXIT
dotnet run --project "$engine_dir/../../src/BidEuchre.Arena" -- export-fixtures --output "$fresh"
"$engine_dir/build/bideuchre-hybrid-replay" --fixtures "$fresh"
