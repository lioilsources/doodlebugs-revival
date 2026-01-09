using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using System.IO;

/// <summary>
/// Post-process iOS build to enable Game Controller support
/// </summary>
public class iOSGameControllerPostProcess
{
    [PostProcessBuild(100)]
    public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
    {
        if (target != BuildTarget.iOS)
            return;

        // Modify Info.plist
        string plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
        PlistDocument plist = new PlistDocument();
        plist.ReadFromFile(plistPath);

        PlistElementDict rootDict = plist.root;

        // Add GCSupportedGameControllers
        PlistElementArray controllers = rootDict.CreateArray("GCSupportedGameControllers");

        // Extended Gamepad (full controllers like Xbox, PS, BackBone)
        PlistElementDict extendedGamepad = controllers.AddDict();
        extendedGamepad.SetString("ProfileName", "ExtendedGamepad");

        // Micro Gamepad (Siri Remote, simpler controllers)
        PlistElementDict microGamepad = controllers.AddDict();
        microGamepad.SetString("ProfileName", "MicroGamepad");

        // Enable controller user interaction
        rootDict.SetBoolean("GCSupportsControllerUserInteraction", true);

        plist.WriteToFile(plistPath);

        // Modify Xcode project to add GameController.framework
        string projPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
        PBXProject proj = new PBXProject();
        proj.ReadFromFile(projPath);

        string mainTargetGuid = proj.GetUnityMainTargetGuid();
        string frameworkTargetGuid = proj.GetUnityFrameworkTargetGuid();

        // Add GameController.framework
        proj.AddFrameworkToProject(mainTargetGuid, "GameController.framework", false);
        proj.AddFrameworkToProject(frameworkTargetGuid, "GameController.framework", false);

        proj.WriteToFile(projPath);

        UnityEngine.Debug.Log("[iOSGameControllerPostProcess] Added Game Controller support to iOS build");
    }
}
