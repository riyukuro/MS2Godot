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
        Assert.That(UserEnvMessage.Read(R(w => { w.WriteByte(4); w.WriteInt(0); })), Is.EqualTo(new UserEnvMessage.Templates(new List<int>())).Using<UserEnvMessage.Templates>((a, b) => a.Ids.SequenceEqual(b.Ids)));
        Assert.That(HomeCommend.Read(R(w => { w.WriteByte(1); w.WriteInt(9); w.WriteLong(1700000000); w.WriteInt(4); w.WriteInt(250); })),
            Is.EqualTo(new HomeCommend(9, 1700000000, 4, 250)));
        Assert.That(HomeCommend.Read(R(w => { w.WriteByte(0); w.WriteLong(1); w.WriteLong(2); })), Is.Null);
    }

    [Test] public void TheDeadStateIsTheOwnPlayersPackets() {
        Assert.That(DeadUser.Read(R(w => { w.WriteInt(9); w.WriteByte(1); w.WriteByte(7); })), Is.EqualTo(new DeadUser(9, true)), "trailing bytes ignored");
        Assert.That(Revival.Read(R(w => { w.WriteInt(9); w.WriteByte(2); })), Is.EqualTo(new Revival(9, 2)));

        var me = new MyCharacter();
        Assert.That((me.Dead, me.Die(9)), Is.EqualTo((false, false)), "before the own FieldAddUser: no local player");
        var u = new FieldAddUser { ObjectId = 9, User = new UserBlock { Name = "a" } };
        u.Stats[0].Health = 500; u.Stats[1].Health = 400; u.Stats[2].Health = 480;
        me.Apply(u);
        Assert.That((me.Dead, me.Stats2[PlayerStat.Health], me.Stats0[PlayerStat.Health]), Is.EqualTo((false, 480L, 500L)), "FieldAddUser's health kinds");
        Assert.That(me.Die(9), Is.False, "health left: PC::vfunc_137 does nothing");
        me.Apply(new PlayerStat(9, new Dictionary<byte, long[]> { [PlayerStat.Health] = new long[] { 500, 400, 0 } }));
        int changed = 0; me.Changed += () => changed++;
        Assert.That((me.Die(8), me.Dead), Is.EqualTo((false, false)), "another object");
        Assert.That((me.Die(9), me.Dead, changed), Is.EqualTo((true, true, 1)), "no current health: dead");
        Assert.That((me.Die(9), changed), Is.EqualTo((false, 1)), "already in the death state");
        Assert.That((me.Revive(8), me.Dead), Is.EqualTo((false, true)), "another object's revival");
        Assert.That((me.Revive(9), me.Dead, changed), Is.EqualTo((true, false, 2)));
        Assert.That(me.Revive(9), Is.False, "not dead: nothing changes");
        Assert.That((me.State, me.Die(9)), Is.EqualTo((0x18, true)), "in the revival state: dies again");
        Assert.That(me.State, Is.EqualTo(0xc));

        u.Stats[2].Health = 0;
        me.Apply(u);
        Assert.That((me.Dead, me.State), Is.EqualTo((true, 0)), "a FieldAddUser with no current health: dead (scenestategame.cpp:11185)");
        Assert.That((me.Die(9), me.State), Is.EqualTo((false, 0xc)), "already dead: MyPC::vfunc_137 enters the death state");
        u.Stats[2].Health = 10;
        me.Apply(u);
        Assert.That(me.Dead, Is.False);
    }

    [Test] public void UserStateEntersTheStateBeforeTheDie() {
        var me = new MyCharacter();
        var u = new FieldAddUser { ObjectId = 9, User = new UserBlock { Name = "a" } };
        u.Stats[0].Health = 500; u.Stats[2].Health = 0;
        me.Apply(u);
        me.Revive(9);
        Assert.That((me.Dead, me.State), Is.EqualTo((false, 0x18)));
        int changed = 0; me.Changed += () => changed++;
        Assert.That((me.ApplyUserState(9, 12), me.Dead, me.State, changed), Is.EqualTo((false, false, 0xc, 0)),
            "vfunc_59(12) enters the death state first, so vfunc_137 does not set the dead flag");
        Assert.That((me.Die(9), me.Dead), Is.EqualTo((false, false)), "a DeadUser after it: already in the death state");
        Assert.That((me.ApplyUserState(9, 1), me.State), Is.EqualTo((false, 0xc)), "no health: vfunc_124 stops the idle state");
        Assert.That((me.ApplyUserState(9, 0x3f), me.State), Is.EqualTo((false, 0x3f)), "the observer state always passes");
        Assert.That((me.Die(9), me.Dead, me.State, changed), Is.EqualTo((true, true, 0xc, 1)), "out of the death state: a DeadUser dies");
        Assert.That((me.ApplyUserState(9, 0x18), me.State), Is.EqualTo((false, 0x18)), "the revival state passes while dead");
        Assert.That((me.ApplyUserState(9, 1), me.State), Is.EqualTo((false, 0x18)), "dead: the idle state is stopped");
        Assert.That((me.ApplyUserState(9, 0x23), me.State), Is.EqualTo((false, 0x18)), "0x23 is not registered");
        me.Revive(9);
        me.Apply(new PlayerStat(9, new Dictionary<byte, long[]> { [PlayerStat.Health] = new long[] { 500, 400, 300 } }));
        Assert.That((me.ApplyUserState(9, 1), me.State), Is.EqualTo((false, 1)), "alive with health: the idle state passes");
        Assert.That((me.ApplyUserState(8, 12), me.State), Is.EqualTo((false, 1)), "another object");
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

    [Test]
    public void EachOwnPutOnOrOffCallSignalsWithOrWithoutAListChange()
    {
        // MyPC::vfunc_127 / vfunc_128 (mypc.cpp:4882-4905) take DAT_143aea120 again on every call, the preset branch or not;
        // OnItemPutOn (scenestategame.cpp:3080-3100) returns first for another pc, kind 6 and an item id without data
        // (CItem::FUN_140523c40); OnItemPutOff (:3144) for an uid the pc does not wear (FUN_141655b80)
        var me = new MyCharacter();
        var u = new FieldAddUser { ObjectId = 1, User = new UserBlock { Name = "a" } };
        u.Equips.Add(new EquipItem { Id = 15000001, Uid = 11, Slot = EquipSlot.RH });
        u.Equips.Add(new EquipItem { Id = 50000001, Uid = 12, Slot = EquipSlot.CL });
        me.Apply(u);
        int calls = 0, lists = 0;
        me.PutOnOrOff += () => calls++;
        me.EquipChanged += () => lists++;
        Func<int, int?> layer = id => id == 15000001 || id == 15000002 ? 0 : null;
        Func<int, bool> known = id => id != 1;
        Assert.That(me.Apply(new ItemPutOn(1, 0, new EquipItem { Id = 15000002, Uid = 13, Slot = EquipSlot.RH }), layer, known), Is.True);
        Assert.That((calls, lists), Is.EqualTo((1, 1)), "a worn item");
        Assert.That(me.Apply(new ItemPutOn(1, 0, new EquipItem { Id = 50000002, Uid = 14, Slot = EquipSlot.CL }), layer, known), Is.False);
        Assert.That((calls, lists), Is.EqualTo((2, 1)), "an item without a model preset: the lists stay, the rate is taken again");
        me.Apply(new ItemPutOn(7, 0, new EquipItem { Id = 15000002, Uid = 15 }), layer, known);
        me.Apply(new ItemPutOn(1, 3, new EquipItem { Id = 15000002, Uid = 15 }), layer, known);
        me.Apply(new ItemPutOn(1, 0, new EquipItem { Id = 1, Uid = 15 }), layer, known);
        Assert.That(calls, Is.EqualTo(2), "another pc, a kind above 2, an unknown item id");
        Assert.That(me.Apply(new ItemPutOff(1, 12), layer), Is.False);
        Assert.That((calls, lists), Is.EqualTo((3, 1)), "a worn uid without a preset");
        Assert.That(me.Apply(new ItemPutOff(1, 13), layer), Is.True);
        Assert.That((calls, lists), Is.EqualTo((4, 2)));
        me.Apply(new ItemPutOff(1, 99), layer);
        me.Apply(new ItemPutOff(7, 12), layer);
        Assert.That(calls, Is.EqualTo(4), "an uid not worn, another pc");
    }
}
