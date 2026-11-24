using System.Collections.Generic;
using System.Linq;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>What each state class plays on the body when the server enters it with no data (OnUserState,
/// scenestategame.cpp:5759-5770; <see cref="OwnStateExit.Enters"/>), and StateStun as the stun buff enters it
/// (AdditionalCliList::FUN_1409713b0, additionalcli_2.cpp:786-833) with its sub and kind kept across entries.</summary>
public class OwnStateEnterTests
{
    const int Self = 9;
    const double Len = 0.5;

    static (MyCharacter me, OwnStateExit exit) Pc(int rand = 1)
    {
        var me = new MyCharacter();
        var u = new FieldAddUser { ObjectId = Self, User = new UserBlock { Name = "a" } };
        u.Stats[0].Health = 500; u.Stats[2].Health = 500;
        me.Apply(u);
        var exit = new OwnStateExit { Stun = { Rand = () => rand }, SequenceLength = n => n.Length > 0 ? Len : null };
        me.StateEntered += st => { if (exit.Enter(st, Self)) me.LeaveServerState(st); };   // LiveSession.EnterOwnStateExit
        return (me, exit);
    }

    /// <summary>The client's play for each own class with no data, read from its enter (cites in OwnStateExit.Enters): the
    /// sequences in the order they play (the enter's, then the first frame's), the hide, the effect and the body's entry.</summary>
    static readonly Dictionary<string, (string[] Seq, bool Hidden, string Effect, BodyEntry Body)> Client = new()
    {
        ["StateIdle"] = (null, false, null, BodyEntry.Idle),
        ["StateWalk"] = (null, false, null, BodyEntry.Walk),
        ["StateCrawl"] = (null, false, null, BodyEntry.Idle),
        ["StateLand"] = (null, false, null, BodyEntry.Land),
        ["StateFall"] = (null, false, null, BodyEntry.Fall),
        ["StateJump"] = (null, false, null, BodyEntry.Jump),
        ["StateJumpTo"] = (null, false, null, BodyEntry.Idle),
        ["StateLadder"] = (null, false, null, BodyEntry.Ladder),
        ["StateRope"] = (null, false, null, BodyEntry.Rope),
        ["StateEmotion"] = (null, false, null, BodyEntry.Idle),
        ["StateHit"] = (null, false, null, BodyEntry.Hit),
        ["StateEmotionIdle"] = (null, false, null, BodyEntry.Idle),
        ["StateSkill"] = (null, false, null, BodyEntry.None),
        ["StateSpawn"] = (null, true, null, BodyEntry.Idle),
        ["StateStun"] = (new[] { "Down_A", "Stnd_Up_A" }, false, null, BodyEntry.Idle),
        ["StateDash"] = (null, false, null, BodyEntry.Dash),
        ["StateTalk"] = (new[] { OwnStateExit.TalkAction }, false, null, BodyEntry.Idle),
        ["StateRevival"] = (null, false, null, BodyEntry.Revival),
        ["StateInteraction"] = (null, false, null, BodyEntry.None),
        ["StateSwim"] = (null, false, null, BodyEntry.Swim),
        ["StateSwimDash"] = (null, false, DashEffect, BodyEntry.Swim),   // StateSwim at once, then the dash effect
        ["StateClimb"] = (null, false, null, BodyEntry.Climb),   // StateIdle at once, then the shadow off
        ["StateGlide"] = (null, false, null, BodyEntry.Idle),
        ["StateFallDamage"] = (null, false, null, BodyEntry.FallDamage),
        ["StateHold"] = (null, false, null, BodyEntry.Idle),
        ["StateRide"] = (null, false, null, BodyEntry.None),
        ["StateTransform"] = (new[] { "Equip_Change_Mantle_A" }, false, "CH/Common/Eff_FittingDoll.xml", BodyEntry.Idle),
        ["StateTaxiCall"] = (null, false, null, BodyEntry.Idle),
        ["StateSummon"] = (null, false, null, BodyEntry.Idle),   // vfunc_3 moves by a zero vector (statesummon.cpp:275-279)
        ["StatePvPWinLose"] = (null, false, null, BodyEntry.Idle),
        ["StateUseFurniture"] = (null, false, null, BodyEntry.None),
        ["StateCashCall"] = (null, false, null, BodyEntry.Idle),
        ["StateGrabTarget"] = (null, false, null, BodyEntry.Idle),
        ["StateRecall"] = (new[] { "Object_React_A" }, false, null, BodyEntry.Idle),
        ["StateFishing"] = (null, false, null, BodyEntry.Idle),
        ["StatePlayInstrument"] = (null, false, null, BodyEntry.None),
        ["StateTriggerEmotion"] = (null, false, null, BodyEntry.TriggerEmotion),
        ["StateHomeConvenient"] = (null, false, null, BodyEntry.Idle),
        ["StateHomemade"] = (null, false, null, BodyEntry.None),
        ["StateAutoInteraction"] = (new[] { "Object_React_C" }, false, null, BodyEntry.Idle),
        ["StateCoupleEmotion"] = (null, false, null, BodyEntry.Idle),
        ["StateBanner"] = (null, false, null, BodyEntry.Idle),
        ["StateMicroGameRps"] = (null, false, null, BodyEntry.None),
        ["StateReact"] = (null, false, null, BodyEntry.None),
        ["StateTreeWatering"] = (null, false, null, BodyEntry.Idle),   // vfunc_3 moves by a zero vector (statetreewateringcontext.cpp:140-145)
        ["StateNurturing"] = (null, false, null, BodyEntry.None),
        ["StateMicroGameCoupleDance"] = (null, false, null, BodyEntry.None),
        ["StateTriggerFollowNpc"] = (null, false, null, BodyEntry.Idle),
        ["StateWeddingEmotion"] = (null, false, null, BodyEntry.None),
    };

    [Test] public void EveryStateClassPlaysAsTheClient()
    {
        Assert.That(OwnStateExit.Enters.Select(r => (r.Class, r.Id)), Is.EqualTo(OwnStateExit.Classes.Select(r => (r.Class, r.Id))));
        var counts = new Dictionary<StateEnterPlay, int>();
        var mismatches = new List<string>();
        int checkedRows = 0;
        foreach (var row in OwnStateExit.Enters)
        {
            counts[row.Play] = counts.GetValueOrDefault(row.Play) + 1;
            if (row.Play == StateEnterPlay.NotOwn)
            {
                if (OwnStateExit.EnterOfOwnState(row.Id)?.Class == row.Class) mismatches.Add($"{row.Class}: the pc's class for the id");
                continue;
            }
            checkedRows++;
            if (OwnStateExit.EnterOfOwnState(row.Id)?.Class != row.Class) { mismatches.Add($"{row.Class}: not the pc's class for the id"); continue; }
            if (!Client.TryGetValue(row.Class, out var want)) { mismatches.Add($"{row.Class}: no client row"); continue; }
            var (played, hidden, effect) = Played(row.Id);
            var body = OwnStateExit.BodyEntryOf(row.Id);
            string got = $"{Show(played)} hidden {hidden} effect {effect ?? "-"} body {body}";
            string exp = $"{Show(want.Seq)} hidden {want.Hidden} effect {want.Effect ?? "-"} body {want.Body}";
            if (got != exp) mismatches.Add($"{row.Class} (0x{row.Id:x}) {row.Play}: {got}, client {exp}");
            // the table's kind agrees with what is played
            bool kindOk = row.Play switch
            {
                StateEnterPlay.Sequence => want.Seq != null,
                StateEnterPlay.Hide => want.Hidden,
                StateEnterPlay.BodyState => want.Seq == null && want.Body is not (BodyEntry.None or BodyEntry.Idle or BodyEntry.TriggerEmotion) || row.Id == 1,
                _ => want.Seq == null && !want.Hidden,
            };
            if (!kindOk) mismatches.Add($"{row.Class}: kind {row.Play} against the client's play");
        }
        foreach (var kv in counts.OrderBy(k => k.Key)) TestContext.Out.WriteLine($"{kv.Key}: {kv.Value}");
        TestContext.Out.WriteLine($"own classes {checkedRows}, matching {checkedRows - mismatches.Count}");
        Assert.That(mismatches, Is.Empty);
        Assert.That(counts, Is.EquivalentTo(new Dictionary<StateEnterPlay, int>
        {
            [StateEnterPlay.Nothing] = 19, [StateEnterPlay.BodyState] = 12, [StateEnterPlay.Sequence] = 5,
            [StateEnterPlay.Hide] = 1, [StateEnterPlay.Effect] = 1, [StateEnterPlay.CastingBar] = 2,
            [StateEnterPlay.Apart] = 2, [StateEnterPlay.NotOwn] = 12, [StateEnterPlay.Later] = 7,
        }));
    }

    /// <summary>The dash effect StateSwimDash's enter starts (PC::GetSwimEffectName(0x32), the caller's).</summary>
    const string DashEffect = "swim dash";

    static string Show(IEnumerable<string> seq) => seq == null ? "nothing" : string.Join(" > ", seq);

    /// <summary>The state entered as OnUserState enters it, then one frame: the sequences played, the hide on the enter and
    /// the effect started.</summary>
    static (List<string> played, bool hidden, string effect) Played(int id)
    {
        var (me, exit) = Pc();
        var played = new List<string>();
        int plays = 0;
        void Take() { if (exit.Played != null && exit.Plays != plays) { played.Add(exit.Played); plays = exit.Plays; } }
        me.ApplyUserState(Self, id);
        Take();
        bool hidden = exit.Hidden;
        string effect = exit.TakeEffect() ?? (exit.TakeDashEffect() ? DashEffect : null);
        int st = exit.State;
        if (exit.Advance(0.01, me.State)) me.LeaveServerState(st);
        Take();
        return (played.Count == 0 ? null : played, hidden, effect);
    }

    [Test] public void SpawnHidesTheBodyUntilItsTimer()
    {
        var (me, exit) = Pc();
        me.ApplyUserState(Self, 0x11);
        double d = OwnStateExit.SpawnDelayMs(Self) / 1000.0;
        Assert.That((exit.Hidden, exit.HoldsBody), Is.EqualTo((true, true)));
        Frames(me, exit, d - 0.005, null);
        Assert.That(exit.Hidden, Is.True);
        Frames(me, exit, 0.01, null);
        Assert.That((exit.Hidden, me.State), Is.EqualTo((false, 0)));
    }

    [Test] public void TheBuffStunSubOfEveryStun()
    {
        var rows = new List<string>();
        foreach (int stun in new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 99 })
            rows.Add($"{stun}: pc {OwnStun.BuffSub(stun, true, false):x} list {OwnStun.BuffSub(stun, true, true):x} npc {OwnStun.BuffSub(stun, false, false):x}");
        Assert.That(rows, Is.EqualTo(new[]
        {
            "0: pc 40 list 40 npc 40", "1: pc 40 list 40 npc 40", "2: pc 3d list 3e npc 40", "3: pc 41 list 41 npc 41",
            "4: pc 42 list 42 npc 42", "5: pc 43 list 43 npc 43", "6: pc 3d list 3e npc 40", "7: pc 44 list 44 npc 44",
            "8: pc 45 list 45 npc 45", "9: pc 40 list 40 npc 40", "99: pc 46 list 46 npc 46",
        }));
    }

    /// <summary>LiveSession.TickOwnStateExit's frames: the buff's entry (while <paramref name="buff"/> is set), then the
    /// running state's frame. Returns the sequences started, in order.</summary>
    static List<string> Frames(MyCharacter me, OwnStateExit exit, double seconds, (int Stun, int Sub, string Anim)? buff, List<string> into = null)
    {
        into ??= new List<string>();
        int plays = exit.Plays;
        for (double t = 0; t < seconds - 1e-9; t += 0.01)
        {
            if (buff is { } b && exit.EnterBuffStun(b.Stun, b.Sub, b.Anim)) me.EnterOwnState(0x12);
            int st = exit.State;
            if (exit.Advance(System.Math.Min(0.01, seconds - t), me.State, false, buff != null)) me.LeaveServerState(st);
            if (exit.Played != null && exit.Plays != plays) { into.Add(exit.Played); plays = exit.Plays; }
        }
        return into;
    }

    static List<string> BuffEnter(MyCharacter me, OwnStateExit exit, int stun, bool fromList, string anim = "")
    {
        int sub = OwnStun.BuffSub(stun, true, fromList);
        Assert.That(exit.EnterBuffStun(stun, sub, anim), Is.True);
        Assert.That(me.EnterOwnState(0x12), Is.True);
        return new List<string> { exit.Played };
    }

    [Test] public void ALiveStunLiesDownLiesAndStandsUpWhenTheBuffEnds()
    {
        var (me, exit) = Pc();
        var seen = BuffEnter(me, exit, 2, false);
        var buff = (2, OwnStun.BuffSub(2, true, false), "");
        Frames(me, exit, Len * 3 + 0.05, buff, seen);
        Assert.That((me.State, exit.HoldsBody), Is.EqualTo((0x12, true)));
        Frames(me, exit, Len + 0.03, null, seen);
        Assert.That(seen, Is.EqualTo(new[] { "Down_A", "Down_Idle_A", "Down_Idle_A", "Down_Idle_A", "Stnd_Up_A" }));
        Assert.That(me.State, Is.EqualTo(0));
    }

    [Test] public void AListStunLiesAtOnceAndKindSixTakesTheCPhases()
    {
        var (me, exit) = Pc(rand: 0);
        var seen = BuffEnter(me, exit, 2, true);
        Frames(me, exit, 0.1, (2, OwnStun.SubLie, ""), seen);
        Assert.That(seen, Is.EqualTo(new[] { "Down_Idle_B" }));

        var (me6, exit6) = Pc();
        seen = BuffEnter(me6, exit6, 6, false);
        Frames(me6, exit6, Len + 0.05, (6, OwnStun.SubDown, ""), seen);
        Frames(me6, exit6, Len + 0.03, null, seen);
        Assert.That(seen, Is.EqualTo(new[] { "Down_C", "Down_Idle_C", "Stnd_Up_C" }));
        Assert.That((me6.State, exit6.Stun.Kind), Is.EqualTo((0, 6)));
    }

    [Test] public void TheServersStunKeepsTheLastStunsSubAndKind()
    {
        var (me, exit) = Pc();
        BuffEnter(me, exit, 6, false);
        Frames(me, exit, 0.05, (6, OwnStun.SubDown, ""));
        var seen = Frames(me, exit, Len + 0.05, null);
        Assert.That((me.State, exit.Stun.Sub, exit.Stun.Kind), Is.EqualTo((0, OwnStun.SubStandUp, 6)));
        // the server's stun, no data: sub 0x3f and kind 6 are kept, so the enter plays Stnd_Up_C and its end leaves
        seen.Clear();
        me.ApplyUserState(Self, 0x12);
        seen.Add(exit.Played);
        Frames(me, exit, Len + 0.03, null, seen);
        Assert.That(seen, Is.EqualTo(new[] { "Stnd_Up_C" }));
        Assert.That(me.State, Is.EqualTo(0));
    }

    [Test] public void PlainSnarePauseAndTheBuffsOwnAnimation()
    {
        // 1, the plain set: Stun_A; the first frame without the buff leaves
        var (me, exit) = Pc();
        Assert.That(BuffEnter(me, exit, 1, false), Is.EqualTo(new[] { "Stun_A" }));
        Frames(me, exit, 2, (1, OwnStun.SubPlain, ""));
        Assert.That(me.State, Is.EqualTo(0x12));
        Frames(me, exit, 0.01, null);
        Assert.That(me.State, Is.EqualTo(0));

        // 4, the snare: its end leaves even under the buff, which enters again the next frame
        (me, exit) = Pc();
        BuffEnter(me, exit, 4, false);
        var seen = Frames(me, exit, Len + 0.05, (4, OwnStun.SubSnare, ""));
        Assert.That((seen.First(), me.State), Is.EqualTo(("Snare_A", 0x12)));
        Assert.That(exit.Stun.Plays, Is.EqualTo(2));

        // 3, the pause: the body paused while the buff holds, playing on after it
        (me, exit) = Pc();
        BuffEnter(me, exit, 3, false);
        Frames(me, exit, 1, (3, OwnStun.SubPause, ""));
        Assert.That((exit.Paused, exit.Played), Is.EqualTo((true, (string)null)));
        Frames(me, exit, 0.01, null);
        Assert.That((exit.Paused, me.State), Is.EqualTo((false, 0)));

        // 99, the effect's own stun animation, looped after its first end
        (me, exit) = Pc();
        Assert.That(BuffEnter(me, exit, 99, false, "Custom_Stun"), Is.EqualTo(new[] { "Custom_Stun" }));
        Assert.That(exit.PlayedLoops, Is.False);
        Frames(me, exit, Len + 0.05, (99, OwnStun.SubOwnAnimation, "Custom_Stun"));
        Assert.That((exit.Played, exit.PlayedLoops, me.State), Is.EqualTo(("Custom_Stun", true, 0x12)));
    }
}
