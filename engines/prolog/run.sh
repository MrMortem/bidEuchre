#!/usr/bin/env bash
set -euo pipefail

engine_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

swipl_command="$(command -v swipl || true)"
# macOS apps opened in Finder have a minimal PATH without Homebrew.
if [[ -z "$swipl_command" && "$(uname -s)" == Darwin ]]; then
  for candidate in /opt/homebrew/bin/swipl /usr/local/bin/swipl; do
    if [[ -x "$candidate" ]]; then
      swipl_command="$candidate"
      break
    fi
  done
fi

if [[ -z "$swipl_command" ]]; then
  echo "Basic Prolog Bot requires SWI-Prolog (swipl) on PATH." >&2
  exit 127
fi

exec "$swipl_command" -q -f none -s "$engine_dir/basic_bot.pl"
