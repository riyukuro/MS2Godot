using System.Numerics;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;

namespace Ms2.Ui.Skills;

/// <summary>
/// The data side of StateCoupleEmotion (statecoupleemotion.cpp, client state 0x3a): its StateCoupleEmotionData (the emotion,
/// the partner's and the requester's object ids, the animation index, the start point and the end angles) and its sub
/// state m_n10: 0x93 the requester waiting, 0x94 the accepter placing, 0x95 started by the server, 0x96 playing.
/// </summary>
public sealed class CoupleEmotionState
{
    public const int Waiting = 0x93, Accepting = 0x94, Started = 0x95, Playing = 0x96;

    public bool Active { get; private set; }
    /// <summary>m_n10 (FUN_14166cc80).</summary>
    public int Sub { get; private set; }
    public uint Emotion { get; private set; }
    public int PartnerId { get; private set; }
    public int RequesterId { get; private set; }
    /// <summary>m_nAni, the index into the row's animations.</summary>
    public int Ani { get; set; }
    /// <summary>m_posStart: where the pair's path nif stands.</summary>
    public Vector3 PosStart { get; set; }
    /// <summary>m_posEnd: the path nif's angles in degrees.</summary>
    public Vector3 PosEnd { get; set; }

    /// <summary>vfunc_59(0x3a, data, sub, 1): the data kept and the sub set.</summary>
    public void Enter(CoupleEmotionStart d, int sub)
    {
        Active = true;
        Sub = sub;
        Emotion = d.Emotion;
        PartnerId = d.PartnerId;
        RequesterId = d.RequesterId;
        Ani = 0;
        PosStart = d.PosStart;
        PosEnd = d.PosEnd;
    }

    /// <summary>The state left (any vfunc_59 to another state).</summary>
    public void Exit() { Active = false; Sub = 0; }

    /// <summary>FUN_14166cc40: the sub changes (State::vfunc_2).</summary>
    public void SetSub(int sub) => Sub = sub;

    /// <summary>IsRequester @0x140b82190: the owner's object id is the requester's.</summary>
    public bool IsRequester(int ownerObjectId) => ownerObjectId == RequesterId;

    /// <summary>StateSyncObjectCoupleEmotion::vfunc_1 (@0x1408369e0, statesyncobject.cpp:294-302) after the base record:
    /// the requester (i32), the partner (i32), the animation (u8), m_posC4 = the end angles, m_posD0 = the start (vec3 each).
    /// The emotion goes in the base record's m_n64 (StateCoupleEmotionData::vfunc_1, statecoupleemotion.cpp:174-186).</summary>
    public byte[] SyncTail()
    {
        var w = new ByteWriter();
        w.WriteInt(RequesterId);
        w.WriteInt(PartnerId);
        w.WriteByte((byte)Ani);
        w.WriteFloat(PosEnd.X); w.WriteFloat(PosEnd.Y); w.WriteFloat(PosEnd.Z);
        w.WriteFloat(PosStart.X); w.WriteFloat(PosStart.Y); w.WriteFloat(PosStart.Z);
        return w.Buffer.AsSpan(0, w.Length).ToArray();
    }

    /// <summary>StateSyncObjectCoupleEmotion::vfunc_2 (@0x140836ab0) over a record's tail, then StateCoupleEmotionData::vfunc_2
    /// (@0x140b7fbe0): the data a remote PC's 0x3a carries; null when the tail is short.</summary>
    public static CoupleEmotionStart ReadTail(uint emotion, byte[] tail, out int ani)
    {
        ani = 0;
        if (tail == null || tail.Length < 4 + 4 + 1 + 24) return null;
        var r = new ByteReader(tail);
        int requester = r.ReadInt(), partner = r.ReadInt();
        ani = r.ReadByte();
        var end = new Vector3(r.ReadFloat(), r.ReadFloat(), r.ReadFloat());
        var start = new Vector3(r.ReadFloat(), r.ReadFloat(), r.ReadFloat());
        return new CoupleEmotionStart(emotion, partner, requester, start, end);
    }
}

/// <summary>What the couple emotion code reads of and does to the world, the own PC and the UI.</summary>
public interface ICoupleEmotionHost
{
    /// <summary>The game option m_disableCoupleEmotionRequest (the option settings' byte at +10).</summary>
    bool RefuseRequests { get; }
    /// <summary>WorldService::GetLocalPlayer exists.</summary>
    bool HasPlayer { get; }
    /// <summary>The own GetCurStateId.</summary>
    int StateId { get; }
    bool InCubeMode { get; }
    /// <summary>PCBase::FUN_141675ed0, the own PC on water.</summary>
    bool OnWater { get; }
    /// <summary>GFxSceneService::GetSubSceneId.</summary>
    int SubSceneId { get; }
    /// <summary>The own FUN_141656a80 (the character id) and GetObjectId.</summary>
    long CharacterId { get; }
    int ObjectId { get; }
    /// <summary>The own vfunc_18 (the feet) and vfunc_20 (the angles in degrees).</summary>
    Vector3 Position { get; }
    Vector3 Rotation { get; }
    /// <summary>The own name (vfunc_77).</summary>
    string Name { get; }
    /// <summary>FUN_14151f820's shown box extra, null when no box is up.</summary>
    string ShownBoxExtra { get; }
    /// <summary>CoupleEmotionMaxDistance.</summary>
    int MaxDistance { get; }
    /// <summary>PCBase::IsRiding.</summary>
    bool Riding { get; }
    /// <summary>CBuddyManager::FUN_140ae51b0: the character is on the block list.</summary>
    bool Blocked(long characterId);
    /// <summary>WorldService::FUN_141bd1530 (by character id) and FindPCById (by object id); null when none.</summary>
    CouplePeer FindPeer(long characterId);
    CouplePeer FindPeerByObject(int objectId);
    void Send(IOutPacket p);
    /// <summary>A notice: FUN_141275260(id, 1, 1) when <paramref name="name"/> is null, else
    /// ShowSystemNotice(FormatStringId(id, name), 1, 1).</summary>
    void Notice(int id, string name);
    /// <summary>The invite's yes / no box (<see cref="CoupleEmotionInbound.Invite"/>).</summary>
    void ShowInviteBox(int emotion, long characterId, string name);
    /// <summary>vfunc_59(1, 0, 0, 0): the own PC to StateIdle (the couple state, if any, left).</summary>
    void EnterIdle();
    /// <summary>vfunc_59(0x3a, data, sub, 1).</summary>
    void EnterCouple(CoupleEmotionStart data, int sub);
    /// <summary>FUN_14166cc40: the own state's sub.</summary>
    void SetSub(int sub);
    /// <summary>CEffectService::SetTick.</summary>
    void SetEffectTick(int tick);
    /// <summary>SendRequestRideMode1 with RideOffActionCoupleEmotion(emotion, character id): off the mount first.</summary>
    void RideOffForCouple(int emotion, long characterId);
    /// <summary>StateCoupleEmotion::FindPlacement (@0x140b82230) on the own state just entered; false when no spot.</summary>
    bool FindPlacement(out Vector3 posA, out Vector3 posB, out float yaw);
}

/// <summary>CNetworkService::OnBuddyEmote (@0x14153e280, networkservice.cpp:1096-1184), the invite box's answer and the
/// own state's stop.</summary>
public static class CoupleEmotionInbound
{
    /// <summary>The invite box's tag, s_coupleEmotion (s_143af3d28).</summary>
    public const string BoxTag = CoupleEmotionCast.BoxExtra;

    /// <summary>The own couple state (FUN_14166ccb0) when the own PC is in 0x3a, else null.</summary>
    static CoupleEmotionState Own(ICoupleEmotionHost h, CoupleEmotionState own) =>
        h.StateId == CoupleEmotionCast.StateCoupleEmotion && own is { Active: true } ? own : null;

    /// <summary>OnBuddyEmote: each mode's checks in the client's order. <paramref name="own"/> is the own couple state.</summary>
    public static void Handle(BuddyEmoteMessage m, ICoupleEmotionHost h, CoupleEmotionState own)
    {
        switch (m)
        {
            case BuddyEmoteMessage.Invite i: Invite(i, h); break;
            case BuddyEmoteMessage.InviteSent s:
            {
                // :1133-1137: the target must be in the field (assert return), notice 0xce6 with its name
                var t = h.FindPeer(s.CharacterId);
                if (t == null) return;
                h.Notice(0xce6, t.Name);
                break;
            }
            case BuddyEmoteMessage.Failed f:
            {
                // :1138-1143: in 0x3a, FUN_140b84010(result) then StateIdle
                if (!h.HasPlayer) return;
                if (h.StateId != CoupleEmotionCast.StateCoupleEmotion) break;
                FailNotice(f.Reason, h, own);
                h.EnterIdle();
                break;
            }
            case BuddyEmoteMessage.Accepted a:
            {
                // :1144-1153: notice 0xce8 when the own request (sub 0x93) is the one accepted
                var t = h.FindPeer(a.CharacterId);
                if (t == null || !h.HasPlayer) return;
                var s = Own(h, own);
                if (s == null || s.Sub != CoupleEmotionState.Waiting) break;
                if (s.Emotion != (uint)a.Emotion || s.PartnerId != t.ObjectId) break;
                h.Notice(0xce8, null);
                break;
            }
            case BuddyEmoteMessage.Declined d:
            {
                // :1154-1163: the own request declined: StateIdle, notice 0xce9 with the name
                if (!h.HasPlayer) return;
                if (h.StateId != CoupleEmotionCast.StateCoupleEmotion) break;
                if (own?.Sub != CoupleEmotionState.Waiting) break;
                var t = h.FindPeer(d.CharacterId);
                if (t == null || own is not { Active: true }) return;
                if (own.Emotion != (uint)d.Emotion || own.PartnerId != t.ObjectId) break;
                h.EnterIdle();
                h.Notice(0xce9, t.Name);
                break;
            }
            case BuddyEmoteMessage.Start st:
            {
                // :1164-1174: the partner is whichever of the two is not this character; the state takes the server's
                // start and angles, the effect clock its tick, then sub 0x95
                if (!h.HasPlayer) return;
                if (h.StateId != CoupleEmotionCast.StateCoupleEmotion) break;
                long peer = h.CharacterId != st.First ? st.First : st.Second;
                var t = h.FindPeer(peer);
                if (t == null || own is not { Active: true }) return;
                if (own.Emotion != (uint)st.Emotion || own.PartnerId != t.ObjectId) break;
                own.PosStart = st.StartPosition;
                own.PosEnd = st.EndAngles;
                h.SetEffectTick(st.Tick);
                h.SetSub(CoupleEmotionState.Started);
                break;
            }
            case BuddyEmoteMessage.Left l:
            {
                // :1175-1182: the partner left: StateIdle
                if (!h.HasPlayer) return;
                var t = h.FindPeer(l.CharacterId);
                if (t == null) return;
                if (h.StateId != CoupleEmotionCast.StateCoupleEmotion) break;
                if (own is not { Active: true }) return;
                if (own.Emotion != (uint)l.Emotion || own.PartnerId != t.ObjectId) break;
                h.EnterIdle();
                break;
            }
        }
    }

    /// <summary>
    /// Mode 0 (:1098-1132): the refuse option answers mode 2 reason 3 at once; without the own PC nothing. In 0x3a: reason 2
    /// and notice 0xceb; a couple box up: reason 1 and 0xceb; the asker blocked, cube mode or the sub scene 0x620001f:
    /// reason 5 and 0xcfa. Else the yes / no box (0xce5 with the asker's name and the emotion's name, tag "coupleEmotion",
    /// the emotion, the asker's character id and name as its arguments, the CoupleEmotionRequestDuration timer, not
    /// blocking, modaless, no Enter) and mode 1 back.
    /// </summary>
    public static void Invite(BuddyEmoteMessage.Invite i, ICoupleEmotionHost h)
    {
        if (h.RefuseRequests) { h.Send(new BuddyEmoteRefuse(i.CharacterId, 3)); return; }
        if (!h.HasPlayer) return;
        if (h.StateId == CoupleEmotionCast.StateCoupleEmotion) { h.Send(new BuddyEmoteRefuse(i.CharacterId, 2)); h.Notice(0xceb, null); return; }
        if (!CoupleEmotionCast.NoCoupleBox(h.ShownBoxExtra)) { h.Send(new BuddyEmoteRefuse(i.CharacterId, 1)); h.Notice(0xceb, null); return; }
        if (h.Blocked(i.CharacterId) || h.InCubeMode || h.SubSceneId == 0x620001f)
        {
            h.Send(new BuddyEmoteRefuse(i.CharacterId, 5));
            h.Notice(0xcfa, null);
            return;
        }
        h.ShowInviteBox(i.Emotion, i.CharacterId, i.Name ?? "");
        h.Send(new BuddyEmoteInviteReceived(i.CharacterId));
    }

    /// <summary>FUN_140b84010 @0x140b84010 (statecoupleemotion.cpp:1218-1279): the notice of a failure reason, with the
    /// partner's name when the own PC is in 0x3a and the partner is found (else "").</summary>
    public static void FailNotice(int why, ICoupleEmotionHost h, CoupleEmotionState own)
    {
        string name = "";
        if (h.HasPlayer && Own(h, own) is { } s && h.FindPeerByObject(s.PartnerId) is { } p) name = p.Name;
        h.Notice(FailNoticeId(why), name);
    }

    /// <summary>FUN_140b84010's switch.</summary>
    public static int FailNoticeId(int why) => why switch
    {
        0 => 0xcf2, 4 => 0xcf3, 1 => 0xcf4, 2 => 0xcf5, 6 => 0xcf8, 7 => 0xcea, 8 => 0xcf9, 5 => 0xcf7, 3 => 0xcf6, 9 => 0xcf0,
        _ => 0xce7,
    };

    /// <summary>UIMessageBox::HandleCoupleEmotionButton (uimessagebox.cpp:2273-2301): no (any button but 0) sends mode 4 with
    /// the box's emotion and asker; yes while riding asks the ride off with RideOffActionCoupleEmotion, else
    /// <see cref="Request"/>.</summary>
    public static void Answer(int button, int emotion, long characterId, ICoupleEmotionHost h, CoupleEmotionState own)
    {
        if (button != 0) { h.Send(new BuddyEmoteDecline(emotion, characterId)); return; }
        if (h.Riding) { h.RideOffForCouple(emotion, characterId); return; }
        Request(characterId, emotion, h, own);
    }

    /// <summary>
    /// RequestCoupleEmotion @0x140b845a0 (statecoupleemotion.cpp:1326-1418): the asker not in the field: notice 0xcf1. Too
    /// far: mode 2 reason 8 and 0xcee with its name; on water or a state that may not start: reason 5 and 0xcfa. Else
    /// 0x3a sub 0x94 with the own feet and angles (the partner and the requester both the asker's object id), then
    /// FindPlacement: none is reason 7, 0xcea with the own name and StateIdle; a spot sends mode 3 with it.
    /// </summary>
    public static bool Request(long characterId, int emotion, ICoupleEmotionHost h, CoupleEmotionState own)
    {
        if (!h.HasPlayer) return false;
        var t = h.FindPeer(characterId);
        if (t == null) { h.Notice(0xcf1, null); return false; }
        if (!CoupleEmotionCast.WithinDistance(h.Position, t.Position, h.MaxDistance))
        {
            h.Send(new BuddyEmoteRefuse(characterId, 8));
            h.Notice(0xcee, t.Name);
            return false;
        }
        if (h.OnWater || !CoupleEmotionCast.MayStart(h.StateId))
        {
            h.Send(new BuddyEmoteRefuse(characterId, 5));
            h.Notice(0xcfa, null);
            return false;
        }
        h.EnterCouple(new CoupleEmotionStart((uint)emotion, t.ObjectId, t.ObjectId, h.Position, h.Rotation), CoupleEmotionState.Accepting);
        if (h.StateId != CoupleEmotionCast.StateCoupleEmotion) return false;
        if (!h.FindPlacement(out var a, out var b, out float yaw))
        {
            h.Send(new BuddyEmoteRefuse(characterId, 7));
            h.Notice(0xcea, h.Name);
            h.EnterIdle();
            return false;
        }
        h.Send(new BuddyEmoteAccept(emotion, characterId, a, b, yaw));
        return true;
    }

    /// <summary>StateCoupleEmotion::Stop @0x140b82aa0 (:915-937): StateIdle; with the partner in the field, mode 6 (the
    /// emotion, the partner's character id) and, with <paramref name="notify"/>, notice 0xcee with its name.</summary>
    public static void Stop(bool notify, ICoupleEmotionHost h, CoupleEmotionState own)
    {
        if (!h.HasPlayer) return;
        uint emotion = own?.Emotion ?? 0;
        int partner = own?.PartnerId ?? 0;
        h.EnterIdle();
        var p = partner != 0 ? h.FindPeerByObject(partner) : null;
        if (p == null) return;
        h.Send(new BuddyEmoteCancel((int)emotion, p.CharacterId));
        if (notify) h.Notice(0xcee, p.Name);
    }
}
