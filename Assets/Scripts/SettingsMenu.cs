using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The Настройки (Settings) screen, opened from the main menu or the pause menu (both just call
// SettingsMenu.Instance.Open()). Three tabs:
//   Графика - screen resolution, fullscreen, and an FPS counter (shown in the top-left corner while it's on)
//   Аудио   - empty for now
//   Игра    - a slider for the character camera's field of view
// Built in code like the other menus.
public class SettingsMenu : MonoBehaviour
{
    public static SettingsMenu Instance { get; private set; }

    GameObject root;
    Button[] tabButtons;
    RectTransform[] tabContents;
    int tab;

    Button fullscreenButton;
    Button fpsButton;
    Button resolutionButton;
    RectTransform resolutionList, resolutionArrow;
    Text fovValueText;

    // The actual on-screen readout: independent of whether the settings screen itself is open.
    Text fpsCounter;
    float fpsSmoothed = -1f;

    void Awake()
    {
        Instance = this;
        UIKit.EnsureEventSystem();
        BuildFpsCounter();
        BuildUI();
        root.SetActive(false);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public bool IsOpen => root.activeSelf;

    public void Open()
    {
        root.SetActive(true);
        RefreshGraphicsTab();
    }

    public void Close() => root.SetActive(false);

    int shownWidth, shownHeight;

    void Update()
    {
        UpdateFpsCounter();

        // Keep the dropdown's label in step if the window is resized while settings are open.
        if (root.activeSelf && (Screen.width != shownWidth || Screen.height != shownHeight))
        {
            shownWidth = Screen.width;
            shownHeight = Screen.height;
            UpdateResolutionLabel();
        }
    }

    void UpdateFpsCounter()
    {
        bool show = GameSettings.ShowFps;
        if (fpsCounter.gameObject.activeSelf != show) fpsCounter.gameObject.SetActive(show);
        if (!show) return;

        float dt = Time.unscaledDeltaTime;
        float instant = dt > 0f ? 1f / dt : 0f;
        fpsSmoothed = fpsSmoothed < 0f ? instant : Mathf.Lerp(fpsSmoothed, instant, 0.1f);
        fpsCounter.text = Mathf.RoundToInt(fpsSmoothed) + " FPS";
    }

    // ---- tabs -----------------------------------------------------------------------------------

    void ShowTab(int index)
    {
        tab = index;
        if (resolutionList != null) SetResolutionListOpen(false);
        for (int i = 0; i < tabButtons.Length; i++)
            UIKit.SetButtonColors(tabButtons[i], i == index ? UIKit.Blue : UIKit.Gray);
        for (int i = 0; i < tabContents.Length; i++)
            tabContents[i].gameObject.SetActive(i == index);
    }

    // ---- graphics ---------------------------------------------------------------------------------

    void RefreshGraphicsTab()
    {
        SetResolutionListOpen(false);
        UpdateFullscreenLabel();
        UpdateFpsLabel();
    }

    void UpdateFullscreenLabel()
    {
        bool full = Screen.fullScreenMode != FullScreenMode.Windowed;
        UIKit.SetButtonText(fullscreenButton, "Полноэкранный режим: " + (full ? "Вкл" : "Выкл"));
    }

    void ToggleFullscreen()
    {
        bool full = Screen.fullScreenMode != FullScreenMode.Windowed;
        Screen.fullScreenMode = full ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow;
        UpdateFullscreenLabel();
    }

    void UpdateFpsLabel() => UIKit.SetButtonText(fpsButton, "Показывать FPS: " + (GameSettings.ShowFps ? "Вкл" : "Выкл"));

    void ToggleFps()
    {
        GameSettings.ShowFps = !GameSettings.ShowFps;
        UpdateFpsLabel();
    }

    static void SetResolution(int width, int height) => Screen.SetResolution(width, height, Screen.fullScreenMode);

    // ---- game -------------------------------------------------------------------------------------

    void OnFovChanged(float value)
    {
        GameSettings.CameraFov = value;
        fovValueText.text = Mathf.RoundToInt(value) + "°";
    }

    // ---- building the UI ----------------------------------------------------------------------

    void BuildFpsCounter()
    {
        var canvas = UIKit.CreateCanvas(transform, "FpsCanvas", 65);
        var text = UIKit.NewUI("Fps", canvas.transform);
        text.anchorMin = text.anchorMax = new Vector2(0f, 1f);
        text.pivot = new Vector2(0f, 1f);
        text.anchoredPosition = new Vector2(16f, -16f);
        text.sizeDelta = new Vector2(220f, 40f);

        fpsCounter = text.gameObject.AddComponent<Text>();
        fpsCounter.font = UIKit.Font;
        fpsCounter.fontSize = 28;
        fpsCounter.fontStyle = FontStyle.Bold;
        fpsCounter.color = new Color(0.4f, 1f, 0.5f);
        fpsCounter.alignment = TextAnchor.UpperLeft;
        fpsCounter.raycastTarget = false;
        var outline = text.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(2f, -2f);
        fpsCounter.gameObject.SetActive(false);
    }

    void BuildUI()
    {
        var canvas = UIKit.CreateCanvas(transform, "SettingsCanvas", 110);
        root = canvas.gameObject;
        UIKit.AddDim(canvas.transform, 0.6f);

        var panel = UIKit.NewUI("Panel", canvas.transform);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(900f, 820f);
        panel.gameObject.AddComponent<Image>().color = UIKit.PanelColor;
        panel.gameObject.AddComponent<Outline>().effectColor = new Color(1f, 1f, 1f, 0.15f);

        var title = UIKit.AddLabel(panel, "НАСТРОЙКИ", 52, FontStyle.Bold, Color.white, 76f);
        UIKit.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -84f), new Vector2(-24f, -16f), new Vector2(0.5f, 1f));

        // Tabs.
        var tabsRow = UIKit.NewUI("Tabs", panel);
        UIKit.Place(tabsRow, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -132f), new Vector2(-24f, -92f), new Vector2(0.5f, 1f));
        var tabsLayout = tabsRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        tabsLayout.spacing = 10f;
        tabsLayout.childControlWidth = true;
        tabsLayout.childControlHeight = true;
        tabsLayout.childForceExpandWidth = true;
        tabsLayout.childForceExpandHeight = true;

        tabButtons = new Button[3];
        tabButtons[0] = UIKit.AddButton(tabsRow, "Графика", UIKit.Blue, () => ShowTab(0), 40f, 26);
        tabButtons[1] = UIKit.AddButton(tabsRow, "Аудио", UIKit.Gray, () => ShowTab(1), 40f, 26);
        tabButtons[2] = UIKit.AddButton(tabsRow, "Игра", UIKit.Gray, () => ShowTab(2), 40f, 26);

        var contentArea = UIKit.NewUI("Content", panel);
        UIKit.Place(contentArea, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(24f, 96f), new Vector2(-24f, -140f), new Vector2(0.5f, 0.5f));

        tabContents = new RectTransform[3];
        tabContents[0] = BuildGraphicsTab(contentArea);
        tabContents[1] = BuildAudioTab(contentArea);
        tabContents[2] = BuildGameTab(contentArea);

        var back = UIKit.AddButton(panel, "Назад", UIKit.Gray, Close, 64f, 30);
        UIKit.Place((RectTransform)back.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-150f, 20f), new Vector2(150f, 84f), new Vector2(0.5f, 0f));

        ShowTab(0);
    }

    RectTransform BuildGraphicsTab(Transform parent)
    {
        var root = UIKit.NewUI("Graphics", parent);
        UIKit.Stretch(root);
        var layout = root.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 14f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        UIKit.AddLabel(root, "Разрешение экрана", 28, FontStyle.Bold, Color.white, 36f, TextAnchor.MiddleLeft);

        // A dropdown: the button always shows the current resolution, a click unfolds the list under it.
        resolutionButton = UIKit.AddButton(root, "", UIKit.Gray, ToggleResolutionList, 64f, 28);
        var arrow = UIKit.AddLabel(resolutionButton.transform, ">", 34, FontStyle.Bold, Color.white, 40f);
        resolutionArrow = arrow.rectTransform;
        UIKit.Place(resolutionArrow, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-60f, -20f), new Vector2(-20f, 20f), new Vector2(0.5f, 0.5f));

        fullscreenButton = UIKit.AddButton(root, "Полноэкранный режим", UIKit.Blue, ToggleFullscreen, 64f, 28);
        fpsButton = UIKit.AddButton(root, "Показывать FPS", UIKit.Blue, ToggleFps, 64f, 28);

        // The unfolded list floats over the buttons below it (last sibling = drawn on top, ignored by the layout).
        resolutionList = UIKit.NewUI("ResolutionList", root);
        resolutionList.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        UIKit.Place(resolutionList, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -394f), new Vector2(0f, -118f), new Vector2(0.5f, 1f));
        resolutionList.gameObject.AddComponent<Image>().color = new Color(0.07f, 0.09f, 0.14f, 1f);
        resolutionList.gameObject.AddComponent<Outline>().effectColor = new Color(1f, 1f, 1f, 0.2f);
        BuildResolutionScroll(resolutionList);
        SetResolutionListOpen(false);

        return root;
    }

    void ToggleResolutionList() => SetResolutionListOpen(!resolutionList.gameObject.activeSelf);

    void SetResolutionListOpen(bool open)
    {
        resolutionList.gameObject.SetActive(open);
        resolutionArrow.localRotation = Quaternion.Euler(0f, 0f, open ? 90f : -90f);
        UpdateResolutionLabel();
    }

    void UpdateResolutionLabel() =>
        UIKit.SetButtonText(resolutionButton, Screen.width + " × " + Screen.height);

    void ChooseResolution(int width, int height)
    {
        SetResolution(width, height);
        SetResolutionListOpen(false);
        shownWidth = -1;   // Screen.width only catches up next frame; Update() then shows what was really applied
    }

    void BuildResolutionScroll(RectTransform parent)
    {
        var viewport = UIKit.NewUI("Viewport", parent);
        UIKit.Stretch(viewport);
        viewport.offsetMin = new Vector2(4f, 4f);
        viewport.offsetMax = new Vector2(-20f, -4f);
        viewport.gameObject.AddComponent<RectMask2D>();

        var content = UIKit.NewUI("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = content.offsetMax = Vector2.zero;

        var list = content.gameObject.AddComponent<VerticalLayoutGroup>();
        list.spacing = 6f;
        list.childAlignment = TextAnchor.UpperCenter;
        list.childControlWidth = true;
        list.childControlHeight = true;
        list.childForceExpandWidth = true;
        list.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var seen = new HashSet<long>();
        foreach (var resolution in Screen.resolutions)
        {
            long key = (long)resolution.width * 100000 + resolution.height;
            if (!seen.Add(key)) continue;

            int w = resolution.width, h = resolution.height;
            UIKit.AddButton(content, w + " × " + h, UIKit.Gray, () => ChooseResolution(w, h), 52f, 26);
        }

        var barRect = UIKit.NewUI("Scrollbar", parent);
        barRect.anchorMin = new Vector2(1f, 0f);
        barRect.anchorMax = new Vector2(1f, 1f);
        barRect.pivot = new Vector2(1f, 1f);
        barRect.offsetMin = new Vector2(-16f, 4f);
        barRect.offsetMax = new Vector2(-2f, -4f);
        barRect.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);

        var sliding = UIKit.NewUI("Sliding Area", barRect);
        UIKit.Stretch(sliding);
        var handle = UIKit.NewUI("Handle", sliding);
        UIKit.Stretch(handle);
        var handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.color = new Color(0.6f, 0.66f, 0.85f, 0.9f);

        var bar = barRect.gameObject.AddComponent<Scrollbar>();
        bar.handleRect = handle;
        bar.targetGraphic = handleImage;
        bar.direction = Scrollbar.Direction.BottomToTop;

        var scroll = parent.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;
        scroll.verticalScrollbar = bar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
    }

    RectTransform BuildAudioTab(Transform parent)
    {
        var root = UIKit.NewUI("Audio", parent);
        UIKit.Stretch(root);

        var label = UIKit.AddLabel(root, "Настройки звука скоро появятся здесь.", 26, FontStyle.Normal, new Color(1f, 1f, 1f, 0.5f), 40f);
        UIKit.Place(label.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -60f), new Vector2(0f, -10f), new Vector2(0.5f, 1f));

        return root;
    }

    RectTransform BuildGameTab(Transform parent)
    {
        var root = UIKit.NewUI("Game", parent);
        UIKit.Stretch(root);
        var layout = root.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 10f;
        layout.padding = new RectOffset(0, 0, 10, 0);
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        UIKit.AddLabel(root, "Угол обзора камеры", 28, FontStyle.Bold, Color.white, 36f, TextAnchor.MiddleLeft);

        float fov = GameSettings.CameraFov;
        UIKit.AddSlider(root, GameSettings.MinFov, GameSettings.MaxFov, fov, OnFovChanged, 56f);
        fovValueText = UIKit.AddLabel(root, Mathf.RoundToInt(fov) + "°", 34, FontStyle.Bold, new Color(0.5f, 0.8f, 1f), 46f);

        return root;
    }
}
