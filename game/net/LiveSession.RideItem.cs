using Godot;
using Ms2.Formats.Options;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui;
using Ms2.Ui.Hud;
using Ms2Godot.Movement;

namespace Ms2Godot.Net;

// A ride item used from a quickslot: MyPC::UseRideeItem and StateSummon (0x26).
public partial class LiveSession
{
    RideSummonCast rideSummon;
    /// <summary>The ride item summon state (StateSummon for a ride item).</summary>
    public RideSummonCast RideSummon => rideSummon ??= new RideSummonCast(new RideSummonHost(this));

    int? summonRideeDuration;
    /// <summary>ClientConstants SummonRideeDuration (5000 when the tables do not set it).</summary>
    int SummonRideeDuration => summonRideeDuration ??=
        (int)(Constants()?.Float("SummonRideeDuration", RideItemUse.DefaultSummonDuration) ?? RideItemUse.DefaultSummonDuration);

    /// <summary>
    /// MyPC::UseRideeItem (@0x14152ba80, mypc.cpp:4627) for the item <paramref name="uid"/>: the facts as far as the port
    /// knows them, then <see cref="RideItemUse.Decide"/>.
    /// STILL-OPEN: the kept mount action (m_spB00) is not kept here; CanUseItem's checks and box; FUN_1415271e0's water row
    /// fields (m_n88, kind 1/2, event buff type 3), so in water it refuses; FUN_141bcb210's map info +0xac and the cube map
    /// flag 5 are taken as passing; the PC bang, VIP, wedding and owner-bind refusals are never raised.
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
            MapAllows = true,
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

    /// <summary>vfunc_59(0x26, StateSummonData(uid, 0)): the cast for the row's rideSummonCastTime or SummonRideeDuration.</summary>
    void StartRideSummon(long uid, int rideeId) =>
        RideSummon.Start(uid, RideItemUse.CastMs(RideeRow(rideeId)?.SummonCastTime, SummonRideeDuration),
            MessageBoxData.Common(RideSummonCast.LabelId), HeldItem);

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

    sealed class RideSummonHost : IRideSummonHost
    {
        readonly LiveSession s;
        public RideSummonHost(LiveSession s) { this.s = s; }

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

        public bool RideeRowOf(long itemUid, out int rideeId)
        {
            var item = s.Items.Get(itemUid);
            rideeId = item != null ? ItemDataOf(item.Id)?.RideeId ?? 0 : 0;
            return rideeId != 0 && RideeRow(rideeId) != null;
        }

        public bool RideOption => GameOptions.Current.CommunityOption.DisableSingleRiding;
    }
}
