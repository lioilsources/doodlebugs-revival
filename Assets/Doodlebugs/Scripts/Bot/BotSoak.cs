#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Editor-only bot soak (Prompts/27 WP4 gates): combat bots fight round after
/// round with no human in the sky (the host's planes are parked in the
/// hangar), at a raised Time.timeScale - allowed here because the editor
/// host is the only simulation. Writes one CSV row per death to
/// Logs/bot-soak-*.csv and logs a summary against the PR gates on stop:
///  - ground + out-of-bounds deaths <= 15 % of a tier's deaths (Pilot and up)
///  - each tier out-kills the tier below in > 60 % of rounds
///  - Recover timeouts at most once per 10 simulated minutes
///  - combat brains < 0.3 ms per physics tick in total
/// Start/stop: Doodlebugs → Debug → Bot Soak.
/// </summary>
public class BotSoak : MonoBehaviour
{
    public static BotSoak Running { get; private set; }

    private byte[] _tiers;
    private float _timeScale;
    private float _startedAt;
    private int _round;
    private readonly StringBuilder _csv = new StringBuilder("sim_time,round,victim_tier,killer_tier,cause\n");

    private readonly int[] _kills = new int[BotTier.Count];
    private readonly int[] _deaths = new int[BotTier.Count];
    private readonly int[] _edgeDeaths = new int[BotTier.Count];   // ground + out of bounds
    private readonly int[] _roundWins = new int[BotTier.Count];
    private readonly int[,] _pair = new int[BotTier.Count, BotTier.Count]; // [killer, victim]
    private readonly int[] _beatsBelow = new int[BotTier.Count];   // rounds tier t out-killed t-1
    private readonly int[] _comparedBelow = new int[BotTier.Count];

    public static void Begin(byte[] tiers, float timeScale)
    {
        if (Running != null) return;
        var mm = MatchManager.Instance;
        if (mm == null || mm.Phase != MatchManager.GamePhase.WaitingForPlayers)
        {
            Debug.LogWarning("[BotSoak] Start from the host's Waiting lobby");
            return;
        }
        var go = new GameObject(nameof(BotSoak));
        Running = go.AddComponent<BotSoak>();
        Running._tiers = tiers;
        Running._timeScale = timeScale;
    }

    public static void End()
    {
        if (Running != null) Destroy(Running.gameObject);
    }

    private void Start()
    {
        BotBrain.RecoverTimeouts = 0;
        BotBrain.TickMillisTotal = 0;
        BotBrain.TickSamples = 0;
        _startedAt = Time.time;
        PlayerController.ServerDeathReported += OnDeath;
        MatchManager.Instance.OnMatchEnded += OnRoundEnded;
        Time.timeScale = _timeScale;
        MatchManager.Instance.ServerDebugStartBotBattle(_tiers);
        Debug.Log($"[BotSoak] Started: {_tiers.Length} bots, x{_timeScale}");
    }

    private void Update()
    {
        // No human in the sky: park the host's planes every time a round deploys them.
        foreach (var p in PlayerController.All)
        {
            if (!p.IsBot && !p.InHangar) p.NetInHangar.Value = true;
        }
        var mm = MatchManager.Instance;
        if (mm != null && mm.IsIntermission) mm.ServerSetClientReady(NetworkManager.ServerClientId);
    }

    private static int TierOf(ulong clientId, int localIdx)
    {
        if (!PlayerController.IsCombatBotIdentity(localIdx)) return -1;
        foreach (var p in PlayerController.All)
        {
            if (p.IsCombatBot && p.OwnerClientId == clientId && p.LocalPlayerIndex == localIdx) return p.NetBotTier.Value;
        }
        return -1;
    }

    private void OnDeath(PlayerController victim, ulong killerCid, int killerIdx, bool credited, KillCause cause)
    {
        if (!victim.IsCombatBot) return;
        int v = victim.NetBotTier.Value;
        int k = credited ? TierOf(killerCid, killerIdx) : -1;
        _deaths[v]++;
        if (cause == KillCause.Ground || cause == KillCause.OutOfBounds) _edgeDeaths[v]++;
        if (k >= 0)
        {
            _kills[k]++;
            _pair[k, v]++;
        }
        _csv.Append($"{Time.time - _startedAt:0.0},{_round},{v},{k},{cause}\n");
    }

    private void OnRoundEnded(MatchManager.MatchResult result)
    {
        _round++;
        int winnerTier = TierOf(result.WinnerClientId, result.WinnerLocalPlayerIndex);
        if (winnerTier >= 0) _roundWins[winnerTier]++;

        var killsThisRound = new int[BotTier.Count];
        var present = new bool[BotTier.Count];
        foreach (var entry in ScoreManager.Instance.AllStats)
        {
            ScoreManager.ParsePlayerId(entry.Key, out ulong cid, out int idx);
            int tier = TierOf(cid, idx);
            if (tier < 0) continue;
            killsThisRound[tier] += entry.Value.Kills;
            present[tier] = true;
        }
        for (int t = 1; t < BotTier.Count; t++)
        {
            if (!present[t] || !present[t - 1]) continue;
            _comparedBelow[t]++;
            if (killsThisRound[t] > killsThisRound[t - 1]) _beatsBelow[t]++;
        }
        Debug.Log($"[BotSoak] Round {_round}: winner tier {winnerTier}, kills by tier {string.Join("/", killsThisRound)}");
    }

    private void OnDestroy()
    {
        PlayerController.ServerDeathReported -= OnDeath;
        if (MatchManager.Instance != null) MatchManager.Instance.OnMatchEnded -= OnRoundEnded;
        Time.timeScale = 1f;
        if (Running == this) Running = null;
        Report();
    }

    private void Report()
    {
        float simMinutes = (Time.time - _startedAt) / 60f;
        var sb = new StringBuilder($"[BotSoak] {_round} rounds, {simMinutes:0.0} simulated minutes\n");
        for (int t = 0; t < BotTier.Count; t++)
        {
            if (_deaths[t] == 0 && _kills[t] == 0) continue;
            float edgePct = _deaths[t] > 0 ? 100f * _edgeDeaths[t] / _deaths[t] : 0f;
            string gate = t >= 1 ? (edgePct <= 15f ? "ok" : "FAIL") : "-";
            sb.Append($"  {BotTier.Get(t).Key,-6} kills {_kills[t],4}  deaths {_deaths[t],4}  ground+oob {edgePct,5:0.0}% [{gate}]  round wins {_roundWins[t]}\n");
        }
        for (int t = 1; t < BotTier.Count; t++)
        {
            if (_comparedBelow[t] == 0) continue;
            float pct = 100f * _beatsBelow[t] / _comparedBelow[t];
            sb.Append($"  {BotTier.Get(t).Key} out-kills {BotTier.Get(t - 1).Key} in {pct:0}% of {_comparedBelow[t]} rounds [{(pct > 60f ? "ok" : "FAIL")}]\n");
        }
        float perTen = simMinutes > 0f ? BotBrain.RecoverTimeouts / (simMinutes / 10f) : 0f;
        sb.Append($"  recover timeouts {BotBrain.RecoverTimeouts} ({perTen:0.00} per 10 min) [{(perTen <= 1f ? "ok" : "FAIL")}]\n");
        int bots = Mathf.Max(1, _tiers.Length);
        double perPhysicsTick = BotBrain.TickSamples > 0 ? BotBrain.TickMillisTotal / (BotBrain.TickSamples / (double)bots) : 0;
        sb.Append($"  brains {perPhysicsTick:0.000} ms per physics tick for {bots} bots [{(perPhysicsTick < 0.3 ? "ok" : "FAIL")}]");
        Debug.Log(sb.ToString());

        try
        {
            Directory.CreateDirectory("Logs");
            string path = Path.Combine("Logs", $"bot-soak-{System.DateTime.Now:yyyyMMdd-HHmmss}.csv");
            File.WriteAllText(path, _csv.ToString());
            Debug.Log($"[BotSoak] Deaths written to {path}");
        }
        catch (System.Exception e) { Debug.LogWarning($"[BotSoak] CSV write failed: {e.Message}"); }
    }
}
#endif
