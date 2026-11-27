namespace Ms2.Ui;

/// <summary>
/// A flag set at any time and read by every reader of one frame alike: the first read of a frame takes what was set and
/// clears it, so every later read of that frame sees the same value and a set after it counts for the next frame. This is
/// the client's world update flag (WorldService m_928: set by FUN_141bc8e00, read by FUN_141bc9a80 for every PCBase,
/// cleared once at the end of the update, worldservice_1.cpp:9842), where the own player and the remote PCs move in one
/// loop.
/// </summary>
public sealed class FrameFlag
{
    bool pending, current;
    ulong frame = ulong.MaxValue;

    public void Set() => pending = true;

    /// <summary>The flag for <paramref name="frameNo"/> (the engine's process frame count).</summary>
    public bool Read(ulong frameNo)
    {
        if (frameNo != frame)
        {
            frame = frameNo;
            current = pending;
            pending = false;
        }
        return current;
    }
}
