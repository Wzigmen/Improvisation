using Unity.Netcode;
using UnityEngine;

// The two item slots of one character (see ItemCatalog / ItemLoadout). Items are passive stat modifiers - no
// button, no cooldown, just a number that changes while they're equipped. What is equipped is synced to everyone
// (packed into one byte, 4 bits per slot) so the host can always work out both fighters' bonuses when it resolves
// a hit.
[RequireComponent(typeof(PlayerController))]
public class PlayerItems : NetworkBehaviour
{
    const byte Empty = 0xFF;

    readonly NetworkVariable<byte> equipped = new NetworkVariable<byte>(
        Empty, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    PlayerController player;

    void Awake() => player = GetComponent<PlayerController>();

    public override void OnNetworkSpawn()
    {
        if (!IsOwner || player.IsBot) return;
        equipped.Value = ItemLoadout.NetworkValue;
    }

    void Update()
    {
        // The owner's loadout can change any time the card panel is open; keep the network copy in step with it.
        if (!IsSpawned || !IsOwner || player.IsBot) return;
        byte want = ItemLoadout.NetworkValue;
        if (equipped.Value != want) equipped.Value = want;
    }

    int Slot(int index)
    {
        int nibble = index == 0 ? equipped.Value & 0xF : (equipped.Value >> 4) & 0xF;
        return nibble == 0xF ? -1 : nibble;
    }

    public bool Has(ItemId id)
    {
        for (int i = 0; i < ItemLoadout.SlotCount; i++)
            if (Slot(i) == (int)id) return true;
        return false;
    }

    // ---- movement / timing (read locally by the owner) -------------------------------------------

    public float MoveSpeedMultiplier => 1f + (Has(ItemId.Sneakers) ? ItemCatalog.SneakerSpeedBonus : 0f);
    public float AttackCooldownMultiplier => Has(ItemId.Knuckles) ? 1f / (1f + ItemCatalog.KnuckleAttackSpeedBonus) : 1f;
    public float AbilityCooldownMultiplier => 1f - (Has(ItemId.Hourglass) ? ItemCatalog.HourglassCooldownCut : 0f);
    public float KnockbackMultiplier => 1f - (Has(ItemId.HeavyBoots) ? ItemCatalog.HeavyBootsKnockbackCut : 0f);

    // ---- combat (read by the host when it resolves a hit) ----------------------------------------

    // The attacker's own boosts: a bonus for this damage type, then a chance at a lucky critical hit.
    public int ApplyOutgoing(int damage, DamageType type)
    {
        float mult = 1f;
        if (type == DamageType.Physical && Has(ItemId.IronFist)) mult += ItemCatalog.IronFistDamageBonus;
        if (type == DamageType.Magical && Has(ItemId.ManaCrystal)) mult += ItemCatalog.ManaCrystalDamageBonus;

        int result = Mathf.Max(1, Mathf.RoundToInt(damage * mult));
        if (Has(ItemId.LuckyCoin) && Random.value < ItemCatalog.LuckyCoinChance) result *= 2;
        return result;
    }

    // The victim's own resistance to that damage type.
    public int ApplyIncoming(int damage, DamageType type)
    {
        float mult = 1f;
        if (type == DamageType.Physical && Has(ItemId.StoneSkin)) mult -= ItemCatalog.StoneSkinResist;
        if (type == DamageType.Magical && Has(ItemId.Ward)) mult -= ItemCatalog.WardResist;
        return Mathf.Max(1, Mathf.RoundToInt(damage * Mathf.Max(0.1f, mult)));
    }

    // A slice of a landed physical hit that comes back as health (Vampiric Fang).
    public float LifestealFraction(DamageType type) =>
        type == DamageType.Physical && Has(ItemId.VampiricFang) ? ItemCatalog.VampiricFangLifesteal : 0f;
}
