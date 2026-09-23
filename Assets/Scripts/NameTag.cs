using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;

// The nickname of the person at this computer. It is typed in the start menu, remembered between sessions and
// sent to everybody when the character spawns. Only letters, digits, space and _ - . are allowed, so it can never
// smuggle rich-text tags into the chat-like lists that show names.
public static class PlayerNickname
{
    public const int MinLength = 2;
    public const int MaxLength = 16;
    const string PrefsKey = "Nickname";

    public static string Current
    {
        get
        {
            try { return Clean(PlayerPrefs.GetString(PrefsKey, "")); }
            catch (System.Exception) { return ""; }
        }
    }

    public static bool IsAllowedChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' || c == ' ';

    // Throws out forbidden characters, trims the ends and cuts the name to its maximum length.
    public static string Clean(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var sb = new System.Text.StringBuilder(text.Length);
        foreach (char c in text)
            if (IsAllowedChar(c)) sb.Append(c);

        string clean = sb.ToString().Trim();
        if (clean.Length > MaxLength) clean = clean.Substring(0, MaxLength).TrimEnd();
        return clean;
    }

    public static bool IsValid(string text) => Clean(text).Length >= MinLength;

    // Saves the nickname if it is long enough.
    public static bool Set(string text)
    {
        string clean = Clean(text);
        if (clean.Length < MinLength) return false;

        try
        {
            PlayerPrefs.SetString(PrefsKey, clean);
            PlayerPrefs.Save();
        }
        catch (System.Exception)
        {
            // Not being able to remember it isn't worth an error: it still works for this session.
        }
        return true;
    }

    public static void Clear()
    {
        try
        {
            PlayerPrefs.DeleteKey(PrefsKey);
            PlayerPrefs.Save();
        }
        catch (System.Exception)
        {
        }
    }

    // A name that fits into the network string (letters outside the basic alphabets take more bytes).
    public static FixedString64Bytes ToNetwork(string text)
    {
        string clean = Clean(text);
        while (true)
        {
            var fixedString = new FixedString64Bytes();
            if (fixedString.Append(clean) == FormatError.None) return fixedString;
            clean = clean.Substring(0, clean.Length - 1);
        }
    }
}

// The nickname floating above a character, in the colour of that character. It is built in code when the
// character spawns and always turns to face the camera, like the health bar.
[RequireComponent(typeof(PlayerController))]
public class NameTag : MonoBehaviour
{
    const float BaseScale = 0.01f;         // the canvas is scaled down (px -> m); never assign a plain 1 here
    const float HeightNoBar = 2.35f;       // above the head
    const float HeightWithBar = 2.95f;     // above the health bar of a fighter

    PlayerController player;
    Transform canvasTransform;
    Text label;
    string shownName;
    ulong shownColorId = ulong.MaxValue;

    void Awake()
    {
        player = GetComponent<PlayerController>();

        var canvasObject = new GameObject("NameTag", typeof(Canvas));
        canvasTransform = canvasObject.transform;
        canvasTransform.SetParent(transform, false);
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        ((RectTransform)canvasTransform).sizeDelta = new Vector2(420f, 64f);
        canvasTransform.localScale = Vector3.one * BaseScale;

        var text = UIKit.NewUI("Name", canvasTransform);
        UIKit.Stretch(text);
        label = text.gameObject.AddComponent<Text>();
        label.font = UIKit.Font;
        label.fontSize = 46;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.supportRichText = false;
        label.raycastTarget = false;

        var outline = text.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        outline.effectDistance = new Vector2(3f, -3f);
    }

    void LateUpdate()
    {
        if (!player.IsSpawned) return;

        // Nobody sees a character behind the curtain of the fitting room, not even its name; and the person in
        // there sees the character without a name tag in the way.
        bool hidden = player.IsOwner ? FittingRoom.Active : player.IsCustomizing;
        if (canvasTransform.gameObject.activeSelf == hidden) canvasTransform.gameObject.SetActive(!hidden);
        if (hidden) return;

        string name = player.DisplayName;
        if (name != shownName)
        {
            shownName = name;
            label.text = name;
        }
        if (player.ColorId != shownColorId)
        {
            shownColorId = player.ColorId;
            label.color = PlayerAppearance.ColorFor(shownColorId);
        }

        bool bar = player.InMatch && player.Health > 0;
        canvasTransform.position = transform.position + Vector3.up * (bar ? HeightWithBar : HeightNoBar);

        var cam = Camera.main;
        if (cam == null) return;

        canvasTransform.rotation = Quaternion.LookRotation(canvasTransform.position - cam.transform.position);

        // Names of far-away characters grow a little so they stay readable.
        float distance = Vector3.Distance(cam.transform.position, canvasTransform.position);
        canvasTransform.localScale = Vector3.one * (BaseScale * Mathf.Clamp(distance / 9.5f, 0.75f, 2.6f));
    }
}
