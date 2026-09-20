using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// The ability cards on screen: a little "cards" icon in the bottom-right corner. Click it (hold Alt to use the mouse,
// or press C) and the five cards slide up in a row along the bottom of the screen. Each card shows what it does,
// which key uses it, and its cooldown. Built in code like the other menus.
public class AbilityUI : MonoBehaviour
{
    class CardView
    {
        public AbilityInfo Info;
        public RectTransform Visual;      // moved for the "dealing" animation
        public Image Frame;
        public RectTransform Cooldown;    // dark shade that drains from the top as the card recharges
        public Text CooldownText;
        public Text Status;
    }

    RectTransform iconButton;
    RectTransform tray;
    CanvasGroup trayGroup;
    CardView[] cards;
    bool open;
    float openProgress;

    PlayerController local;
    float nextLookup;

    void Start()
    {
        UIKit.EnsureEventSystem();
        BuildUI();
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
        SetShown(iconButton, inGame);

        if (inGame && !PauseMenu.IsPaused && Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame)
            Toggle();
        if (!inGame) open = false;

        openProgress = Mathf.MoveTowards(openProgress, open ? 1f : 0f, 5f * Time.unscaledDeltaTime);
        SetShown(tray, openProgress > 0.001f);
        if (openProgress <= 0.001f) return;

        trayGroup.alpha = Mathf.Clamp01(openProgress * 2f);
        var abilities = local != null ? local.Abilities : null;

        for (int i = 0; i < cards.Length; i++)
        {
            var card = cards[i];

            // Cards are dealt one after another: each starts a little later than the one before.
            float p = Mathf.Clamp01(openProgress * 1.6f - i * 0.15f);
            float ease = 1f - (1f - p) * (1f - p) * (1f - p);
            card.Visual.anchoredPosition = new Vector2(0f, -(1f - ease) * 420f);

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

            SetShown(card.Status.rectTransform, active);
            Color frame = card.Info.Color;
            if (active) frame = Color.Lerp(card.Info.Color, Color.white, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8f));
            card.Frame.color = frame;
        }
    }

    void Toggle() => open = !open;

    static void SetShown(RectTransform rt, bool shown)
    {
        if (rt.gameObject.activeSelf != shown) rt.gameObject.SetActive(shown);
    }

    // ---- building the UI ----------------------------------------------------------------------

    void BuildUI()
    {
        var canvas = UIKit.CreateCanvas(transform, "AbilityCanvas", 70);
        BuildIcon(canvas.transform);
        BuildTray(canvas.transform);
    }

    void BuildIcon(Transform parent)
    {
        iconButton = UIKit.NewUI("CardsButton", parent);
        iconButton.anchorMin = iconButton.anchorMax = new Vector2(1f, 0f);
        iconButton.pivot = new Vector2(1f, 0f);
        iconButton.anchoredPosition = new Vector2(-32f, 32f);
        iconButton.sizeDelta = new Vector2(150f, 150f);

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
        MiniCard(iconButton, new Vector2(-18f, 6f), 14f, new Color(0.88f, 0.9f, 0.96f));
        MiniCard(iconButton, new Vector2(18f, 10f), -12f, new Color(0.98f, 0.75f, 0.25f));

        var label = UIKit.AddLabel(iconButton, "Карты", 28, FontStyle.Bold, Color.white, 34f);
        var labelRect = label.rectTransform;
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(1f, 0f);
        labelRect.pivot = new Vector2(0.5f, 0f);
        labelRect.anchoredPosition = new Vector2(0f, 8f);
        labelRect.sizeDelta = new Vector2(0f, 36f);

        var hint = UIKit.AddLabel(iconButton, "C", 24, FontStyle.Bold, new Color(1f, 1f, 1f, 0.55f), 30f, TextAnchor.UpperRight);
        var hintRect = hint.rectTransform;
        hintRect.anchorMin = new Vector2(0f, 1f);
        hintRect.anchorMax = new Vector2(1f, 1f);
        hintRect.pivot = new Vector2(0.5f, 1f);
        hintRect.anchoredPosition = new Vector2(-10f, -6f);
        hintRect.sizeDelta = new Vector2(0f, 30f);

        iconButton.gameObject.SetActive(false);
    }

    static void MiniCard(RectTransform parent, Vector2 position, float angle, Color color)
    {
        var card = UIKit.NewUI("MiniCard", parent);
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
        card.anchoredPosition = position;
        card.sizeDelta = new Vector2(58f, 82f);
        card.localRotation = Quaternion.Euler(0f, 0f, angle);
        card.gameObject.AddComponent<Image>().color = color;

        var inner = UIKit.NewUI("Inner", card);
        UIKit.Stretch(inner);
        inner.offsetMin = new Vector2(5f, 5f);
        inner.offsetMax = new Vector2(-5f, -5f);
        inner.gameObject.AddComponent<Image>().color = new Color(color.r * 0.55f, color.g * 0.55f, color.b * 0.6f, 1f);
    }

    void BuildTray(Transform parent)
    {
        tray = UIKit.NewUI("Tray", parent);
        tray.anchorMin = tray.anchorMax = new Vector2(0.5f, 0f);
        tray.pivot = new Vector2(0.5f, 0f);
        tray.anchoredPosition = new Vector2(0f, 190f);   // above the fight health bar
        tray.sizeDelta = new Vector2(1260f, 350f);
        trayGroup = tray.gameObject.AddComponent<CanvasGroup>();
        trayGroup.blocksRaycasts = false; // clicks go through the gaps; the cards are for looking at

        var row = tray.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 22f;
        row.childAlignment = TextAnchor.MiddleCenter;
        row.childControlWidth = false;
        row.childControlHeight = false;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;

        cards = new CardView[AbilityCatalog.All.Length];
        for (int i = 0; i < cards.Length; i++)
            cards[i] = BuildCard(tray, AbilityCatalog.All[i]);

        tray.gameObject.SetActive(false);
    }

    static CardView BuildCard(Transform parent, AbilityInfo info)
    {
        var view = new CardView { Info = info };

        // The slot keeps its place in the row; the visual inside it is what slides.
        var slot = UIKit.NewUI("Card", parent);
        slot.sizeDelta = new Vector2(224f, 330f);

        view.Visual = UIKit.NewUI("Visual", slot);
        UIKit.Stretch(view.Visual);

        var frameRect = UIKit.NewUI("Frame", view.Visual);
        UIKit.Stretch(frameRect);
        view.Frame = frameRect.gameObject.AddComponent<Image>();
        view.Frame.color = info.Color;

        var body = UIKit.NewUI("Body", frameRect);
        UIKit.Stretch(body);
        body.offsetMin = new Vector2(6f, 6f);
        body.offsetMax = new Vector2(-6f, -6f);
        body.gameObject.AddComponent<Image>().color = new Color(0.09f, 0.11f, 0.17f, 0.98f);

        // title
        var title = UIKit.AddLabel(body, info.Name, 27, FontStyle.Bold, info.Color, 50f);
        // Pinned to the top edge: the offsets are the bottom (-62) and top (-8) of the text box below the card's top.
        Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -62f), new Vector2(0f, -8f), new Vector2(0.5f, 1f));
        // Long names shrink to fit instead of spilling over the frame.
        title.resizeTextForBestFit = true;
        title.resizeTextMinSize = 16;
        title.resizeTextMaxSize = 27;
        title.horizontalOverflow = HorizontalWrapMode.Wrap;
        title.verticalOverflow = VerticalWrapMode.Truncate;
        var titleRect = title.rectTransform;
        titleRect.offsetMin = new Vector2(10f, titleRect.offsetMin.y);
        titleRect.offsetMax = new Vector2(-10f, titleRect.offsetMax.y);

        // key badge
        var badge = UIKit.NewUI("KeyBadge", body);
        badge.anchorMin = badge.anchorMax = badge.pivot = new Vector2(0f, 1f);
        badge.anchoredPosition = new Vector2(10f, -62f);
        badge.sizeDelta = new Vector2(info.Key.Length > 1 ? 74f : 46f, 40f);
        badge.gameObject.AddComponent<Image>().color = info.Color;
        var key = UIKit.AddLabel(badge, info.Key, 24, FontStyle.Bold, new Color(0.08f, 0.08f, 0.12f), 40f);
        UIKit.Stretch(key.rectTransform);

        // the big number
        var big = UIKit.AddLabel(body, info.BigText, 84, FontStyle.Bold, Color.white, 100f);
        Place(big.rectTransform, new Vector2(0f, 0.38f), new Vector2(1f, 0.70f), Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        // A Text whose line is taller than its box draws nothing, so the size is fitted to the box instead.
        big.resizeTextForBestFit = true;
        big.resizeTextMinSize = 30;
        big.resizeTextMaxSize = 76;
        big.horizontalOverflow = HorizontalWrapMode.Overflow;
        big.verticalOverflow = VerticalWrapMode.Overflow;
        var bigOutline = big.gameObject.AddComponent<Outline>();
        bigOutline.effectColor = new Color(info.Color.r * 0.5f, info.Color.g * 0.5f, info.Color.b * 0.5f, 0.9f);
        bigOutline.effectDistance = new Vector2(3f, -3f);

        var caption = UIKit.AddLabel(body, info.BigCaption, 21, FontStyle.Bold, info.Color, 30f);
        Place(caption.rectTransform, new Vector2(0f, 0.30f), new Vector2(1f, 0.39f), Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        caption.horizontalOverflow = HorizontalWrapMode.Overflow;
        caption.verticalOverflow = VerticalWrapMode.Overflow;

        // what it does
        var description = UIKit.AddLabel(body, info.Description, 20, FontStyle.Normal, new Color(1f, 1f, 1f, 0.88f), 110f, TextAnchor.UpperCenter);
        Place(description.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.30f), new Vector2(10f, 6f), new Vector2(-10f, -2f), new Vector2(0.5f, 0.5f));
        description.resizeTextForBestFit = true;
        description.resizeTextMinSize = 13;
        description.resizeTextMaxSize = 20;

        // "active" marker (thorns / dance while they last)
        view.Status = UIKit.AddLabel(body, "АКТИВНО", 26, FontStyle.Bold, new Color(0.5f, 1f, 0.55f), 36f);
        Place(view.Status.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 10f), new Vector2(0f, 50f), new Vector2(0.5f, 0f));
        view.Status.gameObject.SetActive(false);

        // cooldown: a dark shade that drains from the top, with the seconds left in the middle
        view.Cooldown = UIKit.NewUI("Cooldown", body);
        view.Cooldown.anchorMin = new Vector2(0f, 0f);
        view.Cooldown.anchorMax = new Vector2(1f, 1f);
        view.Cooldown.offsetMin = view.Cooldown.offsetMax = Vector2.zero;
        view.Cooldown.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);
        view.CooldownText = UIKit.AddLabel(view.Cooldown, "", 72, FontStyle.Bold, Color.white, 90f);
        var cdRect = view.CooldownText.rectTransform;
        cdRect.anchorMin = cdRect.anchorMax = cdRect.pivot = new Vector2(0.5f, 0.5f);
        cdRect.sizeDelta = new Vector2(200f, 100f);
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
