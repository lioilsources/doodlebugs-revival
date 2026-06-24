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
        }
    }
}
#endif
