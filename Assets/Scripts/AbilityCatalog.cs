using UnityEngine;

public enum AbilityId : byte
{
    HeavyPunch = 0,
    LeapSlam = 1,
    Teleport = 2,
    Thorns = 3,
    Dance = 4,
    Lightning = 5,
    Heal = 6,
    Shield = 7,
    Rage = 8,
    Vortex = 9
}

// What one ability card is: shown on the card and used by the game code.
public class AbilityInfo
{
    public AbilityId Id;
    public string Name;
    public string Description;
    public string BigText;    // the big "number" in the middle of the card
    public string BigCaption; // the small text under it
    public float Cooldown;    // seconds
    public Color Color;
}

// All numbers of the ability cards in one place. Change them here and the cards, the game and the tooltips follow.
public static class AbilityCatalog
{
    // damage and effects
    public const int HeavyPunchDamage = 20;
    public const int SlamDamage = 15;
    public const float SlamRadius = 3.5f;
    public const float TeleportRange = 50f;
    public const float ThornsDuration = 6f;
    public const int ThornsMultiplier = 2;          // whoever hits you takes this many times the damage they dealt
    public const float DanceDuration = 3f;
    public const int DanceTickDamage = 15;
    public const float DanceTickInterval = 0.4f;
    public const float DanceRadius = 7.8f;          // the fire ring is drawn a bit smaller than this
    public const int LightningDamage = 25;
    public const float LightningRange = 25f;
    public const float LightningWidth = 1.1f;       // how far off the line a target can be and still get hit
    public const int HealAmount = 30;
    public const float ShieldDuration = 3f;
    public const float RageDuration = 6f;
    public const int RageMultiplier = 2;            // everything you deal is multiplied while the rage lasts
    public const float VortexRadius = 9f;
    public const int VortexDamage = 5;

    public static readonly AbilityInfo[] All =
    {
        new AbilityInfo
        {
            Id = AbilityId.HeavyPunch, Name = "Сильный удар",
            Description = "Мощный удар кулаком, на ЛКМ тоже. Медленный замах, зато сильный.",
            BigText = HeavyPunchDamage.ToString(), BigCaption = "урона",
            Cooldown = 3f, Color = new Color(0.95f, 0.35f, 0.3f)
        },
        new AbilityInfo
        {
            Id = AbilityId.LeapSlam, Name = "Прыжок-удар",
            Description = "Прыжок вперёд. При приземлении удар по области вокруг.",
            BigText = SlamDamage.ToString(), BigCaption = "урона по области",
            Cooldown = 8f, Color = new Color(0.95f, 0.65f, 0.2f)
        },
        new AbilityInfo
        {
            Id = AbilityId.Teleport, Name = "Телепорт",
            Description = "Зажмите клавишу, наведите метку и отпустите: вы уже там.",
            BigText = ((int)TeleportRange) + " м", BigCaption = "дальность",
            Cooldown = 6f, Color = new Color(0.65f, 0.4f, 0.95f)
        },
        new AbilityInfo
        {
            Id = AbilityId.Thorns, Name = "Шипы",
            Description = "Пока шипы активны, тот, кто вас бьёт, получает двойной урон.",
            BigText = "×" + ThornsMultiplier, BigCaption = "урон обидчику",
            Cooldown = 15f, Color = new Color(0.45f, 0.8f, 0.5f)
        },
        new AbilityInfo
        {
            Id = AbilityId.Dance, Name = "Огненный танец",
            Description = "Вы танцуете в огне и жжёте всех в радиусе 7,8 м каждые 0,4 секунды.",
            BigText = DanceTickDamage.ToString(), BigCaption = "урона каждые 0,4 с",
            Cooldown = 20f, Color = new Color(1f, 0.5f, 0.15f)
        },
        new AbilityInfo
        {
            Id = AbilityId.Lightning, Name = "Молния",
            Description = "Мгновенный удар молнией по первому, кто стоит перед вами. Дальность 25 м.",
            BigText = LightningDamage.ToString(), BigCaption = "урона на расстоянии",
            Cooldown = 8f, Color = new Color(1f, 0.9f, 0.3f)
        },
        new AbilityInfo
        {
            Id = AbilityId.Heal, Name = "Исцеление",
            Description = "Мгновенно возвращает здоровье. Выше максимума оно не поднимется.",
            BigText = "+" + HealAmount, BigCaption = "здоровья",
            Cooldown = 20f, Color = new Color(1f, 0.55f, 0.7f)
        },
        new AbilityInfo
        {
            Id = AbilityId.Shield, Name = "Щит",
            Description = "Вокруг вас вспыхивает щит: пока он держится, весь урон блокируется.",
            BigText = ((int)ShieldDuration) + " с", BigCaption = "неуязвимость",
            Cooldown = 15f, Color = new Color(0.35f, 0.85f, 1f)
        },
        new AbilityInfo
        {
            Id = AbilityId.Rage, Name = "Ярость",
            Description = "Пока вы в ярости, все ваши удары и способности наносят двойной урон.",
            BigText = "×" + RageMultiplier, BigCaption = "ваш урон, " + ((int)RageDuration) + " с",
            Cooldown = 20f, Color = new Color(0.9f, 0.2f, 0.2f)
        },
        new AbilityInfo
        {
            Id = AbilityId.Vortex, Name = "Вихрь",
            Description = "Втягивает всех рядом к вам и наносит небольшой урон.",
            BigText = ((int)VortexRadius) + " м", BigCaption = "радиус притяжения",
            Cooldown = 12f, Color = new Color(0.5f, 0.55f, 1f)
        }
    };

    public static AbilityInfo Get(AbilityId id) => All[(int)id];
}

// The four slots of a character: which card sits in which slot. It belongs to the person at this computer (not to
// a character in the world), is remembered between sessions, and starts empty.
public static class AbilityLoadout
{
    public const int SlotCount = 4;
    const string PrefsKey = "AbilityLoadout";

    static readonly int[] slots = { -1, -1, -1, -1 };
    static bool loaded;

    static void Load()
    {
        if (loaded) return;
        loaded = true;

        try
        {
            string[] parts = PlayerPrefs.GetString(PrefsKey, "").Split(',');
            for (int i = 0; i < SlotCount && i < parts.Length; i++)
            {
                if (int.TryParse(parts[i], out int value) && value >= 0 && value < AbilityCatalog.All.Length &&
                    SlotOfInternal(value) < 0)
                    slots[i] = value;
            }
        }
        catch (System.Exception)
        {
            // Unreadable saved data: just start with empty slots.
        }
    }

    static void Save()
    {
        try
        {
            PlayerPrefs.SetString(PrefsKey, string.Join(",", slots));
            PlayerPrefs.Save();
        }
        catch (System.Exception)
        {
            // Not being able to remember the slots is not worth an error.
        }
    }

    static int SlotOfInternal(int ability)
    {
        for (int i = 0; i < SlotCount; i++)
            if (slots[i] == ability) return i;
        return -1;
    }

    // The card in this slot (an AbilityId as a number), or -1 when the slot is empty.
    public static int Get(int slot)
    {
        Load();
        return slots[slot];
    }

    // The slot a card sits in, or -1 if it isn't equipped.
    public static int SlotOf(AbilityId id)
    {
        Load();
        return SlotOfInternal((int)id);
    }

    public static int SlotOf(int ability)
    {
        Load();
        return SlotOfInternal(ability);
    }

    public static int FirstEmpty()
    {
        Load();
        return SlotOfInternal(-1);
    }

    public static bool AllEmpty()
    {
        Load();
        return FirstFilled() < 0;
    }

    static int FirstFilled()
    {
        for (int i = 0; i < SlotCount; i++)
            if (slots[i] >= 0) return i;
        return -1;
    }

    // Puts a card into a slot. If the card already sits in another slot the two cards swap places,
    // so the same card never takes two slots.
    public static void Set(int slot, int ability)
    {
        Load();
        if (slot < 0 || slot >= SlotCount || ability < 0 || ability >= AbilityCatalog.All.Length) return;

        int from = SlotOfInternal(ability);
        if (from == slot) return;

        int previous = slots[slot];
        slots[slot] = ability;
        if (from >= 0) slots[from] = previous;
        Save();
    }

    public static void Clear(int slot)
    {
        Load();
        if (slot < 0 || slot >= SlotCount || slots[slot] < 0) return;
        slots[slot] = -1;
        Save();
    }
}
