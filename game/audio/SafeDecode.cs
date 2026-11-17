using Godot;
using Ms2.Audio;

namespace Ms2Godot;

/// <summary>
/// MpegDecoder.Decode that never throws: a clip the decoder cannot read logs one named warning (once per clip) and yields null,
/// so the caller skips it and plays that clip silent. The client plays these clips through FMOD, so a null here is a deviation;
/// it is the decoder's layer-II leniency gap (NLayer rejects some valid layer-II mono frames), not the bank slice.
/// </summary>
public static class SafeDecode
{
    static readonly HashSet<string> warned = new();

    public static Pcm16 Decode(SoundClip clip)
    {
        try { return MpegDecoder.Decode(clip); }
        catch (Exception ex)
        {
            bool first;
            lock (warned) first = warned.Add(clip.Name ?? "");
            if (first) GD.PushWarning($"audio: {clip.Name}: decode failed ({ex.GetType().Name}: {ex.Message}), playing silent");
            return null;
        }
    }
}
