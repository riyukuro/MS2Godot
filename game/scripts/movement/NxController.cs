using Godot;

namespace Ms2Godot.Movement;

/// <summary>
/// The client's character controller move: NxCharacter 2.8.4 (NxCharacter64.dll 2.8.4.6; CharacterController.cpp is the
/// same in the 2.8.3 and 2.8.4 SDKs) as GameObjectController::CreateController sets it up (@0x14123f5b0: a capsule, skin
/// 0.01 m, slopeLimit cos 50 deg, stepOffset 0.3 m, CLIMB_EASY) and GameObjectController::Move calls it (@0x14123ff80:
/// minDist 1e-6 m, sharpness 1). Controller::move runs SweepTest::MoveCharacter, an up / side / down sweep of the body's
/// own shape, and when the down pass lands on a triangle steeper than the slope limit it runs the move again from the start
/// as the "walk experiment". Godot's motion test brackets the first contact by bisection (GodotPhysics3D: 8 steps), and the
/// capsule's gap to the touched point is then closed along the move to land on NxSweepCapsuleTriangles' analytic stop.
/// </summary>
public sealed class NxController
{
    /// <summary>NxControllerFlag: the passes that touched something in the last move.</summary>
    [System.Flags]
    public enum Hit { None = 0, Sides = 1, Up = 2, Down = 4 }

    const int MaxIter = 10;                     // MAX_ITER, mMaxIter (CharacterController.cpp:24, :645)
    const float MinDist = 1e-6f * 100f;         // GameObjectController::Move passes 1e-6 m
    const int RefineStages = 3;                 // the start-overlap path's casts: each bracket cast again

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

    /// <summary>Groups (collision layer bits) left out of the sweep: GameObjectController m_nIgnoreGroups. The sweeps'
    /// motion test reads the body's own mask, so a change is applied to it here, once: setting a body's mask moves every
    /// shape in the broadphase and wakes the body (GodotCollisionObject3D::_shape_changed), too slow to do per sweep.</summary>
    public uint IgnoreGroups
    {
        get => ignoreGroups;
        set
        {
            if (value == ignoreGroups) return;
            if (ignoreGroups == 0) groups = body.CollisionMask;
            ignoreGroups = value;
            body.CollisionMask = groups & ~value;                               // move(disp, m_nGroups & ~m_nIgnoreGroups)
        }
    }
    uint ignoreGroups, groups;

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

    // The sweeps' queries: PhysicsServer3D.BodyTestMotion on the body itself, with one parameter and one result object
    // reused. It is the one physics query whose result is not a new managed object (CastMotion returns a float[],
    // GetRestInfo a Dictionary), and it sweeps the body's own shapes against its own mask. Its first step frees a body that
    // starts inside something; the sweep reads that as a start overlap (see Sweep).
    readonly PhysicsTestMotionParameters3D motion = new() { MaxCollisions = 1 };
    // the end test's probe: all its contacts are read (an end inside a one-sided triangle from behind and a solid shape
    // reports either one deepest)
    readonly PhysicsTestMotionResult3D motionResult = new();
    readonly Godot.Collections.Array<Rid> motionExclude = new();
    Rid motionExcluded;

    const float CastMargin = 1e-4f;             // test_body_motion's least margin (TEST_MOTION_MARGIN_MIN_VALUE)
    const float ProbeMotion = 1e-4f;            // a probe's own motion: test_body_motion divides by the motion's length
    const float OverlapTolerance = 1e-3f;       // a start or probe the test frees by more than this is inside a shape

    bool TestMotion(Vector3 at, Vector3 move, float margin, bool restAtStart)
    {
        motion.From = new Transform3D(Basis.Identity, at);
        motion.Motion = move;
        motion.Margin = margin;
        motion.RecoveryAsCollision = restAtStart;
        return PhysicsServer3D.BodyTestMotion(body.GetRid(), motion, motionResult);
    }

    /// <summary>The last test started inside a shape: its first step moved the body out, or it could not and the cast is
    /// stuck (safe and unsafe 0).</summary>
    bool StartedInside(bool collided, Vector3 move)
    {
        float safeF = motionResult.GetCollisionSafeFraction();
        var freed = motionResult.GetTravel() - move * safeF;
        return freed.LengthSquared() > OverlapTolerance * OverlapTolerance ||
               collided && safeF <= 0 && motionResult.GetCollisionUnsafeFraction() <= 0;
    }

    /// <summary>The body's shape at <paramref name="at"/> is inside a shape the move from <paramref name="from"/> along
    /// <paramref name="dir"/> could not pass. The SAT test (the motion test's first step, which does not miss) tells whether it
    /// overlaps anything; what it overlaps is then read from the shape query (<see cref="EndSolid"/>, all the shapes there),
    /// not from the test's contacts: the first step frees the shape from all it overlaps at once, so a shape deep in a
    /// one-sided wall is pushed out of it and down out of the floor box under it, and the contacts are the ones at the freed
    /// point. The SAT test counts a one-sided mesh triangle from behind too; the client's triangle sweeps cull back faces, so
    /// a mesh counts only by a triangle facing the move.</summary>
    bool InsideSolid(Vector3 from, Vector3 at, Vector3 dir)
    {
        var probe = dir * ProbeMotion;
        return StartedInside(TestMotion(at, probe, CastMargin, true), probe) && EndSolid(from, at, dir, out _, out _);
    }

    readonly System.Collections.Generic.Dictionary<Rid, bool> twoSided = new();

    bool TwoSided(Rid shape)
    {
        if (twoSided.TryGetValue(shape, out bool two)) return two;
        // GodotConcavePolygonShape3D::get_data: { faces, backface_collision }
        using var data = (Godot.Collections.Dictionary)PhysicsServer3D.ShapeGetData(shape);   // perf-ok: once per shape, cached in twoSided
        two = (bool)data["backface_collision"];
        twoSided[shape] = two;
        return two;
    }

    /// <summary>
    /// NxSweepCapsuleTriangles and the other shape sweeps: the distance along <paramref name="dir"/> to the first contact of
    /// the body's shape, within <paramref name="length"/>: one motion test (PhysicsServer3D.BodyTestMotion, with its
    /// parameter and result objects kept, so a sweep allocates nothing), then the gap to the touched point is closed.
    /// A sweep that starts inside a shape passes through it, as SweepCapsuleBox's d == 0 does (CharacterController.cpp:366-374):
    /// <see cref="SweepFromInside"/>.
    /// <para>The test's cast is GodotPhysics3D's: a GJK distance test on the shape plus its motion, then a bisection. Against
    /// a large box (thousands of cm) the GJK test reports separation for a motion that passes through the box in 1-3% of
    /// casts, and the cast reports no hit: a body landing on such a floor fell through it. Every such miss found ends with
    /// the shape overlapping the box (up to 14% of casts that end near rest on a 20000 cm box), so a cast with no hit has
    /// its end tested with the SAT overlap test (the test's first step, which does not miss) and, where it overlaps
    /// something, the shape query's overlap (<see cref="InsideSolid"/>); an end inside a shape the move could not pass is
    /// bisected with the same test. A cast hit with no contact within its bracket (the GJK cast also reports a hit on a large
    /// box that is not there, and on a one-sided triangle's back face) is taken as a miss and has the same end test.
    /// STILL-OPEN: a missed cast that ends past the shape (a thin shape crossed whole) is still passed through.</para>
    /// </summary>
    Contact? Sweep(Vector3 from, Vector3 dir, float length)
    {
        if (motionExcluded != IgnoreBody)
        {
            motionExclude.Clear();
            if (IgnoreBody.IsValid) motionExclude.Add(IgnoreBody);
            motion.ExcludeBodies = motionExclude;
            motionExcluded = IgnoreBody;
        }
        return SweepTests(from, dir, length);
    }

    Contact? SweepTests(Vector3 from, Vector3 dir, float length)
    {
        float safe = 0, unsafeD = length;
        bool hit = false, have = false;
        Vector3 normal = default, point = default;
        Rid rid = default;
        int shapeIdx = -1;
        ulong colliderId = 0;
        if (length > 1e-7f)
        {
            var move = dir * length;
            bool collided = TestMotion(from, move, CastMargin, false);
            if (StartedInside(collided, move)) return SweepFromInside(from, dir, length);
            float safeF = motionResult.GetCollisionSafeFraction();
            if (collided && safeF < 1f)
            {
                hit = true;
                unsafeD = motionResult.GetCollisionUnsafeFraction() * length;
                safe = safeF * length;
                have = motionResult.GetCollisionCount() > 0;                    // the test's rest contact at the unsafe point
                if (have) ReadContact(0, out normal, out point, out rid, out shapeIdx, out colliderId);
            }
        }

        // the cast's miss: a move it lets through but that ends inside a solid shape (the SAT test, which does not miss),
        // bisected with the same test
        bool castHit = hit;
    endTest:
        if (!hit)
        {
            startCount = 0;                                                     // the move did not start inside a shape
            if (length <= 1e-7f || !InsideSolid(from, from + dir * length, dir)) return null;
            float lo = 0, hi = length;
            for (int i = 0; i < 16 && hi - lo > 1e-3f; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (InsideSolid(from, from + dir * mid, dir)) hi = mid; else lo = mid;
            }
            safe = lo;
            unsafeD = hi;
            hit = true;
        }

        // what is touched there, and the exact stop: the casts' collision test leaves up to ~1e-3 of the bracket, so the
        // capsule's own gap to the touched point, (segment end lowest along the contact normal) . n - radius, is closed
        // along the move (a plane closes at -dir . n per unit; an edge or vertex converges in a few steps). The rest test is
        // the test's own: its last step, the deepest contact within the margin (GetRestInfo's callback).
        var local = shapeNode.Transform;
        float t1 = safe;
        bool found = false;
        for (int it = 0; it < 4; it++)
        {
            if (!have)
            {
                float margin = Mathf.Max(unsafeD - t1, 0) + 0.01f;
                if (!TestMotion(from + dir * t1, dir * ProbeMotion, margin, true) || motionResult.GetCollisionCount() == 0) break;
                ReadContact(0, out normal, out point, out rid, out shapeIdx, out colliderId);
            }
            have = false;
            found = true;
            if (shapeNode.Shape is not CapsuleShape3D cap) break;
            var centre = from + dir * t1 + local.Origin;
            var half = local.Basis.Y.Normalized() * (cap.Height / 2 - cap.Radius);
            float gap = Mathf.Min((centre + half - point).Dot(normal), (centre - half - point).Dot(normal)) - cap.Radius;
            float rate = -dir.Dot(normal);
            if (rate < 0.05f) break;
            float tn = Mathf.Clamp(t1 + gap / rate, 0, length);
            bool done = Mathf.Abs(tn - t1) < 1e-6f;
            t1 = tn;
            if (done) break;
        }
        if (!found)
        {
            if (castHit)
            {
                // a cast hit with nothing within its bracket (the GJK cast's false hit, as SweepFromInside's): its end test
                castHit = hit = have = false;
                goto endTest;
            }
            return new Contact(safe, -dir, null, false, Vector3.Zero);
        }
        return Touched(t1, normal, point, rid, shapeIdx, colliderId, dir);
    }

    void ReadContact(int i, out Vector3 normal, out Vector3 point, out Rid rid, out int shapeIdx, out ulong colliderId)
    {
        normal = motionResult.GetCollisionNormal(i);
        point = motionResult.GetCollisionPoint(i);
        rid = motionResult.GetColliderRid(i);
        shapeIdx = motionResult.GetColliderShape(i);
        colliderId = motionResult.GetColliderId(i);
    }

    Contact Touched(float distance, Vector3 normal, Vector3 point, Rid rid, int shapeIdx, ulong colliderId, Vector3 dir)
    {
        var collider = GodotObject.InstanceFromId(colliderId);
        bool mesh = PhysicsServer3D.ShapeGetType(PhysicsServer3D.BodyGetShape(rid, shapeIdx)) == PhysicsServer3D.ShapeType.ConcavePolygon;
        var face = mesh ? FaceNormal(rid, shapeIdx, point, dir) ?? normal : normal;
        return new Contact(distance, normal, collider, mesh, face, shapeIdx);
    }

    /// <summary>
    /// A sweep that starts inside a shape: the shape query's cast, which leaves out the shapes it starts inside (GodotPhysics3D
    /// cast_motion skips a shape it overlaps at the start), as SweepCapsuleBox's d == 0 reports no hit. BodyTestMotion cannot
    /// leave out one shape of a body, only the whole body.
    /// A cast with no hit has its end tested as the normal path's is (<see cref="EndInside"/>), so the GJK cast's miss on a
    /// large box does not let the body through it.
    /// The controller keeps one skin off what it touches, so a move starts inside a shape after something moved into it,
    /// or after the body walked through a one-sided triangle's back face (the client's triangle sweeps cull back faces,
    /// so it is inside the triangle for a few moves).
    /// STILL-OPEN: this path allocates its results (a float[] and a Dictionary).
    /// STILL-OPEN, where the motion test differs from the shape query this controller used before: (1) on a one-sided mesh
    /// the motion test's first step pushes the shape out of a triangle it is behind, so a move that starts behind one is
    /// taken here, and the cast passes back faces as the client's triangle sweeps do, where the shape query's cast stopped
    /// on them; (2) on a box sloped 30 degrees a walking body crossing the crest stood up to 9 cm higher than with the
    /// shape query for 20 frames and lost its ground flag for one; which of the two the client matches is not known.
    /// </summary>
    Contact? SweepFromInside(Vector3 from, Vector3 dir, float length)
    {
        var space = body.GetWorld3D().DirectSpaceState;
        var q = Query(body.CollisionMask, true);                                // already m_nGroups & ~m_nIgnoreGroups
        var local = shapeNode.Transform;
        float safe = 0, unsafeD = length;
        bool hit = false;
        for (int stage = 0; stage < RefineStages && unsafeD - safe > 1e-7f; stage++)
        {
            float span = unsafeD - safe;
            q.Transform = new Transform3D(Basis.Identity, from + dir * safe) * local;
            q.Motion = dir * span;
            var r = space.CastMotion(q);                                        // perf-ok: start-overlap path only
            if (r.Length < 2 || r[0] >= 1f) break;
            hit = true;
            unsafeD = safe + r[1] * span;
            safe += r[0] * span;
        }
        if (!hit) return EndInside(from, dir, length);
        float t = safe;
        Godot.Collections.Dictionary info = null;
        for (int it = 0; it < 4; it++)
        {
            q.Transform = new Transform3D(Basis.Identity, from + dir * t) * local;
            q.Motion = Vector3.Zero;
            q.Margin = Mathf.Max(unsafeD - t, 0) + 0.01f;
            var ri = space.GetRestInfo(q);                                      // perf-ok: start-overlap path only
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
        if (info == null) return EndInside(from, dir, length);                 // a cast hit with nothing there: the GJK cast's
                                                                                // false hit on a large box
        if (EndInside(from, dir, t) is { } entered)
        {
            info.Dispose();
            return entered;
        }
        var normal = (Vector3)info[PhysicsKeys.Normal];
        var point = (Vector3)info[PhysicsKeys.Point];
        var rid = (Rid)info[PhysicsKeys.Rid];
        int shapeIdx = (int)info[PhysicsKeys.Shape];
        ulong id = (ulong)info[PhysicsKeys.ColliderId];
        info.Dispose();
        return Touched(t, normal, point, rid, shapeIdx, id, dir);
    }

    /// <summary>
    /// <see cref="SweepFromInside"/>'s cast reported no hit: its end tested as the normal path's is (<see cref="Sweep"/>),
    /// bisected with the same test. The motion test's probe cannot be used here: its first step frees the shape from the
    /// shape it starts inside as well, and a body deep in a one-sided wall is pushed out of a floor box it ends in too. So the
    /// shape query's overlap is taken (the shapes the body's shape overlaps there), as the cast sees them: a shape that is
    /// not a mesh counts unless the sweep started inside it (SweepCapsuleBox d == 0, CharacterController.cpp:366-374); a mesh
    /// counts by its triangles, one the capsule overlaps there that faces the move (either side on a two-sided mesh) and
    /// that it did not overlap at the start. The stop is the last free point of the bisection (within 1e-3).
    /// STILL-OPEN: a triangle facing the move that the sweep starts inside passes, as the cast's skip of the whole shape
    /// does; what NxSweepCapsuleTriangles reports there is not ported. A shape that is neither a mesh nor a box stops the
    /// move with the reversed move as its normal.
    /// </summary>
    Contact? EndInside(Vector3 from, Vector3 dir, float length)
    {
        if (length <= 1e-7f) return null;
        startCount = Overlapping(from, startRid, startShape, null);
        if (!EndSolid(from, from + dir * length, dir, out _, out _)) return null;
        float lo = 0, hi = length;
        for (int i = 0; i < 16 && hi - lo > 1e-3f; i++)
        {
            float mid = (lo + hi) * 0.5f;
            if (EndSolid(from, from + dir * mid, dir, out _, out _)) hi = mid; else lo = mid;
        }
        if (!EndSolid(from, from + dir * hi, dir, out int w, out var face)) return new Contact(lo, -dir, null, false, Vector3.Zero);
        var rid = endRid[w];
        int shapeIdx = endShape[w];
        var collider = GodotObject.InstanceFromId(endId[w]);
        bool mesh = PhysicsServer3D.ShapeGetType(PhysicsServer3D.BodyGetShape(rid, shapeIdx)) == PhysicsServer3D.ShapeType.ConcavePolygon;
        var normal = mesh ? face : BoxNormal(rid, shapeIdx, from + dir * hi + shapeNode.Transform.Origin, dir);
        return new Contact(lo, normal, collider, mesh, normal, shapeIdx);
    }

    const int OverlapResults = 16;
    readonly Rid[] startRid = new Rid[OverlapResults], endRid = new Rid[OverlapResults];
    readonly int[] startShape = new int[OverlapResults], endShape = new int[OverlapResults];
    readonly ulong[] endId = new ulong[OverlapResults];
    int startCount;

    /// <summary>The shapes the body's shape overlaps with the body at <paramref name="at"/> (the shape query, the sweep's
    /// groups), into the given arrays; the count.</summary>
    int Overlapping(Vector3 at, Rid[] rids, int[] shapes, ulong[] ids)
    {
        var q = Query(body.CollisionMask, true);
        q.Transform = new Transform3D(Basis.Identity, at) * shapeNode.Transform;
        var found = body.GetWorld3D().DirectSpaceState.IntersectShape(q, OverlapResults);   // perf-ok: start-overlap path only
        int n = System.Math.Min(found.Count, OverlapResults);
        for (int i = 0; i < n; i++)
        {
            using var d = found[i];
            rids[i] = (Rid)d[PhysicsKeys.Rid];
            shapes[i] = (int)d[PhysicsKeys.Shape];
            if (ids != null) ids[i] = (ulong)d[PhysicsKeys.ColliderId];
        }
        return n;
    }

    /// <summary>See <see cref="EndInside"/>: the body at <paramref name="at"/> is in a shape the sweep from
    /// <paramref name="from"/> along <paramref name="dir"/> could not pass; <paramref name="which"/> is its overlap index,
    /// <paramref name="face"/> the counted triangle's normal facing the move.</summary>
    bool EndSolid(Vector3 from, Vector3 at, Vector3 dir, out int which, out Vector3 face)
    {
        which = -1;
        face = default;
        int n = Overlapping(at, endRid, endShape, endId);
        for (int i = 0; i < n; i++)
        {
            var rid = endRid[i];
            int shapeIdx = endShape[i];
            var shape = PhysicsServer3D.BodyGetShape(rid, shapeIdx);
            if (PhysicsServer3D.ShapeGetType(shape) != PhysicsServer3D.ShapeType.ConcavePolygon)
            {
                bool started = false;
                for (int k = 0; k < startCount && !started; k++) started = startRid[k] == rid && startShape[k] == shapeIdx;
                if (started) continue;
            }
            else if (!TriangleEntered(rid, shapeIdx, TwoSided(shape), from, at, dir, out face)) continue;
            which = i;
            return true;
        }
        return false;
    }

    /// <summary>A triangle of the mesh shape that the body's capsule overlaps at <paramref name="at"/> and not at
    /// <paramref name="from"/>, facing the move (or either side when <paramref name="twoSided"/>); its normal facing the
    /// move.</summary>
    bool TriangleEntered(Rid rid, int shapeIdx, bool twoSided, Vector3 from, Vector3 at, Vector3 dir, out Vector3 face)
    {
        face = default;
        var local = shapeNode.Transform;
        float r = 0, halfLen = 0;
        if (shapeNode.Shape is CapsuleShape3D cap) { r = cap.Radius; halfLen = cap.Height / 2 - cap.Radius; }
        var axis = local.Basis.Y.Normalized() * halfLen;
        Vector3 e0 = at + local.Origin - axis, e1 = at + local.Origin + axis;
        Vector3 s0 = from + local.Origin - axis, s1 = from + local.Origin + axis;
        var lo = e0.Min(e1) - Vector3.One * r;
        var hi = e0.Max(e1) + Vector3.One * r;
        var faces = WorldFaces(rid, shapeIdx);
        for (int f = 0; f + 2 < faces.Length; f += 3)
        {
            Vector3 a = faces[f], b = faces[f + 1], c = faces[f + 2];
            if (Mathf.Max(a.X, Mathf.Max(b.X, c.X)) < lo.X || Mathf.Min(a.X, Mathf.Min(b.X, c.X)) > hi.X ||
                Mathf.Max(a.Y, Mathf.Max(b.Y, c.Y)) < lo.Y || Mathf.Min(a.Y, Mathf.Min(b.Y, c.Y)) > hi.Y ||
                Mathf.Max(a.Z, Mathf.Max(b.Z, c.Z)) < lo.Z || Mathf.Min(a.Z, Mathf.Min(b.Z, c.Z)) > hi.Z)
                continue;
            var n = (c - a).Cross(b - a);                                       // the shape holds (v0, v2, v1)
            if (n.LengthSquared() < 1e-12f) continue;
            n = n.Normalized();
            if (n.Dot(dir) >= 0)
            {
                if (!twoSided) continue;
                n = -n;
            }
            if (SegmentTriangleDistanceSq(e0, e1, a, b, c, n) >= r * r) continue;
            if (SegmentTriangleDistanceSq(s0, s1, a, b, c, n) < r * r) continue;
            face = n;
            return true;
        }
        return false;
    }

    /// <summary>The face normal of a box shape on the side of <paramref name="p"/>; the reversed move for another shape.</summary>
    static Vector3 BoxNormal(Rid rid, int shapeIdx, Vector3 p, Vector3 dir)
    {
        var shape = PhysicsServer3D.BodyGetShape(rid, shapeIdx);
        if (PhysicsServer3D.ShapeGetType(shape) != PhysicsServer3D.ShapeType.Box) return -dir;
        var he = (Vector3)PhysicsServer3D.ShapeGetData(shape);                 // GodotBoxShape3D::get_data: half extents
        var xf = (Transform3D)PhysicsServer3D.BodyGetState(rid, PhysicsServer3D.BodyState.Transform) * PhysicsServer3D.BodyGetShapeTransform(rid, shapeIdx);
        var l = xf.AffineInverse() * p;
        int k = 0;
        float best = -1;
        for (int i = 0; i < 3; i++)
        {
            float v = he[i] > 0 ? Mathf.Abs(l[i]) / he[i] : 0;
            if (v > best) { best = v; k = i; }
        }
        var axis = Vector3.Zero;
        axis[k] = l[k] < 0 ? -1 : 1;
        return (xf.Basis * axis).Normalized();
    }

    static float SegmentTriangleDistanceSq(Vector3 p, Vector3 q, Vector3 a, Vector3 b, Vector3 c, Vector3 n)
    {
        float dp = (p - a).Dot(n), dq = (q - a).Dot(n);
        if (dp * dq <= 0 && dp != dq)
        {
            var x = p + (q - p) * (dp / (dp - dq));
            if (ClosestOnTriangle(x, a, b, c).DistanceSquaredTo(x) < 1e-8f) return 0;
        }
        float best = Mathf.Min(ClosestOnTriangle(p, a, b, c).DistanceSquaredTo(p), ClosestOnTriangle(q, a, b, c).DistanceSquaredTo(q));
        best = Mathf.Min(best, SegmentSegmentDistanceSq(p, q, a, b));
        best = Mathf.Min(best, SegmentSegmentDistanceSq(p, q, b, c));
        return Mathf.Min(best, SegmentSegmentDistanceSq(p, q, c, a));
    }

    static float SegmentSegmentDistanceSq(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
    {
        // Ericson, Real-Time Collision Detection 5.1.9
        Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
        float a = d1.Dot(d1), e = d2.Dot(d2), f = d2.Dot(r), s, t;
        if (a <= 1e-12f && e <= 1e-12f) return r.Dot(r);
        if (a <= 1e-12f) { s = 0; t = Mathf.Clamp(f / e, 0, 1); }
        else
        {
            float c = d1.Dot(r);
            if (e <= 1e-12f) { t = 0; s = Mathf.Clamp(-c / a, 0, 1); }
            else
            {
                float b = d1.Dot(d2), denom = a * e - b * b;
                s = denom != 0 ? Mathf.Clamp((b * f - c * e) / denom, 0, 1) : 0;
                t = (b * s + f) / e;
                if (t < 0) { t = 0; s = Mathf.Clamp(-c / a, 0, 1); }
                else if (t > 1) { t = 1; s = Mathf.Clamp((b - c) / a, 0, 1); }
            }
        }
        return (p1 + d1 * s).DistanceSquaredTo(p2 + d2 * t);
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

    // Per controller, and a controller is per field (Player.Create in Field.SpawnPlayer, a new scene per field).
    // STILL-OPEN: the faces are cached in world space at the first touch, so they would go stale on a mesh body that
    // moves; no field mesh body moves (FieldBuilder's are static, the ride box is a box).
    readonly System.Collections.Generic.Dictionary<(Rid, int), Vector3[]> faceCache = new();

    Vector3[] WorldFaces(Rid rid, int shapeIdx)
    {
        if (faceCache.TryGetValue((rid, shapeIdx), out var w)) return w;
        var shape = PhysicsServer3D.BodyGetShape(rid, shapeIdx);
        // GodotConcavePolygonShape3D::get_data: { faces, backface_collision }
        var faces = (Vector3[])((Godot.Collections.Dictionary)PhysicsServer3D.ShapeGetData(shape))["faces"];   // perf-ok: once per shape, cached in faceCache
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
