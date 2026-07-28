#!/usr/bin/env bash
#
# Prepares a codesigning keychain on the self-hosted runner and imports the
# distribution certificate into it.
#
# Why a dedicated keychain rather than unlocking the login keychain:
# the runner is a launchd daemon with no GUI session, so the login keychain is
# locked and codesign fails with errSecInternalComponent. Since the certificate
# arrives from GitHub secrets as a .p12 on every run anyway, a throwaway keychain
# solves the same problem and is trivially removable — which is also what makes
# "nothing left on disk after the job" enforceable. The runner's login keychain is
# never touched.
#
# Environment:
#   IOS_KEYCHAIN_PASSWORD  password for the throwaway keychain (any string)
#   IOS_P12_BASE64         base64-encoded distribution certificate
#   IOS_P12_PASSWORD       password protecting the .p12
#
# Teardown is ci/lock-keychain.sh — call it from an if: always() step.

set -euo pipefail

: "${IOS_KEYCHAIN_PASSWORD:?IOS_KEYCHAIN_PASSWORD is required}"
: "${IOS_P12_BASE64:?IOS_P12_BASE64 is required}"
: "${IOS_P12_PASSWORD:?IOS_P12_PASSWORD is required}"

KEYCHAIN_NAME="doodlebugs-ci.keychain-db"
KEYCHAIN_PATH="$HOME/Library/Keychains/$KEYCHAIN_NAME"

# A leftover keychain from a crashed run would still hold an old certificate.
security delete-keychain "$KEYCHAIN_PATH" 2>/dev/null || true

security create-keychain -p "$IOS_KEYCHAIN_PASSWORD" "$KEYCHAIN_PATH"
security unlock-keychain -p "$IOS_KEYCHAIN_PASSWORD" "$KEYCHAIN_PATH"

# -t 3600 -u: relock after an hour of inactivity instead of the 5-minute default,
# so the keychain does not lock itself midway through a long Unity/Xcode build.
security set-keychain-settings -t 3600 -u "$KEYCHAIN_PATH"

# xcodebuild only searches keychains on the user search list. Keep the login
# keychain in the list so anything else on the machine keeps working.
security list-keychains -d user -s "$KEYCHAIN_PATH" "$HOME/Library/Keychains/login.keychain-db"

WORK="$(mktemp -d -t doodlebugs-signing)"
trap 'rm -rf "$WORK"' EXIT
P12="$WORK/signing.p12"

echo -n "$IOS_P12_BASE64" | tr -d '[:space:]' | base64 --decode > "$P12" 2>/dev/null

# security import reports both a malformed file and a wrong password as
# "One or more parameters passed to a function were not valid", which sends you
# looking in the wrong place. Probe with openssl first and say which it is.
if [[ ! -s "$P12" ]]; then
  echo "IOS_P12_BASE64 decoded to nothing — the secret is empty or not valid base64." >&2
  exit 1
fi

if ! openssl pkcs12 -in "$P12" -passin "pass:$IOS_P12_PASSWORD" -noout -legacy 2>/dev/null \
   && ! openssl pkcs12 -in "$P12" -passin "pass:$IOS_P12_PASSWORD" -noout 2>/dev/null; then
  echo "The decoded IOS_P12_BASE64 is not a PKCS#12 that opens with IOS_P12_PASSWORD." >&2
  echo "First bytes: $(head -c 16 "$P12" | xxd -p)" >&2
  if head -c 32 "$P12" | grep -q "BEGIN"; then
    echo "Looks like PEM, not PKCS#12. Re-export from Keychain Access as .p12," >&2
    echo "then: base64 -i cert.p12 | tr -d '\\n' | gh secret set IOS_P12_BASE64" >&2
  else
    echo "Either the blob is not a .p12 at all, or IOS_P12_PASSWORD is wrong." >&2
  fi
  exit 1
fi

security import "$P12" \
  -k "$KEYCHAIN_PATH" \
  -P "$IOS_P12_PASSWORD" \
  -T /usr/bin/codesign \
  -T /usr/bin/security

# Without this, codesign pops a GUI "allow access?" dialog that nobody can click on
# a headless runner, and the build hangs until it times out.
security set-key-partition-list \
  -S apple-tool:,apple:,codesign: \
  -s -k "$IOS_KEYCHAIN_PASSWORD" \
  "$KEYCHAIN_PATH" >/dev/null

echo "keychain ready: $KEYCHAIN_PATH"
security find-identity -v -p codesigning "$KEYCHAIN_PATH"
