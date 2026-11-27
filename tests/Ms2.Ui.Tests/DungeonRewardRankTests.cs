using System.Xml.Linq;
using Maple2.PacketLib.Tools;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The mission rank part of the dungeon result window (UIDungeonRewardDialog::FUN_140e24060, CreateMissionLabels, FUN_140e24c50;
/// uidungeonrewarddialog.cpp:317, :424, :492, :738) on its movie. Needs MS2_DATA; no test asserts a language-specific string.
/// </summary>
public class DungeonRewardRankTests
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

    static DungeonMissionTable Missions() => new(XElement.Parse(
        "<ms2><DungeonMission id='1' type='PlayTime' maxScore='30'/><DungeonMission id='2' type='DeathCount' maxScore='20' applyCount='2'/></ms2>"), null);

    static DungeonRoomTable Rooms() => new(f => f switch
    {
        "DungeonConfig.xml" => XElement.Parse("<ms2><DungeonConfig><MissionRank><group id='3' maxScore='50'><rank score='10'/><rank score='20'/><rank score='30'/></group></MissionRank></DungeonConfig></ms2>"),
        "DungeonRoom.xml" => XElement.Parse("<ms2><dungeonRoom dungeonRoomID='21001001' groupType='3'/></ms2>"),
        _ => null,
    }, null, null, null);

    static ByteReader R(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); }

    /// <summary>A stage whose missions are 1 (PlayTime, max 30) and 2 (DeathCount, max 20) with the scores.</summary>
    static RoomStageDungeonCli StageWith(short s1, short s2, bool missions = true)
    {
        var s = new RoomStageDungeonCli(21001001, 0, true, Rooms(), Missions());
        if (missions)
            s.OnPacket(R(w =>
            {
                w.WriteByte(0); w.WriteInt(2);
                w.WriteInt(1); w.WriteShort(s1); w.WriteLong(1); w.WriteLong(2);
                w.WriteInt(2); w.WriteShort(s2); w.WriteShort(0);
            }));
        return s;
    }

    static DungeonRewardPacket Result(int best, int run)
    {
        var w = new ByteWriter();
        w.WriteByte(0); w.WriteBool(true); w.WriteInt(0); w.WriteBool(false); w.WriteInt(60); w.WriteInt(best); w.WriteInt(run);
        w.WriteByte(0); w.WriteByte(0); w.WriteByte(0); w.WriteByte(0);
        for (int i = 0; i < 2; i++) { w.WriteInt(0); w.WriteInt(0); }
        return DungeonRewardPacket.Read(new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()));
    }

    static (DungeonRewardDialogController c, UiHost h) Open(RoomStageDungeonCli stage, int best = 10, int run = 20)
    {
        var t = Tables();
        var model = new UiModel(t);
        model.Player.Name = "Tester"; model.Player.SubJob = 100;
        var c = new DungeonRewardDialogController { GetStage = () => stage };
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000, c.Libraries);
        p.StageW = 1920; p.StageH = 1080;
        var h = new UiHost(p, c, model);
        c.OnPacket(Result(best, run));
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors) + " missing: " + string.Join("; ", rt.Missing.Keys));
        for (int i = 0; i < 5; i++) p.Tick();
        return (c, h);
    }

    static string Text(UiHost h, object clip)
    {
        var f = h.Has(clip, "htmlText") ? clip : h.Child("textField", clip);
        return Rt.ToString(h.Get(f, "text"));
    }

    static double Num(UiHost h, object o, string name) => Rt.ToNumber(h.Get(o, name));

    [Test]
    public void AStageWithAMaximumScoreShowsTheRankPartAtTheThreeClipsPlaces()
    {
        var (_, h) = Open(StageWith(5, 7));
        var rank = h.Child("m_rankAni");
        Assert.That(Rt.ToBoolean(h.Get(rank, "visible")), Is.True);
        Assert.That((Num(h, h.Child("m_rewardAvatar"), "x"), Num(h, h.Child("m_rewardAni"), "x"), Num(h, rank, "x")), Is.EqualTo((484.0, 896.0, 1516.0)));
        Assert.That(Num(h, h.Child("m_rewardAvatar"), "y"), Is.EqualTo(242.0));
    }

    [Test]
    public void WithoutAStageOrWithAZeroMaximumTheRankPartIsHidden()
    {
        foreach (var stage in new[] { null, StageWith(0, 0, missions: false) })
        {
            var (_, h) = Open(stage);
            Assert.That(Rt.ToBoolean(h.Get(h.Child("m_rankAni"), "visible")), Is.False);
            Assert.That((Num(h, h.Child("m_rewardAvatar"), "x"), Num(h, h.Child("m_rewardAni"), "x")), Is.EqualTo((714.0, 1126.0)));
        }
    }

    [Test]
    public void TheGaugeMaskIsTheScoreShareOf294AndTheTotalReadsCurOverMax()
    {
        var (_, h) = Open(StageWith(5, 7));                          // 12 of 50
        var info = h.Child("m_rankInfo", h.Child("m_rankAni"));
        var gauge = h.Child("m_rankGauge", info);
        var mask = h.Child("m_mask", gauge);
        Assert.That(Num(h, mask, "width"), Is.EqualTo((int)(294f * (12f / 50f))).Within(0.5));
        var eff = h.Child("m_eff", gauge);
        Assert.That(Num(h, eff, "x"), Is.EqualTo((int)(294f * (12f / 50f)) + Num(h, mask, "x") - 10).Within(0.5));
        Assert.That(Text(h, h.Child("m_missionScoreTotal", info)), Is.EqualTo("12/50"));
    }

    [Test]
    public void AScoreOverTheMaximumFillsTheGaugeToItsFullWidth()
    {
        var (_, h) = Open(StageWith(60, 7));                          // 67 of 50: the ratio clamps to 1
        var mask = h.Child("m_mask", h.Child("m_rankGauge", h.Child("m_rankInfo", h.Child("m_rankAni"))));
        Assert.That(Num(h, mask, "width"), Is.EqualTo(294.0).Within(0.5));
    }

    [Test]
    public void OneLabelPerMissionAtTheClientsRowsEachWithItsScoreOverMax()
    {
        var (_, h) = Open(StageWith(5, 7));
        var info = h.Child("m_rankInfo", h.Child("m_rankAni"));
        string[] expect = { "5/30", "7/20" };
        for (int i = 0; i < 2; i++)
        {
            var label = FindLabel(h, info, i);
            Assert.That(label, Is.Not.Null, "label " + i);
            Assert.That(Num(h, label, "x"), Is.EqualTo(0.0));
            Assert.That(Num(h, label, "y"), Is.EqualTo(i * 0x24 + 0x115));
            Assert.That(Text(h, h.Child("m_missionScore", label)), Is.EqualTo(expect[i]));
            Assert.That(h.Child("m_missionName", label), Is.Not.Null);
        }
    }

    /// <summary>The i-th label added to the rank clip: the children of the class whose y is the row's.</summary>
    static object FindLabel(UiHost h, object info, int i)
    {
        int n = (int)Num(h, info, "numChildren");
        double y = i * 0x24 + 0x115;
        for (int k = 0; k < n; k++)
        {
            var child = h.Invoke(info, "getChildAt", k);
            if (child != null && h.Child("m_missionName", child) != null && Math.Abs(Num(h, child, "y") - y) < 0.5) return child;
        }
        return null;
    }

    [Test]
    public void TheNewRecordEffectShowsOnlyWhenTheBestScoreIsBelowTheRun()
    {
        foreach (var (best, run, shown) in new[] { (10, 20, true), (20, 20, false), (30, 20, false) })
        {
            var (_, h) = Open(StageWith(5, 7), best, run);
            var movie = h.Child("m_rankMovie", h.Child("m_rankInfo", h.Child("m_rankAni")));
            var rec = h.Child("m_effNewRecord", movie);
            if (rec == null) Assert.Ignore("the movie has no m_effNewRecord");
            Assert.That(Rt.ToBoolean(h.Get(rec, "visible")), Is.EqualTo(shown), $"best {best} run {run}");
        }
    }

    [Test]
    public void TheRankEffectsFollowTheRanksThreeAndFour()
    {
        foreach (short s1 in new short[] { 0, 5, 12, 30 })
        {
            var stage = StageWith(s1, (short)(s1 == 30 ? 20 : 0));
            var (_, h) = Open(stage);
            var movie = h.Child("m_rankMovie", h.Child("m_rankInfo", h.Child("m_rankAni")));
            int rank = stage.Rank;
            var s1c = h.Child("m_RankSEffect1", movie); var s2c = h.Child("m_RankSEffect2", movie); var sa = h.Child("m_rankSAEffect", movie);
            if (s1c != null && s2c != null)
            {
                Assert.That(Rt.ToBoolean(h.Get(s1c, "visible")), Is.EqualTo(rank >= 4), "S1 at rank " + rank);
                Assert.That(Rt.ToBoolean(h.Get(s2c, "visible")), Is.EqualTo(rank >= 4), "S2 at rank " + rank);
            }
            if (sa != null) Assert.That(Rt.ToBoolean(h.Get(sa, "visible")), Is.EqualTo(rank >= 3), "SA at rank " + rank);
        }
    }

    [Test]
    public void TheRankNameIsTheFrameLabelOfTheRank()
    {
        Assert.That(DungeonRewardDialogController.RankName(3), Is.EqualTo("rank3"));
        Assert.That(DungeonRewardDialogController.RankName(0), Is.EqualTo("rank0"));
        Assert.That(DungeonRewardDialogController.RankName(-1), Is.EqualTo("rank_unknown"));
    }

    [Test]
    public void TheRankClipsGoToTheFrameOfTheRanksName()
    {
        foreach (var (s1, s2) in new (short, short)[] { (0, 0), (12, 0), (25, 0), (30, 20) })
        {
            var stage = StageWith(s1, s2);
            var (_, h) = Open(stage);
            var movie = h.Child("m_rankMovie", h.Child("m_rankInfo", h.Child("m_rankAni")));
            string want = DungeonRewardDialogController.RankName(stage.Rank);
            foreach (var name in new[] { "m_rank", "m_rankEffect" })
            {
                var clip = h.Child(name, movie);
                Assert.That(clip, Is.Not.Null, name);
                Assert.That(Rt.ToString(h.Get(clip, "currentLabel")), Is.EqualTo(want), $"{name} at rank {stage.Rank}");
            }
        }
    }
}
