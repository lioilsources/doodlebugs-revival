# Mobile release pipeline

One tag ships both mobile platforms:

```bash
git tag v0.4.0 && git push --tags
```

| Workflow | Runner | Output |
|---|---|---|
| `release-ios.yml` | self-hosted `macos-unity` | `.ipa` → TestFlight |
| `release-android.yml` | self-hosted `macos-unity` | `.apk` → Firebase App Distribution |
| `release.yml` | GitHub-hosted `ubuntu-latest` | Windows / Linux / macOS → GitHub Release |

All three fire on `v*.*.*`. The two mobile workflows share a `concurrency` group so
they never run two Unity editors against the same `Library/` at once.

`workflow_dispatch` is enabled on each mobile workflow separately, which is how you
rebuild a single platform without cutting a tag.

## Layout

```
ci/
  select-ruby.sh      puts a fastlane-capable Ruby on PATH
  unity-build.sh      headless Unity build (ios | android)
  unlock-keychain.sh  creates a throwaway codesigning keychain, imports the .p12
  install-profile.sh  installs the provisioning profile, exports its name/UUID/team
  lock-keychain.sh    teardown for the two above
  sign-apk.sh         signs the APK with apksigner
fastlane/
  Appfile Fastfile Pluginfile
Assets/Doodlebugs/Editor/
  BuildScript.cs            CI entry points (BuildIOS / BuildAndroid)
  iOSPostProcessBuild.cs    Info.plist: local network + export compliance
```

## Secrets

The pipeline reuses the secret names already configured on this repository rather
than introducing a parallel set:

| Secret | Used by | Notes |
|---|---|---|
| `IOS_P12_BASE64` | `unlock-keychain.sh` | base64 distribution certificate |
| `IOS_P12_PASSWORD` | `unlock-keychain.sh` | |
| `IOS_KEYCHAIN_PASSWORD` | `unlock-keychain.sh` | password for the throwaway keychain, any string |
| `IOS_PROVISION_PROFILE_BASE64` | `install-profile.sh` | base64 App Store profile |
| `IOS_TEAM_ID` | `Fastfile` | |
| `APPSTORE_API_KEY_ID` | `Fastfile` | |
| `APPSTORE_ISSUER_ID` | `Fastfile` | |
| `APP_STORE_CONNECT_API_KEY_BASE64` | `Fastfile` | preferred |
| `APPSTORE_API_PRIVATE_KEY` | `Fastfile` | fallback, raw `.p8` contents |
| `ANDROID_KEYSTORE_BASE64` | `sign-apk.sh` | base64 keystore |
| `ANDROID_KEYSTORE_PASSWORD` | `sign-apk.sh` | |
| `ANDROID_KEY_ALIAS` | `sign-apk.sh` | JKS path only |
| `ANDROID_KEY_PASSWORD` | `sign-apk.sh` | JKS path only |
| `FIREBASE_ANDROID_APP_ID` | `Fastfile` | `1:…:android:…` |
| `FIREBASE_SERVICE_ACCOUNT_KEY` | `release-android.yml` | raw JSON or base64, both handled |

There is deliberately **no** `UNITY_LICENSE` / `UNITY_EMAIL` / `UNITY_PASSWORD` for
the mobile workflows. The Mac Mini holds a locally activated Personal licence, and
passing credentials to a batch-mode editor starts a re-activation flow that Personal
cannot finish. (`release.yml` still uses those secrets — it runs game-ci in Docker on
a GitHub-hosted runner, where the licence has to be supplied.)

Anything multi-line goes into a secret base64-encoded. A PEM or JSON pasted directly
survives just often enough to be misleading, and when the newlines are mangled the
failure surfaces much later as an unhelpful signature-validation error.

## Runner requirements

The `macos-unity` runner needs, on the machine itself:

- Unity Hub with a Personal licence activated, and the editor version from
  `ProjectSettings/ProjectVersion.txt` installed with **iOS Build Support** and
  **Android Build Support + OpenJDK + NDK**. The editor may live outside
  `/Applications` — `unity-build.sh` reads Unity Hub's install-location override.
- Xcode with `sudo xcodebuild -license accept` already answered.
- Ruby ≥ 3.0 — `brew install ruby`. macOS still ships 2.6 at `/usr/bin/ruby`, which
  fastlane no longer supports and which is what a launchd-started runner would pick
  up; `select-ruby.sh` finds the Homebrew one and fails loudly if there is none.
  fastlane itself is installed per-repo by `bundle install`, so no `brew install
  fastlane` is needed.
- The runner registered with the label `macos-unity` and installed as a launchd
  service (`./svc.sh install && ./svc.sh start`).

## Running it locally

Everything works outside CI, which is the fastest way to debug a failure:

```bash
# Android — produces an unsigned APK
GITHUB_RUN_NUMBER=1 BUILD_VERSION=0.4.0 ./ci/unity-build.sh android /tmp/out/doodlebugs.apk

# iOS — produces an Xcode project, not an IPA
GITHUB_RUN_NUMBER=1 BUILD_VERSION=0.4.0 ./ci/unity-build.sh ios /tmp/out/ios

# distribution lanes
bundle install
bundle exec fastlane android android_beta
bundle exec fastlane ios ios_beta
```

A local run writes `bundleVersionCode` / `bundleVersion` into
`ProjectSettings/ProjectSettings.asset`. Revert it before committing unless the
change was intentional.

## Design notes

**Unity exits 0 on a failed build.** `BuildScript.Finish` inspects
`BuildReport.summary.result` and calls `EditorApplication.Exit(1)`, otherwise a
broken build would go green and publish an empty artifact. `unity-build.sh` then
also asserts the output path exists.

**`-logFile -`** streams the editor log to stdout. Without it Unity logs to a file
and the job shows nothing at all until it hits the timeout.

**A throwaway keychain, not the login keychain.** The runner is a launchd daemon with
no GUI session, so its login keychain is locked and `codesign` fails with
`errSecInternalComponent`. Because the certificate arrives as a `.p12` from secrets on
every run, `unlock-keychain.sh` builds a dedicated keychain instead — same fix for the
locked-keychain problem, and deleting it afterwards is what makes "no signing material
left on disk" actually true. The login keychain is never touched.

**Manual signing has to be re-applied after every Unity build.** `iOSPostProcessBuild`
deliberately sets `CODE_SIGN_STYLE=Automatic` and the team on both targets so that a
plain local `xcodebuild` provisions itself without hand-editing the export. That cannot
work on the runner: automatic signing needs an Xcode account signed in, and a launchd
daemon has none — the archive step fails with "No signing certificate found".

So the fastlane lane flips both targets back to manual after the Unity step. It has to
run after, not once at setup, because Unity regenerates the project on every build.
Only `Unity-iPhone` carries the provisioning profile; `UnityFramework` gets an identity
but no profile of its own and is re-signed with the app's identity when embedded.

If you change the signing setup, change it in both places or they will fight.

**The APK is signed after the build, not by Unity.** This project's upload keystore is
a PKCS#12 whose ASN.1 uses tag numbers above 30, which Java's `DerValue` still cannot
parse in JDK 21 — so neither Unity's signing step nor `apksigner --ks` can open it.
`sign-apk.sh` extracts the key and certificate with OpenSSL and passes them to
`apksigner --key/--cert` **in DER form**: `--key` rejects PEM with the thoroughly
unhelpful "Not an RSA, EC, or DSA private key", so the `-outform DER` on both openssl
calls is load-bearing. A normal JKS falls through to the Java path automatically, and
`BuildScript` keeps its `ANDROID_KEYSTORE_*` support for that case.

**Build numbers come from `github.run_number`.** TestFlight rejects a duplicate
`CFBundleVersion`, and the run number is monotonic, so re-running the same commit still
produces an acceptable build. The marketing version comes from the tag.

Android offsets it by 100 (`BUILD_NUMBER: ${{ 100 + github.run_number }}`). The run
number restarts at 1 for a newly added workflow, but releases up to v1.0.6 already
shipped `versionCode` 8 — and Android refuses to install an APK whose versionCode is
lower than the installed one, so testers would get a bare "app not installed". Raise
that offset if it ever needs to clear a higher published code; never lower it.

The marketing version is also sanitised for Apple: `CFBundleShortVersionString` accepts
only digits and dots, so a tag like `v2.1.1-beta.1` is trimmed to `2.1.1` for iOS while
Android keeps the full string in `versionName`.

**Export compliance is declared in `Info.plist`.** Without
`ITSAppUsesNonExemptEncryption` every upload parks in TestFlight waiting for someone
to answer the encryption question by hand, which from CI just looks like the build
never arrived.

**`Library/` is neither cached nor cleaned.** `actions/checkout` runs
`git clean -ffdx` by default, which would delete the gitignored `Library/` folder and
add a full reimport to every build; the mobile workflows set `clean: false` so it
survives between runs naturally. `actions/cache` would be pure overhead here.
