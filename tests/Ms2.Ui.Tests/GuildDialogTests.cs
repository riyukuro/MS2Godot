using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Quest;
using Ms2.Ui;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The guild dialog's movie pages (needs MS2_DATA): the accept page's request rows, a page of 7 at 0, 7 and 8
/// requests (FUN_14196da70).</summary>
public class GuildDialogTests
{
    static PackSet packs;
    static MovieSource src;
    static GameTables tables;

    static GuildDialogController Load(GuildBook book, Func<string, string> translate = null)
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        packs ??= TestData.Packs(d);
        src ??= TestData.Movies(packs);
        tables ??= new GameTables(packs, "en");
        var t = tables;
        var c = new GuildDialogController(book)
        {
            Common = t.Common,
            CommonByKey = key => t.Common(key) ?? "",
            LevelByExp = t.GuildLevelByExp,
            PlayerName = () => "Ma",
            PlayerCharId = () => 12,
            FeatureEnabled = _ => false,
            JobName = t.JobName,
            MapName = id => t.MapName(id) ?? id.ToString(),
            ImagePath = (_, _) => "x.png",
            AccumExp = level => t.GuildAccumExp.TryGetValue(level, out var v) ? v : 0,
            MaxLevel = () => t.GuildAccumExp.Count == 0 ? 0 : t.GuildAccumExp.Keys.Last(),
        };
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000, c.Libraries);
        p.StageW = 1920; p.StageH = 1080;
        if (translate != null) p.Translate = translate;
        var h = new UiHost(p, c, new UiModel(t));
        Assert.That(h.Load(), Is.True, c.Movie + " load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 5; i++) p.Tick();
        return c;
    }

    /// <summary>InitializeGuildDialog outside a guild on each data set: the KMS client calls setupCategory(false) (@0x14196b2b9), the
    /// GMS2 client does not (its movie's configUI calls setupCategory() itself); the open leaves no runtime error and the category bar
    /// holds its tabs. On the GMS2 movie the KMS call is the "setupCategory is not a function" error.</summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void OpeningOutsideAGuildRunsTheCategorySetupOfItsOwnClient(string variable)
    {
        var p = TestData.Packs(RcGap.Data(variable));
        var movies = TestData.Movies(p);
        var t = new GameTables(p, "en");
        var c = new GuildDialogController(new GuildBook { Common = id => $"c{id:x}", CommonByKey = k => k, PlayerName = () => "Ma" })
        {
            Common = t.Common,
            CommonByKey = key => t.Common(key) ?? "",
            LevelByExp = t.GuildLevelByExp,
            PlayerName = () => "Ma",
            FeatureEnabled = _ => false,
            JobName = t.JobName,
            MapName = id => t.MapName(id) ?? id.ToString(),
            ImagePath = (_, _) => "x.png",
        };
        var (rt, pl) = movies.Open(c.Movie, r => r.Budget = 20_000_000, c.Libraries);
        pl.StageW = 1920; pl.StageH = 1080;
        var h = new UiHost(pl, c, new UiModel(t));
        Assert.That(h.Load(), Is.True, c.Movie + " load: " + string.Join("; ", rt.Errors));
        // The movie picks the call: the KMS movie's setupCategory takes the flag, the GMS2 movie's none. Nothing here names a
        // session, so a KMS server with GMS2 data (or the reverse) calls what the loaded movie has.
        Assert.That(c.MovieTakesCategoryFlag, Is.EqualTo(LobbyFeatures.LocaleOf(p) != "NA"));
        if (variable == "MS2_GMS2_DATA") Assert.That(c.MovieTakesCategoryFlag, Is.False);
        for (int i = 0; i < 5; i++) pl.Tick();
        Assert.That(rt.Errors, Is.Empty);
        Assert.That(h.Child("m_category") is { } bar ? h.Get(h.Get(bar, "dataProvider"), "length") : null, Is.GreaterThan(0));
    }

    /// <summary>A book in a two-member guild whose grades give the master every right.</summary>
    internal static GuildBook InGuild()
    {
        var b = new GuildBook { Common = id => $"c{id:x}", CommonByKey = k => k, PlayerName = () => "Ma" };
        var info = new GuildInfo { Id = 7, Name = "Gd", MaxMembers = 50, MasterCharId = 12 };
        info.Members.Add(new GuildMember { Mode = 3, Grade = 0, Name = "Ma", CharId = 12, Level = 40, Triple = (0, 0, 0) });
        info.Grades.Add(new GuildGrade(0, "Master", 4095));
        info.Grades.Add(new GuildGrade(1, "Member", 8));
        b.Apply(new GuildMessage((byte)0) { GuildId = 7, Info = info });
        return b;
    }

    const string SkillBoxXml = "<ms2>" +
        "<guildBuff id=\"1\" level=\"1\" requireLevel=\"2\" upgradeCost=\"0\" cost=\"100\" duration=\"1\" cooltime=\"2\"/>" +
        "<guildBuff id=\"1\" level=\"2\" requireLevel=\"5\" upgradeCost=\"300\" cost=\"100\" duration=\"1\" cooltime=\"2\"/>" +
        "<guildBuff id=\"20000\" level=\"1\" requireLevel=\"2\" upgradeCost=\"0\" cost=\"1000\" duration=\"0\" cooltime=\"0\"/>" +
        "</ms2>";

    /// <summary>OnGuildSkillUse / OnGuildMemberSkillUse / OnGuildSkillUpgrade (uiguilddialog.cpp): each callback opens a confirm box; only its yes sends.</summary>
    [Test]
    public void TheSkillCallbacksOpenConfirmBoxesAndOnlyYesSends()
    {
        var sent = new List<IOutPacket>();
        var b = InGuild();
        b.Send = sent.Add;
        b.SkillTable = new Ms2.Formats.World.GuildSkillTable(System.Xml.Linq.XDocument.Parse(SkillBoxXml));
        b.LevelByExp = _ => 5;
        b.ServerTime = () => 1000;
        b.Mesos = () => 5000;
        b.Level = () => 50;
        b.Info.Fund = 9999;
        b.Info.Skills.Add(new GuildSkillState(1, 1, 0));
        b.Info.Skills.Add(new GuildSkillState(20000, 1, 0));
        var c = new GuildDialogController(b) { CommonByKey = k => k, EffectText = (_, _, _) => "n", GroupText = n => n.ToString() };
        var boxes = new List<MessageBoxData>();
        c.ShowBox = boxes.Add;

        Assert.That(c.OnCallback("onGuildSkillUse", new object[] { 1.0 }), Is.True);
        Assert.That(c.OnCallback("onGuildMemberSkillUse", new object[] { 20000.0 }), Is.True);
        Assert.That(c.OnCallback("onGuildSkillUpgrade", new object[] { 1.0 }), Is.False);
        Assert.That(boxes, Has.Count.EqualTo(3));
        Assert.That(sent, Is.Empty, "opening a box sends nothing");
        foreach (var box in boxes) box.InvokeCallback(1, null);
        Assert.That(sent, Is.Empty, "no sends nothing");
        foreach (var box in boxes) box.InvokeCallback(0, null);
        Assert.That(sent.OfType<GuildSkillUseRequest>().Single().SkillId, Is.EqualTo(1));
        Assert.That(sent.OfType<GuildPersonalSkillUseRequest>().Single().SkillId, Is.EqualTo(20000));
        Assert.That(sent.OfType<GuildSkillUpgradeRequest>().Single().SkillId, Is.EqualTo(1));

        boxes.Clear();
        c.OnCallback("onGuildSkillUse", new object[] { 99.0 });
        c.OnCallback("onGuildMemberSkillUse", new object[] { 1.0 });
        Assert.That(boxes, Is.Empty, "no row, and a guild skill is not a personal one");
    }

    /// <summary>OnGuildSkillUse (uiguilddialog.cpp:1105): the box on show closes before the use box shows, so two clicks leave one box.</summary>
    [Test]
    public void TheUseBoxClosesTheShownBoxFirst()
    {
        var b = InGuild();
        b.SkillTable = new Ms2.Formats.World.GuildSkillTable(System.Xml.Linq.XDocument.Parse(SkillBoxXml));
        b.Info.Skills.Add(new GuildSkillState(1, 1, 0));
        var c = new GuildDialogController(b) { CommonByKey = k => k, EffectText = (_, _, _) => "n", GroupText = n => n.ToString() };
        var order = new List<string>();
        c.NextBox = () => order.Add("next");
        c.ShowBox = _ => order.Add("show");
        c.OnCallback("onGuildSkillUse", new object[] { 1.0 });
        c.OnCallback("onGuildSkillUse", new object[] { 1.0 });
        Assert.That(order, Is.EqualTo(new[] { "next", "show", "next", "show" }));
    }

    /// <summary>The guild page writes the duration with FormatTimeSpan (:2213), the member page with the short time text (:2324).</summary>
    [Test]
    public void ThePersonalSkillInfoUsesTheShortTimeText()
    {
        var b = InGuild();
        b.SkillTable = new Ms2.Formats.World.GuildSkillTable(System.Xml.Linq.XDocument.Parse(SkillBoxXml));
        var c = new GuildDialogController(b) { Common = id => "c" + id + ":{0}", CommonByKey = k => k + ":{0}", TimeSpanText = n => "span" + n };
        var s = b.SkillTable.Get(1, 1);
        Assert.That(c.SkillInfoText(s), Does.Contain("span60"));
        Assert.That(c.SkillInfoText(s, true), Does.Not.Contain("span"));
        Assert.That(c.SkillInfoText(s, true), Does.Contain("c2428:1"), "60 s is the one-minute text 0x97c");
    }

    /// <summary>UpdateSkillInfo's inputs are whole seconds, so it pushes once a second and a rebuild waits for the next one.</summary>
    [Test]
    public void TheSkillInfoUpdateRunsOncePerServerSecond()
    {
        var b = InGuild();
        b.SkillTable = new Ms2.Formats.World.GuildSkillTable(System.Xml.Linq.XDocument.Parse(SkillBoxXml));
        long now = 1000;
        b.ServerTime = () => now;
        b.Level = () => 50;
        b.Info.Skills.Add(new GuildSkillState(1, 1, 990));      // running: 50 s of duration, 110 s of cooldown left
        var c = Load(b);
        int built = 0;
        c.EffectIcon = (_, _) => "icon.png";
        c.EffectText = (_, _, _) => { built++; return "n"; };
        c.OpenCategoryPage("Skill");
        int afterOpen = built;
        Assert.That(afterOpen, Is.GreaterThan(0));
        c.Tick();
        Assert.That(built, Is.EqualTo(afterOpen), "the skill is still running");
        b.Info.Skills[0] = new GuildSkillState(1, 1, 0);        // both times over, in the same second
        c.Tick();
        Assert.That(built, Is.EqualTo(afterOpen), "the same second: nothing is read again");
        now = 1001;
        c.Tick();
        Assert.That(built, Is.GreaterThan(afterOpen), "the next second sees the skill ran out and rebuilds the page");
    }

    /// <summary>CreateSkillPage / CreateMemberSkillPage (@0x14196a590, @0x14196a640): the pages are made on the category, the fund texts
    /// come from the guild fund and the player's mesos, and HideAllPages hides them. The rows go to the AS3 page, which this does not read.</summary>
    [Test]
    public void TheSkillCategoriesMakeTheSkillPagesWithTheirFunds()
    {
        var b = InGuild();
        b.SkillTable = new Ms2.Formats.World.GuildSkillTable(System.Xml.Linq.XDocument.Parse(SkillBoxXml));
        b.Info.Fund = 12345; b.Info.EventFund = 678;
        b.Mesos = () => 4242;
        b.Level = () => 50;
        b.Info.Skills.Add(new GuildSkillState(1, 1, 0));
        var c = Load(b);
        c.GroupText = n => "g" + n;
        c.EffectIcon = (_, _) => "icon.png";
        c.OpenCategoryPage("Skill");
        Assert.That(c.SkillPage, Is.Not.Null);
        Assert.That(Rt.ToBoolean(c.Host.Get(c.SkillPage, "visible")), Is.True);
        Assert.That(c.Host.Get(c.Host.Child("m_guildFund", c.SkillPage), "text")?.ToString(), Is.EqualTo("g12345"));
        // the event fund text where the page has one (the GMS2 movie's guild_skill_page has no m_guildEventFund)
        if (c.Host.Child("m_guildEventFund", c.SkillPage) is { } eventFund && !Rt.IsNullish(eventFund))
            Assert.That(c.Host.Get(eventFund, "text")?.ToString(), Is.EqualTo("g678"));
        else Assert.That(c.Host.Child("m_guildFund", c.SkillPage), Is.Not.Null, "the page itself is the data's");
        c.OpenCategoryPage("MemberSkill");
        Assert.That(Rt.ToBoolean(c.Host.Get(c.SkillPage, "visible")), Is.False, "HideAllPages");
        Assert.That(c.SkillPage2, Is.Not.Null);
        Assert.That(c.SkillPage2, Is.Not.SameAs(c.SkillPage));
        Assert.That(c.Host.Get(c.Host.Child("m_guildFund", c.SkillPage2), "text")?.ToString(), Is.EqualTo("g4242"));
    }

    const string EventXml = "<ms2><guildEvent id=\"1\" mapCode=\"2000001\" imagePath=\"a.png\"/><guildEvent id=\"2\" mapCode=\"2000002\" imagePath=\"b.png\"/></ms2>";

    static void Click(GuildDialogController c, object button) =>
        c.Host.Player.Call(button, "dispatchEvent", new object[] { c.Host.RT.GetClass("flash.events.Event").Construct("buttonClick", false, false) });

    /// <summary>CreateEventPage / FUN_141970900 / PurgeExpiredRequests (@0x14196a6f0, @0x141970900, @0x1419760c0): the page is made on the category,
    /// each open custom event waits 30 seconds after its time, and once the tick passes the rows are rebuilt.</summary>
    [Test]
    public void TheEventCategoryMakesThePageAndRebuildsTheRowsWhenAnOpenEventExpires()
    {
        var b = InGuild();
        b.ServerTime = () => 1000;
        b.Info.CustomEvents.Add((1, 990));      // open: 990 + 30 > 1000, expires 20 s on
        b.Info.CustomEvents.Add((2, 900));      // closed
        var c = Load(b);
        uint tick = 5000;
        int built = 0;
        c.TickCount = () => tick;
        c.Events = new Ms2.Formats.World.GuildEventTable(System.Xml.Linq.XDocument.Parse(EventXml));
        c.FieldCapacity = _ => { built++; return 10; };
        c.OpenCategoryPage("Event");
        Assert.That(c.EventPage, Is.Not.Null);
        Assert.That(Rt.ToBoolean(c.Host.Get(c.EventPage, "visible")), Is.True);
        Assert.That(built, Is.EqualTo(2), "a row per event");
        Assert.That(c.EventRequestCount, Is.EqualTo(1), "only the open event waits");
        tick = 5000 + 19_999; c.Tick();
        Assert.That(built, Is.EqualTo(2), "not expired yet");
        tick = 5000 + 20_001; c.Tick();
        Assert.That(built, Is.EqualTo(4), "expired: the rows are rebuilt");
        Assert.That(c.EventRequestCount, Is.EqualTo(1), "the server time is fixed, so the event is open again");
        c.OpenCategoryPage("Skill");
        Assert.That(Rt.ToBoolean(c.Host.Get(c.EventPage, "visible")), Is.False, "HideAllPages");
    }

    /// <summary>FUN_141970900: a row whose custom event or field is missing ends the build (no buildGuildEvent, no later row).</summary>
    [Test]
    public void AnEventWithoutACustomEventOrAFieldEndsTheBuild()
    {
        var b = InGuild();
        b.Info.CustomEvents.Add((2, 0));
        var c = Load(b);
        int asked = 0;
        c.Events = new Ms2.Formats.World.GuildEventTable(System.Xml.Linq.XDocument.Parse(EventXml));
        c.FieldCapacity = _ => { asked++; return null; };
        c.OpenCategoryPage("Event");
        Assert.That(asked, Is.EqualTo(0), "event 1 has no custom event: the loop returns before the field");
        Assert.That(c.EventRequestCount, Is.EqualTo(0));
    }

    /// <summary>Captures the rows of buildGuildEvent; each call adds the rows (id, eventName, eventDesc, state,
    /// isOpenTag) to the list.</summary>
    static List<List<(object Id, object Name, object Desc, object State, object Open)>> CaptureBuilds(GuildDialogController c)
    {
        var calls = new List<List<(object, object, object, object, object)>>();
        c.EventRowsBuilt += list => calls.Add(list.Select(row => (c.Host.Get(row, "id"), c.Host.Get(row, "eventName"), c.Host.Get(row, "eventDesc"),
            c.Host.Get(row, "state"), c.Host.Get(row, "isOpenTag"))).ToList());
        return calls;
    }

    /// <summary>FUN_141970900: the row's name is string table 3 of the map code, its description table 0x26 of the event id, the open event is
    /// state 1 and tagged open (a swap of the two tables, or of the state, fails).</summary>
    [Test]
    public void TheRowsTakeTheirNameFromTable3AndTheirDescriptionFromTable38()
    {
        var b = InGuild();
        b.ServerTime = () => 1000;
        b.Info.CustomEvents.Add((1, 990));
        b.Info.CustomEvents.Add((2, 900));
        var c = Load(b);
        c.StringTableText = (t, id) => $"{t}:{id}";
        c.TickCount = () => 5000;
        c.Events = new Ms2.Formats.World.GuildEventTable(System.Xml.Linq.XDocument.Parse(EventXml));
        c.FieldCapacity = _ => 10;
        c.OpenCategoryPage("Event");
        var calls = CaptureBuilds(c);
        b.Apply(new Ms2.Net.Packets.GuildMessage((byte)0x38) { Name = "x", EventId = 1, EventTime = 990 });
        var rows = calls.Single();
        Assert.That(rows.Select(r => (Convert.ToInt32(r.Id), r.Name?.ToString(), r.Desc?.ToString(), Convert.ToInt32(r.State), Rt.ToBoolean(r.Open))),
            Is.EqualTo(new[] { (1, "3:2000001", "38:1", 1, true), (2, "3:2000002", "38:2", 0, false) }));
    }

    /// <summary>FUN_141970900: both custom events exist and the field of event 1 is unknown: the field is asked once and no row is built.</summary>
    [Test]
    public void AnUnknownFieldEndsTheBuildAfterOneAskAndWithoutTheInvoke()
    {
        var b = InGuild();
        b.Info.CustomEvents.Add((1, 0));
        b.Info.CustomEvents.Add((2, 0));
        var c = Load(b);
        c.Events = new Ms2.Formats.World.GuildEventTable(System.Xml.Linq.XDocument.Parse(EventXml));
        c.FieldCapacity = _ => 10;
        c.OpenCategoryPage("Event");
        var calls = CaptureBuilds(c);
        int asked = 0;
        c.FieldCapacity = _ => { asked++; return null; };
        b.Apply(new Ms2.Net.Packets.GuildMessage((byte)0x38) { Name = "x", EventId = 1, EventTime = 0 });
        Assert.That(asked, Is.EqualTo(1));
        Assert.That(calls, Is.Empty);
    }

    /// <summary>PurgeExpiredRequests: the row expires when the tick passes it; at exactly the expire tick (strict less-than) nothing is rebuilt.</summary>
    [Test]
    public void ATickExactlyAtTheExpireTimeRebuildsNothing()
    {
        var b = InGuild();
        b.ServerTime = () => 1000;
        b.Info.CustomEvents.Add((1, 990));
        b.Info.CustomEvents.Add((2, 900));
        var c = Load(b);
        uint tick = 5000;
        c.TickCount = () => tick;
        c.Events = new Ms2.Formats.World.GuildEventTable(System.Xml.Linq.XDocument.Parse(EventXml));
        c.FieldCapacity = _ => 10;
        c.OpenCategoryPage("Event");
        var calls = CaptureBuilds(c);
        tick = 25000; c.Tick();
        Assert.That(calls, Is.Empty, "expire = 5000 + (30 - 10) * 1000");
        tick = 25001; c.Tick();
        Assert.That(calls, Has.Count.EqualTo(1));
    }

    /// <summary>The vs game modes 0x76 and 0x77 reach the open dialog's UpdateEventButtons (FUN_141968140): the button follows the registration.</summary>
    [Test]
    public void TheVsGameModesUpdateTheOpenDialogsButtons()
    {
        var b = InGuild();
        var c = Load(b);
        c.FeatureEnabled = _ => true;
        c.OpenCategoryPage("Event");
        var (reg, unreg) = c.EventButtons;
        Assert.That(Rt.ToBoolean(c.Host.Get(reg, "visible")), Is.True);
        b.Apply(new Ms2.Net.Packets.GuildMessage((byte)0x76) { Accepted = true });
        Assert.That(Rt.ToBoolean(c.Host.Get(unreg, "visible")), Is.True);
        Assert.That(Rt.ToBoolean(c.Host.Get(reg, "visible")), Is.False);
        b.Apply(new Ms2.Net.Packets.GuildMessage((byte)0x77) { Value = 5 });
        Assert.That(Rt.ToBoolean(c.Host.Get(reg, "visible")), Is.True, "0x77 clears the registration");
    }

    /// <summary>UpdateEventButtons (@0x1419761f0) and the click handlers (@0x141976380, @0x1419763d0): both hidden without feature 0x78; else the
    /// register button, or the unregister button once registered; each click sends its mode.</summary>
    [Test]
    public void TheEventButtonsFollowTheFeatureAndTheRegistrationAndSendTheirModes()
    {
        var b = InGuild();
        var sent = new List<Ms2.Net.Wire.IOutPacket>();
        b.Send = sent.Add;
        var c = Load(b);
        bool on = false;
        c.FeatureEnabled = _ => on;
        c.OpenCategoryPage("Event");
        var (reg, unreg) = c.EventButtons;
        Assert.That(reg, Is.Not.Null);
        Assert.That(Rt.ToBoolean(c.Host.Get(reg, "visible")), Is.False);
        Assert.That(Rt.ToBoolean(c.Host.Get(unreg, "visible")), Is.False);
        on = true; c.UpdateEventButtons();
        Assert.That(Rt.ToBoolean(c.Host.Get(reg, "visible")), Is.True);
        Assert.That(Rt.ToBoolean(c.Host.Get(unreg, "visible")), Is.False);
        Click(c, reg);
        Assert.That(sent.OfType<Ms2.Net.Packets.GuildVsGameRegisterRequest>().Count(), Is.EqualTo(1));
        b.Info.F354 = true; c.UpdateEventButtons();
        Assert.That(Rt.ToBoolean(c.Host.Get(reg, "visible")), Is.False);
        Assert.That(Rt.ToBoolean(c.Host.Get(unreg, "visible")), Is.True);
        Click(c, unreg);
        Assert.That(sent.OfType<Ms2.Net.Packets.GuildVsGameUnregisterRequest>().Count(), Is.EqualTo(1));
    }

    /// <summary>The data's guild events (guildevent.xml): each one has a map name (string table 3), a description (table 0x26) and a field
    /// capacity, so the page's rows build whole.</summary>
    [Test]
    public void TheDataGuildEventsHaveNamesDescriptionsAndFields()
    {
        Load(InGuild());
        var ev = new Ms2.Formats.World.GuildEventTable(packs);
        Assert.That(ev.Count, Is.GreaterThan(0));
        foreach (int id in ev.Ids())
        {
            var e = ev.Get(id);
            Assert.That(tables.StringTableText(3, e.MapCode), Is.Not.Empty, "map name of " + e.MapCode);
            Assert.That(tables.StringTableText(0x26, e.Id), Is.Not.Empty, "description of " + e.Id);
            Assert.That(Ms2.Formats.World.FieldData.FindById(packs, e.MapCode)?.Capacity, Is.GreaterThan(0), "capacity of " + e.MapCode);
        }
    }

    /// <summary>CreateManagePage (@0x14196a1b0): the page is made on the category and hidden by the other categories. The rows it gets
    /// (grades, members) go to the AS3 page, which this does not read.</summary>
    [Test]
    public void TheManagerCategoryMakesThePageAndTheOthersHideIt()
    {
        var b = InGuild();
        var c = Load(b);
        c.OpenCategoryPage("Manager");
        Assert.That(c.ManagePage, Is.Not.Null);
        Assert.That(Rt.ToBoolean(c.Host.Get(c.ManagePage, "visible")), Is.True);
        c.OpenCategoryPage("Event");
        Assert.That(Rt.ToBoolean(c.Host.Get(c.ManagePage, "visible")), Is.False, "HideAllPages");
        c.OpenCategoryPage("Manager");
        Assert.That(Rt.ToBoolean(c.Host.Get(c.ManagePage, "visible")), Is.True);
    }

    const string HouseTableXml = "<ms2><guildHouse level='1' theme='1' stringKey='4' areaSize='25' facility='11,12,13' imagePath='h.png'/></ms2>";
    const string NpcTableXml = "<ms2>" +
        "<npc type='goods' level='1' upgradeCost='0' requireGuildLevel='2' requireHouseLevel='1' stringKey='6'/>" +
        "<npc type='goods' level='2' upgradeCost='900' requireGuildLevel='3' requireHouseLevel='2' stringKey='6'/>" +
        "<npc type='equip' level='1' upgradeCost='5' requireGuildLevel='1' requireHouseLevel='1' stringKey='7'/></ms2>";

    static (GuildDialogController c, List<IReadOnlyList<object>> npc, List<IReadOnlyList<object>> fac) HousePage(GuildBook b, bool feature = true)
    {
        b.Houses = new Ms2.Formats.World.GuildHouseTable(System.Xml.Linq.XDocument.Parse(HouseTableXml));
        b.Npcs = new Ms2.Formats.World.GuildNpcTable(System.Xml.Linq.XDocument.Parse(NpcTableXml));
        var c = Load(b);
        c.FeatureEnabled = id => feature && id == 0x5b;
        c.StringTableText = (t, id) => $"{t}:{id}";
        var npc = new List<IReadOnlyList<object>>(); var fac = new List<IReadOnlyList<object>>();
        c.NpcRowsBuilt += r => npc.Add(r.ToList()); c.HouseFacilityRowsBuilt += r => fac.Add(r.ToList());
        return (c, npc, fac);
    }

    /// <summary>CreateHousePage / FUN_141971490 / FUN_141971ac0: the page is made on the category, hidden by the others, and does nothing
    /// without feature 0x5b.</summary>
    [Test]
    public void TheHouseCategoryMakesThePageAndTheOthersHideItAndNoFeatureDoesNothing()
    {
        var (c, npc, fac) = HousePage(InGuild());
        c.OpenCategoryPage("House");
        Assert.That(c.HousePage, Is.Not.Null);
        Assert.That(Rt.ToBoolean(c.Host.Get(c.HousePage, "visible")), Is.True);
        Assert.That(npc.Count, Is.EqualTo(1));
        Assert.That(fac.Count, Is.EqualTo(1));
        c.OpenCategoryPage("Event");
        Assert.That(Rt.ToBoolean(c.Host.Get(c.HousePage, "visible")), Is.False);

        var (off, npc2, fac2) = HousePage(InGuild(), false);
        off.OpenCategoryPage("House");
        Assert.That(npc2.Count + fac2.Count, Is.EqualTo(0));
    }

    [Test]
    public void TheFacilityRowsPairTheNamesFromTable0x37()
    {
        var (c, _, fac) = HousePage(InGuild());
        c.OpenCategoryPage("House");
        var rows = fac.Single();
        Assert.That(rows.Count, Is.EqualTo(2));
        Assert.That(c.Host.Get(rows[0], "facility1"), Is.EqualTo("55:11"));
        Assert.That(c.Host.Get(rows[0], "facility2"), Is.EqualTo("55:12"));
        Assert.That(c.Host.Get(rows[1], "facility1"), Is.EqualTo("55:13"));
    }

    [Test]
    public void TheNpcRowsHoldTheNextLevelsConditionsAndTheMasterRuleAndMaxAtTheTop()
    {
        var b = InGuild();
        b.LevelByExp = _ => 2;
        var (c, npc, _) = HousePage(b);
        c.OpenCategoryPage("House");
        var rows = npc.Last();
        object Row(int kind) => rows.First(r => Convert.ToInt32(c.Host.Get(r, "type")) == kind);
        Assert.That(rows.Count, Is.EqualTo(2));
        // goods (kind 2) at level 0: the next row is level 1 (guild level 2, house level 1, cost 0): upgradable by the master
        Assert.That(Rt.ToBoolean(c.Host.Get(Row(2), "canUpgrade")), Is.True);
        Assert.That(c.Host.Get(Row(2), "upgradeCost"), Is.EqualTo(tables.Common("s_guild_ui_house_npc_upgrade_nocost") ?? ""));
        // equip (kind 1): the guild level reaches it too
        Assert.That(Rt.ToBoolean(c.Host.Get(Row(1), "canUpgrade")), Is.True);
        // goods at level 2 is the top: MAX and no upgrade
        b.Apply(new GuildMessage((byte)0x3b) { Name = "Ma", NpcKind = 2, NpcLevel = 2 });
        var top = npc.Last().First(r => Convert.ToInt32(c.Host.Get(r, "type")) == 2);
        Assert.That(c.Host.Get(top, "condition"), Is.EqualTo("MAX"));
        Assert.That(c.Host.Get(top, "upgradeCost"), Is.EqualTo("MAX"));
        Assert.That(Rt.ToBoolean(c.Host.Get(top, "canUpgrade")), Is.False);
    }

    [Test]
    public void OnlyTheMasterCanUpgradeAnNpc()
    {
        var b = InGuild();
        b.LevelByExp = _ => 3;
        b.Info.MasterCharId = 99;
        var (c, npc, _) = HousePage(b);
        c.OpenCategoryPage("House");
        Assert.That(npc.Last().Select(r => Rt.ToBoolean(c.Host.Get(r, "canUpgrade"))), Is.All.False);
    }

    [Test]
    public void ALowGuildOrHouseLevelBlocksTheUpgrade()
    {
        var b = InGuild();
        b.LevelByExp = _ => 1;                     // goods level 1 needs guild level 2
        var (c, npc, _) = HousePage(b);
        c.OpenCategoryPage("House");
        var goods = npc.Last().First(r => Convert.ToInt32(c.Host.Get(r, "type")) == 2);
        Assert.That(Rt.ToBoolean(c.Host.Get(goods, "canUpgrade")), Is.False, "guild level 1 < 2");
        b.LevelByExp = _ => 3;
        b.Apply(new GuildMessage((byte)0x3b) { Name = "Ma", NpcKind = 2, NpcLevel = 1 });   // next is level 2: house level 2 needed
        goods = npc.Last().First(r => Convert.ToInt32(c.Host.Get(r, "type")) == 2);
        Assert.That(Rt.ToBoolean(c.Host.Get(goods, "canUpgrade")), Is.False, "house level 1 < 2");
        b.Apply(new GuildMessage((byte)0x39) { Name = "Ma", HouseLevel = 2, HouseTheme = 1 });
        goods = npc.Last().First(r => Convert.ToInt32(c.Host.Get(r, "type")) == 2);
        Assert.That(Rt.ToBoolean(c.Host.Get(goods, "canUpgrade")), Is.True);
    }

    [Test]
    public void TheMemberPopupCallbackRaisesTheMemberFoundByName()
    {
        // OnGuildMemberPopupMenu (uiguilddialog.cpp:811): x, y, the name; a name not in the guild opens nothing
        var c = new GuildDialogController(InGuild());
        var seen = new List<(float, float, string)>();
        c.MemberPopupRequested += (x, y, m) => seen.Add((x, y, m.Name));
        Assert.That(c.OnCallback("OnGuildMemberPopupMenu", new object[] { 10.0, 20.0, "Ma" }), Is.True);
        c.OnCallback("OnGuildMemberPopupMenu", new object[] { 10.0, 20.0, "Nobody" });
        Assert.That(seen, Is.EqualTo(new[] { (10f, 20f, "Ma") }));
    }

    [Test]
    public void TheDungeonButtonOpensTheDungeonRoomWindowWithTheGuildGroupType()
    {
        // onOpenDungeonDialog (uiguilddialog.cpp:1230): the window opens with SetGroupType(6)
        var c = new GuildDialogController(InGuild());
        var seen = new List<int>();
        c.OpenDungeonRoomDialog = seen.Add;
        Assert.That(c.OnCallback("onOpenDungeonDialog", Array.Empty<object>()), Is.True);
        Assert.That(seen, Is.EqualTo(new[] { GuildDialogController.DungeonRoomGroupType }));
        Assert.That(GuildDialogController.DungeonRoomGroupType, Is.EqualTo(6));
    }

    [Test]
    public void TheRankButtonsOpenTheRankingDialogOrThePage()
    {
        // OnClickOpenMayview / OnOpenGuildRank / OnViewPvpRank: feature 0x3f the dialog's category, else 0x3b the page
        var c = new GuildDialogController(InGuild());
        var seen = new List<string>();
        c.RankingRequested += k => seen.Add($"dialog {k}");
        c.RankPageRequested += u => seen.Add(u);
        var on = new HashSet<int>();
        c.FeatureEnabled = on.Contains;
        string[] names = { "OnClickOpenMayview", "OnOpenGuildRank", "OnViewPvpRank" };
        foreach (var n in names) Assert.That(c.OnCallback(n, Array.Empty<object>()), Is.True);
        Assert.That(seen, Is.Empty);
        on.Add(0x3b);
        foreach (var n in names) c.OnCallback(n, Array.Empty<object>());
        on.Add(0x3f);
        foreach (var n in names) c.OnCallback(n, Array.Empty<object>());
        Assert.That(seen, Is.EqualTo(new[] { "maview:/Rank/Guild", "maview:/Rank/Guild?tp=realtime", "maview:/Rank/GuildPVP?tp=realtime",
            "dialog 1", "dialog 1", "dialog 5" }));
    }

    [Test]
    public void TheAttendBackgroundFollowsCanAttendAndTheDonateClickShowsThePopupOrTheMaxBox()
    {
        const long now = 1_795_262_400;
        var b = InGuild();
        b.ServerTime = () => now;
        var me = b.FindMemberByName("Ma");
        me.Fd8 = now - 60;                              // attended today
        var c = Load(b);
        Assert.That(tables.GuildProperties.Values.Any(p => p.FundMax > 0 && p.DonationMax > 0), Is.True, "GuildProperty.xml fundMax / donationMax");
        Assert.That(tables.GuildDonateMeso, Is.GreaterThan(0));
        var h = c.Host;
        var attend = h.Child("m_attendBG");
        Assert.That(attend, Is.Not.Null);
        Assert.That(Rt.ToBoolean(h.Get(attend, "visible")), Is.False);

        var boxes = new List<string>();
        c.ShowBox = d => boxes.Add(d.Msg);
        b.LevelByExp = _ => 1;
        b.Property = _ => new GuildProperty(1_000_000, 2);
        Assert.That(c.OnCallback("onClickDonate", Array.Empty<object>()), Is.True);
        Assert.That(boxes, Is.Empty);
        me.Ff0 = 2; me.Ff8 = now - 60;
        Assert.That(c.OnCallback("onClickDonate", Array.Empty<object>()), Is.True);
        Assert.That(boxes.Single(), Is.EqualTo(c.CommonByKey("s_guild_err_donateMax")));
        b.Property = _ => null;
        Assert.That(c.OnCallback("onClickDonate", Array.Empty<object>()), Is.False, "no property is the client's return 0");
    }

    [Test]
    public void TheGradeEditSendsTheChangedGradesAndBoxesTheRefusedNames()
    {
        // OnChangeGrade (uiguilddialog.cpp:931): an unchanged grade is skipped, a refused name is a line of 0x423 in the
        // box 0x422 (an empty refusal adds no line), a changed grade is SendGuildMode67; a row past the grades is skipped
        var b = InGuild();
        var sent = new List<IOutPacket>();
        b.Send = sent.Add;
        b.CheckGradeName = n => n == "Bad" ? "ban" : n == "Hush" ? "" : null;
        var c = Load(b);
        var boxes = new List<MessageBoxData>();
        c.ShowBox = boxes.Add;
        var h = c.Host;
        AsArray Rows(params (string name, int auth)[] rows)
        {
            var a = new AsArray();
            a.Items.AddRange(rows.Select(r => (object)h.Obj(("gradeName", r.name), ("gradeAuth", r.auth))));
            return a;
        }

        Assert.That(c.OnCallback("OnChangeGrade", new object[] { Rows(("Master", 4095), ("Mem2", 8), ("Extra", 1)) }), Is.False);
        Assert.That(sent, Is.EqualTo(new[] { new GuildGradeRequest(1, new GuildGrade(1, "Mem2", 8)) }));
        Assert.That(boxes, Is.Empty);

        c.OnCallback("OnChangeGrade", new object[] { Rows(("Bad", 4095), ("Hush", 8)) });
        Assert.That(sent.Count, Is.EqualTo(1));
        var t = tables;
        Assert.That(boxes.Single().Msg, Is.EqualTo(ClientFormat.Format(t.Common(0x422), ClientFormat.Format(t.Common(0x423), "Master", "ban"))));

        b.PlayerName = () => "Bo";                      // not the master: SendGuildMode67 sends nothing
        c.OnCallback("OnChangeGrade", new object[] { Rows(("Master", 4095), ("Mem3", 8)) });
        Assert.That(sent.Count, Is.EqualTo(1));
    }

    static GuildJoinRequestForGuild Request(long id) => new() { Id = id, GuildId = 7, CharId = 13, Name = $"bo{id}", Job = 200, Level = 20 };

    [Test]
    public void TheAcceptPagePagesSevenRequests()
    {
        var b = InGuild();
        var c = Load(b);
        c.OpenCategoryPage("AcceptMember");
        var page = c.AcceptRequestPage;
        Assert.That(page, Is.Not.Null);
        var h = c.Host;

        void Rows(int n) => b.Apply(new GuildMessage(0x55) { GuildRequests = Enumerable.Range(0, n).Select(i => Request(i)).ToList() });

        Rows(0);                                        // the manager's notify builds the rows (FUN_14196da70)
        var list = h.Child("m_list", page);
        Assert.That(list, Is.Not.Null);
        Assert.That(Rt.ToNumber(h.Get(h.Get(list, "dataProvider"), "length")), Is.EqualTo(0));     // no rows
        Assert.That(Rt.ToNumber(h.Get(h.Child("m_pageText", page), "text")), Is.EqualTo(1));       // page 1
        Assert.That(Rt.ToNumber(h.Get(h.Child("m_prevPage", page), "enabled")), Is.EqualTo(0));    // 1 of 0 pages

        Rows(7);
        Assert.That(Rt.ToNumber(h.Get(h.Get(list, "dataProvider"), "length")), Is.EqualTo(7));
        Assert.That(Rt.ToNumber(h.Get(h.Child("m_nextPage", page), "enabled")), Is.EqualTo(0));   // 1 of 1

        Rows(8);
        Assert.That(Rt.ToNumber(h.Get(h.Get(list, "dataProvider"), "length")), Is.EqualTo(7));    // the first page
        Assert.That(Rt.ToNumber(h.Get(h.Child("m_nextPage", page), "enabled")), Is.EqualTo(1));   // 1 of 2
        c.OnCallback("OnAcceptMemberNextPage", Array.Empty<object>());
        Assert.That(Rt.ToNumber(h.Get(h.Get(list, "dataProvider"), "length")), Is.EqualTo(1));     // the eighth row
        c.OnCallback("OnAcceptMemberNextPage", Array.Empty<object>());
        Assert.That(Rt.ToNumber(h.Get(h.Get(list, "dataProvider"), "length")), Is.EqualTo(7));     // past the end: page 1 again
        Assert.That(Rt.ToNumber(h.Get(h.Child("m_pageText", page), "text")), Is.EqualTo(1));
        c.OnCallback("OnAcceptMemberPrevPage", Array.Empty<object>());
        Assert.That(Rt.ToNumber(h.Get(h.Get(list, "dataProvider"), "length")), Is.EqualTo(7));     // page 1 keeps its rows
    }

    [Test]
    public void TheMemberCallsOpenTheirBoxesAndTheAnswersSend() {
        var b = InGuild();
        b.Info.Members.Add(new GuildMember { Mode = 3, Grade = 1, Name = "Bo", CharId = 13, Level = 20, Triple = (0, 0, 0) });
        var sent = new List<IOutPacket>(); var notices = new List<string>();
        b.Send = sent.Add;
        b.Notice += (t, _, _) => notices.Add(t);
        var c = Load(b);
        var boxes = new List<MessageBoxData>();
        c.ShowBox = boxes.Add;
        var t = tables;

        c.OnCallback("onExpelMember", new object[] { "Ma" });          // the master: the notice 0x3e1, no box
        c.OnCallback("onChangeMemberGrade", new object[] { "Ma", 1 });
        Assert.That((boxes.Count, sent.Count, notices.Count), Is.EqualTo((0, 0, 2)));
        Assert.That(notices[0], Is.EqualTo(t.Common(0x3e1)));

        c.OnCallback("onExpelMember", new object[] { "Bo" });          // m_strDesc, the box 0x14 of FormatStringId(0x3e3)
        Assert.That((boxes[0].Type, boxes[0].Msg), Is.EqualTo((0x14, ClientFormat.Format(t.Common(0x3e3), "Bo"))));
        c.OnMessageBox(null, 0, "expelMember");
        Assert.That(sent.Single(), Is.EqualTo(new GuildExpelRequest("Bo")));

        c.OnCallback("onChangeMaster", new object[] { "Bo" });
        Assert.That(boxes[1].Msg, Is.EqualTo(ClientFormat.Format(t.Common(0x3e2), "Bo")));
        c.OnMessageBox(null, 0, "changeMaster");
        Assert.That(sent.Last(), Is.EqualTo(new GuildChangeMasterRequest("Bo")));

        c.OnCallback("onChangeMemberGrade", new object[] { "Bo", 1 });
        Assert.That(sent.Last(), Is.EqualTo(new GuildMemberGradeRequest("Bo", 1)));

        c.OnCallback("onClickInviteMember", Array.Empty<object>());
        Assert.That((boxes[2].Type, boxes[2].InputType, boxes[2].Msg), Is.EqualTo((5, 2, t.Common(0x3da))));
        c.OnMessageBox(null, 0, "addMember,Cy");
        Assert.That(sent.Last(), Is.EqualTo(new GuildInviteRequest("Cy")));

        b.Info.Notice = "old";
        c.OnCallback("OnChangeNotify", Array.Empty<object>());
        Assert.That((boxes[3].Type, boxes[3].InputDefault, boxes[3].InputMaxChars), Is.EqualTo((0xb, "old", 300)));
        c.OnMessageBox(null, 0, "changeNotify,new");
        Assert.That(sent.Last(), Is.EqualTo(new GuildTextRequest(1, "new")));

        Assert.That(c.OnCallback("OnClickMemoButton", new object[] { "Bo" }), Is.True);   // another member's row: no box
        Assert.That(boxes.Count, Is.EqualTo(4));
        c.OnCallback("OnClickMemoButton", new object[] { "Ma" });
        Assert.That((boxes[4].InputMaxChars, boxes[4].Msg), Is.EqualTo((0x14, t.Common("s_guild_ui_memo") ?? "")));
        c.OnMessageBox(null, 0, "changeMemo,memo");
        Assert.That(sent.Last(), Is.EqualTo(new GuildMemoRequest("memo")));

        b.Apply(new GuildMessage(0x44) { Focus = 1, Value = 0x204 });
        c.OnCallback("onSetPropensity", new object[] { 2, 0x208 });   // type 2 keeps the focus
        Assert.That(sent.Last(), Is.EqualTo(new GuildPropensityRequest(1, 0x208)));
        c.OnCallback("OnChageRegistSearch", new object[] { false });   // the flag with the propensity it has
        Assert.That(sent.Last(), Is.EqualTo(new GuildPropensityRequest(0, 0x204)));

        c.OnCallback("onChageGradeError", new object[] { 1 });
        Assert.That(notices.Last(), Is.EqualTo(t.Common(999)));
    }

    static (GuildDialogController c, List<string> text) StoragePage(GuildBook b)
    {
        var c = Load(b);
        c.StringTableText = (t, id) => $"{t}:{id}";
        c.CommonByKey = k => k == "s_common_date_month_min" ? "{0}-{1}-{2} {3}:{4}" : k + "|{0}|{1}|{2}|{3}|{4}";
        c.ItemTextColor = g => 0x10 + (uint)g;
        var text = new List<string>(); c.StorageHistoryTextBuilt += text.Add;
        return (c, text);
    }

    static GuildStorageHistory Line(string from, string to, int item, int rank, int count, long time) =>
        new(new(0, 0, from), new(0, 0, to), new GuildStorageItem(item, rank, count, false, false, false, false), time);

    /// <summary>CreateStoragePage / UpdateStorageHistory: the Storage category makes the page, the others hide it, the history shows newest
    /// first and only the last 7 days, with the item in its grade colour.</summary>
    [Test]
    public void TheStorageHistoryListsTheLast7DaysNewestFirst()
    {
        var b = InGuild();
        long now = 1_700_000_000;
        b.ServerTime = () => now;
        var (c, text) = StoragePage(b);
        c.ServerTime = () => now;
        c.OpenCategoryPage("Storage");
        Assert.That(c.StoragePage, Is.Not.Null);
        Assert.That(Rt.ToBoolean(c.Host.Get(c.StoragePage, "visible")), Is.True);
        Assert.That(text, Is.Empty, "no history received yet: no text built");
        b.Apply(new GuildMessage((byte)0x70) { StorageHistory = new[] {
            Line("old", "x", 1, 1, 1, now - 7 * 86400 - 1),
            Line("a", "b", 5, 2, 3, now - 100),
            Line("c", "d", 6, 3, 4, now - 50) } });
        var t = Ms2.Net.MsTime.LocalTm(now - 50);
        string date = $"{t.Year}-{t.Month}-{t.Day} {t.Hour}:{t.Minute}";
        var lines = text.Single().Split('\n');
        Assert.That(lines.Length, Is.EqualTo(2), "the line older than 7 days is skipped");
        Assert.That(lines[0], Is.EqualTo($"s_guild_storage_history_gift|c|<FONT color='#13'>1:6</FONT>|4|d|{date}"));
        Assert.That(lines[1], Does.StartWith("s_guild_storage_history_gift|a|<FONT color='#12'>1:5</FONT>|3|b|"));
        c.OpenCategoryPage("Event");
        Assert.That(Rt.ToBoolean(c.Host.Get(c.StoragePage, "visible")), Is.False);
    }

    [Test]
    public void TheStorageHistoryDoesNothingWithoutThePage()
    {
        var b = InGuild();
        var (c, text) = StoragePage(b);
        b.Apply(new GuildMessage((byte)0x70) { StorageHistory = new[] { Line("a", "b", 5, 2, 3, b.ServerTime()) } });
        Assert.That(text, Is.Empty);
        Assert.That(c.StoragePage, Is.Null);
    }

    [Test]
    public void TheStorageCallbacksSendTheGiftAndAskForTheHistory()
    {
        var sent = new List<IOutPacket>(); var b = InGuild(); b.Send = sent.Add;
        b.Info.Members.Add(new GuildMember { Mode = 3, Grade = 1, Name = "Bo", CharId = 13, Level = 5, Triple = (0, 0, 0) });
        b.Apply(new GuildMessage((byte)0x6e) { StorageItem = new GuildStorageItem(9, 1, 5, false, false, false, false) });
        b.ItemSlotMax = _ => 99; b.ItemHasStringTag0xd7 = _ => false;
        var (c, _) = StoragePage(b);
        c.OpenCategoryPage("Storage");
        Assert.That(c.OnCallback("requestStorageHistory", new object[0]), Is.True);
        Assert.That(sent.Single(), Is.EqualTo(new GuildStorageHistoryRequest()));
        Assert.That(c.OnCallback("onGiftGuildItem", new object[] { "Bo", 9.0, 1.0, 2.0 }), Is.True);
        Assert.That(sent.Last(), Is.EqualTo(new GuildStorageGiftRequest(new GuildStorageItem(9, 1, 2, false, false, false, false), "Bo")));
        Assert.That(c.OnCallback("onGiftGuildItem", new object[] { "Bo", 9.0, 1.0, 50.0 }), Is.True);
        Assert.That(sent.Count, Is.EqualTo(2), "more than stored: nothing sent");
    }

    const string RaidTableXml = "<ms2><guildRaid dungeonRoomID=\"7\"/><guildRaid dungeonRoomID=\"3\"/></ms2>";

    static (GuildDialogController c, List<IReadOnlyList<object>> history) RaidPage(GuildBook b)
    {
        b.Raids = new Ms2.Formats.World.GuildRaidTable(System.Xml.Linq.XDocument.Parse(RaidTableXml));
        var c = Load(b);
        c.StringTableText = (t, id) => $"{t}:{id}";
        c.CommonByKey = k => k == "s_guild_raid_info_score" ? k + "|{0}|{1}" : k + "|{0}|{1}|{2}";
        var history = new List<IReadOnlyList<object>>(); c.RaidHistoryRowsBuilt += r => history.Add(r.ToList());
        return (c, history);
    }

    /// <summary>CreateRaidPage / UpdateRaidScore / FUN_141973220: the Raid category makes the page, the score text has the member's and the
    /// guild's totals, the history a row per raid of the table (ids ascending) saying whether the member cleared it.</summary>
    [Test]
    public void TheRaidPageShowsTheScoreAndOneHistoryRowPerRaid()
    {
        var b = InGuild();
        b.Info.Members.Add(new GuildMember { Mode = 3, Grade = 1, Name = "Bo", CharId = 13, Level = 5, Triple = (0, 0, 0) });
        b.Apply(new GuildMessage((byte)0x25) { Name = "Ma", RaidId = 7, Value = 3 });
        b.Apply(new GuildMessage((byte)0x25) { Name = "Bo", RaidId = 3, Value = 5 });
        var (c, history) = RaidPage(b);
        c.OpenCategoryPage("Raid");
        Assert.That(c.RaidPage, Is.Not.Null);
        Assert.That(Rt.ToBoolean(c.Host.Get(c.RaidPage, "visible")), Is.True);
        var score = c.Host.Get(c.Host.Child("m_scoreText", c.Host.Child("m_info", c.RaidPage)), "text");
        Assert.That(score, Is.EqualTo("s_guild_raid_info_score|3|8"));
        var rows = history.Single();
        Assert.That(rows.Count, Is.EqualTo(2));
        Assert.That((c.Host.Get(rows[0], "isComplete"), c.Host.Get(rows[0], "text")), Is.EqualTo((false, "s_guild_raid_history_desc|20:3|Ma|0")));
        Assert.That((c.Host.Get(rows[1], "isComplete"), c.Host.Get(rows[1], "text")), Is.EqualTo((true, "s_guild_raid_history_desc|20:7|Ma|3")));
        c.OpenCategoryPage("Event");
        Assert.That(Rt.ToBoolean(c.Host.Get(c.RaidPage, "visible")), Is.False);
    }

    [Test]
    public void TheRaidResetPacketUpdatesTheOpenPageAndNothingWithoutIt()
    {
        var b = InGuild();
        b.Apply(new GuildMessage((byte)0x25) { Name = "Ma", RaidId = 7, Value = 3 });
        var (c, history) = RaidPage(b);
        b.Apply(new GuildMessage((byte)0x3e));
        Assert.That(history, Is.Empty, "no page yet");
        b.Apply(new GuildMessage((byte)0x25) { Name = "Ma", RaidId = 7, Value = 3 });
        c.OpenCategoryPage("Raid");
        Assert.That(c.Host.Get(history.Single()[1], "isComplete"), Is.EqualTo(true));
        b.Apply(new GuildMessage((byte)0x3e));
        Assert.That(history.Count, Is.EqualTo(2));
        Assert.That(c.Host.Get(history[1][1], "isComplete"), Is.EqualTo(false));
        Assert.That(c.Host.Get(c.Host.Child("m_scoreText", c.Host.Child("m_info", c.RaidPage)), "text"), Is.EqualTo("s_guild_raid_info_score|0|0"));
    }

    // ---- the ChampionShip and battle history pages ----

    const string SeasonXml = "<ms2><Season seasonID=\"4\" seasonName=\"s\" eventStart=\"2022-1-1-0-0-0\" eventEnd=\"2099-1-1-0-0-0\" grade1=\"0\" grade2=\"100\" grade3=\"200\" grade4=\"300\" grade5=\"400\" grade6=\"500\" grade7=\"600\"/></ms2>";

    static (GuildDialogController c, GuildBook b, List<(int score, float rate, int grade, string text)> rating, List<IReadOnlyList<object>> battles, List<IReadOnlyList<object>> seasons) ChampionPage(bool open = true)
    {
        var b = InGuild();
        b.Seasons = new Ms2.Formats.World.SeasonDataTable(System.Xml.Linq.XDocument.Parse(SeasonXml), "GuildPvP");
        b.ServerTime = () => 4_000_000_000;
        b.ZoneOffset = () => 0;
        b.Common = id => $"c{id:x}|{{0}}|{{1}}|{{2}}";
        b.StringTableText = (t, id) => $"t{t:x}.{id}";
        var c = Load(b);
        c.Common = b.Common;
        c.StringTableText = b.StringTableText;
        c.ServerTime = () => 4_000_000_000;
        c.FeatureEnabled = id => id == 0x110;
        c.GuildChampionshipOpen = () => open;
        c.DateText = u => "d" + u;
        c.TargetGuildName = () => "Tg";
        var rating = new List<(int, float, int, string)>(); c.ChampionshipRatingBuilt += (a, r, g, t) => rating.Add((a, r, g, t));
        var battles = new List<IReadOnlyList<object>>(); c.PvpHistoryRowsBuilt += r => battles.Add(r.ToList());
        var seasons = new List<IReadOnlyList<object>>(); c.PvpSeasonRowsBuilt += r => seasons.Add(r.ToList());
        return (c, b, rating, battles, seasons);
    }

    /// <summary>CreateChampionshipPage / UpdateChampionshipInfo / UpdateChampionshipRating (uiguilddialog.cpp:1611, :2130, :1815): the page is made
    /// only while the championship is open; the season's name and period, the belonging guild, the grade of the score with the share to the next
    /// grade, the result and the member's participation fill the page.</summary>
    [Test]
    public void TheChampionShipPageShowsTheSeasonTheGradeTheShareAndTheResult()
    {
        var (c, b, rating, _, _) = ChampionPage();
        b.Info.Members[0].Fe0 = 6;
        b.Apply(new GuildMessage((byte)0x29) { Score = 250, Wins = 3, Losses = 2 });
        c.OpenCategoryPage("ChampionShip");
        Assert.That(c.ChampionshipPage, Is.Not.Null);
        Assert.That(Rt.ToBoolean(c.Host.Get(c.ChampionshipPage, "visible")), Is.True);
        Assert.That(c.Host.Get(c.Host.Child("m_titleText", c.ChampionshipPage), "text"), Is.EqualTo(b.GetSeason().Name));
        Assert.That(c.Host.Get(c.Host.Child("m_periodText", c.ChampionshipPage), "text"), Is.EqualTo($"d{b.GetSeason().Begin} ~ d{b.GetSeason().End}"));
        Assert.That(c.Host.Get(c.Host.Child("m_belongGuildText", c.ChampionshipPage), "text"), Is.EqualTo("c3fb|Tg|{1}|{2}"));
        var r = rating.Last();
        Assert.That((r.score, r.grade, r.rate, r.text), Is.EqualTo((250, 3, 0.5f, "c3ed|t2d.3|250|{2}")));
        Assert.That(c.Host.Get(c.Host.Child("m_seasonResult", c.ChampionshipPage), "text"), Is.EqualTo("c3ee|5|3|2"));
        Assert.That(c.Host.Get(c.Host.Child("m_participate", c.ChampionshipPage), "text"), Is.EqualTo("c3f5|6|{1}|{2}"));
        b.Apply(new GuildMessage((byte)0x29) { Score = 900, Wins = 3, Losses = 2 });
        Assert.That(rating.Last().rate, Is.EqualTo(1.0f), "the top grade is full");
        c.OpenCategoryPage("Event");
        Assert.That(Rt.ToBoolean(c.Host.Get(c.ChampionshipPage, "visible")), Is.False);
    }

    [Test]
    public void TheChampionShipPagesAreNotMadeWhileTheChampionshipIsNotOpen()
    {
        var (c, b, rating, _, _) = ChampionPage(open: false);
        c.OpenCategoryPage("ChampionShip");
        c.OpenCategoryPage("ChampionShipHistroy");
        Assert.That((c.ChampionshipPage, c.PvpHistoryPage), Is.EqualTo(((object)null, (object)null)));
        b.Apply(new GuildMessage((byte)0x29) { Score = 250 });
        Assert.That(rating, Is.Empty);
    }

    static GuildPvpHistory Battle(long first, long second, string rounds, long time, byte result = 0) => new()
    {
        FirstGuildId = first, SecondGuildId = second, FirstGuildName = "A", SecondGuildName = "B", Result = result, Time = time,
        RoundResults = rounds.Select(ch => (byte)ch).ToArray(),
        FirstMembers = new[] { "1,m1:2,m2", "", "bad", "", "" }, SecondMembers = new[] { "3,x", "", "", "", "" },
    };

    /// <summary>FillPvpHistoryRow (@0x141977b70, :3301): the local guild's battle on the left, another guild's mirrored; the time is days, hours,
    /// minutes since; no round fought is a "no fight" row, a loss when the result flag is 1.</summary>
    [Test]
    public void TheBattleRowsPutTheLocalGuildOnTheLeftAndMirrorTheOthers()
    {
        var (c, b, _, battles, _) = ChampionPage();
        b.Apply(new GuildMessage((byte)0x4a) { PvpHistory = new[] { Battle(7, 9, "WWLWL", 4_000_000_000 - 90000), Battle(8, 7, "WLLWW", 4_000_000_000 - 7300), Battle(7, 9, "\0\0\0\0\0", 4_000_000_000 - 30, 1) } });
        c.OpenCategoryPage("ChampionShipHistroy");
        battles.Clear();
        b.Apply(new GuildMessage((byte)0x4a) { PvpHistory = new GuildPvpHistory[0] });
        var rows = battles.Last();
        Assert.That(rows.Count, Is.EqualTo(3));
        object G(int i, string k) => c.Host.Get(rows[i], k);
        // newest first: the third battle was added last
        Assert.That((G(0, "isNofight"), G(0, "isWin"), G(0, "score"), G(0, "time")), Is.EqualTo((true, false, "c3eb|{0}|{1}|{2}", "c3d5|{0}|{1}|{2}")));
        Assert.That((G(1, "leftSn"), G(1, "rightSn"), G(1, "leftGuildName"), G(1, "isWin"), G(1, "score"), G(1, "time")),
            Is.EqualTo(("7", "8", "B", false, "c3e9|2|3|{2}", "c3d3|2|{1}|{2}")));
        Assert.That((G(1, "result1"), G(1, "result2"), G(1, "left1"), G(1, "right1")), Is.EqualTo((false, true, "x", "m1,m2")));
        Assert.That((G(2, "leftSn"), G(2, "isWin"), G(2, "score"), G(2, "result1"), G(2, "result3"), G(2, "left1"), G(2, "right1"), G(2, "left3"), G(2, "time")),
            Is.EqualTo(("7", true, "c3e9|3|2|{2}", true, false, "m1,m2", "x", "", "c3d2|1|{1}|{2}")));
    }

    /// <summary>FUN_141974640 / FillPvpSeasonHistoryRow (@0x141974640, @0x141978bb0): a row per finished season, newest first, and the summed result.</summary>
    [Test]
    public void TheSeasonRowsListTheFinishedSeasonsAndSumTheirResults()
    {
        var (c, b, _, _, seasons) = ChampionPage();
        c.OpenCategoryPage("ChampionShipHistroy");
        b.Apply(new GuildMessage((byte)0x4b) { PvpSeasonHistory = new[]
        {
            new GuildPvpSeasonHistory { SeasonName = "S1", Rank = 3, Grade = 5, Wins = 4, Losses = 1 },
            new GuildPvpSeasonHistory { SeasonName = "S2", Rank = 1, Grade = 6, Wins = 2, Losses = 2 },
        } });
        var rows = seasons.Last();
        Assert.That(rows.Select(r => (c.Host.Get(r, "seasonName"), c.Host.Get(r, "combatHistory"), c.Host.Get(r, "rank"), c.Host.Get(r, "grade"))),
            Is.EqualTo(new[] { ("S2", "c3ee|4|2|2", "c3ef|1|{1}|{2}", (object)6), ("S1", "c3ee|5|4|1", "c3ef|3|{1}|{2}", 5) }.Select(x => ((object)x.Item1, (object)x.Item2, (object)x.Item3, x.Item4))));
    }

    [Test]
    public void TheMemberMarksAreTheSecondPartOfEachIdAndMarkPairAndAnOddEntryIsLeftOut()
    {
        Assert.That(GuildDialogController.MemberMarks("1,a:2,b"), Is.EqualTo("a,b"));
        Assert.That(GuildDialogController.MemberMarks("1,a::bad:3,c,d:4,e"), Is.EqualTo("a,e"));
        Assert.That(GuildDialogController.MemberMarks(""), Is.EqualTo(""));
    }

    /// <summary>The exp and fund packets with the dialog open: UpdateGuildExp (@0x14196e0a0) sets the Info page's m_guildLevelGauge
    /// (m_guildLevelText and the gauge, 296 wide at a full level) and FUN_14196e2c0 the Info page's m_guildCommonPurseText and the
    /// skill page's m_guildFund / m_guildEventFund. A level change also redraws the skill page (FUN_14196e590).</summary>
    [Test]
    public void TheExpAndFundPacketsRedrawTheOpenDialog()
    {
        var b = InGuild();
        b.SkillTable = new Ms2.Formats.World.GuildSkillTable(System.Xml.Linq.XDocument.Parse(SkillBoxXml));
        b.Mesos = () => 0;
        b.Level = () => 50;
        b.Info.Skills.Add(new GuildSkillState(1, 1, 0));
        var c = Load(b, k => k.StartsWith('$') ? k[1..] + ":{0}" : k);   // a "$" key's text keeps the key and the first argument; other text is left as it is
        b.LevelByExp = c.LevelByExp;
        c.GroupText = n => "g" + n;
        c.EffectIcon = (_, _) => "icon.png";
        int built = 0;
        c.EffectText = (_, _, _) => { built++; return "n"; };
        c.OpenCategoryPage("Info");
        var gauge = c.Host.Child("m_guildLevelGauge", c.InfoPage);
        object Bar() => c.Host.Get(c.Host.Child("gauge", gauge), "width");
        string LevelText() => c.Host.Get(c.Host.Child("m_guildLevelText", gauge), "text")?.ToString();
        int l1 = c.LevelByExp(0);
        Assert.That(l1, Is.GreaterThan(0), "the data has guild levels");
        int lo = c.AccumExp(l1 - 1), hi = c.AccumExp(l1);
        string before = LevelText();
        Assert.That(before, Does.EndWith(":" + l1), "the open draws the level in the gauge text");
        b.Apply(new GuildMessage((byte) 0x32) { Value = lo + (hi - lo) / 2 });
        Assert.That(Rt.ToNumber(Bar()), Is.EqualTo(296.0 * ((hi - lo) / 2) / (hi - lo)).Within(0.5), "half way through the level");
        Assert.That(LevelText(), Is.EqualTo(before), "the same level");
        c.OpenCategoryPage("Skill");
        int afterOpen = built;
        int next = c.LevelByExp(hi) > l1 ? hi : hi + 1;          // the first exp of the next level
        Assert.That(c.LevelByExp(next), Is.GreaterThan(l1));
        b.Apply(new GuildMessage((byte) 0x32) { Value = next });
        Assert.That(LevelText(), Is.Not.EqualTo(before).And.EndWith(":" + c.LevelByExp(next)), "the next level");
        Assert.That(built, Is.GreaterThan(afterOpen), "a level change redraws the skill page");
        b.Apply(new GuildMessage((byte) 0x33) { Value = 777 });
        b.Apply(new GuildMessage((byte) 0x34) { Value = 55 });
        Assert.That(c.Host.Get(c.Host.Child("m_guildCommonPurseText", c.InfoPage), "htmlText")?.ToString(), Does.Contain("guild_info_fund:g777"), "the fund in the Info page text");
        Assert.That(c.Host.Get(c.Host.Child("m_guildFund", c.SkillPage), "text")?.ToString(), Is.EqualTo("g777"));
        if (c.Host.Child("m_guildEventFund", c.SkillPage) is { } eventFund && !Rt.IsNullish(eventFund))
            Assert.That(c.Host.Get(eventFund, "text")?.ToString(), Is.EqualTo("g55"));
    }

    /// <summary>The unread modes' dialog calls: OnPacketGuildRenamed (0x22) SetGuildName, FUN_1413c15b0 (0x1b) SetGuildMarkFromCode with
    /// FUN_141029590's path, OnPacketGradeUpdated (0x1d) RefreshMembersOfGrade (SetGradeInfo and the grade's rows), OnPacketMemberExpelled
    /// (0x14) the guild skill page, OnPacketMemberContribution (0x24) of the player UpdateAttendBackground. Closed, none of them runs.</summary>
    [Test]
    public void TheUnreadModesRedrawTheOpenDialog()
    {
        var b = InGuild();
        b.Info.Members.Add(new GuildMember { Mode = 3, Grade = 1, Name = "bo", CharId = 13, Level = 20, Triple = (0, 0, 0) });
        b.SkillTable = new Ms2.Formats.World.GuildSkillTable(System.Xml.Linq.XDocument.Parse(SkillBoxXml));
        b.Mesos = () => 0;
        b.Level = () => 50;
        b.Info.Skills.Add(new GuildSkillState(1, 1, 0));
        var c = Load(b);
        var marks = new List<(long, string)>();
        c.GuildMarkPath = (id, code) => { marks.Add((id, code)); return "mark/" + code + ".png"; };
        c.EffectIcon = (_, _) => "icon.png";
        int built = 0;
        c.EffectText = (_, _, _) => { built++; return "n"; };
        c.OpenCategoryPage("Info");
        string Text(string child, object page) => c.Host.Get(c.Host.Child(child, page), "text")?.ToString();

        b.Apply(new GuildMessage((byte) 0x22) { Name = "Renamed" });
        Assert.That(Text("m_guildNameText", c.InfoPage), Is.EqualTo("Renamed"), "setGuildName");

        b.Apply(new GuildMessage((byte) 0x1b) { Code = "mk", Changer = new StringIdText(false, 0, 0, Array.Empty<string>(), "bo") });
        Assert.That(marks, Is.EqualTo(new[] { (7L, "mk") }), "SetGuildMark(code, 1) with the guild id");

        c.OpenCategoryPage("Member");
        var rows = new List<string>();
        c.MemberRowDrawn += rows.Add;
        b.Apply(new GuildMessage((byte) 0x1d) { Grade = 1, GradeEntry = new GuildGrade(1, "Elder", 8), Changer = new StringIdText(false, 0, 0, Array.Empty<string>(), "Ma") });
        Assert.That(rows, Is.EqualTo(new[] { "bo" }), "only the grade's members are redrawn");

        c.OpenCategoryPage("Skill");
        int afterOpen = built;
        b.Apply(new GuildMessage((byte) 0x14) { By = "Ma", Name = "bo" });
        Assert.That(built, Is.GreaterThan(afterOpen), "the skill page is redrawn");

        var attend = c.Host.Child("m_attendBG");
        c.Host.SetVisible(attend, false);
        b.Apply(new GuildMessage((byte) 0x24) { Name = "Ma", Contribution = 0 });
        Assert.That(Rt.ToBoolean(c.Host.Get(attend, "visible")), Is.EqualTo(b.CanAttend).And.True, "UpdateAttendBackground after the own check-in time");

        c.Close();
        rows.Clear(); marks.Clear(); built = 0;
        b.Apply(new GuildMessage((byte) 0x1b) { Code = "m2" });
        b.Apply(new GuildMessage((byte) 0x1d) { Grade = 1, GradeEntry = new GuildGrade(1, "E2", 8) });
        b.Apply(new GuildMessage((byte) 0x21) { Name = "Ma" });
        Assert.That((marks.Count, rows.Count, built), Is.EqualTo((0, 0, 0)), "closed: nothing");
    }
}
