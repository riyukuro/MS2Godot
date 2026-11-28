using Godot;
using Ms2.Formats.Options;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui;
using Ms2.Ui.Hud;
using Ms2Godot.Movement;

namespace Ms2Godot.Net;

// A ride item used from a quickslot: MyPC::UseRideeItem, and StateSummon (0x26) for a ride or a pet item.
public partial class LiveSession
{
    SummonCast summonCast;
    /// <summary>The own pc's StateSummon, for a ride item and a pet item alike.</summary>
    public SummonCast SummonState => summonCast ??= new SummonCast(new SummonHost(this));

    int? summonRideeDuration;
    /// <summary>ClientConstants SummonRideeDuration (5000 when the tables do not set it).</summary>
    int SummonRideeDuration => summonRideeDuration ??=
        (int)(Constants()?.Float("SummonRideeDuration", RideItemUse.DefaultSummonDuration) ?? RideItemUse.DefaultSummonDuration);

    int? petSummonCastTime;
    /// <summary>ClientConstants PetSummonCastTime (Value&lt;unsigned int&gt; +0x7d30, 700u by default:
    /// unit_1402ab440_constantsblocks.cpp:1089).</summary>
    int PetSummonCastTime => petSummonCastTime ??= Constants()?.Int("PetSummonCastTime", 700) ?? 700;

    /// <summary>StateSummon vfunc_4's effect for m_n10 0x83 (the pet summon) on the owner.</summary>
    const string PetOnEffect = "CH/Common/Eff_PetOn.xml";

    /// <summary>CPetDataTable::Find(petId) finds a row.</summary>
    static bool PetRowKnown(int petId)
    {
        try { return CaptionTables().Pets.TypeOf((uint)petId) != null; } catch { return false; }
    }

    int? rideeFieldId;
    bool rideeFieldUse = true, epFieldUse = true;
    /// <summary>WorldService::FUN_141bcb210's field part: the current field's &lt;ui useRidee&gt; (no field: false, as m_298 null);
    /// unreadable field data passes.</summary>
    bool FieldUsesRidee()
    {
        if (FieldId is not int id) return false;
        ReadFieldUse(id);
        return rideeFieldUse;
    }

    /// <summary>The current field's &lt;ui useEPSkill&gt; (CFieldData m_nA4, default 1), which Ridee::UpdateRunEffects reads
    /// (ridee.cpp:752); no field or unreadable field data reads the default.</summary>
    bool FieldUsesEp()
    {
        if (FieldId is not int id) return true;
        ReadFieldUse(id);
        return epFieldUse;
    }

    void ReadFieldUse(int id)
    {
        if (rideeFieldId == id) return;
        rideeFieldId = id;
        try
        {
            var f = Ms2.Formats.World.FieldData.FindById(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir), id);
            rideeFieldUse = f?.UseRidee ?? true;
            epFieldUse = f?.UseEPSkill ?? true;
            petFieldUse = f?.UsePet ?? true;
        }
        catch { rideeFieldUse = epFieldUse = petFieldUse = true; }
    }

    /// <summary>
    /// MyPC::UseRideeItem (@0x14152ba80, mypc.cpp:4627) for the item <paramref name="uid"/>: the facts as far as the port
    /// knows them, then <see cref="RideItemUse.Decide"/>.
    /// STILL-OPEN: the kept mount action (m_spB00) is not kept here; CanUseItem's checks and box; FUN_1415271e0's water row
    /// fields (m_n88, kind 1/2, event buff type 3), so in water it refuses; FUN_141bcb210's cube map flag 5 (a house's
    /// permission) is taken as off; the PC bang, VIP, wedding and owner-bind refusals are never raised.
    /// </summary>
    public void UseRideeItem(long uid)
    {
        if (player == null || !IsInstanceValid(player) || selfObject <= 0) return;
        var item = Items.Get(uid);
        var data = item != null ? ItemDataOf(item.Id) : null;
        int rideeId = data?.RideeId ?? 0;
        var mount = player.Mount;
        var f = new RideItemFacts
        {
            RideeKnown = rideeId != 0 && RideeRow(rideeId) != null,
            ItemRideeId = rideeId,
            CurrentRideeId = mount?.Data.Id ?? 0,
            Riding = mount != null,
            MountState = mount != null ? Player.StateId(player.State) : 0,
            CanUseItem = item != null,
            SkillUsable = !player.OnWater,
            StateAllows = LiveInteractObjects.StateAllowsInteraction(InteractPlayerStateId()),
            MapAllows = FieldUsesRidee(),
            Expired = item != null && item.ExpiryTime > 0 && item.ExpiryTime <= ServerTime,
        };
        switch (RideItemUse.Decide(f, out int noticeId, out string noticeKey))
        {
            case RideItemUse.Outcome.RideOff:
                // SendRequestRideMode1(null): a RideOffAction(0), type 0 and m_n20 0
                var ride = mount?.Data;
                if (RideOff.Request(true, ride != null, ride?.Type ?? 0, ride?.EnableRideOffUI ?? false, 0,
                        false, new RideOffRequest(), Send) == RideOff.Outcome.CantRide)
                    SystemNoticeSink?.Invoke(MessageBoxData.Common(RideOff.CantRideNoticeId));
                break;
            case RideItemUse.Outcome.Summon:
                StartRideSummon(uid, rideeId);
                break;
            case RideItemUse.Outcome.RideAlong:
                Send(new RideAlongRequest(rideeId, uid));
                break;
            case RideItemUse.Outcome.Notice:
                var text = noticeKey != null ? MessageBoxData.CommonByKey(noticeKey) : MessageBoxData.Common(noticeId);
                if (!string.IsNullOrEmpty(text)) SystemNoticeSink?.Invoke(text);
                break;
        }
    }

    /// <summary>The quick slot item's CanUseAtLevel for CSkillSystem::FUN_141764e30 (skillslot.cpp:2093-2103): null when the
    /// bag has no item <paramref name="uid"/> (FindItemByUid), else its levelLimit against the own level.</summary>
    public bool? ItemAtOwnLevel(long uid)
    {
        var item = Items.Get(uid);
        if (item == null) return null;
        var data = ItemDataOf(item.Id);
        return data == null || data.IsLevelAllowed(Me.User?.Level ?? 0);
    }

    /// <summary>vfunc_59(0x26, StateSummonData(uid, 0)) for a ride item: StateSummon reads the row's rideSummonCastTime or
    /// SummonRideeDuration itself.</summary>
    void StartRideSummon(long uid, int rideeId) => SummonState.Start(uid, false, HeldItem);

    /// <summary>The riding row (GetRideeData), null for an unknown id.</summary>
    static RideData RideeRow(int rideeId)
    {
        try { return RideData.Load(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir), rideeId); }
        catch { return null; }
    }

    /// <summary>OnResponseRide mode 2 for the own pc (scenestategame.cpp:12052-12062): after the ride off, the summon state
    /// for the answered ridee and item.</summary>
    void SummonAfterRideOff(ResponseRide r)
    {
        if (RideeRow(r.RideeId) == null) return;
        StartRideSummon(r.ItemUid, r.RideeId);
    }

    sealed class SummonHost : ISummonHost
    {
        readonly LiveSession s;
        public SummonHost(LiveSession s) { this.s = s; }

        public bool HasPlayer => s.player != null && IsInstanceValid(s.player) && s.selfObject > 0;
        public void Send(IOutPacket packet) => s.Send(packet);

        public bool StartCasting(ICastingEventListener listener, string text, int durationMs)
        {
            if (s.CastingBar == null) return false;
            s.CastingBar.StartCastingText(listener, text ?? "", durationMs, showTime: false, activeBar: true, endText: "");
            return true;
        }

        public void CancelCasting() => s.CastingBar?.Cancel();
        public long NowMs => (long)Time.GetTicksMsec();

        public void PlayPlayerAnim(string animName, bool loop)
        {
            if (HasPlayer) s.player.StartInteractAnim(animName, loop);
        }

        public void StopPlayerInteractAnim()
        {
            if (s.player != null && IsInstanceValid(s.player)) s.player.StopInteractAnim();
        }

        public SummonItemKind SummonItem(long itemUid, out bool rowFound, out int rideeId)
        {
            rowFound = false;
            rideeId = 0;
            var item = s.Items.Get(itemUid);
            var data = item != null ? ItemDataOf(item.Id) : null;
            if (data == null) return SummonItemKind.None;
            if (data.IsPetItem)
            {
                rowFound = PetRowKnown(data.PetId);
                return SummonItemKind.Pet;
            }
            if (!data.IsRideItem) return SummonItemKind.None;
            rideeId = data.RideeId;
            rowFound = RideeRow(rideeId) != null;
            return SummonItemKind.Ride;
        }

        public int SummonRideeDuration => s.SummonRideeDuration;
        public int PetSummonCastTime => s.PetSummonCastTime;
        public int RideCastMs(int rideeId) => RideItemUse.CastMs(RideeRow(rideeId)?.SummonCastTime, s.SummonRideeDuration);
        public string CommonString(int id) => MessageBoxData.Common(id);
        public void PlayPetOnEffect() => s.PlayOwnEffect(PetOnEffect);
        public bool RideOption => GameOptions.Current.CommunityOption.DisableSingleRiding;
    }
}
