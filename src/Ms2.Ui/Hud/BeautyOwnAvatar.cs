using System;
using System.Collections.Generic;
using System.Linq;
using Ms2.Net.Packets;

namespace Ms2.Ui.Hud;

/// <summary>
/// The own player as the beauty shop's <see cref="IBeautyAvatar"/>: the preview wears and takes off items on the live own PC, as
/// the client does on its PC (PC::vfunc_127 / vfunc_128 with preview = 1, pc.cpp:1400 / :1415), so every view built from
/// <see cref="MyCharacter"/> (the 3D avatar copy redraws on its changed event) shows the preview. An item goes into the slot and
/// layer of <see cref="WornEquips.PutOn"/>; an item without a model preset (<c>wornLayer</c> null) is not worn, as the client.
/// <see cref="IsRealItem"/> is FUN_141421690 (customize.cpp:161-215): the customize kind of the item's data (see
/// <c>WornEquipment.IsCustomizeKind</c>), not an owned-item test; without the kind hook no item is real.
/// <para>STILL-OPEN: The skin colours (FUN_1414239e0 / FUN_141423650) are the user block's two
/// colours packed in wire order; setting one is remembered but the player model's skin tint is not redrawn. The "Customize" tag
/// of a hidden slot (CEquipItem::AddTag) has no effect on a drawn item yet. MyPC::vfunc_127 / vfunc_128 (mypc.cpp:4883-4905) also
/// re-sample DAT_143aea120 (not here).</para>
/// <para>MyPC::vfunc_128 calls ExcludeFromAutoEquip for an item it took off (one with a model preset), for a preview too
/// (customize.cpp's vfunc_128(item, 1)): <see cref="TakenOff"/>.</para>
/// </summary>
public sealed class BeautyOwnAvatar : IBeautyAvatar
{
    readonly MyCharacter me;
    readonly Func<int, int?> wornLayer;
    readonly Func<int, bool> isCustomizeKind;
    readonly int[] skin = new int[2];
    readonly HashSet<EquipSlot> hiddenSlots = new();
    // Uids that sit (or sat, before the preview took them off) in the outfit list: they go back there, as ItemPutOn kind 2.
    readonly HashSet<long> outfitUids;
    // Inside a BeginChange / EndChange bracket the lists are edited here and the views redraw once at the end.
    IReadOnlyList<EquipItem> pendingEquips, pendingOutfit;
    int depth;
    bool dirty;

    /// <param name="isCustomizeKind">FUN_141421690 over an item id (hair, face, eye, skin colour kinds); null = never.</param>
    public BeautyOwnAvatar(MyCharacter me, Func<int, int?> wornLayer, Func<int, bool> isCustomizeKind = null)
    {
        this.me = me; this.wornLayer = wornLayer; this.isCustomizeKind = isCustomizeKind;
        outfitUids = new HashSet<long>((me.Outfit2 ?? Array.Empty<EquipItem>()).Select(e => e.Uid));
        var s = me.User?.Skin ?? default;
        skin[0] = Pack(s.Primary); skin[1] = Pack(s.Secondary);
    }

    static int Pack(Rgba c) => c.B | c.G << 8 | c.R << 16 | c.A << 24;

    IReadOnlyList<EquipItem> Gear => depth > 0 ? pendingEquips : me.Equips;
    IReadOnlyList<EquipItem> Outfit => depth > 0 ? pendingOutfit : me.Outfit2;

    public IReadOnlyList<EquipItem> Worn =>
        (Gear ?? Array.Empty<EquipItem>()).Concat(Outfit ?? Array.Empty<EquipItem>()).ToList();

    public IReadOnlyCollection<EquipSlot> HiddenSlots => hiddenSlots;

    public EquipItem Clone(EquipItem item) => item?.Clone();

    /// <summary>
    /// FUN_140cd3fe0(pc, 0) @customize.cpp:527: the redraw of the avatar waits until <see cref="EndChange"/>, so a restore of
    /// every slot is one change of the worn lists instead of one per item.
    /// </summary>
    public void BeginChange()
    {
        if (depth++ == 0) { pendingEquips = me.Equips; pendingOutfit = me.Outfit2; dirty = false; }
    }

    /// <summary>FUN_140cd3fe0(pc, 1) @customize.cpp:552: the lists of the bracket go to the player in one change, if any changed.</summary>
    public void EndChange()
    {
        if (depth == 0 || --depth > 0) return;
        var e = pendingEquips; var o = pendingOutfit;
        pendingEquips = pendingOutfit = null;
        if (dirty) me.ReplaceWorn(e, o);
        dirty = false;
    }

    public void Equip(EquipItem item)
    {
        if (item == null) return;
        var equips = Gear; var outfit = Outfit;
        // Kind 0: the layer comes from the item data. An item that came from the outfit list goes back there (kind 2), not onto
        // the gear of its slot.
        byte kind = (byte)(outfitUids.Contains(item.Uid) ? 2 : 0);
        if (!WornEquips.PutOn(ref equips, ref outfit, new ItemPutOn(me.ObjectId, kind, item), wornLayer)) return;
        Store(equips, outfit);
    }

    public void Unequip(EquipItem item)
    {
        if (item == null) return;
        var equips = Gear; var outfit = Outfit;
        if ((outfit ?? Array.Empty<EquipItem>()).Any(e => e.Uid == item.Uid)) outfitUids.Add(item.Uid);
        if (WornEquips.PutOff(ref equips, ref outfit, item.Uid, wornLayer) != null) Store(equips, outfit);
        if (wornLayer(item.Id) != null) TakenOff?.Invoke(item.Uid);      // MyPC::vfunc_128: a model preset, ExcludeFromAutoEquip (mypc.cpp:4903)
    }

    /// <summary>The uid of an item the preview took off (one with a model preset): the inventory's ExcludeFromAutoEquip
    /// (<see cref="AutoEquip.ExcludeFromAutoEquip"/>), as MyPC::vfunc_128 (mypc.cpp:4903).</summary>
    public Action<long> TakenOff;

    void Store(IReadOnlyList<EquipItem> equips, IReadOnlyList<EquipItem> outfit)
    {
        if (depth > 0) { pendingEquips = equips; pendingOutfit = outfit; dirty = true; return; }
        me.ReplaceWorn(equips, outfit);
    }

    public bool IsRealItem(EquipItem item) => item != null && isCustomizeKind != null && isCustomizeKind(item.Id);

    public int GetSkinColor(int index) => (uint)index < 2 ? skin[index] : 0;

    public void SetSkinColor(int index, int color) { if ((uint)index < 2) skin[index] = color; }

    public void SetSlotHidden(EquipSlot slot, bool hidden) { if (hidden) hiddenSlots.Add(slot); else hiddenSlots.Remove(slot); }
}
