using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>
/// The worn-equip lists of one PC (gear, then layer-2 outfits) and what a put-on / put-off does to them, for the own PC
/// and for the other players alike (CEquipment::EquipItem / FUN_1411e3960 act on the object's own equipment).
/// </summary>
public static class WornEquips
{
    /// <summary>
    /// CSceneStateGame::OnItemPutOn (scenestategame.cpp:3080): put-on kind 0 / 1 / 2 (FUN_140222f60; others are dropped);
    /// kind 2 sets the item's variant 2, the second outfit layer. PC::vfunc_127 (pc.cpp:1400) wears only an item with a
    /// model preset: <paramref name="wornLayer"/> gives the item's layer or null when the item is unknown or has no preset.
    /// The item replaces the one in the same slot and layer (CEquipment::FUN_1411ec190 puts it into the slots of its model
    /// groups at its layer; the multi-slot groups of a model are not modelled) and goes after the last one of its layer or
    /// a lower one, so the list keeps FieldAddUser's order and a look built from it in order shows each slot's top layer
    /// (CEquipment::GetTopLayerItem @0x1411e1a50, equipment.cpp:835). Returns whether the lists changed.
    /// </summary>
    public static bool PutOn(ref IReadOnlyList<EquipItem> equips, ref IReadOnlyList<EquipItem> outfit2, ItemPutOn p, Func<int, int?> wornLayer)
    {
        if (p.Kind > 2) return false;
        if (wornLayer(p.Item.Id) is not { } dataLayer) return false;
        int layer = p.Kind == 2 ? 2 : dataLayer;
        var slot = p.Item.Slot;
        var gear = new List<EquipItem>(equips ?? Array.Empty<EquipItem>());
        var outfit = new List<EquipItem>(outfit2 ?? Array.Empty<EquipItem>());
        gear.RemoveAll(e => e.Slot == slot && wornLayer(e.Id) == layer);
        outfit.RemoveAll(e => e.Slot == slot && layer == 2);
        if (layer == 2) outfit.Add(p.Item);
        else gear.Insert(gear.FindLastIndex(e => (wornLayer(e.Id) ?? 0) <= layer) + 1, p.Item);
        equips = gear; outfit2 = outfit;
        return true;
    }

    /// <summary>
    /// CSceneStateGame::OnItemPutOff (scenestategame.cpp:3144): the worn item of that uid (PC::FUN_141655b80) is taken off
    /// when it has a model preset (PC::vfunc_128, pc.cpp:1415; CEquipment::FUN_1411e3960 finds it by uid). The slot it
    /// leaves is returned (null when nothing changed). FUN_1411e3960 (equipment.cpp:1022-1034) takes off by
    /// GetItemAt(slot, 3) instead of by uid when FUN_14052a170() == 3; that layer-3 case is not modelled here.
    /// </summary>
    public static EquipSlot? PutOff(ref IReadOnlyList<EquipItem> equips, ref IReadOnlyList<EquipItem> outfit2, long uid, Func<int, int?> wornLayer)
    {
        var item = (equips ?? Array.Empty<EquipItem>()).Concat(outfit2 ?? Array.Empty<EquipItem>()).FirstOrDefault(e => e.Uid == uid);
        if (item == null || wornLayer(item.Id) == null) return null;
        equips = (equips ?? Array.Empty<EquipItem>()).Where(e => e.Uid != uid).ToList();
        outfit2 = (outfit2 ?? Array.Empty<EquipItem>()).Where(e => e.Uid != uid).ToList();
        return item.Slot;
    }

    /// <summary>Whether an item of that uid is worn (PC::FUN_141655b80 finds it), with or without a model preset.</summary>
    public static bool IsWorn(IReadOnlyList<EquipItem> equips, IReadOnlyList<EquipItem> outfit2, long uid) =>
        (equips ?? Array.Empty<EquipItem>()).Concat(outfit2 ?? Array.Empty<EquipItem>()).Any(e => e.Uid == uid);

    /// <summary>The look's items in order: gear, then outfits, so the last one of a slot is the one shown. Layer 2 (the
    /// outfits) is left out while the layer switch is off (<see cref="OutfitShown"/>): the client's top-layer lookup then
    /// falls back to layers 1 and 0 (FUN_1411ef780, equipment.cpp:1655).</summary>
    public static IEnumerable<EquipItem> Look(IReadOnlyList<EquipItem> equips, IReadOnlyList<EquipItem> outfit2, bool outfitShown = true) =>
        outfitShown ? (equips ?? Array.Empty<EquipItem>()).Concat(outfit2 ?? Array.Empty<EquipItem>()) : (equips ?? Array.Empty<EquipItem>());

    /// <summary>CEquipmentSlotLayerInfo::IsActive after SetLayerPeriod (FUN_1411df910, equipment.cpp:401-459): the layer is
    /// off when feature 0x8e is on (FUN_1411df7d0) and the period never started (start 0) or has ended (end above 0 and the
    /// server time reached it) - FUN_1411df700. The period is read from FieldAddUser (scenestategame.cpp:11245-11248) and
    /// changed by <see cref="CodiTab"/>.</summary>
    public static bool OutfitShown(bool feature8e, long start, long end, long serverTime) =>
        !(feature8e && (start == 0 || (end > 0 && serverTime >= end)));

    /// <summary>What a slot's model was built from: a change in any of these rebuilds the slot (an item update that
    /// reads the body again goes through the forced path, the same object being edited in place).</summary>
    public readonly record struct SlotKey(int Id, long Uid, EquipColor Color);

    /// <summary>Skin (the body) and face decals draw no item model.</summary>
    public static bool DrawsModel(EquipSlot slot) => slot != EquipSlot.SK && slot != EquipSlot.FD;

    /// <summary>The shown item of each model-drawing slot: the last one listed for it.</summary>
    public static Dictionary<EquipSlot, SlotKey> Shown(IEnumerable<EquipItem> look)
    {
        var d = new Dictionary<EquipSlot, SlotKey>();
        foreach (var it in look) if (DrawsModel(it.Slot)) d[it.Slot] = new SlotKey(it.Id, it.Uid, it.Color);
        return d;
    }

    /// <summary>The slots whose model has to be rebuilt to go from <paramref name="built"/> to the new look: new, changed
    /// and removed ones; the others keep their nodes.</summary>
    public static List<EquipSlot> Changed(IReadOnlyDictionary<EquipSlot, SlotKey> built, IReadOnlyDictionary<EquipSlot, SlotKey> now)
    {
        var l = new List<EquipSlot>();
        foreach (var (slot, key) in now) if (!built.TryGetValue(slot, out var k) || k != key) l.Add(slot);
        foreach (var slot in built.Keys) if (!now.ContainsKey(slot)) l.Add(slot);
        return l;
    }

    /// <summary>A replace="1" asset of slot <paramref name="slotName"/> takes the place of the body meshes named like the
    /// slot: the name itself, or the name and "_" (CL replaces CL_Skin and CL_Bra, FA replaces FA, FA_Skin and FA_EA).</summary>
    public static bool Replaces(string slotName, string node) =>
        node.StartsWith(slotName, StringComparison.OrdinalIgnoreCase) && (node.Length == slotName.Length || node[slotName.Length] == '_');
}

/// <summary>ItemUse mode 5, OnActiveCodiTab (scenestategame.cpp:8663): an outfit tab was activated for a player; the
/// layer period goes to that player's equipment (SetLayerPeriod). The record is FUN_1407a8250's read.</summary>
public sealed record CodiTab(int ActorId, long Start, long End, long Expire, bool Silent)
{
    public static CodiTab Read(ByteReader r) => new(r.ReadInt(), r.ReadLong(), r.ReadLong(), r.ReadLong(), r.ReadBool());
}
