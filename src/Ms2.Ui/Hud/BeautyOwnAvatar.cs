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
/// <para>STILL-OPEN: <see cref="IsRealItem"/> is FUN_141421690, whose owned-item test is not recovered: an item counts as real
/// when its uid was worn when the avatar was created. The skin colours (FUN_1414239e0 / FUN_141423650) are the user block's two
/// colours packed in wire order; setting one is remembered but the player model's skin tint is not redrawn. The "Customize" tag
/// of a hidden slot (CEquipItem::AddTag) has no effect on a drawn item yet.</para>
/// </summary>
public sealed class BeautyOwnAvatar : IBeautyAvatar
{
    readonly MyCharacter me;
    readonly Func<int, int?> wornLayer;
    readonly HashSet<long> realUids;
    readonly int[] skin = new int[2];
    readonly HashSet<EquipSlot> hiddenSlots = new();

    public BeautyOwnAvatar(MyCharacter me, Func<int, int?> wornLayer)
    {
        this.me = me; this.wornLayer = wornLayer;
        realUids = new HashSet<long>(Worn.Select(e => e.Uid));
        var s = me.User?.Skin ?? default;
        skin[0] = Pack(s.Primary); skin[1] = Pack(s.Secondary);
    }

    static int Pack(Rgba c) => c.B | c.G << 8 | c.R << 16 | c.A << 24;

    public IReadOnlyList<EquipItem> Worn =>
        (me.Equips ?? Array.Empty<EquipItem>()).Concat(me.Outfit2 ?? Array.Empty<EquipItem>()).ToList();

    public IReadOnlyCollection<EquipSlot> HiddenSlots => hiddenSlots;

    public EquipItem Clone(EquipItem item) => item?.Clone();

    public void Equip(EquipItem item)
    {
        if (item == null) return;
        var equips = me.Equips; var outfit = me.Outfit2;
        // Kind 0: the layer comes from the item data (a variant-2 outfit is not what the preview puts on).
        if (!WornEquips.PutOn(ref equips, ref outfit, new ItemPutOn(me.ObjectId, 0, item), wornLayer)) return;
        me.ReplaceWorn(equips, outfit);
    }

    public void Unequip(EquipItem item)
    {
        if (item == null) return;
        var equips = me.Equips; var outfit = me.Outfit2;
        if (WornEquips.PutOff(ref equips, ref outfit, item.Uid, wornLayer) == null) return;
        me.ReplaceWorn(equips, outfit);
    }

    public bool IsRealItem(EquipItem item) => item != null && realUids.Contains(item.Uid);

    public int GetSkinColor(int index) => (uint)index < 2 ? skin[index] : 0;

    public void SetSkinColor(int index, int color) { if ((uint)index < 2) skin[index] = color; }

    public void SetSlotHidden(EquipSlot slot, bool hidden) { if (hidden) hiddenSlots.Add(slot); else hiddenSlots.Remove(slot); }
}
