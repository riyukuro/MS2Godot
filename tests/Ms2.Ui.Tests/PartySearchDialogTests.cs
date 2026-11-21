using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The party search window and its register window on their movies (needs MS2_DATA): the sort rows, the result
/// list and its pages, the register / join gating and the register window's checks (uipartysearchdialog.cpp,
/// uipartysearchregisterdialog.cpp).</summary>
public class PartySearchDialogTests
{
    static PackSet packs;
    static MovieSource src;
    static GameTables tables;

    static GameTables Tables()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        packs ??= new PackSet(d);
        src ??= new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
        return tables ??= new GameTables(packs, "en");
    }

    static void Open(UiController c)
    {
        Tables();
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000, c.Libraries);
        p.StageW = 1920; p.StageH = 1080;
        var h = new UiHost(p, c, new UiModel(tables));
        Assert.That(h.Load(), Is.True, c.Movie + " load: " + string.Join("; ", rt.Errors) + " missing: " + string.Join("; ", rt.Missing.Keys));
        for (int i = 0; i < 5; i++) p.Tick();
    }

    sealed class Env
    {
        public readonly PartyBook Book = new();
        public readonly List<IOutPacket> Sent = new();
        public readonly List<int> Notices = new();
        public readonly List<MessageBoxData> Boxes = new();
        public uint Now = 10_000;
        public int RegisterOpens;
        public Env() { Book.Send = Sent.Add; }
    }

    static PartySearchDialogController Search(Env e, Action<PartySearchDialogController> setup = null)
    {
        var c = new PartySearchDialogController(e.Book)
        {
            Common = id => $"c{id:x}",
            TickMs = () => e.Now,
            PlayerCharId = () => 12,
            PlayerName = () => "Me",
            Notice = e.Notices.Add,
            OpenRegister = () => e.RegisterOpens++,
            ShowBox = e.Boxes.Add,
        };
        setup?.Invoke(c);
        Open(c);
        return c;
    }

    static PartyRecruit Recruit(long id, string leader = "Lead", long leaderChar = 11) => new()
    {
        Id = id, PartyId = 9, Category = 3, Kind = 4, Title = "t" + id, AutoJoin = true, MemberCount = 2, MaxMembers = 4,
        LeaderAccountId = 111, LeaderCharacterId = leaderChar, LeaderName = leader, Created = 1,
    };

    static PartyUser User(long id, string name) => new() { AccountId = id + 100, CharacterId = id, Name = name };

    static int ListCount(UiHost h) => Rt.ToInt32(h.Get(h.Get(h.Child("m_itemList"), "dataProvider"), "length"));

    [Test]
    public void TheOpenSendsOneSearchAfterTheDelayAndTheResultsFillTheList()
    {
        var e = new Env();
        var c = Search(e);
        var h = c.Host;
        var sort = h.Child("m_sortListMemberSearch");
        Assert.That(Rt.ToInt32(h.Get(h.Get(sort, "dataProvider"), "length")), Is.EqualTo(4), "the four sort rows");
        Assert.That(Rt.ToBoolean(h.Get(h.Child("m_registerSearch"), "visible")), Is.True, "no own listing: register shown");
        Assert.That(c.SearchQueued, Is.True);
        c.Tick();
        Assert.That(e.Sent.Single(), Is.EqualTo(new PartySearchRequest(0, 0, 0xc, "", 1, 0)));
        c.Tick();
        Assert.That(e.Sent.Count, Is.EqualTo(1), "one search per queue");

        e.Book.Apply(new PartySearchResults(Enumerable.Range(1, 15).Select(i => Recruit(i)).ToArray()));
        Assert.That(c.SearchQueued, Is.False);
        Assert.That(ListCount(h), Is.EqualTo(PartySearchDialogController.PageSize), "a page of 12 rows");

        // a page change inside the delay is refused; after it, the rows of the page go to buildPageTo
        var ret = h.Obj(("result", false));
        c.OnCallback("onChangePage", new object[] { 12, 12, ret });
        Assert.That(Rt.ToBoolean(h.Get(ret, "result")), Is.False);
        e.Now += PartySearchDialogController.SearchDelayMs;
        c.OnCallback("onChangePage", new object[] { 12, 12, ret });
        Assert.That(Rt.ToBoolean(h.Get(ret, "result")), Is.True);
        Assert.That(ListCount(h), Is.EqualTo(15));

        // OnChangeList queues the page's search
        c.OnCallback("OnChangeList", new object[] { 2, ret });
        Assert.That((c.SearchQueued, c.Condition.Page), Is.EqualTo((true, 2)));
    }

    [Test]
    public void TheSortAndSearchTextGoIntoTheConditionAndARefusedTextShowsTheNotice()
    {
        var e = new Env();
        var c = Search(e, c => c.CheckSearchText = t => t == "bad" ? "no" : null);
        c.Tick();
        c.OnCallback("onChangeSortMemeberSearch", new object[] { 0x16, "abc" });
        Assert.That((c.Condition.Kind, c.Condition.Text, c.SearchQueued), Is.EqualTo((0x16, "abc", true)));
        e.Now += 1000;
        c.Tick();
        Assert.That(e.Sent.Last(), Is.EqualTo(new PartySearchRequest(0, 0, 0x16, "abc", 1, 0)));

        c.OnCallback("onClickSearch", new object[] { "bad" });
        Assert.That(c.SearchQueued, Is.False);
        var notice = c.Host.Child("m_notice");
        Assert.That(Rt.ToString(c.Host.Get(notice, "text")), Is.EqualTo("cb33"));

        // a search with a text and no listing: common 0xb32
        e.Book.Apply(new PartySearchResults(Array.Empty<PartyRecruit>()));
        Assert.That(Rt.ToString(c.Host.Get(notice, "text")), Is.EqualTo("cb32"));
    }

    [Test]
    public void RegisterAndModifyAreGatedByThePartyAndTheOwnListing()
    {
        var e = new Env();
        var c = Search(e);
        c.OnCallback("onClickRegisterMemeberSearchModify", null);
        Assert.That(e.Boxes.Single().Msg, Is.EqualTo("cb3f"), "no own listing to modify");
        c.OnCallback("onClickRegisterMemeberSearch", null);
        Assert.That(e.RegisterOpens, Is.EqualTo(1));

        e.Book.RegisterRecruit("ab", true, 4);
        c.OnCallback("onClickRegisterMemeberSearch", null);
        Assert.That(e.Boxes.Last().Msg, Is.EqualTo("cb3d"), "a register pending");

        e.Book.Apply(new PartyRecruitRegistered(Recruit(77, "Me", 12)));
        Assert.That(Rt.ToBoolean(c.Host.Get(c.Host.Child("m_registerSearchModify"), "visible")), Is.True, "the own listing shows modify");
        c.OnCallback("onClickRegisterMemeberSearchModify", null);
        Assert.That(e.RegisterOpens, Is.EqualTo(2));

        // in a party led by another: 0xb3a / 0xb3e
        e.Book.Apply(new PartyInfo(false, 9, 11, new[] { new PartyInfoMember(false, User(11, "Lead")), new PartyInfoMember(false, User(12, "Me")) }, false, 0), 12, false);
        c.OnCallback("onClickRegisterMemeberSearch", null);
        c.OnCallback("onClickRegisterMemeberSearchModify", null);
        Assert.That(e.Boxes.TakeLast(2).Select(b => b.Msg), Is.EqualTo(new[] { "cb3a", "cb3e" }));
    }

    [Test]
    public void JoinInviteAndWhisperFollowTheClientChecks()
    {
        var e = new Env();
        var whispers = new List<string>();
        var c = Search(e, c => c.Whisper = whispers.Add);
        c.Tick();
        e.Book.Apply(new PartySearchResults(new[] { Recruit(5), Recruit(6, "Me", 12) }));
        e.Sent.Clear();

        c.OnCallback("onClickPartyJoin", new object[] { "6" });                 // the own listing
        c.OnCallback("onClickWhisper", new object[] { "Me" });
        c.OnCallback("onClickPartyInvite", new object[] { "Me" });
        Assert.That(e.Notices, Is.EqualTo(new[] { 0xb35, 0xb34, 0xb38 }));
        c.OnCallback("onClickWhisper", new object[] { "Lead" });
        Assert.That(whispers, Is.EqualTo(new[] { "Lead" }));

        c.OnCallback("onClickPartyJoin", new object[] { "5" });
        c.OnCallback("onClickPartyInvite", new object[] { "Lead" });
        Assert.That(e.Sent, Is.EqualTo(new IOutPacket[] { new PartyRecruitJoinRequest(9, "Lead", 5), new PartyInviteRequest("Lead") }));

        // with an own listing the join asks first; yes joins
        e.Book.Apply(new PartyRecruitRegistered(Recruit(77, "Me", 12)));
        e.Sent.Clear();
        c.OnCallback("onClickPartyJoin", new object[] { "5" });
        var box = e.Boxes.Last();
        Assert.That((box.Msg, box.Extra), Is.EqualTo(("cb31", "askPartyJoinByAlreadyMemberSearch,5")));
        Assert.That(e.Sent, Is.Empty);
        c.OnMessageBox(null, 1, box.Extra);
        Assert.That(e.Sent, Is.Empty, "no keeps the listing");
        c.OnMessageBox(null, 0, box.Extra);
        Assert.That(e.Sent.Single(), Is.EqualTo(new PartyRecruitJoinRequest(9, "Lead", 5)));

        // in a party: box 0xb36
        e.Book.Apply(new PartyInfo(false, 9, 12, new[] { new PartyInfoMember(false, User(12, "Me")), new PartyInfoMember(false, User(13, "B")) }, false, 0), 12, false);
        c.OnCallback("onClickPartyJoin", new object[] { "5" });
        Assert.That(e.Boxes.Last().Msg, Is.EqualTo("cb36"));
    }

    static PartySearchRegisterDialogController Register(Env e, Action<PartySearchRegisterDialogController> before = null)
    {
        var c = new PartySearchRegisterDialogController(e.Book)
        {
            Common = id => $"c{id:x}",
            ShowBox = e.Boxes.Add,
            CheckTitle = t => t == "bad words" ? "refused" : null,
            ComboValues = Tables().PartySearchRegisterComboValues,
        };
        before?.Invoke(c);
        Open(c);
        return c;
    }

    [Test]
    public void TheRegisterWindowChecksTheTitleAndSendsTheComboValue()
    {
        var e = new Env();
        var c = Register(e);
        var h = c.Host;
        var values = tables.PartySearchRegisterComboValues;
        Assert.That(values, Is.Not.Empty, "Constants PartySearchRegisterComboValues");
        var combo = h.Child("m_maxUserCountCombo");
        Assert.That(Rt.ToInt32(h.Get(combo, "selectedIndex")), Is.EqualTo(values.Count - 1), "the last row");
        Assert.That(Rt.ToBoolean(h.Get(h.Child("m_btnRemove"), "visible")), Is.False);
        var title = h.Child("m_inputTitle");

        h.Set(title, "text", " a ");
        c.OnRegisterClicked();
        Assert.That(e.Boxes.Last().Msg, Is.EqualTo("cb53"), "under 2 letters");
        h.Set(title, "text", "bad words");
        c.OnRegisterClicked();
        Assert.That(e.Boxes.Last().Msg, Is.EqualTo("refused"));
        Assert.That(e.Sent, Is.Empty);

        h.Set(combo, "selectedIndex", 0);
        h.Set(title, "text", "  go  ");
        h.Set(h.Child("m_checkAllowAutoJoin"), "selected", false);
        c.OnRegisterClicked();
        Assert.That(e.Sent.Single(), Is.EqualTo(new PartyRecruitRegisterRequest("go", false, values[0])));
        for (int i = 0; i < 60 && !c.IsClosed; i++) h.Player.Tick();   // the close tween
        Assert.That((c.IsClosed, c.SavedTitle, c.SavedAutoJoin), Is.EqualTo((true, "go", false)));

        // a refused register empties the saved title
        e.Book.Apply(new PartySearchError(0, 0x6c));
        Assert.That(c.SavedTitle, Is.EqualTo(""));
    }

    [Test]
    public void AnOwnListingShowsDisabledAndTheRemoveAsksFirst()
    {
        var e = new Env();
        var c = Register(e, c =>
        {
            var own = Recruit(77, "Me", 12);
            own.MaxMembers = c.ComboValues[0];
            e.Book.Apply(new PartyRecruitRegistered(own));
        });
        var h = c.Host;
        Assert.That(Rt.ToBoolean(h.Get(h.Child("m_inputTitle"), "enabled")), Is.False);
        Assert.That(Rt.ToString(h.Get(h.Child("m_inputTitle"), "text")), Is.EqualTo("t77"));
        Assert.That(Rt.ToInt32(h.Get(h.Child("m_maxUserCountCombo"), "selectedIndex")), Is.EqualTo(0));
        Assert.That(Rt.ToBoolean(h.Get(h.Child("m_btnRegister"), "visible")), Is.False);
        Assert.That(Rt.ToBoolean(h.Get(h.Child("m_btnRemove"), "visible")), Is.True);

        c.OnRemoveClicked();
        var box = e.Boxes.Last();
        Assert.That((box.Msg, box.Extra), Is.EqualTo(("cb56", "remove")));
        c.OnMessageBox(null, 0, "remove");
        Assert.That(e.Sent.Single(), Is.EqualTo(new PartyRecruitRemoveRequest()));
        for (int i = 0; i < 60 && !c.IsClosed; i++) h.Player.Tick();   // the close tween
        Assert.That(c.IsClosed, Is.True);
    }
}
