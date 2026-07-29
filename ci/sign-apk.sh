#!/usr/bin/env bash
#
# Signs an APK with the upload key.
#
#   ./ci/sign-apk.sh Builds/Android/doodlebugs.apk Builds/Android/doodlebugs-signed.apk
#
# Environment:
#   ANDROID_KEYSTORE_BASE64    base64-encoded keystore (PKCS#12 or JKS)
#   ANDROID_KEYSTORE_PASSWORD  keystore password
#   ANDROID_KEY_ALIAS          key alias        (JKS path only)
#   ANDROID_KEY_PASSWORD       key password     (JKS path only)
#
# This project's upload keystore is a PKCS#12 whose ASN.1 uses tag numbers above 30,
# which Java's DerValue still cannot parse in JDK 21 — so neither Unity's signing
# step nor apksigner's --ks flag can open it. OpenSSL 3.x reads it fine, so the key
# and certificate are extracted to PEM and handed to apksigner via --key/--cert,
# bypassing Java's KeyStore entirely. A plain JKS falls through to the normal path.

set -euo pipefail

IN_APK="${1:?usage: $0 <input.apk> <output.apk>}"
OUT_APK="${2:?usage: $0 <input.apk> <output.apk>}"

: "${ANDROID_KEYSTORE_BASE64:?ANDROID_KEYSTORE_BASE64 is required}"
: "${ANDROID_KEYSTORE_PASSWORD:?ANDROID_KEYSTORE_PASSWORD is required}"

[[ -f "$IN_APK" ]] || { echo "input APK not found: $IN_APK" >&2; exit 1; }

WORK="$(mktemp -d -t doodlebugs-signing)"
trap 'rm -rf "$WORK"' EXIT

KEYSTORE="$WORK/upload.keystore"
echo -n "$ANDROID_KEYSTORE_BASE64" | tr -d '[:space:]' | base64 --decode > "$KEYSTORE"

# ── Locate apksigner ─────────────────────────────────────────────────────────
find_apksigner() {
  local sdk_roots=()
  [[ -n "${ANDROID_HOME:-}"      ]] && sdk_roots+=("$ANDROID_HOME")
  [[ -n "${ANDROID_SDK_ROOT:-}"  ]] && sdk_roots+=("$ANDROID_SDK_ROOT")

  # The self-hosted runner has no standalone SDK; Unity's Android module ships one.
  local unity_version unity_secondary
  unity_version="$(awk '/^m_EditorVersion:/ {print $2}' ProjectSettings/ProjectVersion.txt 2>/dev/null || true)"
  if [[ -n "$unity_version" ]]; then
    local hub_cfg="$HOME/Library/Application Support/UnityHub/secondaryInstallPath.json"
    if [[ -f "$hub_cfg" ]]; then
      unity_secondary="$(tr -d '"[:space:]' < "$hub_cfg")"
      [[ -n "$unity_secondary" ]] && \
        sdk_roots+=("$unity_secondary/$unity_version/PlaybackEngines/AndroidPlayer/SDK")
    fi
    sdk_roots+=("/Applications/Unity/Hub/Editor/$unity_version/PlaybackEngines/AndroidPlayer/SDK")
  fi

  local root latest
  for root in "${sdk_roots[@]}"; do
    [[ -d "$root/build-tools" ]] || continue
    latest="$(ls "$root/build-tools" | sort -V | tail -1)"
    [[ -x "$root/build-tools/$latest/apksigner" ]] && { echo "$root/build-tools/$latest/apksigner"; return; }
  done

  command -v apksigner 2>/dev/null && return

  echo "apksigner not found (looked in: ${sdk_roots[*]})" >&2
  return 1
}

APKSIGNER="$(find_apksigner)"
echo "using $APKSIGNER"

# ── Sign ─────────────────────────────────────────────────────────────────────
# The PKCS#12 extraction must run under a real OpenSSL 3. A launchd-started
# runner has a bare PATH where `openssl` is LibreSSL, which cannot decrypt this
# modern p12 — the extraction then fails silently and the script falls through
# to the Java KeyStore branch, which dies on the very "Tag number over 30"
# problem this script exists to bypass. Same trap as ci/unlock-keychain.sh.
OPENSSL="$(
  for c in /opt/homebrew/opt/openssl@3/bin/openssl /usr/local/opt/openssl@3/bin/openssl \
           /opt/homebrew/bin/openssl /usr/local/bin/openssl openssl; do
    command -v "$c" >/dev/null 2>&1 && "$c" version 2>/dev/null | grep -q '^OpenSSL 3' && { echo "$c"; break; }
  done
)"
if [[ -z "$OPENSSL" ]]; then
  OPENSSL=openssl
  echo "warning: no OpenSSL 3 found, PKCS#12 extraction may falsely fail (brew install openssl@3)" >&2
fi

# apksigner --key/--cert want DER, not PEM. Passing PEM fails with a misleading
# "Not an RSA, EC, or DSA private key", so -outform DER is load-bearing here.
KEY_DER="$WORK/key.der"
CERT_DER="$WORK/cert.der"

if "$OPENSSL" pkcs12 -in "$KEYSTORE" -nocerts -nodes -passin "pass:$ANDROID_KEYSTORE_PASSWORD" 2>/dev/null \
     | "$OPENSSL" pkcs8 -topk8 -nocrypt -outform DER > "$KEY_DER" 2>/dev/null \
   && [[ -s "$KEY_DER" ]] \
   && "$OPENSSL" pkcs12 -in "$KEYSTORE" -nokeys -clcerts -passin "pass:$ANDROID_KEYSTORE_PASSWORD" 2>/dev/null \
     | "$OPENSSL" x509 -outform DER > "$CERT_DER" 2>/dev/null \
   && [[ -s "$CERT_DER" ]]; then

  echo "keystore read as PKCS#12, signing via extracted DER key/cert"
  "$APKSIGNER" sign --key "$KEY_DER" --cert "$CERT_DER" --out "$OUT_APK" "$IN_APK"

else
  echo "keystore is not PKCS#12, signing via Java KeyStore"
  : "${ANDROID_KEY_ALIAS:?ANDROID_KEY_ALIAS is required for a JKS keystore}"
  : "${ANDROID_KEY_PASSWORD:?ANDROID_KEY_PASSWORD is required for a JKS keystore}"
  "$APKSIGNER" sign \
    --ks "$KEYSTORE" \
    --ks-pass "pass:$ANDROID_KEYSTORE_PASSWORD" \
    --ks-key-alias "$ANDROID_KEY_ALIAS" \
    --key-pass "pass:$ANDROID_KEY_PASSWORD" \
    --out "$OUT_APK" \
    "$IN_APK"
fi

"$APKSIGNER" verify --print-certs "$OUT_APK"
echo "signed: $OUT_APK"
