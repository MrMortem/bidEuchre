#!/usr/bin/env bash
set -euo pipefail

project_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$project_dir/engines/hybrid/scripts/dotnet.sh"
bideuchre_find_dotnet "$project_dir"
# The hybrid install supplies stable local CMake independently of workflow tooling.
if [[ -x "$project_dir/engines/hybrid/.venv/bin/cmake" ]]; then
  export PATH="$project_dir/engines/hybrid/.venv/bin:$PATH"
fi

export DOTNET_CLI_HOME="$project_dir/.dotnet-home"
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

mkdir -p "$DOTNET_CLI_HOME"

if command -v node >/dev/null 2>&1; then
    node --test "$project_dir"/tests/web/*.test.cjs
else
    echo "SKIP  Browser motion tests (Node.js is not installed)"
fi

prolog_engine_path=""
if command -v swipl >/dev/null 2>&1; then
    prolog_engine_path="$project_dir/engines/prolog/run.sh"
    export BIDEUCHRE_PROLOG_ENGINE="$prolog_engine_path"
fi

cpp_engine_path=""
if command -v cmake >/dev/null 2>&1 &&
   { command -v c++ >/dev/null 2>&1 || command -v g++ >/dev/null 2>&1 || command -v clang++ >/dev/null 2>&1; }; then
    "$project_dir/engines/cpp-heuristic/test.sh"
    cpp_engine_path="$project_dir/engines/cpp-heuristic/run.sh"
    export BIDEUCHRE_CPP_HEURISTIC_ENGINE="$cpp_engine_path"
else
    echo "SKIP  C++ Heuristic Bot tests (CMake or a C++ compiler is not installed)"
fi

python_cfr_dir="$project_dir/engines/python-cfr"
python_command=""
for candidate in "$python_cfr_dir/.venv/bin/python" python3 python; do
    if command -v "$candidate" >/dev/null 2>&1 &&
       "$candidate" -c 'import sys; raise SystemExit(sys.version_info < (3, 10))' >/dev/null 2>&1; then
        python_command="$candidate"
        break
    fi
done

if [[ -n "$python_command" ]]; then
    PYTHONDONTWRITEBYTECODE=1 \
        PYTHONPATH="$python_cfr_dir:$python_cfr_dir/tests" \
        "$python_command" -m unittest -v \
        test_actions test_encoding test_simulator test_model_storage.StorageTests
else
    echo "SKIP  Python CFR standard-library tests (Python 3.10+ is not installed)"
fi

if [[ "$(uname -s)" == Darwin ]]; then
    "$project_dir/tests/package-macos.sh"
fi

python_cfr_venv="${BIDEUCHRE_CFR_PYTHON:-$python_cfr_dir/.venv/bin/python}"
if [[ -x "$python_cfr_venv" ]] && "$python_cfr_venv" -c 'import sys; assert sys.version_info >= (3, 10); import torch' >/dev/null 2>&1; then
    PYTHONDONTWRITEBYTECODE=1 PYTHONPATH="$python_cfr_dir" "$python_cfr_venv" \
        -m unittest discover -s "$python_cfr_dir/tests" -v
    export BIDEUCHRE_PYTHON_CFR_ENGINE="$python_cfr_dir/run.sh"
    python_state_dir="$(mktemp -d "$project_dir/.python-cfr-test-state.XXXXXX")"
    export BIDEUCHRE_CFR_STATE_DIR="$python_state_dir"
    trap 'rm -rf -- "$python_state_dir"' EXIT
else
    echo "SKIP  Python CFR PyTorch tests and process integration (Python 3.10+ venv with torch is not installed)"
fi

"$dotnet_command" build "$project_dir/BidEuchre.slnx" --nologo -m:1
"$dotnet_command" run --project "$project_dir/tests/BidEuchre.Server.Tests/BidEuchre.Server.Tests.csproj" --no-build
if command -v node >/dev/null 2>&1; then
    BIDEUCHRE_DOTNET="$dotnet_command" node --test "$project_dir"/tests/server/*.test.cjs
else
    echo "SKIP  Web server security tests (Node.js is not installed)"
fi
"$dotnet_command" run --project "$project_dir/tests/BidEuchre.Tests/BidEuchre.Tests.csproj" --no-build
"$dotnet_command" run --project "$project_dir/tests/BidEuchre.Desktop.Tests/BidEuchre.Desktop.Tests.csproj" --no-build

if [[ -n "$prolog_engine_path" ]]; then
  "$project_dir/engines/prolog/test.sh"
else
  echo "SKIP  Basic Prolog Bot tests (SWI-Prolog is not installed)"
fi
