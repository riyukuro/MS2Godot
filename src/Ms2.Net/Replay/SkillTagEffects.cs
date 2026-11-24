namespace Ms2.Net.Replay;

/// <summary>
/// The motion's tag effects (SkillMotionData::m_mapTagEffects, a text key to an effect xml) as StateSkill::TextKeyEvent
/// (@0x1417e7660, stateskill.cpp:792-834) plays them: when a key of the cast's sequence names one, the effect plays on the
/// caster unless <see cref="Plays"/> says no. <see cref="Keys"/> is the own cast's walk over them.
/// </summary>
public static class SkillTagEffects
{
    /// <summary>
    /// TextKeyEvent's test of a tag key (stateskill.cpp:796-819), the times at speed 1 (FUN_141b6fe90(m_pSequence, key, 1f),
    /// seconds; below 0 for a key the sequence lacks, where the client has -FLT_MAX). <paramref name="key"/> is the time of
    /// the key's name in the sequence. Inside the loop (m_b117, <paramref name="inLoop"/>) a key before "loopstart" or after
    /// "loopend" plays nothing (so nothing without a "loopend"), nor any key while the record is queued behind the running
    /// skill (m_spRecord-&gt;m_n9c, <paramref name="queued"/>). Outside it, after the charging loop ended (m_bLoop,
    /// <paramref name="loop"/>), a key before "charging_attack" plays nothing. A key the sequence lacks plays.
    /// </summary>
    public static bool Plays(float key, bool inLoop, bool queued, bool loop, float loopStart, float loopEnd, float chargingAttack)
    {
        if (key < 0) return true;   // fKey == -FLT_MAX skips both tests
        if (inLoop)
        {
            if (key < loopStart) return false;
            if (key > loopEnd) return false;   // a missing "loopend" (below 0) too
            return !queued;
        }
        return !(loop && key < chargingAttack);
    }

    /// <summary>
    /// StateSkill::TextKeyEvent's voice test (stateskill.cpp:834): a key containing "EventSound" (CString::ContainsNoCase)
    /// plays PlayEventVoice for a PC (the event after the key's first 11 characters, Ms2.Audio.EventSoundTable).
    /// </summary>
    public static bool IsEventSoundKey(string key) =>
        key != null && key.Contains("EventSound", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The effect files of one skill motion that StateSkill plays on the caster besides the tag effects (SkillMotionData::Load,
    /// skillleveldata.cpp:1166-1172; GetMotionEffect's kinds 2, 3 and 4, :886-901): <see cref="Motion"/> "motionEffect",
    /// <see cref="Loop"/> "loopEffect", <see cref="End"/> "endEffect", <see cref="LoopKeep"/> "loopKeepEffect" and
    /// <see cref="LoopEnd"/> "loopEndEffect". Null or empty for none.
    /// </summary>
    public readonly record struct MotionEffects(string Motion, string Loop, string End, string LoopKeep, string LoopEnd)
    {
        public bool Any => !string.IsNullOrEmpty(Motion) || !string.IsNullOrEmpty(Loop) || !string.IsNullOrEmpty(End)
            || !string.IsNullOrEmpty(LoopKeep) || !string.IsNullOrEmpty(LoopEnd);
    }

    /// <summary>TextKeyEvent's loop keep keys (stateskill.cpp:1039-1045, CString::EqualsNoCase): 1 for
    /// "startLoopKeepEffect" (StartLoopEffect), 2 for "endLoopKeepEffect" (StopLoopEffect then PlayLoopEndEffect), else 0.</summary>
    public static int LoopKeepKey(string key) =>
        key == null ? 0
        : key.Equals("startLoopKeepEffect", StringComparison.OrdinalIgnoreCase) ? 1
        : key.Equals("endLoopKeepEffect", StringComparison.OrdinalIgnoreCase) ? 2 : 0;

    /// <summary>
    /// A record's kept tag effects (CSkillRecord m_map138, a CRBMap of the text key to the effect id): TextKeyEvent sets the
    /// id a tag key's effect got (SetAt, stateskill.cpp:829: a key played again replaces its id, the old effect left running).
    /// <see cref="End"/> is StateSkill::vfunc_5's part for them (:515-524). No allocation past the list's growth.
    /// </summary>
    public sealed class Handles
    {
        readonly List<(string key, int id)> map = new();

        /// <summary>The record's motion effect (m_n84): the "motionEffect" StateSkill::AnimActivated (@0x1417e71d0,
        /// stateskill.cpp:750-761) keeps on the caster at the sequence's start, once per record (only while m_n84 is 0);
        /// 0 for none. <see cref="End"/> ends it first, as StopRecordEffects (:2298-2300) and vfunc_5's releases (:520) do.</summary>
        public int Motion;

        /// <summary>The record's loop effect (m_n88): the "loopEffect" the sequence's "loopstart" key keeps on the caster
        /// (<see cref="LoopStart"/>) and its "loopend" key stops (<see cref="LoopEnd"/>); 0 for none.</summary>
        public int Loop;

        /// <summary>The state's loop keep effect (StateSkill m_nLoopEffectId): the "loopKeepEffect" the
        /// "startLoopKeepEffect" key keeps (<see cref="StartLoopKeep"/>) until "endLoopKeepEffect" (<see cref="EndLoopKeep"/>)
        /// or the state's exit stops it; 0 for none.</summary>
        public int LoopKeep;

        public int Count => map.Count + (Motion != 0 ? 1 : 0) + (Loop != 0 ? 1 : 0) + (LoopKeep != 0 ? 1 : 0);

        /// <summary>
        /// TextKeyEvent's "loopstart" (stateskill.cpp:844-860), when the charging loop has not ended (!m_bLoop): the record's
        /// loop effect is stopped (FUN_1411a6060) and <paramref name="loopEffect"/> kept on the caster in its place
        /// (<paramref name="keep"/>, FUN_1411a49c0 with SetActor; 0 when the caller's sight test skips it).
        /// </summary>
        public void LoopStart(string loopEffect, Func<string, int> keep, Action<int> stop)
        {
            if (Loop != 0) { stop?.Invoke(Loop); Loop = 0; }
            if (!string.IsNullOrEmpty(loopEffect)) Loop = keep?.Invoke(loopEffect) ?? 0;
        }

        /// <summary>TextKeyEvent's "loopend" (:867-873): the record's loop effect is stopped.</summary>
        public void LoopEnd(Action<int> stop)
        {
            if (Loop != 0) { stop?.Invoke(Loop); Loop = 0; }
        }

        /// <summary>StateSkill::StartLoopEffect (@0x1417f1ea0, stateskill.cpp:2358-2372): nothing while one is kept, else
        /// <paramref name="loopKeepEffect"/> kept on the caster.</summary>
        public void StartLoopKeep(string loopKeepEffect, Func<string, int> keep)
        {
            if (LoopKeep != 0 || string.IsNullOrEmpty(loopKeepEffect)) return;
            LoopKeep = keep?.Invoke(loopKeepEffect) ?? 0;
        }

        /// <summary>"endLoopKeepEffect" (:1041-1045): StopLoopEffect (@0x1417f1fe0, :1659-1665) stops the loop keep effect,
        /// then PlayLoopEndEffect (@0x1417f2030, :2376-2388) inserts <paramref name="loopEndEffect"/> on the caster, kept by
        /// nothing (<paramref name="insert"/>).</summary>
        public void EndLoopKeep(string loopEndEffect, Action<int> stop, Action<string> insert)
        {
            if (LoopKeep != 0) { stop?.Invoke(LoopKeep); LoopKeep = 0; }
            if (!string.IsNullOrEmpty(loopEndEffect)) insert?.Invoke(loopEndEffect);
        }

        /// <summary>
        /// TextKeyEvent's part for the effect slots other than the tag effects, for a key of the cast's sequence:
        /// "loopstart" while <paramref name="chargingLoopEnded"/> (m_bLoop) is unset, "loopend", and the loop keep keys
        /// (<see cref="LoopKeepKey"/>). True when the key was one of them.
        /// </summary>
        public bool TextKey(string key, in MotionEffects fx, bool chargingLoopEnded, Func<string, int> keep, Action<int> stop,
            Action<string> insert)
        {
            if (key == "loopstart") { if (!chargingLoopEnded) LoopStart(fx.Loop, keep, stop); return true; }
            if (key == "loopend") { LoopEnd(stop); return true; }
            switch (LoopKeepKey(key))
            {
                case 1: StartLoopKeep(fx.LoopKeep, keep); return true;
                case 2: EndLoopKeep(fx.LoopEnd, stop, insert); return true;
            }
            return false;
        }

        /// <summary>The id kept for <paramref name="key"/>, 0 without one.</summary>
        public int this[string key]
        {
            get
            {
                foreach (var (k, id) in map) if (k == key) return id;
                return 0;
            }
        }

        /// <summary>CRBMap::SetAt.</summary>
        public void Set(string key, int id)
        {
            for (int i = 0; i < map.Count; i++)
                if (map[i].key == key) { map[i] = (key, id); return; }
            map.Add((key, id));
        }

        /// <summary>
        /// The state's exit (StateSkill::vfunc_5, stateskill.cpp:515-567): when <paramref name="hard"/> (the owner dead or
        /// stunned, the next state 0xd, or the skill's notKeepEffect) StopRecordEffects (:2296-2310) stops the motion effect,
        /// the loop effect and each tag effect at once (CEffectService::FUN_1411a6060, <paramref name="stop"/>) and zeroes
        /// their ids; then each id left is released (FUN_1411a69e0, <paramref name="release"/>; nothing for an id 0), in the
        /// same order (:520-523). The motion's "endEffect" (<paramref name="endEffect"/>) is then inserted on the caster,
        /// kept by nothing (:536-545, <paramref name="insert"/>), and last the state's loop keep effect is stopped
        /// (StopLoopEffect, :567). The record goes with the state here, so the map is emptied.
        /// </summary>
        public void End(bool hard, Action<int> stop, Action<int> release, string endEffect = null, Action<string> insert = null)
        {
            if (hard)
            {
                if (Motion != 0) { stop?.Invoke(Motion); Motion = 0; }
                if (Loop != 0) { stop?.Invoke(Loop); Loop = 0; }
            }
            if (Motion != 0) { release?.Invoke(Motion); Motion = 0; }
            if (Loop != 0) { release?.Invoke(Loop); Loop = 0; }
            for (int i = 0; i < map.Count; i++)
            {
                var (k, id) = map[i];
                if (id == 0) continue;
                if (hard) { stop?.Invoke(id); map[i] = (k, 0); }
                else release?.Invoke(id);
            }
            map.Clear();
            if (!string.IsNullOrEmpty(endEffect)) insert?.Invoke(endEffect);
            if (LoopKeep != 0) { stop?.Invoke(LoopKeep); LoopKeep = 0; }
        }
    }

    /// <summary>
    /// The tag keys of one cast's sequence in time order (each occurrence of a key the motion names), for a walk that reads
    /// the keys passed since the last frame. Built once per cast (<see cref="Set"/>); <see cref="Next"/> allocates nothing.
    /// </summary>
    public sealed class Keys
    {
        readonly List<(double time, string key, string path)> list = new();

        /// <summary>The keys of <paramref name="textKeys"/> (time order) that <paramref name="tags"/> names, and each
        /// EventSound key (<see cref="IsEventSoundKey"/>) and loop keep key (<see cref="LoopKeepKey"/>) with no path, after
        /// the key's tag effect as TextKeyEvent takes them (:792-841, :1039-1045); none for null keys.</summary>
        public void Set(List<(double time, string text)> textKeys, IReadOnlyDictionary<string, string> tags)
        {
            list.Clear();
            if (textKeys == null) return;
            foreach (var (t, k) in textKeys)
            {
                if (k == null) continue;
                if (tags != null && tags.TryGetValue(k, out var path)) list.Add((t, k, path));
                if (IsEventSoundKey(k) || LoopKeepKey(k) != 0) list.Add((t, k, null));
            }
        }

        public int Count => list.Count;

        /// <summary>The time of the first tag key after <paramref name="after"/> and not after <paramref name="upTo"/>, or -1.</summary>
        public double Next(double after, double upTo)
        {
            foreach (var (t, _, _) in list)
                if (t > after && t <= upTo) return t;
            return -1;
        }

        /// <summary>The tag key at index <paramref name="i"/>: its time, name and effect xml (null for an EventSound or loop
        /// keep key).</summary>
        public (double time, string key, string path) this[int i] => list[i];
    }
}
