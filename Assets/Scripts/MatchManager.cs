using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Which color zone the host pressed "Play" in.
public enum MatchMode : byte
{
    Players = 0,   // everybody who is connected fights each other
    Bots = 1       // the host picks how many bots join the fight
}

// Runs a fight from start to finish. The host spawns one of these; it is a network object so every client
// sees the same phase, countdown and winner. Only the host changes anything.
//
//   Lobby -> (host presses Play) -> Ready -> (everybody is ready) -> Countdown -> Fight -> Result -> Lobby
//
// Everybody is teleported to the ring for Ready (in Bots mode the host adds bots there); in Fight the last
// fighter standing wins.
public class MatchManager : NetworkBehaviour
{
    public enum Phase : byte { Lobby, Ready, Countdown, Fight, Result }

    public static MatchManager Instance { get; private set; }

    public const int MaxBots = 5;
    const float CountdownSeconds = 3f;
    const float ReadyGraceSeconds = 2.5f;   // ignore "ready" while everybody is still being moved to the ring
    const float ResultSeconds = 6f;
    const float BotRingRadius = 8f;

    [SerializeField] GameObject botPrefab;

    readonly NetworkVariable<byte> phase = new NetworkVariable<byte>(
        (byte)Phase.Lobby, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    readonly NetworkVariable<byte> mode = new NetworkVariable<byte>(
        (byte)MatchMode.Players, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    readonly NetworkVariable<int> botCount = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    readonly NetworkVariable<double> phaseEndTime = new NetworkVariable<double>(
        0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    readonly NetworkVariable<long> winnerObjectId = new NetworkVariable<long>(
        -1L, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    readonly NetworkVariable<int> startedWith = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    readonly List<PlayerController> bots = new List<PlayerController>();   // host only
    readonly List<ulong> eliminationOrder = new List<ulong>();              // host only: NetworkObjectIds, in the order fighters went down
    float phaseStartedAt;                                                    // host only, real time

    public Phase CurrentPhase => (Phase)phase.Value;
    public MatchMode Mode => (MatchMode)mode.Value;
    public int BotCount => botCount.Value;
    public double PhaseEndTime => phaseEndTime.Value;
    public long WinnerObjectId => winnerObjectId.Value;   // network object id of the winner, -1 for a draw
    public int StartedWith => startedWith.Value;          // how many fighters the fight began with
    public bool IsFighting => CurrentPhase == Phase.Fight;

    // Seconds left of the countdown / result screen.
    public float SecondsLeft => Mathf.Max(0f, (float)(phaseEndTime.Value - NetworkManager.ServerTime.Time));

    // Fighters may only move, jump and punch while the fight is on (and only while alive).
    // Everybody outside a match is free.
    public bool CanAct(PlayerController player) =>
        !player.InMatch || (CurrentPhase == Phase.Fight && player.Health > 0);

    public override void OnNetworkSpawn() => Instance = this;

    public override void OnNetworkDespawn()
    {
        if (Instance == this) Instance = null;
    }

    // ---- host: control ------------------------------------------------------------------------

    // Only the host (or the solo player, who is the host) may start a match.
    [Rpc(SendTo.Server)]
    public void StartMatchRpc(byte matchMode, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId) return;
        if (CurrentPhase != Phase.Lobby) return;

        var players = ActivePlayers(false, false);
        if (players.Count == 0) return;
        players.Sort((a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));

        for (int i = 0; i < players.Count; i++)
        {
            Vector3 spawn = GameLayout.RingSpawn(i, players.Count);
            players[i].ServerJoinMatch(spawn, GameLayout.YawTowardsRingCenter(spawn));
        }

        mode.Value = matchMode == (byte)MatchMode.Bots ? (byte)MatchMode.Bots : (byte)MatchMode.Players;
        startedWith.Value = players.Count;
        winnerObjectId.Value = -1L;
        botCount.Value = 0;
        eliminationOrder.Clear();

        // Against bots there is always at least one; the host can change the number in the ready menu.
        if (Mode == MatchMode.Bots) SetBotCount(1);

        phaseStartedAt = Time.unscaledTime;
        phase.Value = (byte)Phase.Ready;
    }

    // The host picks how many bots fight (1..MaxBots) while everybody is getting ready.
    [Rpc(SendTo.Server)]
    public void SetBotCountRpc(int count, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId) return;
        if (CurrentPhase != Phase.Ready || Mode != MatchMode.Bots) return;
        SetBotCount(count);
    }

    // The host can call the match off at any time (this is also how a solo training session ends).
    [Rpc(SendTo.Server)]
    public void EndMatchRpc(RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId) return;
        if (CurrentPhase == Phase.Lobby) return;
        ReturnToLobby();
    }

    // ---- host: bots ---------------------------------------------------------------------------

    void SetBotCount(int wanted)
    {
        wanted = Mathf.Clamp(wanted, 1, MaxBots);
        while (bots.Count < wanted) SpawnBot();
        while (bots.Count > wanted) RemoveLastBot();
        botCount.Value = bots.Count;
        startedWith.Value = ActivePlayers(true).Count;
    }

    void SpawnBot()
    {
        if (botPrefab == null) return;

        // Bots stand on an outer circle, spread over MaxBots slots, facing the middle of the ring.
        int index = bots.Count;
        float angle = (index / (float)MaxBots) * Mathf.PI * 2f + Mathf.PI / MaxBots;
        Vector3 position = GameLayout.RingCenter + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * BotRingRadius + Vector3.up * 0.1f;
        var rotation = Quaternion.Euler(0f, GameLayout.YawTowardsRingCenter(position), 0f);

        var go = Instantiate(botPrefab, position, rotation);
        var bot = go.GetComponent<PlayerController>();
        go.GetComponent<NetworkObject>().Spawn(true);
        // Network variables can only be written once the object is spawned. Nothing runs in between (same
        // frame), so the bot never acts before it is in the match and marked ready.
        bot.ServerInitBot(index + 1);
        bots.Add(bot);
    }

    void RemoveLastBot()
    {
        var bot = bots[bots.Count - 1];
        bots.RemoveAt(bots.Count - 1);
        if (bot != null && bot.IsSpawned) bot.NetworkObject.Despawn(true);
    }

    void DespawnAllBots()
    {
        foreach (var bot in bots)
        {
            if (bot != null && bot.IsSpawned) bot.NetworkObject.Despawn(true);
        }
        bots.Clear();
        botCount.Value = 0;
    }

    // ---- host: the state machine --------------------------------------------------------------

    void Update()
    {
        if (!IsServer || !IsSpawned || CurrentPhase == Phase.Lobby) return;

        var fighters = ActivePlayers(true);
        double now = NetworkManager.ServerTime.Time;

        switch (CurrentPhase)
        {
            case Phase.Ready:
                if (HumanCount(fighters) == 0) { ReturnToLobby(); return; }
                if (Time.unscaledTime - phaseStartedAt < ReadyGraceSeconds) return;
                if (AllReady(fighters))
                {
                    startedWith.Value = fighters.Count;   // bots may have been added or removed since the start
                    phaseEndTime.Value = now + CountdownSeconds;
                    phase.Value = (byte)Phase.Countdown;
                }
                break;

            case Phase.Countdown:
                if (HumanCount(fighters) == 0) { ReturnToLobby(); return; }
                if (now >= phaseEndTime.Value) phase.Value = (byte)Phase.Fight;
                break;

            case Phase.Fight:
                if (HumanCount(fighters) == 0) { ReturnToLobby(); return; }
                // With several fighters the last one standing wins. A single fighter is a training session:
                // nobody to beat, so it goes on until the host ends it.
                if (startedWith.Value >= 2)
                {
                    PlayerController lastAlive = null;
                    int alive = 0;
                    foreach (var f in fighters)
                    {
                        if (f.Health > 0) { alive++; lastAlive = f; }
                        // Remember the order fighters go down in, so the podium can seat 2nd and 3rd place too.
                        else if (!eliminationOrder.Contains(f.NetworkObjectId)) eliminationOrder.Add(f.NetworkObjectId);
                    }
                    if (alive <= 1)
                    {
                        winnerObjectId.Value = alive == 1 ? (long)lastAlive.NetworkObjectId : -1L;
                        phaseEndTime.Value = now + ResultSeconds;
                        phase.Value = (byte)Phase.Result;
                        PlacePodium(fighters, lastAlive);
                    }
                }
                break;

            case Phase.Result:
                if (now >= phaseEndTime.Value) ReturnToLobby();
                break;
        }
    }

    // Sends the winner (and whoever went down last, and the one before that) to stand on the podium behind the
    // zone the match was started from, while the result screen is up. A draw leaves the podium empty.
    void PlacePodium(List<PlayerController> fighters, PlayerController winner)
    {
        if (winner == null) return;

        var ranking = new List<PlayerController> { winner };
        for (int i = eliminationOrder.Count - 1; i >= 0 && ranking.Count < 3; i--)
        {
            foreach (var f in fighters)
            {
                if (f.NetworkObjectId == eliminationOrder[i] && !ranking.Contains(f)) { ranking.Add(f); break; }
            }
        }

        Vector3 center = Mode == MatchMode.Bots ? GameLayout.BotsPodiumCenter : GameLayout.PlayersPodiumCenter;
        for (int place = 0; place < ranking.Count; place++)
        {
            GameLayout.PodiumSpot(center, place, out Vector3 position, out float yaw);
            ranking[place].ServerPlacePodium(position, yaw);
        }
    }

    void ReturnToLobby()
    {
        DespawnAllBots();
        eliminationOrder.Clear();

        foreach (var player in ActivePlayers(true, false))
            player.ServerLeaveMatch(PlayerController.GetSpawnPosition(player.OwnerClientId), 0f);

        winnerObjectId.Value = -1L;
        startedWith.Value = 0;
        mode.Value = (byte)MatchMode.Players;
        phase.Value = (byte)Phase.Lobby;
    }

    static bool AllReady(List<PlayerController> fighters)
    {
        foreach (var f in fighters)
        {
            if (!f.IsReady) return false;
        }
        return true;
    }

    static int HumanCount(List<PlayerController> fighters)
    {
        int humans = 0;
        foreach (var f in fighters)
        {
            if (!f.IsBot) humans++;
        }
        return humans;
    }

    // All spawned players, or only those taking part in the match. Bots are included unless told otherwise.
    public static List<PlayerController> ActivePlayers(bool onlyInMatch, bool includeBots = true)
    {
        var list = new List<PlayerController>();
        foreach (var p in FindObjectsByType<PlayerController>())
        {
            if (!p.IsSpawned) continue;
            if (onlyInMatch && !p.InMatch) continue;
            if (!includeBots && p.IsBot) continue;
            list.Add(p);
        }
        return list;
    }
}
