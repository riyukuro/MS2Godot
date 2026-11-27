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
        for (int i = 0; ignoreBody && i < startSkips; i++) excludeList.Add(startSkip[i]);
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

    /// <summary>Groups (collision layer bits) left out of the sweep: GameObjectController m_nIgnoreGroups, applied by the
    /// next <see cref="Move"/>.</summary>
    public uint IgnoreGroups { get; set; }

    /// <summary>The body mask writes so far. The sweeps' motion test reads the body's own mask, so a move's groups go on it;
    /// setting a body's mask moves every shape in the broadphase and wakes the body (GodotCollisionObject3D::_shape_changed),
    /// too slow to do per sweep, so a move writes it only when its groups differ from the last move's.</summary>
    public int MaskWrites { get; private set; }
    uint applied, groups;

    /// <summary>move(disp, m_nGroups & ~nExclude & ~m_nIgnoreGroups) (GameObjectController::Move, physxhelpers.cpp:213-220):
    /// the groups left out of this move on the body's mask, written only when they changed.</summary>
    void ApplyGroups(uint off)
    {
        if (off == applied) return;
        if (applied == 0) groups = body.CollisionMask;
        applied = off;
        body.CollisionMask = groups & ~off;
        MaskWrites++;
    }

    /// <summary>onControllerHit (0x140cfaec0) in the last move: the side or down pass touched another controller (the up
    /// pass runs with the callbacks off, :1036).</summary>
    public bool HitController { get; private set; }

    // SweepTest state
    bool validateCallback, walkExperiment, normalizeResponse, hitNonWalkable, validTri;
    Vector3 touchedNormal;

    /// <param name="faces">the field's mesh face cache (<see cref="FaceGrids"/>), shared by every controller in the field;
    /// null gives this controller a cache of its own (a test's single body)</param>
    public NxController(CharacterBody3D body, CollisionShape3D shape, FaceGrids faces = null)
    {
        this.body = body;
        shapeNode = shape;
        faceGrids = faces ?? new FaceGrids();
    }

    /// <summary>Controller::move (CharacterController.cpp:1205-1336): the body is moved by <paramref name="disp"/>, the
    /// groups in <see cref="IgnoreGroups"/> and <paramref name="exclude"/> (Move's nExcludeGroups, this move only) left out.</summary>
    public Hit Move(Vector3 disp, uint exclude = 0)
    {
        ApplyGroups(IgnoreGroups | exclude);
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

    internal static bool IsController(GodotObject o) =>
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
    // the end test's probe (InsideSolid): up to 8 contacts, all read, so an end inside both a one-sided triangle from
    // behind and a solid shape is not decided by whichever is deepest
    readonly PhysicsTestMotionParameters3D endMotion = new() { MaxCollisions = 8 };
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
    /// overlaps anything, and its contacts what: a shape that is not a mesh, a two-sided mesh, or a one-sided mesh triangle
    /// facing the move counts (the client's triangle sweeps cull back faces). This
    /// reads the probe's own result objects, so it allocates nothing. Where a contact is a one-sided triangle's back face
    /// the contacts cannot be used and the shape query decides (<see cref="EndSolid"/>, all the shapes there): the first
    /// step frees the shape from all it overlaps at once, so a shape in a one-sided wall from behind is pushed out of it,
    /// and down into the floor box it rests on, and the floor's contact is the one at that freed point, not at
    /// <paramref name="at"/>. The same when the test frees the shape but reports no contact (a capsule 0.02 into a mesh
    /// cube's top face, in SpawnCubeStands' bisection), or reports only contacts shallower than <see cref="CastMargin"/>.</summary>
    bool InsideSolid(Vector3 from, Vector3 at, Vector3 dir)
    {
        var probe = dir * ProbeMotion;
        endMotion.From = new Transform3D(Basis.Identity, at);
        endMotion.Motion = probe;
        endMotion.Margin = CastMargin;
        endMotion.RecoveryAsCollision = true;
        if (!StartedInside(PhysicsServer3D.BodyTestMotion(body.GetRid(), endMotion, motionResult), probe)) return false;
        int n = motionResult.GetCollisionCount(), deep = 0;
        for (int i = 0; i < n; i++)
        {
            // a contact shallower than the cast's margin (a facing floor about 1e-6 deep under a shape freed from a wall
            // behind it) is the test's touch, not an overlap: alone it does not make the end solid
            if (motionResult.GetCollisionDepth(i) >= CastMargin) deep++;
            var rid = motionResult.GetColliderRid(i);
            int shapeIdx = motionResult.GetColliderShape(i);
            var shape = PhysicsServer3D.BodyGetShape(rid, shapeIdx);
            if (PhysicsServer3D.ShapeGetType(shape) == PhysicsServer3D.ShapeType.ConcavePolygon && !TwoSided(shape) &&
                !(FaceNormal(rid, shapeIdx, motionResult.GetCollisionPoint(i), dir) is { } f && f.Dot(dir) < 0))
                return EndSolid(from, at, dir, out _, out _);                   // a back face: the contacts are the freed point's
        }
        return deep > 0 || EndSolid(from, at, dir, out _, out _);
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
            endMotion.ExcludeBodies = motionExclude;
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
            if (StartedInside(collided, move))
                return PastStartControllers(from, dir, length, out var past) ? past : SweepFromInside(from, dir, length);
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

    // the other controllers' bodies a sweep started inside, left out of its tests while it runs (PastStartControllers)
    readonly Rid[] startSkip = new Rid[8];
    int startSkips;

    /// <summary>A sweep that starts inside other controllers' bodies only (an npc's capsule moved into the standing player,
    /// a mob pressing it in melee): the same sweep with those bodies left out of the motion tests and the shape query.
    /// SweepCapsuleBox's d == 0 reports no hit for a shape the sweep starts inside (CharacterController.cpp:366-374), and
    /// <see cref="SweepFromInside"/>'s cast leaves out the shapes it starts inside; a body of one shape left out is that
    /// shape left out, so the result is the same, from the motion test's own result objects: it allocates nothing, where
    /// SweepFromInside's cast and rest query allocate their results. The start contacts are the probe's (recovery as
    /// collision, as <see cref="InsideSolid"/>'s); false, with nothing changed, where one at least the cast's margin deep
    /// is not such a body (a mesh, a box, a controller of several shapes). A second run that still starts inside something
    /// takes SweepFromInside with them left out of its query.</summary>
    bool PastStartControllers(Vector3 from, Vector3 dir, float length, out Contact? contact)
    {
        contact = null;
        if (startSkips > 0) return false;                                       // already this sweep's second run
        var probe = dir * ProbeMotion;
        endMotion.From = new Transform3D(Basis.Identity, from);
        endMotion.Motion = probe;
        endMotion.Margin = CastMargin;
        endMotion.RecoveryAsCollision = true;
        PhysicsServer3D.BodyTestMotion(body.GetRid(), endMotion, motionResult);
        int n = motionResult.GetCollisionCount(), k = 0;
        for (int i = 0; i < n; i++)
        {
            if (motionResult.GetCollisionDepth(i) < CastMargin) continue;      // a touch, not an overlap
            var rid = motionResult.GetColliderRid(i);
            if (PhysicsServer3D.BodyGetShapeCount(rid) != 1 || !IsController(GodotObject.InstanceFromId(motionResult.GetColliderId(i))))
                return false;
            bool seen = false;
            for (int j = 0; j < k && !seen; j++) seen = startSkip[j] == rid;
            if (seen) continue;
            if (k == startSkip.Length) return false;
            startSkip[k++] = rid;
        }
        if (k == 0) return false;
        for (int j = 0; j < k; j++) motionExclude.Add(startSkip[j]);
        motion.ExcludeBodies = motionExclude;
        endMotion.ExcludeBodies = motionExclude;
        startSkips = k;
        contact = SweepTests(from, dir, length);                                // inside something else too: SweepFromInside there
        for (int j = 0; j < k; j++) motionExclude.RemoveAt(motionExclude.Count - 1);
        motion.ExcludeBodies = motionExclude;
        endMotion.ExcludeBodies = motionExclude;
        startSkips = 0;
        return true;
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
    /// It allocates its results (a float[] and a Dictionary); a start inside other controllers only does not come here
    /// (<see cref="PastStartControllers"/>).
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
        // a new Array of Dictionaries per call: only on the start-overlap path (EndInside) and in InsideSolid when a probe
        // contact is a one-sided triangle's back face, or the probe frees the shape without a contact. The probe's own
        // contacts (16, recovery as collision) are not this set: they miss shapes the query finds and add shapes only
        // within the margin, and EndSolid decided on them differs from this in a third of its calls inside a wall on a
        // 53,248-triangle mesh and a fifth of movement_sync's
        var found = body.GetWorld3D().DirectSpaceState.IntersectShape(q, OverlapResults);   // perf-ok: see above
        using var tracked = (Godot.Collections.Array)found;                    // the underlying Array is tracked
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
        // the first such triangle in the shape's order: the cells' lists are in that order, a cell stops at the best so far
        var g = Faces(rid, shapeIdx);
        var faces = g.Faces;
        int bestT = int.MaxValue;
        g.Range(lo, hi, out int x0, out int y0, out int z0, out int x1, out int y1, out int z1);
        for (int z = z0; z <= z1; z++)
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int cell = g.CellIndex(x, y, z);
                    for (int k = g.CellStart[cell], end = g.CellStart[cell + 1]; k < end; k++)
                    {
                        int t = g.Tris[k], f = t * 3;
                        if (t >= bestT) break;
                        Vector3 a = faces[f], b = faces[f + 1], c = faces[f + 2];
                        if (Mathf.Max(a.X, Mathf.Max(b.X, c.X)) < lo.X || Mathf.Min(a.X, Mathf.Min(b.X, c.X)) > hi.X ||
                            Mathf.Max(a.Y, Mathf.Max(b.Y, c.Y)) < lo.Y || Mathf.Min(a.Y, Mathf.Min(b.Y, c.Y)) > hi.Y ||
                            Mathf.Max(a.Z, Mathf.Max(b.Z, c.Z)) < lo.Z || Mathf.Min(a.Z, Mathf.Min(b.Z, c.Z)) > hi.Z)
                            continue;
                        var n = g.Normals[t];                                   // the shape holds (v0, v2, v1)
                        if (n == Vector3.Zero) continue;
                        if (n.Dot(dir) >= 0)
                        {
                            if (!twoSided) continue;
                            n = -n;
                        }
                        if (SegmentTriangleDistanceSq(e0, e1, a, b, c, n) >= r * r) continue;
                        if (SegmentTriangleDistanceSq(s0, s1, a, b, c, n) < r * r) continue;
                        bestT = t;
                        face = n;
                        break;
                    }
                }
        return bestT != int.MaxValue;
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
    /// triangles (an edge or a vertex) the one most opposed to the sweep is taken. The candidates are the triangles of the
    /// point's cell in the shape's <see cref="FaceGrid"/>, in the shape's order, so the pick is the full scan's.
    /// </summary>
    Vector3? FaceNormal(Rid rid, int shapeIdx, Vector3 point, Vector3 dir)
    {
        var g = Faces(rid, shapeIdx);
        int cell = g.Cell(point);
        if (cell < 0) return null;
        var faces = g.Faces;
        Vector3? best = null;
        float bestDot = float.MaxValue;
        for (int k = g.CellStart[cell], end = g.CellStart[cell + 1]; k < end; k++)
        {
            int t = g.Tris[k], f = t * 3;
            Vector3 a = faces[f], b = faces[f + 1], c = faces[f + 2];
            if (point.X < Mathf.Min(a.X, Mathf.Min(b.X, c.X)) - FaceEps || point.X > Mathf.Max(a.X, Mathf.Max(b.X, c.X)) + FaceEps ||
                point.Y < Mathf.Min(a.Y, Mathf.Min(b.Y, c.Y)) - FaceEps || point.Y > Mathf.Max(a.Y, Mathf.Max(b.Y, c.Y)) + FaceEps ||
                point.Z < Mathf.Min(a.Z, Mathf.Min(b.Z, c.Z)) - FaceEps || point.Z > Mathf.Max(a.Z, Mathf.Max(b.Z, c.Z)) + FaceEps)
                continue;
            var n = g.Normals[t];
            if (n == Vector3.Zero) continue;
            if (ClosestOnTriangle(point, a, b, c).DistanceTo(point) > FaceEps) continue;
            float d = n.Dot(dir);
            if (d < bestDot) { bestDot = d; best = n; }
        }
        return best;
    }

    const float FaceEps = 0.01f;                // a contact point's distance to its triangle

    /// <summary>
    /// A mesh shape's triangles in world space with a uniform grid over them: each cell lists, in the shape's order, the
    /// triangles whose box grown by <see cref="FaceEps"/> meets it, so a contact point reads one cell and a capsule box the
    /// cells it meets instead of every face.
    /// </summary>
    internal sealed class FaceGrid
    {
        public Vector3[] Faces, Normals;        // three corners per triangle as the shape holds them (v0, v2, v1); its unit normal or zero
        public int[] CellStart, Tris;
        Vector3 min;
        float inv;
        int nx, ny, nz;

        public FaceGrid(Vector3[] faces)
        {
            Faces = faces;
            int tris = faces.Length / 3;
            Normals = new Vector3[tris];
            var lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var hi = -lo;
            float size = 0;
            for (int t = 0; t < tris; t++)
            {
                Vector3 a = faces[t * 3], b = faces[t * 3 + 1], c = faces[t * 3 + 2];
                var n = (c - a).Cross(b - a);
                Normals[t] = n.LengthSquared() < 1e-12f ? Vector3.Zero : n.Normalized();
                var tl = a.Min(b).Min(c); var th = a.Max(b).Max(c);
                lo = lo.Min(tl); hi = hi.Max(th);
                var e = th - tl;
                size += Mathf.Max(e.X, Mathf.Max(e.Y, e.Z));
            }
            if (tris == 0) { lo = hi = Vector3.Zero; }
            min = lo - Vector3.One * FaceEps;
            var ext = hi + Vector3.One * FaceEps - min;
            // cells about twice a mean triangle's size, at most 4 cells per triangle
            float cell = Mathf.Max(2f * size / Mathf.Max(tris, 1), 1f);
            while (true)
            {
                nx = Math.Max(1, (int)Mathf.Ceil(ext.X / cell)); ny = Math.Max(1, (int)Mathf.Ceil(ext.Y / cell)); nz = Math.Max(1, (int)Mathf.Ceil(ext.Z / cell));
                if ((long)nx * ny * nz <= 4L * tris + 64) break;
                cell *= 1.25f;
            }
            inv = 1f / cell;
            CellStart = new int[nx * ny * nz + 1];
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 1)
                {
                    for (int i = 1; i < CellStart.Length; i++) CellStart[i] += CellStart[i - 1];
                    Tris = new int[CellStart[^1]];
                    for (int i = CellStart.Length - 1; i > 0; i--) CellStart[i] = CellStart[i - 1];
                    CellStart[0] = 0;
                }
                for (int t = 0; t < tris; t++)
                {
                    Vector3 a = faces[t * 3], b = faces[t * 3 + 1], c = faces[t * 3 + 2];
                    Index(a.Min(b).Min(c) - Vector3.One * FaceEps, out int x0, out int y0, out int z0);
                    Index(a.Max(b).Max(c) + Vector3.One * FaceEps, out int x1, out int y1, out int z1);
                    for (int z = z0; z <= z1; z++)
                        for (int y = y0; y <= y1; y++)
                            for (int x = x0; x <= x1; x++)
                            {
                                int i = (z * ny + y) * nx + x;
                                if (pass == 0) CellStart[i + 1]++;
                                else Tris[CellStart[i + 1]++] = t;
                            }
                }
            }
        }

        /// <summary>The cells a box meets, clamped to the grid (a triangle box meeting it is listed in one of them).</summary>
        public void Range(Vector3 lo, Vector3 hi, out int x0, out int y0, out int z0, out int x1, out int y1, out int z1)
        {
            Index(lo, out x0, out y0, out z0);
            Index(hi, out x1, out y1, out z1);
        }

        public int CellIndex(int x, int y, int z) => (z * ny + y) * nx + x;

        void Index(Vector3 p, out int x, out int y, out int z)
        {
            var q = (p - min) * inv;
            x = Math.Clamp((int)q.X, 0, nx - 1); y = Math.Clamp((int)q.Y, 0, ny - 1); z = Math.Clamp((int)q.Z, 0, nz - 1);
        }

        /// <summary>The point's cell, -1 outside the grid (no triangle within <see cref="FaceEps"/>).</summary>
        public int Cell(Vector3 p)
        {
            var q = (p - min) * inv;
            if (q.X < 0 || q.Y < 0 || q.Z < 0) return -1;
            int x = (int)q.X, y = (int)q.Y, z = (int)q.Z;
            if (x > nx || y > ny || z > nz) return -1;
            return (Math.Min(z, nz - 1) * ny + Math.Min(y, ny - 1)) * nx + Math.Min(x, nx - 1);
        }
    }

    readonly FaceGrids faceGrids;
    FaceGrid Faces(Rid rid, int shapeIdx) => faceGrids.Get(rid, shapeIdx);

    /// <summary>
    /// The world-space faces of the field's mesh shapes and their grids (<see cref="FaceGrid"/>), built at a shape's first
    /// touch by any controller and kept by (body, shape): one per field (Field.FaceGrids), passed to the player's and every
    /// remote PC's controller, so N bodies on one large mesh build its grid once. The field clears it when it leaves the
    /// tree (a freed body's RID may be reused).
    /// STILL-OPEN: the faces and their grid are cached in world space at the first touch, so they would go stale on a mesh
    /// body that moves; no field mesh body moves (FieldBuilder's are static, the ride box is a box).
    /// </summary>
    public sealed class FaceGrids
    {
        readonly System.Collections.Generic.Dictionary<(Rid, int), FaceGrid> grids = new();

        public void Clear() => grids.Clear();

        internal FaceGrid Get(Rid rid, int shapeIdx)
        {
            if (grids.TryGetValue((rid, shapeIdx), out var g)) return g;
            var shape = PhysicsServer3D.BodyGetShape(rid, shapeIdx);
            // GodotConcavePolygonShape3D::get_data: { faces, backface_collision }
            var faces = (Vector3[])((Godot.Collections.Dictionary)PhysicsServer3D.ShapeGetData(shape))["faces"];   // perf-ok: once per shape, cached here
            var xf = ((Transform3D)PhysicsServer3D.BodyGetState(rid, PhysicsServer3D.BodyState.Transform)) * PhysicsServer3D.BodyGetShapeTransform(rid, shapeIdx);
            var w = new Vector3[faces.Length];
            for (int i = 0; i < faces.Length; i++) w[i] = xf * faces[i];
            g = new FaceGrid(w);
            grids[(rid, shapeIdx)] = g;
            return g;
        }
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
