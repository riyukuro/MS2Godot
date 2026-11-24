using System.Numerics;
using Ms2.Net.Packets;

namespace Ms2.Net.Replay;

/// <summary>
/// The npc's jump arc mover (CNpc +0xdb0: Set @0x14160bde0 / @0x14160beb0, unit_14160a7f0.cpp:198-220; Advance @0x14160bf90,
/// unit_14160bf90.cpp:38-54), started by a sync block with sequence -2 (Jump_A) or -3 (Jump_B), CNpc::ApplySyncBlock
/// (unit_14108e1e0_npcactorandvibrate.cpp:916-929). While it moves, the npc's update takes its position and target yaw from it
/// instead of the velocity (CNpc::vfunc_53, :2360-2370) and a sync block neither places nor turns the npc (:961). Client space
/// (z up), degrees.
/// </summary>
public sealed class NpcJumpArc
{
    /// <summary>The time a jump block without the arc data takes (the short Set's m_fWidth).</summary>
    public const float RelativeTime = 0.45f;
    /// <summary>The peak height per unit of distance between the end points: 150 / 212.132 (both Set overloads).</summary>
    public const float HeightPerDistance = 150f / 212.132f;

    /// <summary>m_bActive: the arc runs; cleared by the advance that reaches its time.</summary>
    public bool Moving { get; private set; }
    public Vector3 Start { get; private set; }
    public Vector3 End { get; private set; }
    public float Elapsed { get; private set; }
    /// <summary>The arc's time in seconds (m_fWidth).</summary>
    public float Duration { get; private set; }
    /// <summary>The peak above the straight line between the ends (m_fLength).</summary>
    public float Height { get; private set; }
    /// <summary>The jump's sequence name (m_strLabel), empty once the arc ended; StateJumpNpc plays it (stateidle.cpp:1014-1021).</summary>
    public string Name { get; private set; } = "";

    /// <summary>The long Set (@0x14160beb0): <paramref name="time"/> seconds, the peak scaled by <paramref name="scale"/>.</summary>
    public void Set(Vector3 from, Vector3 to, float time, float scale, string name)
    {
        Elapsed = 0; Moving = true; Start = from; End = to;
        Height = HeightPerDistance * (from - to).Length() * scale;
        Duration = time; Name = name;
    }

    /// <summary>The short Set (@0x14160bde0): <see cref="RelativeTime"/> and an unscaled peak.</summary>
    public void Set(Vector3 from, Vector3 to, string name) => Set(from, to, RelativeTime, 1f, name);

    /// <summary>
    /// The arc of a jump block (ApplySyncBlock :916-929): with the absolute byte from the block's from point to its to point, at its time
    /// and scale; without it from the block position (already 4 up, :914) to the to point. The to point is put 2 up in both.
    /// <paramref name="position"/> is the block position as the client places it (z + 4). False when the block is no jump.
    /// </summary>
    public bool Begin(NpcSync block, Vector3 position)
    {
        if (block.Sequence is not (-2 or -3)) return false;
        string name = block.Sequence == -2 ? "Jump_A" : "Jump_B";
        var to = block.JumpTo + new Vector3(0, 0, 2);
        if (block.JumpAbsolute) Set(block.JumpFrom, to, block.JumpWidth, block.JumpScale, name);
        else Set(position, to, name);
        return true;
    }

    /// <summary>
    /// Advance (@0x14160bf90): the time goes on by <paramref name="dt"/>, clamped at the arc's time, where the arc ends; the point is the
    /// straight line's at the time's share, lifted by Height * 4t(1 - t). <paramref name="yaw"/> becomes 90 + atan2(dy, dx) of the ends
    /// in degrees, and is left as it is when the ends are the same point. A time of 0 is at the end at once.
    /// </summary>
    public Vector3 Advance(float dt, ref float yaw)
    {
        Elapsed += dt;
        if (!(Elapsed < Duration)) { Elapsed = Duration; Name = ""; Moving = false; }
        float t = Duration == 0 ? 1f : Elapsed / Duration;
        var p = (1f - t) * Start + t * End;
        p.Z += Height * 4f * t * (1f - t);
        var d = End - Start;
        if (d.LengthSquared() > 0) yaw = 90f + MathF.Atan2(d.Y, d.X) * 180f / 3.1415927f;
        return p;
    }
}
