using System.Numerics;

namespace Ms2.Formats.World;

/// <summary>
/// The strip of a lightning part (LightningEffect, lightningeffectparam.cpp; type 0x10), without the engine: at each update
/// (<see cref="Rebuild"/>) a jagged path from the first to the last point of the target's point list and its two edge rows.
/// All points are world points, client space (the client hangs the strip on the effect service's world root, vfunc_4 :277).
/// The rows live in arrays kept at their largest size (<see cref="MaxRow"/>), so a rebuild allocates nothing.
/// </summary>
public sealed class LightningPath
{
    /// <summary>BuildPath's walk stops at 100 points, then the end is added.</summary>
    public const int MaxRow = 101;

    readonly EffectStripParam param;
    readonly Vector3[] path = new Vector3[MaxRow], left = new Vector3[MaxRow], right = new Vector3[MaxRow];
    uint lastTick;
    ulong sequence;

    public LightningPath(EffectStripParam param) => this.param = param;

    /// <summary>The points in each edge row after the last rebuild (0 before one).</summary>
    public int Count { get; private set; }
    /// <summary>The left row (path + side * width / 2) and the right row (path - side * width / 2).</summary>
    public ReadOnlySpan<Vector3> Left => left.AsSpan(0, Count);
    public ReadOnlySpan<Vector3> Right => right.AsSpan(0, Count);
    /// <summary>The walked path (the start, the joints, the end).</summary>
    public ReadOnlySpan<Vector3> Path => path.AsSpan(0, Count);
    /// <summary>The distance class of the last rebuild (0 near: texturePathA, 1 mid: B, 2 far: C) and the texture picked in
    /// it, -1 when that list is empty.</summary>
    public int TextureClass { get; private set; }
    public int TextureIndex { get; private set; } = -1;

    /// <summary>The texture list of a distance class.</summary>
    public string[] Textures(int textureClass) => textureClass switch
    {
        0 => param.TexturesA,
        1 => param.TexturesB,
        _ => param.TexturesC,
    };

    /// <summary>
    /// LightningEffect::Rebuild @0x14144cc30 (lightningeffectparam.cpp:312) for the points <paramref name="first"/> and
    /// <paramref name="last"/> (a list of fewer than 2 points does nothing; the caller passes its first and last):
    /// <list type="bullet">
    /// <item>The distance class: 0 up to 256, 1 up to 512, else 2.</item>
    /// <item>With textureChangeTick set, nothing until that many ms passed since the last rebuild (<paramref name="nowMs"/>,
    /// GetTickCountMs2); the strip keeps its last shape and texture.</item>
    /// <item>The texture: the class's next one in order with textureSequence (the count starts at 0, so the first is index
    /// 1), else rand() % count; an empty list picks none (the client indexes past an empty array there).</item>
    /// <item>The path (<see cref="BuildPath"/>) and the edge rows.</item>
    /// </list>
    /// <paramref name="camera"/> is the camera's world direction (CameraMgr::FUN_140af0250: NiCamera::GetWorldDirection,
    /// zero without a camera), <paramref name="rand"/> the CRT rand (0..32767). True when it rebuilt.
    /// </summary>
    public bool Rebuild(Vector3 first, Vector3 last, Vector3 camera, uint nowMs, Func<int> rand)
    {
        float dist = (last - first).Length();
        int cls = 2;
        if (!(dist > 256f)) cls = 0;
        else if (!(dist > 512f)) cls = 1;
        if (param.TextureChangeTick != 0)
        {
            if (nowMs < lastTick + param.TextureChangeTick) return false;
            lastTick = nowMs;
        }
        var list = Textures(cls);
        int index = -1;
        if (list.Length > 0)
        {
            if (param.TextureSequence)
            {
                sequence = (sequence + 1) % (ulong)list.Length;
                index = (int)sequence;
            }
            else index = (int)((ulong)(long)rand() % (ulong)list.Length);
        }
        TextureClass = cls;
        TextureIndex = index;
        BuildPath(first, last, camera, rand);
        return true;
    }

    /// <summary>
    /// LightningEffect::BuildPath @0x14144d670 (lightningeffectparam.cpp:411): a walk from <paramref name="start"/> toward
    /// <paramref name="end"/>. Each step: the x/y to the end turned by the spread (fDx = R * spread * y; y += R * spread * x;
    /// x += fDx, each R = FUN_14109ace0: -1 + 2 * rand() / 32767), unitized as a 3d vector (its z the remaining z), its x and
    /// y times jointLen; the point's z the start's plus the direction's z times the point's distance along the direction.
    /// The walk ends at 100 points or once the end is at most 2 jointLen away or the point is not ahead of the start; then
    /// the end. The edge rows: each point plus and minus the flat start-to-end direction crossed with the camera direction
    /// (not unitized) times width / 2.
    /// </summary>
    void BuildPath(Vector3 start, Vector3 end, Vector3 camera, Func<int> rand)
    {
        int n = 0;
        var cur = start;
        var delta = end - start;
        var dir = Unitize(delta);
        path[n++] = start;
        float spread = param.Spread, joint = param.JointLen;
        float along, remain;
        do
        {
            var step = end - cur;
            float dx = R(rand) * spread * step.Y;
            step.Y += R(rand) * spread * step.X;
            step.X += dx;
            step = Unitize(step);
            cur.X += step.X * joint;
            cur.Y += step.Y * joint;
            along = Vector3.Dot(dir, cur - start);
            cur.Z = start.Z + dir.Z * along;
            path[n++] = cur;
            if (n >= 100) break;
            remain = (end - cur).Length();
        } while (!(remain <= joint * 2f || along <= 0f));
        path[n++] = end;
        var flat = delta;
        flat.Z = 0;
        flat = Unitize(flat);
        var side = Vector3.Cross(flat, camera);
        float half = param.Width * 0.5f;
        for (int i = 0; i < n; i++)
        {
            left[i] = path[i] + side * half;
            right[i] = path[i] + side * -half;
        }
        Count = n;
    }

    /// <summary>FUN_14109ace0: -1 + 2 * rand() / 32767.</summary>
    static float R(Func<int> rand) => -1f + 2f * rand() / 32767f;

    /// <summary>NiPoint3::Unitize: a zero vector stays zero.</summary>
    static Vector3 Unitize(Vector3 v)
    {
        float len = v.Length();
        return len > 1e-6f ? v / len : Vector3.Zero;
    }
}
