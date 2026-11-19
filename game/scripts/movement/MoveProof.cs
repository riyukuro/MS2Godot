using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;

namespace Ms2Godot.Movement;

/// <summary>
/// Scripted movement run for screenshots: `-- --move-shots DIR [--move-script FILE]`. The script is one step per line:
///   tp X Y Z YAW              teleport (MS2 coordinates, yaw degrees as the spawn point's Rotation.z)
///   hold SECS [DX DY] [jump] [walk] [up|down|left|right]   hold a direction (MS2 world axes) or a key
///   until STATE SECS [DX DY] [jump] [up|down]   hold until the player is in STATE (Idle, Walk, Jump, Fall, Land, Swim, Ladder)
///   shot NAME                 save DIR/NAME.png
///   dash                      start StateDash (the server / a skill does in the client)
///   wheel N                   one mouse wheel notch (1 in, -1 out), as the wheel handler sets it
///   cam                       print the camera zoom distance
///   camtrace S                print the zoom distance and the time since the command every frame for S seconds
///   click X Y Z               click to move to a point (needs --mouse-move)
///   ropes N                   print the N rope segments nearest the player
///   walls R                   print cube walls within R whose face the snap ray (+X / +Y) reaches from the floor in front
///   fx NIF DX DY DZ [YAW]     play an effect nif from Resource/Model/Effect (e.g. ch/common/eff_levelup.nif) at the player + (DX, DY, DZ) MS2 units,
///                             yaw degrees, seed 1, for its own duration (regression shots of the effect runtime)
///   fxclear                   remove the effects the fx steps started
/// Each state change is printed with the position. Lines starting with # are comments.
/// </summary>
public partial class MoveProof : Node
{
    readonly Player player;
    readonly string dir;
    readonly Queue<string[]> steps = new();
    float t;
    int settle = -1;
    Player.MoveState lastState;
    int frame;
    readonly List<Node3D> effects = new();

    /// <summary>The field's packs and model builder, for the fx step.</summary>
    public static (PackSet packs, ModelBuilder builder) Effects;

    public MoveProof(Player player, string dir, IEnumerable<string> lines)
    {
        this.player = player;
        this.dir = dir;
        foreach (var l in lines)
        {
            var s = l.Trim();
            if (s.Length == 0 || s.StartsWith('#')) continue;
            steps.Enqueue(s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            names.Add(s);
        }
        ProcessPriority = -10;      // before the player (Player.ProcessPriority -1)
    }

    public static void Attach(Player p)
    {
        var args = OS.GetCmdlineUserArgs();
        string dir = null, script = null;
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--move-shots") dir = args[i + 1];
            if (args[i] == "--move-script") script = args[i + 1];
        }
        if (dir == null) return;
        DirAccess.MakeDirRecursiveAbsolute(dir);
        var lines = script != null ? System.IO.File.ReadAllLines(script) : DefaultScript.Split('\n');
        var proof = new MoveProof(p, dir, lines) { Name = "MoveProof" };
        p.AddChild(proof);
        string trace = null;
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--move-trace") trace = args[i + 1];
        if (trace != null) p.AddChild(proof.trace = new MoveTrace(p, proof, trace) { Name = "MoveTrace" });
    }

    MoveTrace trace;
    int stepIndex, uneven;
    double camTraceT, camTraceLeft;
    string stepName = "";

    /// <summary>
    /// `--move-trace FILE`: after the camera and everything else that frame (priority 1000), one line per drawn frame of
    /// the frame time, the script step, the state, whether input is held, the body's position (MS2 axes), its facing and
    /// its point on the screen. At the end it prints, per script step, the time, the ground distance and its rate, the
    /// rise and drop, the time off the ground and the turn (with the time it took), and for the walk frames (input held,
    /// Walk, on the ground) the jitter signature of a fixed-tick body under a per-frame camera: frames that moved under a
    /// tenth of the median speed's share of their frame time, and those followed by one over 1.5 shares (a still frame,
    /// then a double step).
    /// </summary>
    public partial class MoveTrace : Node
    {
        readonly Player player;
        readonly MoveProof proof;
        readonly string path;
        readonly System.Text.StringBuilder sb = new("dt step state input x y z yaw sx sy\n");
        bool has;
        Vector3 prev;
        readonly List<(float d, float dt)> walk = new();
        readonly List<string> steps = new();
        int step = -1;
        float time, dist, z0, zMax, zMin, air, yaw0, yawPrev, turnTime;

        public MoveTrace(Player player, MoveProof proof, string path) { this.player = player; this.proof = proof; this.path = path; ProcessPriority = 1000; }

        static float Ms2Yaw(Player p) => Mathf.RadToDeg(p.Visual.Rotation.Y);

        public override void _Process(double delta)
        {
            var cam = GetViewport().GetCamera3D();
            var pos = player.GlobalPosition;
            var screen = cam != null ? cam.UnprojectPosition(pos) : Vector2.Zero;
            bool input = player.Input2 != Vector2.Zero;
            float dt = (float)delta, yaw = Ms2Yaw(player);
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            sb.Append(string.Format(ci, "{0:F6} {1} {2} {3} {4:F3} {5:F3} {6:F3} {7:F2} {8:F3} {9:F3}\n", delta, proof.stepIndex, player.State, input ? 1 : 0, pos.X, -pos.Z, pos.Y, yaw, screen.X, screen.Y));
            if (proof.stepIndex != step) { EndStep(); step = proof.stepIndex; time = dist = air = turnTime = 0; z0 = zMax = zMin = pos.Y; yaw0 = yawPrev = yaw; }
            else if (has)
            {
                var d = pos - prev; d.Y = 0;
                time += dt;
                dist += d.Length();
                zMax = Mathf.Max(zMax, pos.Y); zMin = Mathf.Min(zMin, pos.Y);
                if (player.State is Player.MoveState.Jump or Player.MoveState.Fall) air += dt;
                if (Mathf.Abs(Mathf.AngleDifference(Mathf.DegToRad(yawPrev), Mathf.DegToRad(yaw))) > 1e-4f) turnTime = time;
                if (input && player.State == Player.MoveState.Walk && player.Grounded) walk.Add((d.Length(), dt));
            }
            yawPrev = yaw;
            prev = pos; has = true;
        }

        void EndStep()
        {
            if (step < 0) return;
            float turn = Mathf.RadToDeg(Mathf.AngleDifference(Mathf.DegToRad(yaw0), Mathf.DegToRad(yawPrev)));
            steps.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[move] step {0} {1}: time {2:0.000} dist {3:0.0} rate {4:0.0}/s rise {5:0.0} drop {6:0.0} air {7:0.000} turn {8:0.0} in {9:0.000}",
                step, proof.StepName(step), time, dist, time > 0 ? dist / time : 0, zMax - z0, z0 - zMin, air, turn, turnTime));
        }

        public void Finish()
        {
            EndStep();
            System.IO.File.WriteAllText(path, sb.ToString());
            foreach (var l in steps) GD.Print(l);
            var rates = walk.Where(w => w.dt > 0).Select(w => w.d / w.dt).OrderBy(r => r).ToList();   // perf-ok: test only, once
            float med = rates.Count > 0 ? rates[rates.Count / 2] : 0;
            int still = 0, pairs = 0;
            for (int i = 0; i < walk.Count; i++)
            {
                float share = med * walk[i].dt;
                if (walk[i].d >= 0.1f * share) continue;
                still++;
                if (i + 1 < walk.Count && walk[i + 1].d > 1.5f * med * walk[i + 1].dt) pairs++;
            }
            GD.Print($"[move] trace walk frames {walk.Count}, median speed {med:0.0}/s, still frames {still}, still-then-double {pairs}, frame time min/max {(walk.Count > 0 ? walk.Min(w => w.dt) * 1000 : 0):0.0}/{(walk.Count > 0 ? walk.Max(w => w.dt) * 1000 : 0):0.0} ms");   // perf-ok: test only, once
        }
    }

    string StepName(int i) => i < names.Count ? names[i] : "end";
    readonly List<string> names = new();

    // Tria (02000001_tw_tria): south of the spawn point a flowerbed, then the plaza edge over the water; a ladder
    // column at (2100, -3900) from z 2700 to 3600.
    // Tria (02000001_tw_tria): a 150 block at (0, -1050) beside the plaza (walk level 3000); south of the spawn point
    // the plaza edge drops 200 into the water; a ladder column at (2100, -3900) from z 2700 to 3600.
    public const string DefaultScript = @"
tp -900 -900 3005 180
hold 0.6
shot 01_idle
hold 0.15 0 0 jump
shot 02_jump_rise
hold 0.2
shot 03_jump_down
until Idle 2
shot 04_landed
hold 0.5 0 -1 walk
shot 05_walk
tp -330 -1050 3005 270
hold 0.3 1 0
until Jump 1 1 0 jump
hold 0.2 1 0
shot 06_jump_onto_step
until Walk 2 1 0
hold 0.05 1 0
shot 07_on_step
tp -900 -1300 3005 180
until Swim 3 0 -1
shot 08_into_water
hold 0.5 -1 0
shot 09_swim
hold 0.8
shot 10_swim_idle
tp 2100 -3849 2855 0
hold 0.3
until Ladder 1 up
hold 0.2 up
shot 11_ladder_take
hold 1.5 up
shot 12_ladder_climb
until Idle 12 up
hold 0.4
shot 13_ladder_top
tp 2100 -3700 3605 0
hold 0.2
until Jump 1 0 -1 jump
hold 0.5 0 -1
shot 14_jump_off_top
until Land 3
shot 15_land
hold 0.8
shot 16_after_land
tp -300 -1050 3600 270
until Fall 1
hold 0.2
shot 17_fall
until Land 3
shot 18_fall_land
hold 0.8
";

    Vector2 InputFor(string[] s, int from)
    {
        foreach (var a in s.Skip(from))
        {
            if (a == "up") return new Vector2(0, 1);
            if (a == "down") return new Vector2(0, -1);
            if (a == "left") return new Vector2(-1, 0);
            if (a == "right") return new Vector2(1, 0);
        }
        var nums = s.Skip(from).Where(a => float.TryParse(a, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)).Take(2)
                    .Select(a => float.Parse(a, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        if (nums.Length < 2 || (nums[0] == 0 && nums[1] == 0)) return Vector2.Zero;
        // MS2 world direction -> camera relative input (x = right, y = forward)
        var w = FieldBuilder.ToGodotPoint(new Vec3(nums[0], nums[1], 0)).Normalized();
        var fwd = new Vector3(-Mathf.Sin(player.CameraYaw), 0, -Mathf.Cos(player.CameraYaw));
        var right = new Vector3(-fwd.Z, 0, fwd.X);
        return new Vector2(w.Dot(right), w.Dot(fwd));
    }

    void Step(double delta, bool sameFrame = false)
    {
        if (!sameFrame) frame++;
        if (!sameFrame && camTraceLeft > 0)
        {
            // the camera steps after this node in the frame, so the distance read here is the one camTraceT after the trace began
            GD.Print($"[move] camtrace t {camTraceT:0.0000} dist {(player.GetParent() as Field)?.CameraDistance:0.000}");
            camTraceT += delta;
            camTraceLeft -= delta;
        }
        player.ScriptDriven = true;
        if (player.State != lastState)
        {
            GD.Print($"[move] f{frame} {lastState} -> {player.State} at {Ms2Pos()} ground {player.GroundDistance:0} water {player.OnWater} anim {player.CurrentAnim}");
            lastState = player.State;
        }
        if (settle >= 0) return;
        if (steps.Count == 0)
        {
            GD.Print($"[move] done at {Ms2Pos()}; last fall height {player.LastFallHeight:0}");
            trace?.Finish();
            GetTree().Quit(0);
            return;
        }
        var s = steps.Peek();
        int queued = steps.Count;
        float dt = (float)delta;
        switch (s[0])
        {
            case "tp":
            {
                var p = new Vec3(F(s[1]), F(s[2]), F(s[3]));
                player.GlobalPosition = FieldBuilder.ToGodotPoint(p);
                player.ResetMotion();
                double yaw = F(s[4]) * Math.PI / 180;
                player.Face(FieldBuilder.ToGodotPoint(new Vec3(Math.Sin(yaw), -Math.Cos(yaw), 0)));
                player.Input2 = Vector2.Zero;
                Next();
                break;
            }
            case "hold":
            case "until":
            {
                float secs = F(s[s[0] == "hold" ? 1 : 2]);
                player.Input2 = InputFor(s, s[0] == "hold" ? 2 : 3);
                player.JumpHeld = s.Contains("jump");
                player.WalkMode = s.Contains("walk");
                t += dt;
                bool done = s[0] == "hold" ? t >= secs
                    : player.State.ToString() == s[1] || t >= secs;
                if (s[0] == "until" && t >= secs && player.State.ToString() != s[1])
                    GD.Print($"[move] timeout waiting for {s[1]} (state {player.State})");
                if (done)
                {
                    player.JumpHeld = false;
                    // a hold keeps the script's clock: the frame's time past its end counts toward the next step, so the
                    // steps start on the same script times (to a frame) at any frame rate
                    float over = s[0] == "hold" ? t - secs : 0f;
                    Next();
                    t = over;
                }
                break;
            }
            case "shot":
                settle = 2;
                break;
            case "hitch":                      // one long frame: this frame takes MS more (the next frame's time holds it)
                OS.DelayMsec(int.Parse(s[1]));
                Next();
                break;
            case "uneven":                     // every other frame takes MS more (0: off)
                uneven = int.Parse(s[1]);
                Next();
                break;
            case "wheel":                      // one mouse wheel notch this frame: 1 in, -1 out
                (player.GetParent() as Field)?.Wheel(int.Parse(s[1]));
                Next();
                break;
            case "cam":                        // print the zoom distance
                GD.Print($"[move] cam f{frame} dist {(player.GetParent() as Field)?.CameraDistance:0.00}");
                Next();
                break;
            case "camtrace":                   // print the zoom distance and the time since this command every frame for S s
                camTraceT = 0;
                camTraceLeft = F(s[1]);
                Next();
                break;
            case "dash":
                player.StartDash();
                Next();
                break;
            case "click":
                player.GetParent().GetNodeOrNull<MouseMove>("MouseMove")?.Go(FieldBuilder.ToGodotPoint(new Vec3(F(s[1]), F(s[2]), F(s[3]))));
                Next();
                break;
            case "ropes":
                foreach (var c in player.Climbs.All.Where(c => c.IsRope).OrderBy(c => (c.Base - player.GlobalPosition).Length()).Take(int.Parse(s[1])))
                    GD.Print($"[move] rope {c.Name} at ({c.Position.X:0}, {-c.Position.Z:0}, {c.Position.Y:0}) base ({c.Base.X:0}, {-c.Base.Z:0}, {c.Base.Y:0}) yaw {c.YawDeg:0} extrude {c.Extrude:0} usable {c.Usable}");
                GD.Print($"[move] ropes: {player.Climbs.All.Count(c => c.IsRope)}");
                Next();
                break;
            case "walls":
                PrintWalls(F(s[1]));
                Next();
                break;
            case "fx":
            {
                var (packs, mb) = Effects;
                Next();
                EffectScene scene;
                try { scene = EffectScene.Load(NifFile.Load(mb.Spec, packs.Read("Resource/Model/Effect", s[1].ToLowerInvariant()))); }
                catch (Exception ex) { GD.PushWarning($"move script fx {s[1]}: {ex.Message}"); break; }
                var at = player.GlobalPosition + FieldBuilder.ToGodotPoint(new Vec3(F(s[2]), F(s[3]), F(s[4])));
                float yaw = s.Length > 5 ? Mathf.DegToRad(F(s[5])) : 0;
                var fx = new Ms2Godot.Net.EffectInstance(scene, mb, 0, 1) { Name = "MoveProofFx", Position = at, Rotation = new Vector3(0, yaw, 0) };
                player.GetParent().AddChild(fx);
                effects.Add(fx);
                GD.Print($"[move] fx {s[1]}: {scene.Meshes.Count} meshes, {scene.Systems.Count} particle systems, {scene.Duration:0.00} s");
                break;
            }
            case "fxclear":
                foreach (var e in effects) if (IsInstanceValid(e)) e.QueueFree();
                effects.Clear();
                Next();
                break;
            default:
                GD.PushWarning($"move script: unknown step {s[0]}");
                Next();
                break;
        }
        // a command that takes no time (tp, hitch, uneven, dash, click, ...) lets the next step run in the same frame
        if (steps.Count < queued && s[0] is not ("hold" or "until" or "shot")) Step(0, true);
    }

    public override void _Process(double delta)
    {
        Step(delta);                       // the script's input for this frame's player step
        if (uneven > 0 && Engine.GetProcessFrames() % 2 == 0) OS.DelayMsec(uneven);   // per drawn frame
        if (settle < 0 || --settle > 0) return;
        var s = steps.Dequeue(); stepIndex++;
        var path = System.IO.Path.Combine(dir, s[1] + ".png");
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"[move] shot {s[1]} state {player.State} anim {player.CurrentAnim} at {Ms2Pos()} ground {player.GroundDistance:0} water {player.OnWater}");
        settle = -1;
        t = 0;
    }

    /// <summary>Cube faces toward -X or -Y with no cube in front, a cube to stand on in front, and two or more cubes up.</summary>
    void PrintWalls(float r)
    {
        var me = CubeGrid.Ms2(player.GlobalPosition);
        int n = 0;
        foreach (var (k, p) in player.Cubes.All.OrderBy(c => (new Vector2(c.Value.X, c.Value.Y) - new Vector2(me.X, me.Y)).Length()))
        {
            if ((new Vector2(p.X, p.Y) - new Vector2(me.X, me.Y)).Length() > r) break;
            foreach (var (dx, dy, face) in new[] { (-1, 0, "-X"), (0, -1, "-Y") })
            {
                if (player.Cubes.Neighbour(k, dx, dy, 0) != null || player.Cubes.Neighbour(k, dx, dy, -1) == null) continue;
                if (player.Cubes.Neighbour(k, 0, 0, 1) == null) continue;
                int up = 1;
                while (player.Cubes.Neighbour(k, 0, 0, up) != null) up++;
                // the snap ray from 90 above the stand point must reach this cube's face
                var stand = FieldBuilder.ToGodotPoint(new Vec3(p.X + dx * 110, p.Y + dy * 110, p.Z + 5));
                var from = stand + new Vector3(0, 90, 0);
                var rq = PhysicsRayQueryParameters3D.Create(from, from + FieldBuilder.ToGodotPoint(new Vec3(-dx, -dy, 0)) * 58f);
                rq.Exclude = new Godot.Collections.Array<Rid> { player.GetRid() };
                using var hit = player.GetWorld3D().DirectSpaceState.IntersectRay(rq);
                if (hit.Count == 0 || Mathf.Abs(((Vector3)hit[PhysicsKeys.Position] - from).Length() - 35f) > 3f) continue;
                var at = new Transform3D(Basis.Identity, stand + new Vector3(0, 2, 0));
                if (player.TestMove(at, Vector3.Up * 0.1f) || !player.TestMove(at, Vector3.Down * 10f)) continue;   // room, and a floor
                GD.Print($"[move] wall face {face} cube ({p.X:0}, {p.Y:0}, {p.Z:0}) {up} high; stand at ({p.X + dx * 110:0}, {p.Y + dy * 110:0}, {p.Z + 5:0})");
                if (++n >= 12) return;
            }
        }
        GD.Print($"[move] walls: {n} found");
    }

    void Next() { steps.Dequeue(); t = 0; stepIndex++; }

    string Ms2Pos()
    {
        var g = player.GlobalPosition;
        return $"({g.X:0}, {-g.Z:0}, {g.Y:0})";
    }

    static float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
}
