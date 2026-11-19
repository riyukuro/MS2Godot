using Ms2.Net.Packets;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// PC::CanUseItem (pc.cpp:4683): the item may be used by this player — the gate of a gem's apply and worn
/// (PCGemEquip::FUN_1412712e0 / FUN_141270740, gemslot_2.cpp:235/375), so of the chat balloon gem's skin. The item is the
/// packet's record (<see cref="EquipItem"/> is CItem::ReadFromStream, item.cpp:2655) and <see cref="Pc"/> the field's state
/// of its owner; the item data flags are <see cref="ItemData"/>.
/// <para>
/// The checks, in the client's order: the item itself (<see cref="ItemUsable"/>, CItem::FUN_140528980 item.cpp:1717: an
/// expired non-pet item without a maid id, a score item with no uses left), the PC bang requirement
/// (CItem::FUN_14052a2d0 = the data's &lt;PCBang&gt; flag, item.cpp:1004, against the grade byte m_nFF8, set from the add
/// record at scenestategame.cpp:11151), the VIP requirement (FUN_14052a330 = limit flag6a "vip", item.cpp:1013, against
/// PC::IsVip pc.cpp:2209), the wedding requirement (FUN_14052a390 = limit "wedding", item.cpp:1022, against vfunc_146
/// pc.cpp:2218: the wedding state 2, 3 or 4) and the owner bind (FUN_1406343b0,
/// itemsocketsystemutil.cpp:2534: the transfer's binding and every socketed gem's bind must be the character).
/// </para>
/// <para>
/// Not ported, each unreachable for a worn gem: the customize preview's flag 1 (CItem::SetFlags, customize.cpp:217; a
/// packet item never has it, so the pc bang / vip / wedding tests run), the feature-0xd4 fitting doll skip of the bind
/// (pc.cpp:4696: no FittingDollPC exists in this field, so the bind is always checked), m_n1198 of IsVip (set only by the
/// fitting doll slot, fittingdollslot_1.cpp:264) and the layer test (GetVariant == 2, pc.cpp:4704: m_24 is only set on
/// the layer-2 outfits, SetVariant(2) at scenestategame.cpp:11264, never on a PCGemEquip badge, and the data's kind
/// fallback is 0, 1 or 3, item.cpp:1629). STILL-OPEN: a score item's custom-note parts of FUN_140528980
/// (FUN_140525730 / FUN_140525680, item.cpp:813/1745: the musicscore row's IsCustomNote and the score's m_n38, neither
/// parsed) — a worn gem is never a score item (type 0xc), so the score branch keeps only the client's remaining-uses
/// gate here.
/// </para>
/// </summary>
public static class PcItemUse
{
    /// <summary>
    /// The owner's state the check reads, as the add packets leave it: the PC bang grade byte (PC m_nFF8), the premium
    /// club time (m_n1190, FieldAddUser), the wedding state (m_n1220, FieldAddUser) and the character id
    /// (FUN_141656a80). The server clock is GetServerTime.
    /// </summary>
    public sealed class Pc
    {
        public byte PcBangGrade;
        public long PremiumTime;
        public short WeddingState;
        public long CharacterId;
        public Func<long> ServerTime = () => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    /// <summary>PC::CanUseItem: false for no item; no data (an id the item data does not know is never created,
    /// CItem::FUN_140523c40 fails), then the item, then the four requirements.</summary>
    public static bool CanUseItem(EquipItem item, ItemData data, Pc pc)
    {
        if (item == null || pc == null || data == null) return false;
        if (!ItemUsable(item, data, pc)) return false;
        // FUN_14052a2d0 / FUN_14052a330 / FUN_14052a390: the data flag when the preview's flag 1 is not set
        if (data != null && data.PCBang && pc.PcBangGrade == 0) return false;
        if (data != null && data.Vip && pc.PremiumTime <= pc.ServerTime()) return false;
        if (data != null && data.Wedding && pc.WeddingState is not (2 or 3 or 4)) return false;
        // the bind, always checked (no fitting doll): the transfer's binding, then every socketed gem's bind
        if (item.Transfer.HasBinding && item.Transfer.BindingId != pc.CharacterId) return false;
        if (item.Socket != null)
            foreach (var g in item.Socket.Gems)
            {
                if (g == null || g.GemId == 0) continue;      // an empty slot (IsEmpty: gem id 0)
                if (g.HasBind && g.BindCharacterId != pc.CharacterId) return false;
            }
        return true;
    }

    /// <summary>
    /// CItem::FUN_140528980 (item.cpp:1717): an expired item is unusable unless it is a pet (data type 0xb,
    /// itemdata.cpp:675) or has a maid id (type 6, subtype 7, param2, itemdata.cpp:1163); a score item (type 0xc) needs a
    /// use left (m_a0, the record's RemainUses).
    /// </summary>
    public static bool ItemUsable(EquipItem item, ItemData data, Pc pc)
    {
        bool expired = item.ExpiryTime > 0 && item.ExpiryTime <= pc.ServerTime();   // CItem::IsExpired (item.cpp:1303, m_80)
        if (expired && data?.Type != 0xb && !MaidId(data)) return false;
        if (data?.Type == 0xc && item.RemainUses < 1) return false;
        return true;
    }

    /// <summary>CItemData::HasMaidId (itemdata.cpp:713): GetMaidId is the param2 of a type 6, subtype 7 item.</summary>
    static bool MaidId(ItemData data) => data != null && data.Type == 6 && data.Subtype == 7 && data.Param2 > 0;
}
