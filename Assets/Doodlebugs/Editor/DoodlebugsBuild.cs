#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Doodlebugs.Editor
{
    /// <summary>
    /// Command-line build entry points (CI uses game-ci's own builder; these give
    /// the same result locally via -executeMethod).
    ///
    ///   Unity -quit -batchmode -projectPath . -buildTarget iOS \
    ///         -executeMethod Doodlebugs.Editor.DoodlebugsBuild.BuildIOS
    ///
    /// Exports the Xcode project into ./build-ios (incremental if it exists).
    /// </summary>
    public static class DoodlebugsBuild
    {
        private static string[] EnabledScenes() => EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();

        public static void BuildIOS()
        {
            Run(new BuildPlayerOptions
            {
                scenes = EnabledScenes(),
                locationPathName = "build-ios",
                target = BuildTarget.iOS,
                targetGroup = BuildTargetGroup.iOS,
                options = BuildOptions.None
            }, "iOS");
        }

        public static void BuildAndroid()
        {
            // Ship a single installable APK (not an .aab), matching prior releases.
            EditorUserBuildSettings.buildAppBundle = false;
            Run(new BuildPlayerOptions
            {
                scenes = EnabledScenes(),
                locationPathName = "build-android.apk",
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None
            }, "Android");
        }

        private static void Run(BuildPlayerOptions options, string label)
        {
            var summary = BuildPipeline.BuildPlayer(options).summary;
            Debug.Log($"[DoodlebugsBuild] {label} build {summary.result}: " +
                      $"{summary.totalSize} bytes, {summary.totalErrors} errors");

            if (summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                EditorApplication.Exit(1);
            }
        }
    }
}
#endif
