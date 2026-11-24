using System;
using System.Linq;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>StateTalk's TALK_UP choice, StateStun's face, StateSwimDash's dash effect name and the stun buff's checks
/// (<see cref="OwnStateExit"/>, <see cref="OwnStun"/>, <see cref="SwimGems"/>).</summary>
public class OwnStateStillTests
{
    const int Self = 9;

    static (MyCharacter me, OwnStateExit exit) Pc(Func<bool> talkUp = null)
    {
        var me = new MyCharacter();
        var u = new FieldAddUser { ObjectId = Self, User = new UserBlock { Name = "a" } };
        u.Stats[0].Health = 500; u.Stats[2].Health = 500;
        me.Apply(u);
        var exit = new OwnStateExit { Stun = { Rand = () => 1 }, SequenceLength = n => n.Length > 0 ? 0.5 : null };
        if (talkUp != null) exit.TalkUp = talkUp;
        me.StateEntered += st => { if (exit.Enter(st, Self)) me.LeaveServerState(st); };
        return (me, exit);
    }

    [Test] public void TalkUpNeedsTheNpcsTalkAniAndBothOwnSequences()
    {
        // statetalk.cpp:101-137: bTalk && bFirst && bSecond -> TALK_UP forced (m_n10 0x47), else IDLE (0x49)
        for (int m = 0; m < 8; m++)
        {
            bool ani = (m & 1) != 0, up = (m & 2) != 0, idle = (m & 4) != 0;
            Assert.That(OwnStateExit.ChoosesTalkUp(ani, up, idle), Is.EqualTo(m == 7), $"{ani} {up} {idle}");
        }
        var (me, exit) = Pc(() => true);
        me.ApplyUserState(Self, 0x16);
        Assert.That((exit.Played, exit.PlayedLoops, exit.PlayedIsAction, me.State, exit.TalkPhase),
            Is.EqualTo((OwnStateExit.TalkUpAction, false, true, 0x16, OwnStateExit.TalkPhaseUp)));
        var (me2, exit2) = Pc(() => false);
        me2.ApplyUserState(Self, 0x16);
        Assert.That((exit2.Played, exit2.PlayedLoops, exit2.PlayedIsAction, exit2.TalkPhase),
            Is.EqualTo((OwnStateExit.TalkAction, true, true, OwnStateExit.TalkPhaseNone)));
        // the choice is made at the enter: asked again with no sub, the running state is kept and nothing is chosen again
        int asked = 0;
        var (me3, exit3) = Pc(() => { asked++; return true; });
        me3.ApplyUserState(Self, 0x16);
        me3.ApplyUserState(Self, 0x16);
        Assert.That((asked, exit3.Plays), Is.EqualTo((1, 1)));
    }

    [Test] public void TalkUpsEndPlaysTheTalkOrListenIdleByTheOwnBalloon()
    {
        // EndOfSequence (statetalk.cpp:215-222): TALK_UP's end -> 0x48; vfunc_9 (162-178): TALK_IDLE with the own chat
        // balloon active (m_4c), else LISTEN_IDLE, without bForce (the one playing is kept)
        bool balloon = false;
        var (me, exit) = Pc(() => true);
        exit.ActionLength = n => n == OwnStateExit.TalkUpAction ? 1.0 : null;
        exit.OwnBalloonActive = () => balloon;
        me.ApplyUserState(Self, 0x16);
        Assert.That(exit.Advance(0.99, me.State), Is.False);
        Assert.That((exit.TalkPhase, exit.Played, exit.Plays), Is.EqualTo((OwnStateExit.TalkPhaseUp, OwnStateExit.TalkUpAction, 1)));
        Assert.That(exit.Advance(0.02, me.State), Is.False);
        Assert.That((exit.TalkPhase, exit.Played, exit.PlayedLoops, exit.Plays),
            Is.EqualTo((OwnStateExit.TalkPhaseIdle, OwnStateExit.ListenIdleAction, true, 2)));
        exit.Advance(5, me.State);
        Assert.That(exit.Plays, Is.EqualTo(2));
        balloon = true;
        exit.Advance(0.016, me.State);
        Assert.That((exit.Played, exit.Plays), Is.EqualTo((OwnStateExit.TalkIdleAction, 3)));
        balloon = false;
        exit.Advance(0.016, me.State);
        Assert.That((exit.Played, exit.Plays, me.State), Is.EqualTo((OwnStateExit.ListenIdleAction, 4, 0x16)));
        Assert.That(OwnStateExit.TalkIdleOf(true), Is.EqualTo("TALK_IDLE"));
        Assert.That(OwnStateExit.TalkIdleOf(false), Is.EqualTo("LISTEN_IDLE"));
        // a body with no TALK_UP sequence never ends it; IDLE stays IDLE
        var (me2, exit2) = Pc(() => true);
        me2.ApplyUserState(Self, 0x16);
        exit2.Advance(30, me2.State);
        Assert.That((exit2.TalkPhase, exit2.Played), Is.EqualTo((OwnStateExit.TalkPhaseUp, OwnStateExit.TalkUpAction)));
        var (me3, exit3) = Pc(() => false);
        me3.ApplyUserState(Self, 0x16);
        exit3.Advance(30, me3.State);
        Assert.That((exit3.TalkPhase, exit3.Played, exit3.Plays), Is.EqualTo((OwnStateExit.TalkPhaseNone, OwnStateExit.TalkAction, 1)));
    }

    [Test] public void TalkHoldsTheBodyAndItsLeaveOrHitEndsTheTalk()
    {
        // vfunc_8 is empty (statetalk.cpp:209-211); vfunc_5 and vfunc_6 SendNpcTalkMode0 with a talk open (142-150, 202-206)
        var (me, exit) = Pc();
        me.ApplyUserState(Self, 0x16);
        Assert.That(exit.HoldsBody, Is.True);
        Assert.That(exit.Hit(), Is.False);
        Assert.That((exit.TakeTalkEnds(), exit.TakeTalkEnds(), me.State), Is.EqualTo((true, false, 0x16)));
        me.ApplyUserState(Self, 1);
        exit.Advance(0.016, me.State);
        Assert.That((exit.TakeTalkEnds(), exit.State, exit.TalkPhase, exit.HoldsBody), Is.EqualTo((true, 0, 0, false)));
        // another state's leave ends no talk
        var (me2, exit2) = Pc();
        me2.ApplyUserState(Self, 0x28);
        Assert.That(exit2.Hit(), Is.False);
        me2.ApplyUserState(Self, 1);
        exit2.Advance(0.016, me2.State);
        Assert.That(exit2.TakeTalkEnds(), Is.False);
    }

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void TheTalkSequencesClampOrLoopAsTheirKf(string data)
    {
        // table/pcactiondata.xml: TALK_UP ChatUp_A, TALK_IDLE Talk_A, LISTEN_IDLE Listen_A. NiSequenceData's Cycle Type:
        // ChatUp_A clamps (2), so its end moves StateTalk to 0x48; the two idles loop (0)
        using var packs = new PackSet(RcGap.Data(data));
        var nx = Environment.GetEnvironmentVariable("MS2_NIFXML");
        if (string.IsNullOrEmpty(nx))
            for (var d = new System.IO.DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
                if (System.IO.File.Exists(System.IO.Path.Combine(d.FullName, "MS2Godot.sln"))) { nx = System.IO.Path.Combine(d.FullName, "external", "nifxml", "nif.xml"); break; }
        if (string.IsNullOrEmpty(nx) || !System.IO.File.Exists(nx)) Assert.Ignore("MS2_NIFXML is not set and external/nifxml is not checked out");
        var spec = new Ms2.Formats.Nif.NifSpec(nx);
        var actions = Ms2.Formats.Text.PackXml.Parse(packs.Read("Xml", "table/pcactiondata.xml")).Root!.Descendants("v")
            .GroupBy(v => (string)v.Attribute("type")).ToDictionary(g => g.Key, g => ((string)g.First().Attribute("names")).Split(',')[0]);
        var seen = new System.Collections.Generic.List<string>();
        foreach (var (action, cycle) in new[] { (OwnStateExit.TalkUpAction, 2), (OwnStateExit.TalkIdleAction, 0), (OwnStateExit.ListenIdleAction, 0) })
            foreach (var body in new[] { "male", "female" })
            {
                string seq = actions[action];
                Assert.That(BodySequences.Exists(packs, body, seq), Is.True, $"{body} {seq}");
                var kf = Ms2.Formats.Model.KfAnimation.Load(Ms2.Formats.Nif.NifFile.Load(spec, packs.Read(BodySequences.Archive, BodySequences.Entry(body, seq))));
                Assert.That(kf.Cycle, Is.EqualTo(cycle), $"{body} {seq}");
                seen.Add($"{body}/{seq} {kf.Cycle} {kf.Duration:0.###}");
            }
        TestContext.WriteLine(string.Join("; ", seen));
    }

    [Test] public void EachStunEnterPlaysTheStunFaceOnce()
    {
        // statestun.cpp:112: FUN_14166ef10("Stun", 1, 0, false) at the end of StateStun::vfunc_4
        var (me, exit) = Pc();
        me.ApplyUserState(Self, 0x12);
        Assert.That(exit.TakeFace(), Is.EqualTo(OwnStun.Face));
        Assert.That(exit.TakeFace(), Is.Null);
        me.ApplyUserState(Self, 0x12);   // kept: no vfunc_4
        Assert.That(exit.TakeFace(), Is.Null);
        // the buff's entry of the running StateStun only takes the sub (State::vfunc_25 -> vfunc_2)
        Assert.That(exit.EnterBuffStun(1, OwnStun.BuffSub(1, true, false), ""), Is.False);
        Assert.That(exit.TakeFace(), Is.Null);
        // a fresh entry by the buff runs vfunc_4
        var (me2, exit2) = Pc();
        Assert.That(exit2.EnterBuffStun(1, OwnStun.BuffSub(1, true, false), ""), Is.True);
        Assert.That(exit2.TakeFace(), Is.EqualTo(OwnStun.Face));
        // other states play no face
        foreach (var row in OwnStateExit.Classes.Where(r => r.Id is > 2 and not 0x12 && r.Cause != StateExitCause.NotOwn))
        {
            var (m, e) = Pc();
            m.ApplyUserState(Self, row.Id);
            Assert.That(e.TakeFace(), Is.Null, row.Class);
        }
    }

    // the face items' emotion groups (emotion/item/103*.xml, imports included): the "Stun" StateStun plays
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryFaceGroupHasTheStunFace(string data)
    {
        using var packs = new PackSet(RcGap.Data(data));
        var table = new EmotionTable(packs, EmotionTable.Item);
        var ids = packs.List("Xml").Where(e => e.StartsWith("emotion/item/103", StringComparison.OrdinalIgnoreCase) && e.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Select(e => uint.Parse(e[13..^4])).ToList();
        var groups = ids.Select(id => (id, g: table.Group(id))).Where(x => x.g != null).ToList();
        var without = groups.Where(x => x.g.Find(OwnStun.Face) == null).Select(x => x.id).ToList();
        TestContext.Out.WriteLine($"{data}: {groups.Count} face groups, without Stun {without.Count} {string.Join(",", without.Take(10))}");
        Assert.That(groups.Count, Is.GreaterThan(150), "KMS 191, GMS2 189");
        Assert.That(without, Is.Empty);
        Assert.That(table.Group(10300001)?.Find(OwnStun.Face), Is.Not.Null, "the male base face");
        Assert.That(table.Group(10300003)?.Find(OwnStun.Face), Is.Not.Null, "the female base face");
    }

    [Test] public void SwimDashEntersSwimThenStartsItsEffectEachEntry()
    {
        // StateMoveSkill::vfunc_4 (statemanager.cpp:670-676, 720-723): no data, the fallback StateSwim at once; then
        // FUN_1417fbec0 (stateswimdash.cpp:221-233) on every entry, the last one stopped first
        var (me, exit) = Pc();
        for (int i = 0; i < 2; i++)
        {
            me.ApplyUserState(Self, 0x1c);
            Assert.That((me.State, exit.State), Is.EqualTo((0, 0)));
            Assert.That((exit.TakeFallback(), exit.TakeDashEffect()), Is.EqualTo((0x1b, true)));
            Assert.That(OwnStateExit.BodyEntryOf(0x1c), Is.EqualTo(BodyEntry.Swim));
        }
        // the effect name: the swim gem's dashEffect, else the gender's run tube (GetSwimEffectPath, itemugcmesh.cpp:947-964)
        var gem = new SwimGems.Gem(70600001, "kf", "idle.xml", "run.xml", "dash.xml");
        Assert.That(SwimGems.EffectName(SwimGems.DashAction, gem, 0), Is.EqualTo("dash.xml"));
        Assert.That(SwimGems.EffectName(SwimGems.IdleAction, gem, 0), Is.EqualTo("idle.xml"));
        Assert.That(SwimGems.EffectName(0x31, gem, 0), Is.EqualTo("run.xml"));
        Assert.That(SwimGems.EffectName(SwimGems.DashAction, null, 0), Is.EqualTo("CH/Common/Eff_Tube_M_SwimRun_A.xml"));
        Assert.That(SwimGems.EffectName(SwimGems.DashAction, null, 1), Is.EqualTo("CH/Common/Eff_Tube_F_SwimRun_A.xml"));
        Assert.That(SwimGems.EffectName(SwimGems.IdleAction, null, 1), Is.EqualTo("CH/Common/Eff_Tube_F_SwimIdle_A.xml"));
    }

    [Test] public void ClimbWithNoDataGoesToIdleWithTheShadowOff()
    {
        // StateClimb::vfunc_4 (state.cpp:1029-1057): the fallback StateIdle at once, then SetShadowVisible(false)
        var (me, exit) = Pc();
        me.ApplyUserState(Self, 0x1d);
        Assert.That((me.State, exit.State), Is.EqualTo((0, 0)));
        Assert.That(OwnStateExit.BodyEntryOf(0x1d), Is.EqualTo(BodyEntry.Climb));
        Assert.That(HeldItem.ForStateId(0x1d), Is.EqualTo(HeldItemState.Climb));
        Assert.That(HeldItem.ForStateId(0x1c), Is.EqualTo(HeldItemState.SwimDash));
    }

    [Test] public void TheStunBuffWaitsForHealthGrabTargetAndARidingHolder()
    {
        // additionalcli_2.cpp:786-797
        Assert.That(OwnStun.BuffMayEnter(500, 1, false), Is.True);
        Assert.That(OwnStun.BuffMayEnter(0, 1, false), Is.False, "no health");
        Assert.That(OwnStun.BuffMayEnter(500, OwnStun.StateGrabTarget, false), Is.False, "StateGrabTarget::vfunc_17");
        Assert.That(OwnStun.BuffMayEnter(500, OwnStun.StateHold, true), Is.False, "held by a riding pc");
        // every other own state's vfunc_17 is State's (1)
        foreach (var row in OwnStateExit.Classes.Where(r => r.Id > 0 && r.Id != OwnStun.StateGrabTarget))
            Assert.That(OwnStun.BuffMayEnter(500, row.Id, false), Is.True, row.Class);
        // pc.cpp:2967-2988: StateHold, a PC holder, riding, event buff 2; any one missing lets the stun in
        for (int m = 0; m < 16; m++)
        {
            bool hold = (m & 1) != 0, pc = (m & 2) != 0, rides = (m & 4) != 0, buff = (m & 8) != 0;
            Assert.That(OwnStun.HeldByRidingPc(hold ? OwnStun.StateHold : 1, pc, rides, buff), Is.EqualTo(m == 15), $"{hold} {pc} {rides} {buff}");
        }
    }

    // talkAni on the npc's environment (KMS) or its <basic> (GMS2), true when absent (npcdata.cpp:650, 1463)
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryNpcReadsItsTalkAni(string data)
    {
        using var packs = new PackSet(RcGap.Data(data));
        var t = new NpcTable(packs);
        int off = t.Rows.Count(n => !n.TalkAni);
        TestContext.Out.WriteLine($"{data}: {t.Count} npcs, talkAni off {off}");
        Assert.That(t.Find(22000052)?.TalkAni, Is.True, "talkAni=\"1\"");
        Assert.That(off, Is.GreaterThan(300), "KMS 468 of 10,795, GMS2 338 of 10,193");
        Assert.That(off, Is.LessThan(t.Count));
    }

    // table/itemgemproperty_swim.xml: every <swim> row with its effects
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EverySwimGemReadsItsEffects(string data)
    {
        using var packs = new PackSet(RcGap.Data(data));
        var gems = SwimGems.Load(packs);
        TestContext.Out.WriteLine($"{data}: {gems.Count} swim gems");
        Assert.That(gems.Count, Is.GreaterThan(25), "KMS 33, GMS2 29");
        var g = gems.Find(70600001);
        Assert.That(g, Is.Not.Null, "70600001");
        Assert.That(g.DashEffect, Is.Not.Empty);
        foreach (var id in Enumerable.Range(70600001, 9))
            if (gems.Find(id) is { } x)
                Assert.That((x.IdleEffect.Length > 0, x.RunEffect.Length > 0, x.DashEffect.Length > 0), Is.EqualTo((true, true, true)), $"{id}");
        Assert.That(SwimGems.Feature, Is.EqualTo(Array.IndexOf(FeatureTable.Names.ToArray(), "GemSwim") + 1));
    }
}
