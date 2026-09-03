using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Doodlebugs -> Setup Plane Skin Manager
///
/// One-time (per scene) wiring: creates the "PlaneSkinManager" GameObject
/// with a NetworkObject + PlaneSkinManager component in the open scene, the
/// same shape BackgroundManager already has. Server-authoritative networked
/// singletons in this project are placed directly in the scene (auto-spawned
/// by Netcode on scene load) rather than instantiated via
/// NetworkObjectSpawner, so this has to be a one-time Editor step instead of
/// runtime code - run it once on Scene01, commit the scene change, done.
///
/// Safe to re-run: no-ops if a PlaneSkinManager already exists in the scene.
/// </summary>
public static class PlaneSkinManagerSetup
{
    public const string GameScenePath = "Assets/Doodlebugs/Scenes/Scene01.unity";

    [MenuItem("Doodlebugs/Setup Plane Skin Manager")]
    public static void Setup()
    {
        AddToScene(SceneManager_GetActiveScene());
    }

    /// <summary>
    /// Batchmode entry point (-executeMethod PlaneSkinManagerSetup.SetupBatch).
    /// The menu item works on whatever scene is open, which in batchmode is an
    /// empty untitled one - this opens Scene01 explicitly first.
    /// </summary>
    public static void SetupBatch()
    {
        var scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
        AddToScene(scene);
    }

    private static void AddToScene(UnityEngine.SceneManagement.Scene scene)
    {
        var existing = Object.FindFirstObjectByType<PlaneSkinManager>(FindObjectsInactive.Include);
        if (existing != null)
        {
            Debug.Log("[PlaneSkinManagerSetup] PlaneSkinManager already present in the scene - nothing to do.");
            return;
        }

        var go = new GameObject("PlaneSkinManager");
        go.AddComponent<NetworkObject>();
        go.AddComponent<PlaneSkinManager>();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log($"[PlaneSkinManagerSetup] Created PlaneSkinManager in {scene.path} and saved it. " +
                  "Commit the updated .unity file.");
    }

    private static UnityEngine.SceneManagement.Scene SceneManager_GetActiveScene() =>
        UnityEngine.SceneManagement.SceneManager.GetActiveScene();
}
