using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

// Owns the network session: start solo / host / join, and leave back to the main menu.
// Solo play is simply a host that only listens on localhost, so there is a single code path.
[RequireComponent(typeof(NetworkManager), typeof(UnityTransport))]
public class NetworkGame : MonoBehaviour
{
    public static NetworkGame Instance { get; private set; }
    public static bool InGame { get; private set; }
    public static bool IsMultiplayer { get; private set; }

    [SerializeField] GameObject playerPrefab;
    [SerializeField] GameObject matchManagerPrefab;
    [SerializeField] int maxPlayers = 8;

    public bool Busy { get; private set; }
    public event Action EnteredGame;
    public event Action<string> LeftGame;
    public event Action<string> ConnectFailed;

    NetworkManager networkManager;
    UnityTransport transport;
    LanAnnouncer announcer;
    bool leaving;
    string joinAddress;
    int hostPort = LanProtocol.GamePort;

    void Awake()
    {
        Instance = this;
        InGame = false;
        IsMultiplayer = false;

        networkManager = GetComponent<NetworkManager>();
        transport = GetComponent<UnityTransport>();

        // Give up on unreachable hosts after a few seconds instead of the default minute.
        transport.MaxConnectAttempts = 4;
        transport.ConnectTimeoutMS = 1000;

        // Netcode registers the player prefab itself when the session starts.
        networkManager.NetworkConfig.PlayerPrefab = playerPrefab;

        networkManager.OnClientConnectedCallback += OnClientConnected;
        networkManager.OnClientDisconnectCallback += OnClientDisconnected;

        announcer = gameObject.AddComponent<LanAnnouncer>();
        announcer.enabled = false;
    }

    void OnDestroy()
    {
        if (networkManager != null)
        {
            networkManager.OnClientConnectedCallback -= OnClientConnected;
            networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }
        if (Instance == this) Instance = null;
        InGame = false;
        IsMultiplayer = false;
    }

    // Single player: nobody else can connect.
    public void StartSolo() => StartHosting(false);

    // Host a game for friends: listen on all adapters (incl. Radmin) and announce on the LAN.
    public void StartHostForFriends() => StartHosting(true);

    // IPAddress.TryParse accepts short forms like "26.133.121", which the transport then rejects.
    static bool IsValidIpv4(string text)
    {
        var parts = text.Split('.');
        if (parts.Length != 4) return false;
        foreach (var part in parts)
        {
            if (part.Length == 0 || part.Length > 3 || !int.TryParse(part, out int value) || value < 0 || value > 255)
                return false;
        }
        return true;
    }

    // First free UDP port in [first, first + count), or -1. Lets several copies of the game run on one PC
    // (editor + build) - each host just takes the next port and announces it.
    static int FindFreeUdpPort(int first, int count)
    {
        for (int port = first; port < first + count; port++)
        {
            // Netcode binds to one specific address (127.0.0.1 solo, 0.0.0.0 for friends) and Windows treats
            // "all addresses" and "loopback only" as different bindings, so the port must be free on both.
            if (CanBind(IPAddress.Loopback, port) && CanBind(IPAddress.Any, port))
                return port;
        }
        return -1;
    }

    static bool CanBind(IPAddress address, int port)
    {
        try
        {
            using (new UdpClient(new IPEndPoint(address, port))) { }
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    void StartHosting(bool forFriends)
    {
        if (Busy || leaving || InGame) return;

        // Friends get the familiar port when it's free; a solo game just needs any free port.
        int port = forFriends ? FindFreeUdpPort(LanProtocol.GamePort, 10) : FindFreeUdpPort(47800, 50);
        if (port < 0)
        {
            ConnectFailed?.Invoke("Не удалось найти свободный сетевой порт. Закройте лишние копии игры и попробуйте снова.");
            return;
        }

        Busy = true;
        IsMultiplayer = forFriends;
        hostPort = port;

        transport.SetConnectionData("127.0.0.1", (ushort)port, forFriends ? "0.0.0.0" : "127.0.0.1");

        if (!networkManager.StartHost())
        {
            networkManager.Shutdown();
            Busy = false;
            IsMultiplayer = false;
            ConnectFailed?.Invoke($"Не удалось создать игру на порту {port}.");
            return;
        }

        // The host owns the slimes and the match; they appear for everyone who joins.
        if (MobSpawner.Instance != null) MobSpawner.Instance.OnHostStarted();
        if (matchManagerPrefab != null)
            Instantiate(matchManagerPrefab).GetComponent<NetworkObject>().Spawn(true);

        if (forFriends)
        {
            announcer.ServerName = "Игра " + Environment.UserName;
            announcer.GamePort = hostPort;
            announcer.MaxPlayers = maxPlayers;
            announcer.PlayerCount = () => networkManager.ConnectedClientsIds.Count;
            announcer.enabled = true;
        }

        EnterGame();
    }

    public void Join(string address, int port)
    {
        if (Busy || leaving || InGame) return;

        if (!IsValidIpv4(address))
        {
            ConnectFailed?.Invoke($"«{address}» — неполный или неверный адрес. Нужны четыре числа через точку, например 26.12.34.56");
            return;
        }

        Busy = true;
        IsMultiplayer = true;
        joinAddress = address;
        transport.SetConnectionData(address, (ushort)port);

        if (!networkManager.StartClient())
        {
            networkManager.Shutdown();
            Busy = false;
            IsMultiplayer = false;
            ConnectFailed?.Invoke("Не удалось начать подключение.");
        }
    }

    void OnClientConnected(ulong clientId)
    {
        if (networkManager.IsServer)
        {
            // Host: turn away extra players once the game is full.
            if (clientId != networkManager.LocalClientId && networkManager.ConnectedClientsIds.Count > maxPlayers)
                networkManager.DisconnectClient(clientId, "Игра заполнена");
            return;
        }

        if (clientId == networkManager.LocalClientId && Busy && !InGame)
            EnterGame();
    }

    void OnClientDisconnected(ulong clientId)
    {
        // On the host this fires for every player who leaves - that's fine, nothing to do.
        if (networkManager.IsServer || leaving) return;

        string reason = networkManager.DisconnectReason;
        if (InGame)
        {
            LeaveToMenu(string.IsNullOrEmpty(reason) ? "Соединение с игрой потеряно." : reason);
        }
        else
        {
            networkManager.Shutdown();
            Busy = false;
            IsMultiplayer = false;
            ConnectFailed?.Invoke(string.IsNullOrEmpty(reason)
                ? $"Не удалось подключиться к {joinAddress}. Проверьте, что игра запущена, адрес верный и брандмауэр Windows разрешает игру."
                : reason);
        }
    }

    void EnterGame()
    {
        Busy = false;
        InGame = true;
        PauseMenu.SetCursorCaptured(true);
        EnteredGame?.Invoke();
    }

    public void LeaveToMenu(string message = null)
    {
        if (leaving) return;
        StartCoroutine(LeaveRoutine(message));
    }

    IEnumerator LeaveRoutine(string message)
    {
        leaving = true;
        InGame = false;
        announcer.enabled = false;
        if (MobSpawner.Instance != null) MobSpawner.Instance.StopAll();
        if (PauseMenu.Instance != null) PauseMenu.Instance.ForceClose();
        PauseMenu.SetCursorCaptured(false);

        networkManager.Shutdown();
        while (networkManager.ShutdownInProgress || networkManager.IsListening)
            yield return null;

        Busy = false;
        IsMultiplayer = false;
        leaving = false;
        LeftGame?.Invoke(message);
    }

    // Text for the HUD / pause menu: how many players and which address friends should type.
    public string DescribeSession()
    {
        var sb = new StringBuilder();
        int humans = 0;
        foreach (var p in UnityEngine.Object.FindObjectsByType<PlayerController>())
        {
            if (!p.IsBot) humans++;
        }
        sb.Append("Игроков: ").Append(humans);

        if (IsMultiplayer && networkManager.IsServer)
        {
            var addresses = LanProtocol.GetLocalAddresses();
            if (addresses.Count > 0)
            {
                sb.Append("   Адрес для друзей: ");
                for (int i = 0; i < addresses.Count; i++)
                {
                    if (i > 0) sb.Append(",  ");
                    sb.Append(addresses[i].Address);
                    // Friends must type the port too when it isn't the default one.
                    if (hostPort != LanProtocol.GamePort) sb.Append(':').Append(hostPort);
                }
            }
        }
        return sb.ToString();
    }
}
