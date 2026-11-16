using Godot;
using Ms2.Formats.Model;

namespace Ms2Godot.Movement;

/// <summary>
/// Scripted movement run for screenshots: `-- --move-shots DIR [--move-script FILE]`. The script is one step per line:
///   tp X Y Z YAW              teleport (MS2 coordinates, yaw degrees as the spawn point's Rotation.z)
///   hold SECS [DX DY] [jump] [walk] [up|down]   hold a direction (MS2 world axes) or the up / down key
///   until STATE SECS [DX DY] [jump] [up|down]   hold until the player is in STATE (Idle, Walk, Jump, Fall, Land, Swim, Ladder)
///   shot NAME                 save DIR/NAME.png
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

    public MoveProof(Player player, string dir, IEnumerable<string> lines)
    {
        this.player = player;
        this.dir = dir;
        foreach (var l in lines)
        {
            var s = l.Trim();
            if (s.Length == 0 || s.StartsWith('#')) continue;
            steps.Enqueue(s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
        ProcessPhysicsPriority = -10;      // before the player
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
        p.AddChild(new MoveProof(p, dir, lines) { Name = "MoveProof" });
    }

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
tp 2100 -3800 2855 0
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

    public override void _PhysicsProcess(double delta)
    {
        frame++;
        player.ScriptDriven = true;
        if (player.State != lastState)
        {
            GD.Print($"[move] f{frame} {lastState} -> {player.State} at {Ms2Pos()} ground {player.GroundDistance:0} water {player.OnWater} anim {player.CurrentAnim}");
            lastState = player.State;
        }
        if (settle >= 0) return;
        if (steps.Count == 0)
        {
            GD.Print($"[move] done; last fall height {player.LastFallHeight:0}");
            GetTree().Quit(0);
            return;
        }
        var s = steps.Peek();
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
                if (done) { player.JumpHeld = false; Next(); }
                break;
            }
            case "shot":
                settle = 2;
                break;
            default:
                GD.PushWarning($"move script: unknown step {s[0]}");
                Next();
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (settle < 0 || --settle > 0) return;
        var s = steps.Dequeue();
        var path = System.IO.Path.Combine(dir, s[1] + ".png");
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"[move] shot {s[1]} state {player.State} anim {player.CurrentAnim} at {Ms2Pos()} ground {player.GroundDistance:0} water {player.OnWater}");
        settle = -1;
        t = 0;
    }

    void Next() { steps.Dequeue(); t = 0; }

    string Ms2Pos()
    {
        var g = player.GlobalPosition;
        return $"({g.X:0}, {-g.Z:0}, {g.Y:0})";
    }

    static float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
}
