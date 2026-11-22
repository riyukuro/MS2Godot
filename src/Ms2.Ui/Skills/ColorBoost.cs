namespace Ms2.Ui.Skills;

/// <summary>
/// An actor's colour boosts: CColorBoostManager (PCBase m_colorBoost, colorboostdata.cpp:74-117) and its CColorBoostData
/// entries. <see cref="Add"/> is PCBase::AddColorBoost (@0x141673f70, pcbase.cpp:2947) and <see cref="Update"/> is
/// UpdateBoosts (@0x14109be30), which PCBase::vfunc_53 (pcbase.cpp:3733) runs each frame; when the summed scale changes,
/// the host writes it to the actor's "ColorBoost" float on every render object (SetColorScale, util.cpp:717), the
/// character shader's color_boost. The list keeps its capacity, so a frame allocates nothing.
/// </summary>
public sealed class ColorBoost
{
    struct Boost
    {
        public bool Fixed;       // m_b08: the boost's share is fixed on its first update
        public float Start;      // m_f0c: the game time it was added at
        public float Scale;      // m_f10
        public float Duration;   // m_f14
        public float AccumRatio; // m_f18
        public float Delta;      // m_f1c: the share fixed on the first update
    }

    readonly List<Boost> boosts = new(10);

    /// <summary>m_fScale: the scale last written to the actor (1 at the start).</summary>
    public float Scale { get; private set; } = 1f;

    /// <summary>The boosts still running.</summary>
    public int Count => boosts.Count;

    /// <summary>CColorBoostData(fColorScale, fDuration, fAccumRatio) (@0x14109b6b0) at the game time
    /// <paramref name="now"/>, added at the list's tail (AddBoost @0x14109be00).</summary>
    public void Add(float now, float colorScale, float duration, float accumRatio) =>
        boosts.Add(new Boost { Start = now, Scale = colorScale, Duration = duration, AccumRatio = accumRatio });

    /// <summary>
    /// UpdateBoosts (@0x14109be30): from a scale of 1, each boost in order (index n counting the removed ones too) adds its
    /// share times what is left of its time (CColorBoostData::FUN_14109b770 @0x14109b770): past its duration it is
    /// removed and adds nothing; on its first update the share is fixed to min(scale + its scale, 1 + (1 + n * its
    /// accumulation ratio) * its scale) - scale. True when the sum differs from <see cref="Scale"/>, which then takes it
    /// (the host writes it to the model).
    /// </summary>
    public bool Update(float now)
    {
        float scale = 1f;
        int index = 0;
        for (int k = 0; k < boosts.Count; index++)
        {
            var b = boosts[k];
            float elapsed = now - b.Start;
            if (elapsed > b.Duration) { boosts.RemoveAt(k); continue; }
            float remain = 1f - elapsed / b.Duration;
            if (!b.Fixed)
            {
                float target = MathF.Min(scale + b.Scale, 1f + (1f + index * b.AccumRatio) * b.Scale);
                b.Delta = target - scale;
                b.Fixed = true;
                boosts[k] = b;
            }
            scale += b.Delta * remain;
            k++;
        }
        if (scale == Scale) return false;
        Scale = scale;
        return true;
    }
}
