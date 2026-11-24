using System.Numerics;
using System.Text;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Formats.World;
using Ms2.Net.Replay;
using Ms2.Ui.Skills;
using NUnit.Framework;
using static Ms2.Net.Tests.SkillSequenceSweepTests;

namespace Ms2.Net.Tests;

/// <summary>
/// The skill sequence order with input, for every PC skill of the skill XML and every level whose motions differ, on both
/// bodies: what a held, pressed or let-go key does to a cast, against the client's order (StateSkill, stateskill.cpp, with
/// MyPC, mypc.cpp). Each scenario runs the client's state as written here from the decomp (<see cref="ClientOwn"/>,
/// <see cref="ClientRemote"/>) and the port's (<see cref="PortOwn"/>: the Ms2.Ui skill parts in the order HudScene.Cast.cs
/// calls them; <see cref="PortRemote"/>: <see cref="RemotePcStates"/> as RemotePc drives it), frame by frame:
/// <list type="bullet">
/// <item>Hold: the cast's key held from the press; let go after loopCount + 2 rounds of a motion. A held key keeps a
/// continue skill's loop (vfunc_8 :649-694), takes a charging skill through charging_combo (:946-957), and gives a record
/// each frame (MyPC::vfunc_104 with bAuto, FUN_141526730 mypc.cpp:1767-1859) that may follow the running cast
/// (FUN_1417eeb10 :2996, FUN_1417eed70 :3024, FUN_1417ef4a0 :3092) or cast at its end (FUN_1417ee120 :2893-2945).</item>
/// <item>Mash: the cast's key pressed every 6th frame (WasKeyPressed) and down for 3 frames, let go in between.</item>
/// <item>Output: the skill's combo output skill (&lt;combo outputSkill&gt;) on another key, tapped the same way.</item>
/// <item>Release: a continue or charging skill's key held until its first round, then let go (vfunc_8 :698-722).</item>
/// </list>
/// "held-no-timer": a refused EndOfSequence with nothing to send the sequence back; the client's state stays (no 5 s end).
/// A run stops at the first cast that follows the running one ("follow:" or "queue:" behind a cooldown, with where it went:
/// the key whose callback chained it, "now" for an immediateActive chain type, "frame" for the per-frame chain, "eos" at the
/// end step), at the end, or after 30 s. The other PC gets the own client run's mode 2 syncs (SendChargeState :1271, the
/// charge and loop flags, OnSkillSync -> FUN_1417ec630 :1419-1488) and motion casts in the same frames, and its run stops
/// ("cut") where the own run followed.
/// Not in the sweep: costs (FUN_141765a70 is taken as castable), the auto change (skills with changeSkill autoChange are
/// counted, not run), feature 0xca (off), the compact pick, the key-down skill's 30 s hold limit, magic hold targets.
/// Both sides use the port's SkillCooltimes (FUN_141525ff0, FUN_1417f16f0, FUN_141765fe0) for the cooldowns.
/// MS2_SKILLSEQ_INPUT_OUT names a file the table is written to.
/// </summary>
[NonParallelizable]
public class SkillSequenceInputSweepTests
{
    // whole-ms frames: the port reads each key a frame after the client (see MarkGaps), and a 16.667 ms frame on a whole-ms
    // clock steps 16 or 17 ms, which would put a cooldown 1 ms over on one side only
    const double FrameMs = 16.0;
    const float Dt = 0.016f;
    const double KeysFromStart = Ms2.Net.Replay.SkillMotion.KeysFromStart;
    const int MaxFrames = 60 * 30;

    [Test] public void KmsSkillInputPlaysInTheClientsOrder() => Sweep("MS2_DATA");
    [Test] public void Gms2SkillInputPlaysInTheClientsOrder() => Sweep("MS2_GMS2_DATA");
    [Test] public void KmsWeaponSequenceNames() => WeaponSequences("MS2_DATA");
    [Test] public void Gms2WeaponSequenceNames() => WeaponSequences("MS2_GMS2_DATA");

    /// <summary>
    /// A motion's weaponSequenceName (SkillMotionData, skillleveldata.cpp:1179), which the port reads nowhere: while the PC's
    /// skill state runs that motion, PC::FUN_141658d20 (pc.cpp:1779-1797) gives it to PC::vfunc_136 -> CEquipment::
    /// FUN_1411e8b60 (equipment.cpp:1831-1844), which plays it on each worn item's kfm through CKfmModel::
    /// PlayAnimationWithFallback (kfmmodel.cpp:853-880): the name itself, else vfunc_60's prefix (the weapon action's
    /// prefix or the set name, pc.cpp:1737-1747) and the body's animation, else the body's animation, else Idle_A.
    /// For every motion that names one: the skill's condition weapons (&lt;weapon lh rh&gt;) and whether their item kfms
    /// (Resource/Model/Item, the file names 1 + the two-digit category) have a sequence of that name, or one ending in
    /// "_" + it. A report, not a check of the port: MS2_SKILLSEQ_INPUT_OUT gets the counts.
    /// </summary>
    static void WeaponSequences(string variable)
    {
        string data = DataDir(variable);
        using var packs = new PackSet(data);
        var entries = packs.List("Xml").Where(n => n.StartsWith("skilldata/", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).ToList();
        if (entries.Count == 0) entries = packs.List("Xml").Where(n => SkillTable.PerSkillId(n) != null).ToList();
        var motions = new List<(int skill, string seq, string weapons)>();
        foreach (var entry in entries)
        {
            System.Xml.Linq.XDocument x;
            string fileId = SkillTable.PerSkillId(entry);
            try
            {
                if (fileId != null) { x = System.Xml.Linq.XDocument.Parse(PackXml.Text(packs.Read("Xml", entry))); SkillTable.ToSkillRows(x); }
                else x = PackXml.Parse(packs.Read("Xml", entry));
            }
            catch (System.Xml.XmlException) { continue; }
            foreach (var sk in fileId != null ? new[] { x.Root } : x.Descendants("skill"))
            {
                if (sk == null || !int.TryParse(fileId ?? (string)sk.Attribute("id"), out int id)) continue;
                string weapons = string.Join(",", sk.Descendants("weapon").Select(w => $"{(string)w.Attribute("lh")}/{(string)w.Attribute("rh")}").Distinct());
                foreach (var m in sk.Descendants("motion"))
                    if ((string)m.Attribute("weaponSequenceName") is { Length: > 0 } seq) motions.Add((id, seq, weapons));
            }
        }
        const string items = "Resource/Model/Item";
        var kfms = packs.List(items).Where(n => n.EndsWith(".kfm", StringComparison.OrdinalIgnoreCase)).ToList();
        var byCat = new Dictionary<string, List<KfmFile>>();
        List<KfmFile> Kfms(string cat)
        {
            if (byCat.TryGetValue(cat, out var l)) return l;
            l = new List<KfmFile>();
            foreach (var n in kfms.Where(n => Path.GetFileName(n).StartsWith("1" + cat, StringComparison.Ordinal)))
                try { l.Add(KfmFile.Load(packs.Read(items, n))); } catch (InvalidDataException) { }
            return byCat[cat] = l;
        }
        var sb = new StringBuilder();
        sb.AppendLine($"# weaponSequenceName {variable} {data}");
        sb.AppendLine($"motions naming one: {motions.Count}, skills {motions.Select(m => m.skill).Distinct().Count()}, jobs (id / 100000) "
            + string.Join(",", motions.Select(m => m.skill / 100000).Distinct().OrderBy(j => j)) + $"; item kfms {kfms.Count}");
        foreach (var g in motions.GroupBy(m => (m.seq, m.weapons)))
        {
            var cats = g.Key.weapons.Split(',', StringSplitOptions.RemoveEmptyEntries).SelectMany(w => w.Split('/')).Where(c => c is { Length: > 0 } && c != "0").Distinct().ToList();
            var ks = cats.SelectMany(c => Kfms(c.PadLeft(2, '0'))).ToList();
            int exact = ks.Count(k => k.ByName(g.Key.seq) != null);
            int suffix = ks.Count(k => k.Sequences.Any(q => q.Name.EndsWith("_" + g.Key.seq, StringComparison.OrdinalIgnoreCase)));
            var names = ks.SelectMany(k => k.Sequences.Select(q => q.Name)).Distinct().OrderBy(n => n).Take(8);
            sb.AppendLine($"- {g.Key.seq} on weapons {g.Key.weapons}: {g.Count()} motions of {g.Select(m => m.skill).Distinct().Count()} skills; "
                + $"{ks.Count} kfms, the name in {exact}, a name ending in _{g.Key.seq} in {suffix}; their sequences {string.Join(" ", names)}");
        }
        var outPath = Environment.GetEnvironmentVariable("MS2_SKILLSEQ_INPUT_OUT");
        if (!string.IsNullOrEmpty(outPath)) File.AppendAllText(outPath, sb.ToString());
        TestContext.Progress.WriteLine(sb.ToString());
        Assert.That(entries, Is.Not.Empty, "the data has skill XML");
    }

    enum Scenario { Hold, Mash, Output, Release }

    static bool NearlyZero(float f) => MathF.Abs(f) < 1e-06f;

    /// <summary>StateSkill::ClampedElapsedAfter16ms @0x1417f1a10: the time past <paramref name="start"/> less 16 ms, at most 50.</summary>
    static uint Clamped(uint now, uint start)
    {
        uint e = now > start ? now - start : 0;
        return e > 16 ? Math.Min(e - 16, 50u) : 0;
    }

    /// <summary>The scenario's keys in a frame: key 1 the cast's, key 2 the combo output skill's.</summary>
    sealed class Keys
    {
        public Scenario S;
        public int Frame;
        public bool Released;
        public bool CastPressed => S == Scenario.Mash && Frame % 6 == 0;
        public bool CastDown => S switch { Scenario.Hold or Scenario.Release => !Released, Scenario.Mash => Frame % 6 < 3, _ => false };
        public bool OutPressed => S == Scenario.Output && Frame % 6 == 0;
        public bool OutDown => S == Scenario.Output && Frame % 6 < 3;
    }

    sealed class Ctx
    {
        public SkillData Skill, Out;
        public SkillLevelData Level, OutLevel;
        public BodyKfs Body;
        public Func<uint, SkillData> Skills;
        public bool Jc01;
        public bool IsOutput(uint id) => Skill.ComboInputSkill != 0 && Skill.ComboOutputSkill == id;   // Unk_SkillBlockA4::IsOutputSkill
    }

    /// <summary>What the own client run sent the other PC: its motion casts (OnSkillUse) and its mode 2 syncs, by frame.</summary>
    sealed class Stream
    {
        public readonly List<(int frame, int motion)> Starts = new();
        public readonly List<(int frame, bool charging, bool loop)> Syncs = new();
        public int EndFrame = -1;
        public bool Followed;
    }

    interface IOwn
    {
        List<string> Out { get; }
        bool Done { get; }
        int Loops { get; }
        int LoopsInMotion { get; }
        int MotionLoopCount { get; }
        void Step(Keys k);
    }

    // ---- the client's own PC (StateSkill + MyPC), from the decomp ------------------------------------------------------

    sealed record Cand(SkillData Skill, SkillLevelData Level, string Who);

    sealed class ClientOwn : IOwn
    {
        readonly Ctx c;
        readonly SkillCooltimes cool;
        public List<string> Out { get; } = new();
        public readonly Stream St = new();
        /// <summary>The token count when EndOfSequence was first refused (-1: never).</summary>
        public int RefusedAt = -1;
        /// <summary>The sequence time the keys were read up to when a cast followed.</summary>
        public double FollowPos { get; private set; } = double.NaN;
        public float Speed => speed;
        public bool Done { get; private set; }
        public int Loops { get; private set; }
        public int LoopsInMotion { get; private set; }
        public int MotionLoopCount { get; private set; }
        double timeMs;
        uint Clock => 100000 + (uint)timeMs;
        int frame;

        int motion;
        bool attackOne;                  // the record's m_nAttack = 1 (FUN_141759fc0, skillrecord.cpp:352)
        CastRecord rec;
        KfAnimation kf;
        double pos, keyPos;
        float speed;
        bool atEnd;
        int repeat, attack;
        bool charging, loop, b117, a5, b111, b4a, b114, b115, b49, b116, b112, b48;
        float chargeTime, loopStart, loopStartKey, loopEndKey;
        uint loopStartTick, chargeTick, stateStart, endTick, waitFrom, chargingComboTick;
        Cand next, pressQueued;
        int chainType;
        bool roundQueued, nextAnnounced, sentAny, sentCharging, sentLoop;

        public ClientOwn(Ctx c)
        {
            this.c = c;
            cool = new SkillCooltimes(c.Skills);
            Enter(0, 0f);
        }

        bool IsLastMotion => motion + 1 >= c.Level.Motions.Count;
        bool RecordDone => motion + (attackOne ? 1 : 0) >= c.Level.Motions.Count;   // FUN_141759e40 (skillrecord.cpp:315)

        void Stop(string token)
        {
            Out.Add(token);
            Done = true;
            St.EndFrame = frame;
        }

        /// <summary>StateSkill::vfunc_4 (:390-500), FUN_1417f0940 (:2206-2251), AnimActivated (:742-775), then MyPC's
        /// cooldown (FUN_141525ff0, mypc.cpp:1230) and charge use (:1232).</summary>
        void Enter(int m, float elapsed)
        {
            motion = m;
            attackOne = false;
            rec = new CastRecord { Skill = c.Skill, Level = c.Level, Motion = (byte)m, CastId = 1, Tick = (int)Clock, SendTick = (int)Clock };
            var mo = c.Level.Motions[m];
            b48 = b49 = b4a = b112 = b114 = b115 = b116 = b117 = false;
            charging = loop = false;
            a5 = true;
            chargeTime = 0f;
            next = null;
            chainType = 0;
            roundQueued = false;
            nextAnnounced = false;
            attack = 0;
            repeat = mo.LoopCount;
            MotionLoopCount = mo.LoopCount;
            LoopsInMotion = 0;
            kf = string.IsNullOrEmpty(mo.SequenceName) ? null : c.Body.Get(mo.SequenceName);
            if (kf == null) { Stop(string.IsNullOrEmpty(mo.SequenceName) ? "noseq" : "missing:" + mo.SequenceName); return; }
            Out.Add("play:" + mo.SequenceName.ToLowerInvariant());
            St.Starts.Add((frame, m));
            pos = 0;
            keyPos = KeysFromStart;
            atEnd = false;
            speed = Math.Max(mo.SequenceSpeed, 0.1f);
            loopStartKey = Key(kf, "loopstart");
            loopEndKey = Key(kf, "loopend");
            b111 = c.Skill.ChargingSkill;
            stateStart = Clock;
            float cc = Key(kf, "charging_combo");
            chargingComboTick = stateStart + (uint)(long)(cc / speed * 1000f);
            cool.StartCast(rec, kf, elapsed, Clock);
            cool.OnRechargeSkillUsed(c.Skill, Clock);
        }

        void Seek(float t)
        {
            pos = t;
            keyPos = KeysFromStart;
            atEnd = false;
        }

        public void Step(Keys k)
        {
            frame = k.Frame;
            timeMs += FrameMs;
            // WorldService::OnTick (priority 2001, ms2.cpp:1024) -> MyPC::vfunc_53 (mypc.cpp:2942-2966): PC::vfunc_53 ->
            // PCBase::vfunc_53 -> CStateManager::Update (statemanager.cpp:224-233) runs the state's vfunc_3, then vfunc_8;
            // the animation service (priority 1785, ms2.cpp:998) ticks after it: the text keys and EndOfSequence
            Update(k);          // StateSkill::vfunc_3: FUN_1417ee120, FUN_1417f11d0
            if (Done) return;
            Input(k);           // StateSkill::vfunc_8
            if (Done) return;
            Anim(k);            // the actor manager: the text keys, EndOfSequence
            Trace?.Invoke($"c f{frame} pos {pos:0.###} down {(k.CastDown ? 1 : 0)} charging {(charging ? 1 : 0)} loop {(loop ? 1 : 0)} repeat {repeat} chargeTime {chargeTime:0.##} | {string.Join(" ", Out)}");
        }
        public Action<string> Trace;

        Cand Self() => new(c.Skill, c.Level, "self");
        Cand Output() => new(c.Out, c.OutLevel, "out");
        static bool PressOnly(SkillData s) => (s.IsItemKind || s.RechargeMaxCount != 0 || s.ImmediateActive != 0) && !s.KeyIsDown;

        /// <summary>MyPC::vfunc_104 -> FUN_141526730 with bAuto (mypc.cpp:1767-1859): a key just pressed, else a key down
        /// (a press-only skill only when just pressed).</summary>
        Cand Poll(Keys k)
        {
            if (k.CastPressed) return Self();
            if (k.OutPressed && c.Out != null) return Output();
            if (k.CastDown && !PressOnly(c.Skill)) return Self();
            if (k.OutDown && c.Out != null && !PressOnly(c.Out)) return Output();
            return null;
        }

        void Input(Keys k)
        {
            // FUN_1417eeb10 (:640-641, :2996-3016)
            if (!RecordDone && Poll(k) is { } n && MayFollow(n, k))
            {
                next = n;
                if (chainType != 0) TryChain("now");
                if (Done) return;
            }
            // the continue hold check (:649-694); FUN_141765a70 castable
            if (a5 && c.Skill.Continue && NearlyZero(chargeTime))
            {
                charging = false;
                if (k.CastDown)
                {
                    bool loopKeys = loopStartKey >= 0 && loopEndKey >= 0;
                    if (b111) { if (loopKeys) charging = repeat > 0; }
                    else if (loopKeys) charging = true;
                }
            }
            // the charging release (:698-722)
            if (b111 && !loop && !k.CastDown)
            {
                charging = false;
                repeat = 0;
                if (!c.Skill.DisableChargingAttackSkipFrame)
                {
                    float at = Key(kf, "charging_attack");
                    if (at < 0) at = (float)kf.Duration;   // FUN_141b6fe90 without the key: as the port reads it
                    Seek(at);
                    Out.Add("skip");
                    loop = true;
                    b112 = false;
                    b117 = false;
                    loopStart = at;
                }
                return;
            }
            // SendChargeState (:1271-1303)
            if (!sentAny || sentCharging != charging || sentLoop != loop)
            {
                sentAny = true;
                sentCharging = charging;
                sentLoop = loop;
                St.Syncs.Add((frame, charging, loop));
            }
        }

        /// <summary>FUN_1417eed70 @0x1417eed70 (:3024-3083).</summary>
        bool MayFollow(Cand n, Keys k)
        {
            var s = c.Skill;
            bool same = n.Skill.Id == s.Id;
            if (!n.Skill.KeyIsDown && same) return false;
            chainType = n.Skill.ImmediateActive;
            if (chainType == 1) return !same;
            if (chainType == 2) return n.Skill.OriginId != s.OriginId;
            if (!IsLastMotion) return false;
            if (!b4a && !b114) return false;
            if (s.Continue && (!s.ChargingSkill || NearlyZero(chargeTime)) && k.CastDown) return false;
            var mo = c.Level.Motions[motion];
            if (!string.IsNullOrEmpty(mo.SelfInputCombo))
            {
                if (!b114)
                {
                    if (same) return false;
                    if (c.Jc01 && c.IsOutput(n.Skill.Id)) return false;
                }
                else if (!b4a && !same) return false;
            }
            if (b115 && (same || c.IsOutput(n.Skill.Id))) return false;
            return true;
        }

        /// <summary>FUN_1417ef4a0 @0x1417ef4a0 (:3092-3184), without the auto change veto (m_b11e).</summary>
        void TryChain(string at)
        {
            if (!b49 && !b116 && chainType == 0 && !b112) return;
            if (!IsLastMotion) return;   // IsEndedOrLastMotion (skillrecord.cpp:326)
            if (next == null) return;
            bool chain = true;
            bool same = next.Skill.Id == c.Skill.Id, outp = c.IsOutput(next.Skill.Id);
            var mo = c.Level.Motions[motion];
            if (!string.IsNullOrEmpty(mo.SelfInputCombo))
            {
                if ((b49 || b112) && !b116)
                {
                    if (same || (c.Jc01 && outp)) chain = false;
                }
                else if (!b49 && b116)
                {
                    if (!same && (!c.Jc01 || !outp)) chain = false;
                }
            }
            if (b111 && (b112 || b49))
            {
                if (same) chain = false;
                else if (loop && !b112 && outp) chain = false;
            }
            if (b115 && (same || outp)) chain = false;
            if (!chain) return;
            if (next.Level.HasSequenceName) attackOne = true;
            Use(next, at);
            next = null;
        }

        /// <summary>MyPC::vfunc_102 (mypc.cpp:1146-1236) for the following cast: queued behind the state while cooling
        /// (SetChargeRecord, :1152-1166), else cast. The run stops there.</summary>
        void Use(Cand n, string at)
        {
            // a cooldown over within the frame is no wait: FUN_1417f11d0 casts the queued record in the next vfunc_3
            bool cooling = !cool.CanUseRechargeSkill(n.Skill, Clock) && cool.Remain(n.Skill.Id, false, Clock) * 1000f > FrameMs;
            St.Followed = true;
            FollowPos = keyPos;
            Stop((cooling ? "queue:" : "follow:") + n.Who + (n.Level.HasSequenceName ? "" : ":noseq") + "@" + at);
        }

        void Update(Keys k)
        {
            EndStep(k);          // FUN_1417ee120 (:623)
            if (Done) return;
            ReleaseQueued();     // FUN_1417f11d0 (:624)
        }

        bool Cooling(SkillData s) => !cool.CanUseRechargeSkill(s, Clock) && cool.Remain(s.Id, false, Clock) > 0f;

        /// <summary>FUN_1417ee120 @0x1417ee120 (:2885-2994).</summary>
        void EndStep(Keys k)
        {
            if (!b48) { TryChain("frame"); return; }   // :2889-2893, before the 5 s test
            attack = 0;
            attackOne = true;
            if (RecordDone)
            {
                // :2898-2958: the next cast, else a new record of the key polled now (vfunc_104(1, 1, 1), mypc.cpp:1671:
                // CSkillSystem's pending id, else the key); none ends the state. vfunc_102 queues a record still cooling
                // (SetChargeRecord, mypc.cpp:1152-1166, in place of a queued round) and the state stays
                next ??= Poll(k);
                if (next == null || (b111 && c.IsOutput(next.Skill.Id))) { Stop("end"); return; }
                if (!Cooling(next.Skill) || cool.Remain(next.Skill.Id, false, Clock) * 1000f <= FrameMs) { Use(next, "eos"); return; }
                if (pressQueued == null) Out.Add("queue:" + next.Who + "@eos");
                pressQueued = next;
                roundQueued = false;
                next = null;
            }
            else
            {
                // :2965-2980: the next motion's record through vfunc_102, queued while cooling; FUN_1417f11d0 casts it
                // in the frame its cooldown is over
                if (!nextAnnounced) { nextAnnounced = true; Out.Add("next"); waitFrom = Clock; }
                if (!Cooling(c.Skill))
                {
                    if (Clock - waitFrom > 100) Out.Add("wait");
                    Enter(motion + 1, (Clock - stateStart) * 0.001f);
                    return;
                }
            }
            if (Clock - endTick > 5000) Stop("timeout");   // :2990
        }

        /// <summary>FUN_1417f11d0 @0x1417f11d0 (:1578-1620): the queued record goes once it can recharge or its cooldown is
        /// 0: a continue round (m_n98) plays the next round, any other record is cast (vfunc_102).</summary>
        void ReleaseQueued()
        {
            if (pressQueued != null)
            {
                if (cool.CanUseRechargeSkill(pressQueued.Skill, Clock) || NearlyZero(cool.Remain(pressQueued.Skill.Id, false, Clock)))
                {
                    var n = pressQueued;
                    pressQueued = null;
                    Use(n, "queued");
                }
                return;
            }
            if (roundQueued && (cool.CanUseRechargeSkill(c.Skill, Clock) || NearlyZero(cool.Remain(c.Skill.Id, false, Clock))))
            {
                roundQueued = false;
                SendRound();
                Seek(loopStart + chargeTick * 0.001f);
                Round();
            }
        }

        void Round()
        {
            Out.Add("loop");
            Loops++;
            LoopsInMotion++;
        }

        /// <summary>A round's sends (:921-927, :1591-1595): FUN_1417f16f0 (:1640-1664), a continue skill's cooldown for the
        /// loop at the speed less the charge tick (not a charging skill's, m_n98 is set), and the charge use.</summary>
        void SendRound()
        {
            if (c.Skill.Continue && !b111)
                cool.Start(Clock, c.Skill.Id, Math.Max(0f, (loopEndKey - loopStartKey) / speed - chargeTick * 0.001f), 0f, true, Clock);
            cool.OnRechargeSkillUsed(c.Skill, Clock);
        }

        void Anim(Keys k)
        {
            if (atEnd) return;
            pos = Math.Min(pos + Dt * speed, kf.Duration);
            bool finished = pos >= kf.Duration;
            double prev = keyPos, upTo = finished ? Math.Max(keyPos, kf.Duration) : pos;
            keyPos = upTo;
            foreach (var (t, name) in kf.TextKeys)
            {
                if (!(prev < t && t <= upTo)) continue;
                bool seeked = TextKey(name, (float)t, k);
                if (Done || seeked) return;
            }
            if (finished) EndOfSequence();
        }

        /// <summary>TextKeyEvent @0x1417e7660 (:778-1051) for one key; true when the sequence was sent back (a next round).</summary>
        bool TextKey(string key, float t, Keys k)
        {
            var mo = c.Level.Motions[motion];
            var pAttack = attack < mo.Attacks.Count ? mo.Attacks[attack] : null;   // GetAttackDataAt(m_nAttack) (:790)
            if (key == "loopstart")
            {
                b117 = true;
                if (!loop) { loopStart = t; chargeTime = 0f; b112 = false; loopStartTick = Clock; }
            }
            if (key == "loopend")
            {
                chargeTime = t;
                b117 = false;
                if (charging || repeat > 0)
                {
                    if (repeat > 0) repeat--;
                    attack = 0;
                    charging = false;
                    b112 = false;
                    b117 = true;
                    b49 = false;
                    uint loopMs = (uint)(long)((t - loopStart) / speed * 1000f);
                    chargeTick = Clamped(Clock - loopStartTick, loopMs);
                    if (loopMs - 5u < chargeTick) chargeTick = loopMs - 5u;
                    rec.Charged = true;   // m_n98
                    bool ready = cool.CanUseRechargeSkill(c.Skill, Clock);
                    float cd = cool.Remain(c.Skill.Id, false, Clock);
                    if (!ready && cd > 0f && !b111) roundQueued = true;   // SetChargeRecord (:916)
                    else SendRound();
                    if (!roundQueued)
                    {
                        Seek(loopStart + chargeTick * 0.001f);
                        Round();
                        return true;
                    }
                }
            }
            if (b111 && repeat == 0 && !loop && key == "charging_combo" && k.CastDown)
            {
                chargeTick = Clamped(Clock, chargingComboTick);
                b112 = true;
                loop = true;
                b117 = false;
                TryChain("charging_combo");
                if (Done) return false;
            }
            if (pAttack != null && key == pAttack.Point)
            {
                if (b111 && !loop && repeat == 0) { loop = true; b112 = false; }
                while (attack < mo.Attacks.Count && mo.Attacks[attack].Point == key) attack++;   // FireAttacksAtPoint
                return false;
            }
            if (key == "charging_attack")
            {
                if (b111 && !loop && repeat == 0) { loop = true; b112 = false; b117 = false; }
            }
            else if (key == "combo")
            {
                if (!(b111 && (b112 || repeat != 0)))   // CanChainCombo (:1725)
                {
                    b49 = true;
                    b117 = false;
                    TryChain("combo");
                }
            }
            else if (key == "inputcombo") b4a = true;
            else if (string.Equals(key, "endInputCombo", StringComparison.OrdinalIgnoreCase)) b4a = false;
            else if (!string.IsNullOrEmpty(mo.SelfInputCombo) && key == mo.SelfInputCombo) b114 = true;
            else if (string.Equals(key, "endSelfInputcombo", StringComparison.OrdinalIgnoreCase)) b115 = true;
            else if (key == "selfcombo")
            {
                b116 = true;
                b117 = false;
                TryChain("selfcombo");
            }
            return false;
        }

        /// <summary>EndOfSequence @0x1417e9130 (:1054-1071): refused while charging or with a repeat left; a refused end
        /// with nothing to send the sequence back holds the state.</summary>
        void EndOfSequence()
        {
            atEnd = true;
            if (charging || repeat > 0)
            {
                // FUN_1417ee120 returns before its 5 s test while m_b48 is unset (stateskill.cpp:2889-2893, :2990): the state
                // holds with no timer until a chain, the queued round, a hit, death or a cancel
                if (RefusedAt < 0) RefusedAt = Out.Count;
                if (!roundQueued) Stop("held-no-timer");
                return;
            }
            b116 = b49 = b112 = b48 = loop = true;
            attack = 0;
            endTick = Clock;
        }
    }

    // ---- the port's own PC: the Ms2.Ui skill parts in HudScene.Cast.cs's order ----------------------------------------

    sealed class PortOwn : IOwn
    {
        readonly Ctx c;
        readonly SkillCooltimes cool;
        public List<string> Out { get; } = new();
        public bool Done { get; private set; }
        public int Loops { get; private set; }
        public int LoopsInMotion { get; private set; }
        public int MotionLoopCount { get; private set; }
        public double FollowPos { get; private set; } = double.NaN;
        double timeMs;
        uint Clock => 100000 + (uint)timeMs;

        CastRecord ownCast, ownNext, ownQueued, framePress;
        readonly SkillLoop ownLoop = new();
        SkillFollow.Window ownWindow;
        readonly SkillFollow.ComboKeys ownComboKeys = new();
        readonly HeldKeyPoll heldKeys = new();
        readonly List<int> bound = new();
        int ownChainType, ownAttack;
        bool ownEnded, atEnd, nextAnnounced, pressedThisFrame;
        uint ownEndMs, ownStateStartMs, waitFrom, ownChargingComboTick;
        KfAnimation kf;
        double pos, ownKeyPos, ownPointTime;
        float speed, ownLoopStartKey, ownLoopEndKey, ownChargingAttackKey, ownChargingComboKey;
        Keys keys;
        long castIds = 1;

        public PortOwn(Ctx c)
        {
            this.c = c;
            cool = new SkillCooltimes(c.Skills);
            bound.Add(1);
            if (c.Out != null) bound.Add(2);
            UseOwnRecord(new CastRecord { Skill = c.Skill, Level = c.Level, Motion = 0, CastId = castIds, Key = 1, Tick = (int)Clock, SendTick = (int)Clock }, null);
        }

        bool StateRuns => ownCast != null && !Done;
        SkillData SkillOf(int dik) => dik == 1 ? c.Skill : dik == 2 ? c.Out : null;
        bool IsDown(int dik) => dik == 1 ? keys.CastDown : dik == 2 && keys.OutDown;

        // SkillCast's records carry the request's tick as their send tick (SkillCast.cs:362), which StartCast starts the
        // cooldown from
        CastRecord Request(int dik) => new()
        {
            Skill = SkillOf(dik), Level = dik == 1 ? c.Level : c.OutLevel, Motion = 0, CastId = ++castIds, Key = dik,
            Tick = (int)Clock, SendTick = (int)Clock,
        };

        void Stop(string token)
        {
            Out.Add(token);
            Done = true;
        }

        /// <summary>HudScene.UseOwnRecord: the cooldown gate (SkillCast.CooldownGate), then the cast; a following cast
        /// (<paramref name="at"/> set) stops the run.</summary>
        bool UseOwnRecord(CastRecord r, string at)
        {
            var gate = SkillCast.CooldownGate(cool, r, StateRuns, Clock);
            if (at != null)
            {
                // as the client's: a queue whose cooldown is over within the frame goes in the next TickOwnQueued
                bool wait = gate != CoolGate.Cast && cool.Remain(r.Skill.Id, false, Clock) * 1000f > FrameMs;
                if (wait && at == "eos" && gate == CoolGate.Queued)
                {
                    // queued behind the ended state, which stays until TickOwnQueued casts it or the 5 s timeout
                    if (ownQueued == null || ownQueued.Charged) Out.Add("queue:" + Who(r) + "@eos");
                    ownQueued = r;
                    r.QueuedCharge = true;
                    return true;
                }
                FollowPos = ownKeyPos;
                Stop((wait ? "queue:" : "follow:") + Who(r) + (r.Level.HasSequenceName ? "" : ":noseq") + "@" + at);
                return true;
            }
            if (gate == CoolGate.Queued) { ownQueued = r; r.QueuedCharge = true; }
            if (gate != CoolGate.Cast) return gate == CoolGate.Queued;
            float elapsed = StateRuns ? (Clock - ownStateStartMs) * 0.001f : 0f;
            var mo = r.Level.Motions[r.Motion];
            kf = string.IsNullOrEmpty(mo.SequenceName) ? null : c.Body.Get(mo.SequenceName);
            if (kf == null) { Stop(string.IsNullOrEmpty(mo.SequenceName) ? "noseq" : "missing:" + mo.SequenceName); return true; }
            if (r.Motion > 0 && Clock - waitFrom > 100) Out.Add("wait");
            Out.Add("play:" + mo.SequenceName.ToLowerInvariant());
            StartOwnAttacks(r);
            cool.StartCast(r, kf, elapsed, Clock);
            cool.OnRechargeSkillUsed(r.Skill, Clock);
            return true;
        }

        string Who(CastRecord r) => (object)r.Skill == c.Skill ? "self" : "out";

        /// <summary>HudScene.StartOwnAttacks.</summary>
        void StartOwnAttacks(CastRecord r)
        {
            ownCast = r;
            ownQueued = null;
            ownChainType = 0;
            ownNext = null;
            ownWindow = default;
            var mo = r.Level.Motions[r.Motion];
            ownComboKeys.Set(kf.TextKeys, mo.SelfInputCombo);
            ownEnded = false;
            nextAnnounced = false;
            atEnd = false;
            pos = 0;
            speed = SkillCast.MotionSpeedScale(r);
            ownAttack = 0;
            ownStateStartMs = Clock;
            ownLoop.Reset();
            ownLoop.StartSequence(mo.LoopCount);
            MotionLoopCount = mo.LoopCount;
            LoopsInMotion = 0;
            ownLoopStartKey = Key(kf, "loopstart");
            ownLoopEndKey = Key(kf, "loopend");
            ownChargingAttackKey = Key(kf, "charging_attack");
            ownChargingComboKey = Key(kf, "charging_combo");
            ownChargingComboTick = ownStateStartMs + (uint)(long)(ownChargingComboKey / speed * 1000.0f);
            ownKeyPos = KeysFromStart;
            ownPointTime = PointTime();
        }

        double PointTime() => SkillAttackFire.AttackAt(ownCast, ownAttack) is { Point.Length: > 0 } a ? Key(kf, a.Point) : -1;

        public void Step(Keys k)
        {
            keys = k;
            timeMs += FrameMs;
            Press();                          // CastKeys -> RouteOwn: the input events come before the frame's process
            if (Done) return;
            // Player._Process (ProcessPriority -1, Player.cs:199) first: SkillHoldsMove sees the sequence the AnimationPlayer
            // stopped at its end last frame and runs the end step (Player.SkillSequenceEnding -> OwnSequenceEnding)
            if (!atEnd && pos >= kf.Duration && !OwnSequenceEnding()) return;
            if (Done) return;
            // HudScene._Process (HudScene.cs:3954-3956): priority 0 and the field's parent (HudScene.cs:129, Field.cs:698), so
            // before the player's AnimationPlayer; its keys read up to where the AnimationPlayer got last frame
            TickOwnQueued();
            if (Done) return;
            TickOwnAttacks();
            if (Done) return;
            TickOwnSync();
            // the AnimationPlayer (priority 0, under the field): the sequence moves on, from a seek made above too
            if (!atEnd) pos = Math.Min(pos + Dt * speed, kf.Duration);
            Trace?.Invoke($"p f{keys.Frame} pos {pos:0.###} down {(keys.CastDown ? 1 : 0)} charging {(ownLoop.Charging ? 1 : 0)} loop {(ownLoop.Loop ? 1 : 0)} repeat {ownLoop.Repeat} chargeTime {ownLoop.ChargeTime:0.##} | {string.Join(" ", Out)}");
        }
        public Action<string> Trace;

        /// <summary>HudScene.CastKeys -> KeyRequest -> RouteOwn for a key pressed this frame.</summary>
        void Press()
        {
            int dik = keys.CastPressed ? 1 : keys.OutPressed && c.Out != null ? 2 : 0;
            pressedThisFrame = dik != 0;
            framePress = null;
            if (dik == 0 || !StateRuns) return;
            var r = framePress = Request(dik);
            if (ownEnded) ownNext = r;
            else OwnPress(r);
        }

        /// <summary>HudScene.OwnPress.</summary>
        bool OwnPress(CastRecord r)
        {
            var w = ownWindow;
            w.KeyHeld = keys.CastDown;
            w.ChargeTimeZero = NearlyZero(ownLoop.ChargeTime);
            if (!SkillFollow.MayFollow(ownCast, r.Skill, w, c.Jc01, ref ownChainType)) return false;
            ownNext = r;
            if (ownChainType != 0) TryChainOwn(ownCast, "now");
            return true;
        }

        int PollKey() => heldKeys.Pick(bound, keys.CastDown || keys.OutDown, IsDown, _ => false, SkillOf, true, Clock);

        /// <summary>HudScene.PollOwnPress (the compact pick and the auto change are not swept).</summary>
        void PollOwnPress()
        {
            if (pressedThisFrame) return;
            int dik = PollKey();
            if (dik == 0) return;
            var s = SkillOf(dik);
            if (ownNext != null && ownNext.Key == dik && (object)ownNext.Skill == s) return;
            OwnPress(Request(dik));
        }

        /// <summary>HudScene.TryChainOwn (the auto change veto is not swept).</summary>
        void TryChainOwn(CastRecord running, string at)
        {
            if (ownNext == null) return;
            if (!SkillFollow.ChainsNow(running, ownNext.Skill, ownChainType, ownWindow, c.Jc01, ownLoop.Loop)) return;
            var r = ownNext;
            ownNext = null;
            r.HasSequence = r.Level.HasSequenceName;
            UseOwnRecord(r, at);
        }

        /// <summary>HudScene.OwnSequenceEnding. False when the run stopped or the state left.</summary>
        bool OwnSequenceEnding()
        {
            if (!OwnKeys(Math.Max(ownKeyPos, kf.Duration))) return !Done;
            if (ownLoop.HoldsEnd)
            {
                // EndOfSequence :1056-1060: the body holds the last frame (Player.HoldSkillSequence) with no end timer; with no
                // queued round left to cast nothing sends the sequence back
                atEnd = true;
                if (ownQueued == null) { Stop("held-no-timer"); return false; }
                return true;
            }
            ownWindow.EndOfSequence();
            ownEnded = true;
            atEnd = true;
            ownEndMs = Clock;
            if (OwnEndStep()) return !Done;
            Stop("end");
            return false;
        }

        /// <summary>HudScene.OwnEndStep.</summary>
        bool OwnEndStep()
        {
            var running = ownCast;
            if (!SkillFollow.IsLastMotion(running)) return OwnNextMotion(running);
            var next = ownNext ?? OwnEndPoll();
            ownNext = null;
            if (next == null) return false;
            if (next == ownQueued) return true;
            if (!SkillFollow.CastsAtEnd(running, next.Skill)) return false;
            next.HasSequence = next.Level.HasSequenceName;
            return UseOwnRecord(next, "eos");
        }

        /// <summary>HudScene.OwnNextMotion.</summary>
        bool OwnNextMotion(CastRecord running)
        {
            if (!nextAnnounced) { nextAnnounced = true; Out.Add("next"); waitFrom = Clock; }
            var next = SkillCast.NextMotion(running, Vector3.Zero, (int)Clock);
            if (next == null) { Stop("noinit"); return true; }
            return UseOwnRecord(next, null);
        }

        /// <summary>HudScene.OwnEndPoll.</summary>
        CastRecord OwnEndPoll()
        {
            if (pressedThisFrame) return framePress;
            int dik = PollKey();
            if (dik == 0) return null;
            var s = SkillOf(dik);
            if (ownQueued != null && ownQueued.Key == dik && (object)ownQueued.Skill == s && !ownQueued.Charged)
                return ownQueued;
            return Request(dik);
        }

        /// <summary>HudScene.TickOwnEnded.</summary>
        void TickOwnEnded()
        {
            var running = ownCast;
            if (Clock - ownEndMs > 5000) { Stop("timeout"); return; }
            if (!OwnEndStep() && ownCast == running && !Done) Stop("end");
        }

        /// <summary>HudScene.TickOwnQueued.</summary>
        void TickOwnQueued()
        {
            if (ownQueued == null) return;
            if (!SkillCast.QueuedReady(cool, ownQueued, Clock)) return;
            var r = ownQueued;
            ownQueued = null;
            r.QueuedCharge = false;
            if (r.Charged)
            {
                SendOwnRound(r);
                SeekOwnLoop();
                return;
            }
            UseOwnRecord(r, ownEnded ? "queued" : null);
        }

        /// <summary>HudScene.TickOwnAttacks.</summary>
        void TickOwnAttacks()
        {
            if (ownEnded) { TickOwnEnded(); return; }
            if (!OwnKeys(pos)) return;
            PollOwnPress();
            if (Done || ownEnded) return;
            if (ownNext != null) TryChainOwn(ownCast, "frame");
        }

        string ComboName(double t)
        {
            foreach (var (kt, name) in kf.TextKeys)
                if ((float)kt == (float)t && (name == "selfcombo" || name == "combo")) return name;
            return "combo";
        }

        /// <summary>HudScene.OwnKeys. False when the run stopped, the state left or the sequence was sent back.</summary>
        bool OwnKeys(double upTo)
        {
            double prev = ownKeyPos;
            ownKeyPos = upTo;
            uint now = Clock;
            bool charging = ownCast.Skill.ChargingSkill;
            while (true)
            {
                double tk = SkillLoop.NextTextKey(prev, upTo, ownLoopStartKey, ownChargingComboKey, ownPointTime, ownChargingAttackKey, ownLoopEndKey);
                double tc = ownComboKeys.Next(prev, upTo);
                double t = tk < 0 ? tc : tc < 0 ? tk : Math.Min(tk, tc);
                if (t < 0) return true;
                if (tk == t)
                {
                    if (ownLoopStartKey == t) ownLoop.OnLoopStart(ownLoopStartKey, now);
                    if (ownChargingComboKey == t) ownLoop.OnChargingCombo(charging, keys.CastDown, now, ownChargingComboTick);
                    while (ownPointTime == t)
                    {
                        int a = ownAttack;
                        ownLoop.OnAttackPoint(charging);
                        ownAttack += SkillAttackFire.AttacksAtPoint(ownCast, ownAttack);
                        ownPointTime = PointTime();
                        if (ownPointTime == t && ownAttack == a) break;
                    }
                    if (ownChargingAttackKey == t) ownLoop.OnChargingAttack(charging);
                    if (ownLoopEndKey == t)
                    {
                        OwnLoopEnd(now);
                        if (ownKeyPos != upTo) return false;
                    }
                }
                if (tc == t)
                {
                    var running = ownCast;
                    bool canChain = !(running.Skill.ChargingSkill && (ownWindow.Ended || ownLoop.Repeat != 0));
                    if (ownComboKeys.Advance(ref ownWindow, prev, t, canChain) && ownNext != null)
                    {
                        TryChainOwn(running, ComboName(t));
                        if (Done) return false;
                    }
                }
                prev = t;
            }
        }

        /// <summary>HudScene.OwnLoopEnd with a live session's cooldowns.</summary>
        void OwnLoopEnd(uint now)
        {
            if (!ownLoop.OnLoopEnd(ownLoopEndKey, ownLoopEndKey, speed, now)) return;
            ownAttack = 0;
            ownCast.Charged = true;
            bool ready = cool.CanUseRechargeSkill(ownCast.Skill, now);
            float cd = cool.Remain(ownCast.Skill.Id, false, now);
            if (!ready && cd > 0f && !ownCast.Skill.ChargingSkill) { ownQueued = ownCast; ownCast.QueuedCharge = true; }
            else SendOwnRound(ownCast);
            if (!ownCast.QueuedCharge) SeekOwnLoop();
            else ownPointTime = PointTime();
        }

        /// <summary>HudScene.SendOwnRound (its cooldown and charge use).</summary>
        void SendOwnRound(CastRecord r)
        {
            r.SendTick = (int)Clock;
            if (r.Skill.Continue && !(r.Skill.ChargingSkill && r.Charged))
                cool.Start((uint)r.SendTick, r.Skill.Id, SkillLoop.RoundCooldown(ownLoopStartKey, ownLoopEndKey, ownLoop.Speed, ownLoop.ChargeTick), 0f, true, Clock);
            cool.OnRechargeSkillUsed(r.Skill, Clock);
        }

        /// <summary>HudScene.SeekOwnLoop.</summary>
        void SeekOwnLoop()
        {
            pos = ownLoop.LoopStart + ownLoop.ChargeTick * 0.001;
            atEnd = false;
            ownKeyPos = KeysFromStart;
            ownPointTime = PointTime();
            Out.Add("loop");
            Loops++;
            LoopsInMotion++;
        }

        /// <summary>HudScene.TickOwnSync: the hold check and the charging release (the send is the other PC's stream, from
        /// the client run).</summary>
        void TickOwnSync()
        {
            if (!StateRuns) return;
            if (ownLoop.HoldCheckRuns(ownCast.Skill))
            {
                bool held = keys.CastDown, charging = ownCast.Skill.ChargingSkill;
                ownLoop.Hold(held, ownLoopStartKey >= 0 && ownLoopEndKey >= 0, charging, held && !charging);
            }
            if (ownCast.Skill.ChargingSkill && !ownLoop.Loop)
            {
                float ca = ownChargingAttackKey >= 0 ? ownChargingAttackKey : (float)kf.Duration;
                if (ownLoop.Release(true, keys.CastDown, ownCast.Skill.DisableChargingAttackSkipFrame, ca, out bool seek) && seek)
                {
                    pos = ca;
                    atEnd = false;
                    ownKeyPos = KeysFromStart;
                    Out.Add("skip");
                }
            }
        }
    }

    // ---- another PC ------------------------------------------------------------------------------------------------

    interface IRemote
    {
        List<string> Out { get; }
        bool Done { get; }
        void Cast(int motion);
        void Sync(bool charging, bool start);
        void Step();
        bool Holding { get; }
    }

    /// <summary>The client's other PC (StateSkill for a PC that is not MyPC), from the decomp.</summary>
    sealed class ClientRemote : IRemote
    {
        readonly Ctx c;
        public List<string> Out { get; } = new();
        public bool Done { get; private set; }
        double timeMs;
        uint Clock => 100000 + (uint)timeMs;
        int motion;
        KfAnimation kf;
        double pos, keyPos;
        float speed, chargeTime, loopStart;
        bool atEnd, inState, waitingNext, charging, loop, b111, refused;
        int repeat, attack;
        uint loopStartTick, endTick;

        public ClientRemote(Ctx c) { this.c = c; }

        public bool Holding => inState && refused;

        /// <summary>OnSkillUse (scenestategame.cpp:3295-3346) -> a new record and StateSkill enter (FUN_1417f0940 :2229-2230:
        /// another PC's charging skill gets 0x14 more repeats).</summary>
        public void Cast(int m)
        {
            motion = m;
            var mo = c.Level.Motions[m];
            kf = c.Body.Get(mo.SequenceName ?? "");
            if (kf == null) { Out.Add("missing:" + mo.SequenceName); Done = true; return; }
            Out.Add("play:" + mo.SequenceName.ToLowerInvariant());
            b111 = c.Skill.ChargingSkill;
            repeat = mo.LoopCount + (b111 ? 0x14 : 0);
            charging = loop = false;
            chargeTime = 0f;
            attack = 0;
            pos = 0;
            keyPos = KeysFromStart;
            atEnd = refused = waitingNext = false;
            inState = true;
            speed = Math.Max(mo.SequenceSpeed, 0.1f);
        }

        void Seek(float t, string token)
        {
            pos = t;
            keyPos = KeysFromStart;
            atEnd = refused = false;
            Out.Add(token);
        }

        /// <summary>OnSkillSync (:3368-3405) -> FUN_1417ec630 @0x1417ec630 (stateskill.cpp:1419-1488).</summary>
        public void Sync(bool ch, bool start)
        {
            if (!inState) return;
            charging = ch;
            if (charging)
            {
                if (chargeTime <= 0f) return;
                attack = 0;
                chargeTime = 0f;
                charging = false;
                Seek(loopStart, "loop");
            }
            else if (!loop && start)
            {
                float at = Key(kf, "charging_attack");
                if (at < 0) return;   // FUN_141b6fe90 gives -FLT_MAX for a missing key: as the port takes it
                Seek(at, "skip");
                loop = true;
                charging = false;
                repeat = 0;
                loopStart = at;
            }
        }

        public void Step()
        {
            timeMs += FrameMs;
            if (!inState) return;
            if (waitingNext && Clock - endTick > 5000) { Out.Add("timeout"); Done = true; return; }   // :2990
            if (atEnd) return;
            pos = Math.Min(pos + Dt * speed, kf.Duration);
            bool finished = pos >= kf.Duration;
            double prev = keyPos, upTo = finished ? Math.Max(keyPos, kf.Duration) : pos;
            keyPos = upTo;
            var mo = c.Level.Motions[motion];
            foreach (var (t, name) in kf.TextKeys)
            {
                if (!(prev < t && t <= upTo)) continue;
                if (TextKey(name, (float)t, mo)) return;
            }
            if (finished) EndOfSequence();
        }

        bool TextKey(string key, float t, SkillMotionData mo)
        {
            var pAttack = attack < mo.Attacks.Count ? mo.Attacks[attack] : null;
            if (key == "loopstart" && !loop) { loopStart = t; chargeTime = 0f; loopStartTick = Clock; }
            if (key == "loopend")
            {
                chargeTime = t;
                if (charging || repeat > 0)
                {
                    if (repeat > 0) repeat--;
                    attack = 0;
                    charging = false;
                    uint loopMs = (uint)(long)((t - loopStart) / speed * 1000f);
                    uint tick = Clamped(Clock - loopStartTick, loopMs);
                    if (loopMs - 5u < tick) tick = loopMs - 5u;
                    Seek(loopStart + tick * 0.001f, "loop");
                    return true;
                }
            }
            if (pAttack != null && key == pAttack.Point)
            {
                if (b111 && !loop && repeat == 0) loop = true;
                while (attack < mo.Attacks.Count && mo.Attacks[attack].Point == key) attack++;
                return false;
            }
            if (key == "charging_attack" && b111 && !loop && repeat == 0) loop = true;
            return false;
        }

        void EndOfSequence()
        {
            atEnd = true;
            if (charging || repeat > 0) { refused = true; return; }   // :1056-1059
            loop = true;
            attack = 0;
            if (motion + 1 >= c.Level.Motions.Count) { Out.Add("end"); Done = true; inState = false; return; }   // FUN_1417f0070
            Out.Add("next");
            waitingNext = true;
            endTick = Clock;
        }
    }

    /// <summary>The port's other PC: <see cref="RemotePcStates"/> as RemotePc and RemoteLayer.PlayCast drive it.</summary>
    sealed class PortRemote : IRemote
    {
        readonly Ctx c;
        readonly SkillTable rows;
        readonly int id;
        readonly Pc pc = new();
        readonly RemotePcStates sm;
        public List<string> Out => pc.Out;
        public bool Done { get; private set; }
        int motion, count;
        bool waitingNext, wasFinished, inCast;

        public PortRemote(Ctx c, SkillTable rows)
        {
            this.c = c;
            this.rows = rows;
            id = (int)c.Skill.Id;
            sm = new RemotePcStates(pc);
            sm.ChangeState(RemotePcStates.StateIdle);
            count = rows.MotionCount(id, c.Level.Level);
        }

        public bool Holding => sm.Cur == RemotePcStates.StateSkill && wasFinished && !waitingNext;

        public void Cast(int m)
        {
            motion = m;
            var mo = rows.Motion(id, c.Level.Level, m);
            var kf = c.Body.Get(mo?.SequenceName ?? "");
            if (kf == null) { Out.Add("missing:" + mo?.SequenceName); Done = true; return; }
            float speed = m < c.Level.Motions.Count ? Math.Max(c.Level.Motions[m].SequenceSpeed, 0.1f) : 1f;
            sm.Cast(id, rows.ChargingSkill(id), Vector3.Zero, 0f, 0, "", m, count, speed, mo.LoopCount, mo.AttackPoints);
            pc.Kf = kf;
            pc.Pos = 0;
            pc.LastPos = KeysFromStart;
            pc.Rate = speed;
            pc.Out.Add("play:" + mo.SequenceName.ToLowerInvariant());
            waitingNext = wasFinished = false;
            inCast = true;
        }

        public void Sync(bool charging, bool start)
        {
            pc.Seeked = false;
            sm.SkillSync(0f, Vector2.Zero, charging, start);
            if (pc.Seeked) { pc.LastPos = KeysFromStart; wasFinished = false; }
        }

        public void Step()
        {
            if (!inCast) return;
            var kf = pc.Kf;
            pc.Pos = Math.Min(pc.Pos + Dt * pc.Rate, kf.Duration);
            bool finished = pc.Pos >= kf.Duration;
            pc.Seeked = false;
            double from = pc.LastPos, to = pc.Pos;
            foreach (var (kt, key) in kf.TextKeys)
                if (from < kt && kt <= to && sm.Cur == RemotePcStates.StateSkill) sm.CastTextKey(key, (float)kt);
            pc.LastPos = pc.Seeked ? KeysFromStart : pc.Pos;
            // RemotePc.OnAnimationFinished: once per play of the sequence
            if (finished && !pc.Seeked && !wasFinished && sm.Cur == RemotePcStates.StateSkill)
            {
                wasFinished = true;
                sm.CastSequenceEnded();
                if (sm.Cur == RemotePcStates.StateSkill && motion + 1 < count && sm.CastRepeat <= 0) { pc.Out.Add("next"); waitingNext = true; }
            }
            if (pc.Seeked) wasFinished = false;
            sm.Update(Dt);
            if (sm.Cur != RemotePcStates.StateSkill)
            {
                pc.Out.Add(waitingNext ? "timeout" : "end");
                Done = true;
                inCast = false;
            }
        }
    }

    // ---- the runs ----------------------------------------------------------------------------------------------------

    static List<string> RunOwn(IOwn m, Scenario s)
    {
        var k = new Keys { S = s };
        for (int f = 1; f <= MaxFrames && !m.Done; f++)
        {
            k.Frame = f;
            m.Step(k);
            if (s == Scenario.Hold && m.LoopsInMotion >= m.MotionLoopCount + 2) k.Released = true;
            if (s == Scenario.Release && m.Loops >= 1) k.Released = true;
        }
        if (!m.Done) m.Out.Add("cap");
        return m.Out;
    }

    static List<string> RunRemote(Stream st, IRemote r)
    {
        int si = 0, ci = 0;
        for (int f = 0; f <= MaxFrames + 300 && !r.Done; f++)
        {
            for (; si < st.Syncs.Count && st.Syncs[si].frame <= f; si++) r.Sync(st.Syncs[si].charging, st.Syncs[si].loop);
            for (; ci < st.Starts.Count && st.Starts[ci].frame <= f; ci++) r.Cast(st.Starts[ci].motion);
            if (r.Done) break;
            if (st.Followed && f >= st.EndFrame) { r.Out.Add("cut"); return r.Out; }
            r.Step();
        }
        if (!r.Done) r.Out.Add(r.Holding ? "held-no-timer" : "cap");
        return r.Out;
    }

    // ---- the sweep ---------------------------------------------------------------------------------------------------

    sealed record Row(uint Skill, string Kind, int Level, string Body, string Side, Scenario Scenario, string Expected, string Played)
    {
        public int RefusedAt = -1;
        public double ExpectedFollowPos = double.NaN, PlayedFollowPos = double.NaN, FrameSeqTime;
        public string Verdict = Expected == Played ? "ok" : "FAIL";
        public string Data = "";
    }

    /// <summary>A failing row's data: the skill's input flags, then each motion's sequence, speed, loopCount and keys.</summary>
    static string RowData(SkillData s, SkillLevelData level, BodyKfs body) =>
        $"continue {(s.Continue ? 1 : 0)} charging {(s.ChargingSkill ? 1 : 0)} keyIsDown {(s.KeyIsDown ? 1 : 0)} immediate {s.ImmediateActive} "
        + $"recharge {s.RechargeMaxCount} item {(s.IsItemKind ? 1 : 0)} skip {(s.DisableChargingAttackSkipFrame ? 0 : 1)} cool {level.Cooldown}; "
        + string.Join(" / ", level.Motions.Select(m =>
        {
            var kf = body.Get(m.SequenceName ?? "");
            string keys = kf == null ? "?" : string.Join(",", kf.TextKeys.Select(k => $"{k.text}@{k.time:0.###}"));
            return $"{m.SequenceName} x{m.SequenceSpeed} loop {m.LoopCount} self {m.SelfInputCombo} len {kf?.Duration:0.###} [{keys}]";
        }));

    /// <summary>
    /// The rows that differ only in which callback sees the same cast first, marked "same cast", not failed. Every other
    /// difference fails.
    /// </summary>
    static void MarkGaps(List<Row> list)
    {
        foreach (var r in list)
        {
            // same cast: the same skill follows at the same sequence time, within a frame, through another callback (the
            // per-frame chain or the combo key's; the client's end step casting a new record once its cooldown is over or
            // FUN_1417f11d0 / TickOwnQueued casting the queued one). The port reads a frame's keys in the next frame (HudScene
            // runs before the AnimationPlayer under it), so which callback sees the cast first can differ
            if (r.Verdict == "FAIL" && r.Side == "own" && !double.IsNaN(r.ExpectedFollowPos) && !double.IsNaN(r.PlayedFollowPos)
                && Math.Abs(r.ExpectedFollowPos - r.PlayedFollowPos) <= r.FrameSeqTime + 1e-6 && SameButLabel(r.Expected, r.Played))
                r.Verdict = "same cast";
        }
    }

    static bool SameButLabel(string e, string p)
    {
        static string Cut(string s) { int i = s.LastIndexOf('@'); return i < 0 ? s : s[..i]; }
        return Cut(e) == Cut(p);
    }

    static void Sweep(string variable)
    {
        string data = DataDir(variable);
        var spec = new NifSpec(NifXml());
        using var packs = new PackSet(data);
        var tables = Ms2.Ui.GameTables.Shared(packs, "en");
        var rows = new SkillTable(packs);
        var job = JobSkills(tables);
        var bodies = Bodies.ToDictionary(b => b, b => new BodyKfs(packs, spec, b));
        Func<uint, SkillData> skills = tables.Skills.Skills.GetSkillData;
        var list = new List<Row>();
        int levelsSwept = 0, autoChange = 0, runs = 0;
        string trace = Environment.GetEnvironmentVariable("MS2_SKILLINPUT_TRACE");   // "<skill id>:<scenario>": each frame of both own runs
        foreach (int id in rows.Ids.OrderBy(i => i))
        {
            uint uid = (uint)id;
            var skill = skills(uid);
            if (skill == null) continue;
            bool isJob = job.Contains(uid);
            var seen = new HashSet<string>();
            foreach (var lr in rows.Levels(id))
            {
                var level = skill.GetLevelData(lr.Level);
                if (level == null || !level.HasSequenceName || level.Motions.Count == 0) continue;
                string sig = string.Join("|", level.Motions.Select(m => $"{m.SequenceName}/{m.SequenceSpeed}/{m.LoopCount}/{m.SelfInputCombo}/{string.Join(",", m.Attacks.Select(a => a.Point))}"));
                if (!seen.Add(sig)) continue;
                bool pcBody = level.Motions.All(m => string.IsNullOrEmpty(m.SequenceName) || bodies["male"].Has(m.SequenceName));
                if (!pcBody && !isJob) continue;
                if (level.Motions.Any(m => string.IsNullOrEmpty(m.SequenceName) || !bodies["male"].Has(m.SequenceName))) continue;
                if (skill.Change.AutoChange != 0) { autoChange++; continue; }
                levelsSwept++;
                var outSkill = skill.ComboOutputSkill != 0 && skill.ComboOutputSkill != uid ? skills(skill.ComboOutputSkill) : null;
                var outLevel = outSkill?.GetLevelData(level.Level) ?? outSkill?.GetLevelData(1);
                if (outLevel == null || !outLevel.HasSequenceName) outSkill = null;
                foreach (var (bname, body) in bodies)
                {
                    var ctx = new Ctx { Skill = skill, Level = level, Out = outSkill, OutLevel = outSkill != null ? outLevel : null, Body = body, Skills = skills };
                    foreach (Scenario s in Enum.GetValues<Scenario>())
                    {
                        if (s == Scenario.Output && ctx.Out == null) continue;
                        if (s == Scenario.Release && !skill.Continue && !skill.ChargingSkill) continue;
                        runs++;
                        var e = new ClientOwn(ctx);
                        var po = new PortOwn(ctx);
                        if (trace == $"{uid}:{s}" && bname == "male")
                        {
                            e.Trace = TestContext.Progress.WriteLine;
                            po.Trace = TestContext.Progress.WriteLine;
                        }
                        var exp = string.Join(" ", RunOwn(e, s));
                        var got = string.Join(" ", RunOwn(po, s));
                        string kind = isJob ? "job" : "pc";
                        list.Add(new Row(uid, kind, level.Level, bname, "own", s, exp, got) {
                            Data = exp == got ? "" : RowData(skill, level, body), RefusedAt = e.RefusedAt,
                            ExpectedFollowPos = e.FollowPos, PlayedFollowPos = po.FollowPos, FrameSeqTime = Dt * e.Speed,
                        });
                        if ((s == Scenario.Hold || s == Scenario.Release) && (skill.Continue || skill.ChargingSkill))
                        {
                            var rexp = string.Join(" ", RunRemote(e.St, new ClientRemote(ctx)));
                            var rgot = string.Join(" ", RunRemote(e.St, new PortRemote(ctx, rows)));
                            list.Add(new Row(uid, kind, level.Level, bname, "remote", s, rexp, rgot));
                        }
                    }
                }
            }
        }
        MarkGaps(list);
        var fails = list.Where(r => r.Verdict == "FAIL").ToList();
        var outPath = Environment.GetEnvironmentVariable("MS2_SKILLSEQ_INPUT_OUT");
        string counts = $"levels swept {levelsSwept}, auto change skills not run {autoChange}, runs {runs}, rows {list.Count}, fails {fails.Count}, "
            + string.Join(", ", list.Where(r => r.Verdict != "ok" && r.Verdict != "FAIL").GroupBy(r => r.Verdict).Select(g => $"{g.Key} {g.Count()}"));
        if (!string.IsNullOrEmpty(outPath))
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# {variable} {data}");
            sb.AppendLine(counts);
            sb.AppendLine();
            sb.AppendLine("## by scenario and side: rows / not ok");
            foreach (var g in list.GroupBy(r => (r.Scenario, r.Side)).OrderBy(g => g.Key.Scenario).ThenBy(g => g.Key.Side))
                sb.AppendLine($"- {g.Key.Scenario} {g.Key.Side}: {g.Count()} / {g.Count(r => r.Verdict != "ok")}");
            sb.AppendLine("## table");
            sb.AppendLine("| skill | kind | level | body | side | scenario | expected | played | verdict | data |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
            foreach (var r in list.OrderBy(r => r.Verdict == "ok").ThenBy(r => r.Skill).ThenBy(r => r.Scenario))
                sb.AppendLine($"| {r.Skill} | {r.Kind} | {r.Level} | {r.Body} | {r.Side} | {r.Scenario} | {r.Expected} | {r.Played} | {r.Verdict} | {r.Data} |");
            File.AppendAllText(outPath, sb.ToString());
        }
        TestContext.Progress.WriteLine($"{variable}: {counts}");
        Assert.That(levelsSwept, Is.GreaterThan(100), "the data has job skills");
        Assert.That(fails.Select(f => $"{f.Skill} {f.Level} {f.Body} {f.Side} {f.Scenario}: expected [{f.Expected}] played [{f.Played}]").Take(40), Is.Empty);
    }
}
