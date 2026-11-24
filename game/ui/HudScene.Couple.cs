using System;
using Godot;
using Ms2.Formats.Nif;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using Ms2.Ui.Skills;

namespace Ms2Godot.Ui;

/// <summary>
/// The couple emotion's network side and the own StateCoupleEmotion's world: CNetworkService::OnBuddyEmote
/// (<see cref="CoupleEmotionInbound.Handle"/>), the invite box's answer (UIMessageBox::HandleCoupleEmotionButton), the
/// ride off before an accept (MyPC::vfunc_121 case 0x13) and the own state's partner, path nif and stop.
/// STILL-OPEN: CEffectService::SetTick of mode 5 (the effect clock is not ported).
/// </summary>
public partial class HudScene
{
    CoupleHost coupleHost;
    Action unwatchCouple;
    Player couplePlayer;

    /// <summary>The live session's BuddyEmote, the couple ride off and the box answer, once per session.</summary>
    void WatchCouple()
    {
        var live = Net.LiveSession.Instance;
        if (live == null) return;
        Action<BuddyEmoteMessage> onEmote = m => { if (CoupleReady(live) is { } h) CoupleEmotionInbound.Handle(m, h, h.Me.Couple); };
        Action<long, int> onRideOff = (id, emotion) => { if (CoupleReady(live) is { } h) CoupleEmotionInbound.Request(id, emotion, h, h.Me.Couple); };
        live.BuddyEmoteReceived += onEmote;
        live.CoupleAfterRideOff += onRideOff;
        MessageBoxLayer.CoupleEmotionAnswer = AnswerCoupleEmotion;
        Net.RemoteLayer.CouplePathLoader = LoadCouplePath;
        unwatchCouple = () =>
        {
            live.BuddyEmoteReceived -= onEmote; live.CoupleAfterRideOff -= onRideOff;
            if (MessageBoxLayer.CoupleEmotionAnswer == AnswerCoupleEmotion) MessageBoxLayer.CoupleEmotionAnswer = null;
            Net.RemoteLayer.CouplePathLoader = null;
        };
    }

    /// <summary>HandleCoupleEmotionButton's answer (the message box layer calls it).</summary>
    public void AnswerCoupleEmotion(int button, uint emotion, long characterId)
    {
        var live = Net.LiveSession.Instance;
        if (live == null) return;
        if (CoupleReady(live) is { } h) CoupleEmotionInbound.Answer(button, (int)emotion, characterId, h, h.Me.Couple);
        // no own PC: the decline still goes (the client's no reads only the box)
        else if (button != 0) live.SendBuddyEmote(new BuddyEmoteDecline((int)emotion, characterId));
    }

    /// <summary>The host over the own PC; null without one (WorldService::GetLocalPlayer fails). The PC's couple hooks are
    /// set the first time a PC is seen.</summary>
    CoupleHost CoupleReady(Net.LiveSession live)
    {
        if (player == null || !IsInstanceValid(player)) player = FindPlayer(this);
        if (player == null) return null;
        if (coupleHost == null || coupleHost.Me != player) coupleHost = new CoupleHost(this, player, live);
        if (couplePlayer != player) WireCouplePlayer(player, live);
        return coupleHost;
    }

    void WireCouplePlayer(Player me, Net.LiveSession live)
    {
        couplePlayer = me;
        me.CouplePartner = id => live.CouplePartnerOf(id);
        me.CoupleMaxDistance = World.FieldContents.Constants(assets.Packs).Int("CoupleEmotionMaxDistance", CoupleEmotionCast.DefaultMaxDistance);
        me.CouplePathLoader = LoadCouplePath;
        Net.RemoteLayer.CouplePathLoader ??= LoadCouplePath;
        me.CoupleStop += notify => { if (CoupleReady(live) is { } h) CoupleEmotionInbound.Stop(notify, h, me.Couple); };
    }

    NifSpec coupleSpec;

    /// <summary>CNifLoadingService::Load of the path nif (Data/Resource/Model/Path/...), then the node lookups.</summary>
    CouplePath LoadCouplePath(string path, string ownerNode, string peerNode)
    {
        const string prefix = "Data/Resource/Model/Path/";
        string entry = (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? path[prefix.Length..] : path).ToLowerInvariant();
        try
        {
            coupleSpec ??= ModelBuilder.SharedSpec(Settings.NifXml);
            if (!assets.Packs.Exists("Resource/Model/Path", entry)) return null;
            return CouplePath.Load(NifFile.Load(coupleSpec, assets.Packs.Read("Resource/Model/Path", entry)), ownerNode, peerNode);
        }
        catch (Exception e)
        {
            GD.PushWarning($"couple: {path}: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// What the couple code reads of the world and does to it. The own PC's state calls (vfunc_59 to 0x3a or StateIdle, the
    /// sub) go to <see cref="Player"/>; the partner lookups to the remote layer; the sends to the live session.
    /// </summary>
    sealed class CoupleHost : ICoupleEmotionHost
    {
        readonly HudScene hud;
        readonly Net.LiveSession live;
        public readonly Player Me;

        public CoupleHost(HudScene hud, Player me, Net.LiveSession live) { this.hud = hud; Me = me; this.live = live; }

        public bool RefuseRequests => GameOptions.AccountCommunity.DisableCouplemotionRequest;
        public bool HasPlayer => IsInstanceValid(Me);
        public int StateId => Me.ClientStateId;
        public bool InCubeMode => live.CubeCraft.InCubeMode;
        public bool OnWater => Me.OnWater;
        public int SubSceneId => live.Guide?.World.SubSceneId ?? 0;
        public long CharacterId => live.CharacterId;
        public int ObjectId => live.Me.ObjectId;
        public System.Numerics.Vector3 Position { get { var g = Me.GlobalPosition; return new(g.X, -g.Z, g.Y); } }
        public System.Numerics.Vector3 Rotation => new(0f, 0f, Me.FacingRotZ);
        public string Name => hud.hudModel?.Player?.Name ?? "";
        public string ShownBoxExtra => MessageBoxQueue.Current() is { } box ? box.Extra : null;
        public int MaxDistance => Me.CoupleMaxDistance;
        public bool Riding => Me.Mount != null;
        public bool Blocked(long characterId) => live.Buddies.IsBlocked(characterId);
        public CouplePeer FindPeer(long characterId) => live.CouplePeerOf(characterId);
        public CouplePeer FindPeerByObject(int objectId) => live.CouplePeerByObject(objectId);
        public void Send(Ms2.Net.Wire.IOutPacket p) => live.SendBuddyEmote(p);

        public void Notice(int id, string name)
        {
            var t = hud.hudModel.Tables;
            hud.SystemNoticeText(name != null ? Ms2.Quest.ClientFormat.Format(t.Common(id), name) : t.Common(id));
        }

        public void ShowInviteBox(int emotion, long characterId, string name)
        {
            var t = hud.hudModel.Tables;
            int duration = World.FieldContents.Constants(hud.assets.Packs).Int("CoupleEmotionRequestDuration", CoupleEmotionInviteBox.DefaultDuration);
            CoupleEmotionInviteBox.Show(emotion, characterId, name, t.SkillName(unchecked((uint)emotion)), duration);
        }

        public void EnterIdle() => Me.LeaveCouple();

        /// <summary>vfunc_59(0x3a, data, sub, 1) with StateCoupleEmotion::vfunc_4's reads: the emotion's skill row, the
        /// partner's gender, the table.</summary>
        public void EnterCouple(CoupleEmotionStart data, int sub)
        {
            var t = hud.hudModel.Tables;
            var row = t.Skills.GetSkillData(data.Emotion);
            Me.EnterCouple(data, sub, row, live.GenderOf(data.PartnerId), t.CoupleEmotions, live.Me.ObjectId);
        }

        public void SetSub(int sub) => Me.SetCoupleSub(sub);
        public void SetEffectTick(int tick) { }
        public void RideOffForCouple(int emotion, long characterId) => live.RideOffForSkill(new RideOffCoupleEmotion(0, emotion, characterId));

        public bool FindPlacement(out System.Numerics.Vector3 posA, out System.Numerics.Vector3 posB, out float yaw)
        {
            var probe = new CouplePlaceProbe(Me.GetWorld3D().DirectSpaceState);
            return Me.FindCouplePlacement(p => CoupleEmotionCast.Placeable(probe, p), probe.RayHits, out posA, out posB, out yaw);
        }
    }
}
