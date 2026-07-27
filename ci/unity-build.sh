#!/usr/bin/env bash
#
# Headless Unity build wrapper.
#
#   ./ci/unity-build.sh ios     Builds/iOS
#   ./ci/unity-build.sh android Builds/Android/doodlebugs.apk
#
# Environment:
#   GITHUB_RUN_NUMBER  becomes CFBundleVersion / versionCode (defaults to 0 locally)
#   BUILD_NUMBER       overrides GITHUB_RUN_NUMBER — Android needs an offset, see below
#   BUILD_VERSION      optional marketing version, e.g. 0.4.0
#   UNITY_BIN          optional explicit path to the Unity executable
#   ANDROID_KEYSTORE_* consumed by BuildScript.ConfigureAndroidSigning (android only)
#
# No -username / -password / -serial is passed. The editor on this machine holds a
# locally activated Personal licence; handing Unity any credentials would kick off a
# re-activation flow that Personal cannot complete in batch mode.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

PLATFORM="${1:-}"
OUTPUT="${2:-}"

if [[ -z "$PLATFORM" || -z "$OUTPUT" ]]; then
  echo "usage: $0 <ios|android> <output-path>" >&2
  exit 2
fi

case "$PLATFORM" in
  ios)     BUILD_METHOD="BuildIOS" ;;
  android) BUILD_METHOD="BuildAndroid"
           # Unity wants a file path for an APK; accept a bare directory too.
           [[ "$OUTPUT" == *.apk ]] || OUTPUT="${OUTPUT%/}/doodlebugs.apk" ;;
  *)       echo "unknown platform '$PLATFORM' (expected ios or android)" >&2; exit 2 ;;
esac

# ── Locate the editor ────────────────────────────────────────────────────────
# The version always comes from the project so CI cannot drift from what the repo
# was authored against.
UNITY_VERSION="$(awk '/^m_EditorVersion:/ {print $2}' ProjectSettings/ProjectVersion.txt)"
if [[ -z "$UNITY_VERSION" ]]; then
  echo "could not read m_EditorVersion from ProjectSettings/ProjectVersion.txt" >&2
  exit 1
fi

find_unity() {
  # 1. explicit override
  if [[ -n "${UNITY_BIN:-}" ]]; then
    echo "$UNITY_BIN"; return
  fi

  local candidates=()

  # 2. Unity Hub "install location" override — this machine keeps editors on an
  #    external volume, so the default /Applications path does not exist.
  local hub_cfg="$HOME/Library/Application Support/UnityHub/secondaryInstallPath.json"
  if [[ -f "$hub_cfg" ]]; then
    local secondary
    secondary="$(tr -d '"[:space:]' < "$hub_cfg")"
    [[ -n "$secondary" ]] && candidates+=("$secondary/$UNITY_VERSION/Unity.app/Contents/MacOS/Unity")
  fi

  # 3. default Hub layout
  candidates+=("/Applications/Unity/Hub/Editor/$UNITY_VERSION/Unity.app/Contents/MacOS/Unity")

  local c
  for c in "${candidates[@]}"; do
    [[ -x "$c" ]] && { echo "$c"; return; }
  done

  echo "Unity $UNITY_VERSION not found. Looked in:" >&2
  printf '  %s\n' "${candidates[@]}" >&2
  echo "Install it via Unity Hub, or set UNITY_BIN to the executable." >&2
  return 1
}

UNITY="$(find_unity)"

BUILD_NUMBER="${BUILD_NUMBER:-${GITHUB_RUN_NUMBER:-0}}"

EXTRA_ARGS=()
if [[ -n "${BUILD_VERSION:-}" ]]; then
  EXTRA_ARGS+=(-buildVersion "$BUILD_VERSION")
fi

echo "── Unity build ──────────────────────────────────────────────"
echo "  editor   : $UNITY"
echo "  version  : $UNITY_VERSION"
echo "  platform : $PLATFORM"
echo "  output   : $OUTPUT"
echo "  build no : $BUILD_NUMBER"
echo "─────────────────────────────────────────────────────────────"

# -logFile - streams the log to stdout. Without it Unity writes to a file and CI
# shows nothing at all until the job times out.
set +e
"$UNITY" \
  -batchmode \
  -nographics \
  -quit \
  -projectPath "$REPO_ROOT" \
  -executeMethod "Doodlebugs.Editor.BuildScript.$BUILD_METHOD" \
  -buildPath "$OUTPUT" \
  -buildNumber "$BUILD_NUMBER" \
  "${EXTRA_ARGS[@]}" \
  -logFile -
UNITY_EXIT=$?
set -e

if [[ $UNITY_EXIT -ne 0 ]]; then
  echo "Unity exited with $UNITY_EXIT" >&2
  exit $UNITY_EXIT
fi

# BuildScript already fails the run on a bad BuildReport; this is the belt-and-braces
# check that something actually landed on disk.
if [[ ! -e "$OUTPUT" ]]; then
  echo "Unity reported success but $OUTPUT does not exist" >&2
  exit 1
fi

echo "build finished: $OUTPUT"
