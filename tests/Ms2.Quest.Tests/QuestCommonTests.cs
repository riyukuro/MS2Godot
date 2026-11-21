using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Quest.Tests;

public class QuestCommonTests
{
    static QuestRecord Rec(int state, bool complete) => new(1, state, 0, 0, 0, complete, false);

    static QuestButtonInputs Live(int field) => new() { FieldId = field, FieldExists = _ => true };

    static DungeonRooms Rooms() => DungeonRooms.Parse(System.Xml.Linq.XDocument.Parse(
        @"<ms2><dungeonRoom dungeonRoomID=""7"" groupType=""1"" fieldIDs=""50,51"" maxUserCount=""4"" /></ms2>"), _ => true);

    static List<string> Run(QuestData d, QuestRecord r, QuestButtonInputs live, IQuestState st = null) =>
        QuestCommon.Buttons(d, r, live, FieldMeta.Parse("<ms2/>"), st ?? new NoQuestState(1), Rooms());

    [Test]
    public void TheRoomRowKeepsChaosDamageMeter()
    {
        var t = DungeonRooms.Parse(System.Xml.Linq.XDocument.Parse(
            @"<ms2><dungeonRoom dungeonRoomID=""7"" groupType=""2"" chaosDamageMeter=""1"" /><dungeonRoom dungeonRoomID=""8"" /></ms2>"), _ => true);
        Assert.That((t.GetRoom(7).ChaosDamageMeter, t.GetRoom(8).ChaosDamageMeter), Is.EqualTo((true, false)), "default false (dungeonroom.cpp:429)");
    }

    [Test]
    public void GiveupNeedsActiveAndNoDisable()
    {
        var d = new QuestData();
        Assert.That(Run(d, Rec(1, false), Live(10)), Is.EqualTo(new[] { "giveup" }));
        Assert.That(Run(d, Rec(2, false), Live(10)), Is.Empty);
        d.DisableGiveup = true;
        Assert.That(Run(d, Rec(1, false), Live(10)), Is.Empty);
    }

    [Test]
    public void GotoDungeonStateAndRoom()
    {
        var d = new QuestData { DisableGiveup = true, GotoDungeonState = 1, GotoDungeonRoom = 7 };
        Assert.That(Run(d, Rec(1, false), Live(10)), Is.EqualTo(new[] { "gotoDungeon" }));
        Assert.That(Run(d, Rec(1, true), Live(10)), Is.Empty);               // state 1 is not complete
        Assert.That(Run(d, Rec(1, false), Live(50)), Is.Empty);              // already in a field of the room
        var live = Live(10); live.ServiceType = 2;
        Assert.That(Run(d, Rec(1, false), live), Is.Empty);                  // FUN_140930b70
        var party = Live(10); party.DirectQuestFindParty = true;
        Assert.That(Run(d, Rec(1, false), party), Is.EqualTo(new[] { "gotoDungeon", "partyFind" }));
        party.IsMatchSearching = t => t == QuestCommon.ClubBuffTypeOf(1);
        Assert.That(Run(d, Rec(1, false), party)[1], Is.EqualTo("partyFindCancel"));
    }

    [Test]
    public void GotoNpcNeedsCompleteAndOtherField()
    {
        var d = new QuestData { DisableGiveup = true, GotoNpcEnable = 1, GotoNpcField = 20, CompleteNpc = 5 };
        var meta = FieldMeta.Parse(@"<ms2><npc id=""5""><field id=""20"" /></npc></ms2>");
        Assert.That(QuestCommon.Buttons(d, Rec(1, true), Live(10), meta, new NoQuestState(1), Rooms()), Is.EqualTo(new[] { "gotoNpc" }));
        Assert.That(QuestCommon.Buttons(d, Rec(1, true), Live(20), meta, new NoQuestState(1), Rooms()), Is.Empty);
        Assert.That(QuestCommon.Buttons(d, Rec(1, false), Live(10), meta, new NoQuestState(1), Rooms()), Is.Empty);
    }

    [Test]
    public void FieldQuestDispatchesWhenComplete()
    {
        var d = new QuestData { DisableGiveup = true, Type = 8 };
        Assert.That(Run(d, Rec(1, true), Live(10)), Is.EqualTo(new[] { "gotoDispatch" }));
        Assert.That(Run(d, Rec(1, false), Live(10)), Is.Empty);
    }

    [Test]
    public void TimeLimitedNeedsAnEnd()
    {
        Assert.That(QuestCommon.IsTimeLimited(new QuestData()), Is.False);
        Assert.That(QuestCommon.IsTimeLimited(new QuestData { DurationMinutes = 5 }), Is.True);
        Assert.That(QuestCommon.IsTimeLimited(new QuestData { ResetType = 2 }), Is.True);
    }

    [Test]
    public void RealQuestsHaveNoButtons()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA is not set");
        var packs = new PackSet(data);
        var t = QuestTable.Load(packs, new[] { "en" }, "KR", _ => true);
        var rooms = DungeonRooms.Load(packs, t.Available);
        var meta = FieldMeta.Load(packs);
        foreach (int id in new[] { 40002710, 40002700 })
        {
            var d = t.Quest(id);
            Assert.That(d.DisableGiveup, Is.True);
            var buttons = QuestCommon.Buttons(d, new QuestRecord(id, 1, 0, 0, 0, false, false), Live(2000001), meta, new NoQuestState(1), rooms);
            TestContext.WriteLine($"{id}: [{string.Join(",", buttons)}]");
            Assert.That(buttons, Is.Empty);
        }
    }
}
