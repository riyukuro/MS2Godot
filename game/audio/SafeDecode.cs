using Godot;
using Ms2.Audio;

namespace Ms2Godot;

/// <summary>
/// MpegDecoder.Decode that never throws: a clip the decoder cannot read logs one named warning (once per clip) and yields null,
/// so the caller skips it and plays that clip silent. The client plays these clips through FMOD, so a null here is a deviation
/// (the one-frame stand-ins and the layer II mono samples above 192 kbps that once failed here are decoded by MpegDecoder frame by
/// frame; MpegFrameSweepTests decodes every bank payload of both data sets).
/// </summary>
public static class SafeDecode
{
    static readonly HashSet<string> warned = new();

    public static Pcm16 Decode(SoundClip clip)
    {
        var pcm = Decode(clip, out var failure);
        if (failure != null) GD.PushWarning(failure);
        return pcm;
    }

    /// <summary>The form for a pool thread: no engine call; <paramref name="failure"/> is the warning to log (the first failure
    /// of the clip only, else null), for the caller to push where an engine call is safe.</summary>
    public static Pcm16 Decode(SoundClip clip, out string failure)
    {
        failure = null;
        try { return MpegDecoder.Decode(clip); }
        catch (Exception ex)
        {
            bool first;
            lock (warned) first = warned.Add(clip.Name ?? "");
            if (first) failure = $"audio: {clip.Name}: decode failed ({ex.GetType().Name}: {ex.Message}), playing silent";
            return null;
        }
    }
}
