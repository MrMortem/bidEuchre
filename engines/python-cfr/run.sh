#!/usr/bin/env bash
set -euo pipefail

engine_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
default_python="$engine_dir/.venv/bin/python"
if [[ ! -x "$default_python" && -x "$engine_dir/.venv/Scripts/python.exe" ]]; then
  default_python="$engine_dir/.venv/Scripts/python.exe"
fi
python_command="${BIDEUCHRE_CFR_PYTHON:-$default_python}"

if [[ ! -x "$python_command" ]]; then
  echo "PyTorch CFR Bot is not installed. Run $engine_dir/install.sh first." >&2
  exit 2
fi

export PYTHONPATH="$engine_dir${PYTHONPATH:+:$PYTHONPATH}"
export BIDEUCHRE_CFR_STATE_DIR="${BIDEUCHRE_CFR_STATE_DIR:-$engine_dir/state}"
exec "$python_command" -u -m bideuchre_cfr.bot "$@"
