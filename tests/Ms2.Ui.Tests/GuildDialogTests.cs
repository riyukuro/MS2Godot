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
