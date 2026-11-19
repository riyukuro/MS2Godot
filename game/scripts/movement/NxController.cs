using Godot;

namespace Ms2Godot.Movement;

/// <summary>
/// The client's character controller move: NxCharacter 2.8.4 (NxCharacter64.dll 2.8.4.6; CharacterController.cpp is the
/// same in the 2.8.3 and 2.8.4 SDKs) as GameObjectController::CreateController sets it up (@0x14123f5b0: a capsule, skin
/// 0.01 m, slopeLimit cos 50 deg, stepOffset 0.3 m, CLIMB_EASY) and GameObjectController::Move calls it (@0x14123ff80:
/// minDist 1e-6 m, sharpness 1). Controller::move runs SweepTest::MoveCharacter, an up / side / down sweep of the body's
/// own shape, and when the down pass lands on a triangle steeper than the slope limit it runs the move again from the start
/// as the "walk experiment". Godot's shape casts bracket the first contact by bisection (GodotPhysics3D cast_motion: 8
/// steps), so each sweep is cast three times over the bracket to land on NxSweepCapsuleTriangles' analytic stop.
/// </summary>
public sealed class NxController
{
    /// <summary>NxControllerFlag: the passes that touched something in the last move.</summary>
    [System.Flags]
    public enum Hit { None = 0, Sides = 1, Up = 2, Down = 4 }

    const int MaxIter = 10;                     // MAX_ITER, mMaxIter (CharacterController.cpp:24, :645)
    const float MinDist = 1e-6f * 100f;         // GameObjectController::Move passes 1e-6 m
    const int RefineStages = 3;

    readonly CharacterBody3D body;
    readonly CollisionShape3D shapeNode;
    readonly float skin = MoveConfig.SkinWidth;
    readonly float slopeLimit = Mathf.Cos(Mathf.DegToRad(MoveConfig.SlopeLimitDeg));

    /// <summary>
    /// Bodies this controller's sweeps pass through: the body's own kinematic box (a mount's), which starts inside its capsule.
    /// SweepCapsuleBox reports no hit for an initial overlap (CharacterController.cpp:366-374, d == 0 returns false).
    /// </summary>
    public Rid IgnoreBody;

    // One query and one exclude list for this controller, reused: a new RefCounted query and Array per sweep is finalizable
    // garbage on every physics tick. Query() sets every field the callers vary (the rest stay at a fresh query's defaults),
    // so a cast gives the result a new query would.
    readonly PhysicsShapeQueryParameters3D shapeQuery = new();
    readonly Godot.Collections.Array<Rid> excludeList = new();

    PhysicsShapeQueryParameters3D Query(uint mask, bool ignoreBody)
    {
        excludeList.Clear();
        excludeList.Add(body.GetRid());
        if (ignoreBody && IgnoreBody.IsValid) excludeList.Add(IgnoreBody);
        var q = shapeQuery;
        q.Shape = shapeNode.Shape;
        q.CollisionMask = mask;
        q.Exclude = excludeList;
        q.Margin = 0;
        q.Motion = Vector3.Zero;
        return q;
    }

    /// <summary>ShapeHitCallback: a shape (not another controller) the side or down pass touched, in the order touched,
    /// as its body and the body's shape index.</summary>
    public System.Action<GodotObject, int> ShapeHit;

    /// <summary>Groups (collision layer bits) left out of the sweep: GameObjectController m_nIgnoreGroups.</summary>
    public uint IgnoreGroups;

    /// <summary>onControllerHit (0x140cfaec0) in the last move: the side or down pass touched another controller (the up
    /// pass runs with the callbacks off, :1036).</summary>
    public bool HitController { get; private set; }

    // SweepTest state
    bool validateCallback, walkExperiment, normalizeResponse, hitNonWalkable, validTri;
    Vector3 touchedNormal;

    public NxController(CharacterBody3D body, CollisionShape3D shape)
    {
        this.body = body;
        shapeNode = shape;
    }

    /// <summary>Controller::move (CharacterController.cpp:1205-1336): the body is moved by <paramref name="disp"/>.</summary>
    public Hit Move(Vector3 disp)
    {
        var backup = body.GlobalPosition;
        HitController = false;
        walkExperiment = false;
        var flags = MoveCharacter(disp);
        if (hitNonWalkable)
        {
            // :1300-1312: "a bit slow, but everything else I tried was less convincing"
            walkExperiment = true;
            body.GlobalPosition = backup;
            flags = MoveCharacter(disp);
            walkExperiment = false;
        }
        return flags;
    }

    /// <summary>SweepTest::MoveCharacter (:956-1184); Y is the up direction.</summary>
    Hit MoveCharacter(Vector3 direction)
    {
        hitNonWalkable = false;
        var flags = Hit.None;
        float stepOffset = MoveConfig.StepOffset;
        float originalHeight = body.GlobalPosition.Y;
        if (direction.Y > 0) stepOffset = 0;                                    // :985-986

        var up = Vector3.Zero;
        var down = Vector3.Zero;
        if (direction.Y < 0) down.Y = direction.Y; else up.Y = direction.Y;   // :998
        var side = new Vector3(direction.X, 0, direction.Z);
        if (side != Vector3.Zero) up.Y += stepOffset;                         // :1005-1006

        // UP PASS (:1027-1062): no callbacks; skipped by the walk experiment
        validateCallback = false;
        if (!walkExperiment && DoSweepTest(up, MaxIter, out int n, false, MinDist) && n > 0)
        {
            flags |= Hit.Up;
            // clamp the step offset so the down pass doesn't undo more than was done
            float delta = body.GlobalPosition.Y - originalHeight;
            if (delta < stepOffset) stepOffset = delta;
        }

        // SIDE PASS (:1064-1081)
        validateCallback = true;
        if (DoSweepTest(side, MaxIter, out n, false, MinDist) && n > 0) flags |= Hit.Sides;

        // DOWN PASS (:1083-1179): one iteration, so no sliding
        if (side != Vector3.Zero) down.Y -= stepOffset;                       // :1092-1093, undo the artificial up motion
        if (DoSweepTest(down, 1, out n, true, MinDist) && n > 0)
            flags |= Hit.Down;                                                  // CLIMB_EASY: no constrainedClimbingMode test

        // the slope test on the touched triangle's own normal (USE_CONTACT_NORMAL_FOR_SLOPE_TEST is off)
        if (validTri && direction.Y < 0 && touchedNormal.Y >= 0 && touchedNormal.Y < slopeLimit)
        {
            hitNonWalkable = true;
            if (!walkExperiment) return flags;                                  // :1145, run again by Controller::move
            normalizeResponse = true;
            float pos = body.GlobalPosition.Y;
            float recover = (pos > originalHeight ? pos - originalHeight : 0) + Mathf.Abs(direction.Y);
            float md = recover < MinDist ? recover / MaxIter : MinDist;
            DoSweepTest(new Vector3(0, -recover, 0), MaxIter, out _, false, md);
            normalizeResponse = false;
        }
        return flags;
    }

    /// <summary>SweepTest::DoSweepTest (:822-952): true when the body moved at least once.</summary>
    bool DoSweepTest(Vector3 direction, int maxIter, out int nbCollisions, bool downPass, float minDist)
    {
        nbCollisions = 0;
        if (direction == Vector3.Zero) return false;
        bool hasMoved = false;
        validTri = false;
        var current = body.GlobalPosition;
        var target = current + direction;
        while (maxIter-- > 0)
        {
            var dir = target - current;
            float length = dir.Length();
            if (length < minDist) break;
            dir /= length;
            // From Quake2: "if velocity is against the original velocity, stop dead to avoid tiny oscillations in sloping corners"
            if (dir.Dot(direction) <= 0) break;
            hasMoved = true;

            // the closest contact within the move plus one skin
            var cc = Sweep(current, dir, length + skin);
            if (cc is not { } c)
            {
                current = target;
                break;
            }
            if (IsController(c.Collider))
            {
                // another character controller: UserHitCallback, which leaves the shape hit report alone; a first down hit
                // on one gets more iterations so the body slides off it (:891-903)
                if (validateCallback) HitController = true;
                if (downPass && nbCollisions == 0) maxIter += 9;
            }
            else
            {
                if (c.Mesh) { validTri = true; touchedNormal = c.FaceNormal; }
                if (validateCallback && c.Collider != null) ShapeHit?.Invoke(c.Collider, c.Shape);
            }
            nbCollisions++;
            if (c.Distance > skin) current += dir * (c.Distance - skin);      // :925-928, stop one skin short along the move

            var normal = c.Normal;
            if (walkExperiment)
            {
                // make sure the auto-step doesn't bypass this
                normal.Y = 0;
                normal = normal.Normalized();
            }
            CollisionResponse(ref target, current, dir, normal);
        }
        body.GlobalPosition = current;
        return hasMoved;
    }

    /// <summary>CollisionResponse (:28-53) with bump 0 and friction 1: what is left of the move, along the hit plane.</summary>
    void CollisionResponse(ref Vector3 target, Vector3 current, Vector3 dir, Vector3 normal)
    {
        var reflect = (dir - 2 * dir.Dot(normal) * normal).Normalized();
        var tangent = reflect - reflect.Dot(normal) * normal;
        float amplitude = target.DistanceTo(current);
        if (normalizeResponse) tangent = tangent.Normalized();
        target = current + tangent * amplitude;
    }

    /// <summary>
    /// GameObjectController::Move's checkOverlapCapsule(capsule, NX_ALL_SHAPES, 0xffffffff): the body's own shape where it
    /// stands, against every shape but its own (setCollision(false) around the test), with no skin. The test has no sides,
    /// so the field's one-sided mesh triangles count from behind too (<see cref="TwoSidedSpace"/>).
    /// </summary>
    public bool Overlaps()
    {
        var q = Query(uint.MaxValue, false);
        q.Transform = new Transform3D(Basis.Identity, body.GlobalPosition) * shapeNode.Transform;
        var world = body.GetWorld3D();
        using var found = (Godot.Collections.Array)world.DirectSpaceState.IntersectShape(q, 1);   // the underlying Array is tracked
        return found.Count > 0 || TwoSidedSpace.Overlaps(world, shapeNode.Shape, q.Transform, uint.MaxValue);
    }

    /// <summary>The body's shape with the body at <paramref name="at"/> against every shape of a group in
    /// <paramref name="groups"/>, the field's mesh triangles from either side; the body's own shape is left out.</summary>
    public bool OverlapsAt(Transform3D at, uint groups)
    {
        var q = Query(groups, false);
        q.Transform = at * shapeNode.Transform;
        var world = body.GetWorld3D();
        using var found = (Godot.Collections.Array)world.DirectSpaceState.IntersectShape(q, 1);   // the underlying Array is tracked
        return found.Count > 0 || TwoSidedSpace.Overlaps(world, shapeNode.Shape, q.Transform, groups);
    }

    static bool IsController(GodotObject o) =>
        o is CharacterBody3D || o is Node n && n.HasMeta(MoveConfig.ControllerMeta);

    readonly struct Contact
    {
        public readonly float Distance;
        public readonly Vector3 Normal, FaceNormal;
        public readonly GodotObject Collider;
        public readonly bool Mesh;
        public readonly int Shape;
        public Contact(float d, Vector3 n, GodotObject c, bool mesh, Vector3 face, int shape = -1) { Distance = d; Normal = n; Collider = c; Mesh = mesh; FaceNormal = face; Shape = shape; }
    }

    /// <summary>
    /// NxSweepCapsuleTriangles and the other shape sweeps: the distance along <paramref name="dir"/> to the first contact of
    /// the body's shape, within <paramref name="length"/>. The bracket of each cast is cast again, three stages in all.
    /// </summary>
    Contact? Sweep(Vector3 from, Vector3 dir, float length)
    {
        var space = body.GetWorld3D().DirectSpaceState;
        var q = Query(body.CollisionMask & ~IgnoreGroups, true);                // move(disp, m_nGroups & ~m_nIgnoreGroups)
        var local = shapeNode.Transform;
        float safe = 0, unsafeD = length;
        bool hit = false;
        for (int stage = 0; stage < RefineStages && unsafeD - safe > 1e-7f; stage++)
        {
            float span = unsafeD - safe;
            q.Transform = new Transform3D(Basis.Identity, from + dir * safe) * local;
            q.Motion = dir * span;
            var r = space.CastMotion(q);
            if (r.Length < 2 || r[0] >= 1f)
            {
                if (stage == 0) return null;
                break;                                                          // the stage before already bracketed it
            }
            hit = true;
            unsafeD = safe + r[1] * span;
            safe += r[0] * span;
        }
        if (!hit) return null;

        // what is touched there, and the exact stop: the casts' collision test leaves up to ~1e-3 of the bracket, so the
        // capsule's own gap to the touched point, (segment end lowest along the contact normal) . n - radius, is closed
        // along the move (a plane closes at -dir . n per unit; an edge or vertex converges in a few steps)
        float t = safe;
        Godot.Collections.Dictionary info = null;
        for (int it = 0; it < 4; it++)
        {
            q.Transform = new Transform3D(Basis.Identity, from + dir * t) * local;
            q.Motion = Vector3.Zero;
            q.Margin = Mathf.Max(unsafeD - t, 0) + 0.01f;
            var ri = space.GetRestInfo(q);
            if (ri.Count == 0) { ri.Dispose(); break; }
            info?.Dispose();
            info = ri;
            if (shapeNode.Shape is not CapsuleShape3D cap) break;
            var n0 = (Vector3)ri[PhysicsKeys.Normal];
            var p0 = (Vector3)ri[PhysicsKeys.Point];
            var centre = from + dir * t + local.Origin;
            var half = local.Basis.Y.Normalized() * (cap.Height / 2 - cap.Radius);
            float gap = Mathf.Min((centre + half - p0).Dot(n0), (centre - half - p0).Dot(n0)) - cap.Radius;
            float rate = -dir.Dot(n0);
            if (rate < 0.05f) break;
            float tn = Mathf.Clamp(t + gap / rate, 0, length);
            bool done = Mathf.Abs(tn - t) < 1e-6f;
            t = tn;
            if (done) break;
        }
        if (info == null) return new Contact(safe, -dir, null, false, Vector3.Zero);
        safe = t;
        var normal = (Vector3)info[PhysicsKeys.Normal];
        var point = (Vector3)info[PhysicsKeys.Point];
        var rid = (Rid)info[PhysicsKeys.Rid];
        int shapeIdx = (int)info[PhysicsKeys.Shape];
        var collider = GodotObject.InstanceFromId((ulong)info[PhysicsKeys.ColliderId]);
        info.Dispose();
        bool mesh = PhysicsServer3D.ShapeGetType(PhysicsServer3D.BodyGetShape(rid, shapeIdx)) == PhysicsServer3D.ShapeType.ConcavePolygon;
        var face = mesh ? FaceNormal(rid, shapeIdx, point, dir) ?? normal : normal;
        return new Contact(safe, normal, collider, mesh, face, shapeIdx);
    }

    /// <summary>
    /// mTouched.normal(): the touched triangle's own normal, (v1 - v0) x (v2 - v0). Where the contact is shared by several
    /// triangles (an edge or a vertex) the one most opposed to the sweep is taken.
    /// </summary>
    Vector3? FaceNormal(Rid rid, int shapeIdx, Vector3 point, Vector3 dir)
    {
        var faces = WorldFaces(rid, shapeIdx);
        const float eps = 0.01f;
        Vector3? best = null;
        float bestDot = float.MaxValue;
        for (int f = 0; f + 2 < faces.Length; f += 3)
        {
            Vector3 a = faces[f], b = faces[f + 1], c = faces[f + 2];
            if (point.X < Mathf.Min(a.X, Mathf.Min(b.X, c.X)) - eps || point.X > Mathf.Max(a.X, Mathf.Max(b.X, c.X)) + eps ||
                point.Y < Mathf.Min(a.Y, Mathf.Min(b.Y, c.Y)) - eps || point.Y > Mathf.Max(a.Y, Mathf.Max(b.Y, c.Y)) + eps ||
                point.Z < Mathf.Min(a.Z, Mathf.Min(b.Z, c.Z)) - eps || point.Z > Mathf.Max(a.Z, Mathf.Max(b.Z, c.Z)) + eps)
                continue;
            var n = (c - a).Cross(b - a);                                       // the shape holds (v0, v2, v1)
            if (n.LengthSquared() < 1e-12f) continue;
            n = n.Normalized();
            if (ClosestOnTriangle(point, a, b, c).DistanceTo(point) > eps) continue;
            float d = n.Dot(dir);
            if (d < bestDot) { bestDot = d; best = n; }
        }
        return best;
    }

    readonly System.Collections.Generic.Dictionary<(Rid, int), Vector3[]> faceCache = new();

    Vector3[] WorldFaces(Rid rid, int shapeIdx)
    {
        if (faceCache.TryGetValue((rid, shapeIdx), out var w)) return w;
        var shape = PhysicsServer3D.BodyGetShape(rid, shapeIdx);
        // GodotConcavePolygonShape3D::get_data: { faces, backface_collision }
        var faces = (Vector3[])((Godot.Collections.Dictionary)PhysicsServer3D.ShapeGetData(shape))["faces"];
        var xf = ((Transform3D)PhysicsServer3D.BodyGetState(rid, PhysicsServer3D.BodyState.Transform)) * PhysicsServer3D.BodyGetShapeTransform(rid, shapeIdx);
        w = new Vector3[faces.Length];
        for (int i = 0; i < faces.Length; i++) w[i] = xf * faces[i];
        faceCache[(rid, shapeIdx)] = w;
        return w;
    }

    static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        // Ericson, Real-Time Collision Detection 5.1.5
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = ab.Dot(ap), d2 = ac.Dot(ap);
        if (d1 <= 0 && d2 <= 0) return a;
        var bp = p - b;
        float d3 = ab.Dot(bp), d4 = ac.Dot(bp);
        if (d3 >= 0 && d4 <= d3) return b;
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + ab * (d1 / (d1 - d3));
        var cp = p - c;
        float d5 = ab.Dot(cp), d6 = ac.Dot(cp);
        if (d6 >= 0 && d5 <= d6) return c;
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + ac * (d2 / (d2 - d6));
        float va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) return b + (c - b) * ((d4 - d3) / (d4 - d3 + (d5 - d6)));
        float denom = 1f / (va + vb + vc);
        return a + ab * (vb * denom) + ac * (vc * denom);
    }
}
