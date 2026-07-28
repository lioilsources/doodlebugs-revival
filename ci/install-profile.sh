#!/usr/bin/env bash
#
# Installs the App Store provisioning profile and publishes its metadata to the
# rest of the job.
#
# Unity emits an Xcode project configured for automatic signing. Manual signing
# needs the profile's *name* (not its UUID) for fastlane's
# update_code_signing_settings and its UUID for xcodebuild, so both are read out
# of the profile itself rather than duplicated into secrets where they could drift.
#
# Environment:
#   IOS_PROVISION_PROFILE_BASE64  base64-encoded .mobileprovision
#
# Exports to $GITHUB_ENV: PROFILE_NAME, PROFILE_UUID, PROFILE_TEAM_ID

set -euo pipefail

: "${IOS_PROVISION_PROFILE_BASE64:?IOS_PROVISION_PROFILE_BASE64 is required}"

PROFILE_DIR="$HOME/Library/MobileDevice/Provisioning Profiles"
mkdir -p "$PROFILE_DIR"

TMP_PROFILE="$(mktemp -t doodlebugs-profile)"
trap 'rm -f "$TMP_PROFILE"' EXIT

echo -n "$IOS_PROVISION_PROFILE_BASE64" | tr -d '[:space:]' | base64 --decode > "$TMP_PROFILE" 2>/dev/null

if [[ ! -s "$TMP_PROFILE" ]]; then
  echo "IOS_PROVISION_PROFILE_BASE64 decoded to nothing — empty secret or not valid base64." >&2
  exit 1
fi

# .mobileprovision is a CMS-signed plist; strip the signature before parsing.
if ! DECODED="$(security cms -D -i "$TMP_PROFILE" 2>/dev/null)" || [[ -z "$DECODED" ]]; then
  echo "The decoded IOS_PROVISION_PROFILE_BASE64 is not a CMS-signed provisioning profile." >&2
  echo "First bytes: $(head -c 16 "$TMP_PROFILE" | xxd -p)" >&2
  echo "Download the App Store profile for the bundle id from the Apple Developer portal," >&2
  echo "then: base64 -i profile.mobileprovision | tr -d '\\n' | gh secret set IOS_PROVISION_PROFILE_BASE64" >&2
  exit 1
fi
PROFILE_UUID="$(printf '%s' "$DECODED" | plutil -extract UUID raw -)"
PROFILE_NAME="$(printf '%s' "$DECODED" | plutil -extract Name raw -)"
PROFILE_TEAM_ID="$(printf '%s' "$DECODED" | plutil -extract TeamIdentifier.0 raw -)"

cp "$TMP_PROFILE" "$PROFILE_DIR/$PROFILE_UUID.mobileprovision"

echo "installed profile '$PROFILE_NAME' ($PROFILE_UUID) for team $PROFILE_TEAM_ID"

if [[ -n "${GITHUB_ENV:-}" ]]; then
  {
    echo "PROFILE_NAME=$PROFILE_NAME"
    echo "PROFILE_UUID=$PROFILE_UUID"
    echo "PROFILE_TEAM_ID=$PROFILE_TEAM_ID"
  } >> "$GITHUB_ENV"
fi
