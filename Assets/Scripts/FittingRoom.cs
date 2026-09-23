using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The fitting room next to the punching bag: a booth with a curtain. Stepping through the curtain starts the loading
// at once and opens the customization screen: the camera is pinned in front of the character, who looks straight at us, and a
// panel on the left has a tab for every part (head, eyes, mouth, hands, clothes, legs) with the options to choose from.
// While somebody is behind the curtain nobody else sees their character (see CharacterStyle).
public class FittingRoom : MonoBehaviour
{
    // True while the local player is in the customization screen (other HUD hides itself).
    public static bool Active { get; private set; }

    [SerializeField] Transform curtainLeft;
    [SerializeField] Transform curtainRight;
    [SerializeField] Transform standPoint;     // where the character stands while dressing up
    [SerializeField] Transform cameraPoint;    // where the camera hangs, looking at the character
    [SerializeField] Transform exitPoint;      // where the character reappears in front of the curtain
    [SerializeField] Transform signAnchor;     // the sign floats above this
    [SerializeField] Vector2 doorCenter;       // world X/Z of the doorway...
    [SerializeField] Vector2 doorHalfSize;     // ...and its half size: anybody in there makes the curtain open
    [SerializeField] Vector2 insideCenter;     // world X/Z of the inside of the booth, from just behind the curtain...
    [SerializeField] Vector2 insideHalfSize;   // ...to the back wall: stepping in here at once starts the fitting
    [SerializeField] float fov = 58f;   // the character is taller now (round body on legs, top hats), so a wider view

    // interface
    GameObject canvasRoot;
    Button[] tabButtons;
    Text sectionLabel;
    RectTransform optionsList;
    readonly List<OptionRow> rows = new List<OptionRow>();
    int tab;
    uint current;

    // world
    Vector3 leftClosed, rightClosed, leftScale, rightScale;
    float open;
    Transform signCanvas;
    PlayerController local;
    readonly List<PlayerController> players = new List<PlayerController>();
    float nextLookup;
    float nextEnter;
    bool switching;

    void Awake()
    {
        if (curtainLeft != null) { leftClosed = curtainLeft.localPosition; leftScale = curtainLeft.localScale; }
        if (curtainRight != null) { rightClosed = curtainRight.localPosition; rightScale = curtainRight.localScale; }
        BuildSign();
    }

    void Start()
    {
        UIKit.EnsureEventSystem();
        BuildUI();
    }

    void OnDestroy()
    {
        Active = false;
        PauseMenu.CustomizerOpen = false;
    }

    void Update()
    {
        if (Time.unscaledTime >= nextLookup)
        {
            nextLookup = Time.unscaledTime + 0.25f;
            local = null;
            players.Clear();
            foreach (var p in FindObjectsByType<PlayerController>())
            {
                if (!p.IsSpawned || p.IsBot) continue;
                players.Add(p);
                if (p.IsOwner) local = p;
            }
        }

        UpdateCurtain();
        UpdateSign();

        if (Active)
        {
            // Leaving the game (or losing the character) closes the screen.
            if (!NetworkGame.InGame || local == null) { CloseNow(); return; }
            if (!PauseMenu.IsPaused && Cursor.lockState == CursorLockMode.Locked) PauseMenu.SetCursorCaptured(false);
            return;
        }

        // The moment somebody steps through the curtain the loading starts; there is no need to walk to the back.
        if (!NetworkGame.InGame || local == null || switching || PauseMenu.IsPaused || Time.unscaledTime < nextEnter) return;
        if (local.InMatch || !local.ControlledNow) return;

        Vector3 position = local.transform.position;
        if (Mathf.Abs(position.x - insideCenter.x) <= insideHalfSize.x && Mathf.Abs(position.z - insideCenter.y) <= insideHalfSize.y)
            Enter();
    }

    // ---- the booth ----------------------------------------------------------------------------

    // The curtain slides open while somebody stands in the doorway and closes again behind them.
    void UpdateCurtain()
    {
        bool someoneAtDoor = false;
        foreach (var p in players)
        {
            if (p == null) continue;
            Vector3 position = p.transform.position;
            if (Mathf.Abs(position.x - doorCenter.x) <= doorHalfSize.x && Mathf.Abs(position.z - doorCenter.y) <= doorHalfSize.y)
            {
                someoneAtDoor = true;
                break;
            }
        }

        open = Mathf.MoveTowards(open, someoneAtDoor ? 1f : 0f, 2.5f * Time.unscaledDeltaTime);
        float eased = open * open * (3f - 2f * open);

        // The halves gather up against the walls (they get narrower) instead of sliding out past them.
        float squeeze = Mathf.Lerp(1f, 0.28f, eased);
        if (curtainLeft != null)
        {
            float width = leftScale.x * squeeze;
            curtainLeft.localScale = new Vector3(width, leftScale.y, leftScale.z);
            curtainLeft.localPosition = new Vector3(leftClosed.x - leftScale.x * 0.5f + width * 0.5f, leftClosed.y, leftClosed.z);
        }
        if (curtainRight != null)
        {
            float width = rightScale.x * squeeze;
            curtainRight.localScale = new Vector3(width, rightScale.y, rightScale.z);
            curtainRight.localPosition = new Vector3(rightClosed.x + rightScale.x * 0.5f - width * 0.5f, rightClosed.y, rightClosed.z);
        }
    }

    void BuildSign()
    {
        if (signAnchor == null) return;

        var canvasObject = new GameObject("Sign", typeof(Canvas));
        signCanvas = canvasObject.transform;
        signCanvas.SetParent(signAnchor, false);
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        ((RectTransform)signCanvas).sizeDelta = new Vector2(700f, 110f);
        signCanvas.localScale = Vector3.one * 0.01f;

        var text = UIKit.NewUI("Text", signCanvas);
        UIKit.Stretch(text);
        var label = text.gameObject.AddComponent<Text>();
        label.font = UIKit.Font;
        label.text = "ПРИМЕРОЧНАЯ";
        label.fontSize = 84;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.color = new Color(1f, 0.85f, 0.95f);
        label.raycastTarget = false;
        var outline = text.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.45f, 0.05f, 0.3f, 0.95f);
        outline.effectDistance = new Vector2(4f, -4f);
    }

    void UpdateSign()
    {
        var cam = Camera.main;
        if (signCanvas == null || cam == null) return;
        signCanvas.rotation = Quaternion.LookRotation(signCanvas.position - cam.transform.position);
    }

    // ---- entering and leaving -----------------------------------------------------------------

    void Enter()
    {
        switching = true;
        if (MatchUI.Instance != null) MatchUI.Instance.Transition(Begin);
        else Begin();
    }

    void Begin()
    {
        switching = false;
        if (local == null || !local.IsSpawned) return;

        Active = true;
        PauseMenu.CustomizerOpen = true;
        local.Locked = true;
        local.SetCustomizing(true);

        // The character turns to face the camera.
        Vector3 toCamera = cameraPoint.position - standPoint.position;
        toCamera.y = 0f;
        local.TeleportInstant(standPoint.position, Quaternion.LookRotation(toCamera).eulerAngles.y);

        var follow = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        if (follow != null) follow.SetFixedView(cameraPoint.position, cameraPoint.rotation, fov);

        current = local.StyleValue;
        PauseMenu.SetCursorCaptured(false);
        canvasRoot.SetActive(true);
        ShowTab(0);
    }

    // The "Готово" button: keep the outfit and step out in front of the curtain.
    void Finish()
    {
        if (!Active || switching) return;
        switching = true;
        if (MatchUI.Instance != null) MatchUI.Instance.Transition(Leave);
        else Leave();
    }

    void Leave()
    {
        switching = false;
        PlayerStyle.Save(current);
        var player = local;
        CloseNow();
        if (player != null && player.IsSpawned)
        {
            Vector3 outward = exitPoint.position - standPoint.position;
            outward.y = 0f;
            player.TeleportInstant(exitPoint.position, Quaternion.LookRotation(outward).eulerAngles.y);
        }
        nextEnter = Time.unscaledTime + 1f;
    }

    // Closes the screen right away, whatever the reason.
    void CloseNow()
    {
        Active = false;
        PauseMenu.CustomizerOpen = false;
        canvasRoot.SetActive(false);

        if (local != null && local.IsSpawned)
        {
            local.Locked = false;
            local.SetCustomizing(false);
        }

        var follow = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        if (follow != null) follow.ClearFixedView();

        if (NetworkGame.InGame && !PauseMenu.IsPaused && !PauseMenu.UiWantsCursor) PauseMenu.SetCursorCaptured(true);
    }

    // ---- choosing -----------------------------------------------------------------------------

    void ShowTab(int index)
    {
        tab = index;
        sectionLabel.text = CharacterStyleCatalog.SlotNames[tab] + ": выберите вариант";
        for (int i = 0; i < tabButtons.Length; i++)
            SetButtonColor(tabButtons[i], i == tab ? UIKit.Blue : UIKit.Gray);
        RefreshOptions();
    }

    void Choose(int option)
    {
        current = CharacterStyleCatalog.Set(current, tab, option);
        Apply();
        RefreshOptions();
    }

    void ResetAll()
    {
        current = 0;
        Apply();
        RefreshOptions();
    }

    void Apply()
    {
        PlayerStyle.Save(current);
        if (local != null) local.SetStyle(current);
    }

    // The rows are built once and only re-coloured / re-labelled afterwards. (Destroying and rebuilding them on every
    // click made the whole list blink.)
    void RefreshOptions()
    {
        int chosen = CharacterStyleCatalog.Get(current, tab);
        string[] names = CharacterStyleCatalog.OptionNames[tab];

        while (rows.Count < names.Length) rows.Add(CreateOptionRow(rows.Count));

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            bool used = i < names.Length;
            if (row.Root.gameObject.activeSelf != used) row.Root.gameObject.SetActive(used);
            if (!used) continue;

            bool selected = i == chosen;
            row.Label.text = names[i];
            row.Label.fontStyle = selected ? FontStyle.Bold : FontStyle.Normal;
            row.Chip.color = CharacterStyleCatalog.Swatches[tab][i];

            Color normal = selected ? new Color(0.25f, 0.6f, 0.35f) : new Color(0.2f, 0.24f, 0.33f);
            SetButtonColor(row.Button, normal);
        }
    }

    // ---- building the screen ------------------------------------------------------------------

    void BuildUI()
    {
        var canvas = UIKit.CreateCanvas(transform, "FittingCanvas", 80);
        canvasRoot = canvas.gameObject;

        var panel = UIKit.NewUI("Panel", canvas.transform);
        panel.anchorMin = panel.anchorMax = new Vector2(0f, 0.5f);
        panel.pivot = new Vector2(0f, 0.5f);
        panel.anchoredPosition = new Vector2(40f, 0f);
        panel.sizeDelta = new Vector2(660f, 980f);
        panel.gameObject.AddComponent<Image>().color = new Color(0.1f, 0.12f, 0.19f, 0.94f);

        var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(24, 24, 22, 22);
        layout.spacing = 14f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        UIKit.AddLabel(panel, "Примерочная", 48, FontStyle.Bold, Color.white, 62f, TextAnchor.MiddleLeft);

        // the tabs, along the top of the panel
        var tabs = UIKit.NewUI("Tabs", panel);
        tabs.gameObject.AddComponent<LayoutElement>().preferredHeight = 68f;
        var tabsLayout = tabs.gameObject.AddComponent<HorizontalLayoutGroup>();
        tabsLayout.spacing = 6f;
        tabsLayout.childControlWidth = true;
        tabsLayout.childControlHeight = true;
        tabsLayout.childForceExpandWidth = true;
        tabsLayout.childForceExpandHeight = true;

        tabButtons = new Button[CharacterStyleCatalog.SlotCount];
        for (int i = 0; i < tabButtons.Length; i++)
        {
            int index = i;
            tabButtons[i] = UIKit.AddButton(tabs, CharacterStyleCatalog.SlotNames[i], UIKit.Gray, () => ShowTab(index), 68f, 22);
        }

        sectionLabel = UIKit.AddLabel(panel, "", 30, FontStyle.Bold, new Color(1f, 1f, 1f, 0.85f), 44f, TextAnchor.MiddleLeft);

        optionsList = UIKit.AddList(panel, "Options", 10f);
        optionsList.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
        var listLayout = optionsList.GetComponent<VerticalLayoutGroup>();
        listLayout.childAlignment = TextAnchor.UpperCenter;

        UIKit.AddButton(panel, "Готово", UIKit.Green, Finish, 84f, 38);
        UIKit.AddButton(panel, "Сбросить всё", UIKit.Gray, ResetAll, 62f, 28);

        canvasRoot.SetActive(false);
    }

    class OptionRow
    {
        public RectTransform Root;
        public Button Button;
        public Image Chip;
        public Text Label;
    }

    OptionRow CreateOptionRow(int index)
    {
        var row = new OptionRow();
        row.Root = UIKit.NewUI("Option", optionsList);
        row.Root.gameObject.AddComponent<LayoutElement>().preferredHeight = 76f;
        var image = row.Root.gameObject.AddComponent<Image>();
        image.color = Color.white;

        row.Button = row.Root.gameObject.AddComponent<Button>();
        row.Button.targetGraphic = image;
        row.Button.onClick.AddListener(() => Choose(index));

        var chip = UIKit.NewUI("Swatch", row.Root);
        chip.anchorMin = chip.anchorMax = new Vector2(0f, 0.5f);
        chip.pivot = new Vector2(0f, 0.5f);
        chip.anchoredPosition = new Vector2(16f, 0f);
        chip.sizeDelta = new Vector2(46f, 46f);
        row.Chip = chip.gameObject.AddComponent<Image>();
        row.Chip.raycastTarget = false;
        chip.gameObject.AddComponent<Outline>().effectColor = new Color(1f, 1f, 1f, 0.45f);

        row.Label = UIKit.AddLabel(row.Root, "", 32, FontStyle.Normal, Color.white, 40f, TextAnchor.MiddleLeft);
        var textRect = row.Label.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(80f, 0f);
        textRect.offsetMax = new Vector2(-16f, 0f);
        return row;
    }

    // Colours change at once (no fade), so a click never flashes through the old colour.
    static void SetButtonColor(Button button, Color color)
    {
        var colors = button.colors;
        colors.normalColor = color;
        colors.highlightedColor = Color.Lerp(color, Color.white, 0.2f);
        colors.selectedColor = color;
        colors.pressedColor = Color.Lerp(color, Color.black, 0.25f);
        colors.fadeDuration = 0f;
        button.colors = colors;
    }
}
