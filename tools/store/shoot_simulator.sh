#!/usr/bin/env bash
#
# Store screenshots from the iOS Simulator, no hands (Prompts/34).
#
#   tools/store/shoot_simulator.sh            # build + shoot iPhone and iPad
#   SKIP_UNITY=1 tools/store/shoot_simulator.sh   # reuse Builds/iOS-shots
#
# 1. Unity exports an iOS Simulator Xcode project with StoreShotDirector
#    compiled in (BuildScript.BuildIOSStoreShots).
# 2. xcodebuild builds it for the simulator, unsigned.
# 3. Each simulator boots, gets the app, runs it; the director walks the boot
#    screen, PRACTICE, the hangar tabs and a bot-vs-bot dogfight and writes
#    PNGs into the app's Documents/storeshots.
# 4. The PNGs land in tools/store/out/raw/<device>/ (gitignored). Pick the
#    keepers, then fit_screenshots.py sizes them for each store.
#
# The simulator renders at the device's native size: iPhone 17 Pro Max is
# 2868x1320 (the 6.9" slot), iPad Pro 13" is 2752x2064 (the 13" slot).

set -euo pipefail
cd "$(dirname "$0")/../.."
ROOT="$PWD"

BUNDLE="com.ol1n.doodlebugsRevival"
PROJ="Builds/iOS-shots"
DD="Builds/iOS-shots-dd"
OUT="tools/store/out/raw"
if [[ -n "${DEVICE:-}" ]]; then DEVICES=("$DEVICE"); else DEVICES=("iPhone 17 Pro Max" "iPad Pro 13-inch (M5)"); fi
TIMEOUT="${TIMEOUT:-420}"

UNITY_VERSION="$(awk '/^m_EditorVersion:/ {print $2}' ProjectSettings/ProjectVersion.txt)"
UNITY="${UNITY_BIN:-}"
if [[ -z "$UNITY" ]]; then
  cfg="$HOME/Library/Application Support/UnityHub/secondaryInstallPath.json"
  [[ -f "$cfg" ]] && UNITY="$(tr -d '"[:space:]' < "$cfg")/$UNITY_VERSION/Unity.app/Contents/MacOS/Unity"
  [[ -x "$UNITY" ]] || UNITY="/Applications/Unity/Hub/Editor/$UNITY_VERSION/Unity.app/Contents/MacOS/Unity"
fi

if [[ "${SKIP_UNITY:-0}" != "1" ]]; then
  echo "── Unity: simulator project → $PROJ"
  "$UNITY" -batchmode -nographics -quit -projectPath "$ROOT" -buildTarget iOS \
    -executeMethod Doodlebugs.Editor.BuildScript.BuildIOSStoreShots \
    -buildPath "$PROJ" -logFile - | grep -E "\[BuildScript\]|error CS|Exiting" || true
  [[ -d "$PROJ/Unity-iPhone.xcodeproj" ]] || { echo "Unity export failed" >&2; exit 1; }
fi

echo "── xcodebuild: simulator app"
xcodebuild -project "$PROJ/Unity-iPhone.xcodeproj" -scheme Unity-iPhone \
  -configuration Release -sdk iphonesimulator -destination 'generic/platform=iOS Simulator' \
  -derivedDataPath "$DD" CODE_SIGNING_ALLOWED=NO -quiet build
APP="$(find "$DD/Build/Products" -maxdepth 2 -name '*.app' -type d | head -1)"
[[ -n "$APP" ]] || { echo "no .app under $DD" >&2; exit 1; }
echo "app: $APP"

for dev in "${DEVICES[@]}"; do
  udid="$(xcrun simctl list devices available | grep -F "    $dev (" | head -1 | sed -E 's/.*\(([0-9A-F-]{36})\).*/\1/')"
  [[ -n "$udid" ]] || { echo "simulator '$dev' not found, skipping" >&2; continue; }
  slug="$(echo "$dev" | tr 'A-Z' 'a-z' | tr -cs 'a-z0-9' '-' | sed 's/-$//')"
  echo "── $dev ($udid)"
  xcrun simctl boot "$udid" 2>/dev/null || true
  xcrun simctl bootstatus "$udid" -b >/dev/null
  xcrun simctl status_bar "$udid" override --time 9:41 --batteryState charged --batteryLevel 100 \
    --wifiBars 3 --cellularBars 4 2>/dev/null || true
  xcrun simctl terminate "$udid" "$BUNDLE" 2>/dev/null || true
  xcrun simctl install "$udid" "$APP"
  xcrun simctl privacy "$udid" grant all "$BUNDLE" 2>/dev/null || true
  xcrun simctl launch "$udid" "$BUNDLE" >/dev/null
  data="$(xcrun simctl get_app_container "$udid" "$BUNDLE" data)"
  shots="$data/Documents/storeshots"
  t=0
  until [[ -f "$shots/done.txt" ]]; do
    sleep 5; t=$((t + 5))
    (( t < TIMEOUT )) || { echo "timed out after ${TIMEOUT}s, taking what is there" >&2; break; }
  done
  mkdir -p "$OUT/$slug"
  cp "$shots"/*.png "$OUT/$slug/" 2>/dev/null || true
  echo "$(ls "$OUT/$slug" | wc -l | tr -d ' ') shots → $OUT/$slug"
  xcrun simctl terminate "$udid" "$BUNDLE" 2>/dev/null || true
  xcrun simctl shutdown "$udid" 2>/dev/null || true
done
