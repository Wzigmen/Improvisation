using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Esc opens a pause menu during the game (Resume / Main menu / Quit).
// In single player the game freezes; in multiplayer it keeps running for everyone else.
public class PauseMenu : MonoBehaviour
{
    public static bool IsPaused { get; private set; }
    public static PauseMenu Instance { get; private set; }

    // Set by other menus (the ready check) that need the mouse: the cursor stays free while true.
    public static bool UiWantsCursor;

    // True while the ability card panel (C) or the fitting-room screen is open: the cursor stays free and the mouse
    // belongs to that screen, so no punches, dashes or cards are fired.
    public static bool AbilityPanelOpen;
    public static bool CustomizerOpen;
    public static bool PanelOpen => AbilityPanelOpen || CustomizerOpen;

    GameObject menuRoot;
    Button resumeButton;
    Button endMatchButton;
    bool altFree;
    Text infoLabel;
    InputAction pauseAction;

    void Awake()
    {
        Instance = this;
        IsPaused = false;
        Time.timeScale = 1f;

        pauseAction = new InputAction("Pause", InputActionType.Button);
        pauseAction.AddBinding("<Keyboard>/escape");
        pauseAction.AddBinding("<Gamepad>/start");
        pauseAction.performed += OnPausePressed;

        UIKit.EnsureEventSystem();
        BuildUI();
        menuRoot.SetActive(false);
    }

    void OnEnable() => pauseAction.Enable();
    void OnDisable() => pauseAction.Disable();

    void OnDestroy()
    {
        pauseAction.performed -= OnPausePressed;
        pauseAction.Dispose();
        IsPaused = false;
        Time.timeScale = 1f;
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (!NetworkGame.InGame || IsPaused)
        {
            altFree = false;
            return;
        }

        // Holding Alt frees the cursor so on-screen buttons (like "Play") can be clicked without pausing.
        bool alt = Keyboard.current != null && Keyboard.current.leftAltKey.isPressed;
        if (alt && !altFree && Cursor.lockState == CursorLockMode.Locked)
        {
            altFree = true;
            SetCursorCaptured(false);
        }
        else if (!alt && altFree)
        {
            altFree = false;
            if (!UiWantsCursor && !PanelOpen) SetCursorCaptured(true);
        }

        // Clicking back into the game window re-captures the cursor, unless a menu needs the mouse.
        if (!UiWantsCursor && !PanelOpen && !altFree && Cursor.lockState != CursorLockMode.Locked &&
            Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            SetCursorCaptured(true);
        }
    }

    void OnPausePressed(InputAction.CallbackContext ctx)
    {
        if (!NetworkGame.InGame) return;
        if (IsPaused) Resume();
        else Pause();
    }

    void Pause()
    {
        IsPaused = true;
        Time.timeScale = NetworkGame.IsMultiplayer ? 1f : 0f;
        infoLabel.text = NetworkGame.Instance != null && NetworkGame.IsMultiplayer ? NetworkGame.Instance.DescribeSession() : "";

        // The host can call a fight off (this is also how a solo training session ends).
        var match = MatchManager.Instance;
        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
        endMatchButton.gameObject.SetActive(isHost && match != null && match.CurrentPhase != MatchManager.Phase.Lobby);

        menuRoot.SetActive(true);
        SetCursorCaptured(false);
        EventSystem.current.SetSelectedGameObject(resumeButton.gameObject);
    }

    void Resume()
    {
        IsPaused = false;
        Time.timeScale = 1f;
        menuRoot.SetActive(false);
        SetCursorCaptured(true);
    }

    // Used when leaving the game so the menu never stays open behind the main menu.
    public void ForceClose()
    {
        IsPaused = false;
        Time.timeScale = 1f;
        menuRoot.SetActive(false);
    }

    void EndMatch()
    {
        if (MatchManager.Instance != null) MatchManager.Instance.EndMatchRpc();
        Resume();
    }

    void ToMainMenu()
    {
        if (NetworkGame.Instance != null) NetworkGame.Instance.LeaveToMenu();
    }

    void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public static void SetCursorCaptured(bool captured)
    {
        Cursor.lockState = captured ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !captured;
    }

    void BuildUI()
    {
        var canvas = UIKit.CreateCanvas(transform, "PauseCanvas", 100);
        menuRoot = canvas.gameObject;

        UIKit.AddDim(canvas.transform, 0.55f);
        var panel = UIKit.AddPanel(canvas.transform, "Panel", new Vector2(640f, 680f));

        UIKit.AddLabel(panel, "ПАУЗА", 64, FontStyle.Bold, Color.white, 100f);
        infoLabel = UIKit.AddLabel(panel, "", 26, FontStyle.Normal, new Color(1f, 1f, 1f, 0.7f), 40f);
        resumeButton = UIKit.AddButton(panel, "Продолжить", UIKit.Green, Resume);
        endMatchButton = UIKit.AddButton(panel, "Завершить бой", new Color(0.85f, 0.55f, 0.2f), EndMatch);
        endMatchButton.gameObject.SetActive(false); // only shown to the host during a match
        UIKit.AddButton(panel, "В главное меню", UIKit.Blue, ToMainMenu);
        UIKit.AddButton(panel, "Выйти из игры", UIKit.Red, Quit);
    }
}
