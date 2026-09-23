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

    static Sprite circleSprite;

    // A soft-edged white circle, generated once and reused: tint it via Image.color to get a coloured dot of any
    // size (item icons, and anywhere else a round UI shape is handy).
    public static Sprite Circle()
    {
        if (circleSprite != null) return circleSprite;

        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float radius = size / 2f - 1.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                float a = Mathf.Clamp01(radius - d + 1.5f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        circleSprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        return circleSprite;
    }

    public static Image AddCircle(Transform parent, string name, Color color)
    {
        var rt = NewUI(name, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = Circle();
        img.color = color;
        return img;
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

    // Pins a RectTransform by anchors + offsets in one call, for menus laid out by hand instead of a layout group.
    public static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Vector2 pivot)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
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

    // Recolours a button (its highlighted/pressed/selected shades are derived from `color`) - handy for tab
    // buttons where the active one needs to stand out.
    public static void SetButtonColors(Button button, Color color)
    {
        var colors = button.colors;
        colors.normalColor = color;
        colors.highlightedColor = Color.Lerp(color, Color.white, 0.2f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = Color.Lerp(color, Color.black, 0.25f);
        button.colors = colors;
    }

    // A standard horizontal slider (background, fill, round handle) between minValue and maxValue.
    public static Slider AddSlider(Transform parent, float minValue, float maxValue, float value,
        UnityAction<float> onChange, float height = 56f)
    {
        var root = NewUI("Slider", parent);
        root.gameObject.AddComponent<LayoutElement>().preferredHeight = height;

        var background = NewUI("Background", root);
        background.anchorMin = new Vector2(0f, 0.35f);
        background.anchorMax = new Vector2(1f, 0.65f);
        background.offsetMin = background.offsetMax = Vector2.zero;
        background.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.15f);

        var fillArea = NewUI("Fill Area", root);
        fillArea.anchorMin = new Vector2(0f, 0.35f);
        fillArea.anchorMax = new Vector2(1f, 0.65f);
        fillArea.offsetMin = new Vector2(10f, 0f);
        fillArea.offsetMax = new Vector2(-10f, 0f);

        var fill = NewUI("Fill", fillArea);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = new Vector2(0.5f, 1f);
        fill.sizeDelta = Vector2.zero;
        fill.gameObject.AddComponent<Image>().color = Blue;

        // A slide area with no height of its own (a line through the middle): the slider stretches the handle to the
        // area's height, so with a zero height the handle is exactly as tall as its sizeDelta - a round knob.
        var handleArea = NewUI("Handle Slide Area", root);
        handleArea.anchorMin = new Vector2(0f, 0.5f);
        handleArea.anchorMax = new Vector2(1f, 0.5f);
        handleArea.offsetMin = new Vector2(10f, 0f);
        handleArea.offsetMax = new Vector2(-10f, 0f);

        var handle = AddCircle(handleArea, "Handle", Color.white);
        handle.rectTransform.sizeDelta = new Vector2(height * 0.8f, height * 0.8f);

        var slider = root.gameObject.AddComponent<Slider>();
        slider.fillRect = fill;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = minValue;
        slider.maxValue = maxValue;
        slider.value = value;
        if (onChange != null) slider.onValueChanged.AddListener(onChange);
        return slider;
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
