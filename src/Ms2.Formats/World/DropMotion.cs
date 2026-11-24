using System.Numerics;

namespace Ms2.Formats.World;

/// <summary>The Client::Constants a CDropItem reads in its constructor (dropitem.cpp:190); every one is 0 until constants.xml sets it.</summary>
public sealed class DropConstants
{
    /// <summary>DropItemTargetZPos: the height above the picker the drop flies to.</summary>
    public float TargetZ;
    /// <summary>DropItemMaxLength / DropMoneyMaxLength: the pickup range of an item and of meso.</summary>
    public float MaxLength, MoneyMaxLength;
    /// <summary>DropItemPickUpVel / DropItemPickUpGravity: the fly-to-picker arc.</summary>
    public float PickUpVel, PickUpGravity;
    /// <summary>DropItemPickUpCompleteRotateTime / ...Vel: the hover over the picker, and its spin.</summary>
    public float RotateTime, RotateVel;
    /// <summary>DropItemPickFailTime / DropItemPickFailHeight: the hop of a refused pickup.</summary>
    public float FailTime, FailHeight;
    /// <summary>PetTrapDropVisibleDelay: how long a pet trap's drop stays hidden and unpickable (ms).</summary>
    public uint PetTrapVisibleDelayMs;

    public static DropConstants From(ClientConstants c) => new()
    {
        TargetZ = c.Float("DropItemTargetZPos", 0f), MaxLength = c.Float("DropItemMaxLength", 0f), MoneyMaxLength = c.Float("DropMoneyMaxLength", 0f),
        PickUpVel = c.Float("DropItemPickUpVel", 0f), PickUpGravity = c.Float("DropItemPickUpGravity", 0f),
        RotateTime = c.Float("DropItemPickUpCompleteRotateTime", 0f), RotateVel = c.Float("DropItemPickUpCompleteRotateVel", 0f),
        FailTime = c.Float("DropItemPickFailTime", 0f), FailHeight = c.Float("DropItemPickFailHeight", 0f),
        PetTrapVisibleDelayMs = (uint)Math.Max(0f, c.Float("PetTrapDropVisibleDelay", 0f)),
    };
}

/// <summary>What the source of a drop says about the throw (the npc row's drop block, CNpcData::m_drop; the defaults of SetState).</summary>
public struct DropLaunch
{
    public float Height;
    public float DistanceBase;
    public uint DistanceRandom;
    public float Speed;
    /// <summary>The source's facing (meso thrown by a player lands behind it): null for any other source.</summary>
    public Vector3? SourceForward;
    public static DropLaunch Default => new() { Height = 0f, DistanceBase = 50f, DistanceRandom = 100, Speed = 100f };
}

/// <summary>Unk_Random (questmasktable.cpp:350-450): the three-word generator a drop seeds with its object id.</summary>
public struct DropRandom
{
    uint s1, s2, s3;
    public DropRandom(uint a, uint b, uint c) { s1 = a | 0x100000; s2 = b | 0x1000; s3 = c | 0x10; }
    /// <summary>Unk_Random(int nSeed) @0x140768210 (questmasktable.cpp:400-407): three Unk_RandomStep values (the LCG
    /// x * 0x343fd + 0x269ec3, :374-379) through SetState.</summary>
    public static DropRandom Seeded(int seed)
    {
        uint a = unchecked((uint)seed); seed = unchecked(seed * 0x343fd + 0x269ec3);
        uint b = unchecked((uint)seed); seed = unchecked(seed * 0x343fd + 0x269ec3);
        uint c = unchecked((uint)seed);
        return new DropRandom(a, b, c);
    }
    public uint Next()
    {
        s1 = (s1 & 0xfffffffe) << 12 ^ (s1 << 13 ^ s1) >> 19;
        s2 = (s2 & 0xfffffff8) << 4 ^ (s2 << 2 ^ s2) >> 25;
        s3 = (s3 & 0xfffffff0) << 17 ^ (s3 << 3 ^ s3) >> 11;
        return s1 ^ s2 ^ s3;
    }
}

/// <summary>Unk_ArcFlight (arccontroller.h): the launch solution the drop flies along.</summary>
public struct DropArc
{
    public Vector3 Origin, Direction, Target;
    public float Gravity, FlightScale, AngleX, AngleY, Speed;

    /// <summary>Unk_ArcFlight::FUN_141479ed0 (mapmetamanager.cpp:81): the low-angle arc from origin to target at the speed under gravity; a target too far for the speed retries at 1.16 times it.</summary>
    public void Solve(Vector3 origin, Vector3 target, float speed, float gravity)
    {
        for (int tries = 0; tries < 64; tries++)
        {
            float dx = target.X - origin.X, dy = target.Y - origin.Y;
            float distance = MathF.Sqrt(dx * dx + dy * dy);
            if (MathF.Abs(distance) < 1e-4f) { Speed = speed; return; }
            float dz = target.Z - origin.Z, speedSq = speed * speed, distSq = distance * distance;
            float disc = distSq - gravity * gravity * distSq * distSq / (speedSq * speedSq) + 2f * gravity * distSq * dz / speedSq;
            if (disc < 0f) { speed *= 1.16f; continue; }
            float tangent = speedSq * ((-distance - MathF.Sqrt(disc)) / (gravity * distSq));
            float angle = MathF.Atan(tangent);
            Speed = speed; Direction = Horizontal(origin, target); Target = target; Origin = origin; Gravity = gravity;
            AngleX = MathF.Cos(angle); AngleY = MathF.Sin(angle);
            FlightScale = MathF.Cos(angle) <= 0f ? 0f : distance / (AngleX * speed);
            return;
        }
        Speed = speed;
    }

    /// <summary>GetHorizontalDirection(from, to, unitize): the planar direction, unit length (zero when the points share their x, y).</summary>
    public static Vector3 Horizontal(Vector3 from, Vector3 to)
    {
        var d = new Vector3(to.X - from.X, to.Y - from.Y, 0f);
        float l = d.Length();
        return l < 1e-6f ? Vector3.Zero : d / l;
    }

    /// <summary>The position at time t (the state functions' kOffset: planar speed x cos x t, height 0.5 g t^2 + speed x sin x t).</summary>
    public Vector3 At(float t) => new(Origin.X + Direction.X * Speed * AngleX * t, Origin.Y + Direction.Y * Speed * AngleX * t,
        Origin.Z + 0.5f * Gravity * t * t + Speed * AngleY * t);
}

/// <summary>
/// CDropItem's motion (dropitem.cpp): state 6 created, 1 flies out of its source, 0 rests and bobs, 2 flies to the picker, 3 hovers over the
/// picker, 4 sinks and goes, 5 hops back after a refused pickup. Positions are the client's (z up). Built on the packet position;
/// <see cref="Advance"/> allocates nothing.
/// </summary>
public sealed class DropMotion
{
    public const int Created = 6, FlyOut = 1, Rest = 0, FlyToPicker = 2, Hover = 3, Sink = 4, Regrow = 5;

    readonly DropConstants k;
    readonly DropStyle style;
    readonly int objectId;
    readonly bool pet;

    public int State { get; private set; } = Created;
    /// <summary>The drop is gone (the sink ended, or its picker left the field).</summary>
    public bool Done { get; private set; }
    public Vector3 Position { get; private set; }
    /// <summary>The spin about each axis in degrees (FUN_1411925e0: axis x speed x 360 x seconds in the state); zero for a pet item.</summary>
    public Vector3 Spin { get; private set; }
    DropArc arc;
    public DropArc Arc => arc;
    float elapsed, spawn, grow, pickVel;
    Vector3 posGround, posGrow;
    /// <summary>Where the drop was placed (CDropItem m_posOrigin): the range test's centre.</summary>
    public Vector3 Origin { get; private set; }

    public DropMotion(DropConstants constants, DropStyle style, int objectId, bool petItem)
    {
        k = constants; this.style = style; this.objectId = objectId; pet = petItem; pickVel = constants.PickUpVel;
    }

    /// <summary>The packet position raised by 70 (OnFieldAddItem), the place the drop is created at.</summary>
    public static Vector3 Created70(Vector3 packet) => new(packet.X, packet.Y, packet.Z + 70f);

    /// <summary>
    /// SetState(1) (dropitem.cpp:431-592) from the position OnFieldAddItem placed it at. <paramref name="ground"/> is the stand-in of the
    /// client's downward ray casts (FUN_14147a500, CPhysXHelper::CastRay): the ground height under a point, or null. A drop with no
    /// ground to find stays where it is (the client's last branch: flight 0, state 0). <paramref name="fixedThrow"/> is m_n28c.
    /// </summary>
    public void Launch(Vector3 placed, in DropLaunch launch, bool fixedThrow, bool meso, Func<Vector3, float?> ground)
    {
        Origin = new Vector3(placed.X, placed.Y, placed.Z - 70f);
        var self = new Vector3(placed.X, placed.Y, placed.Z + launch.Height);
        if (pet) self = Origin;
        Position = self;
        var kBase = new Vector3(self.X, self.Y, self.Z + 85f);
        var rand = new DropRandom((uint)objectId, (uint)(objectId + 0x7fff), (uint)(objectId * 0x7fff));
        Vector3 target;
        if (fixedThrow) target = self + Vector3.One;   // m_n28c: NiPoint3::ALL_ONES (dropitem.cpp:143), one unit on each axis
        else if (meso && launch.SourceForward is { } fwd)
        {
            var f = new Vector3(fwd.X, fwd.Y, 0f);
            target = kBase - f * 50f;
        }
        else
        {
            float rot = (rand.Next() % 0x168) * MathF.PI / 180f;
            var dir = new Vector3(-MathF.Sin(rot), MathF.Cos(rot), 0f);      // Point3::UNIT_Y rotated about z
            float dist = launch.DistanceBase + (launch.DistanceRandom == 0 ? 0 : rand.Next() % launch.DistanceRandom);
            target = kBase + dir * dist;
        }
        float gravity = -15f - rand.Next() % 0x19;
        float hitZ;
        Vector3 start = self;
        float? g = ground?.Invoke(new Vector3(target.X, target.Y, kBase.Z));
        if (g is { } z1) { hitZ = z1 + style.RestHeight; arc.Solve(start, new Vector3(target.X, target.Y, hitZ), launch.Speed, gravity); }
        else if (ground?.Invoke(new Vector3(kBase.X, kBase.Y, kBase.Z)) is { } z2)
        {
            hitZ = z2 + style.RestHeight;
            arc = new DropArc { Origin = start, Direction = Vector3.Zero, Target = new Vector3(kBase.X, kBase.Y, hitZ), Gravity = -9.8f, FlightScale = 5f, AngleX = 0f, AngleY = 0f, Speed = launch.Speed };
        }
        else
        {
            arc = new DropArc { Origin = start, Direction = Vector3.Zero, Target = start, Gravity = -9.8f, FlightScale = 0f, Speed = launch.Speed };
            Enter(Rest);
            return;
        }
        if (250f < arc.Target.Z - Origin.Z) arc.Target = new Vector3(Origin.X, Origin.Y, Origin.Z + 85f);
        State = FlyOut; elapsed = 0f; spawn = 0f;
    }

    void Enter(int state)
    {
        elapsed = 0f; spawn = 0f; State = state;
    }

    /// <summary>OnPickedUp's SetState(2): the drop flies to the picker standing at <paramref name="picker"/>. Only a resting drop takes it.</summary>
    public void PickedUp(Vector3 picker, uint randomHalf)
    {
        if (State != Rest) return;
        Enter(FlyToPicker);
        posGround = picker;
        var goal = new Vector3(picker.X, picker.Y, picker.Z + k.TargetZ);
        uint half = (uint)pickVel >> 1;
        pickVel = half + (half == 0 ? 0 : randomHalf % half);
        arc.Solve(Position, goal, pickVel, k.PickUpGravity);
    }

    /// <summary>FUN_1411911a0 = SetState(5): a refused pickup hops back.</summary>
    public void PickFailed()
    {
        if (State == Regrow) return;
        Enter(Regrow); grow = 0f; posGrow = Position;
    }

    /// <summary>
    /// One frame (CDropItem::vfunc_53 and the state function). <paramref name="picker"/> is the picker's position, null when the picker is gone
    /// (a drop in a pickup state then goes: WorldService::FUN_141bbb7f0).
    /// </summary>
    public void Advance(float dt, Vector3? picker)
    {
        if (Done) return;
        if (State == Created) Enter(FlyOut);
        spawn += dt;
        switch (State)
        {
            case FlyOut: AdvanceFly(dt); break;
            case Rest: AdvanceRest(dt); break;
            case FlyToPicker:
                if (picker is not { } p2) { Done = true; return; }
                AdvanceToPicker(dt, p2); break;
            case Hover:
                if (picker is not { } p3) { Done = true; return; }
                AdvanceHover(dt, p3); break;
            case Sink:
                if (picker is not { } p4) { Done = true; return; }
                AdvanceSink(dt, p4); break;
            case Regrow: AdvanceRegrow(dt); break;
        }
    }

    void AdvanceFly(float dt)
    {
        if (elapsed > arc.FlightScale) { Enter(Rest); Position = arc.Target; return; }
        elapsed += dt * 10f;
        Position = arc.At(elapsed);
        Spin = Axis(style.DropAni, style.DropAniSpeed);
    }

    void AdvanceRest(float dt)
    {
        elapsed += dt;
        if (style.BouncePeriod > 0f)
        {
            if (elapsed > style.BouncePeriod) elapsed = 0f;
            float s = -1f * MathF.Sin(elapsed * MathF.PI * 2f / style.BouncePeriod) * 0.5f;
            Position = new Vector3(arc.Target.X, arc.Target.Y, arc.Target.Z + style.BounceHeight * s);
        }
        Spin = Axis(style.BeAni, style.BeAniSpeed);
    }

    void AdvanceToPicker(float dt, Vector3 picker)
    {
        if (elapsed > arc.FlightScale) { Enter(Hover); return; }
        elapsed += dt * 10f;
        var o = arc.At(elapsed) - arc.Origin;
        Position = arc.Origin + o + (picker - posGround);
        Spin = Axis(style.GetAni, style.GetAniSpeed);
    }

    void AdvanceHover(float dt, Vector3 picker)
    {
        elapsed += dt;
        if (elapsed > k.RotateTime) { Enter(Sink); return; }
        Position = new Vector3(picker.X, picker.Y, picker.Z + k.TargetZ);
        Spin = pet ? Vector3.Zero : new Vector3(0f, 0f, k.RotateVel * 360f * spawn);
    }

    void AdvanceSink(float dt, Vector3 picker)
    {
        float dx = picker.X - Position.X, dy = picker.Y - Position.Y, dz = picker.Z - Position.Z;
        float len = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        if (len < 100f || len > 300f) { Done = true; return; }
        Position = new Vector3(picker.X, picker.Y, Position.Z - dt * 1000f);
    }

    void AdvanceRegrow(float dt)
    {
        grow += dt;
        float ratio = k.FailTime > 0f ? grow / k.FailTime : 1f;
        if (ratio >= 1f) { grow = 0f; State = Rest; elapsed = 0f; return; }
        Position = new Vector3(posGrow.X, posGrow.Y, posGrow.Z + MathF.Sin(MathF.PI * ratio) * k.FailHeight);
    }

    Vector3 Axis(float[] axis, float degrees) => pet ? Vector3.Zero : new Vector3(axis[0], axis[1], axis[2]) * (degrees * 360f * spawn);
}
