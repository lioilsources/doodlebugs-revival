using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Doodlebugs -> Sync Background Profiles
///
/// Asset pipeline for new arenas: drop a background PNG into
/// Sprites/Background/&lt;Name&gt;.png (4096x2732, landscape) and optionally a
/// foreground strip into Sprites/Foreground/&lt;Name&gt;_fg.png (4096 px wide,
/// 100 px = 1 world unit, transparent silhouette), then run this menu item.
/// It fixes import settings (foreground needs Read/Write + bottom-left
/// pivot), creates a missing Profile_&lt;Name&gt;.asset and re-registers ALL
/// profiles from Prefabs/Backgrounds on the scene's BackgroundManager.
///
/// Textures already referenced by a hand-made profile (Manhattan, Teheran,
/// SierraNevada - they predate the naming convention) are left untouched.
/// </summary>
public static class BackgroundProfileSync
{
    private const string BackgroundDir = "Assets/Doodlebugs/Sprites/Background";
    private const string ForegroundDir = "Assets/Doodlebugs/Sprites/Foreground";
    private const string ProfileDir = "Assets/Doodlebugs/Prefabs/Backgrounds";

    /// <summary>
    /// Batchmode entry point (-executeMethod BackgroundProfileSync.SyncBatch).
    /// RegisterProfilesInScene needs the BackgroundManager, which lives in
    /// Scene01 - in batchmode the open scene is an empty untitled one, so
    /// open it explicitly first.
    /// </summary>
    public static void SyncBatch()
    {
        EditorSceneManager.OpenScene(PlaneSkinManagerSetup.GameScenePath, OpenSceneMode.Single);
        Sync();
    }

    [MenuItem("Doodlebugs/Sync Background Profiles")]
    public static void Sync()
    {
        var profiles = LoadAllProfiles();

        // Sprites (by asset path) already claimed by an existing profile -
        // never touch their import settings or auto-create duplicates.
        var usedBackgrounds = new HashSet<string>();
        var usedForegrounds = new HashSet<string>();
        foreach (var profile in profiles)
        {
            if (profile.backgroundSprite != null)
                usedBackgrounds.Add(AssetDatabase.GetAssetPath(profile.backgroundSprite));
            if (profile.foregroundSprite != null)
                usedForegrounds.Add(AssetDatabase.GetAssetPath(profile.foregroundSprite));
        }

        int created = 0;
        foreach (string bgPath in Directory.GetFiles(BackgroundDir, "*.png"))
        {
            string assetPath = bgPath.Replace('\\', '/');
            if (usedBackgrounds.Contains(assetPath)) continue;

            string name = Path.GetFileNameWithoutExtension(assetPath);
            string fgPath = $"{ForegroundDir}/{name}_fg.png";
            bool hasForeground = File.Exists(fgPath) && !usedForegrounds.Contains(fgPath);

            EnsureBackgroundImport(assetPath);
            if (hasForeground) EnsureForegroundImport(fgPath);

            var profile = ScriptableObject.CreateInstance<BackgroundProfile>();
            profile.backgroundSprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            profile.foregroundSprite = hasForeground
                ? AssetDatabase.LoadAssetAtPath<Sprite>(fgPath)
                : null; // null = runtime placeholder silhouette

            string profilePath = $"{ProfileDir}/Profile_{name}.asset";
            AssetDatabase.CreateAsset(profile, profilePath);
            profiles.Add(profile);
            created++;
            Debug.Log($"[BackgroundProfileSync] Created {profilePath}" +
                      (hasForeground ? $" (fg: {name}_fg.png)" : " (no foreground - placeholder)"));
        }

        AssetDatabase.SaveAssets();
        RegisterProfilesInScene(profiles);

        Debug.Log($"[BackgroundProfileSync] Done - {created} profile(s) created, " +
                  $"{profiles.Count} registered on BackgroundManager");
    }

    private static List<BackgroundProfile> LoadAllProfiles()
    {
        var result = new List<BackgroundProfile>();
        foreach (string guid in AssetDatabase.FindAssets("t:BackgroundProfile", new[] { ProfileDir }))
        {
            var profile = AssetDatabase.LoadAssetAtPath<BackgroundProfile>(
                AssetDatabase.GUIDToAssetPath(guid));
            if (profile != null) result.Add(profile);
        }
        result.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return result;
    }

    // Background: plain stretched sprite - no pixel reads needed
    private static void EnsureBackgroundImport(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return;

        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.textureType = TextureImporterType.Sprite;
        settings.spriteMode = (int)SpriteImportMode.Single;
        settings.spritePixelsPerUnit = 100;
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        settings.readable = false;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();
    }

    // Foreground: tile splitting reads pixels -> Read/Write REQUIRED;
    // bottom-left pivot so the strip anchors to the screen bottom
    private static void EnsureForegroundImport(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return;

        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.textureType = TextureImporterType.Sprite;
        settings.spriteMode = (int)SpriteImportMode.Single;
        settings.spritePixelsPerUnit = 100;
        settings.spriteAlignment = (int)SpriteAlignment.BottomLeft;
        settings.readable = true;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();
    }

    private static void RegisterProfilesInScene(List<BackgroundProfile> profiles)
    {
        var manager = Object.FindFirstObjectByType<BackgroundManager>(FindObjectsInactive.Include);
        if (manager == null)
        {
            Debug.LogWarning("[BackgroundProfileSync] No BackgroundManager in the open scene - " +
                             "open Scene01 and run the sync again.");
            return;
        }

        var serialized = new SerializedObject(manager);
        var array = serialized.FindProperty("profiles");
        array.arraySize = profiles.Count;
        for (int i = 0; i < profiles.Count; i++)
        {
            array.GetArrayElementAtIndex(i).objectReferenceValue = profiles[i];
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        EditorSceneManager.SaveScene(manager.gameObject.scene);
    }
}
