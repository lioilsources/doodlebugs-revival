#if DOODLEBUGS_STORESHOTS
using System.Collections;
using System.IO;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Store screenshots without hands (Prompts/34). Compiled only into the iOS
/// Simulator build from BuildScript.BuildIOSStoreShots, never into a player
/// that ships. It walks the boot screen, PRACTICE, every hangar tab, then
/// flies the player's plane with a BotBrain against the warm-up bot and
/// saves PNGs at native resolution into Application.persistentDataPath
/// (the app's Documents folder). tools/store/shoot_simulator.sh pulls them.
///
/// Private hangar methods are reached by reflection on purpose: the shipping
/// code gets no hooks for a tool that is not in it.
/// </summary>
public class StoreShotDirector : MonoBehaviour
{
    private const float FightSeconds = 90f;
    private const float FightInterval = 1.5f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        var go = new GameObject("StoreShotDirector");
        DontDestroyOnLoad(go);
        go.AddComponent<StoreShotDirector>();
    }

    private string _dir;

    private IEnumerator Start()
    {
        _dir = Path.Combine(Application.persistentDataPath, "storeshots");
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        Directory.CreateDirectory(_dir);
        Log($"saving to {_dir}");

        yield return Until(() => HangarScreen.Instance != null, 20f);
        yield return Wait(5f);
        yield return Shot("01_boot");

        var cm = Doodlebugs.Network.ConnectionManager.Instance;
        // Home no longer pairs at boot: PARTY starts the search, HostNow ends it
        // at once - the Waiting hangar with every tab, as before.
        cm?.BeginParty();
        yield return null;
        if (cm == null || !cm.HostNow()) Log("HostNow refused, waiting for discovery to host on its own");
        yield return Until(() => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer
                                 && Mode() == MatchManager.HangarMode.Waiting, 40f);
        yield return Wait(3f);
        yield return Shot("02_weapon");

        foreach (var tab in new[] { "Gadget", "Upgrades", "Plane" })
        {
            SwitchTab(tab);
            yield return Wait(2f);
            yield return Shot("03_" + tab.ToLowerInvariant());
        }
        SwitchTab("Weapon");
        yield return Wait(1f);

        Invoke(HangarScreen.Instance, "Fly");
        yield return Until(() => HangarScreen.OwnedPlane() != null, 10f);
        var pc = HangarScreen.OwnedPlane();
        if (pc != null)
        {
            var brain = pc.gameObject.AddComponent<BotBrain>();
            brain.Init(pc);
            pc.SetInputOverride(brain);
        }
        yield return Wait(4f);

        int n = 0;
        for (float t = 0f; t < FightSeconds; t += FightInterval)
        {
            yield return Shot($"10_fight_{n++:00}");
            yield return Wait(FightInterval);
        }

        File.WriteAllText(Path.Combine(_dir, "done.txt"), n.ToString());
        Log("done");
    }

    // --- helpers -----------------------------------------------------------

    private IEnumerator Shot(string name)
    {
        yield return new WaitForEndOfFrame();
        // ReadPixels at the end of the frame, not ScreenCapture: the project
        // has the ScreenCapture module disabled.
        var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
        tex.Apply();
        File.WriteAllBytes(Path.Combine(_dir, name + ".png"), tex.EncodeToPNG());
        Destroy(tex);
        Log($"{name} {Screen.width}x{Screen.height}");
    }

    private static IEnumerator Wait(float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
    }

    private static IEnumerator Until(System.Func<bool> done, float timeout)
    {
        float end = Time.realtimeSinceStartup + timeout;
        while (!done() && Time.realtimeSinceStartup < end) yield return null;
        if (!done()) Log("timed out waiting, carrying on");
    }

    private static MatchManager.HangarMode Mode()
    {
        var h = HangarScreen.Instance;
        if (h == null) return MatchManager.HangarMode.Searching;
        var f = typeof(HangarScreen).GetField("_mode", BindingFlags.NonPublic | BindingFlags.Instance);
        return f != null ? (MatchManager.HangarMode)f.GetValue(h) : MatchManager.HangarMode.Searching;
    }

    private static void SwitchTab(string tab)
    {
        var h = HangarScreen.Instance;
        if (h == null) return;
        var tabType = typeof(HangarScreen).GetNestedType("Tab", BindingFlags.NonPublic);
        var m = typeof(HangarScreen).GetMethod("SwitchTab", BindingFlags.NonPublic | BindingFlags.Instance,
            null, new[] { tabType, typeof(bool) }, null);
        if (tabType == null || m == null) { Log("SwitchTab not found"); return; }
        m.Invoke(h, new[] { System.Enum.Parse(tabType, tab), (object)true });
    }

    private static void Invoke(object target, string method)
    {
        var m = target?.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance,
            null, System.Type.EmptyTypes, null);
        if (m == null) { Log($"{method} not found"); return; }
        m.Invoke(target, null);
    }

    private static void Log(string msg) => Debug.Log($"[StoreShots] {msg}");
}
#endif
