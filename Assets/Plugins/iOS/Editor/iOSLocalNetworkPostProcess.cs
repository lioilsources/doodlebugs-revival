#if UNITY_IOS
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using System.IO;

/// <summary>
/// Post-process iOS build for local-network (LAN UDP broadcast) game discovery.
/// iOS 14+ requires NSLocalNetworkUsageDescription for any LAN traffic - without
/// it the OS silently blocks discovery and the game can never find/host a match.
/// Sending/receiving UDP broadcast additionally requires the Apple-granted
/// com.apple.developer.networking.multicast entitlement.
/// </summary>
public class iOSLocalNetworkPostProcess
{
    // Flip to true ONLY after Apple grants the multicast entitlement to your App ID
    // (request at https://developer.apple.com/contact/request/networking-multicast).
    // Building with the entitlement but without the grant fails code signing.
    private static readonly bool AddMulticastEntitlement = false;

    private const string EntitlementsFileName = "Doodlebugs.entitlements";
    private const string LocalNetworkUsageDescription =
        "Doodlebugs uses your local network to find and connect to nearby players.";

    [PostProcessBuild(101)]
    public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
    {
        if (target != BuildTarget.iOS)
            return;

        // Info.plist: local network usage description + Bonjour service types.
        // NSBonjourServices is REQUIRED on iOS 14+ for MultipeerConnectivity —
        // MCNearbyServiceBrowser/Advertiser publish the session as Bonjour
        // services named after NativeLocalCoopManager.SERVICE_TYPE. Without
        // these keys browsing fails silently and iOS devices never pair
        // (iOS pairs exclusively through Multipeer since the Wi-Fi broadcast
        // path was gated off in ConnectionManager.DetermineMode).
        string plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
        PlistDocument plist = new PlistDocument();
        plist.ReadFromFile(plistPath);
        plist.root.SetString("NSLocalNetworkUsageDescription", LocalNetworkUsageDescription);
        PlistElementArray bonjour = plist.root.CreateArray("NSBonjourServices");
        bonjour.AddString("_doodlebugs._tcp");
        bonjour.AddString("_doodlebugs._udp");
        plist.WriteToFile(plistPath);

        // Optional multicast entitlement
        if (AddMulticastEntitlement)
        {
            string projPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
            PBXProject proj = new PBXProject();
            proj.ReadFromFile(projPath);
            string mainTargetGuid = proj.GetUnityMainTargetGuid();

            // Merge with an existing entitlements file if one is ever added
            string entitlementsPath = Path.Combine(pathToBuiltProject, EntitlementsFileName);
            PlistDocument entitlements = new PlistDocument();
            if (File.Exists(entitlementsPath))
            {
                entitlements.ReadFromFile(entitlementsPath);
            }
            entitlements.root.SetBoolean("com.apple.developer.networking.multicast", true);
            entitlements.WriteToFile(entitlementsPath);

            proj.AddFile(EntitlementsFileName, EntitlementsFileName);
            proj.AddBuildProperty(mainTargetGuid, "CODE_SIGN_ENTITLEMENTS", EntitlementsFileName);
            proj.WriteToFile(projPath);
        }

        UnityEngine.Debug.Log("[iOSLocalNetworkPostProcess] Added local-network permission"
            + (AddMulticastEntitlement ? " + multicast entitlement" : " (multicast entitlement OFF)"));
    }
}
#endif
