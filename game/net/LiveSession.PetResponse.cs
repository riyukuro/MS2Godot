using Godot;
using Ms2.Formats.Options;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Net;

// ResponsePet (CPetManager::OnResponsePet) and OnFieldAddPet's owner part: the own pet's summon fields, the bag slot mark, the
// game menu's pet buttons, the notices and the summon sound.
public partial class LiveSession
{
    /// <summary>CPetManager: m_nSummonUid and m_nAuto.</summary>
    public readonly PetManager Pets = new();

    /// <summary>UIInventoryDialog::RefreshItemSlot(uid) of the open bag (the hud sets it).</summary>
    public Action<long> PetItemSlotRefresh;
    /// <summary>UIGameMenuDialog::InitPetInfo of the open game menu (the hud sets it).</summary>
    public Action<bool, bool, int> PetInfoInit;

    /// <summary>s_strPetSummonSound / DAT_143ae7988 "02100199" (scenestategame.cpp:1202) in the system sound group.</summary>
    const string PetSummonSound = "ms2sound/sound/system/02100199";
    /// <summary>FUN_1411a5110's object kind for the summon sound (petmanager.cpp:860, scenestategame.cpp:9729).</summary>
    const int PetSummonSoundKind = 1;
    /// <summary>The own pc's hide reason a pet takes over when it is made (scenestategame.cpp:9716-9717).</summary>
    const int PetOwnerHideReason = 1;

    PetResponseHost petHost;
    /// <summary>UIGameMenuDialog::OnPetAttack's CPetManager::SendRequestPet (uigamemenudialog.cpp:2441-2447): the new m_nAuto.</summary>
    public int TogglePetAuto() => Pets.ToggleAuto(Send);

    /// <summary>The own pc's last made pet: its object id and CPet m_nPetId (SetPetId, scenestategame.cpp:9697).</summary>
    (int ObjectId, int PetId) ownFieldPet;

    /// <summary>
    /// UIGameMenuDialog::RefreshMenu's pet part (uigamemenudialog.cpp:1513-1518): the local pc's pet object (FUN_141676a10, the
    /// m_unk_b70 object) gives (its row's m_nType == 1, CPetManager::GetAuto); null without the object. A pet whose row is not in
    /// the table reads type 0 (the client reads a null row).
    /// </summary>
    public (bool AttackType, int Auto)? OwnPetMenuInfo()
    {
        int id = Me.PetObjectId;
        if (id == 0 || !(remote?.HasNpc(id) ?? false)) return null;
        int? type = null;
        if (ownFieldPet.ObjectId == id) try { type = CaptionTables().Pets.TypeOf((uint)ownFieldPet.PetId); } catch { }
        return (type == 1, Pets.Auto);
    }

    /// <summary>CPetManager::OnResponsePet @0x140c895e0 (petmanager.cpp:722-745).</summary>
    void OnResponsePet(Ms2.Net.Wire.RawPacket p)
    {
        var r = PetResponse.Read(p.Reader(), game.Profile);
        if (LiveCheck) GD.Print($"live: response pet mode {r.Mode} owner {r.OwnerId} value {r.Value} uid {r.Item?.Uid ?? r.Uid}");
        Pets.OnResponsePet(r, petHost ??= new PetResponseHost(this));
    }

    /// <summary>
    /// OnFieldAddPet's owner part (scenestategame.cpp:9712-9731) once the pet is made: for the local pc's pet
    /// SendRequestPetInventoryMode7 and AddHideReason(1) while the own pc has reason 1; then with an owner and vfunc_127 the
    /// summon sound at the owner's position.
    /// </summary>
    void OnFieldPetAdded(FieldAddPet pet)
    {
        bool own = selfObject != 0 && pet.OwnerId == selfObject && player != null && IsInstanceValid(player);
        if (own)
        {
            ownFieldPet = (pet.ObjectId, pet.NpcId);
            Send(new PetInventoryLoadRequest());
            if (player.HasHideReason(Player.HideReasonField)) remote?.SetNpcHideReason(pet.ObjectId, PetOwnerHideReason, true);
        }
        if (PetOwnerAt(pet.OwnerId) is { } at && PetShown(pet.ObjectId, own)) PlayPetSummonSound(at);
    }

    /// <summary>
    /// CPet::vfunc_127 @0x140b11460 (unit_140b0f4c0_petactor.cpp:706) for a pet with an owner: not while FUN_140b10150 holds
    /// (DisplayHideMyPet with the own pc's pet, ShowOnlyMyPet with another's, or any hide reason), nor ShowOnlyMyPet with another's.
    /// </summary>
    bool PetShown(int petObjectId, bool ownerIsMine)
    {
        var status = GameOptions.Current?.StatusOption;
        if (ownerIsMine && (status?.DisplayHideMyPet ?? false)) return false;
        if (!ownerIsMine && (status?.DisplayShowOnlyMyPet ?? false)) return false;
        return !(remote?.NpcHidden(petObjectId) ?? false);
    }

    /// <summary>The owner pc's position (vfunc_18) in client coordinates; null when the pc is not here (GetOwnerPC 0).</summary>
    System.Numerics.Vector3? PetOwnerAt(int ownerId)
    {
        if (ownerId != 0 && ownerId == selfObject && player != null && IsInstanceValid(player)) return ClientPositionOf(player);
        return remote?.PositionOf(ownerId);
    }

    /// <summary>FUN_1411a5110("ms2sound/sound/system", s_strPetSummonSound, "", 1, at, 1.0, 0).</summary>
    void PlayPetSummonSound(System.Numerics.Vector3 at)
    {
        if (LiveCheck) GD.Print($"live: pet summon sound at {at}");
        FieldAudio.Instance?.PlaySoundEvent(PetSummonSound, PetSummonSoundKind, new Vector3(at.X, at.Z, -at.Y),
            player != null && IsInstanceValid(player) ? player.GlobalPosition : null);
    }

    sealed class PetResponseHost : IPetResponseHost
    {
        readonly LiveSession s;
        public PetResponseHost(LiveSession s) { this.s = s; }

        bool Self(int objectId) => objectId != 0 && objectId == s.selfObject && s.player != null && IsInstanceValid(s.player);

        public bool PcExists(int objectId) => Self(objectId) || (s.remote?.HasUser(objectId) ?? false);
        public bool IsLocalPc(int objectId) => Self(objectId);

        /// <summary>The own pc's pet fields; another pc's pet item is not kept by the port (its field pet comes and goes by
        /// FieldAddPet / FieldRemovePet), so nothing changes for it.</summary>
        public void SetPcPet(int objectId, bool replaceItem, EquipItem item, int petObjectId, long summonedUid)
        {
            if (!Self(objectId)) return;
            if (replaceItem) s.Me.Pet = item;
            s.Me.PetObjectId = petObjectId;
            s.Me.PetUid = summonedUid;
        }

        public long PcSummonedUid(int objectId) => Self(objectId) ? s.Me.PetUid : 0;
        public void RefreshItemSlot(long uid) => s.PetItemSlotRefresh?.Invoke(uid);

        public int? PetTypeOfItem(long uid)
        {
            var item = s.Items.Get(uid);
            int petId = item != null ? ItemDataOf(item.Id)?.PetId ?? 0 : 0;
            if (petId == 0) return null;
            try { return CaptionTables().Pets.TypeOf((uint)petId); } catch { return null; }
        }

        public void InitPetInfo(bool up, bool attackType, int auto) => s.PetInfoInit?.Invoke(up, attackType, auto);

        public void Notice(int commonId)
        {
            var text = MessageBoxData.Common(commonId);
            if (!string.IsNullOrEmpty(text)) s.SystemNoticeSink?.Invoke(text);
        }

        public void MessageBox(int commonId) => MessageBoxQueue.ShowMessageBox(MessageBoxData.Common(commonId), 0, null, "");
        public void OpenCharge() => NotApplied.Print("pet: the meret purchase dialog (CUIService::FUN_141add1f0) is not ported");

        public bool OwnPetShown(int ownerObjectId) =>
            Self(ownerObjectId) && s.Me.PetObjectId != 0 && (s.remote?.HasNpc(s.Me.PetObjectId) ?? false) && s.PetShown(s.Me.PetObjectId, true);

        public void PlaySummonSound(int ownerObjectId)
        {
            if (s.PetOwnerAt(ownerObjectId) is { } at) s.PlayPetSummonSound(at);
        }
    }
}
