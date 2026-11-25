using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Replay;
using Ms2.Net.Wire;

namespace Ms2.Net.Tests;

/// <summary>
/// Shared helper that feeds a minimal FieldAddUser (KMS 0x17, GMS2 0x17) into a <see cref="FieldTracker"/> using
/// <see cref="FieldTracker.Profile"/> so subsequent recorded packets for that object id hit a known actor.
/// </summary>
static class FieldTrackerSeed {
    public static void AddUser(FieldTracker t, int objectId, string name = "Actor") {
        bool gms = t.Profile.IsGms2;
        var w = new ByteWriter();
        w.WriteInt(objectId);
        // UserBlock (Characters.cs: locale "NA" adds two i64 values in the platform block)
        w.WriteLong(1); w.WriteLong(2); w.WriteUnicodeString(name);
        w.WriteByte(0); w.WriteByte(0); w.WriteLong(0); w.WriteInt(0);
        w.WriteInt(8); w.WriteInt(0); w.WriteInt(0); w.WriteShort(21); w.WriteShort(1);
        w.WriteInt(10); w.WriteInt(100); w.WriteInt(2100); w.WriteInt(1630); w.WriteShort(0);
        w.WriteLong(0); w.WriteLong(0); w.WriteLong(0); w.WriteInt(0);
        w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(0); w.WriteInt(0);
        for (int i = 0; i < 8; i++) w.WriteByte(0);
        w.WriteLong(0); w.WriteInt(0); w.WriteInt(0); w.WriteInt(0);
        w.WriteLong(0); w.WriteUnicodeString(""); w.WriteUnicodeString(""); w.WriteUnicodeString("");
        w.WriteByte(0); w.WriteByte(0);
        for (int i = 0; i < 12; i++) w.WriteInt(0);
        w.WriteUnicodeString(""); w.WriteLong(0);
        if (gms) { w.WriteLong(0); w.WriteLong(0); }
        w.WriteInt(0); w.WriteByte(0); w.WriteBool(false); w.WriteLong(0); w.WriteInt(0);
        w.WriteInt(0); w.WriteLong(0); w.WriteInt(0); w.WriteLong(0);
        w.WriteInt(0); w.WriteInt(0); w.WriteShort(0); w.WriteLong(0);
        // SkillBook (SkillBook.cs: GMS2 writes job + group count; KMS2 writes rank + job group + skill count)
        if (gms) { w.WriteInt(100); w.WriteByte(0); }
        else { w.WriteInt(1); w.WriteInt(100); w.WriteByte(0); }
        w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(0);
        w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(0);
        w.WriteByte(0); w.WriteByte(0x23);
        for (int i = 0; i < 3; i++) { w.WriteLong(2100); w.WriteInt(0); w.WriteInt(0); w.WriteInt(0); w.WriteInt(0); }
        w.WriteBool(false); w.WriteByte(0);
        w.WriteInt(0); w.WriteLong(0); w.WriteLong(0); w.WriteBool(false); w.WriteInt(0);
        for (int i = 0; i < 8; i++) w.WriteByte(0);
        w.WriteUnicodeString(""); w.WriteBool(false);
        w.WriteInt(0); w.WriteLong(0); w.WriteInt(0); w.WriteInt(0);
        void Block(params byte[] body) { w.WriteBool(false); w.WriteInt(body.Length); w.WriteBytes(body); }
        Block(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        Block(0);
        Block(0);
        w.WriteShort(0);
        w.WriteInt(0); w.WriteInt(0); w.WriteByte(0); w.WriteInt(0); w.WriteByte(0); w.WriteByte(0);
        w.WriteInt(0); w.WriteShort(0); w.WriteBool(false);
        w.WriteInt(0); w.WriteBool(false);
        w.WriteLong(0); w.WriteInt(0); w.WriteByte(0);
        if (gms) { w.WriteInt(0); w.WriteInt(0); }
        w.WriteShort(0);
        t.Inbound(new RawPacket(t.Profile.Number(Inbound.FieldAddUser), w.Buffer.AsSpan(0, w.Length).ToArray()));
        t.Events.Clear();
    }
}
