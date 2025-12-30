#if UNITY_IOS
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using System.IO;

namespace Doodlebugs.Editor
{
    public static class iOSPostProcessBuild
    {
        [PostProcessBuild(1)]
        public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS)
                return;

            string plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
            PlistDocument plist = new PlistDocument();
            plist.ReadFromFile(plistPath);

            // Add local network usage description for UDP discovery
            plist.root.SetString("NSLocalNetworkUsageDescription",
                "This app uses local network to find nearby players for multiplayer games.");

            // Add Bonjour services for discovery
            var bonjourServices = plist.root.CreateArray("NSBonjourServices");
            bonjourServices.AddString("_doodlebugs._udp");

            plist.WriteToFile(plistPath);
        }
    }
}
#endif
