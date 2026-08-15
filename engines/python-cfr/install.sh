#!/usr/bin/env bash
set -euo pipefail

engine_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
python_command="${PYTHON:-python3}"

"$python_command" -m venv "$engine_dir/.venv"

if [[ -x "$engine_dir/.venv/bin/python" ]]; then
  venv_python="$engine_dir/.venv/bin/python"
else
  venv_python="$engine_dir/.venv/Scripts/python.exe"
fi

"$venv_python" -m pip install --upgrade pip
"$venv_python" -m pip install "$engine_dir"

echo "Installed PyTorch CFR Bot. Load this executable in Bid Euchre:"
echo "$engine_dir/run.sh"
