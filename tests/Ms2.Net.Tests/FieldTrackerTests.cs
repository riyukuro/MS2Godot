using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// The tracker's routing of the live field's stat and battle packets: the npc and PC forms of OnStat (the PC's reader is
/// PC::vfunc_90, pc.cpp:1645, not the own player's) and OnUserBattle, which the live head bars hang on. A stat for an id
/// nobody added is dropped, as the client's actor lookup drops it.
/// </summary>
public class FieldTrackerTests {
    static byte[] Bytes(ByteWriter w) => w.Buffer.AsSpan(0, w.Length).ToArray();
    static RawPacket Packet(Inbound op, byte[] body) => new((ushort) op, body);

    /// <summary>A FieldAddUser in the client's layout (the reader of Field.cs decodes to its last byte), a member of the
    /// field with no equips, no buffs and no pet, so the tracker's actor table has the id the stat packets name.</summary>
    static void AddUser(FieldTracker t, int objectId, long characterId) {
        var w = new ByteWriter();
        w.WriteInt(objectId);
        // UserBlock (locale KR)
        w.WriteLong(1); w.WriteLong(characterId); w.WriteUnicodeString("Member");
        w.WriteByte(0); w.WriteByte(0); w.WriteLong(0); w.WriteInt(0);
        w.WriteInt(8); w.WriteInt(0); w.WriteInt(0); w.WriteShort(21); w.WriteShort(1);
        w.WriteInt(10); w.WriteInt(100); w.WriteInt(2100); w.WriteInt(1630); w.WriteShort(0);
        w.WriteLong(0); w.WriteLong(0); w.WriteLong(0); w.WriteInt(0);
        w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(0);
        w.WriteInt(0);
        w.WriteByte(0); w.WriteByte(0); w.WriteByte(0); w.WriteByte(0); w.WriteByte(0); w.WriteByte(0); w.WriteByte(0); w.WriteByte(0);
        w.WriteLong(0); w.WriteInt(0); w.WriteInt(0); w.WriteInt(0);
        w.WriteLong(0); w.WriteUnicodeString(""); w.WriteUnicodeString(""); w.WriteUnicodeString("");
        w.WriteByte(0); w.WriteByte(0);
        for (int i = 0; i < 12; i++) w.WriteInt(0);
        w.WriteUnicodeString(""); w.WriteLong(0);
        w.WriteInt(0); w.WriteByte(0); w.WriteBool(false); w.WriteLong(0); w.WriteInt(0);
        w.WriteInt(0); w.WriteLong(0); w.WriteInt(0); w.WriteLong(0);
        w.WriteInt(0);                                    // LongListB
        w.WriteInt(0);                                    // the second count list
        w.WriteShort(0); w.WriteLong(0);                  // survival level, the last long
        // SkillBook: rank 1, job group 100, no skills
        w.WriteInt(1); w.WriteInt(100); w.WriteByte(0);
        w.WriteFloat(-900); w.WriteFloat(-900); w.WriteFloat(3175);     // position
        w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(135);            // rotation
        w.WriteByte(0); w.WriteByte(0x23);                              // the stats block
        for (int i = 0; i < 3; i++) { w.WriteLong(2100); w.WriteInt(0); w.WriteInt(0); w.WriteInt(0); w.WriteInt(0); }
        w.WriteBool(false);                                            // in battle
        w.WriteByte(0);
        w.WriteInt(0); w.WriteLong(0); w.WriteLong(0); w.WriteBool(false);   // held cube
        w.WriteInt(0);
        w.WriteByte(0); w.WriteByte(0); w.WriteByte(0); w.WriteByte(0); w.WriteByte(0); w.WriteByte(0); w.WriteByte(0); w.WriteByte(0);   // skin colors
        w.WriteUnicodeString(""); w.WriteBool(false);
        w.WriteInt(0); w.WriteLong(0); w.WriteInt(0); w.WriteInt(0);
        void Block(params byte[] body) { w.WriteBool(false); w.WriteInt(body.Length); w.WriteBytes(body); }
        Block(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0); // equips: none, no skin tabs
        Block(0);                                                      // the unknown block
        Block(0);                                                      // badges: none
        w.WriteShort(0);                                               // buffs
        w.WriteInt(0); w.WriteInt(0); w.WriteByte(0); w.WriteInt(0); w.WriteByte(0); w.WriteByte(0);
        w.WriteInt(0); w.WriteShort(0); w.WriteBool(false);            // title, insignia, shown
        w.WriteInt(0); w.WriteBool(false);
        w.WriteLong(0); w.WriteInt(0); w.WriteByte(0); w.WriteShort(0);
        t.Inbound(Packet(Inbound.FieldAddUser, Bytes(w)));
    }

    [Test]
    public void PlayerStatRoutesToUserStat() {
        var w = new ByteWriter();
        w.WriteInt(77); w.WriteByte(0); w.WriteByte(2);
        w.WriteByte(4); w.WriteLong(1000); w.WriteLong(900); w.WriteLong(650);   // health: total, base, current
        w.WriteByte(7); w.WriteInt(100); w.WriteInt(100); w.WriteInt(40);
        var t = new FieldTracker();
        AddUser(t, 77, 2);
        t.Events.Clear();
        t.Inbound(Packet(Inbound.Stat, Bytes(w)));
        var e = t.Events.Single() as UserStatEvent;
        Assert.That(e, Is.Not.Null);
        Assert.That(e.Stat.ObjectId, Is.EqualTo(77));
        Assert.That(e.Stat.TryGet(PcStat.Health, out long max, out long cur), Is.True);
        Assert.That((max, cur), Is.EqualTo((1000L, 650L)));
        Assert.That(t.StatDrops, Is.EqualTo(0));
    }

    [Test]
    public void PlayerStatFullBlockRoutesToUserStat() {
        // PC::vfunc_90's 0x23 branch: the block FieldAddUser also carries (the kms2525 server's StatsPacket.Update(entity)
        // broadcast), three stat rows of health and the four speeds
        var w = new ByteWriter();
        w.WriteInt(77); w.WriteByte(0); w.WriteByte(0x23);
        long[] health = { 500, 480, 470 };
        for (int row = 0; row < 3; row++) {
            w.WriteLong(health[row]); w.WriteInt(100 + 10 * row); w.WriteInt(0); w.WriteInt(0); w.WriteInt(0);
        }
        var t = new FieldTracker();
        AddUser(t, 77, 2);
        t.Events.Clear();
        t.Inbound(Packet(Inbound.Stat, Bytes(w)));
        var e = t.Events.Single() as UserStatEvent;
        Assert.That(e, Is.Not.Null);
        Assert.That(e.Stat.TryGet(PcStat.Health, out long max, out long cur), Is.True);
        Assert.That((max, cur), Is.EqualTo((500L, 470L)));       // row 0 is the total, row 2 the current
        Assert.That(e.Stat.TryGet(PcStat.AttackSpeed, out _, out var atk), Is.True);
        Assert.That(atk, Is.EqualTo(120));                        // row 2's attack speed
    }

    [Test]
    public void StatForAnUnknownIdIsDropped() {
        var w = new ByteWriter();
        w.WriteInt(88); w.WriteByte(0); w.WriteByte(0x23);
        for (int row = 0; row < 3; row++) { w.WriteLong(500); w.WriteInt(1); w.WriteInt(2); w.WriteInt(3); w.WriteInt(4); }
        var t = new FieldTracker();
        t.Inbound(Packet(Inbound.Stat, Bytes(w)));
        Assert.That(t.Events, Is.Empty);                        // no actor with that id: OnStat returns
        Assert.That(t.StatDrops, Is.EqualTo(0));
    }

    [Test]
    public void ABuffOfAPcInTheFieldRoutesToItsList() {
        // OnBuff (scenestategame.cpp:7591): FUN_141bd1200 of the owner header's id, nothing when it is not in the field
        static byte[] Remove(int owner) { var w = new ByteWriter(); w.WriteByte(1); w.WriteInt(owner); w.WriteInt(9); w.WriteInt(owner); return Bytes(w); }
        var t = new FieldTracker();
        t.Inbound(Packet(Inbound.Buff, Remove(77)));
        Assert.That(t.Events, Is.Empty);
        AddUser(t, 77, 2);
        t.Events.Clear();
        t.Inbound(Packet(Inbound.Buff, Remove(77)));
        Assert.That(t.Events.Single(), Is.EqualTo(new UserBuffEvent(0, new BuffPacket(BuffPacket.Remove, 77, 9, 77, 0, null, null))));
    }

    [Test]
    public void UserBattleRoutesToTheStanceEvent() {
        var w = new ByteWriter();
        w.WriteInt(88); w.WriteByte(1);
        var t = new FieldTracker();
        t.Inbound(Packet(Inbound.UserBattle, Bytes(w)));
        Assert.That(t.Events.Single(), Is.EqualTo(new UserBattleEvent(0, 88, 1)));
    }

    static void AddNpc(FieldTracker t, int objectId) {
        var w = new ByteWriter();
        w.WriteInt(objectId); w.WriteInt(21000001);
        w.WriteFloat(1); w.WriteFloat(2); w.WriteFloat(3);
        w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(90);
        w.WriteByte(0);                                   // not the single-stat form
        for (int i = 0; i < 3; i++) { w.WriteLong(100); w.WriteInt(10); }
        w.WriteByte(0); w.WriteShort(0); w.WriteLong(0); w.WriteByte(1); w.WriteInt(1); w.WriteInt(0); w.WriteByte(1);
        t.Inbound(Packet(Inbound.FieldAddNpc, Bytes(w)));
    }

    static byte[] Block(int objectId, byte flags) {
        var b = new ByteWriter();
        b.WriteInt(objectId); b.WriteByte(flags);
        b.WriteShort(10); b.WriteShort(20); b.WriteShort(30);   // position
        b.WriteShort(0);                                          // rotation
        b.WriteShort(0); b.WriteShort(0); b.WriteShort(0);       // velocity
        b.WriteShort(100);                                        // animation speed
        b.WriteByte(0); b.WriteShort(1); b.WriteShort(1);        // state, sequence, serial
        return Bytes(b);
    }

    [Test]
    public void NpcControlEmotionBitGivesStanceEventsOnlyOnChanges() {
        var t = new FieldTracker { NpcLayoutOf = _ => default };
        AddNpc(t, 5);
        void Control(params byte[][] blocks) {
            var w = new ByteWriter();
            w.WriteShort((short) blocks.Length);
            foreach (var bl in blocks) { w.WriteShort((short) bl.Length); w.WriteBytes(bl); }
            t.Inbound(Packet(Inbound.NpcControl, Bytes(w)));
        }
        Control(Block(5, 2));              // ApplySyncBlock's emotion bit set: a change from the spawn's 0
        Assert.That(t.Events.OfType<NpcStanceEvent>().Single(), Is.EqualTo(new NpcStanceEvent(0, 5, true)));
        Control(Block(5, 2));              // unchanged: SetEmotion's guard skips it
        Control(Block(5, 0));              // cleared: one change event, out of battle
        Assert.That(t.Events.OfType<NpcStanceEvent>().Last(), Is.EqualTo(new NpcStanceEvent(0, 5, false)));
        Assert.That(t.Events.Count(e => e is NpcStanceEvent), Is.EqualTo(2));
    }
}
