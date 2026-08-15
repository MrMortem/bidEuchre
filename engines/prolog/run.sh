#!/usr/bin/env bash
set -euo pipefail

engine_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if ! command -v swipl >/dev/null 2>&1; then
  echo "Basic Prolog Bot requires SWI-Prolog (swipl) on PATH." >&2
  exit 127
fi

exec swipl -q -f none -s "$engine_dir/basic_bot.pl"
