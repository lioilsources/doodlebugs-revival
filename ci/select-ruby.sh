#!/usr/bin/env bash
#
# Puts a fastlane-capable Ruby on PATH for the rest of the job.
#
# macOS still ships Ruby 2.6 at /usr/bin/ruby and that is what a launchd-started
# runner picks up. fastlane dropped 2.6 years ago, so without this the job fails
# deep inside `bundle install` with a gem resolution error that says nothing about
# the actual cause.
#
# Writing to $GITHUB_PATH affects subsequent steps, not this one.

set -euo pipefail

MIN_MAJOR=3
MIN_MINOR=0

version_ok() {
  local ruby="$1" major minor
  major="$("$ruby" -e 'print RUBY_VERSION.split(".")[0]' 2>/dev/null)" || return 1
  minor="$("$ruby" -e 'print RUBY_VERSION.split(".")[1]' 2>/dev/null)" || return 1
  (( major > MIN_MAJOR )) || { (( major == MIN_MAJOR )) && (( minor >= MIN_MINOR )); }
}

CANDIDATE_DIRS=(
  "/opt/homebrew/opt/ruby/bin"   # Apple silicon Homebrew
  "/usr/local/opt/ruby/bin"      # Intel Homebrew
  "$HOME/.rbenv/shims"
)

for dir in "${CANDIDATE_DIRS[@]}"; do
  if [[ -x "$dir/ruby" ]] && version_ok "$dir/ruby"; then
    echo "using $("$dir/ruby" --version) from $dir"
    [[ -n "${GITHUB_PATH:-}" ]] && echo "$dir" >> "$GITHUB_PATH"
    exit 0
  fi
done

# Fall back to whatever is already on PATH, but only if it is new enough.
if command -v ruby >/dev/null && version_ok "$(command -v ruby)"; then
  echo "using $(ruby --version) from PATH"
  exit 0
fi

echo "No Ruby >= $MIN_MAJOR.$MIN_MINOR found." >&2
echo "Current: $(ruby --version 2>/dev/null || echo 'none')" >&2
echo "Install one with: brew install ruby" >&2
exit 1
