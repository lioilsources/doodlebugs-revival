#if UNITY_IOS
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using System.IO;

namespace Doodlebugs.Editor
{
    public static class iOSPostProcessBuild
    {
        // Apple Developer team the test devices are registered under. A clean
        // Unity export leaves the main target on manual signing with an empty
        // team (PlayerSettings has automatic signing off), so xcodebuild fails
        // with "requires a provisioning profile" — set it here instead.
        private const string AppleTeamId = "P82HWPG7FN";

        [PostProcessBuild(1)]
        public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS)
                return;

            string plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
            PlistDocument plist = new PlistDocument();
            plist.ReadFromFile(plistPath);

            // Add local network usage description for UDP discovery + Multipeer
            plist.root.SetString("NSLocalNetworkUsageDescription",
                "This app uses the local network and nearby devices to find players for multiplayer games.");

            // Bluetooth usage descriptions — Multipeer Connectivity (mobile-data fallback)
            // links nearby devices over Bluetooth when no shared Wi-Fi is available.
            plist.root.SetString("NSBluetoothAlwaysUsageDescription",
                "This app uses Bluetooth to connect to nearby players for local multiplayer.");
            plist.root.SetString("NSBluetoothPeripheralUsageDescription",
                "This app uses Bluetooth to connect to nearby players for local multiplayer.");

            // Bonjour services for discovery — UDP (LAN) + Multipeer (_tcp/_udp).
            // The "doodlebugs" service type must match NativeLocalCoopManager.SERVICE_TYPE.
            var bonjourServices = plist.root.CreateArray("NSBonjourServices");
            bonjourServices.AddString("_doodlebugs._udp");
            bonjourServices.AddString("_doodlebugs._tcp");

            plist.WriteToFile(plistPath);

            // DoodlebugsMultipeer.m uses MCSession/MCPeerID — without this the
            // UnityFramework link step fails with undefined _OBJC_CLASS_$_MC* symbols.
            string projPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
            PBXProject proj = new PBXProject();
            proj.ReadFromFile(projPath);

            string mainGuid = proj.GetUnityMainTargetGuid();
            string frameworkGuid = proj.GetUnityFrameworkTargetGuid();

            proj.AddFrameworkToProject(frameworkGuid, "MultipeerConnectivity.framework", false);

            // Enable automatic signing with the team so `xcodebuild` can provision
            // a development profile without hand-editing the exported project.
            foreach (string guid in new[] { mainGuid, frameworkGuid })
            {
                proj.SetBuildProperty(guid, "CODE_SIGN_STYLE", "Automatic");
                proj.SetTeamId(guid, AppleTeamId);
            }

            proj.WriteToFile(projPath);
        }
    }
}
#endif
