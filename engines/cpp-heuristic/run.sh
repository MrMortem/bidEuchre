#!/usr/bin/env bash
set -euo pipefail

engine_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
build_dir="${BIDEUCHRE_CPP_BUILD_DIR:-$engine_dir/build}"
binary="$build_dir/bideuchre-cpp-heuristic"

if [[ ! -x "$binary" ]]; then
    echo "C++ Heuristic Bot is not built. Run $engine_dir/build.sh first." >&2
    exit 1
fi

exec "$binary" "$@"
