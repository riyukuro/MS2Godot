using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Quest;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The npc duel bar (UINpcDuelHPBarDialog, uinpcduelhpbardialog.cpp) run by its movie: the Trigger 0x15 show and
/// hide, the countdown, both sides and the npc's hp cut into bars (needs MS2_DATA).</summary>
public class NpcDuelHpBarTests
{
    static PackSet packs;
    static MovieSource src;
    static GameTables tables;

    sealed class Rig
    {
        public NpcDuelHpBarController C;
        public UiHost H;
        public Runtime RT;
        public Player P;
        public uint Now = 10_000;
        public int Opens;
        public long NpcHp = 1000, NpcMax = 1000;
        public readonly List<bool> Upper = new();
    }

    static Rig New()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        if (packs == null)
        {
            packs = TestData.Packs(d);
            src = TestData.Movies(packs);
            tables = new GameTables(packs, "en");
        }
        var g = new Rig();
        g.C = new NpcDuelHpBarController
        {
            NowMs = () => g.Now,
            LocalPlayer = () => new NpcDuelHpBarController.Player("Tester", 100, "", 400, 800),
            FindNpc = id => id == 77 ? new NpcDuelHpBarController.Npc(21000003, 0, "", 37, g.NpcHp, g.NpcMax) : null,
            OpenRequested = () => g.Opens++,
        };
        return g;
    }

    static void Load(Rig g)
    {
        var (rt, p) = src.Open(g.C.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1920; p.StageH = 1080;
        g.RT = rt; g.P = p;
        g.H = new UiHost(p, g.C, new UiModel(tables));
        Assert.That(g.H.Load(), Is.True, g.C.Movie + " load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 5; i++) p.Tick();
    }

    static object Member(Rig g, string child, string member) => g.H.Get(g.H.FindNamed(child), member);

    [Test]
    public void AShowStartsTheDuelOpensTheDialogAndUnloadsTheUpperBarAndAHideUndoesIt()
    {
        var g = New();
        g.C.Apply(new TriggerDuelHpBar(true, 77, 125_000, 0), g.Upper.Add);
        Assert.That((g.C.ObjectId, g.C.BarCount, g.Opens), Is.EqualTo((77, 1, 1)), "a bar count of 0 counts as 1");
        Assert.That(g.Upper, Is.EqualTo(new[] { false }));
        g.C.Apply(new TriggerDuelHpBar(false, 0, 0, 0), g.Upper.Add);
        Assert.That(g.Upper, Is.EqualTo(new[] { false, true }));
        Assert.That(g.Opens, Is.EqualTo(1), "a hide opens nothing");
    }

    [Test]
    public void TheLoadFillsBothSidesAndTheTimeCountsDownToZero()
    {
        var g = New();
        g.C.StartDuelBar(77, 125_000, 4);
        Load(g);
        Assert.That(Member(g, "m_leftName", "text"), Is.EqualTo("Tester"));
        Assert.That(Member(g, "m_rightLevel", "text"), Is.EqualTo("37"));
        g.Now += 500;
        g.C.Update();
        Assert.That(g.C.ShownTime, Is.EqualTo("0204"), "124.5 s left: whole seconds");
        g.Now += 200_000;
        g.C.Update();
        Assert.That(g.C.ShownTime, Is.EqualTo("0000"), "past the end tick");
        for (int i = 0; i < 10; i++) g.P.Tick();
        Assert.That(g.RT.Errors, Is.Empty);
    }

    [Test]
    public void TheNpcHpIsCutIntoTheBarCountsEqualBars()
    {
        var g = New();
        g.C.StartDuelBar(77, 60_000, 4);
        Load(g);
        g.C.Update();
        Assert.That(g.C.RightBar, Is.EqualTo((250L, 250L, 4)), "full hp: the top bar is full (1000 / 250 + 1 = 5, held at 4)");
        Assert.That(Member(g, "m_rightBarCount", "visible"), Is.EqualTo(true));
        Assert.That(Member(g, "m_rightBarCount", "text"),
            Is.EqualTo(ClientFormat.Format(tables.Common("s_npc_duelhpbar_dialog_barCount") ?? "", 4)));
        Assert.That(Member(g, "m_rightHP_BG", "visible"), Is.EqualTo(true), "the movie shows the next bar's colour under it");
        g.NpcHp = 600;
        g.C.Update();
        Assert.That(g.C.RightBar, Is.EqualTo((100L, 250L, 3)));
        g.NpcHp = 100;
        g.C.Update();
        Assert.That(g.C.RightBar, Is.EqualTo((100L, 250L, 1)));
        Assert.That(Member(g, "m_rightBarCount", "visible"), Is.EqualTo(false), "one bar: no count");
        Assert.That(Member(g, "m_rightHP_BG", "visible"), Is.EqualTo(false));
        g.NpcHp = 0;
        g.C.Update();
        Assert.That(g.C.RightBar, Is.EqualTo((0L, 250L, 1)));
        // a max hp of 0 sends nothing
        g.NpcHp = 5; g.NpcMax = 0;
        g.C.Update();
        Assert.That(g.C.RightBar, Is.EqualTo((0L, 250L, 1)));
        Assert.That(g.RT.Errors, Is.Empty);
    }

    [Test]
    public void AnUnknownNpcLeavesTheRightSideAndTheCloseForgetsTheDuel()
    {
        var g = New();
        g.C.StartDuelBar(5, 60_000, 3);
        Load(g);
        g.C.Update();
        Assert.That(g.C.RightBar, Is.EqualTo((0L, 0L, 0)), "no npc of that id: setHpBarRight is not called");
        Assert.That(Member(g, "m_leftName", "text"), Is.EqualTo("Tester"), "the left side is filled");
        g.C.Close();
        for (int i = 0; i < 120 && !g.C.IsClosed; i++) g.P.Tick();     // the close tween ends with OnEndClose
        Assert.That(g.C.IsClosed, Is.True);
        Assert.That((g.C.ObjectId, g.C.BarCount), Is.EqualTo((0, 1)), "vfunc_67: object 0, one bar");
    }
}
