#if DOODLEBUGS_STORESHOTS
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Store screenshots without hands (Prompts/34). Compiled only into the iOS
/// Simulator build from BuildScript.BuildIOSStoreShots, never into a player
/// that ships. It walks the 2.14 success path a player sees: Home (PLAY /
/// PARTY), PLAY into a solo run against bots, the intermission hangar (every
/// tab, the PLANE tab's paint shop scrolled to each BUY card), more rounds and
/// the podium. The player's plane flies on a combat BotBrain. PNGs at native
/// resolution land in Application.persistentDataPath/storeshots (the app's
/// Documents folder); tools/store/shoot_simulator.sh pulls them.
///
/// Launch environment (simctl passes SIMCTL_CHILD_X as X):
///   STORESHOTS_VIDEO=1   no PNGs (they stall a frame); a slower hangar tour
///                        with a pan across the paint shop, for a recording.
/// Every step is appended to storeshots/timeline.txt (UTC ms + label) so a
/// recording can be cut without guessing. UI language: launch with
/// -AppleLanguages "(cs)".
///
/// Private hangar methods are reached by reflection on purpose: the shipping
/// code gets no hooks for a tool that is not in it.
/// </summary>
public class StoreShotDirector : MonoBehaviour
{
    private const float FightInterval = 2f;
    private const float GiveUpSeconds = 30f * 60f;

    // Pack -> the paint the hero wears while that pack's BUY card is on screen
    private static readonly (string id, int skin)[] PackShots =
    {
        ("skins_all_packs", 37),     // Dragon Scale
        ("skins_camo_pack", 12),     // Jungle Camo
        ("skins_metallic_pack", 22), // Gold Leaf
        ("skins_cosmic_pack", 38),   // Phoenix Flame
        ("skins_homage_pack", 48),   // Retro Wave
    };
    private const int FightSkin = 37;
    private const int FightModel = 2;   // Racer

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        var go = new GameObject("StoreShotDirector");
        DontDestroyOnLoad(go);
        go.AddComponent<StoreShotDirector>();
    }

    private string _dir;
    private bool _video;
    private float _deathAt = -1f;
    private bool _runEnded;
    private int _round = 1;

    private IEnumerator Start()
    {
        _video = Environment.GetEnvironmentVariable("STORESHOTS_VIDEO") == "1";
        _dir = Path.Combine(Application.persistentDataPath, "storeshots");
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        Directory.CreateDirectory(_dir);
        Log($"saving to {_dir} (video {_video}, language {Loc.Language})");

        yield return Until(() => HangarScreen.Instance != null, 20f);
        yield return Wait(_video ? 3f : 5f);
        Mark("home");
        yield return Shot("01_home");
        if (_video) yield return Wait(2.5f);

        PlayerController.ServerDeathReported += OnDeath;
        if (MatchManager.Instance != null) MatchManager.Instance.OnRunEnded += OnRunEnded;

        Mark("play");
        Invoke(HangarScreen.Instance, "PressPlay");

        bool toured = false, inHangar = false;
        float nextFight = 0f;
        int n = 0;
        float giveUp = Time.realtimeSinceStartup + GiveUpSeconds;
        while (!_runEnded && Time.realtimeSinceStartup < giveUp)
        {
            Pilot();
            var mm = MatchManager.Instance;
            bool open = IsOpen();

            if (open && Mode() == MatchManager.HangarMode.Intermission)
            {
                if (!inHangar)
                {
                    inHangar = true;
                    Mark($"hangar_{_round}");
                    yield return toured ? Wait(_video ? 2f : 3f) : Tour();
                    toured = true;
                    Mark($"ready_{_round}");
                    Invoke(HangarScreen.Instance, "PressCta");
                }
            }
            else if (inHangar && !open)
            {
                inHangar = false;
                _round++;
                Mark($"fight_{_round}");
            }

            if (!_video && mm != null && mm.Phase == MatchManager.GamePhase.Battle && !open)
            {
                if (_deathAt > 0f)
                {
                    float at = _deathAt;
                    _deathAt = -1f;
                    int k = n++;
                    foreach (float d in new[] { 0.08f, 0.25f, 0.5f, 0.9f })
                    {
                        float wait = at + d - Time.realtimeSinceStartup;
                        if (wait > 0f) yield return Wait(wait);
                        yield return Shot($"20_boom_r{_round}_{k:00}_{Mathf.RoundToInt(d * 100):000}");
                    }
                }
                else if (Time.realtimeSinceStartup >= nextFight)
                {
                    nextFight = Time.realtimeSinceStartup + FightInterval;
                    yield return Shot($"10_fight_r{_round}_{n++:00}");
                }
            }
            else _deathAt = -1f;
            yield return null;
        }

        Mark("podium");
        yield return Wait(1.5f);
        yield return Shot("30_podium_a");
        yield return Wait(2.5f);
        yield return Shot("30_podium_b");
        yield return Wait(_video ? 6f : 1f);
        Mark("end");

        PlayerController.ServerDeathReported -= OnDeath;
        File.WriteAllText(Path.Combine(_dir, "done.txt"), n.ToString());
        Log("done");
    }

    /// <summary>The first intermission: every tab, then the paint shop at each BUY card.</summary>
    private IEnumerator Tour()
    {
        float dwell = _video ? 3.2f : 2f;
        yield return Wait(2.5f);
        Mark("tab_weapon");
        yield return Shot("02_weapon");
        if (_video) yield return Wait(1.5f);

        foreach (var tab in new[] { "Gadget", "Upgrades" })
        {
            if (_video && tab == "Upgrades") continue;
            SwitchTab(tab);
            Mark("tab_" + tab.ToLowerInvariant());
            yield return Wait(dwell);
            yield return Shot("03_" + tab.ToLowerInvariant());
        }

        SwitchTab("Plane");
        Mark("tab_plane");
        PlaneTab.SendPick(FightModel, true);
        yield return Wait(dwell);
        yield return Shot("04_plane");

        if (_video)
        {
            // One slow pan from the free paints across every BUY card
            PlaneTab.SendPick(FightSkin, false);
            yield return Wait(1.2f);
            var all = GameObject.Find("Buy_skins_all_packs");
            var (scroll, content) = StripOf(all);
            if (content != null)
            {
                float target = LeftEdge(all) - 12f;
                float view = ((RectTransform)scroll.transform).rect.width;
                float end = Mathf.Max(target, content.rect.width - view);
                Mark("paint_pan");
                yield return Pan(scroll, content, 0f, target, 1.6f);
                yield return Wait(1.6f);
                yield return Pan(scroll, content, target, end, 5f);
                yield return Wait(1.2f);
                yield return Pan(scroll, content, end, target, 1.5f);
                yield return Wait(1.5f);
            }
            yield break;
        }

        foreach (var (id, skin) in PackShots)
        {
            PlaneTab.SendPick(skin, false);
            yield return Wait(0.9f);
            var card = GameObject.Find("Buy_" + id);
            var (scroll, content) = StripOf(card);
            if (content != null)
            {
                scroll.StopMovement();
                content.anchoredPosition = new Vector2(-(LeftEdge(card) - 12f), content.anchoredPosition.y);
            }
            else Log($"no BUY card for {id}");
            yield return Wait(1.4f);
            yield return Shot("05_iap_" + id);
        }

        PlaneTab.SendPick(FightSkin, false);
        yield return Wait(1f);
        SwitchTab("Weapon");
        yield return Wait(1.5f);
    }

    // --- fight helpers -------------------------------------------------------

    private static void Pilot()
    {
        var pc = HangarScreen.OwnedPlane();
        if (pc == null || pc.GetComponent<BotBrain>() != null) return;
        var brain = pc.gameObject.AddComponent<BotBrain>();
        brain.Init(pc, true);
        pc.SetInputOverride(brain);
    }

    private void OnDeath(PlayerController victim, ulong killer, int killerIdx, bool credited, KillCause cause)
    {
        if (_deathAt < 0f) _deathAt = Time.realtimeSinceStartup;
    }

    private void OnRunEnded(ulong _) => _runEnded = true;

    // --- paint strip ----------------------------------------------------------

    private static (ScrollRect, RectTransform) StripOf(GameObject card)
    {
        if (card == null) return (null, null);
        var content = card.transform.parent as RectTransform;
        var scroll = content != null ? content.GetComponentInParent<ScrollRect>() : null;
        if (scroll == null) return (null, null);
        Canvas.ForceUpdateCanvases();
        return (scroll, content);
    }

    private static float LeftEdge(GameObject card)
    {
        var r = (RectTransform)card.transform;
        return r.localPosition.x - r.rect.width * r.pivot.x;
    }

    private static IEnumerator Pan(ScrollRect scroll, RectTransform content, float from, float to, float seconds)
    {
        scroll.StopMovement();
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / seconds);
            content.anchoredPosition = new Vector2(-Mathf.Lerp(from, to, k), content.anchoredPosition.y);
            yield return null;
        }
        content.anchoredPosition = new Vector2(-to, content.anchoredPosition.y);
    }

    // --- helpers -----------------------------------------------------------

    private IEnumerator Shot(string name)
    {
        if (_video) yield break;
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

    private void Mark(string label)
    {
        long ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        File.AppendAllText(Path.Combine(_dir, "timeline.txt"), $"{ms} {label}\n");
        Log("mark " + label);
    }

    private static IEnumerator Wait(float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
    }

    private static IEnumerator Until(Func<bool> done, float timeout)
    {
        float end = Time.realtimeSinceStartup + timeout;
        while (!done() && Time.realtimeSinceStartup < end) yield return null;
        if (!done()) Log("timed out waiting, carrying on");
    }

    private static T Field<T>(string name, T fallback)
    {
        var h = HangarScreen.Instance;
        var f = typeof(HangarScreen).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        return h != null && f != null ? (T)f.GetValue(h) : fallback;
    }

    private static MatchManager.HangarMode Mode() => Field("_mode", MatchManager.HangarMode.Searching);

    private static bool IsOpen() => Field("_open", false);

    private static void SwitchTab(string tab)
    {
        var h = HangarScreen.Instance;
        if (h == null) return;
        var tabType = typeof(HangarScreen).GetNestedType("Tab", BindingFlags.NonPublic);
        var m = typeof(HangarScreen).GetMethod("SwitchTab", BindingFlags.NonPublic | BindingFlags.Instance,
            null, new[] { tabType, typeof(bool) }, null);
        if (tabType == null || m == null) { Log("SwitchTab not found"); return; }
        m.Invoke(h, new[] { Enum.Parse(tabType, tab), (object)true });
    }

    private static void Invoke(object target, string method)
    {
        var m = target?.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance,
            null, Type.EmptyTypes, null);
        if (m == null) { Log($"{method} not found"); return; }
        m.Invoke(target, null);
    }

    private static void Log(string msg) => Debug.Log($"[StoreShots] {msg}");
}
#endif
