using Godot;

namespace Ms2Godot;

/// <summary>
/// Player body (CharacterBody3D, Y-up, file units). The client uses a PhysX capsule controller (a cylinder here) with a 50 degree
/// slope limit and a 0.3 m step (30 units); Godot has no step offset, so a step-up test runs after a blocked move.
/// The idle / walk / run animation follows the horizontal speed.
/// </summary>
public partial class Player : CharacterBody3D
{
    public const float Radius = 25f, Height = 110f;
    public const float StepHeight = 30f;
    public float WalkSpeed = 150f, RunSpeed = 350f, Gravity = 1500f;

    public Node3D Visual;                 // the character root (faces +Z)
    public AnimationPlayer Anim;
    public Vector3 SpawnPoint;
    public Vector2 Input2;                // x = right, y = forward, camera relative (set by Field)
    public bool WalkMode;
    public float CameraYaw;               // radians about +Y; camera forward = (-sin, 0, -cos)
    public string CurrentAnim = "";

    public static Player Create(Node3D visual, AnimationPlayer anim)
    {
        var p = new Player { Name = "Player", Visual = visual, Anim = anim };
        p.FloorMaxAngle = Mathf.DegToRad(50f);
        p.FloorSnapLength = StepHeight;
        p.SafeMargin = 1f;
        var shape = new CollisionShape3D
        {
            // a flat-bottomed cylinder stands on step tops; a capsule's round bottom slides back off a 30 riser edge
            Shape = new CylinderShape3D { Radius = Radius, Height = Height },
            Position = new Vector3(0, Height / 2, 0),
        };
        p.AddChild(shape);
        p.AddChild(visual);
        return p;
    }

    public void Face(Vector3 dir)
    {
        if (dir.LengthSquared() > 1e-6f) Visual.Rotation = new Vector3(0, Mathf.Atan2(dir.X, dir.Z), 0);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        var fwd = new Vector3(-Mathf.Sin(CameraYaw), 0, -Mathf.Cos(CameraYaw));
        var right = new Vector3(-fwd.Z, 0, fwd.X);
        var dir = right * Input2.X + fwd * Input2.Y;
        if (dir.LengthSquared() > 1) dir = dir.Normalized();
        float speed = WalkMode ? WalkSpeed : RunSpeed;
        var v = Velocity;
        v.X = dir.X * speed;
        v.Z = dir.Z * speed;
        v.Y = IsOnFloor() ? Mathf.Min(v.Y, 0) : v.Y - Gravity * dt;
        Velocity = v;

        var before = GlobalPosition;
        var wantedHoriz = new Vector3(v.X, 0, v.Z) * dt;
        MoveAndSlide();
        var moved = GlobalPosition - before;
        moved.Y = 0;
        if (IsOnFloor() && IsOnWall() && wantedHoriz.Length() > 0.01f && moved.Length() < wantedHoriz.Length() * 0.5f)
        {
            // slide the step motion along walls taller than a step, so a riser in a corner can still be climbed
            var stepMotion = wantedHoriz;
            for (int i = 0; i < GetSlideCollisionCount(); i++)
            {
                var c = GetSlideCollision(i);
                var n = c.GetNormal();
                if (Mathf.Abs(n.Y) > 0.3f || c.GetPosition().Y < GlobalPosition.Y + StepHeight + 2 * SafeMargin) continue;
                n.Y = 0;
                n = n.Normalized();
                if (stepMotion.Dot(n) < 0) stepMotion -= n * stepMotion.Dot(n);
            }
            if (stepMotion.Length() > 0.01f) TryStepUp(stepMotion);
        }

        Face(dir);
        if (GlobalPosition.Y < SpawnPoint.Y - 3000)      // IsFallReturn: back to the spawn point
        {
            GlobalPosition = SpawnPoint;
            Velocity = Vector3.Zero;
        }

        var hs = new Vector2(Velocity.X, Velocity.Z).Length();
        string want = hs < 10 ? "idle" : (WalkMode ? "walk" : "run");
        if (want != CurrentAnim && Anim.HasAnimation(want))
        {
            Anim.Play(want, 0.15);
            CurrentAnim = want;
        }
    }

    void TryStepUp(Vector3 horiz)
    {
        var t = GlobalTransform;
        var up = Vector3.Up * (StepHeight + 2 * SafeMargin);   // a 30 riser must pass with the margin
        var col = new KinematicCollision3D();
        if (TestMove(t, up, col)) return;
        var raised = t.Translated(up);
        if (TestMove(raised, horiz, col)) return;
        var ahead = raised.Translated(horiz);
        // land on the step top (a walkable normal, or a contact above the start height on the riser edge)
        if (TestMove(ahead, -up * 1.5f, col) && (col.GetNormal().Y > Mathf.Cos(FloorMaxAngle) || col.GetPosition().Y > t.Origin.Y + 1))
            GlobalTransform = ahead.Translated(col.GetTravel());
    }
}
