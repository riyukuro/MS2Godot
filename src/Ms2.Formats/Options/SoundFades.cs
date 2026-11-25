namespace Ms2.Formats.Options;

/// <summary>
/// The running volume fades of CSoundEventMgr's categories (m_mapFades, one per category, soundeventmgr.h:282): StartFadeIn
/// (@0x14177ddf0) and StartFadeOut (@0x14177dea0) store the category's volume at the start and a timer of the given
/// milliseconds, replacing a fade the category already has (soundeventmgr.cpp:1744-1764); <see cref="Update"/> is UpdateFades
/// (@0x14177bbc0, soundeventmgr.cpp:1063-1097), run from CSoundEventMgr::Update each frame. The clock is injected (the
/// client's Timer reads the service clock, timer.cpp:39-60, 91-95, 159-164).
/// </summary>
public sealed class SoundFades
{
    const int FadeIn = 1, FadeOut = 2;

    sealed record Fade(int Category, int Mode, float Volume, long StartMs, int PeriodMs);

    readonly SoundMix mix;
    readonly Func<long> clockMs;
    readonly SortedDictionary<int, Fade> fades = new();

    public SoundFades(SoundMix mix, Func<long> clockMs) { this.mix = mix; this.clockMs = clockMs; }

    /// <summary>The system clock in milliseconds, for a player.</summary>
    public static long SystemClockMs() => Environment.TickCount64;

    public bool Running => fades.Count > 0;

    /// <summary>StartFadeIn @0x14177ddf0 (:1744-1753): mode 1, the volume the category has now, the timer started.</summary>
    public void StartFadeIn(int category, int ms) => fades[category] = new Fade(category, FadeIn, mix.Volume(category), clockMs(), ms);

    /// <summary>StartFadeOut @0x14177dea0 (:1755-1764): mode 2, the volume the category has now, the timer started.</summary>
    public void StartFadeOut(int category, int ms) => fades[category] = new Fade(category, FadeOut, mix.Volume(category), clockMs(), ms);

    /// <summary>UpdateFades: a timer that no longer runs (Timer::IsRunning: its period is not 0 and the elapsed time is less than
    /// it) ends its fade with the final volume (0 for a fade out, the stored volume for a fade in) and drops it; a running fade
    /// sets (1 - ratio) * volume or ratio * volume. Returns true when a volume was set.</summary>
    public bool Update()
    {
        if (fades.Count == 0) return false;
        long now = clockMs();
        bool set = false;
        foreach (var f in fades.Values.ToArray())
        {
            long elapsed = f.PeriodMs == 0 ? 0 : now - f.StartMs;
            bool running = f.PeriodMs != 0 && elapsed < f.PeriodMs;
            if (!running)
            {
                mix.SetCategoryVolume(f.Category, f.Mode == FadeOut ? 0f : f.Volume);
                fades.Remove(f.Category);
            }
            else
            {
                float ratio = (float)elapsed / f.PeriodMs;
                mix.SetCategoryVolume(f.Category, f.Mode == FadeOut ? (1f - ratio) * f.Volume : ratio * f.Volume);
            }
            set = true;
        }
        return set;
    }
}
