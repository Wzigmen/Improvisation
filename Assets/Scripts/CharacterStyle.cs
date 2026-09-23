using System.Collections.Generic;
using UnityEngine;

// What can be changed about the character in the fitting room: six sections with six options each
// (option 0 is always the plain, default look). The whole outfit is one number, five bits per section,
// so it can be saved in PlayerPrefs and synced over the network as a single value.
public static class CharacterStyleCatalog
{
    public const int SlotCount = 6;
    public const int Head = 0, Eyes = 1, Mouth = 2, Hands = 3, Clothes = 4, Legs = 5;

    public static readonly string[] SlotNames = { "Голова", "Глаза", "Рот", "Руки", "Одежда", "Ноги" };

    public static readonly string[][] OptionNames =
    {
        new[] { "Хохолки", "Кепка", "Цилиндр", "Корона", "Ирокез", "Шапка-бини" },
        new[] { "Сонные", "Добрые", "Тёмные очки", "Прищур", "Большие", "Красные" },
        new[] { "Ухмылка", "Улыбка", "Удивлённый", "Зубастый", "Усы", "Язык" },
        new[] { "Белые перчатки", "Голые руки", "Боксёрские перчатки", "Золотые", "Чёрные", "Шипастые" },
        new[] { "Без одежды", "Жёлтая футболка", "Синяя толстовка", "Тельняшка", "Плащ героя", "Бабочка" },
        new[] { "Белые ботинки", "Красные кроссовки", "Ролики", "Ласты", "Клоунские туфли", "Крылатые ботинки" }
    };

    // A little colour chip next to every option in the menu.
    public static readonly Color[][] Swatches =
    {
        new[] { new Color(0.5f, 0.5f, 0.55f), new Color(0.9f, 0.2f, 0.2f), new Color(0.15f, 0.15f, 0.18f), new Color(1f, 0.8f, 0.2f), new Color(0.3f, 0.9f, 0.4f), new Color(0.3f, 0.5f, 0.95f) },
        new[] { new Color(0.9f, 0.3f, 0.25f), new Color(0.4f, 0.75f, 1f), new Color(0.1f, 0.1f, 0.12f), new Color(0.75f, 0.75f, 0.8f), new Color(1f, 1f, 1f), new Color(1f, 0.1f, 0.1f) },
        new[] { new Color(0.85f, 0.85f, 0.9f), new Color(1f, 0.8f, 0.3f), new Color(1f, 0.5f, 0.5f), new Color(1f, 1f, 1f), new Color(0.45f, 0.28f, 0.15f), new Color(1f, 0.45f, 0.6f) },
        new[] { new Color(1f, 1f, 1f), new Color(0.93f, 0.76f, 0.6f), new Color(0.9f, 0.15f, 0.15f), new Color(1f, 0.8f, 0.2f), new Color(0.12f, 0.12f, 0.14f), new Color(0.65f, 0.68f, 0.75f) },
        new[] { new Color(0.5f, 0.5f, 0.55f), new Color(1f, 0.8f, 0.15f), new Color(0.2f, 0.4f, 0.9f), new Color(0.9f, 0.9f, 1f), new Color(0.5f, 0.2f, 0.8f), new Color(0.2f, 0.4f, 0.9f) },
        new[] { new Color(0.96f, 0.96f, 0.98f), new Color(0.9f, 0.15f, 0.15f), new Color(0.2f, 0.4f, 0.9f), new Color(0.2f, 0.75f, 0.4f), new Color(1f, 0.85f, 0.2f), new Color(1f, 0.8f, 0.25f) }
    };

    public static int OptionCount(int slot) => OptionNames[slot].Length;

    public static int Get(uint style, int slot) => (int)((style >> (slot * 5)) & 31u);

    public static uint Set(uint style, int slot, int option)
    {
        uint mask = 31u << (slot * 5);
        return (style & ~mask) | (((uint)option & 31u) << (slot * 5));
    }

    // Bots get a random outfit that depends only on their number, so it is the same on every machine.
    public static uint RandomFor(int seed)
    {
        var random = new System.Random(seed * 7919 + 13);
        uint style = 0;
        for (int slot = 0; slot < SlotCount; slot++)
            style = Set(style, slot, random.Next(0, OptionCount(slot)));
        return style;
    }
}

// The outfit of the person at this computer: remembered between sessions and sent to everybody at spawn.
public static class PlayerStyle
{
    const string PrefsKey = "CharacterStyle";

    public static uint Saved
    {
        get
        {
            try { return Sanitize((uint)PlayerPrefs.GetInt(PrefsKey, 0)); }
            catch (System.Exception) { return 0; }
        }
    }

    public static void Save(uint style)
    {
        try
        {
            PlayerPrefs.SetInt(PrefsKey, (int)style);
            PlayerPrefs.Save();
        }
        catch (System.Exception)
        {
            // Not being able to remember the outfit isn't worth an error.
        }
    }

    // Anything out of range becomes the plain option.
    public static uint Sanitize(uint style)
    {
        for (int slot = 0; slot < CharacterStyleCatalog.SlotCount; slot++)
        {
            if (CharacterStyleCatalog.Get(style, slot) >= CharacterStyleCatalog.OptionCount(slot))
                style = CharacterStyleCatalog.Set(style, slot, 0);
        }
        return style;
    }
}

// Dresses the character according to the outfit number that is synced by PlayerController: hats, eyes, mouth,
// gloves, clothes and shoes. Everything is built from primitives in code the moment the outfit changes, so the
// same character prefab serves every outfit. The default face (eyes, brows, smirk) is part of the prefab and
// is changed or hidden here.
[RequireComponent(typeof(PlayerController))]
public class CharacterStyle : MonoBehaviour
{
    [SerializeField] Material baseMaterial;   // a plain lit material; every colour used here is a copy of it

    static Mesh sphereMesh, cubeMesh, cylinderMesh;
    static readonly Dictionary<int, Material> materials = new Dictionary<int, Material>();

    class Saved
    {
        public Transform Transform;
        public Vector3 Position, Scale;
    }

    PlayerController player;
    Transform model, body, footL, footR, handL, handR, legL, legR, armL, armR;
    Renderer handRendererL, handRendererR, footRendererL, footRendererR;
    Renderer pupilRendererL, pupilRendererR;
    Transform eyeL, eyeR, pupilL, pupilR, lidL, lidR, cuffL, cuffR;
    Transform[] smirk, hair;
    readonly List<Saved> saved = new List<Saved>();
    Color footDefault = new Color(0.96f, 0.96f, 0.98f);
    Color pupilDefault = new Color(0.05f, 0.05f, 0.05f);

    readonly List<GameObject> spawned = new List<GameObject>();
    readonly List<Transform> wings = new List<Transform>();
    uint applied = uint.MaxValue;
    bool wasHidden;

    void Awake()
    {
        player = GetComponent<PlayerController>();

        model = transform.Find("Model");
        if (model == null) return;
        body = model.Find("Body");
        footL = model.Find("FootL");
        footR = model.Find("FootR");
        handL = model.Find("HandL");
        handR = model.Find("HandR");
        if (body == null || footL == null || footR == null || handL == null || handR == null) { model = null; return; }

        eyeL = Remember(body, "EyeL");
        eyeR = Remember(body, "EyeR");
        pupilL = Remember(body, "PupilL");
        pupilR = Remember(body, "PupilR");
        lidL = Remember(body, "LidL");
        lidR = Remember(body, "LidR");
        cuffL = Remember(handL, "GloveCuff");
        cuffR = Remember(handR, "GloveCuff");
        armL = model.Find("ArmL");
        armR = model.Find("ArmR");
        smirk = new[] { Remember(body, "SmirkA"), Remember(body, "SmirkB"), Remember(body, "SmirkC"), Remember(body, "SmirkDimple") };
        var curls = new List<Transform>();
        foreach (string side in new[] { "L", "R" })
            for (int i = 1; i <= 4; i++)
            {
                Transform curl = Remember(body, "Hair" + side + i);
                if (curl != null) curls.Add(curl);
            }
        hair = curls.ToArray();
        Remember(footL, null);
        Remember(footR, null);

        handRendererL = handL.GetComponent<Renderer>();
        handRendererR = handR.GetComponent<Renderer>();
        footRendererL = footL.GetComponent<Renderer>();
        footRendererR = footR.GetComponent<Renderer>();
        pupilRendererL = pupilL != null ? pupilL.GetComponent<Renderer>() : null;
        pupilRendererR = pupilR != null ? pupilR.GetComponent<Renderer>() : null;
        if (footRendererL != null && footRendererL.sharedMaterial != null) footDefault = footRendererL.sharedMaterial.GetColor("_BaseColor");
        if (pupilRendererL != null && pupilRendererL.sharedMaterial != null) pupilDefault = pupilRendererL.sharedMaterial.GetColor("_BaseColor");

        // Shoe accessories stay glued to the feet, which CartoonWalker moves every frame.
        legL = new GameObject("StyleLegL").transform;
        legL.SetParent(model, false);
        legR = new GameObject("StyleLegR").transform;
        legR.SetParent(model, false);
    }

    // Remembers where a default part sits, so a style can move it and the next one can put it back.
    Transform Remember(Transform parent, string childName)
    {
        Transform t = childName == null ? parent : parent.Find(childName);
        if (t != null) saved.Add(new Saved { Transform = t, Position = t.localPosition, Scale = t.localScale });
        return t;
    }

    void LateUpdate()
    {
        if (model == null || !player.IsSpawned) return;

        if (player.StyleValue != applied) Apply(player.StyleValue);

        // The shoe accessories follow the feet.
        legL.localPosition = footL.localPosition;
        legR.localPosition = footR.localPosition;

        // Wings flap.
        float flap = Mathf.Sin(Time.time * 9f) * 14f;
        for (int i = 0; i < wings.Count; i++)
        {
            if (wings[i] == null) continue;
            float baseAngle = wings[i].name.EndsWith("L") ? 28f : -28f;
            wings[i].localRotation = Quaternion.Euler(0f, 0f, baseAngle + (wings[i].name.EndsWith("L") ? flap : -flap));
        }

        // Behind the curtain of the fitting room nobody else sees the character.
        bool hidden = player.IsCustomizing && !player.IsOwner;
        if (hidden != wasHidden)
        {
            wasHidden = hidden;
            model.gameObject.SetActive(!hidden);
        }
    }

    // ---- applying an outfit -------------------------------------------------------------------

    void Apply(uint style)
    {
        applied = style;

        foreach (var go in spawned)
            if (go != null) Destroy(go);
        spawned.Clear();
        wings.Clear();

        // Put the default face and feet back before anything changes them.
        foreach (var s in saved)
        {
            if (s.Transform == null) continue;
            s.Transform.localPosition = s.Position;
            s.Transform.localScale = s.Scale;
            s.Transform.gameObject.SetActive(true);
        }

        BuildHead(CharacterStyleCatalog.Get(style, CharacterStyleCatalog.Head));
        BuildEyes(CharacterStyleCatalog.Get(style, CharacterStyleCatalog.Eyes));
        BuildMouth(CharacterStyleCatalog.Get(style, CharacterStyleCatalog.Mouth));
        BuildHands(CharacterStyleCatalog.Get(style, CharacterStyleCatalog.Hands));
        BuildClothes(CharacterStyleCatalog.Get(style, CharacterStyleCatalog.Clothes));
        BuildLegs(CharacterStyleCatalog.Get(style, CharacterStyleCatalog.Legs));
        RefreshColors();
    }

    // Hands and feet change colour with the outfit; the hands' plain colour is the player's own.
    public void RefreshColors()
    {
        if (model == null) return;

        int hands = CharacterStyleCatalog.Get(applied == uint.MaxValue ? 0u : applied, CharacterStyleCatalog.Hands);
        Color handColor = hands switch
        {
            1 => new Color(0.93f, 0.76f, 0.6f),   // bare hands: the same skin as the arms
            2 => new Color(0.9f, 0.12f, 0.12f),
            3 => new Color(1f, 0.78f, 0.2f),
            4 => new Color(0.1f, 0.1f, 0.12f),
            5 => new Color(0.6f, 0.63f, 0.7f),
            _ => new Color(0.97f, 0.97f, 1f)      // the default: white gloves
        };
        SetColor(handRendererL, handColor);
        SetColor(handRendererR, handColor);

        int legs = CharacterStyleCatalog.Get(applied == uint.MaxValue ? 0u : applied, CharacterStyleCatalog.Legs);
        Color footColor = legs switch
        {
            1 => new Color(0.9f, 0.15f, 0.15f),
            2 => new Color(0.2f, 0.4f, 0.9f),
            3 => new Color(0.2f, 0.75f, 0.4f),
            4 => new Color(1f, 0.85f, 0.2f),
            5 => new Color(1f, 0.78f, 0.25f),
            _ => footDefault
        };
        SetColor(footRendererL, footColor);
        SetColor(footRendererR, footColor);

        int eyes = CharacterStyleCatalog.Get(applied == uint.MaxValue ? 0u : applied, CharacterStyleCatalog.Eyes);
        Color pupilColor = eyes == 5 ? new Color(1f, 0.08f, 0.08f) : pupilDefault;
        SetColor(pupilRendererL, pupilColor);
        SetColor(pupilRendererR, pupilColor);
    }

    static void SetColor(Renderer r, Color color)
    {
        if (r != null) r.material.SetColor("_BaseColor", color);
    }

    // ---- head ---------------------------------------------------------------------------------

    void BuildHead(int option)
    {
        Quaternion none = Quaternion.identity;

        // The two curls of hair only show with a bare head (option 0) and the mohawk replaces them.
        foreach (var curl in hair) SetActive(curl, option == 0);

        switch (option)
        {
            case 1: // red baseball cap
            {
                Material red = Mat(0.9f, 0.15f, 0.15f);
                Part(Sphere(), "CapDome", body, new Vector3(0f, 1.2f, -0.02f), new Vector3(1.12f, 0.8f, 1.12f), red, none);
                Part(Sphere(), "CapVisor", body, new Vector3(0f, 1.12f, 0.55f), new Vector3(0.6f, 0.07f, 0.5f), Mat(0.7f, 0.1f, 0.1f), Quaternion.Euler(12f, 0f, 0f));
                Part(Sphere(), "CapButton", body, new Vector3(0f, 1.6f, -0.02f), Vector3.one * 0.1f, Mat(1f, 1f, 1f), none);
                break;
            }
            case 2: // top hat
            {
                Material black = Mat(0.08f, 0.08f, 0.1f);
                Part(Cylinder(), "HatBrim", body, new Vector3(0f, 1.3f, 0f), new Vector3(1.0f, 0.03f, 1.0f), black, none);
                Part(Cylinder(), "HatCrown", body, new Vector3(0f, 1.64f, 0f), new Vector3(0.56f, 0.34f, 0.56f), black, none);
                Part(Cylinder(), "HatBand", body, new Vector3(0f, 1.4f, 0f), new Vector3(0.575f, 0.05f, 0.575f), Mat(0.8f, 0.1f, 0.15f), none);
                break;
            }
            case 3: // gold crown
            {
                Material gold = Mat(1f, 0.78f, 0.2f, 0.85f, 0.7f);
                Part(Cylinder(), "CrownRing", body, new Vector3(0f, 1.38f, 0f), new Vector3(0.66f, 0.1f, 0.66f), gold, none);
                for (int i = 0; i < 5; i++)
                {
                    float a = i / 5f * Mathf.PI * 2f;
                    Vector3 p = new Vector3(Mathf.Sin(a) * 0.27f, 1.55f, Mathf.Cos(a) * 0.27f);
                    Part(Cube(), "CrownSpike", body, p, new Vector3(0.14f, 0.22f, 0.14f), gold, Quaternion.Euler(0f, a * Mathf.Rad2Deg, 45f) * Quaternion.Euler(45f, 0f, 0f));
                    Part(Sphere(), "CrownPearl", body, p + Vector3.up * 0.1f, Vector3.one * 0.07f, Mat(1f, 1f, 1f), none);
                }
                Part(Sphere(), "CrownGem", body, new Vector3(0f, 1.42f, 0.34f), Vector3.one * 0.1f, Mat(0.9f, 0.1f, 0.2f, 0.3f, 0.9f), none);
                break;
            }
            case 4: // mohawk
            {
                Color[] colors = { new Color(0.3f, 0.95f, 0.4f), new Color(1f, 0.4f, 0.8f), new Color(0.3f, 0.9f, 1f) };
                for (int i = 0; i < 7; i++)
                {
                    float z = Mathf.Lerp(0.34f, -0.36f, i / 6f);
                    float y = 1.3f - Mathf.Abs(z) * 0.14f;
                    float height = 0.5f - Mathf.Abs(z) * 0.28f;
                    var c = colors[i % colors.Length];
                    Part(Sphere(), "Spike", body, new Vector3(0f, y + height * 0.32f, z), new Vector3(0.14f, height, 0.2f), Mat(c.r, c.g, c.b), Quaternion.Euler(-z * 30f, 0f, 0f));
                }
                break;
            }
            case 5: // blue beanie with a pompom
            {
                Material blue = Mat(0.25f, 0.45f, 0.95f);
                Part(Sphere(), "BeanieDome", body, new Vector3(0f, 1.2f, -0.02f), new Vector3(1.13f, 0.82f, 1.13f), blue, none);
                Part(Sphere(), "BeanieStripe", body, new Vector3(0f, 1.24f, -0.02f), new Vector3(1.145f, 0.14f, 1.145f), Mat(1f, 1f, 1f), none);
                Part(Sphere(), "Pompom", body, new Vector3(0f, 1.62f, -0.02f), Vector3.one * 0.2f, Mat(1f, 1f, 1f), none);
                break;
            }
        }
    }

    // ---- eyes ---------------------------------------------------------------------------------

    void BuildEyes(int option)
    {
        Quaternion none = Quaternion.identity;
        // The heavy eyelids belong to the sleepy default eyes and the red ones; the other looks need open eyes.
        bool lidsVisible = option == 0 || option == 5;
        SetActive(lidL, lidsVisible);
        SetActive(lidR, lidsVisible);

        switch (option)
        {
            case 1: // kind: bigger round eyes with a sparkle
                foreach (float side in new[] { -1f, 1f })
                {
                    Transform eye = side < 0f ? eyeL : eyeR;
                    Transform pupil = side < 0f ? pupilL : pupilR;
                    Place(eye, new Vector3(side * 0.2f, 0.9f, 0.5f), Vector3.one * 0.35f);
                    Place(pupil, new Vector3(side * 0.2f, 0.9f, 0.64f), Vector3.one * 0.23f);
                    Part(Sphere(), "Sparkle", body, new Vector3(side * 0.2f + 0.04f, 0.95f, 0.735f), Vector3.one * 0.07f, Mat(1f, 1f, 1f), none);
                }
                break;
            case 2: // dark sunglasses
            {
                Material glass = Mat(0.03f, 0.03f, 0.05f, 0.2f, 0.95f);
                foreach (float side in new[] { -1f, 1f })
                {
                    Part(Sphere(), "Lens", body, new Vector3(side * 0.2f, 0.9f, 0.63f), new Vector3(0.42f, 0.3f, 0.16f), glass, none);
                    Part(Sphere(), "Glint", body, new Vector3(side * 0.2f - side * 0.07f, 0.96f, 0.7f), new Vector3(0.09f, 0.04f, 0.02f), Mat(1f, 1f, 1f), Quaternion.Euler(0f, 0f, side * 25f));
                }
                Part(Cube(), "Bridge", body, new Vector3(0f, 0.93f, 0.62f), new Vector3(0.14f, 0.05f, 0.05f), glass, none);
                break;
            }
            case 3: // squint: thin slits
                foreach (float side in new[] { -1f, 1f })
                {
                    Transform eye = side < 0f ? eyeL : eyeR;
                    Transform pupil = side < 0f ? pupilL : pupilR;
                    Place(eye, new Vector3(side * 0.2f, 0.88f, 0.5f), new Vector3(0.32f, 0.12f, 0.3f));
                    Place(pupil, new Vector3(side * 0.19f, 0.88f, 0.62f), new Vector3(0.14f, 0.07f, 0.14f));
                }
                break;
            case 4: // huge eyes
                foreach (float side in new[] { -1f, 1f })
                {
                    Transform eye = side < 0f ? eyeL : eyeR;
                    Transform pupil = side < 0f ? pupilL : pupilR;
                    Place(eye, new Vector3(side * 0.23f, 0.93f, 0.47f), Vector3.one * 0.46f);
                    Place(pupil, new Vector3(side * 0.22f, 0.93f, 0.66f), Vector3.one * 0.24f);
                }
                break;
        }
    }

    // ---- mouth --------------------------------------------------------------------------------

    void BuildMouth(int option)
    {
        if (option == 0) return; // the smirk from the prefab

        foreach (var part in smirk) SetActive(part, false);

        Material dark = Mat(0.06f, 0.03f, 0.03f);
        switch (option)
        {
            case 1: // smile: ends up, middle down
            {
                var points = new List<Vector2>();
                for (int i = 0; i <= 8; i++)
                {
                    float x = Mathf.Lerp(-0.24f, 0.24f, i / 8f);
                    points.Add(new Vector2(x, 0.56f + 0.13f * (x / 0.24f) * (x / 0.24f)));
                }
                Polyline("Smile", points, 0.058f, dark);
                break;
            }
            case 2: // surprised: a round open mouth
                Part(Sphere(), "MouthO", body, OnFace(0f, 0.57f, 0.006f), new Vector3(0.17f, 0.21f, 0.06f), dark, Quaternion.identity);
                Part(Sphere(), "Tongue", body, OnFace(0f, 0.515f, 0.03f), new Vector3(0.09f, 0.06f, 0.03f), Mat(1f, 0.45f, 0.5f), Quaternion.identity);
                break;
            case 3: // toothy grin
            {
                Material teeth = Mat(1f, 1f, 1f);
                Part(Sphere(), "Grin", body, OnFace(0f, 0.58f, 0.006f), new Vector3(0.4f, 0.17f, 0.06f), dark, Quaternion.identity);
                Part(Sphere(), "TeethTop", body, OnFace(0f, 0.625f, 0.03f), new Vector3(0.34f, 0.045f, 0.03f), teeth, Quaternion.identity);
                Part(Sphere(), "TeethBottom", body, OnFace(0f, 0.535f, 0.03f), new Vector3(0.28f, 0.04f, 0.03f), teeth, Quaternion.identity);
                break;
            }
            case 4: // moustache over a little mouth
            {
                Material brown = Mat(0.3f, 0.17f, 0.08f);
                Part(Sphere(), "MoustacheL", body, OnFace(-0.115f, 0.64f, 0.02f), new Vector3(0.24f, 0.09f, 0.07f), brown, Quaternion.Euler(0f, -12f, 16f));
                Part(Sphere(), "MoustacheR", body, OnFace(0.115f, 0.64f, 0.02f), new Vector3(0.24f, 0.09f, 0.07f), brown, Quaternion.Euler(0f, 12f, -16f));
                var points = new List<Vector2> { new Vector2(-0.07f, 0.55f), new Vector2(0f, 0.545f), new Vector2(0.07f, 0.55f) };
                Polyline("MouthLine", points, 0.045f, dark);
                break;
            }
            case 5: // tongue out
            {
                var points = new List<Vector2>();
                for (int i = 0; i <= 6; i++)
                {
                    float x = Mathf.Lerp(-0.2f, 0.2f, i / 6f);
                    points.Add(new Vector2(x, 0.585f + 0.06f * (x / 0.2f) * (x / 0.2f)));
                }
                Polyline("TongueMouth", points, 0.055f, dark);
                Part(Sphere(), "TongueOut", body, OnFace(0.07f, 0.5f, 0.02f), new Vector3(0.13f, 0.19f, 0.045f), Mat(1f, 0.4f, 0.55f), Quaternion.identity);
                break;
            }
        }
    }

    // ---- hands (gloves) -------------------------------------------------------------------------

    void BuildHands(int option)
    {
        if (option == 0) return;   // the white gloves and their cuffs are part of the prefab

        // Everything else replaces the white cuffs (or, for bare hands, just takes them off).
        SetActive(cuffL, false);
        SetActive(cuffR, false);
        if (option == 1) return;

        foreach (float side in new[] { -1f, 1f })
        {
            Transform hand = side < 0f ? handL : handR;
            float inward = -side; // towards the body

            // a cuff on the wrist (the hand is a ball of radius 0.5 in its own scale)
            Color cuffColor = option switch
            {
                2 => new Color(1f, 1f, 1f),
                3 => new Color(1f, 0.78f, 0.2f),
                4 => new Color(0.9f, 0.15f, 0.15f),
                _ => new Color(0.25f, 0.25f, 0.3f)
            };
            Part(Cylinder(), "Cuff", hand, new Vector3(inward * 0.6f, 0f, 0f), new Vector3(0.95f, 0.22f, 0.95f), Mat(cuffColor.r, cuffColor.g, cuffColor.b), Quaternion.Euler(0f, 0f, 90f));

            if (option == 2) // boxing glove: a fat red ball with a thumb
            {
                Part(Sphere(), "Glove", hand, Vector3.zero, new Vector3(1.55f, 1.5f, 1.7f), Mat(0.9f, 0.12f, 0.12f, 0f, 0.6f), Quaternion.identity);
                Part(Sphere(), "Thumb", hand, new Vector3(0f, 0.55f, 0.35f), new Vector3(0.6f, 0.5f, 0.7f), Mat(0.95f, 0.18f, 0.18f, 0f, 0.6f), Quaternion.identity);
            }
            else if (option == 5) // studded knuckles
            {
                Material stud = Mat(0.85f, 0.85f, 0.9f, 0.9f, 0.8f);
                Vector3[] studs =
                {
                    new Vector3(-0.32f, 0.28f, 0.38f), new Vector3(-0.16f, 0.36f, 0.42f), new Vector3(0f, 0.4f, 0.44f),
                    new Vector3(0.16f, 0.36f, 0.42f), new Vector3(0.32f, 0.28f, 0.38f)
                };
                foreach (var p in studs) Part(Sphere(), "Stud", hand, p, Vector3.one * 0.22f, stud, Quaternion.identity);
            }
        }
    }

    // ---- clothes ------------------------------------------------------------------------------

    void BuildClothes(int option)
    {
        const float Deg = Mathf.Deg2Rad;
        Quaternion none = Quaternion.identity;
        switch (option)
        {
            case 1: // yellow T-shirt (a red one would vanish on the red body): covers the back up to the shoulders and the belly, short sleeves
            {
                Material yellow = Mat(1f, 0.8f, 0.15f);
                Shell("Shirt", 0f, Mathf.PI * 2f, 118f * Deg, 58f * Deg, 160f * Deg, 0f, 1f, 0.025f, yellow);
                Sleeves(yellow, 0.3f);
                break;
            }
            case 2: // blue hoodie with a hood and a pocket, and long sleeves
            {
                Material blue = Mat(0.2f, 0.4f, 0.9f);
                Shell("Hoodie", 0f, Mathf.PI * 2f, 112f * Deg, 52f * Deg, 160f * Deg, 0f, 1f, 0.03f, blue);
                Sleeves(blue, 0.8f);
                Part(Sphere(), "Hood", body, new Vector3(0f, 1.0f, -0.5f), new Vector3(0.62f, 0.5f, 0.42f), blue, Quaternion.Euler(-15f, 0f, 0f));
                Part(Sphere(), "Pocket", body, OnFaceLow(0f, 0.34f, 0.035f), new Vector3(0.44f, 0.15f, 0.06f), Mat(0.15f, 0.3f, 0.75f), Quaternion.Euler(-8f, 0f, 0f));
                break;
            }
            case 3: // striped shirt
            {
                Material white = Mat(0.95f, 0.95f, 1f);
                Material stripe = Mat(0.2f, 0.32f, 0.75f);
                const int stripes = 6;
                for (int i = 0; i < stripes; i++)
                    Shell("Stripe", 0f, Mathf.PI * 2f, 118f * Deg, 58f * Deg, 160f * Deg, i / (float)stripes, (i + 1) / (float)stripes, 0.025f, i % 2 == 0 ? white : stripe);
                Sleeves(stripe, 0.3f);
                break;
            }
            case 4: // hero's cape: only the back half, hanging over the shoulders
            {
                Material cape = Mat(0.5f, 0.2f, 0.8f);
                Shell("Cape", Mathf.PI * 0.55f, Mathf.PI * 1.45f, 40f * Deg, 40f * Deg, 150f * Deg, 0f, 1f, 0.07f, cape);
                // gold clasps where the cape meets the sides of the body
                Part(Sphere(), "CapeClaspL", body, new Vector3(-0.56f, 0.86f, 0.32f), Vector3.one * 0.12f, Mat(1f, 0.78f, 0.2f, 0.85f, 0.7f), none);
                Part(Sphere(), "CapeClaspR", body, new Vector3(0.56f, 0.86f, 0.32f), Vector3.one * 0.12f, Mat(1f, 0.78f, 0.2f, 0.85f, 0.7f), none);
                break;
            }
            case 5: // bow tie
            {
                Material red = Mat(0.2f, 0.4f, 0.9f);   // blue, so it shows on the red body
                Vector3 knot = OnFaceLow(0f, 0.42f, 0.03f);
                Part(Sphere(), "BowKnot", body, knot, Vector3.one * 0.075f, Mat(0.1f, 0.2f, 0.6f), none);
                Part(Sphere(), "BowL", body, knot + new Vector3(-0.1f, 0f, -0.015f), new Vector3(0.15f, 0.11f, 0.06f), red, Quaternion.Euler(0f, 0f, 22f));
                Part(Sphere(), "BowR", body, knot + new Vector3(0.1f, 0f, -0.015f), new Vector3(0.15f, 0.11f, 0.06f), red, Quaternion.Euler(0f, 0f, -22f));
                break;
            }
        }
    }

    // Sleeves: a wider tube around the top part of each arm (`fraction` of the arm, from the shoulder). The arm is
    // stretched by LimbLinker, so the sleeve, being its child, grows and shrinks with it.
    void Sleeves(Material material, float fraction)
    {
        foreach (Transform arm in new[] { armL, armR })
        {
            if (arm == null) continue;
            Part(Cylinder(), "Sleeve", arm, new Vector3(0f, -1f + fraction, 0f), new Vector3(1.9f, fraction, 1.9f), material, Quaternion.identity);
        }
    }

    // ---- legs (shoes) ---------------------------------------------------------------------------

    void BuildLegs(int option)
    {
        if (option == 0) return;

        foreach (float side in new[] { -1f, 1f })
        {
            Transform foot = side < 0f ? footL : footR;
            Transform leg = side < 0f ? legL : legR;
            Quaternion none = Quaternion.identity;

            switch (option)
            {
                case 1: // red sneakers: a white stripe on top and a chunky white sole
                    Part(Sphere(), "Stripe", leg, new Vector3(0f, 0.13f, 0.06f), new Vector3(0.3f, 0.06f, 0.26f), Mat(1f, 1f, 1f), none);
                    Part(Sphere(), "Sole", leg, new Vector3(0f, -0.135f, 0.04f), new Vector3(0.44f, 0.06f, 0.64f), Mat(0.95f, 0.95f, 1f), none);
                    break;
                case 2: // roller skates: four wheels under every boot
                {
                    Material wheel = Mat(1f, 0.85f, 0.2f);
                    foreach (float x in new[] { -0.09f, 0.09f })
                        foreach (float z in new[] { -0.17f, 0.2f })
                            Part(Sphere(), "Wheel", leg, new Vector3(x, -0.13f, z), Vector3.one * 0.11f, wheel, none);
                    break;
                }
                case 3: // flippers: long, wide, flat
                    foot.localScale = new Vector3(0.62f, 0.16f, 0.95f);
                    break;
                case 4: // clown shoes: huge, polka dots and a pompom on the toe
                {
                    foot.localScale = new Vector3(0.5f, 0.32f, 0.95f);
                    Material dot = Mat(0.9f, 0.15f, 0.15f);
                    Part(Sphere(), "Dot", leg, new Vector3(-0.11f, 0.14f, 0.12f), Vector3.one * 0.09f, dot, none);
                    Part(Sphere(), "Dot", leg, new Vector3(0.12f, 0.14f, -0.06f), Vector3.one * 0.09f, dot, none);
                    Part(Sphere(), "Dot", leg, new Vector3(0.02f, 0.15f, 0.3f), Vector3.one * 0.08f, dot, none);
                    Part(Sphere(), "Pompom", leg, new Vector3(0f, 0.06f, 0.5f), Vector3.one * 0.15f, Mat(0.9f, 0.15f, 0.15f), none);
                    break;
                }
                case 5: // winged boots: little wings on the ankles that flap
                {
                    Material feather = Mat(1f, 1f, 1f);
                    string tag = side < 0f ? "L" : "R";
                    for (int i = 0; i < 2; i++)
                    {
                        var wing = Part(Sphere(), "Wing" + tag, leg, new Vector3(side * 0.24f, 0.1f + i * 0.04f, -0.1f - i * 0.08f),
                            new Vector3(0.3f - i * 0.06f, 0.04f, 0.15f), feather, Quaternion.Euler(0f, 0f, side * -28f));
                        wings.Add(wing.transform);
                    }
                    break;
                }
            }
        }
    }

    // ---- building blocks ---------------------------------------------------------------------

    GameObject Part(Mesh mesh, string partName, Transform parent, Vector3 position, Vector3 scale, Material material, Quaternion rotation)
    {
        var go = new GameObject("Style " + partName);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localRotation = rotation;
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        spawned.Add(go);
        return go;
    }

    static void Place(Transform t, Vector3 position, Vector3 scale)
    {
        if (t == null) return;
        t.localPosition = position;
        t.localScale = scale;
    }

    static void SetActive(Transform t, bool active)
    {
        if (t != null) t.gameObject.SetActive(active);
    }

    // A point on the front of the round body (x across, y up), lifted a little off the surface.
    static Vector3 OnFace(float x, float y, float lift = 0.012f)
    {
        float inside = 1f - (x / 0.65f) * (x / 0.65f) - ((y - 0.75f) / 0.6f) * ((y - 0.75f) / 0.6f);
        return new Vector3(x, y, 0.6f * Mathf.Sqrt(Mathf.Max(0.02f, inside)) + lift);
    }

    static Vector3 OnFaceLow(float x, float y, float lift) => OnFace(x, y, lift);

    // A line drawn on the face: stretched balls laid end to end along the points.
    void Polyline(string lineName, List<Vector2> points, float thickness, Material material)
    {
        for (int i = 0; i < points.Count - 1; i++)
        {
            Vector3 a = OnFace(points[i].x, points[i].y);
            Vector3 b = OnFace(points[i + 1].x, points[i + 1].y);
            Vector3 along = b - a;
            Part(Sphere(), lineName, body, (a + b) * 0.5f, new Vector3(along.magnitude * 1.75f, thickness, thickness * 1.1f), material,
                Quaternion.FromToRotation(Vector3.right, along));
        }
    }

    // A piece of clothing: a thin skin over part of the ball body, from an upper edge (which may be lower at the
    // front than at the back) down to `bottom`. `from`..`to` picks a slice of it (for stripes).
    void Shell(string shellName, float phi0, float phi1, float topFront, float topBack, float bottom, float from, float to,
        float inflate, Material material)
    {
        var go = new GameObject("Style " + shellName);
        go.transform.SetParent(body, false);
        go.AddComponent<MeshFilter>().sharedMesh = BuildShellMesh(phi0, phi1, topFront, topBack, bottom, from, to, inflate);
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        spawned.Add(go);
    }

    static Mesh BuildShellMesh(float phi0, float phi1, float topFront, float topBack, float bottom, float from, float to, float inflate)
    {
        const int columns = 40;
        int rows = Mathf.Max(2, Mathf.CeilToInt((to - from) * 8f));
        Vector3 radii = new Vector3(0.65f + inflate, 0.6f + inflate, 0.6f + inflate); // the body's ellipsoid, a bit larger

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var triangles = new List<int>();

        for (int j = 0; j <= rows; j++)
        {
            for (int i = 0; i <= columns; i++)
            {
                float phi = Mathf.Lerp(phi0, phi1, i / (float)columns);
                float front = Mathf.SmoothStep(0f, 1f, (Mathf.Cos(phi) + 1f) * 0.5f);
                float top = Mathf.Lerp(topBack, topFront, front);
                float theta = Mathf.Lerp(top, bottom, Mathf.Lerp(from, to, j / (float)rows));
                float st = Mathf.Sin(theta), ct = Mathf.Cos(theta), sp = Mathf.Sin(phi), cp = Mathf.Cos(phi);

                vertices.Add(new Vector3(radii.x * st * sp, 0.75f + radii.y * ct, radii.z * st * cp));
                normals.Add(new Vector3(st * sp / radii.x, ct / radii.y, st * cp / radii.z).normalized);
            }
        }

        for (int j = 0; j < rows; j++)
        {
            for (int i = 0; i < columns; i++)
            {
                int v00 = j * (columns + 1) + i, v10 = v00 + 1, v01 = v00 + columns + 1, v11 = v01 + 1;
                triangles.Add(v00); triangles.Add(v01); triangles.Add(v10);
                triangles.Add(v10); triangles.Add(v01); triangles.Add(v11);
            }
        }

        var mesh = new Mesh { name = "ClothesShell" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    Material Mat(float r, float g, float b, float metallic = 0f, float smoothness = 0.35f)
    {
        int key = Mathf.RoundToInt(r * 255f) | (Mathf.RoundToInt(g * 255f) << 8) | (Mathf.RoundToInt(b * 255f) << 16) |
                  (Mathf.RoundToInt(metallic * 15f) << 24) | (Mathf.RoundToInt(smoothness * 15f) << 28);
        if (materials.TryGetValue(key, out Material material) && material != null) return material;

        material = baseMaterial != null ? new Material(baseMaterial) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.SetColor("_BaseColor", new Color(r, g, b));
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        materials[key] = material;
        return material;
    }

    static Mesh Sphere() => sphereMesh != null ? sphereMesh : sphereMesh = PrimitiveMesh(PrimitiveType.Sphere);
    static Mesh Cube() => cubeMesh != null ? cubeMesh : cubeMesh = PrimitiveMesh(PrimitiveType.Cube);
    static Mesh Cylinder() => cylinderMesh != null ? cylinderMesh : cylinderMesh = PrimitiveMesh(PrimitiveType.Cylinder);

    static Mesh PrimitiveMesh(PrimitiveType type)
    {
        var primitive = GameObject.CreatePrimitive(type);
        Mesh mesh = primitive.GetComponent<MeshFilter>().sharedMesh;
        Destroy(primitive);
        return mesh;
    }
}
