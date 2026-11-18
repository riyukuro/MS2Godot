using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class PartyPacketTests {
    static PartyUser User(long id, string name, short channel = 1) => new() {
        AccountId = id + 100, CharacterId = id, Name = name, MapId = 2000001, MapInstanceId = 7, Level = 30, Channel = (ushort) channel,
        Job = 500, MaxHp = 2000, Hp = 1500, TombFlags = 2, GearScore = 1234, Picture = "pic", ReturnUserKind = 2, IsDead = true,
    };

    static PartyMessage Read(byte[] body) {
        var r = new ByteReader(body);
        var m = PartyMessage.Read(r);
        Assert.That(r.Available, Is.Zero, "the whole body is read");
        return m;
    }

    static byte[] Body(Action<ByteWriter> write) { var w = new ByteWriter(); write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }

    [Test]
    public void TheMemberRecordReadsBackItsFields() {
        var info = (PartyInfo) Read(new PartyInfo(true, 9, 11, new[] { new PartyInfoMember(false, User(11, "Lead")), new PartyInfoMember(true, User(12, "Other", 0)) }, true, 42).Write());
        Assert.That(info.Announce, Is.True);
        Assert.That(info.PartyId, Is.EqualTo(9));
        Assert.That(info.LeaderId, Is.EqualTo(11));
        Assert.That(info.Matchmaking, Is.True);
        Assert.That(info.DungeonId, Is.EqualTo(42));
        Assert.That(info.Members, Has.Count.EqualTo(2));
        var u = info.Members[0].User;
        Assert.That((u.AccountId, u.CharacterId, u.Name, u.MapId, u.MapInstanceId, u.Level, u.Channel), Is.EqualTo((111L, 11L, "Lead", 2000001, 7, (short) 30, (ushort) 1)));
        Assert.That((u.Job, u.MaxHp, u.Hp, u.TombType, u.GearScore, u.Picture, u.ReturnUserKind, u.IsDead), Is.EqualTo((500, 2000, 1500, 2, 1234, "pic", (byte) 2, true)));
        Assert.That(info.Members[1].Offline, Is.True);
        Assert.That(info.Members[1].User.Channel, Is.Zero);
    }

    [Test]
    public void TheShortModesRead() {
        Assert.That(Read(Body(w => { w.WriteByte(3); w.WriteLong(12); w.WriteBool(true); })), Is.EqualTo(new PartyLeave(12, true)));
        Assert.That(Read(Body(w => { w.WriteByte(4); w.WriteLong(12); })), Is.EqualTo(new PartyKick(12)));
        Assert.That(Read(Body(w => { w.WriteByte(6); w.WriteLong(12); })), Is.EqualTo(new PartyOffline(12)));
        Assert.That(Read(Body(w => w.WriteByte(7))), Is.EqualTo(new PartyDisband()));
        Assert.That(Read(Body(w => { w.WriteByte(8); w.WriteLong(12); })), Is.EqualTo(new PartyLeaderChange(12)));
        Assert.That(Read(Body(w => { w.WriteByte(0xf); w.WriteLong(12); w.WriteInt(77); })), Is.EqualTo(new PartyGearScore(12, 77)));
        Assert.That(Read(Body(w => { w.WriteByte(0x13); w.WriteLong(12); w.WriteLong(112); w.WriteInt(900); w.WriteInt(450); w.WriteShort(1); })),
            Is.EqualTo(new PartyMemberFlags(12, 112, 900, 450, 1)));
        var join = (PartyJoin) Read(new PartyJoin(User(13, "New")).Write());
        Assert.That(join.User.Name, Is.EqualTo("New"));
        var update = (PartyMemberUpdate) Read(Body(w => { w.WriteByte(0xd); w.WriteLong(13); User(13, "Renamed").Write(w); }));
        Assert.That((update.CharacterId, update.User.Name), Is.EqualTo((13L, "Renamed")));
    }

    [Test]
    public void ThePartyFollowsThePackets() {
        // CPartyManager: OnPartyInfo opens the window with more than one member; join, leader change, offline, leave, own kick
        var book = new PartyBook();
        var log = new List<string>();
        book.Opened += () => log.Add("open");
        book.Closed += () => log.Add("close");
        book.MemberAdded += m => log.Add("add " + m.User.Name);
        book.MemberRemoved += m => log.Add("remove " + m.User.Name);
        book.MemberUpdated += (m, f) => log.Add($"update {m.User.Name} {f:x}");
        book.ChiefChanged += (o, n) => log.Add($"chief {o?.User.Name} {n?.User.Name}");
        const long self = 11;
        book.Apply(Read(new PartyInfo(false, 1, 11, new[] { new PartyInfoMember(false, User(11, "Me")), new PartyInfoMember(false, User(12, "B")) }, false, 0).Write()), self, false);
        Assert.That(book.Members[0].Leader, Is.True);
        Assert.That(book.IsLeader(self), Is.True);
        book.Apply(new PartyJoin(User(13, "C")), self, true);
        book.Apply(new PartyLeaderChange(12), self, true);
        Assert.That(book.Members[1].Leader && !book.Members[0].Leader, Is.True);
        book.Apply(new PartyOffline(13), self, true);
        Assert.That(book.Members[2].State, Is.Zero);
        book.Apply(new PartyOnline(User(13, "C")), self, true);
        book.Apply(new PartyLeave(12, false), self, true);
        Assert.That(book.Members.Select(m => (m.User.Name, m.Index)), Is.EqualTo(new[] { ("Me", 0), ("C", 1) }));
        book.Apply(new PartyKick(self), self, true);
        Assert.That(book.Members, Is.Empty);
        Assert.That(log, Is.EqualTo(new[] { "open", "add C", "chief Me B", "update C 1", "update C 1ff", "remove B", "close" }));
    }
}
