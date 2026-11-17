namespace Ms2.Video;

/// <summary>
/// Plays a <see cref="UsmVideo"/> against a clock: a worker thread decodes a few frames ahead into its own buffers and
/// <see cref="Update"/> hands out the frame due at the given time. Large movies (1080p) decode in about a frame's time,
/// so the work stays off the caller's thread.
/// </summary>
public sealed class VideoPlayback : IDisposable
{
    readonly UsmVideo video;
    readonly Queue<YuvFrame> ready = new(), free = new();
    readonly object gate = new();
    readonly Thread worker;
    readonly int depth;
    volatile bool stop;
    bool decoderDone;
    YuvFrame shown;

    /// <summary>Restart at the end instead of stopping (the client's VideoPlayer.setLoop(true)).</summary>
    public bool Loop { get; }
    /// <summary>The last frame was shown and no more will come (never true while looping).</summary>
    public bool Finished { get; private set; }
    /// <summary>Times the movie started over.</summary>
    public int Loops { get; private set; }
    public UsmVideo Video => video;

    public VideoPlayback(UsmVideo video, bool loop, int depth = 4)
    {
        this.video = video ?? throw new ArgumentNullException(nameof(video));
        Loop = loop;
        this.depth = Math.Max(2, depth);
        worker = new Thread(Run) { IsBackground = true, Name = "usm decode" };
        worker.Start();
    }

    void Run()
    {
        double offset = 0, step = video.FrameRate > 0 ? 1 / video.FrameRate : 1 / 30.0;
        try
        {
            while (!stop)
            {
                YuvFrame slot;
                lock (gate)
                {
                    while (!stop && ready.Count >= depth) Monitor.Wait(gate);
                    if (stop) return;
                    slot = free.Count > 0 ? free.Dequeue() : null;
                }
                var f = video.Next();
                if (f == null)
                {
                    if (!Loop || video.Decoder.FramesDecoded == 0) break;
                    offset += video.Decoder.FramesDecoded * step;
                    video.Rewind();
                    lock (gate) { if (slot != null) free.Enqueue(slot); Loops++; }
                    continue;
                }
                if (slot == null || slot.Y.Length != f.Y.Length || slot.Cb.Length != f.Cb.Length)
                    slot = new YuvFrame(f.Width, f.Height, f.LumaStride, f.LumaHeight, f.ChromaStride, f.ChromaHeight);
                Buffer.BlockCopy(f.Y, 0, slot.Y, 0, f.Y.Length);
                Buffer.BlockCopy(f.Cb, 0, slot.Cb, 0, f.Cb.Length);
                Buffer.BlockCopy(f.Cr, 0, slot.Cr, 0, f.Cr.Length);
                slot.Time = offset + (video.Decoder.FramesDecoded - 1) * step;
                lock (gate) { ready.Enqueue(slot); Monitor.PulseAll(gate); }
            }
        }
        catch (Exception e) when (e is IndexOutOfRangeException or InvalidOperationException)
        {
            // a corrupt stream ends the movie
        }
        lock (gate) { decoderDone = true; Monitor.PulseAll(gate); }
    }

    /// <summary>
    /// The frame to show at <paramref name="seconds"/> since the start, or null if the shown one is still current.
    /// A returned frame stays valid until the next call.
    /// </summary>
    public YuvFrame Update(double seconds)
    {
        lock (gate)
        {
            YuvFrame next = null;
            while (ready.Count > 0 && (ready.Peek().Time <= seconds || shown == null && next == null))
            {
                if (next != null) free.Enqueue(next);
                next = ready.Dequeue();
            }
            if (next != null)
            {
                if (shown != null) free.Enqueue(shown);
                shown = next;
                Monitor.PulseAll(gate);
            }
            else if (decoderDone && ready.Count == 0) Finished = true;
            return next;
        }
    }

    /// <summary>Waits (up to <paramref name="timeoutMs"/>) until the first frame is decoded; for tools and tests.</summary>
    public bool WaitFirstFrame(int timeoutMs)
    {
        lock (gate)
        {
            var end = Environment.TickCount64 + timeoutMs;
            while (ready.Count == 0 && shown == null && !decoderDone)
            {
                long left = end - Environment.TickCount64;
                if (left <= 0 || !Monitor.Wait(gate, (int)left)) break;
            }
            return ready.Count > 0 || shown != null;
        }
    }

    public void Dispose()
    {
        stop = true;
        lock (gate) Monitor.PulseAll(gate);
        worker.Join(2000);
    }
}
