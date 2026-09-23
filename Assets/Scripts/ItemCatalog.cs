using UnityEngine;

public enum ItemId : byte
{
    Sneakers = 0,
    Knuckles = 1,
    IronFist = 2,
    ManaCrystal = 3,
    StoneSkin = 4,
    Ward = 5,
    VampiricFang = 6,
    Hourglass = 7,
    HeavyBoots = 8,
    LuckyCoin = 9
}

// What one item is: shown on its card and used by PlayerItems. Unlike the ability cards, items have no button and
// no cooldown - equipping one just changes a number, all the time.
public class ItemInfo
{
    public ItemId Id;
    public string Name;
    public string Caption;      // the short stat line under the icon
    public string Description;  // shown only in the panel
    public Color Color;
}

// All numbers of the items in one place, and the ten items themselves. Change them here and the cards and the
// game follow.
public static class ItemCatalog
{
    public const float SneakerSpeedBonus = 0.15f;
    public const float KnuckleAttackSpeedBonus = 0.30f;
    public const float IronFistDamageBonus = 0.20f;
    public const float ManaCrystalDamageBonus = 0.20f;
    public const float StoneSkinResist = 0.15f;
    public const float WardResist = 0.15f;
    public const float VampiricFangLifesteal = 0.10f;
    public const float HourglassCooldownCut = 0.20f;
    public const float HeavyBootsKnockbackCut = 0.30f;
    public const float LuckyCoinChance = 0.15f;

    public static readonly ItemInfo[] All =
    {
        new ItemInfo
        {
            Id = ItemId.Sneakers, Name = "Кроссовок с пером",
            Caption = "+15% скорости",
            Description = "Перо на пятке ловит ветер: вы двигаетесь быстрее.",
            Color = new Color(0.5f, 0.8f, 1f)
        },
        new ItemInfo
        {
            Id = ItemId.Knuckles, Name = "Кастет вихря",
            Caption = "+30% скорости атаки",
            Description = "Замах короче: кулаками можно бить намного чаще.",
            Color = new Color(0.35f, 0.85f, 0.9f)
        },
        new ItemInfo
        {
            Id = ItemId.IronFist, Name = "Стальной кулак",
            Caption = "+20% физ. урона",
            Description = "Удары становятся тяжелее: физический урон выше.",
            Color = new Color(0.75f, 0.4f, 0.35f)
        },
        new ItemInfo
        {
            Id = ItemId.ManaCrystal, Name = "Кристалл маны",
            Caption = "+20% маг. урона",
            Description = "Питает способности силой: магический урон выше.",
            Color = new Color(0.6f, 0.4f, 0.95f)
        },
        new ItemInfo
        {
            Id = ItemId.StoneSkin, Name = "Каменная кожа",
            Caption = "−15% физ. урона",
            Description = "Кожа твердеет, как камень: физический урон по вам слабее.",
            Color = new Color(0.58f, 0.58f, 0.63f)
        },
        new ItemInfo
        {
            Id = ItemId.Ward, Name = "Оберег",
            Caption = "−15% маг. урона",
            Description = "Отводит колдовство: магический урон по вам слабее.",
            Color = new Color(0.35f, 0.8f, 0.75f)
        },
        new ItemInfo
        {
            Id = ItemId.VampiricFang, Name = "Вампирский клык",
            Caption = "+10% похищения жизни",
            Description = "Удар кулаком в бою возвращает вам часть урона здоровьем.",
            Color = new Color(0.7f, 0.15f, 0.2f)
        },
        new ItemInfo
        {
            Id = ItemId.Hourglass, Name = "Песочные часы",
            Caption = "−20% перезарядки",
            Description = "Время для способностей и рывка течёт быстрее.",
            Color = new Color(0.85f, 0.65f, 0.25f)
        },
        new ItemInfo
        {
            Id = ItemId.HeavyBoots, Name = "Тяжёлые сапоги",
            Caption = "−30% отбрасывания",
            Description = "Гасят удар при попадании: вас отбрасывает слабее.",
            Color = new Color(0.45f, 0.32f, 0.2f)
        },
        new ItemInfo
        {
            Id = ItemId.LuckyCoin, Name = "Счастливая монета",
            Caption = "15% шанс ×2 урона",
            Description = "Иногда удача удваивает урон вашего удара.",
            Color = new Color(0.95f, 0.78f, 0.25f)
        }
    };

    public static ItemInfo Get(ItemId id) => All[(int)id];
}

// The two item slots of a character: which item sits in which slot. Belongs to the person at this computer (not to
// a character in the world), is remembered between sessions, and starts empty. Mirrors AbilityLoadout exactly.
public static class ItemLoadout
{
    public const int SlotCount = 2;
    const string PrefsKey = "ItemLoadout";

    static readonly int[] slots = { -1, -1 };
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
                if (int.TryParse(parts[i], out int value) && value >= 0 && value < ItemCatalog.All.Length &&
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

    static int SlotOfInternal(int item)
    {
        for (int i = 0; i < SlotCount; i++)
            if (slots[i] == item) return i;
        return -1;
    }

    // The item in this slot (an ItemId as a number), or -1 when the slot is empty.
    public static int Get(int slot)
    {
        Load();
        return slots[slot];
    }

    // The slot an item sits in, or -1 if it isn't equipped.
    public static int SlotOf(int item)
    {
        Load();
        return SlotOfInternal(item);
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

    // Puts an item into a slot. If it already sits in the other slot the two swap places, so the same item never
    // takes both slots.
    public static void Set(int slot, int item)
    {
        Load();
        if (slot < 0 || slot >= SlotCount || item < 0 || item >= ItemCatalog.All.Length) return;

        int from = SlotOfInternal(item);
        if (from == slot) return;

        int previous = slots[slot];
        slots[slot] = item;
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

    // Packed for the network: 4 bits per slot (0-9 = an item, 0xF = empty), so both slots fit in one byte.
    public static byte NetworkValue
    {
        get
        {
            Load();
            int a = slots[0] < 0 ? 0xF : slots[0];
            int b = slots[1] < 0 ? 0xF : slots[1];
            return (byte)(a | (b << 4));
        }
    }
}
