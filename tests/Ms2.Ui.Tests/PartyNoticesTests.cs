using Maple2.PacketLib.Tools;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Quest;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The party manager's chat lines, centre notices, boxes and sounds outside the book (CPartyManager, partymanager.cpp): the
/// result table of mode 0, the notice modes, the dungeon match modes, the invite line and the lines the book notes itself.
/// The strings are the data's own: each test compares against the common table, never against a language's text.
/// </summary>
[NonParallelizable]
public class PartyNoticesTests
{
    static PackSet packs;
    static GameTables tables;

    static GameTables Tables()
    {
        if (tables == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = new PackSet(d);
            tables = new GameTables(packs, "en");
        }
        return tables;
    }

    sealed class Rec
    {
        public readonly List<(string Text, int Kind)> Chat = new();
        public readonly List<string> Notices = new(), Sounds = new();
        public readonly List<MessageBoxData> Boxes = new();
        public readonly List<string> Invites = new();
        public bool Messenger = true;
        public readonly List<IOutPacket> Sent = new();
        public PartyNotices Ui;
        public PartyBook Book;
    }

    const long Self = 11;

    static PartyUser User(long id, string name) => new() { AccountId = id + 100, CharacterId = id, Name = name, MapId = 2000001, Level = 30, Channel = 1, JobGroup = 50, Job = 500, MaxHp = 10, Hp = 10 };

    static Rec Make(bool inParty)
    {
        var rec = new Rec { Book = new PartyBook() };
        rec.Book.Send = rec.Sent.Add;
        if (inParty)
            rec.Book.Apply(new PartyInfo(false, 9, Self, new[] { new PartyInfoMember(false, User(Self, "Lead")), new PartyInfoMember(false, User(12, "Other")) }, false, 0), Self, false);
        rec.Ui = new PartyNotices
        {
            Tables = Tables(), SelfCharacterId = Self,
            ChatLine = (t, k) => rec.Chat.Add((t, k)), Notice = rec.Notices.Add, PlaySound = rec.Sounds.Add, ShowBox = rec.Boxes.Add,
            InviteLine = n => { if (!rec.Messenger) return false; rec.Invites.Add(n); return true; },
            DungeonName = id => "dungeon" + id,
            StringIdText = t => "text:" + t.Text,
        };
        return rec;
    }

    static string Id(int id, string name) => ClientFormat.Format(Tables().Common(id), name);
    static string Key(string key) => Tables().Common(key) ?? "";

    [Test]
    public void EachResultCodeShowsItsLineOrBoxInTheKindTheClientUses()
    {
        // OnPartyResultMessage (:1372): (result, kind) with kind 0x14 chat line, 1 centre notice, 2 box
        var chatIds = new Dictionary<int, int>
        {
            [0x9] = 0x330, [0x14] = 0x331, [0x15] = 0x332, [0xc] = 0x333, [0xb] = 0x32e, [0x5] = 0x32a, [0x7] = 0x329, [0x8] = 0x329, [0xd] = 0x329,
            [0x2] = 0x32d, [0x10] = 0x336, [0x11] = 0x337, [0x12] = 0x338, [0x17] = 0x33c, [0x1a] = 0x33e, [0x1b] = 0x35, [0x99] = 0x979,
        };
        foreach (var (result, id) in chatIds)
        {
            var rec = Make(true);
            rec.Ui.OnApplied(rec.Book, new PartyResult(result, "Bob"));
            Assert.That(rec.Chat, Is.EqualTo(new[] { (Id(id, "Bob"), 0x14) }), $"result {result:x}");
            Assert.That(rec.Notices.Count + rec.Boxes.Count, Is.Zero, $"result {result:x}");
        }
        foreach (var (result, id) in new[] { (0x4, 0x32c), (0xe, 0x32f) })
        {
            var rec = Make(true);
            rec.Ui.OnApplied(rec.Book, new PartyResult(result, "Bob"));
            Assert.That(rec.Chat, Is.EqualTo(new[] { (Tables().Common(id), 0x14) }), $"result {result:x}: the string without the name");
        }
        foreach (var (result, id) in new[] { (0xa, 0x32a), (0x13, 0x339), (0x18, 0x33a), (0x16, 0x33b), (0x19, 0x33d), (0x26, 0x341) })
        {
            var rec = Make(true);
            rec.Ui.OnApplied(rec.Book, new PartyResult(result, "Bob"));
            Assert.That(rec.Notices, Is.EqualTo(new[] { Tables().Common(id) }), $"result {result:x}");
            Assert.That(rec.Chat, Is.Empty);
        }
        var byKey = new Dictionary<int, string>
        {
            [0x1c] = "s_party_err_inviteMe", [0x1e] = "s_party_invite_boss_room", [0x1f] = "s_party_err_not_found", [0x21] = "s_party_err_already_vote",
            [0x22] = "s_party_err_vote_need_more_people", [0x23] = "s_party_err_vote_cooldown", [0x25] = "s_party_err_vote_cannot_kick_vote_only_dungeon",
            [0x27] = "s_party_expel_maple_survival_squad", [0x2b] = "s_party_err_dungeon_match_another", [0x2c] = "s_party_err_isNotChief",
            [0x2d] = "s_party_err_hasOfflineUser", [0x2e] = "s_party_err_inside_survival", [0x2f] = "s_party_err_another_matching",
            [0x30] = "s_party_err_survival_has_solo_register", [0x31] = "s_maple_survival_error_squad_register_over_count",
        };
        foreach (var (result, key) in byKey)
        {
            var rec = Make(true);
            rec.Ui.OnApplied(rec.Book, new PartyResult(result, "Bob"));
            Assert.That(rec.Notices, Is.EqualTo(new[] { Key(key) }), $"result {result:x}");
        }
        foreach (var (result, key) in new[] { (0x1d, "s_room_party_err_is_in_user"), (0x28, "s_dungeonMatch_error_isNotChief"), (0x29, "s_dungeonMatch_error_hasOfflineUser"), (0x2a, "s_dungeonMatch_error_insideDungeonUser") })
        {
            var rec = Make(true);
            rec.Ui.OnApplied(rec.Book, new PartyResult(result, "Bob"));
            Assert.That(rec.Boxes.Select(b => (b.Msg, b.Type)), Is.EqualTo(new[] { (Key(key), 0) }), $"result {result:x}");
            Assert.That(rec.Chat.Count + rec.Notices.Count, Is.Zero);
        }
        var named = Make(true);
        named.Ui.OnApplied(named.Book, new PartyResult(0xf, "Bob"));
        named.Ui.OnApplied(named.Book, new PartyResult(0x20, "Bob"));
        Assert.That(named.Notices, Is.EqualTo(new[] { Id(0x32b, "Bob"), ClientFormat.Format(Key("s_party_request_invite"), "Bob") }));
    }

    [Test]
    public void TheKickVoteRefusalListsTheDungeonGroupNames()
    {
        var rec = Make(true);
        rec.Ui.OnApplied(rec.Book, new PartyResult(0x24, "2,3"));
        string names = Key("s_enum_dungeon_group_raid") + ", " + Key("s_enum_dungeon_group_chaosRaid");
        Assert.That(rec.Notices, Is.EqualTo(new[] { ClientFormat.Format(Key("s_party_err_vote_cannot_kick_vote"), names) }));
        Assert.That(rec.Ui.FormatDungeonGroupList("1"), Is.EqualTo(Key("s_enum_dungeon_group_normal")));
        Assert.That(rec.Ui.FormatDungeonGroupList("99"), Is.Empty, "outside the list: empty");
    }

    [Test]
    public void TheNoticeModesWriteAPartyChatLineAndPlayTheirSound()
    {
        var rec = Make(true);
        rec.Ui.OnApplied(rec.Book, new PartyNotice("s_party_match_dungeon_register", "Some_Sound", "Argument"));
        rec.Ui.OnApplied(rec.Book, new PartyNoticeWithSound(new StringIdText(false, 0, 0, Array.Empty<string>(), "hello"), "Other_Sound"));
        Assert.That(rec.Chat, Is.EqualTo(new[] { (ClientFormat.Format(Key("s_party_match_dungeon_register"), "Argument"), 2), ("text:hello", 2) }));
        Assert.That(rec.Sounds, Is.EqualTo(new[] { "Some_Sound", "Other_Sound" }));
    }

    [Test]
    public void ThePartyChatNoticeIsDroppedForBlockedSenders()
    {
        var buddies = new BuddyBook { Common = _ => "" };
        buddies.Apply(new BuddyMessage(1) { Entries = new[] { new BuddyInfo { Id = 1, CharacterId = 77, Name = "Blocked", Blocked = true } } });
        var rec = Make(true);
        rec.Ui.Buddies = buddies;
        var text = new StringIdText(false, 0, 0, Array.Empty<string>(), "hi");
        rec.Ui.OnApplied(rec.Book, new PartyChatNotice(5, "Blocked", text));    // by name
        rec.Ui.OnApplied(rec.Book, new PartyChatNotice(77, "Renamed", text));   // by id
        rec.Ui.OnApplied(rec.Book, new PartyChatNotice(0, "Blocked", text));
        Assert.That(rec.Chat, Is.Empty);
        rec.Ui.OnApplied(rec.Book, new PartyChatNotice(0, "Renamed", text));    // id 0 is not looked up
        rec.Ui.IsBlindBlocked = n => n == "Muted";
        rec.Ui.OnApplied(rec.Book, new PartyChatNotice(8, "Muted", text));
        rec.Ui.OnApplied(rec.Book, new PartyChatNotice(8, "Friend", text));
        Assert.That(rec.Chat, Is.EqualTo(new[] { ("text:hi", 2), ("text:hi", 2) }));
    }

    [Test]
    public void TheDungeonMatchRegisterWritesItsLineOnlyInAPartyAndAlwaysPlaysItsSound()
    {
        foreach (bool inParty in new[] { true, false })
        {
            var rec = Make(inParty);
            rec.Ui.OnApplied(rec.Book, new PartyDungeonMatchRegister(2, true, true, 0));
            rec.Ui.OnApplied(rec.Book, new PartyDungeonMatchRegister(2, false, true, 0));
            rec.Ui.OnApplied(rec.Book, new PartyDungeonMatchRegister(2, true, false, 0));   // not announced: nothing
            string group = Key("s_enum_dungeon_group_raid");
            Assert.That(rec.Chat, inParty
                ? Is.EqualTo(new[] { (ClientFormat.Format(Key("s_party_match_dungeon_register"), group), 2), (ClientFormat.Format(Key("s_party_match_dungeon_unregister"), group), 2) })
                : Is.Empty);
            Assert.That(rec.Sounds, Is.EqualTo(new[] { "Dungeon_Room_searchParty", "Dungeon_Room_searchPartyCancel" }));
        }
    }

    [Test]
    public void TheDungeonRoomBoxesAnswerThroughTheBook()
    {
        var rec = Make(true);
        rec.Ui.OnApplied(rec.Book, new PartyDungeonMatchConfirm());
        var box = rec.Boxes.Single();
        Assert.That((box.Msg, box.Type), Is.EqualTo((Key("s_party_match_dungeon"), 1)));
        box.InvokeCallback(1, null);
        Assert.That(rec.Sent, Is.Empty, "the second button sends nothing");
        box.InvokeCallback(0, null);
        Assert.That(rec.Sent, Has.Count.EqualTo(1));
        // the room create: the leader gets nothing, another member the box and the sound
        rec.Boxes.Clear();
        rec.Ui.OnApplied(rec.Book, new PartyDungeonRoomCreate(5));
        Assert.That((rec.Boxes, rec.Sounds), Is.EqualTo((new List<MessageBoxData>(), new List<string>())));
        var member = Make(true);
        member.Ui.SelfCharacterId = 12;
        member.Ui.OnApplied(member.Book, new PartyDungeonRoomCreate(5));
        Assert.That(member.Boxes.Single().Msg, Is.EqualTo(ClientFormat.Format(Key("s_party_create_dungeon"), "dungeon5")));
        Assert.That(member.Sounds, Is.EqualTo(new[] { "Dungeon_Room_Open" }));
    }

    [Test]
    public void TheFindDungeonCooldownCountsWholeSecondsPlusOne()
    {
        var rec = Make(false);
        rec.Ui.OnApplied(rec.Book, new PartyFindDungeonCooldown(2500));
        rec.Ui.OnApplied(rec.Book, new PartyFindDungeonCooldown(0));
        Assert.That(rec.Chat, Is.EqualTo(new[]
        {
            (ClientFormat.Format(Key("s_party_find_dungeon_helper_cooldown"), 3u), 0x14),
            (ClientFormat.Format(Key("s_party_find_dungeon_helper_cooldown"), 1u), 0x14),
        }));
    }

    [Test]
    public void TheSurvivalSquadLineGoesToThePartyChannelOnlyInAParty()
    {
        foreach (bool inParty in new[] { true, false })
        {
            var rec = Make(inParty);
            rec.Ui.OnApplied(rec.Book, new PartySurvivalSquadRegister(true, true));
            rec.Ui.OnApplied(rec.Book, new PartySurvivalSquadRegister(false, true));
            rec.Ui.OnApplied(rec.Book, new PartySurvivalSquadRegister(true, false));
            int kind = inParty ? 2 : 0xe;
            Assert.That(rec.Chat, Is.EqualTo(new[] { (Key("s_maple_survival_squad_register_squad"), kind), (Key("s_maple_survival_squad_unregister_squad"), kind) }));
            Assert.That(rec.Sounds, Is.EqualTo(new[] { "Dungeon_Room_searchParty", "Dungeon_Room_searchPartyCancel" }));
        }
    }

    [Test]
    public void TheInviteLineIsShownOncePerMinuteAndOnlyWithTheMessenger()
    {
        var rec = Make(false);
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        rec.Book.ServerNow = () => now;
        rec.Messenger = false;
        rec.Ui.OnApplied(rec.Book, new PartyInviteSent("Bob"));
        Assert.That(rec.Invites, Is.Empty, "no messenger: no line, no entry");
        rec.Messenger = true;
        rec.Ui.OnApplied(rec.Book, new PartyInviteSent("Bob"));
        rec.Ui.OnApplied(rec.Book, new PartyInviteSent("Bob"));
        now = now.AddSeconds(30);
        rec.Ui.OnApplied(rec.Book, new PartyInviteSent("Bob"));
        Assert.That(rec.Invites, Is.EqualTo(new[] { "Bob" }));
        now = now.AddSeconds(31);
        rec.Ui.OnApplied(rec.Book, new PartyInviteSent("Bob"));
        Assert.That(rec.Invites, Is.EqualTo(new[] { "Bob", "Bob" }));
    }

    [Test]
    public void TheBooksOwnLinesAreWrittenInThePartyNoticeKind()
    {
        var rec = Make(true);
        rec.Ui.OnNoted(new PartyChatNote(0x321, null));
        rec.Ui.OnNoted(new PartyChatNote(0x31c, "Newcomer"));
        Assert.That(rec.Chat, Is.EqualTo(new[] { (Tables().Common(0x321), 0xe), (Id(0x31c, "Newcomer"), 0xe) }));
        rec.Ui.OnNoted(new PartyDungeonChangeNote(5));
        Assert.That(rec.Notices, Is.EqualTo(new[] { ClientFormat.Format(Key("s_party_change_help_dungeon"), "dungeon5") }));
    }

    [Test]
    public void EveryStringTheNoticesUseExistsInTheData()
    {
        // The whole list: each common id and key of the result table, the notice modes and the book's lines.
        var ids = new List<int> { 0x329, 0x32a, 0x32b, 0x32c, 0x32d, 0x32e, 0x32f, 0x330, 0x331, 0x332, 0x333, 0x336, 0x337, 0x338, 0x339, 0x33a, 0x33b, 0x33c, 0x33d, 0x33e, 0x341, 0x35, 0x979 };
        for (int i = 0x31b; i <= 0x327; i++) ids.Add(i);
        ids.AddRange(new[] { 799, 800 });
        var keys = new List<string>
        {
            "s_party_err_inviteMe", "s_party_invite_boss_room", "s_party_err_not_found", "s_party_request_invite", "s_party_err_already_vote",
            "s_party_err_vote_need_more_people", "s_party_err_vote_cooldown", "s_party_err_vote_cannot_kick_vote", "s_party_err_vote_cannot_kick_vote_only_dungeon",
            "s_party_expel_maple_survival_squad", "s_dungeonMatch_error_isNotChief", "s_dungeonMatch_error_hasOfflineUser", "s_dungeonMatch_error_insideDungeonUser",
            "s_party_err_dungeon_match_another", "s_party_err_isNotChief", "s_party_err_hasOfflineUser", "s_party_err_inside_survival", "s_party_err_another_matching",
            "s_party_err_survival_has_solo_register", "s_maple_survival_error_squad_register_over_count", "s_room_party_err_is_in_user", "s_party_match_dungeon",
            "s_party_match_dungeon_register", "s_party_match_dungeon_unregister", "s_party_create_dungeon", "s_party_change_help_dungeon",
            "s_party_find_dungeon_helper_cooldown", "s_maple_survival_squad_register_squad", "s_maple_survival_squad_unregister_squad",
        };
        for (int g = 1; g < AchieveController.DungeonGroupKeys.Length; g++) keys.Add("s_enum_dungeon_group_" + AchieveController.DungeonGroupKeys[g]);
        var missing = ids.Where(i => string.IsNullOrEmpty(Tables().Common(i))).Select(i => "0x" + i.ToString("x"))
            .Concat(keys.Where(k => string.IsNullOrEmpty(Tables().Common(k))));
        Assert.That(missing, Is.Empty, "common strings the party notices read");
    }

    [Test]
    public void TheInviteLineLinksTheNameAndIsKindNineteen()
    {
        var log = new MessengerBrowser { Model = new UiModel(Tables()) };
        log.AddInvitePartyLine("Bob");
        var line = log.Pending.Single();
        Assert.That(line.Kind, Is.EqualTo(0x19));
        Assert.That(line.Msg, Does.Contain("'event:inviteParty,Bob'"));
        Assert.That(line.Msg, Does.Contain("Bob"));
    }
}
