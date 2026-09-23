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

    // nickname: a field in the top-right corner, and a small dialog that asks for it when somebody tries to play without one
    CanvasGroup nickCornerGroup;
    InputField nickField;
    Text nickStatus;
    GameObject nickDialog;
    InputField dialogField;
    Text dialogError;
    System.Action pendingAction;   // what the player wanted to do when the dialog interrupted them

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
            // Settings sits on top of everything else in the menu; Esc just closes it.
            if (SettingsMenu.Instance != null && SettingsMenu.Instance.IsOpen)
            {
                if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) SettingsMenu.Instance.Close();
                return;
            }

            // The nickname dialog takes all the keys: Enter confirms, Esc cancels.
            if (nickDialog.activeSelf)
            {
                if (Keyboard.current != null && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame))
                    ConfirmNickDialog();
                else if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                    CloseNickDialog();
                return;
            }

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
        nickDialog.SetActive(false);
        RefreshNickCorner();
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
        if (NeedNickname(PlaySolo)) return;
        mainGroup.interactable = false;
        NetworkGame.Instance.StartSolo();
    }

    void HostGame()
    {
        if (NeedNickname(HostGame)) return;
        mainGroup.interactable = false;
        mainMessage.text = "Создаём игру…";
        NetworkGame.Instance.StartHostForFriends();
    }

    void JoinServer(ServerInfo server)
    {
        if (NeedNickname(() => JoinServer(server))) return;
        joinGroup.interactable = false;
        joinStatus.text = $"Подключаемся к {server.Name} ({server.Address})…";
        NetworkGame.Instance.Join(server.Address, server.Port);
    }

    void JoinByIp()
    {
        if (NeedNickname(JoinByIp)) return;
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

    void OpenSettings()
    {
        if (SettingsMenu.Instance != null) SettingsMenu.Instance.Open();
    }

    // ---- nickname ---------------------------------------------------------------------------

    static char ValidateNickChar(string text, int index, char c) => PlayerNickname.IsAllowedChar(c) ? c : '\0';

    // True (and the dialog opens) when there is no usable nickname yet; `action` is what to do after it is entered.
    bool NeedNickname(System.Action action)
    {
        // Somebody may click "play" straight from the corner field, before it has reported that it lost focus.
        if (!PlayerNickname.IsValid(PlayerNickname.Current) && PlayerNickname.IsValid(nickField.text))
            PlayerNickname.Set(nickField.text);

        if (PlayerNickname.IsValid(PlayerNickname.Current)) return false;

        pendingAction = action;
        dialogField.text = "";
        dialogError.text = "";
        nickDialog.SetActive(true);
        nickDialog.transform.SetAsLastSibling();
        nickCornerGroup.interactable = false;
        mainGroup.interactable = false;
        joinGroup.interactable = false;
        dialogField.Select();
        dialogField.ActivateInputField();
        return true;
    }

    void ConfirmNickDialog()
    {
        if (!PlayerNickname.Set(dialogField.text))
        {
            dialogError.text = $"Ник слишком короткий: нужно хотя бы {PlayerNickname.MinLength} символа.";
            dialogField.ActivateInputField();
            return;
        }

        var action = pendingAction;
        CloseNickDialog();   // (clears pendingAction)
        RefreshNickCorner();
        action?.Invoke();
    }

    void CloseNickDialog()
    {
        pendingAction = null;
        nickDialog.SetActive(false);
        nickCornerGroup.interactable = true;
        mainGroup.interactable = true;
        joinGroup.interactable = true;
    }

    // The corner field: saves the nickname as soon as the person leaves the field.
    void OnNickEdited(string text)
    {
        string clean = PlayerNickname.Clean(text);
        if (clean.Length == 0)
        {
            PlayerNickname.Clear();
        }
        else if (PlayerNickname.Set(clean))
        {
            nickField.text = clean;
        }
        else
        {
            nickStatus.color = new Color(1f, 0.55f, 0.5f);
            nickStatus.text = $"Минимум {PlayerNickname.MinLength} символа";
            return;
        }
        RefreshNickCorner();
    }

    void RefreshNickCorner()
    {
        string current = PlayerNickname.Current;
        if (nickField.text != current && !nickField.isFocused) nickField.text = current;

        bool valid = PlayerNickname.IsValid(current);
        nickStatus.color = valid ? new Color(0.5f, 0.95f, 0.6f) : new Color(1f, 0.85f, 0.5f);
        nickStatus.text = valid ? "Ник сохранён" : "Без ника в игру не войти";
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

    // Top-right corner: "Ваш ник" and a field to type it in.
    void BuildNickCorner(Transform parent)
    {
        var corner = UIKit.NewUI("NickCorner", parent);
        corner.anchorMin = corner.anchorMax = corner.pivot = new Vector2(1f, 1f);
        corner.anchoredPosition = new Vector2(-32f, -32f);
        corner.sizeDelta = new Vector2(440f, 210f);
        corner.gameObject.AddComponent<Image>().color = UIKit.PanelColor;
        nickCornerGroup = corner.gameObject.AddComponent<CanvasGroup>();

        var layout = corner.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(24, 24, 18, 16);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        UIKit.AddLabel(corner, "Ваш ник", 32, FontStyle.Bold, Color.white, 44f, TextAnchor.MiddleLeft);
        nickField = UIKit.AddInputField(corner, "введите ник", 70f);
        nickField.characterLimit = PlayerNickname.MaxLength;
        nickField.onValidateInput = ValidateNickChar;
        nickField.onEndEdit.AddListener(OnNickEdited);
        nickStatus = UIKit.AddLabel(corner, "", 24, FontStyle.Normal, Color.white, 34f, TextAnchor.MiddleLeft);
    }

    // The mini menu that appears when somebody presses a "play" button without a nickname.
    void BuildNickDialog(Transform parent)
    {
        var root = UIKit.NewUI("NickDialog", parent);
        UIKit.Stretch(root);
        nickDialog = root.gameObject;
        UIKit.AddDim(root, 0.6f);

        var panel = UIKit.AddPanel(root, "NickPanel", new Vector2(760f, 560f));
        UIKit.AddLabel(panel, "Введите ник", 56, FontStyle.Bold, Color.white, 80f);
        UIKit.AddLabel(panel, $"Он будет виден над вашим персонажем. От {PlayerNickname.MinLength} до {PlayerNickname.MaxLength} символов: " +
                              "буквы, цифры, пробел, _ - .", 26, FontStyle.Normal, new Color(1f, 1f, 1f, 0.75f), 76f);
        dialogField = UIKit.AddInputField(panel, "ваш ник", 76f);
        dialogField.characterLimit = PlayerNickname.MaxLength;
        dialogField.onValidateInput = ValidateNickChar;
        dialogError = UIKit.AddLabel(panel, "", 26, FontStyle.Bold, new Color(1f, 0.55f, 0.5f), 36f);
        UIKit.AddButton(panel, "Готово", UIKit.Green, ConfirmNickDialog, 76f);
        UIKit.AddButton(panel, "Отмена", UIKit.Gray, CloseNickDialog, 66f);

        nickDialog.SetActive(false);
    }

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
        UIKit.AddButton(mainPanel, "Настройки", UIKit.Gray, OpenSettings);
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

        BuildNickCorner(canvas.transform);
        BuildNickDialog(canvas.transform);

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
