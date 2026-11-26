namespace Ms2.Audio;

/// <summary>
/// An event's fade-in (FMOD_EVENTPROPERTY_FADEIN, milliseconds; <see cref="FevEvent.FadeIn"/>) baked into the PCM its sound
/// plays, so the start rises from silence as FMOD's does and nothing is set per frame. FMOD ramps the event's volume by the
/// time since its start, min(1, elapsed / fade-in), at each event system update. The client calls update once a frame
/// (CSoundService::OnTick, soundservice.cpp:1001-1012). Its renders of the meso pickup event in a real-time mixer with the
/// client's 1024 x 10 DSP buffer show this: with a 20 ms fade-in the start holds at 0.80-0.85 (one 16.7 ms frame in) until
/// the next update, and with 50 ms at 0.30-0.32. The baked fade is that same line, min(1, t / fade-in), at every sample
/// instead of every frame. Not baked: in 6 of 10 starts the renders also mute the first ~2.25 ms after the meso pickup's
/// quiet 6.5 ms lead-in, whatever the fade-in's length (5, 10 or 20 ms). That mute is the gain of 0 that lasts until the
/// first update after the start reaches the mix, a timing effect. The other 4 starts play in full. The fade-out (+0x90) is
/// not baked: the client's stops are immediate (CSoundEvent::StopOrReset @0x14177a920 and ~CSoundEvent @0x14177a800 call
/// stop(true)).
/// </summary>
public static class EventFadeIn
{
    /// <summary>The fade-in's length in frames at <paramref name="rate"/>; 0 without one.</summary>
    public static int Frames(int fadeInMs, int rate) => fadeInMs <= 0 || rate <= 0 ? 0 : (int)((long)fadeInMs * rate / 1000);

    /// <summary>The gain at frame <paramref name="frame"/> of a fade-in <paramref name="fadeFrames"/> long.</summary>
    public static float Gain(int frame, int fadeFrames) => frame >= fadeFrames ? 1f : (float)frame / fadeFrames;

    /// <summary>
    /// Where a loop baked by <see cref="Bytes"/> (looping over [<paramref name="loopBegin"/>, <paramref name="loopEnd"/>) frames
    /// at <paramref name="rate"/>) is <paramref name="elapsed"/> seconds after its start (s): the first pass, fade-in and all,
    /// then the loop range over and over. A loop stopped while the listener is past its silent distance plays again from
    /// here, since FMOD's event kept its time while it could not be heard. The FMOD probe's renders show it: with 4 software
    /// channels and 4 louder loops started, AMB/11100071 went virtual for 3.0 s while its channel's position ran on (1924 to
    /// 4883 ms), then came back in one step at its full level with no fade-in, its first 20 ms matching the uninterrupted
    /// render of the same loop 29 ms on (a residual of 0.000); a loop out of its range stays real at volume 0 and plays on,
    /// back at its full level the same way.
    /// </summary>
    public static double LoopPosition(double elapsed, int loopBegin, int loopEnd, int rate)
    {
        if (rate <= 0 || elapsed <= 0) return 0;
        double e = elapsed * rate;
        int span = loopEnd - loopBegin;
        if (e < loopEnd || span <= 0) return Math.Min(e, Math.Max(loopEnd, 0)) / rate;
        return (loopBegin + (e - loopBegin) % span) / rate;
    }

    /// <summary>
    /// The 16-bit little-endian PCM bytes of <paramref name="pcm"/> with a fade-in of <paramref name="fadeInMs"/>, and the loop
    /// range in frames. Without a fade these are the plain bytes, and a loop runs over the whole sound. A one-shot is faded
    /// in place. A loop gets the faded first pass plus one more unfaded pass: its first n frames are faded, then the loop runs
    /// over frames [n, n + frames). That range is the sound rotated to start at n, so every pass after the first is heard
    /// unfaded, and the cost is n extra frames.
    /// </summary>
    public static byte[] Bytes(Pcm16 pcm, int fadeInMs, bool loops, out int loopBegin, out int loopEnd)
    {
        int frames = pcm.Frames, ch = pcm.Channels, n = Frames(fadeInMs, pcm.Rate);
        loopBegin = 0;
        loopEnd = frames;
        if (n <= 0 || frames == 0) return pcm.ToBytes();
        int total = loops ? n + frames : frames;
        var s = pcm.Samples;
        var b = new byte[total * ch * 2];
        for (int i = 0; i < total; i++)
        {
            int src = (i % frames) * ch, o = i * ch * 2;
            float g = Gain(i, n);
            for (int c = 0; c < ch; c++, o += 2)
            {
                int v = g < 1f ? (int)MathF.Round(s[src + c] * g) : s[src + c];
                b[o] = (byte)v;
                b[o + 1] = (byte)(v >> 8);
            }
        }
        if (loops) { loopBegin = n; loopEnd = n + frames; }
        return b;
    }
}
