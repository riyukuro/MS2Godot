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
    /// The tag keys of one cast's sequence in time order (each occurrence of a key the motion names), for a walk that reads
    /// the keys passed since the last frame. Built once per cast (<see cref="Set"/>); <see cref="Next"/> allocates nothing.
    /// </summary>
    public sealed class Keys
    {
        readonly List<(double time, string key, string path)> list = new();

        /// <summary>The keys of <paramref name="textKeys"/> (time order) that <paramref name="tags"/> names; none for null.</summary>
        public void Set(List<(double time, string text)> textKeys, IReadOnlyDictionary<string, string> tags)
        {
            list.Clear();
            if (textKeys == null || tags == null || tags.Count == 0) return;
            foreach (var (t, k) in textKeys)
                if (k != null && tags.TryGetValue(k, out var path)) list.Add((t, k, path));
        }

        public int Count => list.Count;

        /// <summary>The time of the first tag key after <paramref name="after"/> and not after <paramref name="upTo"/>, or -1.</summary>
        public double Next(double after, double upTo)
        {
            foreach (var (t, _, _) in list)
                if (t > after && t <= upTo) return t;
            return -1;
        }

        /// <summary>The tag key at index <paramref name="i"/>: its time, name and effect xml.</summary>
        public (double time, string key, string path) this[int i] => list[i];
    }
}
