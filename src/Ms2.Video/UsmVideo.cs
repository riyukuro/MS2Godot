using Ms2.Video.Mpeg1;
using Ms2.Video.Usm;

namespace Ms2.Video;

/// <summary>
/// A .usm movie ready to play: its first video stream demuxed, the MPEG-1 decoder set up from the stream header, and a
/// clock-driven frame pick. Frames are 4:2:0 YUV (see <see cref="YuvFrame"/>); a frame is valid until the next call.
/// </summary>
public sealed class UsmVideo
{
    public UsmFile File { get; }
    public UsmFile.Stream Stream { get; }
    public Mpeg1Decoder Decoder { get; }

    public int Width { get; }
    public int Height { get; }
    /// <summary>Frames per second: the header's framerate_n / framerate_d, else the MPEG sequence header's.</summary>
    public double FrameRate { get; }
    /// <summary>The header's total_frames (0 if it has none).</summary>
    public int FrameCount { get; }
    /// <summary>Length in seconds (0 if the frame count is unknown).</summary>
    public double Duration => FrameRate > 0 ? FrameCount / FrameRate : 0;
    /// <summary>The file also carries audio (@SFA). It is not decoded.</summary>
    public bool HasAudio => File.HasAudio;

    YuvFrame last;

    UsmVideo(UsmFile file, UsmFile.Stream stream)
    {
        File = file;
        Stream = stream;
        Decoder = new Mpeg1Decoder(stream.Data, stream.Length);
        var h = stream.Header;
        if (h != null && h.Long(0, "mpeg_dcprec") is >= 8 and <= 11 and var prec) Decoder.IntraDcPrecision = (int)prec;
        Decoder.HasHeader();
        Width = h != null && h.Long(0, "disp_width") > 0 ? (int)h.Long(0, "disp_width") : Decoder.Width;
        Height = h != null && h.Long(0, "disp_height") > 0 ? (int)h.Long(0, "disp_height") : Decoder.Height;
        long n = h?.Long(0, "framerate_n") ?? 0, d = h?.Long(0, "framerate_d") ?? 0;
        FrameRate = n > 0 && d > 0 ? (double)n / d : Decoder.FrameRate;
        FrameCount = (int)(h?.Long(0, "total_frames") ?? 0);
    }

    /// <summary>Demuxes a .usm file; null if it has no MPEG-1 video stream.</summary>
    public static UsmVideo Open(byte[] usm)
    {
        var file = UsmFile.Read(usm);
        var stream = file.Video.FirstOrDefault(s => s.Length > 0);
        if (stream == null) return null;
        var v = new UsmVideo(file, stream);
        return v.Decoder.Width > 0 ? v : null;
    }

    /// <summary>The next frame, or null at the end. Its <see cref="YuvFrame.Time"/> is from the header's frame rate.</summary>
    public YuvFrame Next()
    {
        var f = Decoder.Decode();
        if (f != null && FrameRate > 0) f.Time = (Decoder.FramesDecoded - 1) / FrameRate;
        return last = f;
    }

    /// <summary>The start time of the next frame, in seconds.</summary>
    public double NextTime => FrameRate > 0 ? Decoder.FramesDecoded / FrameRate : Decoder.Time;

    /// <summary>
    /// The frame to show at <paramref name="seconds"/> from the start: decodes forward (skipping frames that are already
    /// late). Returns null while the current frame is still the right one, or at the end (see <see cref="Ended"/>).
    /// </summary>
    public YuvFrame FrameAt(double seconds)
    {
        YuvFrame shown = null;
        while (!Decoder.HasEnded && (last == null || NextTime <= seconds))
        {
            var f = Next();
            if (f == null) break;
            shown = f;
        }
        return shown;
    }

    /// <summary>No frames left.</summary>
    public bool Ended => Decoder.HasEnded;

    public void Rewind()
    {
        Decoder.Rewind();
        last = null;
    }
}
