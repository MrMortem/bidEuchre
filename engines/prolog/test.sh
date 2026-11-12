#!/usr/bin/env bash
set -euo pipefail

engine_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if ! command -v swipl >/dev/null 2>&1; then
  echo "SKIP: SWI-Prolog (swipl) is not installed." >&2
  exit 77
fi

swipl -q -f none -s "$engine_dir/basic_bot_tests.pl"

expected=$'id name "Basic Prolog Bot"\nid author "Bid Euchre Project"\nprotocol bideuchre 1\nbeuciok\nreadyok'
actual="$(printf 'beuci\nisready\nnewgame\nsetoption name Style value basic\nstop\nquit\n' | "$engine_dir/run.sh")"

if [[ "$actual" != "$expected" ]]; then
  echo "FAIL: unexpected BEUCI handshake transcript" >&2
  diff -u <(printf '%s\n' "$expected") <(printf '%s\n' "$actual") >&2 || true
  exit 1
fi

case_actual="$(printf 'BEUCI\r\nISREADY\r\nQUIT\r\n' | "$engine_dir/run.sh")"
if [[ "$case_actual" != "$expected" ]]; then
  echo "FAIL: commands were not case-insensitive or CRLF-safe" >&2
  exit 1
fi

go_error="$(printf 'go\nquit\n' | "$engine_dir/run.sh")"
if [[ "$go_error" != 'error a position must be supplied before go' ]]; then
  echo "FAIL: go-before-position did not produce the expected error" >&2
  exit 1
fi

playing_payload='eyJzZWF0IjoxLCJnYW1lIjp7InBoYXNlIjoiUGxheWluZyIsImxlZ2FsQWN0aW9ucyI6eyJjYW5QYXNzIjpmYWxzZSwiYmlkcyI6W10sImNvbnRyYWN0TW9kZXMiOltdLCJ0cnVtcFN1aXRzIjpbXSwiY2FyZHMiOlt7ImNvZGUiOiJBUyJ9XX19fQ'
recovery_lines=()
while IFS= read -r line; do
  recovery_lines+=("$line")
done < <(
  printf 'position invalid!\ngo\nposition %s\ngo\nquit\n' "$playing_payload" | "$engine_dir/run.sh"
)

if [[ "${#recovery_lines[@]}" -ne 2 ||
      "${recovery_lines[0]}" != error\ invalid\ position\ payload:* ||
      "${recovery_lines[1]}" != 'bestaction play as' ]]; then
  echo "FAIL: malformed-position recovery produced an unexpected transcript" >&2
  printf '%s\n' "${recovery_lines[@]}" >&2
  exit 1
fi

# Finder-launched Mac apps do not inherit the terminal's Homebrew PATH.
if [[ "$(uname -s)" == Darwin &&
      ( -x /opt/homebrew/bin/swipl || -x /usr/local/bin/swipl ) ]]; then
  finder_actual="$(printf 'beuci\nisready\nquit\n' | PATH=/usr/bin:/bin:/usr/sbin:/sbin "$engine_dir/run.sh")"
  if [[ "$finder_actual" != "$expected" ]]; then
    echo "FAIL: Prolog engine did not load with Finder's PATH" >&2
    exit 1
  fi
fi

echo "PASS: Basic Prolog Bot unit, transcript, and recovery tests"
