using Godot;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;

namespace Ms2Godot;

/// <summary>
/// Pilot scene: the female body on a ground plane, idle / walk from the player's own Character.m2d.
/// Keys: 1 idle, 2 walk. User args (after --): --shots DIR saves frames of both animations and quits.
/// </summary>
public partial class Main : Node3D
{
    PackSet packs;
    AnimationPlayer player;
    Camera3D camera;
    string shotDir;
    readonly Queue<(string anim, double time, string file)> shots = new();
    int settle = -1;
    bool seeked;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i] == "--shots") shotDir = args[i + 1];

        BuildStage();
        var data = Settings.DataDir;
        var nifxml = Settings.NifXml;
        if (string.IsNullOrEmpty(data) || string.IsNullOrEmpty(nifxml))
        {
            GD.PushError("Set MS2_DATA (client Data folder) and MS2_NIFXML (niftools nif.xml), or fill in local.cfg (see settings.example.cfg).");
            Quit(2);
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        packs = new PackSet(data);
        var builder = new ModelBuilder(new NifSpec(nifxml), packs);
        var md = builder.LoadModel("female/f_body.nif");
        var character = builder.Build(md, "Female");
        AddChild(character);
        player = character.GetNode<AnimationPlayer>("Model/AnimationPlayer");
        var lib = player.GetAnimationLibrary("");
        lib.AddAnimation("idle", ModelBuilder.BuildAnimation(builder.LoadKf("female/idle_a.kf"), md));
        lib.AddAnimation("walk", ModelBuilder.BuildAnimation(builder.LoadKf("female/walk_a.kf"), md));
        GD.Print($"MS2Godot: loaded f_body ({md.Bones.Count} bones, {md.Meshes.Count} meshes) + idle/walk in {sw.ElapsedMilliseconds} ms");
        player.Play("idle");

        if (shotDir != null)
        {
            DirAccess.MakeDirRecursiveAbsolute(shotDir);
            foreach (var t in new[] { 0.0, 0.5, 1.0, 1.5 }) shots.Enqueue(("idle", t, $"idle_{t:0.00}.png"));
            foreach (var t in new[] { 0.0, 0.1667, 0.3333, 0.5 }) shots.Enqueue(("walk", t, $"walk_{t:0.00}.png"));
            settle = 5;
        }
    }

    void BuildStage()
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.55f, 0.7f, 0.85f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.6f, 0.6f, 0.65f),
            AmbientLightEnergy = 0.8f,
        };
        AddChild(new WorldEnvironment { Environment = env });
        var sun = new DirectionalLight3D { ShadowEnabled = true, LightEnergy = 1.2f };
        AddChild(sun);
        sun.LookAtFromPosition(new Vector3(100, 200, 150), Vector3.Zero, Vector3.Up);

        var ground = new MeshInstance3D
        {
            Name = "Ground",
            Mesh = new PlaneMesh { Size = new Vector2(600, 600) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.55f, 0.4f), Roughness = 1f },
        };
        AddChild(ground);

        camera = new Camera3D { Fov = 40, Near = 1, Far = 5000 };
        AddChild(camera);
        camera.LookAtFromPosition(new Vector3(120, 95, 230), new Vector3(0, 55, 0), Vector3.Up);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true } k && player != null)
        {
            if (k.Keycode == Key.Key1) player.Play("idle");
            if (k.Keycode == Key.Key2) player.Play("walk");
        }
    }

    public override void _Process(double delta)
    {
        if (settle < 0) return;
        if (settle > 0) { settle--; return; }
        if (shots.Count == 0) { Quit(0); return; }
        var (anim, time, file) = shots.Peek();
        if (player.CurrentAnimation != anim || player.SpeedScale != 0)
        {
            player.Play(anim);
            player.SpeedScale = 0;
        }
        if (!seeked)
        {
            seeked = true;
            player.Seek(time, true);
            settle = 3;                       // let the pose reach the rendered frame
            return;
        }
        var path = System.IO.Path.Combine(shotDir, file);
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print("shot ", path);
        shots.Dequeue();
        seeked = false;
    }

    void Quit(int code)
    {
        packs?.Dispose();
        GetTree().Quit(code);
    }
}
