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
/// Two things StateSkill does for every skill motion, over every PC skill level of the skill XML on both bodies:
/// <list type="bullet">
/// <item>The motion effect (SkillMotionData "motionEffect"): StateSkill::AnimActivated (@0x1417e71d0, stateskill.cpp:750-761)
/// keeps it on the caster (FUN_1411a49c0 with SetActor) in the record's m_n84, once per record; the state's exit
/// (vfunc_5 :515-524) stops it at once (StopRecordEffects :2296-2310) for a dead, stunned or hit (0xd) owner or a
/// notKeepEffect skill, else releases it (FUN_1411a69e0). A level without a sequence has PlayCastStartEffect
/// (:1829-1838) insert it on the caster instead. Checked: <see cref="RemotePcStates"/> (the other PCs) spawns it once at
/// the cast on the caster and ends it as the client does, through <see cref="SkillTagEffects.Handles"/>, the handles the
/// own cast keeps too (HudScene.Cast.cs). Each effect's xml is looked up (<see cref="EffectTable"/>).</item>
/// <item>The voice of a text key containing "EventSound" (TextKeyEvent :834-841): PlayEventVoice(FUN_141793500(key.Mid(11)))
/// from Table/EventSound.xml (<see cref="Ms2.Audio.EventSoundTable"/>) by the PC's gender, job group and whether it is the local player.
/// Checked: the own cast's key walk (<see cref="SkillTagEffects.Keys"/>) holds every such key at its time, and
/// <see cref="RemotePcStates.CastTextKey"/> hands every one to <see cref="RemotePcStates.EventSound"/>; the voice each
/// gives the own PC (the skill's job group; a non-job skill every group) and the other PCs is counted. That the paths
/// are sound events of the data's MS2Sound.fev is Ms2.Audio.Tests' EventSoundTableTests.</item>
/// </list>
/// MS2_SKILLSEQ_FXVOICE_OUT names a file the counts and the rows not ok are written to.
/// </summary>
[NonParallelizable]
public class SkillMotionEffectVoiceSweepTests
{
    [Test] public void KmsMotionEffectsAndVoices() => Sweep("MS2_DATA");
    [Test] public void Gms2MotionEffectsAndVoices() => Sweep("MS2_GMS2_DATA");

    /// <summary>PC::GetJobGroupId of a job skill's job: the id's first three digits 101-111 are the jobs 10-110, 100 the
    /// beginner (1); 0 for a skill of no job.</summary>
    static int JobGroupOf(int skillId)
    {
        int j = skillId / 100000;
        if (j == 100) return 1;
        return j is >= 101 and <= 111 ? (j - 100) * 10 : 0;
    }

    static readonly int[] JobGroups = { 1, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110 };

    sealed class Calls
    {
        public readonly List<string> Out = new();
        int next = 1;
        public int Spawn(string fx) { Out.Add("keep:" + fx); return next++; }
        public void Stop(int id) => Out.Add("stop:" + id);
        public void Release(int id) => Out.Add("release:" + id);
    }

    static void Sweep(string variable)
    {
        string data = DataDir(variable);
        var spec = new NifSpec(NifXml());
        using var packs = new PackSet(data);
        var rows = new SkillTable(packs);
        var bodies = Bodies.ToDictionary(b => b, b => new BodyKfs(packs, spec, b));
        var effects = new EffectTable(packs);
        var voices = Ms2.Audio.EventSoundTable.Parse(Encoding.UTF8.GetString(packs.Read("Xml", "table/eventsound.xml")));
        var bad = new List<string>();
        int levels = 0, motions = 0, withFx = 0, fxMissing = 0, fxLoop = 0, fxSound = 0, fxMoving = 0, noSeqFx = 0, fxRows = 0;
        int eventKeys = 0, ownVoices = 0, remoteVoices = 0, voiceRows = 0;
        var keySpellings = new Dictionary<string, int>(StringComparer.Ordinal);
        var noVoiceGroups = new HashSet<string>();
        foreach (int id in rows.Ids.OrderBy(i => i))
        {
            var seen = new HashSet<string>();
            foreach (var lr in rows.Levels(id))
            {
                var level = lr;
                if (level.Motions.Count == 0) continue;
                string sig = string.Join("|", level.Motions.Select(m => $"{m.SequenceName}/{m.MotionEffect}/{m.LoopCount}"));
                if (!seen.Add(sig)) continue;
                levels++;
                int jobGroup = JobGroupOf(id);
                for (int mi = 0; mi < level.Motions.Count; mi++)
                {
                    var m = level.Motions[mi];
                    motions++;
                    bool hasSeq = !string.IsNullOrEmpty(m.SequenceName);
                    if (!string.IsNullOrEmpty(m.MotionEffect))
                    {
                        withFx++;
                        var parts = effects.Get(m.MotionEffect);
                        if (parts == null) fxMissing++;
                        else
                        {
                            if (parts.Any(p => p.Loop)) fxLoop++;
                            if (parts.Any(p => p.Type is "sound" or "soundEvent")) fxSound++;
                        }
                        if (m.MoveDistance != 0 || m.KeyMove != 0) fxMoving++;
                        if (!hasSeq) noSeqFx++;
                        else
                        {
                            // the other PC's state, as RemoteLayer.Casts drives it: a normal end releases, a hard one stops
                            foreach (bool dead in new[] { false, true })
                            {
                                fxRows++;
                                var calls = new Calls();
                                var pc = new Pc();
                                var sm = new RemotePcStates(pc) { KeepEffect = calls.Spawn, StopEffect = calls.Stop, ReleaseEffect = calls.Release };
                                sm.ChangeState(RemotePcStates.StateIdle);
                                bool notKeep = rows.NotKeepEffect(id);
                                sm.Cast(id, rows.ChargingSkill(id), Vector3.Zero, 0f, motion: mi, motionCount: level.Motions.Count,
                                    notKeepEffect: notKeep, effects: new SkillTagEffects.MotionEffects(m.MotionEffect, null, null, null, null));
                                if (dead) sm.SetDead(true);
                                sm.ChangeState(dead ? RemotePcStates.StateDead : RemotePcStates.StateIdle);
                                string exp = $"keep:{m.MotionEffect} {(dead || notKeep ? "stop:1" : "release:1")}";
                                string got = string.Join(" ", calls.Out);
                                if (got != exp) bad.Add($"| {id} | {lr.Level} | {mi} | motionEffect | remote{(dead ? " dead" : "")} | {exp} | {got} |");
                            }
                        }
                    }
                    if (!hasSeq) continue;
                    foreach (var (bname, body) in bodies)
                    {
                        var kf = body.Get(m.SequenceName);
                        if (kf == null) continue;
                        var keys = kf.TextKeys.Where(k => SkillTagEffects.IsEventSoundKey(k.text)).ToList();
                        if (keys.Count == 0) continue;
                        voiceRows++;
                        int gender = bname == "male" ? 0 : 1;
                        foreach (var (_, text) in keys)
                        {
                            eventKeys++;
                            keySpellings[text] = keySpellings.GetValueOrDefault(text) + 1;
                            int ev = Ms2.Audio.EventSoundTable.TextKeyType(text);
                            foreach (int g in jobGroup != 0 ? new[] { jobGroup } : JobGroups)
                            {
                                if (voices.VoicePath(ev, gender, g, true, 1, false) != null) ownVoices++;
                                else noVoiceGroups.Add($"{text} gender {gender} job {g}");
                                if (voices.VoicePath(ev, gender, g, false, 3, false) != null) remoteVoices++;
                            }
                        }
                        // the own walk holds each key at its time
                        var walk = new SkillTagEffects.Keys();
                        walk.Set(kf.TextKeys, m.TagEffects);
                        var walked = Enumerable.Range(0, walk.Count).Select(i => walk[i]).Where(e => e.path == null && SkillTagEffects.IsEventSoundKey(e.key)).Select(e => (e.time, e.key)).ToList();
                        if (!walked.SequenceEqual(keys.Select(k => (k.time, k.text))))
                            bad.Add($"| {id} | {lr.Level} | {mi} | voice | own {bname} | {string.Join(",", keys.Select(k => $"{k.text}@{k.time:0.###}"))} | {string.Join(",", walked.Select(k => $"{k.key}@{k.time:0.###}"))} |");
                        // the other PC's state hands each key on
                        var heard = new List<string>();
                        var rpc = new Pc { Kf = kf };
                        var rsm = new RemotePcStates(rpc) { EventSound = heard.Add };
                        rsm.ChangeState(RemotePcStates.StateIdle);
                        rsm.Cast(id, false, Vector3.Zero, 0f, motion: mi, motionCount: level.Motions.Count);
                        foreach (var (t, text) in keys) rsm.CastTextKey(text, (float)t);
                        if (!heard.SequenceEqual(keys.Select(k => k.text)))
                            bad.Add($"| {id} | {lr.Level} | {mi} | voice | remote {bname} | {string.Join(",", keys.Select(k => k.text))} | {string.Join(",", heard)} |");
                    }
                }
            }
        }
        string counts = $"levels {levels}, motions {motions}; motionEffect: motions {withFx} (no sequence, inserted {noSeqFx}; xml missing {fxMissing}; "
            + $"looping {fxLoop}; with a sound {fxSound}; the motion moves the caster {fxMoving}), remote rows {fxRows}; "
            + $"EventSound: motion/body rows {voiceRows}, keys {eventKeys}, own voices {ownVoices}, other PC voices {remoteVoices}; "
            + $"event voice slots {voices.Count}; not ok {bad.Count}";
        var sb = new StringBuilder();
        sb.AppendLine($"# {variable} {data}");
        sb.AppendLine(counts);
        sb.AppendLine("key spellings: " + string.Join(", ", keySpellings.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value}")));
        sb.AppendLine("own keys with no voice: " + (noVoiceGroups.Count == 0 ? "none" : string.Join("; ", noVoiceGroups.OrderBy(s => s).Take(40))));
        if (bad.Count > 0)
        {
            sb.AppendLine("| skill | level | motion | what | side | expected | played |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var b in bad) sb.AppendLine(b);
        }
        var outPath = Environment.GetEnvironmentVariable("MS2_SKILLSEQ_FXVOICE_OUT");
        if (!string.IsNullOrEmpty(outPath)) File.AppendAllText(outPath, sb.ToString());
        TestContext.Progress.WriteLine(sb.ToString());
        Assert.That(levels, Is.GreaterThan(100), "the data has skills");
        Assert.That(voices.Count, Is.GreaterThan(0), "the data has Table/EventSound.xml");
        Assert.That(bad.Take(40), Is.Empty);
    }
}
