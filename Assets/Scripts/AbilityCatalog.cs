using UnityEngine;

public enum AbilityId : byte
{
    HeavyPunch = 0,
    LeapSlam = 1,
    Teleport = 2,
    Thorns = 3,
    Dance = 4
}

// What one ability card is: shown on the card and used by the game code.
public class AbilityInfo
{
    public AbilityId Id;
    public string Name;
    public string Description;
    public string Key;        // shown on the card
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
    public const float DanceRadius = 2.6f;          // the fire ring is drawn a bit smaller than this

    public static readonly AbilityInfo[] All =
    {
        new AbilityInfo
        {
            Id = AbilityId.HeavyPunch, Name = "Сильный удар", Key = "ЛКМ",
            Description = "Мощный удар кулаком. Медленный замах, зато сильный.",
            BigText = HeavyPunchDamage.ToString(), BigCaption = "урона",
            Cooldown = 3f, Color = new Color(0.95f, 0.35f, 0.3f)
        },
        new AbilityInfo
        {
            Id = AbilityId.LeapSlam, Name = "Прыжок-удар", Key = "2",
            Description = "Прыжок вперёд. При приземлении удар по области вокруг.",
            BigText = SlamDamage.ToString(), BigCaption = "урона по области",
            Cooldown = 8f, Color = new Color(0.95f, 0.65f, 0.2f)
        },
        new AbilityInfo
        {
            Id = AbilityId.Teleport, Name = "Телепорт", Key = "3",
            Description = "Зажмите клавишу, наведите метку и отпустите: вы уже там.",
            BigText = ((int)TeleportRange) + " м", BigCaption = "дальность",
            Cooldown = 6f, Color = new Color(0.65f, 0.4f, 0.95f)
        },
        new AbilityInfo
        {
            Id = AbilityId.Thorns, Name = "Шипы", Key = "4",
            Description = "Пока шипы активны, тот, кто вас бьёт, получает двойной урон.",
            BigText = "×" + ThornsMultiplier, BigCaption = "урон обидчику",
            Cooldown = 15f, Color = new Color(0.45f, 0.8f, 0.5f)
        },
        new AbilityInfo
        {
            Id = AbilityId.Dance, Name = "Огненный танец", Key = "5",
            Description = "Вы танцуете в огне и жжёте всех рядом каждые 0,4 секунды.",
            BigText = DanceTickDamage.ToString(), BigCaption = "урона каждые 0,4 с",
            Cooldown = 20f, Color = new Color(1f, 0.5f, 0.15f)
        }
    };

    public static AbilityInfo Get(AbilityId id) => All[(int)id];
}
