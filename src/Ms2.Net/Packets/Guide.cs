using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

// The guide system's packets (CGuideManager, client/ms2/guideentity_1b.cpp; the guide actions, client/ms2/guideaction.cpp).

/// <summary>
/// GuideRecord inbound (0x89), CGuideManager::ReceiveStates (@0x14138b280): u32 count, then count × (i32 save id, i32 step);
/// a pair with save id 0 is skipped. Then u8 the 8-bit set the manager keeps (m_bits; what its bits mean is STILL-OPEN, no
/// reader of it is known).
/// </summary>
public sealed record GuideRecordIn(IReadOnlyList<(int SaveId, int Step)> Steps, byte Bits) {
    public static GuideRecordIn Read(ByteReader r) {
        uint n = (uint) r.ReadInt();
        var steps = new List<(int, int)>();
        for (uint i = 0; i < n; i++) {
            int key = r.ReadInt(); int value = r.ReadInt();
            if (key == 0) continue;
            steps.Add((key, value));
        }
        var p = new GuideRecordIn(steps, r.ReadByte());
        r.ExpectEnd("GuideRecord");
        return p;
    }
}

/// <summary>GuideRecord outbound (0x57), CGuideManager::SendStates (@0x14138b170): i32 count, then (i32 save id, i32 step) per
/// saved step in ascending save id order (the manager's CRBMap). MyPC::SendFunctionCube (@0x1415293b0) sends it when GetStateChanged is set.</summary>
public sealed record GuideRecordOut(IReadOnlyList<(int SaveId, int Step)> Steps) : IOutPacket {
    public Outbound Op => Outbound.GuideRecord;
    public void Write(ByteWriter w) {
        w.WriteInt(Steps.Count);
        foreach (var (key, value) in Steps) { w.WriteInt(key); w.WriteInt(value); }
    }
}

/// <summary>CGuideActionServerTriggerEvent::Start (@0x141385e60): Trigger (0x2F) with u8 9, u8 1 and i32 the event id. The GMS2 client's
/// Trigger requests all start with 8 instead of 9 (every recorded one, 40+ sessions: 08 01 id here, 08 05 id for <see cref="CutSceneExitTrigger"/>).</summary>
public sealed record GuideServerTriggerEvent(int EventId) : IProfileOutPacket {
    public Outbound Op => Outbound.Trigger;
    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);
    public void Write(ByteWriter w, ProtocolProfile profile) { w.WriteByte(profile.IsGms2 ? (byte) 8 : (byte) 9); w.WriteByte(1); w.WriteInt(EventId); }
}

/// <summary>UICutSceneMovie::Exit (uicutscenemovie.cpp:425): Trigger (0x2F) with u8 9 (GMS2 8), u8 5 and i32 the cut scene id, when a cut scene movie ends.</summary>
public sealed record CutSceneExitTrigger(int CutSceneId) : IProfileOutPacket {
    public Outbound Op => Outbound.Trigger;
    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);
    public void Write(ByteWriter w, ProtocolProfile profile) { w.WriteByte(profile.IsGms2 ? (byte) 8 : (byte) 9); w.WriteByte(5); w.WriteInt(CutSceneId); }
}

/// <summary>CGuideActionRequestTutorialItem::Start (@0x141386150): RequestTutorialItem (0x9C), no body.</summary>
public sealed record RequestTutorialItem : IOutPacket {
    public Outbound Op => Outbound.RequestTutorialItem;
    public void Write(ByteWriter w) { }
}

/// <summary>CGuideActionClearTutorial::Start (@0x141386600): Tutorial (0x8A), no body.</summary>
public sealed record ClearTutorial : IOutPacket {
    public Outbound Op => Outbound.Tutorial;
    public void Write(ByteWriter w) { }
}
