using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Quest;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The party links of a chat line (UIMessenger::HandlePartyLink, uimessenger.cpp:2302). The strings are the data's own: each test
/// compares against the common table, never against a language's text.
/// </summary>
[NonParallelizable]
public class PartyLinksTests
{
    static GameTables tables;

    static GameTables Tables()
    {
        if (tables == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            tables = new GameTables(new PackSet(d), "en");
        }
        return tables;
    }

    sealed class Rec
    {
        public readonly List<string> Notices = new();
        public readonly List<MessageBoxData> Boxes = new();
        public readonly List<IOutPacket> Sent = new();
        public bool InParty;
        public int State;
        public bool? Claimable = true;
        public readonly List<int> Opened = new();
        public PartyLinks Links;
    }

    static Rec Make(string self = "Me")
    {
        var t = Tables();
        var rec = new Rec();
        rec.Links = new PartyLinks
        {
            SelfName = () => self,
            IsInParty = () => rec.InParty,
            EnterState = _ => rec.State,
            RoomOf = id => new DungeonRoom(id, 0, 4, new[] { 1 }, LimitPlayerLevel: 40),
            DungeonName = id => "dungeon" + id,
            Key = k => t.Common(k) ?? "",
            Common = id => t.Common(id) ?? "",
            Notice = rec.Notices.Add,
            ShowBox = rec.Boxes.Add,
            Send = rec.Sent.Add,
            HelperRewardClaimable = _ => rec.Claimable,
            OpenRoom = rec.Opened.Add,
        };
        return rec;
    }

    static string Key(string key) => Tables().Common(key) ?? "";

    [Test]
    public void TheInviteLinkSendsTheInviteByNameAndOnlyWithOneName()
    {
        var rec = Make();
        rec.Links.Handle("inviteParty,Bob");
        Assert.That(rec.Sent, Is.EqualTo(new IOutPacket[] { new PartyInviteRequest("Bob") }));
        rec.Links.Handle("inviteParty");
        rec.Links.Handle("inviteParty,Bob,Extra");
        rec.Links.Handle("");
        rec.Links.Handle("somethingElse,Bob");
        Assert.That(rec.Sent, Has.Count.EqualTo(1), "a wrong part count or an unknown link does nothing");
    }

    [Test]
    public void TheDungeonRoomLinkOpensTheRoomWithOneArgument()
    {
        var rec = Make();
        rec.Links.Handle("dungeonRoom,20000001");
        rec.Links.Handle("dungeonRoom");
        rec.Links.Handle("dungeonRoom,1,2");
        Assert.That(rec.Opened, Is.EqualTo(new[] { 20000001 }));
        rec.Links.OpenRoom = null;
        Assert.DoesNotThrow(() => rec.Links.Handle("dungeonRoom,5"), "no room dialog yet: nothing happens");
    }

    [TestCase(true)]
    [TestCase(false)]
    public void TheJoinLinksAskInABoxAndTheFirstButtonSendsTheirOwnMode(bool helper)
    {
        var rec = Make();
        string link = (helper ? "joinPartyDungeonHelper" : "joinPartyDungeonMember") + ",77,Lead,20000003";
        rec.Links.Handle(link);
        var box = rec.Boxes.Single();
        Assert.That((box.Msg, box.Type), Is.EqualTo((
            ClientFormat.Format(Key(helper ? "s_party_join_dungeon_helper_msg" : "s_party_join_dungeon_member_msg"), "Lead", "dungeon20000003"), 1)));
        Assert.That(rec.Sent, Is.Empty, "nothing goes out before the answer");
        box.InvokeCallback(1, null);
        Assert.That(rec.Sent, Is.Empty, "the second button sends nothing");
        box.InvokeCallback(0, null);
        IOutPacket want = helper ? new PartyDungeonHelperJoin(77, 20000003) : new PartyDungeonMemberJoin(77, 20000003);
        Assert.That(rec.Sent, Is.EqualTo(new[] { want }));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ABoxAnsweredYesWithAZeroPartyOrRoomSendsNothing(bool helper)
    {
        string name = helper ? "joinPartyDungeonHelper" : "joinPartyDungeonMember";
        foreach (var link in new[] { name + ",0,Lead,5", name + ",7,Lead,0", name + ",x,Lead,5" })
        {
            var rec = Make();
            rec.Links.Handle(link);
            Assert.That(rec.Boxes, Has.Count.EqualTo(1), link);
            rec.Boxes[0].InvokeCallback(0, null);
            Assert.That(rec.Sent, Is.Empty, link);
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void TheJoinLinksRefuseTheOwnLinkAnAlreadyPartyAndALimitedRoomInTheClientsOrder(bool helper)
    {
        string name = helper ? "joinPartyDungeonHelper" : "joinPartyDungeonMember";
        var own = Make("Lead");
        own.InParty = true; own.State = 12;
        own.Links.Handle(name + ",1,Lead,5");
        Assert.That(own.Notices, Is.EqualTo(new[] { Key("s_party_is_my_dungeon_help") }), "the own name first");
        var inParty = Make();
        inParty.InParty = true; inParty.State = 12;
        inParty.Links.Handle(name + ",1,Lead,5");
        Assert.That(inParty.Notices, Is.EqualTo(new[] { Key("s_party_has_party") }), "then a party");
        var limited = Make();
        limited.State = 12;
        limited.Links.Handle(name + ",1,Lead,5");
        Assert.That(limited.Notices, Is.EqualTo(new[] { ClientFormat.Format(Tables().Common(0x99b), 40) }), "then the enter state at 10 or more");
        var open = Make();
        open.State = 9;
        open.Links.Handle(name + ",1,Lead,5");
        foreach (var r in new[] { own, inParty, limited })
        {
            Assert.That(r.Boxes, Is.Empty);
            Assert.That(r.Sent, Is.Empty);
        }
        Assert.That((open.Notices, open.Boxes.Count), Is.EqualTo((new List<string>(), 1)), "below 10 the box opens");
    }

    [Test]
    public void TheJoinLinksDoNothingWithoutALocalPlayerAndAWrongPartCount()
    {
        var none = Make(null);
        none.Links.SelfName = () => null;
        none.Links.Handle("joinPartyDungeonMember,1,Lead,5");
        Assert.That((none.Boxes, none.Notices, none.Sent), Is.EqualTo((new List<MessageBoxData>(), new List<string>(), new List<IOutPacket>())));
        var rec = Make();
        rec.Links.Handle("joinPartyDungeonHelper,1,Lead");
        rec.Links.Handle("joinPartyDungeonMember,1,Lead,5,6");
        Assert.That((rec.Boxes, rec.Notices), Is.EqualTo((new List<MessageBoxData>(), new List<string>())));
    }

    [Test]
    public void TheHelperLinkStopsWhereTheRewardClaimIsNotKnownOrNotAllowed()
    {
        foreach (bool? claim in new bool?[] { null, false })
        {
            var rec = Make();
            rec.Claimable = claim;
            rec.Links.Handle("joinPartyDungeonHelper,1,Lead,5");
            Assert.That((rec.Boxes, rec.Notices, rec.Sent), Is.EqualTo((new List<MessageBoxData>(), new List<string>(), new List<IOutPacket>())), claim?.ToString() ?? "unknown");
        }
        var member = Make();
        member.Claimable = null;
        member.Links.Handle("joinPartyDungeonMember,1,Lead,5");
        Assert.That(member.Boxes, Has.Count.EqualTo(1), "the member link has no such check");
    }

    [Test]
    public void EveryStringTheLinksUseExistsInTheData()
    {
        foreach (var key in new[] { "s_party_is_my_dungeon_help", "s_party_has_party", "s_party_join_dungeon_helper_msg", "s_party_join_dungeon_member_msg" })
            Assert.That(Tables().Common(key), Is.Not.Null.And.Not.Empty, key);
        Assert.That(Tables().Common(0x99b), Is.Not.Null.And.Not.Empty);
    }
}
