#!/usr/bin/env bash
set -euo pipefail
output_dir="$(cd "${1:?Pass the published package directory}" && pwd)"
[[ -x "$output_dir/BidEuchre" ]] || { echo 'Published BidEuchre executable was not found.' >&2; exit 1; }
bundle="$output_dir/Bid Euchre.app"
mkdir -p "$bundle/Contents/MacOS" "$bundle/Contents/Resources"
# Include loose rules/licenses alongside the self-contained executable. Exclude
# the bundle itself so rebuilding cannot recursively copy a previous .app.
for entry in "$output_dir"/*; do
  [[ "$entry" == "$bundle" ]] && continue
  cp -R "$entry" "$bundle/Contents/MacOS/"
done
cat > "$bundle/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleExecutable</key><string>BidEuchre</string>
  <key>CFBundleIdentifier</key><string>io.github.mrmortem.bideuchre</string>
  <key>CFBundleName</key><string>Bid Euchre</string>
  <key>CFBundleDisplayName</key><string>Bid Euchre</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST
echo "Mac app created at $bundle"
