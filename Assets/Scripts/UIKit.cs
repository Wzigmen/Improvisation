using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// Small helpers for building uGUI menus in code (no prefabs or scene wiring needed).
public static class UIKit
{
    static Font font;
    public static Font Font
    {
        get
        {
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return font;
        }
    }

    public static readonly Color PanelColor = new Color(0.12f, 0.15f, 0.22f, 0.97f);
    public static readonly Color Green = new Color(0.25f, 0.6f, 0.35f);
    public static readonly Color Blue = new Color(0.25f, 0.45f, 0.85f);
    public static readonly Color Red = new Color(0.75f, 0.3f, 0.3f);
    public static readonly Color Gray = new Color(0.35f, 0.4f, 0.5f);

    public static void EnsureEventSystem()
    {
        if (Object.FindAnyObjectByType<EventSystem>() != null) return;

        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
    }

    public static Canvas CreateCanvas(Transform parent, string name, int sortingOrder)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    public static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    public static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    // Full-screen dimmer.
    public static RectTransform AddDim(Transform parent, float alpha)
    {
        var dim = NewUI("Dim", parent);
        Stretch(dim);
        dim.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, alpha);
        return dim;
    }

    // Centered panel that stacks its children vertically.
    public static RectTransform AddPanel(Transform parent, string name, Vector2 size)
    {
        var panel = NewUI(name, parent);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = size;
        panel.gameObject.AddComponent<Image>().color = PanelColor;
        var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(48, 48, 36, 36);
        layout.spacing = 18f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        return panel;
    }

    // Plain vertical container (no background) for lists.
    public static RectTransform AddList(Transform parent, string name, float spacing)
    {
        var list = NewUI(name, parent);
        var layout = list.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        return list;
    }

    public static Text AddLabel(Transform parent, string text, int size, FontStyle style, Color color, float height,
        TextAnchor alignment = TextAnchor.MiddleCenter)
    {
        var rt = NewUI("Label", parent);
        var label = rt.gameObject.AddComponent<Text>();
        label.font = Font;
        label.text = text;
        label.fontSize = size;
        label.fontStyle = style;
        label.color = color;
        label.alignment = alignment;
        label.raycastTarget = false;
        rt.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        return label;
    }

    public static Button AddButton(Transform parent, string text, Color color, UnityAction onClick,
        float height = 84f, int fontSize = 36)
    {
        var rt = NewUI(text, parent);
        var image = rt.gameObject.AddComponent<Image>();
        image.color = Color.white;
        rt.gameObject.AddComponent<LayoutElement>().preferredHeight = height;

        var button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        var colors = button.colors;
        colors.normalColor = color;
        colors.highlightedColor = Color.Lerp(color, Color.white, 0.25f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = Color.Lerp(color, Color.black, 0.25f);
        colors.disabledColor = new Color(color.r, color.g, color.b, 0.4f);
        button.colors = colors;
        button.onClick.AddListener(onClick);

        var labelRt = NewUI("Text", rt);
        Stretch(labelRt);
        var label = labelRt.gameObject.AddComponent<Text>();
        label.font = Font;
        label.text = text;
        label.fontSize = fontSize;
        label.color = Color.white;
        label.alignment = TextAnchor.MiddleCenter;
        label.raycastTarget = false;
        return button;
    }

    public static void SetButtonText(Button button, string text)
    {
        var label = button.GetComponentInChildren<Text>();
        if (label != null) label.text = text;
    }

    public static InputField AddInputField(Transform parent, string placeholder, float height = 76f)
    {
        var rt = NewUI("InputField", parent);
        var background = rt.gameObject.AddComponent<Image>();
        background.color = new Color(1f, 1f, 1f, 0.12f);
        rt.gameObject.AddComponent<LayoutElement>().preferredHeight = height;

        var textRt = NewUI("Text", rt);
        Stretch(textRt);
        textRt.offsetMin = new Vector2(20f, 6f);
        textRt.offsetMax = new Vector2(-20f, -6f);
        var text = textRt.gameObject.AddComponent<Text>();
        text.font = Font;
        text.fontSize = 34;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleLeft;
        text.supportRichText = false;

        var phRt = NewUI("Placeholder", rt);
        Stretch(phRt);
        phRt.offsetMin = new Vector2(20f, 6f);
        phRt.offsetMax = new Vector2(-20f, -6f);
        var ph = phRt.gameObject.AddComponent<Text>();
        ph.font = Font;
        ph.fontSize = 30;
        ph.fontStyle = FontStyle.Italic;
        ph.color = new Color(1f, 1f, 1f, 0.4f);
        ph.alignment = TextAnchor.MiddleLeft;
        ph.text = placeholder;
        ph.raycastTarget = false;

        var input = rt.gameObject.AddComponent<InputField>();
        input.targetGraphic = background;
        input.textComponent = text;
        input.placeholder = ph;
        input.lineType = InputField.LineType.SingleLine;
        return input;
    }
}
