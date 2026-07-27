#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Doodlebugs.Editor
{
    /// <summary>
    /// CI entry points for headless builds. Invoked from ci/unity-build.sh as
    ///   -executeMethod Doodlebugs.Editor.BuildScript.BuildIOS
    ///   -executeMethod Doodlebugs.Editor.BuildScript.BuildAndroid
    ///
    /// Recognised extra CLI arguments:
    ///   -buildPath    &lt;dir&gt;   output directory (iOS: Xcode project, Android: .apk path)
    ///   -buildNumber  &lt;n&gt;     CFBundleVersion / versionCode, supplied by CI as GITHUB_RUN_NUMBER
    ///   -buildVersion &lt;x.y.z&gt; optional marketing version (bundleVersion), derived from the git tag
    ///
    /// The build number deliberately comes from CI rather than ProjectSettings:
    /// TestFlight rejects a duplicate CFBundleVersion, so it has to be monotonic
    /// across runs and nobody wants to bump it by hand before every release.
    /// </summary>
    public static class BuildScript
    {
        // ── CLI plumbing ──────────────────────────────────────────────────────

        /// <summary>Reads "-name value" from the Unity command line, or null when absent.</summary>
        private static string GetArg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                    return args[i + 1];
            }
            return null;
        }

        private static string[] EnabledScenes =>
            EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

        /// <summary>
        /// Apple only accepts a CFBundleShortVersionString made of digits and dots,
        /// so a pre-release tag like v1.2.3-beta.1 has to be trimmed down to 1.2.3
        /// before it reaches PlayerSettings. Android's versionName has no such rule
        /// and keeps the full tag, which makes beta builds easier to tell apart.
        /// </summary>
        private static string SanitizeAppleVersion(string version)
        {
            var match = Regex.Match(version ?? string.Empty, @"^[0-9]+(\.[0-9]+)*");
            if (!match.Success)
                return null;

            var result = match.Value.TrimEnd('.');
            return result.Length > 18 ? result.Substring(0, 18).TrimEnd('.') : result;
        }

        /// <summary>
        /// Applies the CI-supplied version numbers. Returns the build number string
        /// so each platform can also map it onto its own field.
        /// </summary>
        /// <param name="appleVersionRules">
        /// Restrict the marketing version to Apple's digits-and-dots format.
        /// </param>
        private static string ApplyVersioning(bool appleVersionRules)
        {
            var buildVersion = GetArg("-buildVersion");
            if (!string.IsNullOrEmpty(buildVersion))
            {
                if (appleVersionRules)
                {
                    var sanitized = SanitizeAppleVersion(buildVersion);
                    if (string.IsNullOrEmpty(sanitized))
                    {
                        Debug.LogError($"[BuildScript] -buildVersion '{buildVersion}' has no numeric prefix; " +
                                       "Apple requires a version made of digits and dots.");
                        EditorApplication.Exit(1);
                        return null;
                    }

                    if (sanitized != buildVersion)
                        Log($"trimmed -buildVersion '{buildVersion}' to '{sanitized}' for Apple");

                    buildVersion = sanitized;
                }

                PlayerSettings.bundleVersion = buildVersion;
                Log($"bundleVersion = {buildVersion}");
            }

            var buildNumber = GetArg("-buildNumber");
            if (string.IsNullOrEmpty(buildNumber))
            {
                // Local runs without CI: fall back to whatever ProjectSettings holds.
                Log("no -buildNumber supplied, keeping the value from ProjectSettings");
                return null;
            }

            return buildNumber;
        }

        private static void Log(string message) =>
            Debug.Log($"[BuildScript] {message}");

        // ── iOS ───────────────────────────────────────────────────────────────

        public static void BuildIOS()
        {
            try
            {
                var outputPath = GetArg("-buildPath") ?? "Builds/iOS";

                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS);

                var buildNumber = ApplyVersioning(appleVersionRules: true);
                if (!string.IsNullOrEmpty(buildNumber))
                {
                    PlayerSettings.iOS.buildNumber = buildNumber;
                    Log($"iOS buildNumber (CFBundleVersion) = {buildNumber}");
                }

                // A stale Xcode project silently keeps old Info.plist entries and
                // old native plugins, so start from a clean directory every time.
                if (Directory.Exists(outputPath))
                    Directory.Delete(outputPath, recursive: true);
                Directory.CreateDirectory(outputPath);

                Log($"building iOS Xcode project → {outputPath}");
                var options = new BuildPlayerOptions
                {
                    scenes = EnabledScenes,
                    locationPathName = outputPath,
                    target = BuildTarget.iOS,
                    targetGroup = BuildTargetGroup.iOS,
                    options = BuildOptions.None,
                };

                Finish(BuildPipeline.BuildPlayer(options));
            }
            catch (Exception e)
            {
                Debug.LogError($"[BuildScript] iOS build threw: {e}");
                EditorApplication.Exit(1);
            }
        }

        // ── Android ───────────────────────────────────────────────────────────

        public static void BuildAndroid()
        {
            try
            {
                var outputPath = GetArg("-buildPath") ?? "Builds/Android/doodlebugs.apk";

                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

                // Firebase App Distribution can serve an AAB, but only once the
                // project is linked to Google Play. APK keeps internal testing
                // independent of the Play Console.
                EditorUserBuildSettings.buildAppBundle = false;

                var buildNumber = ApplyVersioning(appleVersionRules: false);
                if (!string.IsNullOrEmpty(buildNumber))
                {
                    if (int.TryParse(buildNumber, out var versionCode))
                    {
                        PlayerSettings.Android.bundleVersionCode = versionCode;
                        Log($"Android bundleVersionCode = {versionCode}");
                    }
                    else
                    {
                        Debug.LogError($"[BuildScript] -buildNumber '{buildNumber}' is not an integer; " +
                                       "Android versionCode must be numeric.");
                        EditorApplication.Exit(1);
                        return;
                    }
                }

                ConfigureAndroidSigning();

                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                Log($"building Android APK → {outputPath}");
                var options = new BuildPlayerOptions
                {
                    scenes = EnabledScenes,
                    locationPathName = outputPath,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = BuildOptions.None,
                };

                Finish(BuildPipeline.BuildPlayer(options));
            }
            catch (Exception e)
            {
                Debug.LogError($"[BuildScript] Android build threw: {e}");
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Wires the upload keystore in from the environment. When the variables are
        /// absent the APK is left debug-signed on purpose — ci/sign-apk.sh then signs
        /// it with apksigner, which is the path this project uses because its upload
        /// keystore is a PKCS#12 that Java's KeyStore parser cannot read.
        /// </summary>
        private static void ConfigureAndroidSigning()
        {
            var keystorePath = Environment.GetEnvironmentVariable("ANDROID_KEYSTORE_PATH");
            if (string.IsNullOrEmpty(keystorePath))
            {
                PlayerSettings.Android.useCustomKeystore = false;
                Log("ANDROID_KEYSTORE_PATH unset — building unsigned, expecting apksigner downstream");
                return;
            }

            if (!File.Exists(keystorePath))
                throw new FileNotFoundException($"ANDROID_KEYSTORE_PATH points at a missing file: {keystorePath}");

            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = keystorePath;
            PlayerSettings.Android.keystorePass = Environment.GetEnvironmentVariable("ANDROID_KEYSTORE_PASS");
            PlayerSettings.Android.keyaliasName = Environment.GetEnvironmentVariable("ANDROID_KEYALIAS_NAME");
            PlayerSettings.Android.keyaliasPass = Environment.GetEnvironmentVariable("ANDROID_KEYALIAS_PASS");
            Log($"signing with keystore {keystorePath}, alias {PlayerSettings.Android.keyaliasName}");
        }

        // ── Result handling ───────────────────────────────────────────────────

        /// <summary>
        /// Unity exits 0 even when the build failed, so CI would happily publish an
        /// empty artifact. Translate the BuildReport into a real exit code.
        /// </summary>
        private static void Finish(BuildReport report)
        {
            var summary = report.summary;
            Log($"result={summary.result} " +
                $"size={summary.totalSize} bytes " +
                $"time={summary.totalTime} " +
                $"errors={summary.totalErrors} warnings={summary.totalWarnings}");

            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[BuildScript] build did not succeed: {summary.result}");
                EditorApplication.Exit(1);
                return;
            }

            EditorApplication.Exit(0);
        }
    }
}
#endif
