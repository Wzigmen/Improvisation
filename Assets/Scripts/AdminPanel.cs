using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// What the admin sliders are set to. Only the host (or the solo player) can move them, and they only change their
// own character (PlayerController.AdminMultiplier); they start at 1 and go back to 1 when the game is left.
public static class AdminSettings
{
    public const float Min = 0.5f, Max = 5f;
    public static float MoveSpeed = 1f, AttackSpeed = 1f, JumpHeight = 1f;

    public static void Reset()
    {
        MoveSpeed = AttackSpeed = JumpHeight = 1f;
    }
}

// Put on any button or slider: while the mouse is over it, the admin panel shows `Text` in a little tooltip.
public class AdminHoverTip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public static AdminHoverTip Hovered { get; private set; }
    public string Text;

    public void OnPointerEnter(PointerEventData eventData) => Hovered = this;
    public void OnPointerExit(PointerEventData eventData)
    {
        if (Hovered == this) Hovered = null;
    }

    void OnDisable()
    {
        if (Hovered == this) Hovered = null;
    }
}

// The admin panel, for whoever runs the game (the host, or the person playing alone). "Ё" (the key left of 1) slides
// it out on the left, under the FPS counter, where a hint says so.
//   Игроки    - unfolds the list of players; every other player has a round button that teleports them to the
//               admin and a cross that kicks them off the server
//   Sliders   - walking speed, attack speed and jump height of the admin's own character
// Every button says what it does when the mouse is over it.
public class AdminPanel : MonoBehaviour
{
    const float PanelWidth = 470f;

    GameObject root;
    RectTransform canvasRect, panel, playersList, tip;
    Text hint, tipText, playersLabel;
    Slider moveSlider, attackSlider, jumpSlider;
    Text moveValue, attackValue, jumpValue;

    bool open;
    bool listOpen = true;
    float progress;
    float nextRefresh;
    string shownPlayers = "";

    static bool Available =>
        NetworkGame.InGame && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

    void Awake()
    {
        UIKit.EnsureEventSystem();
        BuildUI();
    }

    void OnDestroy()
    {
        PauseMenu.AdminOpen = false;
        AdminSettings.Reset();
    }

    void Update()
    {
        bool available = Available;
        hint.gameObject.SetActive(available && !FittingRoom.Active);

        if (!available)
        {
            if (open) SetOpen(false);
            AdminSettings.Reset();
        }
        else if (Keyboard.current != null && Keyboard.current.backquoteKey.wasPressedThisFrame &&
                 !PauseMenu.IsPaused && !FittingRoom.Active)
        {
            SetOpen(!open);
        }

        if (open && (PauseMenu.IsPaused || FittingRoom.Active)) SetOpen(false);

        PauseMenu.AdminOpen = open;
        if (open && Cursor.lockState == CursorLockMode.Locked) PauseMenu.SetCursorCaptured(false);

        // Slide in from the left edge and back.
        progress = Mathf.MoveTowards(progress, open ? 1f : 0f, 5f * Time.unscaledDeltaTime);
        bool visible = progress > 0.001f;
        if (panel.gameObject.activeSelf != visible) panel.gameObject.SetActive(visible);
        if (visible)
        {
            float eased = 1f - (1f - progress) * (1f - progress);
            panel.anchoredPosition = new Vector2(Mathf.Lerp(-PanelWidth - 30f, 16f, eased), panel.anchoredPosition.y);
        }

        if (open && Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + 0.4f;
            RefreshPlayers();
        }

        UpdateTip();
    }

    void SetOpen(bool value)
    {
        if (open == value) return;
        open = value;

        if (open)
        {
            SyncSliders();
            shownPlayers = "";   // force the list to be rebuilt
            nextRefresh = 0f;
            PauseMenu.SetCursorCaptured(false);
        }
        else
        {
            // Give the mouse back to the game unless another menu still needs it.
            if (NetworkGame.InGame && !PauseMenu.IsPaused && !PauseMenu.UiWantsCursor && !PauseMenu.AbilityPanelOpen && !PauseMenu.CustomizerOpen)
                PauseMenu.SetCursorCaptured(true);
        }
    }

    // ---- players ------------------------------------------------------------------------------------

    static PlayerController LocalPlayer()
    {
        foreach (var p in MatchManager.ActivePlayers(false, false))
            if (p.IsOwner) return p;
        return null;
    }

    void RefreshPlayers()
    {
        var players = MatchManager.ActivePlayers(false, false);
        players.Sort((a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));

        string signature = listOpen ? "open" : "closed";
        foreach (var p in players) signature += "|" + p.OwnerClientId + ":" + p.DisplayName + ":" + p.IsOwner;
        playersLabel.text = "Игроки: " + players.Count + (listOpen ? "  (скрыть)" : "  (показать)");
        if (signature == shownPlayers) return;
        shownPlayers = signature;

        playersList.gameObject.SetActive(listOpen);
        for (int i = playersList.childCount - 1; i >= 0; i--)
            Destroy(playersList.GetChild(i).gameObject);
        if (!listOpen) return;

        foreach (var p in players) BuildRow(p);
    }

    void BuildRow(PlayerController target)
    {
        var row = UIKit.NewUI("Player " + target.DisplayName, playersList);
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 54f;
        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        bool isAdmin = target.IsOwner;
        var name = UIKit.AddLabel(row, target.DisplayName + (isAdmin ? "  (вы)" : ""), 28, FontStyle.Normal,
            isAdmin ? new Color(0.6f, 0.9f, 0.6f) : Color.white, 54f, TextAnchor.MiddleLeft);
        name.horizontalOverflow = HorizontalWrapMode.Overflow;
        name.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1f;

        if (isAdmin) return;   // there is nothing to do to yourself

        ulong clientId = target.OwnerClientId;
        RoundButton(row, new Color(0.65f, 0.4f, 0.95f), "Телепортировать игрока ко мне", () => Summon(clientId), teleport: true);
        RoundButton(row, new Color(0.85f, 0.25f, 0.25f), "Выгнать игрока с сервера", () => Kick(clientId), teleport: false);
    }

    // A round button. The teleport one has a little target on it, the kick one a cross.
    static void RoundButton(Transform parent, Color color, string tip, UnityEngine.Events.UnityAction onClick, bool teleport)
    {
        var image = UIKit.AddCircle(parent, teleport ? "Teleport" : "Kick", Color.white);
        var layoutElement = image.gameObject.AddComponent<LayoutElement>();
        layoutElement.preferredWidth = layoutElement.preferredHeight = 46f;

        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        UIKit.SetButtonColors(button, color);
        button.onClick.AddListener(onClick);
        image.gameObject.AddComponent<AdminHoverTip>().Text = tip;

        if (teleport)
        {
            Dot(image.transform, 26f, Color.white);
            Dot(image.transform, 16f, color);
            Dot(image.transform, 7f, Color.white);
        }
        else
        {
            var cross = UIKit.AddLabel(image.transform, "×", 40, FontStyle.Bold, Color.white, 46f);
            UIKit.Stretch(cross.rectTransform);
        }
    }

    static void Dot(Transform parent, float diameter, Color color)
    {
        var dot = UIKit.AddCircle(parent, "Dot", color);
        dot.rectTransform.sizeDelta = new Vector2(diameter, diameter);
        dot.raycastTarget = false;
    }

    // Brings the player to a spot two metres in front of the admin, facing the admin.
    static void Summon(ulong clientId)
    {
        var admin = LocalPlayer();
        if (admin == null) return;

        foreach (var p in MatchManager.ActivePlayers(false, false))
        {
            if (p.OwnerClientId != clientId || p == admin) continue;
            Vector3 flatForward = admin.transform.forward;
            flatForward.y = 0f;
            flatForward = flatForward.sqrMagnitude > 0.01f ? flatForward.normalized : Vector3.forward;
            p.ServerSummon(admin.transform.position + flatForward * 2f + Vector3.up * 0.1f,
                Quaternion.LookRotation(-flatForward, Vector3.up).eulerAngles.y);
            return;
        }
    }

    static void Kick(ulong clientId)
    {
        var manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsServer || clientId == manager.LocalClientId) return;
        manager.DisconnectClient(clientId, "Вас выгнал администратор.");
    }

    void TogglePlayers()
    {
        listOpen = !listOpen;
        shownPlayers = "";
        nextRefresh = 0f;
    }

    // ---- sliders ----------------------------------------------------------------------------------

    void SyncSliders()
    {
        moveSlider.SetValueWithoutNotify(AdminSettings.MoveSpeed);
        attackSlider.SetValueWithoutNotify(AdminSettings.AttackSpeed);
        jumpSlider.SetValueWithoutNotify(AdminSettings.JumpHeight);
        moveValue.text = Format(AdminSettings.MoveSpeed);
        attackValue.text = Format(AdminSettings.AttackSpeed);
        jumpValue.text = Format(AdminSettings.JumpHeight);
    }

    static string Format(float value) => "×" + value.ToString("0.0");

    // ---- tooltip ----------------------------------------------------------------------------------

    void UpdateTip()
    {
        var hovered = AdminHoverTip.Hovered;
        bool show = open && hovered != null && !string.IsNullOrEmpty(hovered.Text) && Mouse.current != null;
        if (tip.gameObject.activeSelf != show) tip.gameObject.SetActive(show);
        if (!show) return;

        tipText.text = hovered.Text;
        float width = tipText.preferredWidth + 28f;
        tip.sizeDelta = new Vector2(width, 44f);

        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, Mouse.current.position.ReadValue(), null, out Vector2 local);
        Vector2 half = canvasRect.rect.size * 0.5f;
        float x = Mathf.Min(local.x + 22f, half.x - width - 6f);
        float y = Mathf.Max(local.y - 26f, -half.y + 50f);
        tip.anchoredPosition = new Vector2(x, y);
    }

    // ---- building the UI --------------------------------------------------------------------------

    void BuildUI()
    {
        var canvas = UIKit.CreateCanvas(transform, "AdminCanvas", 72);
        root = canvas.gameObject;
        canvasRect = (RectTransform)canvas.transform;

        // The reminder under the FPS counter.
        var hintRect = UIKit.NewUI("Hint", canvas.transform);
        hintRect.anchorMin = hintRect.anchorMax = new Vector2(0f, 1f);
        hintRect.pivot = new Vector2(0f, 1f);
        hintRect.anchoredPosition = new Vector2(16f, -56f);
        hintRect.sizeDelta = new Vector2(360f, 34f);
        hint = hintRect.gameObject.AddComponent<Text>();
        hint.font = UIKit.Font;
        hint.fontSize = 24;
        hint.color = new Color(1f, 1f, 1f, 0.85f);
        hint.alignment = TextAnchor.UpperLeft;
        hint.raycastTarget = false;
        hint.text = "(админка на «ё»)";
        var outline = hintRect.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(2f, -2f);
        hint.gameObject.SetActive(false);

        // The panel: a column that grows with its contents, hanging from the top-left corner.
        panel = UIKit.NewUI("Panel", canvas.transform);
        panel.anchorMin = panel.anchorMax = new Vector2(0f, 1f);
        panel.pivot = new Vector2(0f, 1f);
        panel.anchoredPosition = new Vector2(-PanelWidth - 30f, -100f);
        panel.sizeDelta = new Vector2(PanelWidth, 100f);
        panel.gameObject.AddComponent<Image>().color = new Color(0.1f, 0.13f, 0.2f, 0.96f);
        panel.gameObject.AddComponent<Outline>().effectColor = new Color(1f, 1f, 1f, 0.15f);
        var column = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        column.padding = new RectOffset(20, 20, 16, 20);
        column.spacing = 10f;
        column.childAlignment = TextAnchor.UpperCenter;
        column.childControlWidth = true;
        column.childControlHeight = true;
        column.childForceExpandWidth = true;
        column.childForceExpandHeight = false;
        panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        UIKit.AddLabel(panel, "АДМИНКА", 34, FontStyle.Bold, Color.white, 44f);

        var playersButton = UIKit.AddButton(panel, "Игроки", UIKit.Blue, TogglePlayers, 56f, 28);
        playersLabel = playersButton.GetComponentInChildren<Text>();
        playersButton.gameObject.AddComponent<AdminHoverTip>().Text = "Показать или скрыть список игроков";

        playersList = UIKit.AddList(panel, "Players", 6f);

        moveSlider = AddSliderRow("Скорость передвижения", "Насколько быстрее вы ходите (×1 — как обычно)", v =>
        {
            AdminSettings.MoveSpeed = v;
            moveValue.text = Format(v);
        }, out moveValue);
        attackSlider = AddSliderRow("Скорость атаки", "Насколько чаще вы бьёте кулаком", v =>
        {
            AdminSettings.AttackSpeed = v;
            attackValue.text = Format(v);
        }, out attackValue);
        jumpSlider = AddSliderRow("Высота прыжка", "Во сколько раз выше вы прыгаете", v =>
        {
            AdminSettings.JumpHeight = v;
            jumpValue.text = Format(v);
        }, out jumpValue);

        // The tooltip: last, so it draws over everything.
        tip = UIKit.NewUI("Tip", canvas.transform);
        tip.anchorMin = tip.anchorMax = new Vector2(0.5f, 0.5f);
        tip.pivot = new Vector2(0f, 1f);
        tip.sizeDelta = new Vector2(300f, 44f);
        var tipImage = tip.gameObject.AddComponent<Image>();
        tipImage.color = new Color(0.02f, 0.03f, 0.06f, 0.95f);
        tipImage.raycastTarget = false;
        tip.gameObject.AddComponent<Outline>().effectColor = new Color(1f, 1f, 1f, 0.25f);
        var tipTextRect = UIKit.NewUI("Text", tip);
        UIKit.Stretch(tipTextRect);
        tipText = tipTextRect.gameObject.AddComponent<Text>();
        tipText.font = UIKit.Font;
        tipText.fontSize = 24;
        tipText.color = Color.white;
        tipText.alignment = TextAnchor.MiddleCenter;
        tipText.horizontalOverflow = HorizontalWrapMode.Overflow;
        tipText.raycastTarget = false;
        tip.gameObject.SetActive(false);

        panel.gameObject.SetActive(false);
    }

    Slider AddSliderRow(string title, string tipText, UnityEngine.Events.UnityAction<float> onChange, out Text valueLabel)
    {
        var header = UIKit.NewUI("SliderHeader", panel);
        header.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
        header.gameObject.AddComponent<Image>().color = Color.clear;   // so the mouse over the title finds the tooltip
        var layout = header.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var label = UIKit.AddLabel(header, title, 26, FontStyle.Bold, Color.white, 34f, TextAnchor.MiddleLeft);
        label.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1f;
        valueLabel = UIKit.AddLabel(header, "×1.0", 26, FontStyle.Bold, new Color(0.5f, 0.8f, 1f), 34f, TextAnchor.MiddleRight);
        valueLabel.gameObject.GetComponent<LayoutElement>().preferredWidth = 90f;

        var slider = UIKit.AddSlider(panel, AdminSettings.Min, AdminSettings.Max, 1f, onChange, 46f);
        header.gameObject.AddComponent<AdminHoverTip>().Text = tipText;
        slider.gameObject.AddComponent<AdminHoverTip>().Text = tipText;
        return slider;
    }
}
