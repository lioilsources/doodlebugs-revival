#!/usr/bin/env bash
#
# Signs an Android App Bundle with the upload key, for Google Play.
#
#   ./ci/sign-aab.sh Builds/Android/doodlebugs.aab Builds/Android/doodlebugs-signed.aab
#
# Environment: the same ANDROID_KEYSTORE_* variables as ci/sign-apk.sh.
#
# An AAB is signed with jarsigner, not apksigner, and jarsigner only reads a Java
# KeyStore. This project's upload keystore is a PKCS#12 that Java cannot parse
# (ASN.1 tag numbers above 30, see ci/sign-apk.sh), so OpenSSL 3 re-exports the
# same key and certificate into a plain PKCS#12 that Java does read, in a temp
# directory that is wiped on exit. The key never changes - Play sees the same
# upload certificate as the APK on Firebase.

set -euo pipefail

IN_AAB="${1:?usage: $0 <input.aab> <output.aab>}"
OUT_AAB="${2:?usage: $0 <input.aab> <output.aab>}"

: "${ANDROID_KEYSTORE_BASE64:?ANDROID_KEYSTORE_BASE64 is required}"
: "${ANDROID_KEYSTORE_PASSWORD:?ANDROID_KEYSTORE_PASSWORD is required}"

[[ -f "$IN_AAB" ]] || { echo "input AAB not found: $IN_AAB" >&2; exit 1; }

WORK="$(mktemp -d -t doodlebugs-aab)"
trap 'rm -rf "$WORK"' EXIT
umask 077

KEYSTORE="$WORK/upload.keystore"
echo -n "$ANDROID_KEYSTORE_BASE64" | tr -d '[:space:]' | base64 --decode > "$KEYSTORE"

# ── Tools ────────────────────────────────────────────────────────────────────
# jarsigner from Unity's bundled OpenJDK (the runner has no system JDK).
find_jarsigner() {
  local roots=()
  local unity_version
  unity_version="$(awk '/^m_EditorVersion:/ {print $2}' ProjectSettings/ProjectVersion.txt 2>/dev/null || true)"
  if [[ -n "$unity_version" ]]; then
    local hub_cfg="$HOME/Library/Application Support/UnityHub/secondaryInstallPath.json"
    if [[ -f "$hub_cfg" ]]; then
      local secondary
      secondary="$(tr -d '"[:space:]' < "$hub_cfg")"
      [[ -n "$secondary" ]] && roots+=("$secondary/$unity_version/PlaybackEngines/AndroidPlayer/OpenJDK")
    fi
    roots+=("/Applications/Unity/Hub/Editor/$unity_version/PlaybackEngines/AndroidPlayer/OpenJDK")
  fi
  [[ -n "${JAVA_HOME:-}" ]] && roots+=("$JAVA_HOME")
  local r
  for r in "${roots[@]}"; do
    [[ -x "$r/bin/jarsigner" ]] && { echo "$r/bin/jarsigner"; return; }
  done
  command -v jarsigner 2>/dev/null && return
  echo "jarsigner not found (looked in: ${roots[*]})" >&2
  return 1
}
JARSIGNER="$(find_jarsigner)"
echo "using $JARSIGNER"

# Same OpenSSL 3 lookup as sign-apk.sh: launchd's PATH has LibreSSL.
OPENSSL="$(
  for c in /opt/homebrew/opt/openssl@3/bin/openssl /usr/local/opt/openssl@3/bin/openssl \
           /opt/homebrew/bin/openssl /usr/local/bin/openssl openssl; do
    command -v "$c" >/dev/null 2>&1 && "$c" version 2>/dev/null | grep -q '^OpenSSL 3' && { echo "$c"; break; }
  done
)"
[[ -n "$OPENSSL" ]] || { echo "OpenSSL 3 is required (brew install openssl@3)" >&2; exit 1; }

export AAB_STORE_PASS="$ANDROID_KEYSTORE_PASSWORD"
ALIAS="upload"

if [[ "$(head -c 4 "$KEYSTORE" | xxd -p)" == "feedfeed" ]]; then
  # A plain JKS: Java reads it as is.
  : "${ANDROID_KEY_ALIAS:?ANDROID_KEY_ALIAS is required for a JKS keystore}"
  : "${ANDROID_KEY_PASSWORD:?ANDROID_KEY_PASSWORD is required for a JKS keystore}"
  export AAB_KEY_PASS="$ANDROID_KEY_PASSWORD"
  JAVA_STORE="$KEYSTORE"
  STORE_TYPE="JKS"
  ALIAS="$ANDROID_KEY_ALIAS"
else
  LEGACY_FLAG=""
  if ! "$OPENSSL" pkcs12 -in "$KEYSTORE" -noout -passin env:AAB_STORE_PASS 2>/dev/null; then
    if "$OPENSSL" pkcs12 -in "$KEYSTORE" -noout -passin env:AAB_STORE_PASS -legacy 2>/dev/null; then
      LEGACY_FLAG="-legacy"
    else
      echo "The keystore does not open with ANDROID_KEYSTORE_PASSWORD (tried plain and -legacy)" >&2
      exit 1
    fi
  fi

  # PEM key + cert, then a fresh PKCS#12 with nothing but them (AES, SHA-256
  # MAC): every JDK since 8u301 reads that.
  "$OPENSSL" pkcs12 -in "$KEYSTORE" -nocerts -nodes -passin env:AAB_STORE_PASS $LEGACY_FLAG > "$WORK/key.pem"
  "$OPENSSL" pkcs12 -in "$KEYSTORE" -nokeys -clcerts -passin env:AAB_STORE_PASS $LEGACY_FLAG > "$WORK/cert.pem"
  [[ -s "$WORK/key.pem" && -s "$WORK/cert.pem" ]] || { echo "key/cert extraction produced nothing" >&2; exit 1; }

  JAVA_STORE="$WORK/java.p12"
  "$OPENSSL" pkcs12 -export -inkey "$WORK/key.pem" -in "$WORK/cert.pem" -name "$ALIAS" \
    -keypbe AES-256-CBC -certpbe AES-256-CBC -macalg sha256 \
    -passout env:AAB_STORE_PASS -out "$JAVA_STORE"
  export AAB_KEY_PASS="$ANDROID_KEYSTORE_PASSWORD"
  STORE_TYPE="PKCS12"
fi

# ── Sign and verify ──────────────────────────────────────────────────────────
cp "$IN_AAB" "$OUT_AAB"
"$JARSIGNER" -keystore "$JAVA_STORE" -storetype "$STORE_TYPE" \
  -storepass:env AAB_STORE_PASS -keypass:env AAB_KEY_PASS \
  "$OUT_AAB" "$ALIAS"

# -verify exits 0 even for an unsigned jar; look for the verdict instead.
"$JARSIGNER" -verify "$OUT_AAB" | tee "$WORK/verify.txt" | tail -3
grep -q "jar verified" "$WORK/verify.txt" || { echo "jarsigner did not verify $OUT_AAB" >&2; exit 1; }
echo "signed: $OUT_AAB"
