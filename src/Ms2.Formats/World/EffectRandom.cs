namespace Ms2.Formats.World;

/// <summary>
/// CRandomEffect (randomeffectparam.cpp): a type="random" part makes one of its children, once per play. CreateEffect makes
/// the random part (its own vfunc_7 filters and view test, effectservice.cpp:287-345), and CRandomEffect::vfunc_3 @0x1416f48e0
/// (:143-178) picks the child and makes it through CreateEffect: the target's m_nRandomIndex mod the child count when it is
/// not 0, else a weighted pick seeded by CEffectService::GetTick. No child made, or every weight 0, makes nothing.
/// </summary>
public static class EffectRandom
{
    /// <summary>
    /// CEffectService::m_nTick (GetTick @0x1411a8e70, effectservice.cpp:2195): GetTickCountMs2 when the service is made (:1211),
    /// then the serial of each OnSkillUse (SetTick, scenestategame.cpp:3340) and a couple emotion's value (networkservice.cpp:1172).
    /// Every client seeing a cast picks the same child.
    /// </summary>
    public static uint Tick = unchecked((uint)Environment.TickCount);

    /// <summary>CSoundEventEffectParam::vfunc_2's FMOD lookups (soundeventmgr.cpp:310-345): true when the part's group is empty,
    /// or its group and its event (each job's with byJob) are found. Null takes every soundEvent part as made.</summary>
    public static Func<EffectPart, bool> SoundEventFound;

    /// <summary>
    /// CreateEffectParam @0x1411a04e0 (effectservice.cpp:701-737) makes a param of a known type whose vfunc_2 passes; only those
    /// are a random part's children (randomeffectparam.cpp:90-92). CEffectParam::vfunc_2 (:667-685) refuses a monochrome,
    /// swoosh, status, light, fog, vibrate or textureOverride part with no loop and a duration of 0; a soundEvent part's
    /// group and event must be found (<see cref="SoundEventFound"/>); a random part needs a child (randomeffectparam.cpp:98-108);
    /// a vibrate part's duration must pass its damping time (CVibrateEffectParam::vfunc_2, vibrateeffectparam.cpp:77-88).
    /// STILL-OPEN: a soundMusic part's cue (soundmusiceffectparam.cpp:89-104) is not tested.
    /// </summary>
    public static bool ParamMade(IReadOnlyList<EffectPart> parts, EffectPart p)
    {
        if (p.TypeId == 0) return false;
        if (!p.Loop && MathF.Abs(p.Duration) <= 1e-4f)
            switch (p.TypeId)
            {
                case EffectSkipTable.TypeMonochrome: case EffectSkipTable.TypeSwoosh: case EffectSkipTable.TypeStatus:
                case EffectSkipTable.TypeLight: case EffectSkipTable.TypeFog: case EffectSkipTable.TypeVibrate:
                case EffectSkipTable.TypeTextureOverride:
                    return false;
            }
        if (p.TypeId == EffectSkipTable.TypeVibrate) return p.Duration > p.VibrateDampingTime;
        if (p.TypeId == EffectSkipTable.TypeSoundEvent) return SoundEventFound?.Invoke(p) ?? true;
        if (p.TypeId == EffectSkipTable.TypeRandom)
        {
            foreach (int c in p.RandomChildren) if (ParamMade(parts, parts[c])) return true;
            return false;
        }
        return true;
    }

    /// <summary>
    /// CRandomEffect::vfunc_3 (randomeffectparam.cpp:143-178): the child of <paramref name="random"/> one play makes, as an
    /// index in <paramref name="parts"/>; -1 when none is. The children are the made params (<see cref="ParamMade"/>), in the
    /// file's order. A <paramref name="randomIndex"/> other than 0 (Unk_EffectTarget::m_nRandomIndex, a region skill's object
    /// id) takes child randomIndex mod count (:153-156). Else Unk_WeightedPicker (:162-171): child i with weight i of prob
    /// (0 past its end), a weight 0 never; Build (@0x1416f5250, :49-62) keys each child by its running total - 1, Pick
    /// (@0x1416f55b0, :65-72) takes Unk_Random(<paramref name="tick"/>)'s first number mod the total and the first key not
    /// below it.
    /// </summary>
    public static int Pick(IReadOnlyList<EffectPart> parts, EffectPart random, uint randomIndex, uint tick)
    {
        var kids = random.RandomChildren;
        if (kids == null) return -1;
        int count = 0;
        foreach (int c in kids) if (ParamMade(parts, parts[c])) count++;
        if (count == 0) return -1;
        int chosen;
        if (randomIndex != 0) chosen = (int)(randomIndex % (uint)count);
        else
        {
            var w = random.RandomWeights;
            int total = 0;
            for (int i = 0; i < count; i++) total += i < w.Length ? (int)w[i] : 0;
            if (total == 0) return -1;
            int key = (int)(DropRandom.Seeded(unchecked((int)tick)).Next() % unchecked((uint)total));
            chosen = -1;
            int run = 0;
            for (int i = 0; i < count && chosen < 0; i++)
            {
                int wi = i < w.Length ? (int)w[i] : 0;
                if (wi == 0) continue;
                run += wi;
                if (run - 1 >= key) chosen = i;
            }
            if (chosen < 0) return -1;
        }
        foreach (int c in kids)
            if (ParamMade(parts, parts[c]) && chosen-- == 0) return c;
        return -1;
    }

    /// <summary>
    /// The parts one play of an effect makes, into <paramref name="into"/> (cleared first) in the file's order: each top level
    /// part (the effect data's params, effectservice.cpp:614-617), a random part replaced by its one chosen child
    /// (<see cref="Pick"/>, with the same <paramref name="tick"/> for every random part of the play). A child keeps
    /// <see cref="EffectPart.RandomParent"/>, so the caller applies its random part's view test too
    /// (<see cref="EffectSkipTable.RandomOutOfView"/>). With an <paramref name="owner"/>, a random part its filters refuse
    /// (CEffectParam::vfunc_7, <see cref="EffectPart.SkippedFor"/>, before CreateEffect makes it) picks nothing.
    /// </summary>
    public static List<EffectPart> PlayList(IReadOnlyList<EffectPart> parts, uint randomIndex, uint tick, List<EffectPart> into,
        EffectOwner? owner = null)
    {
        into.Clear();
        if (parts == null) return into;
        for (int i = 0; i < parts.Count; i++)   // by index: a foreach over the interface boxes its enumerator each play
        {
            var p = parts[i];
            if (!p.TopLevel) continue;
            Add(parts, p, randomIndex, tick, into, owner);
        }
        return into;
    }

    static void Add(IReadOnlyList<EffectPart> parts, EffectPart p, uint randomIndex, uint tick, List<EffectPart> into, EffectOwner? owner)
    {
        if (p.TypeId != EffectSkipTable.TypeRandom) { into.Add(p); return; }
        if (owner is { } o && p.SkippedFor(o)) return;
        int c = Pick(parts, p, randomIndex, tick);
        if (c >= 0) Add(parts, parts[c], randomIndex, tick, into, owner);
    }
}
