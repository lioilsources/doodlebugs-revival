using UnityEditor;
using UnityEngine;

/// <summary>
/// Play-mode shortcuts for the combat-bot plumbing (Prompts/27 WP3): from the
/// host's Waiting lobby, put N combat bots in the sky and start a battle.
/// </summary>
public static class BotDebugMenu
{
    private const string Root = "Doodlebugs/Debug/";

    [MenuItem(Root + "Bot Battle: 2 Rookies")]
    private static void TwoRookies() => Start(2, 0);

    [MenuItem(Root + "Bot Battle: 2 Pilots")]
    private static void TwoPilots() => Start(2, 1);

    [MenuItem(Root + "Bot Battle: 3 Aces")]
    private static void ThreeAces() => Start(3, 2);

    [MenuItem(Root + "Remove Combat Bots")]
    private static void Remove() => BotManager.Instance?.ServerDespawnCombat();

    [MenuItem(Root + "Bot Battle: 2 Rookies", true)]
    [MenuItem(Root + "Bot Battle: 2 Pilots", true)]
    [MenuItem(Root + "Bot Battle: 3 Aces", true)]
    [MenuItem(Root + "Remove Combat Bots", true)]
    private static bool CanRun() =>
        Application.isPlaying && MatchManager.Instance != null &&
        Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsServer;

    private static void Start(int count, byte tier)
    {
        var mm = MatchManager.Instance;
        if (mm.Phase != MatchManager.GamePhase.WaitingForPlayers)
        {
            Debug.LogWarning("[BotDebug] Only from the Waiting lobby - adjusting the roster only");
            BotManager.Instance?.ServerSetCombatRoster(count, tier);
            return;
        }
        mm.ServerDebugStartBotBattle(count, tier);
    }
}
