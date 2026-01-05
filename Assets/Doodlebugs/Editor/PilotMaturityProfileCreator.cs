using UnityEngine;
using UnityEditor;
using System.IO;

public class PilotMaturityProfileCreator
{
    private const string ProfilesPath = "Assets/Doodlebugs/Prefabs/MaturityProfiles";

    [MenuItem("Doodlebugs/Create Maturity Profiles")]
    public static void CreateAllProfiles()
    {
        // Ensure directory exists
        if (!Directory.Exists(ProfilesPath))
        {
            Directory.CreateDirectory(ProfilesPath);
            AssetDatabase.Refresh();
        }

        CreateProfile(PilotMaturityLevel.Novice);
        CreateProfile(PilotMaturityLevel.Advanced);
        CreateProfile(PilotMaturityLevel.Expert);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[Doodlebugs] Created 3 maturity profiles in {ProfilesPath}");
        EditorUtility.DisplayDialog("Profiles Created",
            $"Created 3 pilot maturity profiles:\n- Novice\n- Advanced\n- Expert\n\nLocation: {ProfilesPath}",
            "OK");
    }

    private static void CreateProfile(PilotMaturityLevel level)
    {
        string assetPath = $"{ProfilesPath}/{level}Profile.asset";

        // Check if already exists
        var existing = AssetDatabase.LoadAssetAtPath<PilotMaturityProfile>(assetPath);
        if (existing != null)
        {
            Debug.Log($"[Doodlebugs] Profile already exists: {assetPath}");
            return;
        }

        // Create profile with level-specific values (single source of truth for asset creation)
        var profile = ScriptableObject.CreateInstance<PilotMaturityProfile>();
        profile.level = level;
        profile.name = $"{level}Profile";

        switch (level)
        {
            case PilotMaturityLevel.Expert:
                profile.rotateSpeed = 200f;
                profile.maxSpeed = 20f;
                profile.maxGravity = 0.5f;
                profile.gravityIncreaseRate = 0.35f;
                profile.engineOffRotateMultiplier = 2f;  // 200 * 2 = 400
                profile.engineRestartMinRotation = -0.8f;
                profile.engineRestartMaxRotation = -0.6f;
                profile.bulletForceMultiplier = 2f;
                profile.bulletGravityScale = 2f;
                break;

            case PilotMaturityLevel.Advanced:
                profile.rotateSpeed = 100f;
                profile.maxSpeed = 20f;
                profile.maxGravity = 0.25f;
                profile.gravityIncreaseRate = 0.175f;
                profile.engineOffRotateMultiplier = 6f;  // 100 * 6 = 600
                profile.engineRestartMinRotation = -0.85f;
                profile.engineRestartMaxRotation = -0.53f;
                profile.bulletForceMultiplier = 1f;
                profile.bulletGravityScale = 1f;
                break;

            case PilotMaturityLevel.Novice:
                // Uses class default values (Novice)
                break;
        }

        AssetDatabase.CreateAsset(profile, assetPath);
        Debug.Log($"[Doodlebugs] Created profile: {assetPath}");
    }

    [MenuItem("Doodlebugs/Setup Maturity Manager in Scene")]
    public static void SetupManagerInScene()
    {
        // Check if manager already exists
        var existingManager = Object.FindObjectOfType<PilotMaturityManager>();
        if (existingManager != null)
        {
            Selection.activeGameObject = existingManager.gameObject;
            Debug.Log("[Doodlebugs] PilotMaturityManager already exists in scene");
            return;
        }

        // Create new GameObject with manager
        var managerGO = new GameObject("PilotMaturityManager");
        var manager = managerGO.AddComponent<PilotMaturityManager>();

        // Try to assign profiles
        var novice = AssetDatabase.LoadAssetAtPath<PilotMaturityProfile>($"{ProfilesPath}/NoviceProfile.asset");
        var advanced = AssetDatabase.LoadAssetAtPath<PilotMaturityProfile>($"{ProfilesPath}/AdvancedProfile.asset");
        var expert = AssetDatabase.LoadAssetAtPath<PilotMaturityProfile>($"{ProfilesPath}/ExpertProfile.asset");

        if (novice == null || advanced == null || expert == null)
        {
            EditorUtility.DisplayDialog("Profiles Missing",
                "Please run 'Doodlebugs/Create Maturity Profiles' first to create the profile assets.",
                "OK");
        }

        // Use SerializedObject to set private serialized fields
        var serializedObj = new SerializedObject(manager);
        serializedObj.FindProperty("noviceProfile").objectReferenceValue = novice;
        serializedObj.FindProperty("advancedProfile").objectReferenceValue = advanced;
        serializedObj.FindProperty("expertProfile").objectReferenceValue = expert;
        serializedObj.ApplyModifiedProperties();

        Selection.activeGameObject = managerGO;
        Undo.RegisterCreatedObjectUndo(managerGO, "Create PilotMaturityManager");

        Debug.Log("[Doodlebugs] Created PilotMaturityManager in scene");
    }
}
