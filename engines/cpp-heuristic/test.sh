#!/usr/bin/env bash
set -euo pipefail

engine_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
build_dir="${BIDEUCHRE_CPP_BUILD_DIR:-$engine_dir/build}"

"$engine_dir/build.sh"
ctest --test-dir "$build_dir" -C Release --output-on-failure

expected=$'id name "C++ Heuristic Bot"\nid author "Bid Euchre Project"\nprotocol bideuchre 1\nbeuciok\nreadyok'
actual="$(printf 'beuci\nisready\nnewgame\nsetoption name Samples value 32\nstop\nquit\n' | "$engine_dir/run.sh")"

if [[ "$actual" != "$expected" ]]; then
    echo "FAIL: unexpected C++ BEUCI handshake transcript" >&2
    diff -u <(printf '%s\n' "$expected") <(printf '%s\n' "$actual") >&2 || true
    exit 1
fi

case_actual="$(printf 'BEUCI\r\nISREADY\r\nQUIT\r\n' | "$engine_dir/run.sh")"
if [[ "$case_actual" != "$expected" ]]; then
    echo "FAIL: C++ commands were not case-insensitive or CRLF-safe" >&2
    exit 1
fi

go_error="$(printf 'go\nquit\n' | "$engine_dir/run.sh")"
if [[ "$go_error" != 'error a position must be supplied before go' ]]; then
    echo "FAIL: C++ go-before-position did not produce the expected error" >&2
    exit 1
fi

playing_payload='eyJzZWF0IjoxLCJnYW1lIjp7InBoYXNlIjoiUGxheWluZyIsImhhbmROdW1iZXIiOjEsImRlYWxlciI6MCwiY3VycmVudFNlYXQiOjEsInNjb3JlcyI6WzAsMF0sInBsYXllcnMiOlt7InNlYXQiOjAsImNhcmRDb3VudCI6MCwiY2FyZHMiOm51bGwsImlzU2l0dGluZ091dCI6ZmFsc2V9LHsic2VhdCI6MSwiY2FyZENvdW50IjoxLCJjYXJkcyI6W3siY29kZSI6IkFTIn1dLCJpc1NpdHRpbmdPdXQiOmZhbHNlfSx7InNlYXQiOjIsImNhcmRDb3VudCI6MCwiY2FyZHMiOm51bGwsImlzU2l0dGluZ091dCI6ZmFsc2V9LHsic2VhdCI6MywiY2FyZENvdW50IjowLCJjYXJkcyI6bnVsbCwiaXNTaXR0aW5nT3V0IjpmYWxzZX1dLCJhdWN0aW9uIjpbXSwiaGlnaEJpZCI6IlRocmVlIiwiYmlkZGVyIjoxLCJjb250cmFjdCI6eyJiaWQiOiJUaHJlZSIsIm1vZGUiOiJUcnVtcCIsInRydW1wIjoiU3BhZGVzIn0sImN1cnJlbnRUcmljayI6W10sImNvbXBsZXRlZFRyaWNrcyI6W3sid2lubmVyIjoxLCJwbGF5cyI6W3sic2VhdCI6MSwiY2FyZCI6eyJjb2RlIjoiOUMifX1dfV0sInRyaWNrc0J5VGVhbSI6WzAsNV0sImxlZ2FsQWN0aW9ucyI6eyJjYW5QYXNzIjpmYWxzZSwiYmlkcyI6W10sImNvbnRyYWN0TW9kZXMiOltdLCJ0cnVtcFN1aXRzIjpbXSwiY2FyZHMiOlt7ImNvZGUiOiJBUyJ9XX19fQ'
mapfile -t recovery_lines < <(
    printf 'position invalid!\ngo\nposition %s\ngo\nquit\n' "$playing_payload" | "$engine_dir/run.sh"
)

if [[ "${#recovery_lines[@]}" -ne 2 ||
      "${recovery_lines[0]}" != error\ invalid\ position\ payload:* ||
      "${recovery_lines[1]}" != 'bestaction play AS' ]]; then
    echo "FAIL: C++ malformed-position recovery produced an unexpected transcript" >&2
    printf '%s\n' "${recovery_lines[@]}" >&2
    exit 1
fi

echo "PASS: C++ Heuristic Bot unit, transcript, and recovery tests"
