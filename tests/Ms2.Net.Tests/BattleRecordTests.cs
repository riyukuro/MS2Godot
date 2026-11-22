using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>CBattleRecordManager (battlerecordmanager.cpp): the groups, RecordDamage, the value texts, the SkillDamage records
/// it is fed, and CPartyManager's member records.</summary>
public class BattleRecordTests {
    // objects 10 and 11 are players (characters 1000, 1100), 20 a pet of 11, 30 a class 6 npc, 31 a class 3 npc
    static BattleRecordManager Manager(uint now = 5000) => new() {
        Tick = () => now,
        CharacterIdOf = id => id switch { 10 => 1000, 11 => 1100, _ => 0 },
        PetOwnerOf = id => id == 20 ? 11 : 0,
        NpcClassOf = id => id switch { 30 => 6, 31 => 3, _ => 0 },
    };

    static PartyUser User(long charId) => new() { CharacterId = charId, Name = "M" + charId };

    [Test]
    public void RecordDamageTalliesTheAttackerAndTheTargetInActiveGroupsOnly() {
        var m = Manager();
        m.AddPartyMemberRecord(1000);
        m.AddPartyMemberRecord(1100);
        m.RecordDamage(10, 30, new[] { new Hit(0, -100), new Hit(1, -50) });
        Assert.That(m.GetRecord(1, 1000).Amount(BattleRecord.Dealt), Is.Zero, "group 1 not started: no tally");

        m.StartGroup(1);
        m.RecordDamage(10, 30, new[] { new Hit(0, -100), new Hit(1, -50), new Hit(2, 0), new Hit(3, -7) });
        var r = m.GetRecord(1, 1000);
        Assert.That(r.Amount(BattleRecord.Attacks), Is.EqualTo(4), "slot 2: the value count");
        Assert.That(r.Amount(BattleRecord.Dealt), Is.EqualTo(157), "slot 0: |signed total|");
        Assert.That(r.Amount(BattleRecord.BossDealt), Is.EqualTo(157), "slot 7: the npc's class is 6");
        Assert.That(r.Amount(BattleRecord.Hits), Is.EqualTo(2), "kinds 0 and 1 hit; 2 (miss) and 3 do not count");
        Assert.That(r.Amount(BattleRecord.Crits), Is.EqualTo(1));
        Assert.That(m.GetRecord(2, 1000).Amount(BattleRecord.Dealt), Is.Zero, "the chaos group needs its room meter");

        m.RecordDamage(31, 10, new[] { new Hit(0, -40) });
        m.RecordDamage(10, 31, new[] { new Hit(0, -10) });
        Assert.That(r.Amount(BattleRecord.Taken), Is.EqualTo(40), "slot 1 on the target's record");
        Assert.That(r.Amount(BattleRecord.BossDealt), Is.EqualTo(157), "class 3 is not the boss slot");

        m.RecordDamage(20, 31, new[] { new Hit(0, -9) });
        Assert.That(m.GetRecord(1, 1100).Amount(BattleRecord.Dealt), Is.EqualTo(9), "a pet's damage goes to its owner");
        m.RecordDamage(10, 11, new[] { new Hit(0, 25), new Hit(0, -5) });
        Assert.That(m.GetRecord(1, 1100).Amount(BattleRecord.Taken), Is.EqualTo(20), "a heal and a hit sum signed first");
    }

    [Test]
    public void StartResetsStopResetsAndClearDropsTheRecords() {
        uint now = 1000;
        var m = Manager(); m.Tick = () => now;
        m.AddRecord(1, 1000);
        m.StartGroupIfInactive(1);
        Assert.That(m.GroupStartTick(1), Is.EqualTo(1000u));
        m.RecordExp(1000, 30);
        now = 2000;
        m.StartGroupIfInactive(1);
        Assert.That(m.GroupStartTick(1), Is.EqualTo(1000u), "already running: no restart");
        Assert.That(m.GetRecord(1, 1000).Amount(BattleRecord.Exp), Is.EqualTo(30));
        m.StartGroup(1);
        Assert.That(m.GroupStartTick(1), Is.EqualTo(2000u));
        Assert.That(m.GetRecord(1, 1000).Amount(BattleRecord.Exp), Is.Zero, "Start resets every record");
        m.RecordMeso(1000, 5);
        m.StopGroup(1);
        Assert.That((m.IsGroupActive(1), m.GroupStartTick(1), m.GetRecord(1, 1000).Amount(BattleRecord.Meso)), Is.EqualTo((false, 0u, 0L)));
        Assert.That(m.HasRecord(1, 1000), Is.True, "Stop keeps the records");
        m.AddRecord(2, 1000);
        m.ClearPartyGroups();
        Assert.That(m.HasRecord(1, 1000) || m.HasRecord(2, 1000), Is.False);
    }

    [Test]
    public void TheChaosGroupNeedsItsRoomMeter() {
        bool room = false;
        var m = Manager(); m.ChaosRoomMeter = () => room;
        m.AddPartyMemberRecord(1000);
        m.StartGroup(2);
        Assert.That(m.IsGroupActive(2), Is.False);
        m.RecordDamage(10, 31, new[] { new Hit(0, -10) });
        Assert.That(m.GetRecord(2, 1000).Amount(BattleRecord.Dealt), Is.Zero);
        room = true;
        Assert.That(m.IsGroupActive(2), Is.True);
        m.StartGroupIfInactive(2);
        m.RecordDamage(10, 31, new[] { new Hit(0, -10) });
        Assert.That(m.GetRecord(2, 1000).Amount(BattleRecord.Dealt), Is.EqualTo(10));
    }

    [Test]
    public void ValueTextsFollowGetRecordValueText() {
        uint now = 10_000;
        var m = Manager(); m.Tick = () => now;
        m.AddRecord(1, 1000);
        Assert.That(m.RecordValueText(1, 2000, 0), Is.Empty, "no record");
        m.StartGroup(1);
        Assert.That(m.RecordValueText(1, 1000, 2), Is.EqualTo("0"), "no time yet");
        Assert.That(m.RecordValueText(1, 1000, 4), Is.EqualTo("0.000000"), "no attack: rate 0");
        m.RecordDamage(10, 30, new[] { new Hit(0, -1000), new Hit(1, -500), new Hit(2, 0) });
        now = 13_000;
        Assert.That(m.RecordValueText(1, 1000, 0), Is.EqualTo("1500"));
        Assert.That(m.RecordValueText(1, 1000, 1), Is.EqualTo("1500"));
        Assert.That(m.RecordValueText(1, 1000, 2), Is.EqualTo("499"), "per second: 1500 / (3000 * 0.001f), 3.0000002 s in float, truncated");
        Assert.That(m.RecordValueText(1, 1000, 4), Is.EqualTo(((double) (100.0f / 3 * 2)).ToString("F6", System.Globalization.CultureInfo.InvariantCulture)),
            "hit rate: (100 / attacks) * hits in float");
        Assert.That(m.RecordValueText(1, 1000, 5), Is.EqualTo("50.000000"), "crit rate: (100 / hits) * crits");
        Assert.That(m.RecordValueText(1, 1000, 9), Is.Empty);
        now = 10_000 + 3_725_000;
        Assert.That(m.ElapsedTimeText(1), Is.EqualTo("01:02:05"));
        Assert.That(BattleRecordManager.ModeKey(6), Is.EqualTo("s_dps_type_target_damage"));
        Assert.That(BattleRecordManager.ModeKey(9), Is.Empty);
    }

    static SkillDamage Read(Action<ByteWriter> write) {
        var w = new ByteWriter(); write(w);
        return SkillDamage.Read(new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()));
    }

    [Test]
    public void ModeThreeIsAttackerThenObjectHitThenBuff() {
        // OnDotDamage (scenestategame.cpp:5663) into CDotDamageRecord::Init(+0x2c, +0x28, m_nEffectLevel, type, damage)
        var d = Read(w => { w.WriteByte(3); w.WriteInt(10); w.WriteInt(31); w.WriteInt(777); w.WriteByte(1); w.WriteLong(-60); });
        Assert.That((d.OwnerId, d.TargetId, d.BuffId), Is.EqualTo((10, 31, 777)));
        Assert.That(d.Targets.Single().TargetId, Is.EqualTo(31), "the record hits the second int");
        Assert.That(BattleRecordManager.DamageRecords(d, _ => true, id => id == 31, _ => false).Single(), Is.EqualTo((10, 31, d.Targets[0].Hits)));
        Assert.That(BattleRecordManager.DamageRecords(d, _ => true, id => id == 10, _ => true), Is.Empty, "the PCBase test is on the object hit");
    }

    [Test]
    public void TheSkillDamageModesFeedTheirRecords() {
        var hits = new[] { new Hit(0, -5) };
        SkillDamage D(byte mode) => new() { Mode = mode, OwnerId = 10, RegionId = 900, Targets = { new HitTarget(31, hits, default, default), new HitTarget(32, hits, default, default) } };
        Func<int, bool> in31 = id => id == 31;
        Assert.That(BattleRecordManager.DamageRecords(D(1), in31, _ => false, _ => false), Is.EqualTo(new[] { (10, 31, hits) }),
            "mode 1: the owner on each target in the field");
        Assert.That(BattleRecordManager.DamageRecords(D(5), in31, _ => true, _ => false), Is.Empty, "mode 5: no region skill object");
        Assert.That(BattleRecordManager.DamageRecords(D(5), in31, _ => true, id => id == 900), Is.EqualTo(new[] { (10, 31, hits) }));
        Assert.That(BattleRecordManager.DamageRecords(D(6), in31, _ => true, _ => true), Is.EqualTo(new[] { (0, 31, hits) }), "mode 6: no owner");
        Assert.That(BattleRecordManager.DamageRecords(D(0), _ => true, _ => true, _ => true), Is.Empty, "mode 0 is a cast");

        var m = Manager();
        m.AddPartyMemberRecord(1000);
        m.StartGroup(1);
        m.Record(D(1), in31, _ => true, _ => true);
        m.Record(D(6), _ => true, _ => true, _ => true);
        Assert.That(m.GetRecord(1, 1000).Amount(BattleRecord.Dealt), Is.EqualTo(5), "one target in the field; mode 6 credits no one");
    }

    [Test]
    public void ThePartyBookAddsAndRemovesTheMembersRecords() {
        var m = Manager();
        var book = new PartyBook { BattleRecords = m };
        book.Apply(new PartyInfo(false, 7, 1, new[] { new PartyInfoMember(false, User(1)), new PartyInfoMember(true, User(2)) }, false, 0), 1, false);
        Assert.That(m.HasRecord(1, 1) && m.HasRecord(2, 2) && !m.HasRecord(0, 1), Is.True, "OnPartyInfo: every member in groups 1 and 2");
        book.Apply(new PartyJoin(User(3)), 1, true);
        Assert.That(m.HasRecord(1, 3), Is.True, "OnPartyMemberJoin");
        book.Apply(new PartyLeave(2, false), 1, true);
        Assert.That(m.HasRecord(1, 2) || m.HasRecord(2, 2), Is.False, "RemoveMemberAt");
        book.Apply(new PartyOnline(User(4)), 1, true);
        Assert.That(m.HasRecord(2, 4), Is.True, "a new member's OnPartyMemberOnline");
        m.StartGroup(1);
        m.RecordExp(4, 9);
        book.Apply(new PartyOnline(User(4)), 1, true);
        Assert.That(m.GetRecord(1, 4).Amount(BattleRecord.Exp), Is.EqualTo(9), "an existing member coming online keeps its record");
        book.Apply(new PartyDisband(), 1, true);
        Assert.That(m.HasRecord(1, 1) || m.IsGroupActive(1), Is.False, "ClearParty: ClearPartyGroups");
    }

    static long LeastBytes(Action calls) {
        long least = long.MaxValue;                     // the least of up to 5 windows: tiering lands one-time allocations anywhere
        for (int i = 0; i < 5 && least > 0; i++) {
            long before = GC.GetAllocatedBytesForCurrentThread();
            calls();
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        return least;
    }

    [Test]
    public void RecordingASkillDamageAllocatesNothing() {
        var m = Manager();
        m.AddPartyMemberRecord(1000);
        var hits = new[] { new Hit(0, -5), new Hit(1, -7) };
        var d = new SkillDamage { Mode = 1, OwnerId = 10, Targets = { new HitTarget(30, hits, default, default), new HitTarget(31, hits, default, default) } };
        Func<int, bool> yes = _ => true;
        void Calls() { for (int i = 0; i < 1000; i++) m.Record(d, yes, yes, yes); }
        Calls();
        Assert.That(LeastBytes(Calls), Is.Zero, "no group active");
        int roomAsks = 0;
        m.ChaosRoomMeter = () => { roomAsks++; return true; };
        Calls();
        Assert.That(LeastBytes(Calls), Is.Zero, "in a chaos dungeon room, no group running");
        Assert.That(roomAsks, Is.Zero, "the room is asked only for a running chaos group");
        Assert.That(m.HasRecord(2, 1000) && m.GetRecord(2, 1000).Amount(BattleRecord.Dealt) == 0, Is.True);
        m.StartGroup(1);
        Calls();
        Assert.That(LeastBytes(Calls), Is.Zero, "the party group active");
        Assert.That(m.GetRecord(1, 1000).Amount(BattleRecord.Dealt), Is.GreaterThan(0));
    }

    [Test]
    public void OnGms2ModesFourSevenAndEightStayRaw() {
        // their GMS2 layouts are not known: a read that guesses could throw, and a replay has no per-packet catch
        static SkillDamage Gms(Action<ByteWriter> write) {
            var w = new ByteWriter(); write(w);
            return SkillDamage.Read(new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()), ProtocolProfile.Gms2);
        }
        var h = Gms(w => { w.WriteByte(4); w.WriteInt(31); w.WriteInt(10); });
        var g = Gms(w => { w.WriteByte(7); w.WriteInt(40); w.WriteInt(2); w.WriteInt(41); w.WriteInt(42); });
        var c = Gms(w => { w.WriteByte(8); w.WriteLong(9001); w.WriteByte(0); w.WriteByte(0); });
        Assert.That((h.Mode, h.Raw?.Length, g.Mode, g.Raw?.Length, c.Mode, c.Raw?.Length), Is.EqualTo(((byte) 4, (int?) 8, (byte) 7, (int?) 16, (byte) 8, (int?) 10)));
        Assert.That((g.Released, c.Create), Is.EqualTo(((List<int>) null, false)));
    }

    [Test]
    public void ModeSevenTakesNoCapacityPastTheBytesLeft() {
        // a misread or hostile count must not size the list before the reads run out (16M ids, two on the wire)
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.That(() => Read(w => { w.WriteByte(7); w.WriteInt(40); w.WriteInt(0x01000000); w.WriteInt(41); w.WriteInt(42); }), Throws.Exception);
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(1 << 20));
    }

    [Test]
    public void ModesFourSevenAndEightReadTheClientsFields() {
        // FUN_14171d820 (scenestategame.cpp:5685): target, owner, buff, three u32 values, the show byte
        var h = Read(w => { w.WriteByte(4); w.WriteInt(31); w.WriteInt(10); w.WriteInt(777); w.WriteInt(-1); w.WriteInt(0); w.WriteInt(55); w.WriteByte(1); });
        Assert.That((h.Raw, h.TargetId, h.OwnerId, h.BuffId, h.Value1, h.Value2, h.Value3, h.ShowValues),
            Is.EqualTo(((byte[]) null, 31, 10, 777, 4294967295L, 0L, 55L, true)), "the values are unsigned");
        Assert.That(h.Targets, Is.Empty);
        // FUN_14171e860 (:7128): the grabbed actor, then a counted list of PC ids
        var g = Read(w => { w.WriteByte(7); w.WriteInt(40); w.WriteInt(2); w.WriteInt(41); w.WriteInt(42); });
        Assert.That((g.Raw, g.TargetId, g.Released), Is.EqualTo(((byte[]) null, 40, new List<int> { 41, 42 })));
        // FUN_14171e9a0 (:7149): the tag and the create byte; skill, level, owner and caster only when creating
        var c = Read(w => { w.WriteByte(8); w.WriteLong(9001); w.WriteByte(1); w.WriteInt(10200001); w.WriteShort(3); w.WriteInt(10); w.WriteInt(11); });
        Assert.That((c.Raw, c.CastUid, c.Create, c.SkillId, c.Level, c.OwnerId, c.CasterId), Is.EqualTo(((byte[]) null, 9001L, true, 10200001, (short) 3, 10, 11)));
        var x = Read(w => { w.WriteByte(8); w.WriteLong(9001); w.WriteByte(0); });
        Assert.That((x.Raw, x.CastUid, x.Create, x.SkillId), Is.EqualTo(((byte[]) null, 9001L, false, 0)));
    }
}
