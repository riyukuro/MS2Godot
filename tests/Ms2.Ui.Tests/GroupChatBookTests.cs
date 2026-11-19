using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>CGroupChatManager (GroupChatBook): the sends' rejects send nothing, and what each GroupChat packet does to the
/// chats and the browser. Texts are the common ids ("c&lt;id&gt; {0}"), so no language's strings.</summary>
public class GroupChatBookTests
{
    sealed class Ui : IGroupMessengerUi
    {
        public readonly List<string> Calls = new();
        public void OnChatCreated(int oid) => Calls.Add($"created {oid}");
        public void OnInvited(int oid, string inviter, string second) => Calls.Add($"invited {oid} {inviter} {second}");
        public void RemoveGroup(int oid) => Calls.Add($"remove {oid}");
        public void OnMemberInvited(int oid, string name, string member) => Calls.Add($"member {oid} {name} {member}");
        public void OnMemberRemoved(int oid, string name, int reason) => Calls.Add($"left {oid} {name} {reason}");
        public void OnGroupMemberOnline(int oid, string name) => Calls.Add($"online {oid} {name}");
        public void OnGroupMemberOffline(int oid, string name) => Calls.Add($"offline {oid} {name}");
        public void QueueTalkLog(int oid, string name, string message) => Calls.Add($"talk {oid} {name} {message}");
        public void ReadItemLinks(int oid, string name, ChatItemLinks links) => Calls.Add($"links {oid} {name} {links.Items.Count}");
        public void AddNoticeLine(int oid, string name, string message) => Calls.Add($"notice {oid} {name} {message}");
    }

    readonly List<IOutPacket> sent = new();
    readonly List<MessageBoxData> boxes = new();
    readonly Ui ui = new();
    MessageBoxData shown;

    GroupChatBook Book() => new()
    {
        Common = id => $"c{id:x} {{0}}",
        Send = sent.Add,
        Messenger = () => ui,
        LocalName = () => "Me",
        ShownBox = () => shown,
        ShowBox = boxes.Add,
    };

    static GroupChatMember Member(string name, int state = 1, long id = 1) =>
        new() { State = state, User = new PartyUser { Name = name, CharacterId = id } };

    static GroupChat Chat(int oid, params string[] names)
    {
        var c = new GroupChat { Oid = oid };
        for (int i = 0; i < names.Length; i++) c.AddMember(Member(names[i], 1, i + 1));
        return c;
    }

    [SetUp]
    public void Reset() { sent.Clear(); boxes.Clear(); ui.Calls.Clear(); shown = null; }

    [Test]
    public void CreateWithThreeChatsShowsTheLimitBoxAndSendsNothing()
    {
        var b = Book();
        for (int i = 1; i <= 3; i++) b.Apply(new GroupChatAdd(Chat(i, "Me")));
        b.AddChat(Chat(4, "Me"));                                       // a fourth is not kept
        Assert.That(b.ChatCount, Is.EqualTo(3));
        b.Create("");
        Assert.That(sent, Is.Empty);
        Assert.That(boxes.Single().Msg, Is.EqualTo("c818 Me"));
    }

    [Test]
    public void CreateAndInviteSendNothingForALongName()
    {
        var b = Book();
        b.Create(new string('a', GroupChatBook.MaxNameLength + 1));
        b.Apply(new GroupChatAdd(Chat(7, "Me")));
        b.Invite(7, new string('a', GroupChatBook.MaxNameLength + 1));
        b.Invite(8, "Ann");                                             // no chat 8
        Assert.That(sent, Is.Empty);
        Assert.That(boxes, Is.Empty);
        b.Create(new string('a', GroupChatBook.MaxNameLength));
        b.Invite(7, "Ann");
        Assert.That(sent, Is.EqualTo(new IOutPacket[] {
            new GroupChatCreateRequest(new string('a', GroupChatBook.MaxNameLength)), new GroupChatInviteRequest("Ann", 7) }));
    }

    [Test]
    public void InvitingAMemberShowsTheExistBoxAndSendsNothing()
    {
        var b = Book();
        b.Apply(new GroupChatAdd(Chat(7, "Me", "Ann")));
        b.Invite(7, "Ann");
        Assert.That(sent, Is.Empty);
        Assert.That(boxes.Single().Msg, Is.EqualTo("c816 {0}"));
    }

    [Test]
    public void InvitingIntoAFullChatShowsTheBoxAndStillSends()
    {
        var b = Book();
        var names = Enumerable.Range(0, GroupChat.MaxMembers).Select(i => "m" + i.ToString("00")).ToArray();
        b.Apply(new GroupChatAdd(Chat(7, names)));
        b.Invite(7, "Ann");
        Assert.That(boxes.Single().Msg, Is.EqualTo("c814 {0}"));
        Assert.That(boxes.Single().Extra, Is.EqualTo("c814 {0}"));
        Assert.That(sent, Is.EqualTo(new IOutPacket[] { new GroupChatInviteRequest("Ann", 7) }));
        // the same box already up: neither a box nor a send
        shown = boxes[0];
        b.Invite(7, "Bob");
        Assert.That(boxes.Count, Is.EqualTo(1));
        Assert.That(sent.Count, Is.EqualTo(1));
    }

    [Test]
    public void TalkIsSentOnlyToAKnownChatAndNotWhenBlocked()
    {
        var b = Book();
        b.SendTalk(7, "hi");
        b.SendLeave(7, 0);
        Assert.That(sent, Is.Empty);
        b.Apply(new GroupChatAdd(Chat(7, "Me")));
        b.SendTalk(7, new string('a', GroupChatBook.MaxTextLength + 1));
        b.ChatLimited = () => true;
        b.SendTalk(7, "hi");
        Assert.That(sent, Is.Empty);
        b.ChatLimited = () => false;
        b.BlockedMessage = () => "blocked";
        b.SendTalk(7, "hi");
        Assert.That(sent, Is.Empty);
        Assert.That(ui.Calls, Is.EqualTo(new[] { "notice 7 Me blocked" }));
        b.BlockedMessage = () => null;
        b.SendTalk(7, "hi");
        b.SendLeave(7, 0);
        Assert.That(sent, Is.EqualTo(new IOutPacket[] { new GroupChatTalkRequest("hi", 7), new GroupChatLeaveRequest(7, 0) }));
    }

    [Test]
    public void ThePacketsChangeTheChatsAndTellTheBrowser()
    {
        var b = Book();
        b.Apply(new GroupChatCreated(7));                               // unknown chat: nothing
        b.Apply(new GroupChatAdd(Chat(7, "Me")));
        b.Apply(new GroupChatCreated(7));
        b.Apply(new GroupChatInvited("Ann", "Me", 7));
        b.Apply(new GroupChatMemberAdd(7, "Me", Member("Ann", 1, 9)));
        Assert.That(b.FindChat(7).HasMemberById(9), Is.True);
        Assert.That(b.FindChatsWithMember(9), Is.EqualTo(new[] { 7 }));
        b.Apply(new GroupChatMemberOffline(7, "Ann"));
        Assert.That(b.FindChat(7).FindMemberByName("Ann").State, Is.EqualTo(2));
        b.Apply(new GroupChatMemberOnline(7, "Ann"));
        b.Apply(new GroupChatTalk(7, "Ann", "hi"));
        b.Apply(new GroupChatTalk(7, "Bob", "hi"));                     // not a member: dropped
        b.Apply(new GroupChatItemLinks(7, "Ann", new ChatItemLinks(new List<EquipItem>())));
        b.Apply(new GroupChatMemberPicture(7, "Ann", "pic"));
        Assert.That(b.FindChat(7).FindMemberByName("Ann").User.Picture, Is.EqualTo("pic"));
        b.Apply(new GroupChatMemberRemove(7, 0, "Ann"));
        b.Apply(new GroupChatMemberRemove(7, 0, "Ann"));                // gone: nothing
        Assert.That(b.FindChat(7).MemberCount, Is.EqualTo(1));
        b.Apply(new GroupChatRemove(7));
        b.Apply(new GroupChatRemove(7));
        Assert.That(b.ChatCount, Is.EqualTo(0));
        Assert.That(ui.Calls, Is.EqualTo(new[] {
            "created 7", "invited 7 Ann Me", "member 7 Me Ann", "offline 7 Ann", "online 7 Ann", "talk 7 Ann hi",
            "links 7 Ann 0", "left 7 Ann 0", "remove 7" }));
        Assert.That(sent, Is.Empty);
    }

    [Test]
    public void RefusingInvitesSendsTheRefuseLeave()
    {
        var b = Book();
        b.RefuseInvite = () => true;
        b.Apply(new GroupChatInvited("Ann", "Me", 7));                  // unknown chat: SendLeave sends nothing
        b.Apply(new GroupChatAdd(Chat(7, "Me")));
        b.Apply(new GroupChatInvited("Ann", "Me", 7));
        Assert.That(sent, Is.EqualTo(new IOutPacket[] { new GroupChatLeaveRequest(7, 1) }));
        Assert.That(ui.Calls, Is.Empty);
    }

    [Test]
    public void TheResultCodesShowTheirBoxes()
    {
        var b = Book();
        foreach (int code in new[] { 3, 10, 8, 0xd, 1, 5 }) b.Apply(new GroupChatResult(0, code, "a", "b"));
        Assert.That(boxes.Select(x => x.Msg), Is.EqualTo(new[] { "c815 {0}", "c818 b", "c819 b", "c29 b" }));
    }
}
