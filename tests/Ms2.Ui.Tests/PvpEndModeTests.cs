using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Net.Packets;
using Ms2.Quest;
using Ms2.Ui.Hud;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The Pvp modes the field PvP zone ends with: 'e' (the own win / lose pose, StatePvPWinLose through MyPC::vfunc_59),
/// 'f' (CPVPManager::Clear) and the field ranking 0x67-0x6a (Unk_PvpTimer, unit_141017950_pvprankinglist.cpp).
/// </summary>
public class PvpEndModeTests
{
    const int Self = 9;
    const double Happy = 2.0, HandKiss = 3.0, Failure = 1.5;

    // pcactiondata's MODEPVP_WIN (0x41) and MODEPVP_DEAD (0x40), the same in the KMS and GMS2 tables
    static readonly string[] Win = { "Emotion_Happy_A", "Emotion_HandKiss_A", "Emotion_Provoke_A" };
    static readonly string[] Dead = { "Emotion_Failure_A", "Emotion_Disappoint_A" };

    static (MyCharacter me, OwnStateExit exit) Pc(bool missingWinSequence = false)
    {
        var me = new MyCharacter();
        var u = new FieldAddUser { ObjectId = Self, User = new UserBlock { Name = "a" } };
        u.Stats[0].Health = 500; u.Stats[2].Health = 500;
        me.Apply(u);
        var exit = new OwnStateExit
        {
            ActionNameAt = (type, i) =>
            {
                var list = type == OwnStateExit.PvpWinAction ? Win : type == OwnStateExit.PvpLoseAction ? Dead : null;
                if (list == null || i < 0 || i >= list.Length) return null;
                return missingWinSequence && type == OwnStateExit.PvpWinAction ? null : list[i];
            },
            SequenceLength = n => n switch { "Emotion_Happy_A" => Happy, "Emotion_HandKiss_A" => HandKiss, "Emotion_Failure_A" => Failure, _ => null },
        };
        return (me, exit);
    }

    /// <summary>LiveSession.EnterOwnPvpWinLose: the state through PC::vfunc_124 and ChangeState, then the pose.</summary>
    static bool PvpE(MyCharacter me, OwnStateExit exit, bool win, int team)
    {
        if (me.State == 0x28) return false;
        if (!me.EnterOwnState(0x28)) return false;
        return exit.EnterPvpWinLose(win ? OwnStateExit.PvpWinSub : OwnStateExit.PvpLoseSub, team);
    }

    static void Run(MyCharacter me, OwnStateExit exit, double seconds)
    {
        for (double t = 0; t < seconds - 1e-9; t += 0.01)
        {
            int st = exit.State;
            if (exit.Advance(System.Math.Min(0.01, seconds - t), me.State)) me.LeaveServerState(st);
        }
    }

    [Test]
    public void WinPlaysTheTeamsWinEntryOnceAndItsEndEntersIdle()
    {
        var (me, exit) = Pc();
        Assert.That(PvpE(me, exit, win: true, team: 1), Is.True);
        Assert.That((me.State, exit.Played, exit.PlayedLoops, exit.HoldsBody), Is.EqualTo((0x28, "Emotion_HandKiss_A", false, true)),
            "MODEPVP_WIN at the own team's index, once, the move input held");
        Run(me, exit, HandKiss - 0.02);
        Assert.That(me.State, Is.EqualTo(0x28), "kept until the sequence ends");
        Run(me, exit, 0.03);
        Assert.That((me.State, exit.State, exit.Played), Is.EqualTo((0, 0, (string)null)), "EndOfSequence: vfunc_59(1)");
    }

    [Test]
    public void LosePlaysTheDeadEntryOfTeamZero()
    {
        var (me, exit) = Pc();
        PvpE(me, exit, win: false, team: 0);
        Assert.That((exit.Played, exit.Left), Is.EqualTo(("Emotion_Failure_A", Failure)));
        Run(me, exit, Failure + 0.01);
        Assert.That(me.State, Is.EqualTo(0));
    }

    [Test]
    public void ARunningPoseOnlyTakesTheSub()
    {
        var (me, exit) = Pc();
        PvpE(me, exit, win: true, team: 0);
        int plays = exit.Plays;
        Run(me, exit, 1.0);
        Assert.That(PvpE(me, exit, win: false, team: 0), Is.False, "State::vfunc_25: the running state is kept");
        Assert.That((exit.Played, exit.Plays, exit.Left), Is.EqualTo(("Emotion_Happy_A", plays, Happy - 1.0)).Within(1e-6));
    }

    [Test]
    public void ATeamPastTheListPlaysNothingAndNeverLeaves()
    {
        var (me, exit) = Pc();
        PvpE(me, exit, win: false, team: 5);
        Assert.That((me.State, exit.Played, exit.Left), Is.EqualTo((0x28, (string)null, -1.0)));
        Run(me, exit, 30);
        Assert.That(me.State, Is.EqualTo(0x28), "no sequence ends, so EndOfSequence never enters StateIdle");
        var (me2, exit2) = Pc(missingWinSequence: true);
        PvpE(me2, exit2, win: true, team: 0);
        Run(me2, exit2, 30);
        Assert.That((me2.State, exit2.Played), Is.EqualTo((0x28, (string)null)), "a sequence the body lacks: the same");
    }

    [Test]
    public void TheDeadPcRefusesThePose()
    {
        var (me, exit) = Pc();
        me.Stats2[PlayerStat.Health] = 0;
        Assert.That(PvpE(me, exit, win: true, team: 0), Is.False, "PC::vfunc_124");
        Assert.That((me.State, exit.State), Is.EqualTo((0, 0)));
    }

    [Test]
    public void TheServersPvpStateStillPlaysNothing()
    {
        var (me, exit) = Pc();
        me.StateEntered += st => { if (exit.Enter(st, Self)) me.LeaveServerState(st); };
        me.ApplyUserState(Self, 0x28);
        Assert.That((me.State, exit.State, exit.Played, exit.Left), Is.EqualTo((0x28, 0x28, (string)null, -1.0)), "sub 0");
        Assert.That(PvpE(me, exit, win: true, team: 0), Is.False, "a running StatePvPWinLose takes only the sub");
        Assert.That(exit.Played, Is.Null);
    }

    // ---- the ranking ---------------------------------------------------------------------------------------------

    static PvpRankingList Ranking(int? mode, params (long Id, string Name)[] inField)
    {
        var field = new System.Collections.Generic.Dictionary<long, string>();
        foreach (var (id, name) in inField) field[id] = name;
        return new PvpRankingList
        {
            FieldPcName = id => field.TryGetValue(id, out var n) ? n : null,
            OwnMode = () => mode,
        };
    }

    [Test]
    public void FfaRanksByScoreThenKillsThenFewerDeathsThenDamage()
    {
        var r = Ranking(2, (1, "a"), (2, "b"), (3, "c"));
        Assert.That(r.SetScore(1, 0, 0, 0, 0), Is.EqualTo((0, 1)));
        r.SetScore(2, 0, 0, 0, 0);
        r.SetScore(3, 0, 0, 0, 0);
        Assert.That((r.RankOf(3), r.RankOf(2), r.RankOf(1)), Is.EqualTo((1, 2, 3)), "all equal: the higher character id first");
        Assert.That(r.SetScore(1, 100, 1, 0, 0), Is.EqualTo((3, 1)), "a kill moves the row up; from and to for the result dialog");
        r.SetScore(2, -50, 0, 1, 0);
        Assert.That((r.At(1).Name, r.At(2).Name, r.At(3).Name), Is.EqualTo(("a", "c", "b")));
        r.SetDamage(3, 500);
        r.SetScore(2, 0, 0, 0, 0);
        Assert.That((r.RankOf(3), r.RankOf(2)), Is.EqualTo((2, 3)), "equal score, kills and deaths: the damage decides");
    }

    [Test]
    public void FfaPutsPlayersNotInTheFieldLast()
    {
        var field = new System.Collections.Generic.Dictionary<long, string> { [1] = "a", [2] = "b" };
        var r = new PvpRankingList { FieldPcName = id => field.TryGetValue(id, out var n) ? n : null, OwnMode = () => 2 };
        r.SetScore(1, 300, 3, 0, 0);
        r.SetScore(2, 0, 0, 0, 0);
        field.Remove(1);
        r.SetScore(2, 0, 0, 1, 0);
        Assert.That((r.RankOf(2), r.RankOf(1), r.At(2).Online, r.At(2).Name), Is.EqualTo((1, 2, false, "a")),
            "the online flags are refreshed by a score row; the name stays the one taken at the row's start");
    }

    [Test]
    public void ModeFiveRanksByScoreKillsDeathsThenTheLowerFourthValue()
    {
        var r = Ranking(5, (1, "a"));
        r.SetScore(1, 10, 1, 0, 7);
        r.SetScore(2, 10, 1, 0, 3);
        Assert.That((r.RankOf(2), r.RankOf(1), r.At(1).Name), Is.EqualTo((1, 2, PvpRankingList.UnknownName)),
            "online does not count in mode 5; a character with no PC here is Unknown");
    }

    [Test]
    public void OtherModesKeepTheArrivalOrderAndNoLocalPcLeavesNoRanks()
    {
        var r = Ranking(0);
        r.SetScore(1, 0, 0, 0, 0);
        r.SetScore(2, 100, 1, 0, 0);
        Assert.That((r.RankOf(1), r.RankOf(2)), Is.EqualTo((1, 2)));
        var none = Ranking(null);
        Assert.That(none.SetScore(1, 0, 0, 0, 0), Is.EqualTo((0, 0)), "FUN_141018010 returns before the rank map");
        none.SetScore(1, 100, 1, 0, 0);
        Assert.That(none.Count, Is.EqualTo(2), "an unranked character gets a second row, as in the client");
    }

    [Test]
    public void DamageOfANewCharacterStartsAZeroRow()
    {
        var r = Ranking(2, (4, "d"));
        Assert.That(r.SetDamage(4, 1234), Is.EqualTo((0, 1)));
        var row = r.At(1);
        Assert.That((row.Name, row.Score, row.Kill, row.Death, row.Damage, row.Online), Is.EqualTo(("d", 0, 0, 0, 1234UL, false)),
            "the damage path does not refresh the online flag");
        r.SetScore(4, 0, 0, 0, 0);
        Assert.That(r.At(1).Online, Is.True);
    }

    [Test]
    public void ClearEmptiesTheRanking()
    {
        var r = Ranking(2, (1, "a"));
        r.SetScore(1, 100, 1, 0, 0);
        r.Clear();
        Assert.That((r.Count, r.RankOf(1), r.At(1)), Is.EqualTo((0, 0, (PvpRankingList.Row)null)));
        Assert.That(r.SetScore(1, 0, 0, 0, 0), Is.EqualTo((0, 1)), "a new row after the clear");
    }

    [Test]
    public void LastRankSetsTheFirstRowAgain()
    {
        var field = new System.Collections.Generic.Dictionary<long, string> { [1] = "a", [2] = "b" };
        var r = new PvpRankingList { FieldPcName = id => field.TryGetValue(id, out var n) ? n : null, OwnMode = () => 2 };
        r.SetScore(1, 100, 1, 0, 0);
        r.SetScore(2, 0, 0, 0, 0);
        field.Remove(1);
        r.RefreshFirst();
        Assert.That((r.RankOf(2), r.RankOf(1)), Is.EqualTo((1, 2)), "the refresh re-reads the online flags and sorts");
    }

    // ---- data -----------------------------------------------------------------------------------------------------

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void TheEndPosesAndTheStreakNoticesAreInTheData(string env)
    {
        var d = System.Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(d) || !System.IO.Directory.Exists(d)) Assert.Ignore($"{env} is not set");
        var packs = TestData.Packs(d);
        var x = PackXml.Parse(packs.Read("Xml", "table/pcactiondata.xml"));
        string Names(string type)
        {
            foreach (var v in x.Descendants())
                if ((string)v.Attribute("type") == type) return (string)v.Attribute("names");
            return null;
        }
        Assert.That(Names("MODEPVP_WIN"), Is.EqualTo(string.Join(",", Win)));
        Assert.That(Names("MODEPVP_DEAD"), Is.EqualTo(string.Join(",", Dead)));
        var tables = new GameTables(packs, "en");
        foreach (int id in new[] { 2421, 2422, 2423, 2424 })
        {
            string text = ClientFormat.Format(tables.Common(id), "Name");
            Assert.That(text, Does.Contain("Name"), $"common string {id} ({CommonStringIds.Key(id)}) formats the killer's name");
        }
    }
}
