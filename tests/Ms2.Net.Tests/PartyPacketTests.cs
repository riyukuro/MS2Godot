using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class PartyPacketTests {
    static PartyUser User(long id, string name, short channel = 1) => new() {
        AccountId = id + 100, CharacterId = id, Name = name, MapId = 2000001, MapInstanceId = 7, Level = 30, Channel = (ushort) channel,
        JobGroup = 50, Job = 500, Trophy = (1, 2, 3), MaxHp = 2000, Hp = 1500, TombFlags = 2, GearScore = 1234, Picture = "pic", ReturnUserKind = 2, IsDead = true,
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
        Assert.That((u.JobGroup, u.Trophy), Is.EqualTo((50, (1, 2, 3))));
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

    [Test]
    public void TheInviteReadsAndIsKeptForItsAnswer() {
        // OnPartyInvite (partymanager.cpp:968): partyName, then partyKind; the Maple2 server's PartyPacket.Invite (mode 11)
        var invite = (PartyInvite) Read(Body(w => { w.WriteByte(0xb); w.WriteUnicodeString("Inviter"); w.WriteInt(77); }));
        Assert.That((invite.PartyName, invite.Kind), Is.EqualTo(("Inviter", 77)));
        var book = new PartyBook();
        PartyInvite raised = null;
        book.Invited += m => raised = m;
        book.Apply(invite, 1, false);
        Assert.That(raised, Is.SameAs(invite));
        Assert.That((book.PartyName, book.PartyKind), Is.EqualTo(("Inviter", 77)));
        Assert.That(book.IsInParty, Is.False, "an invite changes no party");
    }

    static PartyVote Vote(int kind, int id, long[] who, long[] yes, long[] no) {
        var v = PartyVote.Create(kind);
        v.Id = id; v.Expires = 1000;
        foreach (long x in who) v.Participants.Add(x);
        foreach (long x in yes) v.Yes.Add(x);
        foreach (long x in no) v.No.Add(x);
        return v;
    }

    [Test]
    public void TheVotePacketsRead() {
        // OnPartyVoteStart: u8 kind, the body for kinds 1 and 2 (i32 id, i64 end, three i64 sets; the kick vote adds owner,
        // target, name, u8 reason)
        var kick = Vote(PartyVote.KickUser, 5, new long[] { 11, 12, 13 }, new long[] { 11 }, new long[0]);
        kick.Owner = 11; kick.Target = 13; kick.Name = "Target"; kick.Reason = 2;
        var start = (PartyVoteStart) Read(new PartyVoteStart(1, kick).Write());
        var v = start.Vote;
        Assert.That((v.Kind, v.Id, v.Expires, v.Owner, v.Target, v.Name, v.Reason), Is.EqualTo((1, 5, 1000L, 11L, 13L, "Target", (byte) 2)));
        Assert.That((v.Participants, v.Yes, v.No.Count), Is.EqualTo((new[] { 11L, 12L, 13L }, new[] { 11L }, 0)));
        Assert.That((v.ShowsPage, v.HasVoted(11), v.HasVoted(12)), Is.EqualTo((true, true, false)));
        var ready = (PartyVoteStart) Read(new PartyVoteStart(2, Vote(PartyVote.Ready, 6, new long[] { 11 }, new long[0], new long[] { 11 })).Write());
        Assert.That((ready.Vote.Kind, ready.Vote.ShowsPage, ready.Vote.No.Single()), Is.EqualTo((2, false, 11L)));
        // CreateVote gives null for another kind: its body is not read
        Assert.That(Read(Body(w => { w.WriteByte(0x2f); w.WriteByte(3); })), Is.EqualTo(new PartyVoteStart(3, null)));
        Assert.That(Read(new PartyVoteReply(12, true).Write()), Is.EqualTo(new PartyVoteReply(12, true)));
        Assert.That(Read(Body(w => w.WriteByte(0x31))), Is.EqualTo(new PartyVoteEnd()));
    }

    [Test]
    public void TheReadyCheckMarksTheMembers() {
        var book = new PartyBook();
        const long self = 11;
        book.Apply(new PartyInfo(false, 1, 11, new[] { new PartyInfoMember(false, User(11, "Me")), new PartyInfoMember(false, User(12, "B")), new PartyInfoMember(false, User(13, "C")) }, false, 0), self, true);
        var started = new List<int>(); var ended = new List<int>();
        book.VoteStarted += v => started.Add(v.Id);
        book.VoteEnded += v => ended.Add(v.Id);
        // PartyVoteReadyCli::vfunc_2: 2 yes, 3 no, 1 waiting
        book.Apply(new PartyVoteStart(2, Vote(PartyVote.Ready, 7, new long[] { 11, 12, 13 }, new long[] { 11 }, new long[0])), self, true);
        Assert.That(book.Members.Select(m => m.ReadyState), Is.EqualTo(new[] { 2, 1, 1 }));
        // AddVote, then vfunc_7: 3 for a no
        book.Apply(new PartyVoteReply(12, false), self, true);
        Assert.That((book.Vote.No.Single(), book.Members[1].ReadyState), Is.EqualTo((12L, 3)));
        // OnPartyMemberOffline: vfunc_6 takes the member out of the vote
        book.Apply(new PartyVoteReply(13, true), self, true);
        book.Apply(new PartyOffline(13), self, true);
        Assert.That(book.Vote.Participants.Contains(13) || book.Vote.Yes.Contains(13), Is.False);
        // OnPartyVoteEnd: vfunc_3 marks the members still waiting as not ready (FUN_14162c3a0), then ClearVote
        book.Apply(new PartyVoteStart(2, Vote(PartyVote.Ready, 8, new long[] { 11, 12 }, new long[] { 11 }, new long[0])), self, true);
        book.Apply(new PartyVoteEnd(), self, true);
        Assert.That(book.Members.Take(2).Select(m => m.ReadyState), Is.EqualTo(new[] { 2, 3 }));
        Assert.That(book.Vote, Is.Null);
        Assert.That((started, ended), Is.EqualTo((new[] { 7, 8 }, new[] { 8 })));
        // SetAllMembersState: the deadline's clear
        book.SetAllMembersState(0);
        Assert.That(book.Members.All(m => m.ReadyState == 0), Is.True);
        // a reply or an end without a vote does nothing
        book.Apply(new PartyVoteReply(12, true), self, true);
        book.Apply(new PartyVoteEnd(), self, true);
        Assert.That(ended, Has.Count.EqualTo(1));
    }

    // The nine modes OnParty dispatches that the reader left as PartyOtherMode with the body unread.

    [Test]
    public void TheNoticeWithSoundReadsItsStringIdTextThenTheSound() {
        var m = (PartyNoticeWithSound) Read(Body(w => {
            w.WriteByte(0x15);
            w.WriteBool(true); w.WriteInt(4); w.WriteInt(0x321); w.WriteInt(2); w.WriteUnicodeString("a"); w.WriteUnicodeString("b");
            w.WriteUnicodeString("snd");
        }));
        Assert.That((m.Text.Formatted, m.Text.Kind, m.Text.Id, m.Text.Args, m.Sound), Is.EqualTo((true, 4, 0x321, new[] { "a", "b" }, "snd")));
        var plain = (PartyNoticeWithSound) Read(Body(w => { w.WriteByte(0x15); w.WriteBool(false); w.WriteInt(0); w.WriteUnicodeString("t"); w.WriteUnicodeString("s"); }));
        Assert.That((plain.Text.Formatted, plain.Text.Text, plain.Sound), Is.EqualTo((false, "t", "s")));
    }

    [Test]
    public void TheDungeonMatchRegisterReadsItsGroupFlagsAndBits() {
        Assert.That(Read(Body(w => { w.WriteByte(0x1e); w.WriteByte(3); w.WriteBool(true); w.WriteBool(false); w.WriteByte(0x0a); })),
            Is.EqualTo(new PartyDungeonMatchRegister(3, true, false, 0x0a)));
    }

    [Test]
    public void TheMatchConfirmHasNoBody() {
        Assert.That(Read(Body(w => w.WriteByte(0x1f))), Is.EqualTo(new PartyDungeonMatchConfirm()));
        Assert.Throws<PacketLayoutException>(() => PartyMessage.Read(new ByteReader(Body(w => { w.WriteByte(0x1f); w.WriteByte(1); }))));
    }

    [Test]
    public void TheShortDungeonAndNoticeModesRead() {
        Assert.That(Read(Body(w => { w.WriteByte(0x23); w.WriteLong(0x1234567890L); })), Is.EqualTo(new PartyExpireTimer(0x1234567890L)));
        Assert.That(Read(Body(w => { w.WriteByte(0x28); w.WriteInt(4500); })), Is.EqualTo(new PartyFindDungeonCooldown(4500)));
        Assert.That(Read(Body(w => { w.WriteByte(0x2c); w.WriteUnicodeString("Guest"); })), Is.EqualTo(new PartyInviteSent("Guest")));
        Assert.That(Read(Body(w => { w.WriteByte(0x37); w.WriteBool(true); w.WriteBool(false); })), Is.EqualTo(new PartySurvivalSquadRegister(true, false)));
        Assert.That(Read(Body(w => { w.WriteByte(0x38); w.WriteInt(77); })), Is.EqualTo(new PartyDungeonRoomCreate(77)));
    }

    [Test]
    public void TheChatNoticeReadsTheSenderAndItsText() {
        var m = (PartyChatNotice) Read(Body(w => {
            w.WriteByte(0x25); w.WriteLong(55); w.WriteUnicodeString("Sender");
            w.WriteBool(true); w.WriteInt(0); w.WriteInt(0x500); w.WriteInt(1); w.WriteUnicodeString("x");
        }));
        Assert.That((m.SenderId, m.Name, m.Text.Id, m.Text.Args), Is.EqualTo((55L, "Sender", 0x500, new[] { "x" })));
    }

    [Test]
    public void TheSearchBitsAndTheSurvivalSquadFollowThePackets() {
        // OnPartyInfo, OnPartyDungeonMatchRegister and OnPartySurvivalSquadRegister keep permissions and flag29; ClearParty resets them
        var book = new PartyBook();
        const long self = 11;
        var info = new PartyInfo(false, 9, 11, new[] { new PartyInfoMember(false, User(11, "Lead")), new PartyInfoMember(false, User(12, "Other")) }, false, 0) { SearchBits = 0x06, SurvivalSquad = true };
        book.Apply((PartyInfo) Read(info.Write()), self, false);
        Assert.That((book.SearchBits, book.SurvivalSquadRegistered, book.IsAnySearching), Is.EqualTo((0x06, true, true)));
        Assert.That((book.IsMatchSearching(0), book.IsMatchSearching(1), book.IsMatchSearching(2), book.IsMatchSearching(3)), Is.EqualTo((false, true, true, false)));
        book.Apply(new PartyDungeonMatchRegister(1, true, true, 0x02), self, true);
        Assert.That((book.SearchBits, book.IsMatchSearching(1), book.IsMatchSearching(2)), Is.EqualTo((0x02, true, false)));
        book.Apply(new PartySurvivalSquadRegister(false, false), self, true);
        Assert.That(book.SurvivalSquadRegistered, Is.False);
        book.Apply(new PartyMatchDungeonChange(true, 31), self, true);
        Assert.That((book.Matchmaking, book.DungeonId), Is.EqualTo((true, 31)));
        book.Apply(new PartyDisband(), self, true);
        Assert.That((book.SearchBits, book.IsAnySearching, book.SurvivalSquadRegistered), Is.EqualTo((0, false, false)));
    }

    // ---- the chat lines the handlers write (kind 0xe) ---------------------------------------------------------------------------

    static (PartyBook Book, List<PartyNote> Notes) Party(bool announce = false) {
        var book = new PartyBook();
        var notes = new List<PartyNote>();
        var info = new PartyInfo(announce, 9, 11, new[] { new PartyInfoMember(false, User(11, "Lead")), new PartyInfoMember(false, User(12, "Other")) }, false, 0);
        book.Apply((PartyInfo) Read(info.Write()), 11, false);
        book.Noted += notes.Add;
        return (book, notes);
    }
    static PartyChatNote Chat(int id, string name = null) => new(id, name);

    [Test]
    public void TheMembersComingAndGoingWriteTheirPartyLines() {
        var (book, notes) = Party();
        book.Apply(new PartyJoin(User(13, "Newcomer")), 11, true);
        book.Apply(new PartyOffline(13), 11, true);
        book.Apply(new PartyOnline(User(13, "Newcomer")), 11, true);
        book.Apply(new PartyOnline(User(14, "Stranger")), 11, true);       // not in the party: added, then the join line
        book.Apply(new PartyLeave(13, false), 11, true);
        book.Apply(new PartyKick(14), 11, true);
        Assert.That(notes, Is.EqualTo(new[] {
            Chat(0x31c, "Newcomer"), Chat(0x323, "Newcomer"), Chat(0x322, "Newcomer"), Chat(0x31c, "Stranger"), Chat(0x31e, "Newcomer"), Chat(800, "Stranger") }));
    }

    [Test]
    public void TheClosingModesWriteTheirLineAfterTheWindowCloses() {
        foreach (var (message, expected) in new (PartyMessage, PartyChatNote)[] {
            (new PartyDisband(), Chat(0x321)), (new PartyLeave(12, true), Chat(0x31d)), (new PartyKick(11), Chat(799)) }) {
            var (book, notes) = Party();
            var order = new List<string>();
            book.Closed += () => order.Add("closed");
            book.Noted += _ => order.Add("note");
            book.Apply(message, 11, true);
            Assert.That(notes, Is.EqualTo(new[] { expected }), message.GetType().Name);
            Assert.That(order, Is.EqualTo(new[] { "closed", "note" }), message.GetType().Name);
            Assert.That(book.IsInParty, Is.False);
        }
    }

    [Test]
    public void ALeaderChangeWritesItsLineEvenWhenTheLeaderStays() {
        var (book, notes) = Party();
        book.Apply(new PartyLeaderChange(12), 11, true);        // the other player leads: the name
        book.Apply(new PartyLeaderChange(12), 11, true);        // the same leader again: the line anyway
        book.Apply(new PartyLeaderChange(0), 11, true);         // no id: nothing changes, the line is made with an empty name
        book.Apply(new PartyLeaderChange(11), 11, true);        // the own character: the "you lead" line
        Assert.That(notes, Is.EqualTo(new[] { Chat(0x325, "Other"), Chat(0x325, ""), Chat(0x325, ""), Chat(0x324) }));
        Assert.That(book.LeaderId, Is.EqualTo(11));
    }

    [Test]
    public void ALoneMemberGetsNoLeaderLine() {
        var book = new PartyBook();
        var notes = new List<PartyNote>();
        book.Noted += notes.Add;
        book.Apply(new PartyLeaderChange(5), 11, false);
        Assert.That(notes, Is.Empty);
    }

    [Test]
    public void TheInfoAnnouncesOnlyWithAnnounceAndMoreThanOneMember() {
        var book = new PartyBook();
        var notes = new List<PartyNote>();
        book.Noted += notes.Add;
        var two = new[] { new PartyInfoMember(false, User(11, "Lead")), new PartyInfoMember(false, User(12, "Other")) };
        book.Apply((PartyInfo) Read(new PartyInfo(false, 1, 11, two, false, 0).Write()), 11, false);
        Assert.That(notes, Is.Empty);
        book.Apply((PartyInfo) Read(new PartyInfo(true, 1, 11, two, false, 0).Write()), 11, false);
        Assert.That(notes, Is.EqualTo(new[] { Chat(0x31b) }));
        notes.Clear();
        book.Apply((PartyInfo) Read(new PartyInfo(true, 1, 11, two.Take(1).ToArray(), false, 0).Write()), 11, false);
        Assert.That(notes, Is.Empty);
    }

    [Test]
    public void ThePartyMemberResultNamesTheOtherMemberOnly() {
        var (book, notes) = Party();
        book.Apply(new PartyMemberResult(12, 0), 11, true);
        book.Apply(new PartyMemberResult(12, 1), 11, true);
        book.Apply(new PartyMemberResult(11, 1), 11, true);      // the own character: nothing
        book.Apply(new PartyMemberResult(99, 1), 11, true);      // not a member: nothing
        Assert.That(notes, Is.EqualTo(new[] { Chat(0x326, "Other"), Chat(0x327, "Other") }));
    }

    [Test]
    public void ADungeonChangeIsNotedOnlyForAMatchedPartyMovingToAnotherDungeon() {
        var (book, notes) = Party();
        book.Apply(new PartyMatchDungeonChange(false, 20), 11, true);   // from no dungeon
        Assert.That(notes, Is.Empty);
        book.Apply(new PartyMatchDungeonChange(false, 20), 11, true);   // the same one
        book.Apply(new PartyMatchDungeonChange(true, 21), 11, true);    // matchmaking
        Assert.That(notes, Is.Empty);
        book.Apply(new PartyMatchDungeonChange(false, 22), 11, true);
        Assert.That(notes, Is.EqualTo(new PartyNote[] { new PartyDungeonChangeNote(22) }));
        var alone = new PartyBook();
        var aloneNotes = new List<PartyNote>();
        alone.Noted += aloneNotes.Add;
        alone.Apply(new PartyMatchDungeonChange(false, 20), 11, false);
        alone.Apply(new PartyMatchDungeonChange(false, 22), 11, false);
        Assert.That(aloneNotes, Is.Empty);
    }

    [Test]
    public void TheDungeonRoomAnswerSendsOnlyForTheFirstButton() {
        var book = new PartyBook();
        var sent = new List<IOutPacket>();
        book.Send = sent.Add;
        Assert.That(book.AnswerDungeonRoom(1), Is.True);
        Assert.That(sent, Is.Empty);
        Assert.That(book.AnswerDungeonRoom(0), Is.True);
        Assert.That(sent, Is.EqualTo(new IOutPacket[] { new RoomDungeonEnterRequest(0) }));
    }

    [Test]
    public void AnInviteLineIsShownOncePerMinutePerName() {
        var book = new PartyBook();
        var now = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
        book.ServerNow = () => now;
        Assert.That(book.InviteSentIsNew("Guest"), Is.True);
        Assert.That(book.InviteSentIsNew("Guest"), Is.True, "no entry before the line is shown (the client needs the messenger first)");
        book.InviteLineShown("Guest");
        Assert.That(book.InviteSentIsNew("Guest"), Is.False);
        Assert.That(book.InviteSentIsNew("guest"), Is.True, "the name is compared as written (std::map of CStringW)");
        now = now.AddSeconds(59);
        Assert.That(book.InviteSentIsNew("Guest"), Is.False);
        now = now.AddSeconds(1);
        Assert.That(book.InviteSentIsNew("Guest"), Is.True, "now < expiry fails at the expiry");
    }
}
