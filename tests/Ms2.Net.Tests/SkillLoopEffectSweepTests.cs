using System.Numerics;
using System.Text;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Replay;
using NUnit.Framework;
using static Ms2.Net.Tests.SkillSequenceSweepTests;

namespace Ms2.Net.Tests;

/// <summary>
/// Every effect slot StateSkill runs for a skill motion besides the tag effects, over every skill level of the skill XML on
/// both PC bodies:
/// <list type="bullet">
/// <item>the motion effect, kept from AnimActivated (stateskill.cpp:750-761) in the record's m_n84;</item>
/// <item>the loop effect ("loopEffect"), kept at each "loopstart" while the charging loop has not ended (!m_bLoop, :844-860,
/// the old one stopped first) in m_n88 and stopped at "loopend" (:867-873);</item>
/// <item>the loop keep effect ("loopKeepEffect"), kept at "startLoopKeepEffect" when none is (StartLoopEffect :2358-2372) and
/// stopped at "endLoopKeepEffect" (StopLoopEffect :1659-1665), which then inserts the loop end effect ("loopEndEffect",
/// PlayLoopEndEffect :2376-2388);</item>
/// <item>the state's exit (vfunc_5 :515-567): StopRecordEffects (:2296-2310) stops m_n84 and m_n88 for a dead or stunned owner,
/// the next state 0xd or a notKeepEffect skill, the rest released; the end effect ("endEffect") inserted (:536-545); the loop
/// keep effect stopped (:567).</item>
/// </list>
/// Checked: <see cref="RemotePcStates"/> (another PC), driven frame by frame through each motion of the level as RemotePc
/// throws the sequence's keys (the loops' seeks included), gives the calls a model of those rules gives for the keys it
/// was handed, for the state's own end and for an exit to StateHit, StateStun, StateDead and a notKeepEffect skill after
/// the first motion's keys. The own cast's key walk (<see cref="SkillTagEffects.Keys"/>) holds every loop keep key at its
/// time (the own loop keys are the walk's own "loopstart"/"loopend" times, HudScene.OwnKeys). Each slot is counted: the
/// motions that name it, those whose sequence lacks the key that plays it, the effect files the data lacks, and the npc
/// skills (no body here: the npc's keys are not walked by the port, STILL-OPEN in RemoteLayer.EndNpcSkill).
/// MS2_SKILLSEQ_LOOPFX_OUT names a file the counts and the rows not ok are written to.
/// </summary>
[NonParallelizable]
public class SkillLoopEffectSweepTests
{
    [Test] public void KmsLoopEffectSlots() => Sweep("MS2_DATA");
    [Test] public void Gms2LoopEffectSlots() => Sweep("MS2_GMS2_DATA");

    const float Dt = 1f / 60f;
    const float StuckSeconds = 60f;

    /// <summary>The kept effects as the port's callbacks see them: each keep gets the next id.</summary>
    sealed class Calls
    {
        public readonly List<string> Out = new();
        int next = 1;
        public int Keep(string fx) { Out.Add($"keep:{fx}#{next}"); return next++; }
        public void Stop(int id) => Out.Add("stop:" + id);
        public void Release(int id) => Out.Add("release:" + id);
        public void Insert(string fx) => Out.Add("insert:" + fx);
    }

    /// <summary>The client's rules for the slots (the summary's cites), fed the keys the state took.</summary>
    sealed class Model
    {
        public readonly List<string> Out = new();
        int next = 1, n84, n88, loopKeep;
        bool inState;
        SkillTagEffects.MotionEffects fx;
        bool notKeep;

        int Keep(string f) { Out.Add($"keep:{f}#{next}"); return next++; }

        public void Enter(SkillTagEffects.MotionEffects effects, bool notKeepEffect)
        {
            if (inState) Exit(false);
            inState = true; fx = effects; notKeep = notKeepEffect; n84 = 0; n88 = 0;
            if (!string.IsNullOrEmpty(fx.Motion)) n84 = Keep(fx.Motion);   // AnimActivated, m_n84 == 0 for the new record
        }

        public void Key(string key, bool chargingLoopEnded)
        {
            if (key == "loopstart" && !chargingLoopEnded)
            {
                if (n88 != 0) { Out.Add("stop:" + n88); n88 = 0; }
                if (!string.IsNullOrEmpty(fx.Loop)) n88 = Keep(fx.Loop);
            }
            if (key == "loopend" && n88 != 0) { Out.Add("stop:" + n88); n88 = 0; }
            if (key.Equals("startLoopKeepEffect", StringComparison.OrdinalIgnoreCase) && loopKeep == 0 && !string.IsNullOrEmpty(fx.LoopKeep))
                loopKeep = Keep(fx.LoopKeep);
            if (key.Equals("endLoopKeepEffect", StringComparison.OrdinalIgnoreCase))
            {
                if (loopKeep != 0) { Out.Add("stop:" + loopKeep); loopKeep = 0; }
                if (!string.IsNullOrEmpty(fx.LoopEnd)) Out.Add("insert:" + fx.LoopEnd);
            }
        }

        /// <summary>vfunc_5: <paramref name="hard"/> for the owner dead or stunned or the next state 0xd.</summary>
        public void Exit(bool hard)
        {
            if (!inState) return;
            inState = false;
            if (hard || notKeep)
            {
                if (n84 != 0) Out.Add("stop:" + n84);
                if (n88 != 0) Out.Add("stop:" + n88);
                n84 = n88 = 0;
            }
            if (n84 != 0) Out.Add("release:" + n84);
            if (n88 != 0) Out.Add("release:" + n88);
            n84 = n88 = 0;
            if (!string.IsNullOrEmpty(fx.End)) Out.Add("insert:" + fx.End);
            if (loopKeep != 0) { Out.Add("stop:" + loopKeep); loopKeep = 0; }
        }
    }

    static SkillTagEffects.MotionEffects EffectsOf(Ms2.Formats.World.SkillMotion m) =>
        new(m.MotionEffect, m.LoopEffect, m.EndEffect, m.LoopKeepEffect, m.LoopEndEffect);

    /// <summary>The exits checked after the first motion's keys: none (the state's own end), StateHit, StateStun, StateDead
    /// (the owner dead), and the state's own end of a skill taken as notKeepEffect.</summary>
    enum ExitKind { Own, Hit, Stun, Dead, NotKeep }

    /// <summary>One run of a level's motions through RemotePcStates; the port's calls and the model's for the same keys, or
    /// null when a motion has no sequence or the body lacks it.</summary>
    static (string got, string exp)? Run(int id, SkillLevelRow level, SkillTable rows, BodyKfs body, ExitKind exit)
    {
        var calls = new Calls();
        var model = new Model();
        var pc = new Pc();
        var sm = new RemotePcStates(pc) { KeepEffect = calls.Keep, StopEffect = calls.Stop, ReleaseEffect = calls.Release, InsertEffect = calls.Insert };
        sm.ChangeState(RemotePcStates.StateIdle);
        bool charging = rows.ChargingSkill(id);
        bool notKeep = rows.NotKeepEffect(id) || exit == ExitKind.NotKeep;
        int count = level.Motions.Count;
        for (int m = 0; m < count; m++)
        {
            var mo = level.Motions[m];
            if (string.IsNullOrEmpty(mo.SequenceName)) return null;
            var kf = body.Get(mo.SequenceName);
            if (kf == null) return null;
            var fx = EffectsOf(mo);
            const float speed = 1f;   // the speed changes no key the state is handed (SkillTable has no sequenceSpeed)
            sm.Cast(id, charging, Vector3.Zero, 0f, 0, "", m, count, speed, mo.LoopCount, mo.AttackPoints, notKeepEffect: notKeep, effects: fx);
            model.Enter(fx, notKeep);
            bool chargingLoopEnded = false;
            int attack = 0;
            pc.Kf = kf; pc.Pos = 0; pc.LastPos = Ms2.Net.Replay.SkillMotion.KeysFromStart; pc.Rate = speed;
            bool ended = false;
            for (float t = 0; t < StuckSeconds; t += Dt)
            {
                pc.Pos = Math.Min(pc.Pos + Dt * pc.Rate, kf.Duration);
                bool finished = pc.Pos >= kf.Duration;
                pc.Seeked = false;
                double from = pc.LastPos, to = pc.Pos;
                foreach (var (kt, key) in kf.TextKeys)
                {
                    if (!(from < kt && kt <= to) || sm.Cur != RemotePcStates.StateSkill) continue;
                    int repeat = sm.CastRepeat;
                    model.Key(key, chargingLoopEnded);
                    // m_bLoop (:982-997): a charging skill's current attack point or "charging_attack" with no repeat left
                    string point = attack < mo.AttackPoints.Count ? mo.AttackPoints[attack] : null;
                    if (charging && !chargingLoopEnded && repeat == 0 && (key == "charging_attack" || (point != null && key == point)))
                        chargingLoopEnded = true;
                    if (key == "loopend" && repeat > 0) attack = 0;
                    if (point != null && key == point)
                        while (attack < mo.AttackPoints.Count && mo.AttackPoints[attack] == point) attack++;
                    sm.CastTextKey(key, (float)kt);
                }
                pc.LastPos = pc.Seeked ? Ms2.Net.Replay.SkillMotion.KeysFromStart : pc.Pos;
                if (m == 0 && exit is ExitKind.Hit or ExitKind.Stun or ExitKind.Dead && finished)
                {
                    // the first motion's keys are out: the owner leaves the state for the exit checked
                    if (exit == ExitKind.Dead) sm.SetDead(true);
                    sm.ChangeState(exit switch { ExitKind.Hit => RemotePcStates.StateHit, ExitKind.Stun => RemotePcStates.StateStun, _ => RemotePcStates.StateDead });
                    model.Exit(true);
                    return (string.Join(" ", calls.Out), string.Join(" ", model.Out));
                }
                if (finished && !pc.Seeked && sm.Cur == RemotePcStates.StateSkill) sm.CastSequenceEnded();
                sm.Update(Dt);
                if (sm.Cur != RemotePcStates.StateSkill) { model.Exit(false); return (string.Join(" ", calls.Out), string.Join(" ", model.Out)); }
                if (finished && !pc.Seeked && sm.CastRepeat <= 0) { ended = true; break; }
            }
            if (!ended) break;   // held: the state stays (the input sweep's "held-no-timer")
        }
        // the state did not end by itself (held, or the last motion waiting): another state takes it
        if (sm.Cur == RemotePcStates.StateSkill) { sm.ChangeState(RemotePcStates.StateIdle); model.Exit(false); }
        return (string.Join(" ", calls.Out), string.Join(" ", model.Out));
    }

    static bool HasKey(Ms2.Formats.Model.KfAnimation kf, string key, bool noCase) =>
        kf.TextKeys.Any(k => noCase ? string.Equals(k.text, key, StringComparison.OrdinalIgnoreCase) : k.text == key);

    static void Sweep(string variable)
    {
        string data = DataDir(variable);
        var spec = new NifSpec(NifXml());
        using var packs = new PackSet(data);
        var rows = new SkillTable(packs);
        var bodies = Bodies.ToDictionary(b => b, b => new BodyKfs(packs, spec, b));
        var effects = new EffectTable(packs);
        var names = new GameStrings(packs).Table("skillname", "name");
        var bad = new List<string>();
        int levels = 0, runs = 0, npcLevels = 0;
        // per slot: motions naming it (PC body sequences), those whose sequence lacks the playing key, the effect xml missing
        var slots = new[] { "motionEffect", "loopEffect", "loopKeepEffect", "loopEndEffect", "endEffect" };
        var named = slots.ToDictionary(s => s, _ => 0);
        var noKey = slots.ToDictionary(s => s, _ => 0);
        var noXml = slots.ToDictionary(s => s, _ => 0);
        var npcNamed = slots.ToDictionary(s => s, _ => 0);
        var slotSkills = slots.ToDictionary(s => s, _ => new SortedSet<string>());
        int ignoreHide = 0, loopKeepSamples = 0;
        var samples = new List<string>();
        // MS2_SKILLSEQ_LOOPFX_DUMP=<id>[,<id>...]: each level's motions of those skills, their sequence and effect files
        var dump = (Environment.GetEnvironmentVariable("MS2_SKILLSEQ_LOOPFX_DUMP") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToHashSet();
        var dumped = new List<string>();
        foreach (int id in dump.OrderBy(i => i))
            foreach (var level in rows.Levels(id))
                for (int m = 0; m < level.Motions.Count; m++)
                {
                    var mo = level.Motions[m];
                    var kf = string.IsNullOrEmpty(mo.SequenceName) ? null : bodies.Values.Select(b => b.Get(mo.SequenceName)).FirstOrDefault(k => k != null);
                    dumped.Add($"{id} level {level.Level} motion {m} {mo.SequenceName} loops {mo.LoopCount}: motion {mo.MotionEffect} loop {mo.LoopEffect} keep {mo.LoopKeepEffect} "
                        + $"loopEnd {mo.LoopEndEffect} end {mo.EndEffect}; keys {(kf == null ? "-" : string.Join(",", kf.TextKeys.Select(k => $"{k.text}@{k.time:0.###}")))}");
                }
        foreach (int id in rows.Ids.OrderBy(i => i))
        {
            var seen = new HashSet<string>();
            foreach (var level in rows.Levels(id))
            {
                if (level.Motions.Count == 0) continue;
                string sig = string.Join("|", level.Motions.Select(m => $"{m.SequenceName}/{m.MotionEffect}/{m.LoopEffect}/{m.LoopKeepEffect}/{m.LoopEndEffect}/{m.EndEffect}/{m.LoopCount}"));
                if (!seen.Add(sig)) continue;
                bool onBody = level.Motions.Any(m => !string.IsNullOrEmpty(m.SequenceName) && bodies.Values.Any(b => b.Get(m.SequenceName) != null));
                if (!onBody)
                {
                    if (level.Motions.Any(m => EffectsOf(m).Any)) npcLevels++;
                    foreach (var m in level.Motions)
                    {
                        var f = EffectsOf(m);
                        foreach (var (slot, file) in Slots(f)) if (!string.IsNullOrEmpty(file)) npcNamed[slot]++;
                    }
                    continue;
                }
                levels++;
                foreach (var m in level.Motions)
                {
                    if (m.IgnoreOptimalHideCastEffect) ignoreHide++;
                    var f = EffectsOf(m);
                    var kf = bodies.Values.Select(b => string.IsNullOrEmpty(m.SequenceName) ? null : b.Get(m.SequenceName)).FirstOrDefault(k => k != null);
                    foreach (var (slot, file) in Slots(f))
                    {
                        if (string.IsNullOrEmpty(file)) continue;
                        named[slot]++;
                        if (slot != "motionEffect") slotSkills[slot].Add($"{id}{(names.TryGetValue(id.ToString(), out var nm) ? $" {nm};" : ";")}");
                        if (effects.Get(file) == null) noXml[slot]++;
                        if (kf == null) continue;
                        bool has = slot switch
                        {
                            "loopEffect" => HasKey(kf, "loopstart", false),
                            "loopKeepEffect" => HasKey(kf, "startLoopKeepEffect", true),
                            "loopEndEffect" => HasKey(kf, "endLoopKeepEffect", true),
                            _ => true,
                        };
                        if (!has) { noKey[slot]++; slotSkills[slot].Add(id + " (no key);"); }
                    }
                    if (kf != null && (HasKey(kf, "startLoopKeepEffect", true) || HasKey(kf, "endLoopKeepEffect", true)))
                    {
                        // the own walk holds each loop keep key at its time
                        loopKeepSamples++;
                        var walk = new SkillTagEffects.Keys();
                        walk.Set(kf.TextKeys, m.TagEffects);
                        var want = kf.TextKeys.Where(k => SkillTagEffects.LoopKeepKey(k.text) != 0).ToList();
                        var held = Enumerable.Range(0, walk.Count).Select(i => walk[i]).Where(e => e.path == null && SkillTagEffects.LoopKeepKey(e.key) != 0).Select(e => (e.time, e.key)).ToList();
                        if (!held.SequenceEqual(want.Select(k => (k.time, k.text))))
                            bad.Add($"| {id} | {level.Level} | own walk | | {string.Join(",", want.Select(k => $"{k.text}@{k.time:0.###}"))} | {string.Join(",", held.Select(k => $"{k.key}@{k.time:0.###}"))} |");
                    }
                }
                if (!level.Motions.Any(m => EffectsOf(m).Any)) continue;
                foreach (var (bname, body) in bodies)
                    foreach (var exit in Enum.GetValues<ExitKind>())
                    {
                        if (Run(id, level, rows, body, exit) is not { } r) continue;
                        runs++;
                        if (r.got != r.exp) bad.Add($"| {id} | {level.Level} | {bname} | {exit} | {r.exp} | {r.got} |");
                        else if (exit == ExitKind.Own && samples.Count < 12 && level.Motions.Any(m => !string.IsNullOrEmpty(m.LoopEffect) || !string.IsNullOrEmpty(m.LoopKeepEffect)))
                            samples.Add($"{id} level {level.Level} {bname}: {r.got}");
                    }
            }
        }
        var sb = new StringBuilder();
        sb.AppendLine($"# {variable} {data}");
        sb.AppendLine($"PC levels {levels} (with a body sequence), remote runs {runs}, not ok {bad.Count}; npc levels with a slot {npcLevels}; "
            + $"motions with IgnoreOptimalHideCastEffect {ignoreHide}; motions with a loop keep key {loopKeepSamples}");
        sb.AppendLine("| slot | PC motions | sequence lacks the key | effect xml missing | npc motions |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var s in slots) sb.AppendLine($"| {s} | {named[s]} | {noKey[s]} | {noXml[s]} | {npcNamed[s]} |");
        foreach (var s in slots.Skip(1)) sb.AppendLine($"{s} skills: {string.Join(" ", slotSkills[s])}");
        foreach (var d in dumped) sb.AppendLine("dump " + d);
        sb.AppendLine("samples:");
        foreach (var s in samples) sb.AppendLine("  " + s);
        if (bad.Count > 0)
        {
            sb.AppendLine("| skill | level | body | exit | expected | played |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (var b in bad.Take(200)) sb.AppendLine(b);
        }
        var outPath = Environment.GetEnvironmentVariable("MS2_SKILLSEQ_LOOPFX_OUT");
        if (!string.IsNullOrEmpty(outPath)) File.AppendAllText(outPath, sb.ToString());
        TestContext.Progress.WriteLine(sb.ToString());
        Assert.That(levels, Is.GreaterThan(100), "the data has skills");
        Assert.That(runs, Is.GreaterThan(100), "levels with an effect slot were run");
        Assert.That(bad.Take(40), Is.Empty);
    }

    static IEnumerable<(string slot, string file)> Slots(SkillTagEffects.MotionEffects f)
    {
        yield return ("motionEffect", f.Motion);
        yield return ("loopEffect", f.Loop);
        yield return ("loopKeepEffect", f.LoopKeep);
        yield return ("loopEndEffect", f.LoopEnd);
        yield return ("endEffect", f.End);
    }
}
