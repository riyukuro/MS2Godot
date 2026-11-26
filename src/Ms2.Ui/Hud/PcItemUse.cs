using Ms2.Net.Packets;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// PC::CanUseItem (pc.cpp:4683): the item may be used by this player — the worn gem's gate (PCGemEquip::FUN_1412712e0 /
/// FUN_141270740, gemslot_2.cpp:235/375), so what sets the chat balloon gem's skin. The item is the packet's record
/// (<see cref="EquipItem"/> is CItem::ReadFromStream, item.cpp:2655) and <see cref="Pc"/> the field's state of its owner;
/// the item data flags are <see cref="ItemData"/>.
/// <para>
/// The checks, in the client's order: the item itself (<see cref="ItemUsable"/>, CItem::FUN_140528980 item.cpp:1717: an
/// expired item that is neither a pet nor a maid item, a score item with no uses left), the PC bang requirement
/// (CItem::FUN_14052a2d0 = the data's &lt;PCBang&gt; flag, item.cpp:1004, against the grade byte m_nFF8, set from the add
/// record at scenestategame.cpp:11151), the VIP requirement (FUN_14052a330 = limit flag6a "vip", item.cpp:1013, against
/// PC::IsVip pc.cpp:2209), the wedding requirement (FUN_14052a390 = limit "wedding", item.cpp:1022, against vfunc_146
/// pc.cpp:2218: the wedding state 2, 3 or 4) and the owner bind (FUN_1406343b0,
/// itemsocketsystemutil.cpp:2534: the transfer's binding and every socketed gem's bind must be the character).
/// </para>
/// <para>
/// This is the worn gem's gate: not valid for layer-2 outfits or custom-note scores (both skipped, unreachable for a
/// gem). Also not ported, each unreachable for a worn gem: the customize preview's flag 1 (CItem::SetFlags,
/// customize.cpp:217; a packet item never has it, so the pc bang / vip / wedding tests run), the feature-0xd4 fitting
/// doll skip of the bind (pc.cpp:4696: no FittingDollPC exists in this field, so the bind is always checked), m_n1198 of
/// IsVip (set only by the fitting doll slot, fittingdollslot_1.cpp:264) and the layer test (GetVariant == 2,
/// pc.cpp:4704: m_24 is only set on the layer-2 outfits, SetVariant(2) at scenestategame.cpp:11264, never on a
/// PCGemEquip badge, and the data's kind fallback is 0, 1 or 3, item.cpp:1629). STILL-OPEN: the custom-note parts of
/// FUN_140528980 (FUN_140525730 / FUN_140525680, item.cpp:813/1745: the musicscore row's IsCustomNote and the score's
/// m_n38, neither parsed) — a worn gem is never a score item (type 0xc), so the score branch keeps only the client's
/// remaining-uses gate here.
/// </para>
/// </summary>
public static class PcItemUse
{
    /// <summary>
    /// The owner's state the check reads, as the add packets leave it: the PC bang grade byte (PC m_nFF8), the premium
    /// club time (m_n1190, FieldAddUser), the wedding state (m_n1220, FieldAddUser; the own player's is the wedding manager's, see WeddingManagerState) and the character id
    /// (FUN_141656a80). ServerTime is the client's GetServerTime: the session's clock, falling back to the local UTC
    /// when there is no session.
    /// </summary>
    public sealed class Pc
    {
        public byte PcBangGrade;
        public long PremiumTime;
        public short WeddingState;
        /// <summary>The own player's wedding manager: MyPC::vfunc_146 (mypc.cpp:1409) asks it instead of the field state. Null for another player.</summary>
        public WeddingManagerState Manager;
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
        if (!PcBangOk(data, pc) || !VipOk(data, pc) || !WeddingOk(data, pc)) return false;
        return OwnerBindOk(item, pc);
    }

    /// <summary>IsPCBangItem (the data's PCBang flag, item.cpp:1004) against the PC bang grade (FUN_141658f30: grade above 0).</summary>
    public static bool PcBangOk(ItemData data, Pc pc) => !data.PCBang || pc.PcBangGrade != 0;

    /// <summary>FUN_140525db0 (the limit flag "vip", item.cpp:1013) against IsVip (pc.cpp:2209: the premium time is ahead of the server time).</summary>
    public static bool VipOk(ItemData data, Pc pc) => !data.Vip || pc.PremiumTime > pc.ServerTime();

    /// <summary>
    /// HasWeddingLimit ("wedding", item.cpp:1022) against vfunc_146: MyPC's (mypc.cpp:1409) is WeddingManager::IsInProgress
    /// (weddingmanager.cpp:1174, the manager's proxy state 2, 3 or 4: <see cref="WeddingManagerState"/>); another PC's (PC::vfunc_146,
    /// pc.cpp:2218) is its own wedding state 2, 3 or 4.
    /// </summary>
    public static bool WeddingOk(ItemData data, Pc pc) =>
        !data.Wedding || (pc.Manager?.IsInProgress ?? WeddingManagerState.InProgress(pc.WeddingState));

    /// <summary>
    /// FUN_1406343b0 (itemsocketsystemutil.cpp:2534): the transfer's binding must be the character
    /// (CItemTransfer::IsBindingCompatible, :2881) and so must every socketed gem's bind (AllGemsUsableByCharacter,
    /// TSocketGemStoneInfo::IsUsableBy itemsocket.cpp:139).
    /// </summary>
    public static bool OwnerBindOk(EquipItem item, Pc pc)
    {
        if (item == null) return false;
        if (item.Transfer.HasBinding && item.Transfer.BindingId != pc.CharacterId) return false;
        if (item.Socket != null)
            foreach (var g in item.Socket.Gems)
            {
                if (g == null || g.GemId == 0) continue;      // an empty slot (IsEmpty: gem id 0)
                // TSocketGemStoneInfo::IsBound (itemsocket.cpp:131): a bind with character id 0 is not bound
                if (g.HasBind && g.BindCharacterId != 0 && g.BindCharacterId != pc.CharacterId) return false;
            }
        return true;
    }

    /// <summary>
    /// CItem::FUN_140528980 (item.cpp:1717): an expired item is unusable unless it is a pet (data type 0xb,
    /// itemdata.cpp:675) or has a maid id (type 6, subtype 7, param2, itemdata.cpp:1163); a score item (type 0xc) needs
    /// a use left (m_a0, the record's RemainUses).
    /// </summary>
    public static bool ItemUsable(EquipItem item, ItemData data, Pc pc)
    {
        bool expired = item.ExpiryTime > 0 && item.ExpiryTime <= pc.ServerTime();   // CItem::IsExpired (item.cpp:1303, m_80)
        if (expired && !data.IsPetItem && !data.HasMaidId) return false;
        if (data.IsScoreItem && item.RemainUses < 1) return false;
        return true;
    }
}
