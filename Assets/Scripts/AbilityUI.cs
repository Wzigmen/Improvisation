using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// The ability cards on screen.
//  - Four slots sit in the bottom-right corner (keys 1-4). They start empty; a card put into a slot is shown there.
//  - Press C (or click the "Карты" icon) to open the card panel: the mouse cursor is freed and all the cards are laid
//    out in a scrollable panel. Drag a card onto a slot to equip it (or click it to fill the first free slot), drag
//    a card from one slot to another to swap, drag it back onto the panel (or press its ×) to take it out.
// Built in code like the other menus.
public class AbilityUI : MonoBehaviour
{
    const float SlotCardScale = 0.55f;   // cards in the slots
    const float PanelCardScale = 0.9f;   // cards in the panel and under the mouse while dragging
    const float SlotGap = 8f;

    class CardView
    {
        public AbilityInfo Info;
        public RectTransform Root;
        public CanvasGroup Group;
        public Image Frame;
        public RectTransform Cooldown;    // dark shade that drains from the top as the card recharges
        public Text CooldownText;
        public Text Status;
        public RectTransform TagRoot;     // "В СЛОТЕ 2" pill on the cards of the panel
        public Text Tag;
        public int TagSlot = -2;
    }

    class SlotView
    {
        public RectTransform Root;
        public Image Background;
        public RectTransform Holder;      // the card of this slot lives in here
        public GameObject Empty;          // the "1 / пусто" look
        public GameObject Remove;         // the × button
        public CardView Card;
        public int Shown = -2;            // which card is built into the slot right now (-1 = none)
    }

    static readonly Color DashReady = new Color(0.35f, 0.85f, 1f);
    static readonly Color DashCooling = new Color(0.3f, 0.4f, 0.5f);

    RectTransform canvasRect;
    RectTransform iconButton, slotsRoot, hintLabel, panel, dragLayer, ghost;
    RectTransform dashIcon, dashShade;     // the Ctrl dash: not a card, just an icon with its cooldown
    Image dashFrame;
    Text dashShadeText;
    CanvasGroup slotsGroup, panelGroup;
    Text toast;
    float toastUntil;
    SlotView[] slots;
    CardView[] collection;

    bool open;
    float openProgress;

    // drag and drop
    int dragAbility = -1;
    int dragFromSlot = -1;
    CanvasGroup dragSource;

    PlayerController local;
    float nextLookup;

    static readonly Color SlotIdle = new Color(0.07f, 0.09f, 0.14f, 0.72f);
    static readonly Color SlotHover = new Color(0.3f, 0.42f, 0.65f, 0.92f);

    void Start()
    {
        UIKit.EnsureEventSystem();
        BuildUI();
    }

    void OnDestroy()
    {
        PauseMenu.AbilityPanelOpen = false;
    }

    void Update()
    {
        if (Time.unscaledTime >= nextLookup)
        {
            nextLookup = Time.unscaledTime + 0.5f;
            local = null;
            foreach (var p in FindObjectsByType<PlayerController>())
            {
                if (p.IsSpawned && p.IsOwner && !p.IsBot) { local = p; break; }
            }
        }

        bool inGame = NetworkGame.InGame;
        // Inside the fitting room the screen belongs to the character: no cards, no dash icon.
        bool showHud = inGame && !FittingRoom.Active;
        SetShown(iconButton, showHud);
        SetShown(slotsRoot, showHud);
        SetShown(dashIcon, showHud);
        if (showHud) UpdateDashIcon();

        // The pause menu (Esc), the fitting room and leaving the game close the panel.
        if (!showHud || PauseMenu.IsPaused) SetOpen(false);
        else if (Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame) Toggle();

        PauseMenu.AbilityPanelOpen = open;
        if (open && Cursor.lockState == CursorLockMode.Locked) PauseMenu.SetCursorCaptured(false);

        openProgress = Mathf.MoveTowards(openProgress, open ? 1f : 0f, 6f * Time.unscaledDeltaTime);
        bool panelVisible = openProgress > 0.001f;
        SetShown(panel, panelVisible);
        if (panelVisible)
        {
            float ease = 1f - (1f - openProgress) * (1f - openProgress);
            panelGroup.alpha = ease;
            panelGroup.blocksRaycasts = open;
            panel.anchoredPosition = new Vector2(0f, 250f - (1f - ease) * 70f);
        }

        var abilities = local != null ? local.Abilities : null;
        UpdateSlots(abilities, showHud);
        if (panelVisible) UpdateCollection(abilities);

        if (toast.gameObject.activeSelf && Time.unscaledTime > toastUntil) toast.gameObject.SetActive(false);
    }

    void Toggle() => SetOpen(!open);

    void SetOpen(bool value)
    {
        if (open == value) return;
        open = value;

        if (open)
        {
            PauseMenu.SetCursorCaptured(false);
        }
        else
        {
            CancelDrag();
            // Give the mouse back to the game unless another menu (or the pause menu) still needs it.
            if (NetworkGame.InGame && !PauseMenu.IsPaused && !PauseMenu.UiWantsCursor)
                PauseMenu.SetCursorCaptured(true);
        }
    }

    static void SetShown(Component c, bool shown)
    {
        if (c.gameObject.activeSelf != shown) c.gameObject.SetActive(shown);
    }

    // ---- the dash icon ------------------------------------------------------------------------

    void UpdateDashIcon()
    {
        float left = local != null ? local.DashCooldownLeft : 0f;
        bool cooling = left > 0.01f;

        SetShown(dashShade, cooling);
        if (cooling)
        {
            dashShade.anchorMin = new Vector2(0f, 1f - Mathf.Clamp01(left / PlayerController.DashCooldown));
            dashShadeText.text = left.ToString("0.0");
        }
        dashFrame.color = cooling ? DashCooling : DashReady;
    }

    // A small square above the "Карты" button: speed lines and a double arrow pointing forward, with "Ctrl" under it.
    void BuildDashIcon(Transform parent)
    {
        dashIcon = UIKit.NewUI("DashIcon", parent);
        dashIcon.anchorMin = dashIcon.anchorMax = new Vector2(1f, 0f);
        dashIcon.pivot = new Vector2(1f, 0f);
        dashIcon.anchoredPosition = new Vector2(-36f, 156f);
        dashIcon.sizeDelta = new Vector2(96f, 96f);
        dashFrame = dashIcon.gameObject.AddComponent<Image>();
        dashFrame.color = DashReady;

        var body = UIKit.NewUI("Body", dashIcon);
        UIKit.Stretch(body);
        body.offsetMin = new Vector2(4f, 4f);
        body.offsetMax = new Vector2(-4f, -4f);
        body.gameObject.AddComponent<Image>().color = new Color(0.09f, 0.11f, 0.17f, 0.96f);

        // speed lines
        var lineColor = new Color(0.75f, 0.92f, 1f, 0.85f);
        Bar(body, new Vector2(-24f, 15f), new Vector2(26f, 7f), 0f, lineColor);
        Bar(body, new Vector2(-20f, 3f), new Vector2(34f, 7f), 0f, lineColor);
        Bar(body, new Vector2(-24f, -9f), new Vector2(26f, 7f), 0f, lineColor);

        // two arrow heads (the front one bright, the one behind it fainter)
        Chevron(body, 32f, new Color(1f, 1f, 1f, 1f));
        Chevron(body, 10f, new Color(0.6f, 0.85f, 1f, 0.6f));

        var key = UIKit.AddLabel(body, "Ctrl", 20, FontStyle.Bold, new Color(1f, 1f, 1f, 0.85f), 24f);
        Place(key.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 3f), new Vector2(0f, 27f), new Vector2(0.5f, 0f));

        // cooldown: a dark shade draining from the top, with the seconds in the middle
        dashShade = UIKit.NewUI("Cooldown", body);
        dashShade.anchorMin = Vector2.zero;
        dashShade.anchorMax = Vector2.one;
        dashShade.offsetMin = dashShade.offsetMax = Vector2.zero;
        dashShade.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.82f);
        dashShadeText = UIKit.AddLabel(dashShade, "", 34, FontStyle.Bold, Color.white, 40f);
        var textRect = dashShadeText.rectTransform;
        textRect.anchorMin = textRect.anchorMax = textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.sizeDelta = new Vector2(90f, 44f);
        textRect.anchoredPosition = Vector2.zero;
        dashShade.gameObject.SetActive(false);

        dashIcon.gameObject.SetActive(false);
    }

    // A ">" made of two bars, its tip `tipX` pixels right of the middle of the icon.
    static void Chevron(RectTransform parent, float tipX, Color color)
    {
        const float dx = 22f, dy = 20f;   // how far each arm reaches back and up from the tip
        float length = Mathf.Sqrt(dx * dx + dy * dy) + 5f;
        float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
        Bar(parent, new Vector2(tipX - dx / 2f, dy / 2f), new Vector2(length, 9f), -angle, color);
        Bar(parent, new Vector2(tipX - dx / 2f, -dy / 2f), new Vector2(length, 9f), angle, color);
    }

    static void Bar(RectTransform parent, Vector2 position, Vector2 size, float angle, Color color)
    {
        var bar = UIKit.NewUI("Bar", parent);
        bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0.5f, 0.5f);
        bar.anchoredPosition = position + new Vector2(0f, 6f);   // a little above the middle, "Ctrl" sits below
        bar.sizeDelta = size;
        bar.localRotation = Quaternion.Euler(0f, 0f, angle);
        var image = bar.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }

    // ---- per frame: slots and cards -----------------------------------------------------------

    void UpdateSlots(PlayerAbilities abilities, bool inGame)
    {
        slotsGroup.blocksRaycasts = open;
        SetShown(hintLabel, inGame && !open && AbilityLoadout.AllEmpty());

        Vector2 mouse = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;

        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            int ability = AbilityLoadout.Get(i);
            if (slot.Shown != ability) RebuildSlot(slot, ability, i);

            if (slot.Remove.activeSelf != (ability >= 0 && open && dragAbility < 0))
                slot.Remove.SetActive(ability >= 0 && open && dragAbility < 0);

            // The slot under a dragged card lights up.
            bool hover = dragAbility >= 0 && RectTransformUtility.RectangleContainsScreenPoint(slot.Root, mouse, null);
            slot.Background.color = hover ? SlotHover : SlotIdle;

            if (slot.Card != null) UpdateCard(slot.Card, abilities);
        }
    }

    void UpdateCollection(PlayerAbilities abilities)
    {
        foreach (var card in collection)
        {
            UpdateCard(card, abilities);

            int slot = AbilityLoadout.SlotOf(card.Info.Id);
            if (card.TagSlot != slot)
            {
                card.TagSlot = slot;
                card.TagRoot.gameObject.SetActive(slot >= 0);
                if (slot >= 0) card.Tag.text = "В СЛОТЕ " + (slot + 1);
            }
        }
    }

    static void UpdateCard(CardView card, PlayerAbilities abilities)
    {
        float left = abilities != null ? abilities.CooldownLeft(card.Info.Id) : 0f;
        bool active = abilities != null && abilities.IsActive(card.Info.Id);

        bool cooling = left > 0.01f && !active;
        SetShown(card.Cooldown, cooling);
        if (cooling)
        {
            float fraction = Mathf.Clamp01(left / card.Info.Cooldown);
            card.Cooldown.anchorMin = new Vector2(0f, 1f - fraction);
            card.CooldownText.text = left.ToString("0.0");
        }

        SetShown(card.Status, active);
        Color frame = card.Info.Color;
        if (active) frame = Color.Lerp(card.Info.Color, Color.white, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8f));
        card.Frame.color = frame;
    }

    void RebuildSlot(SlotView slot, int ability, int index)
    {
        if (slot.Card != null) Destroy(slot.Card.Root.gameObject);
        slot.Card = null;
        slot.Shown = ability;
        slot.Empty.SetActive(ability < 0);
        if (ability < 0) return;

        var card = BuildCard(slot.Holder, AbilityCatalog.All[ability], SlotCardScale, index + 1, false);
        UIKit.Stretch(card.Root);
        card.Root.gameObject.AddComponent<AbilityCardHandle>().Init(this, ability, index, card.Group);
        slot.Card = card;
        slot.Remove.transform.SetAsLastSibling(); // the × stays on top of the card
    }

    // ---- drag and drop ------------------------------------------------------------------------

    public void BeginDrag(int ability, int fromSlot, CanvasGroup source)
    {
        if (!open || dragAbility >= 0) return;

        dragAbility = ability;
        dragFromSlot = fromSlot;
        dragSource = source;
        if (dragSource != null) dragSource.alpha = 0.35f;

        var view = BuildCard(dragLayer, AbilityCatalog.All[ability], PanelCardScale, 0, true);
        view.Root.anchorMin = view.Root.anchorMax = view.Root.pivot = new Vector2(0.5f, 0.5f);
        view.Root.localRotation = Quaternion.Euler(0f, 0f, 3f);
        view.Group.alpha = 0.92f;
        view.Group.blocksRaycasts = false; // so the drop lands on what is under the card
        view.Group.interactable = false;
        view.TagRoot.gameObject.SetActive(false);
        ghost = view.Root;
    }

    public void MoveGhost(PointerEventData e)
    {
        if (ghost == null) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, e.position, null, out Vector2 local);
        ghost.anchoredPosition = local;
    }

    public void EndDrag() => CancelDrag();

    void CancelDrag()
    {
        if (ghost != null) Destroy(ghost.gameObject);
        ghost = null;
        if (dragSource != null) dragSource.alpha = 1f;
        dragSource = null;
        dragAbility = -1;
        dragFromSlot = -1;
    }

    // slot >= 0: a card was dropped on that slot; slot -1: it was dropped on the panel.
    public void Drop(int slot)
    {
        if (dragAbility < 0) return;

        if (slot >= 0) AbilityLoadout.Set(slot, dragAbility);
        else if (dragFromSlot >= 0) AbilityLoadout.Clear(dragFromSlot);
    }

    // Clicking a card in the panel: into the first free slot, or out of its slot if it is already equipped.
    public void ClickCard(int ability, int fromSlot)
    {
        if (!open || fromSlot >= 0) return;

        int equipped = AbilityLoadout.SlotOf(ability);
        if (equipped >= 0)
        {
            AbilityLoadout.Clear(equipped);
            return;
        }

        int free = AbilityLoadout.FirstEmpty();
        if (free >= 0)
        {
            AbilityLoadout.Set(free, ability);
            return;
        }

        toast.text = "Все слоты заняты: перетащите карточку прямо на нужный слот.";
        toast.gameObject.SetActive(true);
        toastUntil = Time.unscaledTime + 3f;
    }

    // ---- building the UI ----------------------------------------------------------------------

    void BuildUI()
    {
        var canvas = UIKit.CreateCanvas(transform, "AbilityCanvas", 70);
        canvasRect = (RectTransform)canvas.transform;

        BuildSlots(canvas.transform);
        BuildPanel(canvas.transform);
        BuildIcon(canvas.transform);
        BuildDashIcon(canvas.transform);

        // The card under the mouse is drawn on top of everything else.
        dragLayer = UIKit.NewUI("DragLayer", canvas.transform);
        UIKit.Stretch(dragLayer);
    }

    void BuildSlots(Transform parent)
    {
        float w = 224f * SlotCardScale;
        float h = 330f * SlotCardScale;

        slotsRoot = UIKit.NewUI("Slots", parent);
        slotsRoot.anchorMin = slotsRoot.anchorMax = new Vector2(1f, 0f);
        slotsRoot.pivot = new Vector2(1f, 0f);
        slotsRoot.anchoredPosition = new Vector2(-158f, 24f);
        slotsRoot.sizeDelta = new Vector2(AbilityLoadout.SlotCount * w + (AbilityLoadout.SlotCount - 1) * SlotGap, h);
        slotsGroup = slotsRoot.gameObject.AddComponent<CanvasGroup>();

        slots = new SlotView[AbilityLoadout.SlotCount];
        for (int i = 0; i < slots.Length; i++)
            slots[i] = BuildSlot(slotsRoot, i, w, h);

        // A hint above the slots while nothing is equipped yet.
        var hint = UIKit.AddLabel(parent, "C — выбрать способности", 26, FontStyle.Bold, new Color(1f, 1f, 1f, 0.9f), 36f, TextAnchor.LowerRight);
        hintLabel = hint.rectTransform;
        hintLabel.anchorMin = hintLabel.anchorMax = new Vector2(1f, 0f);
        hintLabel.pivot = new Vector2(1f, 0f);
        hintLabel.anchoredPosition = new Vector2(-158f, 24f + h + 8f);
        hintLabel.sizeDelta = new Vector2(560f, 36f);
        var shadow = hint.gameObject.AddComponent<Outline>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
        shadow.effectDistance = new Vector2(2f, -2f);
        hintLabel.gameObject.SetActive(false);
    }

    SlotView BuildSlot(RectTransform parent, int index, float w, float h)
    {
        var slot = new SlotView();

        slot.Root = UIKit.NewUI("Slot" + (index + 1), parent);
        slot.Root.anchorMin = slot.Root.anchorMax = slot.Root.pivot = new Vector2(0f, 0f);
        slot.Root.anchoredPosition = new Vector2(index * (w + SlotGap), 0f);
        slot.Root.sizeDelta = new Vector2(w, h);

        slot.Background = slot.Root.gameObject.AddComponent<Image>();
        slot.Background.color = SlotIdle;
        var border = slot.Root.gameObject.AddComponent<Outline>();
        border.effectColor = new Color(1f, 1f, 1f, 0.3f);
        border.effectDistance = new Vector2(2f, -2f);
        slot.Root.gameObject.AddComponent<AbilityDropTarget>().Init(this, index);

        // The empty look: a big number and "пусто".
        var empty = UIKit.AddLabel(slot.Root, (index + 1) + "\n<size=22>пусто</size>", 64, FontStyle.Bold, new Color(1f, 1f, 1f, 0.32f), 100f);
        UIKit.Stretch(empty.rectTransform);
        slot.Empty = empty.gameObject;

        slot.Holder = UIKit.NewUI("Holder", slot.Root);
        UIKit.Stretch(slot.Holder);

        // × to take the card out (only while the panel is open); in the bottom corner so it never covers the name.
        var remove = UIKit.NewUI("Remove", slot.Root);
        remove.anchorMin = remove.anchorMax = remove.pivot = new Vector2(1f, 0f);
        remove.anchoredPosition = new Vector2(-4f, 4f);
        remove.sizeDelta = new Vector2(28f, 28f);
        var removeImage = remove.gameObject.AddComponent<Image>();
        removeImage.color = Color.white;
        var button = remove.gameObject.AddComponent<Button>();
        button.targetGraphic = removeImage;
        var colors = button.colors;
        colors.normalColor = new Color(0.75f, 0.25f, 0.25f, 0.95f);
        colors.highlightedColor = new Color(0.95f, 0.35f, 0.35f, 1f);
        colors.pressedColor = new Color(0.55f, 0.15f, 0.15f, 1f);
        colors.selectedColor = colors.normalColor;
        button.colors = colors;
        button.onClick.AddListener(() => AbilityLoadout.Clear(index));
        var cross = UIKit.AddLabel(remove, "×", 26, FontStyle.Bold, Color.white, 30f);
        UIKit.Stretch(cross.rectTransform);
        slot.Remove = remove.gameObject;
        slot.Remove.SetActive(false);

        return slot;
    }

    void BuildIcon(Transform parent)
    {
        iconButton = UIKit.NewUI("CardsButton", parent);
        iconButton.anchorMin = iconButton.anchorMax = new Vector2(1f, 0f);
        iconButton.pivot = new Vector2(1f, 0f);
        iconButton.anchoredPosition = new Vector2(-24f, 24f);
        iconButton.sizeDelta = new Vector2(120f, 120f);

        var background = iconButton.gameObject.AddComponent<Image>();
        background.color = Color.white;
        var button = iconButton.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        var colors = button.colors;
        colors.normalColor = new Color(0.12f, 0.15f, 0.22f, 0.92f);
        colors.highlightedColor = new Color(0.2f, 0.25f, 0.36f, 0.96f);
        colors.pressedColor = new Color(0.08f, 0.1f, 0.15f, 1f);
        colors.selectedColor = colors.normalColor;
        button.colors = colors;
        button.onClick.AddListener(Toggle);

        // Two little cards fanned out.
        MiniCard(iconButton, new Vector2(-14f, 8f), 14f, new Color(0.88f, 0.9f, 0.96f));
        MiniCard(iconButton, new Vector2(14f, 12f), -12f, new Color(0.98f, 0.75f, 0.25f));

        var label = UIKit.AddLabel(iconButton, "Карты", 24, FontStyle.Bold, Color.white, 30f);
        var labelRect = label.rectTransform;
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(1f, 0f);
        labelRect.pivot = new Vector2(0.5f, 0f);
        labelRect.anchoredPosition = new Vector2(0f, 6f);
        labelRect.sizeDelta = new Vector2(0f, 32f);

        var hint = UIKit.AddLabel(iconButton, "C", 22, FontStyle.Bold, new Color(1f, 1f, 1f, 0.55f), 28f, TextAnchor.UpperRight);
        var hintRect = hint.rectTransform;
        hintRect.anchorMin = new Vector2(0f, 1f);
        hintRect.anchorMax = new Vector2(1f, 1f);
        hintRect.pivot = new Vector2(0.5f, 1f);
        hintRect.anchoredPosition = new Vector2(-8f, -4f);
        hintRect.sizeDelta = new Vector2(0f, 28f);

        iconButton.gameObject.SetActive(false);
    }

    static void MiniCard(RectTransform parent, Vector2 position, float angle, Color color)
    {
        var card = UIKit.NewUI("MiniCard", parent);
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
        card.anchoredPosition = position;
        card.sizeDelta = new Vector2(46f, 66f);
        card.localRotation = Quaternion.Euler(0f, 0f, angle);
        card.gameObject.AddComponent<Image>().color = color;

        var inner = UIKit.NewUI("Inner", card);
        UIKit.Stretch(inner);
        inner.offsetMin = new Vector2(4f, 4f);
        inner.offsetMax = new Vector2(-4f, -4f);
        inner.gameObject.AddComponent<Image>().color = new Color(color.r * 0.55f, color.g * 0.55f, color.b * 0.6f, 1f);
    }

    // The panel with every card in a scrollable grid.
    void BuildPanel(Transform parent)
    {
        panel = UIKit.NewUI("Panel", parent);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0f);
        panel.pivot = new Vector2(0.5f, 0f);
        panel.anchoredPosition = new Vector2(0f, 250f);   // above the slots
        panel.sizeDelta = new Vector2(1380f, 600f);
        panel.gameObject.AddComponent<Image>().color = new Color(0.09f, 0.11f, 0.18f, 0.96f);
        panel.gameObject.AddComponent<Outline>().effectColor = new Color(1f, 1f, 1f, 0.15f);
        panelGroup = panel.gameObject.AddComponent<CanvasGroup>();
        // Dropping a card from a slot anywhere on the panel takes it out of the slot.
        panel.gameObject.AddComponent<AbilityDropTarget>().Init(this, -1);

        var title = UIKit.AddLabel(panel, "Способности", 40, FontStyle.Bold, Color.white, 46f, TextAnchor.MiddleLeft);
        Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -56f), new Vector2(-24f, -10f), new Vector2(0.5f, 1f));

        var hint = UIKit.AddLabel(panel,
            "Перетащите карточку на слот справа внизу (клик по карточке — в первый свободный слот). C — закрыть.",
            22, FontStyle.Normal, new Color(1f, 1f, 1f, 0.65f), 28f, TextAnchor.MiddleLeft);
        Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -88f), new Vector2(-24f, -56f), new Vector2(0.5f, 1f));

        toast = UIKit.AddLabel(panel, "", 24, FontStyle.Bold, new Color(1f, 0.85f, 0.35f), 30f, TextAnchor.MiddleRight);
        Place(toast.rectTransform, new Vector2(0.35f, 1f), new Vector2(1f, 1f), new Vector2(0f, -56f), new Vector2(-24f, -14f), new Vector2(0.5f, 1f));
        toast.gameObject.SetActive(false);

        // scroll area
        var scrollRect = UIKit.NewUI("Scroll", panel);
        scrollRect.anchorMin = Vector2.zero;
        scrollRect.anchorMax = Vector2.one;
        scrollRect.offsetMin = new Vector2(20f, 20f);
        scrollRect.offsetMax = new Vector2(-44f, -96f);
        scrollRect.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.18f);

        var viewport = UIKit.NewUI("Viewport", scrollRect);
        UIKit.Stretch(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();

        var content = UIKit.NewUI("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = content.offsetMax = Vector2.zero;
        // An invisible image so the empty gaps between cards can also be grabbed to scroll.
        content.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

        var grid = content.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(224f * PanelCardScale, 330f * PanelCardScale);
        grid.spacing = new Vector2(16f, 16f);
        grid.padding = new RectOffset(12, 12, 12, 12);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 6;
        grid.childAlignment = TextAnchor.UpperCenter;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        collection = new CardView[AbilityCatalog.All.Length];
        for (int i = 0; i < collection.Length; i++)
        {
            var view = BuildCard(content, AbilityCatalog.All[i], PanelCardScale, 0, true);
            view.Root.gameObject.AddComponent<AbilityCardHandle>().Init(this, i, -1, view.Group);
            collection[i] = view;
        }

        // scrollbar
        var barRect = UIKit.NewUI("Scrollbar", panel);
        barRect.anchorMin = new Vector2(1f, 0f);
        barRect.anchorMax = new Vector2(1f, 1f);
        barRect.pivot = new Vector2(1f, 1f);
        barRect.offsetMin = new Vector2(-34f, 20f);
        barRect.offsetMax = new Vector2(-14f, -96f);
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

        var scroll = scrollRect.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;
        scroll.verticalScrollbar = bar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

        panel.gameObject.SetActive(false);
    }

    // One card. `slotNumber` > 0 puts the key badge on it (cards in the slots); `collection` cards show the description
    // and the "В СЛОТЕ" tag instead.
    static CardView BuildCard(Transform parent, AbilityInfo info, float k, int slotNumber, bool collection)
    {
        var view = new CardView { Info = info };
        int F(float size) => Mathf.Max(6, Mathf.RoundToInt(size * k));
        Vector2 V(float x, float y) => new Vector2(x * k, y * k);

        view.Root = UIKit.NewUI("Card " + info.Name, parent);
        view.Root.sizeDelta = new Vector2(224f * k, 330f * k);
        view.Group = view.Root.gameObject.AddComponent<CanvasGroup>();

        var frameRect = UIKit.NewUI("Frame", view.Root);
        UIKit.Stretch(frameRect);
        view.Frame = frameRect.gameObject.AddComponent<Image>();
        view.Frame.color = info.Color;

        var body = UIKit.NewUI("Body", frameRect);
        UIKit.Stretch(body);
        body.offsetMin = V(6f, 6f);
        body.offsetMax = V(-6f, -6f);
        body.gameObject.AddComponent<Image>().color = new Color(0.09f, 0.11f, 0.17f, 0.98f);

        // title (long names shrink to fit instead of spilling over the frame)
        var title = UIKit.AddLabel(body, info.Name, F(27), FontStyle.Bold, info.Color, 50f * k);
        Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), V(10f, -62f), V(-10f, -8f), new Vector2(0.5f, 1f));
        title.resizeTextForBestFit = true;
        title.resizeTextMinSize = F(16);
        title.resizeTextMaxSize = F(27);
        title.horizontalOverflow = HorizontalWrapMode.Wrap;
        title.verticalOverflow = VerticalWrapMode.Truncate;

        // key badge (cards in the slots) or the "in slot" tag (cards in the panel)
        if (slotNumber > 0)
        {
            var badge = UIKit.NewUI("KeyBadge", body);
            badge.anchorMin = badge.anchorMax = badge.pivot = new Vector2(0f, 1f);
            badge.anchoredPosition = V(10f, -62f);
            badge.sizeDelta = V(46f, 40f);
            badge.gameObject.AddComponent<Image>().color = info.Color;
            var key = UIKit.AddLabel(badge, slotNumber.ToString(), F(24), FontStyle.Bold, new Color(0.08f, 0.08f, 0.12f), 40f * k);
            UIKit.Stretch(key.rectTransform);
        }

        view.TagRoot = UIKit.NewUI("SlotTag", body);
        view.TagRoot.anchorMin = view.TagRoot.anchorMax = view.TagRoot.pivot = new Vector2(0.5f, 1f);
        view.TagRoot.anchoredPosition = V(0f, -62f);
        view.TagRoot.sizeDelta = V(150f, 36f);
        view.TagRoot.gameObject.AddComponent<Image>().color = new Color(0.25f, 0.6f, 0.35f, 0.95f);
        view.Tag = UIKit.AddLabel(view.TagRoot, "", F(20), FontStyle.Bold, Color.white, 36f * k);
        UIKit.Stretch(view.Tag.rectTransform);
        view.TagRoot.gameObject.SetActive(false);

        // the big number
        var big = UIKit.AddLabel(body, info.BigText, F(84), FontStyle.Bold, Color.white, 100f * k);
        if (collection) Place(big.rectTransform, new Vector2(0f, 0.38f), new Vector2(1f, 0.70f), Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        else Place(big.rectTransform, new Vector2(0f, 0.30f), new Vector2(1f, 0.64f), Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        // A Text whose line is taller than its box draws nothing, so the size is fitted to the box instead.
        big.resizeTextForBestFit = true;
        big.resizeTextMinSize = F(30);
        big.resizeTextMaxSize = F(76);
        big.horizontalOverflow = HorizontalWrapMode.Overflow;
        big.verticalOverflow = VerticalWrapMode.Overflow;
        var bigOutline = big.gameObject.AddComponent<Outline>();
        bigOutline.effectColor = new Color(info.Color.r * 0.5f, info.Color.g * 0.5f, info.Color.b * 0.5f, 0.9f);
        bigOutline.effectDistance = new Vector2(3f, -3f) * k;

        var caption = UIKit.AddLabel(body, info.BigCaption, F(21), FontStyle.Bold, info.Color, 30f * k);
        if (collection) Place(caption.rectTransform, new Vector2(0f, 0.30f), new Vector2(1f, 0.39f), Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        else Place(caption.rectTransform, new Vector2(0f, 0.12f), new Vector2(1f, 0.30f), V(4f, 0f), V(-4f, 0f), new Vector2(0.5f, 0.5f));
        caption.resizeTextForBestFit = true;
        caption.resizeTextMinSize = F(12);
        caption.resizeTextMaxSize = F(21);
        caption.horizontalOverflow = HorizontalWrapMode.Wrap;
        caption.verticalOverflow = VerticalWrapMode.Overflow;

        // what it does (only in the panel)
        if (collection)
        {
            var description = UIKit.AddLabel(body, info.Description, F(20), FontStyle.Normal, new Color(1f, 1f, 1f, 0.88f), 110f * k, TextAnchor.UpperCenter);
            Place(description.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.30f), V(10f, 6f), V(-10f, -2f), new Vector2(0.5f, 0.5f));
            description.resizeTextForBestFit = true;
            description.resizeTextMinSize = F(13);
            description.resizeTextMaxSize = F(20);
        }

        // "active" marker (thorns / dance / shield / rage while they last)
        view.Status = UIKit.AddLabel(body, "АКТИВНО", F(26), FontStyle.Bold, new Color(0.5f, 1f, 0.55f), 36f * k);
        Place(view.Status.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), V(0f, 10f), V(0f, 50f), new Vector2(0.5f, 0f));
        view.Status.resizeTextForBestFit = true;
        view.Status.resizeTextMinSize = F(12);
        view.Status.resizeTextMaxSize = F(26);
        view.Status.gameObject.SetActive(false);

        // cooldown: a dark shade that drains from the top, with the seconds left in the middle
        view.Cooldown = UIKit.NewUI("Cooldown", body);
        view.Cooldown.anchorMin = new Vector2(0f, 0f);
        view.Cooldown.anchorMax = new Vector2(1f, 1f);
        view.Cooldown.offsetMin = view.Cooldown.offsetMax = Vector2.zero;
        var shade = view.Cooldown.gameObject.AddComponent<Image>();
        shade.color = new Color(0f, 0f, 0f, 0.86f); // dark enough that the seconds read clearly over the big number
        shade.raycastTarget = false; // the card underneath stays draggable while it recharges
        view.CooldownText = UIKit.AddLabel(view.Cooldown, "", F(72), FontStyle.Bold, Color.white, 90f * k);
        var cdRect = view.CooldownText.rectTransform;
        cdRect.anchorMin = cdRect.anchorMax = cdRect.pivot = new Vector2(0.5f, 0.5f);
        cdRect.sizeDelta = V(200f, 100f);
        cdRect.anchoredPosition = Vector2.zero;
        view.Cooldown.gameObject.SetActive(false);

        return view;
    }

    static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Vector2 pivot)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }
}

// Put on every card: dragging it picks it up, a click without dragging equips / unequips it.
public class AbilityCardHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
{
    AbilityUI ui;
    int ability;
    int slot;               // -1 for the cards of the panel
    CanvasGroup group;

    public void Init(AbilityUI owner, int abilityNumber, int slotIndex, CanvasGroup cardGroup)
    {
        ui = owner;
        ability = abilityNumber;
        slot = slotIndex;
        group = cardGroup;
    }

    public void OnBeginDrag(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Left) ui.BeginDrag(ability, slot, group);
    }

    public void OnDrag(PointerEventData e) => ui.MoveGhost(e);
    public void OnEndDrag(PointerEventData e) => ui.EndDrag();

    public void OnPointerClick(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Left) ui.ClickCard(ability, slot);
    }
}

// Put on the four slots (slot = 0..3) and on the panel itself (slot = -1): the place a dragged card is dropped.
public class AbilityDropTarget : MonoBehaviour, IDropHandler
{
    AbilityUI ui;
    int slot;

    public void Init(AbilityUI owner, int slotIndex)
    {
        ui = owner;
        slot = slotIndex;
    }

    public void OnDrop(PointerEventData e) => ui.Drop(slot);
}
