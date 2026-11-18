using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class CharacterInfoTests {
    static ByteReader R(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); }

    [Test] public void AttributePointModesAreTheStatPointManagersLoads() {
        var stats = AttributePointMessage.Read(R(w => { w.WriteByte(1); w.WriteInt(20); w.WriteInt(2); w.WriteByte(0); w.WriteInt(3); w.WriteByte(4); w.WriteInt(5); }));
        var extra = AttributePointMessage.Read(R(w => { w.WriteByte(0); w.WriteInt(25); w.WriteInt(1); w.WriteInt(2); w.WriteInt(7); }));
        Assert.That(AttributePointMessage.Read(R(w => w.WriteByte(2))), Is.Null);
        var b = new StatPointBook(); int changed = 0; b.Changed += () => changed++;
        b.Apply(stats);
        Assert.That((b.Total, b.Used, b.Remaining), Is.EqualTo((20, 8, 12)));
        Assert.That(b.GetStatPoints(4), Is.EqualTo(5)); Assert.That(b.GetStatPoints(1), Is.Zero);
        b.Apply(extra);                                                     // LoadExtra keeps the used points
        Assert.That((b.Total, b.Used, b.Remaining), Is.EqualTo((25, 8, 17)));
        Assert.That(b.GetExtra(2), Is.EqualTo(7u)); Assert.That(b.GetStatPoints(4), Is.EqualTo(5));
        Assert.That(changed, Is.EqualTo(2));
        Assert.That(StatPointBook.CostIds, Is.EqualTo(new[] { 0, 1, 2, 3, 4, 0x11 }));
    }

    [Test] public void UserEnvTitleModesAndHomeCommend() {
        Assert.That(UserEnvMessage.Read(R(w => { w.WriteByte(0); w.WriteInt(10000001); })), Is.EqualTo(new UserEnvMessage.TitleAdded(10000001)));
        Assert.That(UserEnvMessage.Read(R(w => { w.WriteByte(1); w.WriteInt(9); w.WriteInt(10000002); })), Is.EqualTo(new UserEnvMessage.TitleChanged(9, 10000002)));
        var list = (UserEnvMessage.Titles) UserEnvMessage.Read(R(w => { w.WriteByte(2); w.WriteInt(2); w.WriteInt(5); w.WriteInt(6); }));
        Assert.That(list.Ids, Is.EqualTo(new uint[] { 5, 6 }));
        Assert.That(UserEnvMessage.Read(R(w => { w.WriteByte(3); w.WriteInt(0); })), Is.Null);
        Assert.That(HomeCommend.Read(R(w => { w.WriteByte(1); w.WriteInt(9); w.WriteLong(1700000000); w.WriteInt(4); w.WriteInt(250); })),
            Is.EqualTo(new HomeCommend(9, 1700000000, 4, 250)));
        Assert.That(HomeCommend.Read(R(w => { w.WriteByte(0); w.WriteLong(1); w.WriteLong(2); })), Is.Null);
    }

    [Test] public void TheRecordIsTheOwnPlayersPackets() {
        var me = new MyCharacter();
        Assert.That(me.ToPcInfo(0), Is.Null, "before the own FieldAddUser");
        var user = new UserBlock { AccountId = 1, CharacterId = 2, Name = "Tester", Gender = 1, Level = 30, JobCode = 10, Job = 110,
            GuildName = "Guild", Motto = "hello", Picture = "pic", PrestigeLevel = 12 };
        long now = new DateTimeOffset(2026, 11, 18, 3, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        long sameMonth = new DateTimeOffset(2026, 11, 2, 3, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        me.Apply(new FieldAddUser { ObjectId = 9, User = user, Title = 10000002, HomeCommendMonth = sameMonth, HomeCommendMonthly = 3, HomeCommendPoint = 40 });
        me.Apply(new PlayerStat(9, new Dictionary<byte, long[]> { [4] = new long[] { 500, 400, 480 }, [0x14] = new long[] { 30, 20, 31 } }));
        me.Apply(new UserEnvMessage.Titles(new uint[] { 10000002, 10000005 }));
        me.Apply(new UserEnvMessage.TitleChanged(8, 1));                    // another player's
        me.Apply(new ProxyGameObj { Command = 3, ObjectId = 70, CharacterId = 2, AchievementCombat = 1, AchievementAdventure = 2, AchievementLifestyle = 4 });
        var i = me.ToPcInfo(now);
        Assert.That(i.IsLocal, Is.True);
        Assert.That((i.Level, i.JobGroup, i.SubJob, i.Gender, i.Title), Is.EqualTo(((ushort) 30, 10, 110, (byte) 1, 10000002)));
        Assert.That((i.Name, i.GuildName, i.Motto, i.Picture, i.PrestigeLevel), Is.EqualTo(("Tester", "Guild", "hello", "pic", 12)));
        Assert.That((i.Stats0[4], i.Stats1[4], i.Stats2[4], i.Stats2[0x14]), Is.EqualTo((500L, 400L, 480L, 31L)));
        Assert.That(i.Titles, Is.EqualTo(new uint[] { 10000002, 10000005 }));
        Assert.That(i.Trophy, Is.EqualTo(7));
        Assert.That((i.HomeCommendPoint, i.HomeCommendMonthly), Is.EqualTo((40, 3)));
        me.Apply(new ProxyGameObj { Command = 4, ObjectId = 70 });
        Assert.That(me.ToPcInfo(now).Trophy, Is.Zero, "no proxy: FindProxyObject finds none");
        // another calendar month resets the count (PC::GetMonthlyCount)
        long nextMonth = new DateTimeOffset(2026, 12, 1, 3, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        Assert.That(me.ToPcInfo(nextMonth).HomeCommendMonthly, Is.Zero);
        Assert.That(me.HomeCommendMonth, Is.EqualTo(nextMonth));
        me.Apply(new HomeCommend(9, nextMonth, 6, 41));
        Assert.That(me.ToPcInfo(nextMonth).HomeCommendMonthly, Is.EqualTo(6));
        me.Apply(new UserEnvMessage.TitleChanged(9, 10000005));
        Assert.That(me.ToPcInfo(nextMonth).Title, Is.EqualTo(10000005));
    }

    [Test]
    public void CodiLayerComesFromTheOwnFieldAddUser()
    {
        var me = new MyCharacter();
        var u = new FieldAddUser { ObjectId = 1, User = new UserBlock { Name = "a" }, SkinTabStart = 0, SkinTabEnd = 0 };
        me.Apply(u);
        var info = me.ToPcInfo(1000);
        Assert.That(info.GetCodiPeriodState(true, 1000), Is.EqualTo(4), "never started");
        Assert.That(info.GetCodiPeriodState(false, 1000), Is.EqualTo(0), "feature 0x8e off");
        Assert.That(info.CodiItems, Is.Zero);
        u.SkinTabStart = 500; u.SkinTabEnd = 0; u.Outfit2.Add(new EquipItem { Id = 11300001 });
        me.Apply(u);
        info = me.ToPcInfo(1000);
        Assert.That(info.GetCodiPeriodState(true, 1000), Is.EqualTo(2), "no end");
        Assert.That(info.CodiItems, Is.EqualTo(1));
        u.SkinTabEnd = 2000; me.Apply(u);
        Assert.That(me.ToPcInfo(1000).GetCodiPeriodState(true, 1000), Is.EqualTo(1));
        Assert.That(me.ToPcInfo(2000).GetCodiPeriodState(true, 2000), Is.EqualTo(3), "ended");
    }

    [Test]
    public void TheRecordCarriesTheOwnEquipsOutfitsAndBadges()
    {
        var me = new MyCharacter();
        var u = new FieldAddUser { ObjectId = 1, User = new UserBlock { Name = "a" } };
        u.Equips.Add(new EquipItem { Id = 15000001 });
        u.Outfit2.Add(new EquipItem { Id = 11300001 });
        u.Badges.Add(new EquipItem { Id = 70200001, GemType = 3 });
        me.Apply(u);
        var i = me.ToPcInfo(0);
        Assert.That(i.Equips.Select(e => e.Id), Is.EqualTo(new[] { 15000001 }));
        Assert.That(i.Outfit2.Select(e => e.Id), Is.EqualTo(new[] { 11300001 }));
        Assert.That(i.Gems.Single().GemType, Is.EqualTo((byte)3));
        u.Equips.Clear(); me.Apply(u);
        Assert.That(me.ToPcInfo(0).Equips, Is.Empty, "a later FieldAddUser replaces the lists");
    }
}
