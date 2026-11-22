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

    static GuildDialogController Load(GuildBook book)
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        packs ??= new PackSet(d);
        src ??= new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
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
        var h = new UiHost(p, c, new UiModel(t));
        Assert.That(h.Load(), Is.True, c.Movie + " load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 5; i++) p.Tick();
        return c;
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
        Assert.That(c.Host.Get(c.Host.Child("m_guildEventFund", c.SkillPage), "text")?.ToString(), Is.EqualTo("g678"));
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
}
