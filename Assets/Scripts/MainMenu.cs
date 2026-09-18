using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Start screen: play solo, host a game for friends, or browse/join games on the network.
// Built in code, like the pause menu.
public class MainMenu : MonoBehaviour
{
    const int MaxServerRows = 5;

    GameObject menuRoot;
    RectTransform mainPanel;
    RectTransform joinPanel;
    CanvasGroup mainGroup;
    CanvasGroup joinGroup;
    Text mainMessage;
    Text joinStatus;
    RectTransform serverList;
    InputField ipField;
    Text hud;
    LanBrowser browser;
    int shownVersion = -1;
    float nextHudUpdate;

    void Start()
    {
        UIKit.EnsureEventSystem();
        BuildUI();

        browser = gameObject.AddComponent<LanBrowser>();
        browser.enabled = false;

        var game = NetworkGame.Instance;
        game.EnteredGame += OnEnteredGame;
        game.LeftGame += OnLeftGame;
        game.ConnectFailed += OnConnectFailed;

        PauseMenu.SetCursorCaptured(false);
        ShowMain(null);
    }

    void OnDestroy()
    {
        var game = NetworkGame.Instance;
        if (game == null) return;
        game.EnteredGame -= OnEnteredGame;
        game.LeftGame -= OnLeftGame;
        game.ConnectFailed -= OnConnectFailed;
    }

    void Update()
    {
        if (!NetworkGame.InGame)
        {
            // Esc goes back from the server list.
            if (joinPanel.gameObject.activeSelf && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                ShowMain(null);

            if (joinPanel.gameObject.activeSelf) RefreshServerList();
            return;
        }

        if (Time.unscaledTime >= nextHudUpdate)
        {
            nextHudUpdate = Time.unscaledTime + 1f;
            hud.text = NetworkGame.IsMultiplayer ? NetworkGame.Instance.DescribeSession() + "     Esc — меню" : "";
        }
    }

    // ---- navigation -------------------------------------------------------------------------

    void ShowMain(string message)
    {
        menuRoot.SetActive(true);
        hud.gameObject.SetActive(false);
        mainPanel.gameObject.SetActive(true);
        joinPanel.gameObject.SetActive(false);
        browser.enabled = false;
        mainGroup.interactable = true;
        mainMessage.text = message ?? "";
    }

    void ShowJoin()
    {
        mainPanel.gameObject.SetActive(false);
        joinPanel.gameObject.SetActive(true);
        joinGroup.interactable = true;
        browser.enabled = true;
        shownVersion = -1;
        RefreshServerList();
    }

    void OnEnteredGame()
    {
        menuRoot.SetActive(false);
        browser.enabled = false;
        hud.gameObject.SetActive(true);
        nextHudUpdate = 0f;
    }

    void OnLeftGame(string message) => ShowMain(message);

    void OnConnectFailed(string message)
    {
        if (joinPanel.gameObject.activeSelf)
        {
            joinGroup.interactable = true;
            joinStatus.text = message;
        }
        else
        {
            mainGroup.interactable = true;
            mainMessage.text = message;
        }
    }

    // ---- actions ----------------------------------------------------------------------------

    void PlaySolo()
    {
        mainGroup.interactable = false;
        NetworkGame.Instance.StartSolo();
    }

    void HostGame()
    {
        mainGroup.interactable = false;
        mainMessage.text = "Создаём игру…";
        NetworkGame.Instance.StartHostForFriends();
    }

    void JoinServer(ServerInfo server)
    {
        joinGroup.interactable = false;
        joinStatus.text = $"Подключаемся к {server.Name} ({server.Address})…";
        NetworkGame.Instance.Join(server.Address, server.Port);
    }

    void JoinByIp()
    {
        string text = ipField.text.Trim();
        if (text.Length == 0)
        {
            joinStatus.text = "Введите IP-адрес хоста, например 26.12.34.56";
            return;
        }

        string address = text;
        int port = LanProtocol.GamePort;
        int colon = text.LastIndexOf(':');
        if (colon > 0 && int.TryParse(text.Substring(colon + 1), out int parsedPort))
        {
            address = text.Substring(0, colon);
            port = parsedPort;
        }

        joinGroup.interactable = false;
        joinStatus.text = $"Подключаемся к {address}…";
        NetworkGame.Instance.Join(address, port);
    }

    void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---- server list ------------------------------------------------------------------------

    void RefreshServerList()
    {
        if (!joinGroup.interactable) return; // busy connecting: keep the status text

        var servers = browser.GetServers();
        if (browser.Error != null)
            joinStatus.text = "Не удалось начать поиск игр: " + browser.Error;
        else if (servers.Count == 0)
            joinStatus.text = "Ищем игры в сети… Если друга не видно — введите его IP ниже.";
        else
            joinStatus.text = $"Найдено игр: {servers.Count}. Нажмите на игру, чтобы подключиться.";

        if (browser.Version == shownVersion) return;
        shownVersion = browser.Version;

        for (int i = serverList.childCount - 1; i >= 0; i--)
            Destroy(serverList.GetChild(i).gameObject);

        int shown = Mathf.Min(servers.Count, MaxServerRows);
        for (int i = 0; i < shown; i++)
        {
            var server = servers[i];
            string label = $"{server.Name}   {server.Address}   {server.Players}/{server.MaxPlayers}";
            UIKit.AddButton(serverList, label, UIKit.Blue, () => JoinServer(server), 62f, 30);
        }
        if (servers.Count > MaxServerRows)
            UIKit.AddLabel(serverList, $"…и ещё {servers.Count - MaxServerRows}", 26, FontStyle.Italic,
                new Color(1f, 1f, 1f, 0.6f), 36f);
    }

    // ---- UI ---------------------------------------------------------------------------------

    void BuildUI()
    {
        var canvas = UIKit.CreateCanvas(transform, "MainMenuCanvas", 90);
        menuRoot = canvas.gameObject;
        UIKit.AddDim(canvas.transform, 0.35f);

        // Main panel
        mainPanel = UIKit.AddPanel(canvas.transform, "MainPanel", new Vector2(700f, 720f));
        mainGroup = mainPanel.gameObject.AddComponent<CanvasGroup>();
        UIKit.AddLabel(mainPanel, "IMPROVISATION", 60, FontStyle.Bold, Color.white, 110f);
        UIKit.AddButton(mainPanel, "Играть одному", UIKit.Green, PlaySolo);
        UIKit.AddButton(mainPanel, "Создать игру по сети", UIKit.Blue, HostGame);
        UIKit.AddButton(mainPanel, "Подключиться по сети", UIKit.Blue, ShowJoin);
        UIKit.AddButton(mainPanel, "Выйти", UIKit.Red, Quit);
        mainMessage = UIKit.AddLabel(mainPanel, "", 26, FontStyle.Normal, new Color(1f, 0.85f, 0.5f), 90f);

        // Join panel: found games + manual IP
        joinPanel = UIKit.AddPanel(canvas.transform, "JoinPanel", new Vector2(940f, 1000f));
        joinGroup = joinPanel.gameObject.AddComponent<CanvasGroup>();
        UIKit.AddLabel(joinPanel, "Игры в сети", 56, FontStyle.Bold, Color.white, 90f);
        joinStatus = UIKit.AddLabel(joinPanel, "", 26, FontStyle.Normal, new Color(1f, 1f, 1f, 0.75f), 70f);
        serverList = UIKit.AddList(joinPanel, "ServerList", 8f);
        serverList.gameObject.AddComponent<LayoutElement>().preferredHeight = 350f;
        UIKit.AddLabel(joinPanel, "Или подключитесь по IP (адрес хоста в Radmin):", 26, FontStyle.Normal,
            new Color(1f, 1f, 1f, 0.75f), 44f);
        ipField = UIKit.AddInputField(joinPanel, "например 26.12.34.56");
        UIKit.AddButton(joinPanel, "Подключиться по IP", UIKit.Green, JoinByIp, 76f);
        UIKit.AddButton(joinPanel, "Назад", UIKit.Gray, () => ShowMain(null), 76f);

        // In-game HUD (separate canvas so it stays visible when the menu is hidden)
        var hudCanvas = UIKit.CreateCanvas(transform, "HudCanvas", 50);
        hud = UIKit.AddLabel(hudCanvas.transform, "", 28, FontStyle.Bold, Color.white, 40f, TextAnchor.UpperLeft);
        var hudRt = hud.rectTransform;
        hudRt.anchorMin = new Vector2(0f, 1f);
        hudRt.anchorMax = new Vector2(1f, 1f);
        hudRt.pivot = new Vector2(0f, 1f);
        hudRt.anchoredPosition = new Vector2(24f, -20f);
        hudRt.sizeDelta = new Vector2(-48f, 44f);
        hud.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.8f);
    }
}
