using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Quest;
using Ms2.Ui.Hud;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// Field PvP: the PvP info feed (CPvPInfo, PC::vfunc_116), the name tag colour (PC::vfunc_53), the kill log dialog
/// UIPvPKillLogDialog (0x4ea0, by its own movie) and the pk zone notice of UISceneGamePlay::vfunc_58.
/// </summary>
public class PvpFieldTests
{
    static PackSet packs;
    static MovieSource src;

    static PackSet Packs()
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = TestData.Packs(d);
            src = TestData.Movies(packs);
        }
        return packs;
    }

    [Test]
    public void PvpPointIsTheTeamWhenSet()
    {
        Assert.That((new PvpInfo(0, 3, 1).HasPoint, new PvpInfo(0, 3, 1).PcTeam), Is.EqualTo((false, 1)), "no point: team 1");
        Assert.That((new PvpInfo(101, 0, 2).HasPoint, new PvpInfo(101, 0, 2).PcTeam), Is.EqualTo((true, 101)));
        Assert.That(new PvpInfo(-5, 0, 0).HasPoint, Is.False, "HasPvPPoint is point > 0");
    }

    [Test]
    public void PvpPointsDecideHostilityOffPkFields()
    {
        static HitCandidate Pc(int id, PvpInfo p) => new() { Id = id, Kind = HitKind.Pc, Team = p.PcTeam, PvPPoint = p.HasPoint };
        var none = new PvpInfo(0, 0, 0);
        Assert.That(SkillHit.PcHostile(Pc(1, none), Pc(2, none), false), Is.False, "two players without a point");
        Assert.That(SkillHit.PcHostile(Pc(1, new PvpInfo(101, 0, 1)), Pc(2, new PvpInfo(102, 0, 1)), false), Is.True, "arena points differ");
        Assert.That(SkillHit.PcHostile(Pc(1, new PvpInfo(101, 0, 1)), Pc(2, new PvpInfo(101, 0, 1)), false), Is.False, "same point, same team");
        Assert.That(SkillHit.PcHostile(Pc(1, new PvpInfo(101, 0, 1)), Pc(2, none), false), Is.False, "point against none");
    }

    [Test]
    public void TagColourOrderIsSelfHostilePartyPc()
    {
        Assert.That(PcTagColors.Of(true, true, true), Is.EqualTo(PcTagColor.MyPc));
        Assert.That(PcTagColors.Of(false, true, true), Is.EqualTo(PcTagColor.Hostile));
        Assert.That(PcTagColors.Of(false, false, true), Is.EqualTo(PcTagColor.Party));
        Assert.That(PcTagColors.Of(false, false, false), Is.EqualTo(PcTagColor.Pc));
    }

    [Test]
    public void HostileTagColourAndPvpTextsAreInTheData()
    {
        var c = FeatureElements.Constants(Packs());
        Assert.That(c.String("NameTagColorPCHostile"), Is.Not.Null.And.Not.Empty);
        Assert.That(c.Color("NameTagColorPCHostile", (-1f, -1f, -1f)), Is.Not.EqualTo((-1f, -1f, -1f)), "a colour value");
        var tables = new GameTables(Packs(), "en");
        foreach (int id in new[] { 0x96d, 0x96e, 0x96f, 0x970, 0x972, 0x973 })
            Assert.That(tables.Common(id), Is.Not.Empty, $"common string 0x{id:x} ({CommonStringIds.Key(id)})");
    }

    [Test]
    public void PkZoneNoticeOnEnterAndLeaveOnly()
    {
        var n = new PkZoneNotice();
        Assert.That(n.OnFadeInComplete(true, 1, 0), Is.Null, "shadow safe field first: nothing");
        Assert.That(n.OnFadeInComplete(true, 1, 1), Is.EqualTo((0x96d, 0x96e)), "into a pk field");
        Assert.That(n.InPkZone, Is.True);
        Assert.That(n.OnFadeInComplete(true, 1, 2), Is.Null, "pk to pk: nothing");
        Assert.That(n.OnFadeInComplete(true, 1, 0), Is.EqualTo((0x96f, 0x970)), "back to a safe field");
        Assert.That(n.OnFadeInComplete(true, 1, 0), Is.Null);
        n.OnFadeInComplete(true, 1, 1);
        Assert.That(n.OnFadeInComplete(true, 0, 0), Is.Null, "off the shadow continent: no notice");
        Assert.That(n.InPkZone, Is.False, "and the flag clears");
        Assert.That(n.OnFadeInComplete(true, 0, 1), Is.Null, "a pk field off the shadow continent");
        n.OnFadeInComplete(true, 1, 1);
        Assert.That(n.OnFadeInComplete(false, 0, 0), Is.Null, "no field data");
        Assert.That(n.InPkZone, Is.False);
    }

    sealed class Sinks
    {
        public readonly List<string> Sounds = new();
        public readonly List<(string, int)> Lines = new();
    }

    static (UiHost Host, PvPKillLogController C, Sinks S) Load(string me, Action<PvPKillLogController> before = null)
    {
        var model = new UiModel(new GameTables(Packs(), "en")) { HasLocalPlayer = true };
        var s = new Sinks();
        var c = new PvPKillLogController
        {
            MyName = () => me,
            Sound = s.Sounds.Add,
            ChatLine = (t, k) => s.Lines.Add((t, k)),
            Common = id => $"<{id:x}:{{0}}>",
        };
        before?.Invoke(c);
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        p.Translate = k => k;
        var h = new UiHost(p, c, model);
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 30; i++) p.Tick();
        foreach (var e in rt.Errors) TestContext.WriteLine("  error: " + e);
        return (h, c, s);
    }

    static object Member(UiHost h, int i) => h.Child("m_member" + i);
    static string Field(UiHost h, object member, string name) => Rt.ToString(h.Get(h.Get(member, name), "text"));

    [Test]
    public void OwnKillShowsTheRowLogsTheTargetAndPlaysBothSounds()
    {
        Packs();
        var (h, c, s) = Load("Me");
        c.AddKill("Me", "Foe");
        var m1 = Member(h, 1);
        Assert.That((Field(h, m1, "m_attackerName"), Field(h, m1, "m_targetName")), Is.EqualTo(("Me", "Foe")));
        Assert.That(Rt.ToNumber(h.Get(h.Get(m1, "m_attackerName"), "textColor")), Is.EqualTo(16764699), "own name highlighted");
        Assert.That(Rt.ToBoolean(h.Get(Member(h, 2), "visible")), Is.False);
        Assert.That(s.Sounds, Is.EqualTo(new[] { "BattleField_Kill", "System_KillPlayer" }));
        Assert.That(s.Lines, Is.EqualTo(new[] { (ClientFormat.Format("<973:{0}>", "Foe"), 0x14) }));
    }

    [Test]
    public void OwnDeathLogsTheAttackerAndOthersKillsAreSilent()
    {
        Packs();
        var (h, c, s) = Load("Me");
        c.AddKill("Foe", "Me");
        Assert.That(s.Sounds, Is.EqualTo(new[] { "System_KillPlayer" }));
        Assert.That(s.Lines, Is.EqualTo(new[] { (ClientFormat.Format("<972:{0}>", "Foe"), 0x14) }));
        s.Sounds.Clear(); s.Lines.Clear();
        c.AddKill("A", "B");
        Assert.That((s.Sounds.Count, s.Lines.Count), Is.EqualTo((0, 0)));
        Assert.That(Field(h, Member(h, 1), "m_attackerName"), Is.EqualTo("A"), "the newest kill is the top row");
        Assert.That(Field(h, Member(h, 2), "m_attackerName"), Is.EqualTo("Foe"));
    }

    [Test]
    public void AKillBeforeTheMovieWaitsForLoadComplete()
    {
        Packs();
        var (h, c, s) = Load("Me", before: k => k.AddKill("Me", "Foe"));
        Assert.That(c.Pending, Is.EqualTo(("", "")), "cleared once shown");
        Assert.That(Field(h, Member(h, 1), "m_targetName"), Is.EqualTo("Foe"));
        Assert.That(s.Sounds, Is.EqualTo(new[] { "BattleField_Kill", "System_KillPlayer" }));
    }

    [Test]
    public void NoLocalPlayerShowsNothing()
    {
        Packs();
        var (h, c, s) = Load(null);
        c.AddKill("Me", "Foe");
        Assert.That((s.Sounds.Count, s.Lines.Count), Is.EqualTo((0, 0)));
        Assert.That(Rt.ToBoolean(h.Get(Member(h, 1), "visible")), Is.False, "no addKill");
    }

    [Test]
    public void KillLogIsNotClosableAndHasNoTween()
    {
        var c = new PvPKillLogController();
        Assert.That((c.NotClosable, PvPKillLogController.UiId, c.TypeName), Is.EqualTo((true, 0x4ea0, "PvPKillLogDialog")));
    }
}
