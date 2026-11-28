using Ms2.Net.Packets;
using Ms2.Net.Wire;

namespace Ms2.Ui;

/// <summary>What <see cref="PetManager.OnResponsePet"/> asks of the game: the pcs, the own pc's pet fields, the dialogs and the notices.</summary>
public interface IPetResponseHost
{
    /// <summary>WorldService::FindPCById found the pc (the own pc or another one).</summary>
    bool PcExists(int objectId);
    /// <summary>The pc is the local player (GetLocalPlayer).</summary>
    bool IsLocalPc(int objectId);
    /// <summary>
    /// The pc's pet fields after PC::ReadPetItem and FUN_141676a50 / FUN_141676ab0: with <paramref name="replaceItem"/> the pet
    /// item (m_spItem11b8) becomes <paramref name="item"/> (null clears it), else it stays; m_unk_b70 <paramref name="petObjectId"/>,
    /// m_nUidB78 <paramref name="summonedUid"/>. The equipment's refresh flag (CEquipment::FUN_1411e7660, m_54) goes with it.
    /// </summary>
    void SetPcPet(int objectId, bool replaceItem, EquipItem item, int petObjectId, long summonedUid);
    /// <summary>The pc's pet item uid (FUN_1416769f0, m_nUidB78), 0 for none.</summary>
    long PcSummonedUid(int objectId);
    /// <summary>UIInventoryDialog::RefreshItemSlot(uid) (0x4e2d), when the bag is open.</summary>
    void RefreshItemSlot(long uid);
    /// <summary>CPetDataTable::Find(the item <paramref name="uid"/>'s pet id)->m_nType (GetSummonedPetRow), null without a row.</summary>
    int? PetTypeOfItem(long uid);
    /// <summary>UIGameMenuDialog::InitPetInfo (0x4e28) when the game menu is open; the feature 0x97 test is the menu's.</summary>
    void InitPetInfo(bool up, bool attackType, int auto);
    /// <summary>FUN_141275260(id, 1, 1): a common system notice.</summary>
    void Notice(int commonId);
    /// <summary>FUN_140d73bc0(id, 0, 0, ""): a common string message box.</summary>
    void MessageBox(int commonId);
    /// <summary>CUIService::FUN_141add1f0: the cash charge window.</summary>
    void OpenCharge();
    /// <summary>GetPlayerPet(owner) found the pet object, its owner is the local player and the pet's vfunc_127 holds.</summary>
    bool OwnPetShown(int ownerObjectId);
    /// <summary>CEffectService::FUN_1411a5110("ms2sound/sound/system", s_strPetSummonSound, "", 1, the owner's position, 1, 0).</summary>
    void PlaySummonSound(int ownerObjectId);
}

/// <summary>
/// CPetManager's summon part (petmanager.cpp:715-1160): m_nSummonUid, m_nAuto and OnResponsePet for the summon modes.
/// <para>The modes it applies: 0 OnPetSummoned, 1 OnPetUnsummoned, 2 OnPetSummonSound, 0xf OnPetAutoChanged, 0x13 OnPetError.
/// The rest are read and left: 4 rename, 5 potion options, 6 loot flags, 7 / 8 the counts, 9 the pet window's info, 10 / 0xb
/// experience and level, 0xc fusion, 0x10 another pc's pet info (the inspect window), 0x11 / 0x12 evolve, 0x14 the trap skill,
/// 0x15 the pet item rewrite; their windows (the pet window, inventory, potion slot, taming, evolve, rename) are not ported.</para>
/// </summary>
public sealed class PetManager
{
    /// <summary>m_nSummonUid: the own summoned pet item's uid, set by ResponsePet 0 / 1 only.</summary>
    public long SummonUid { get; private set; }
    /// <summary>m_nAuto: the pet's attack mode, 1 at construction (CPetManager::CPetManager @0x140c894b0, petmanager.cpp:715).</summary>
    public int Auto { get; private set; } = 1;

    /// <summary>The common string ids the handlers show.</summary>
    public const int UnsummonedNotice = 0xc0c, ErrorDefaultNotice = 0x979;

    /// <summary>
    /// SendRequestPet @0x140c8b340 (petmanager.cpp:1118): m_nAuto flips, RequestPet 0xf with m_nAuto == 1 goes out, the new
    /// m_nAuto comes back (UIGameMenuDialog::OnPetAttack hands it to setPetAttackState).
    /// </summary>
    public int ToggleAuto(Action<IOutPacket> send)
    {
        Auto = Auto == 0 ? 1 : 0;
        send?.Invoke(new PetAttackRequest(Auto == 1));
        return Auto;
    }

    /// <summary>CPetManager::OnResponsePet @0x140c895e0 (petmanager.cpp:722-745) for one read packet.</summary>
    public void OnResponsePet(PetResponse r, IPetResponseHost h)
    {
        if (r == null || h == null) return;
        switch (r.Kind)
        {
            case PetResponseKind.Summoned: OnPetSummoned(r, h); break;
            case PetResponseKind.Unsummoned: OnPetUnsummoned(r, h); break;
            case PetResponseKind.SummonSound: OnPetSummonSound(r, h); break;
            case PetResponseKind.AutoChanged: Auto = r.Flag != 0 ? 1 : 0; break;    // OnPetAutoChanged @0x140c8b070: m_nAuto = ReadBool
            case PetResponseKind.Error: OnPetError(r.Value, h); break;
        }
    }

    /// <summary>
    /// OnPetSummoned @0x140c89b20 (petmanager.cpp:791-815): the owner (FindPCById) or nothing; ReadPetItem, FUN_141676a50(its
    /// uid, the packet's value), the equipment's refresh flag; for the local pc m_nSummonUid, the bag slot of the uid and the game
    /// menu's InitPetInfo(1, row type == 1, m_nAuto) when the summoned row is found.
    /// ReadPetItem without an item clears the pet item and keeps m_nUidB78; with an id the item data does not know it returns
    /// before both (pc.cpp:3357-3380).
    /// </summary>
    void OnPetSummoned(PetResponse r, IPetResponseHost h)
    {
        if (!h.PcExists(r.OwnerId)) return;
        long uid = h.PcSummonedUid(r.OwnerId);
        if (r.Item == null) h.SetPcPet(r.OwnerId, true, null, r.Value, uid);                   // no item: m_spItem11b8 = 0, m_nUidB78 kept
        else if (r.Item.Item == null) h.SetPcPet(r.OwnerId, false, null, r.Value, uid);        // FUN_140523c40 failed: both kept
        else h.SetPcPet(r.OwnerId, true, r.Item.Item, r.Value, uid = r.Item.Uid);              // SetPetItem, FUN_1416769a0(uid)
        if (!h.IsLocalPc(r.OwnerId)) return;
        SummonUid = uid;
        h.RefreshItemSlot(uid);
        if (h.PetTypeOfItem(SummonUid) is int type) h.InitPetInfo(true, type == 1, Auto);
    }

    /// <summary>
    /// OnPetUnsummoned @0x140c89d00 (petmanager.cpp:817-842): the owner or nothing; FUN_141676ab0(uid), SetPetItem(0), the
    /// equipment's refresh flag; for the local pc m_nSummonUid 0, the bag slot of the uid, InitPetInfo(0, 0, 0), the pet window's
    /// FUN_140e74640(2) (not ported) and notice 0xc0c.
    /// </summary>
    void OnPetUnsummoned(PetResponse r, IPetResponseHost h)
    {
        if (!h.PcExists(r.OwnerId)) return;
        h.SetPcPet(r.OwnerId, true, null, 0, 0);
        if (!h.IsLocalPc(r.OwnerId)) return;
        SummonUid = 0;
        h.RefreshItemSlot(r.Uid);
        h.InitPetInfo(false, false, 0);
        h.Notice(UnsummonedNotice);
    }

    /// <summary>OnPetSummonSound @0x140c89ea0 (petmanager.cpp:845-865): the owner's pet object, owned by the local player and
    /// shown (vfunc_127), plays the summon sound at the owner.</summary>
    void OnPetSummonSound(PetResponse r, IPetResponseHost h)
    {
        if (h.OwnPetShown(r.OwnerId)) h.PlaySummonSound(r.OwnerId);
    }

    /// <summary>OnPetError @0x140c8ae50 (petmanager.cpp:1042-1055) by the error code.</summary>
    public static void OnPetError(int error, IPetResponseHost h)
    {
        switch (error)
        {
            case 0x1b: h.Notice(0x34); break;
            case 0x1c: h.OpenCharge(); break;
            case 0x20: h.MessageBox(0xc24); break;
            case 0x1e: h.Notice(0xf2); break;
            case 0x27: break;
            case 0x21: h.Notice(0xc21); break;
            default: h.Notice(ErrorDefaultNotice); break;
        }
    }
}
