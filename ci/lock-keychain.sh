#!/usr/bin/env bash
#
# Teardown counterpart to ci/unlock-keychain.sh. Removes the throwaway keychain
# (and with it the imported certificate and private key) and restores the login
# keychain as the only entry on the user search list.
#
# Safe to call when setup never ran — intended for an `if: always()` step.

set -uo pipefail

KEYCHAIN_PATH="$HOME/Library/Keychains/doodlebugs-ci.keychain-db"

security delete-keychain "$KEYCHAIN_PATH" 2>/dev/null || true
security list-keychains -d user -s "$HOME/Library/Keychains/login.keychain-db" 2>/dev/null || true

# PROFILE_UUID is published to $GITHUB_ENV by ci/install-profile.sh and is still
# visible here because always() steps see the job environment.
if [[ -n "${PROFILE_UUID:-}" ]]; then
  rm -f "$HOME/Library/MobileDevice/Provisioning Profiles/$PROFILE_UUID.mobileprovision"
fi

echo "codesigning material removed"
exit 0
