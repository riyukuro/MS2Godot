using System.Globalization;
using System.Xml.Linq;
using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2Godot;

/// <summary>
/// Field-walk slice: loads one field (default Tria, 02000001_tw_tria) from the player's data, places the female
/// character at SpawnPointPC and lets her walk. WASD / arrows move (camera relative), Shift walks, right mouse drag
/// turns the camera, wheel zooms.
/// User args (after --): --field XBLOCK, --shots DIR (scripted walk with screenshots, then quit).
/// </summary>
public partial class Field : Node3D
{
    PackSet packs;
    Player player;
    Camera3D camera;
    float camYaw, camPitch = Mathf.DegToRad(45f), camDist = 1250f;
    string shotDir;
    // scripted steps: seconds, input (right, forward), walk, camera yaw change (deg/s), shot file at the end
    readonly Queue<(float secs, Vector2 input, bool walk, float yawRate, string shot)> script = new();
    float stepTime;
    int shotSettle = -1;

    public override void _Ready()
    {
        string fieldName = "02000001_tw_tria";
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--shots") shotDir = args[i + 1];
            if (args[i] == "--field") fieldName = args[i + 1];
        }
        var data = Settings.DataDir;
        var nifxml = Settings.NifXml;
        if (string.IsNullOrEmpty(data) || string.IsNullOrEmpty(nifxml))
        {
            GD.PushError("Set MS2_DATA (client Data folder) and MS2_NIFXML (niftools nif.xml), or fill in local.cfg (see settings.example.cfg).");
            GetTree().Quit(2);
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        packs = new PackSet(data);
        var spec = new NifSpec(nifxml);
        var builder = new ModelBuilder(spec, packs);
        var assets = new AssetIndex(packs);
        var flats = new FlatLibrary(packs);
        var map = FieldMap.Load(packs, flats, fieldName);
        long tMap = sw.ElapsedMilliseconds;
        var fb = new FieldBuilder(packs, assets, builder);
        AddChild(fb.Build(map));
        long tBuild = sw.ElapsedMilliseconds;
        GD.Print($"MS2Godot field {fieldName}: {map.Entities.Count} entities, {fb.Instances} instances in {fb.MultiMeshes} multimeshes, " +
                 $"colliders {fb.Boxes} boxes / {fb.Meshes} PhysX-mesh entities / {fb.Whiteboxes} whiteboxes, {fb.Missing} unresolved models; " +
                 $"entity set {tMap} ms, build {tBuild - tMap} ms");

        BuildEnvironment(map, fieldName);
        SpawnPlayer(builder, map);
        camera = new Camera3D { Fov = 40, Near = 10, Far = 30000, Current = true };
        AddChild(camera);
        UpdateCamera();

        if (shotDir != null)
        {
            DirAccess.MakeDirRecursiveAbsolute(shotDir);
            script.Enqueue((1.0f, Vector2.Zero, false, 0, "field_spawn.png"));
            script.Enqueue((1.2f, new Vector2(0, 1), true, 0, "field_walk.png"));
            script.Enqueue((0.5f, Vector2.Zero, false, 0, null));
            script.Enqueue((2.0f, new Vector2(0, 1), false, 0, "field_run_fwd.png"));
            script.Enqueue((2.1f, new Vector2(1, 0), false, 0, "field_run_right.png"));
            script.Enqueue((0.6f, new Vector2(0, 1), false, 0, "field_stairs_1.png"));
            script.Enqueue((0.6f, new Vector2(0, 1), false, 0, "field_stairs_2.png"));
            script.Enqueue((0.8f, new Vector2(0, 1), false, 0, "field_stairs_3.png"));
            script.Enqueue((3.0f, new Vector2(0, -1), false, 0, "field_run_back.png"));
            script.Enqueue((3.0f, new Vector2(-1, 0.3f), false, 30, "field_run_left_turn.png"));
            script.Enqueue((1.0f, Vector2.Zero, false, 0, "field_idle_end.png"));
        }
    }

    void BuildEnvironment(FieldMap map, string fieldName)
    {
        Color fog = new(199 / 255f, 237 / 255f, 1f);
        float fogNear = 5276, fogFar = 20000;
        try
        {
            var x = XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", $"mapxblock/{fieldName}.xml")));
            var f = x.Root?.Element("fog");
            if (f != null)
            {
                var c = ((string)f.Attribute("color")).Split(',').Select(s => float.Parse(s, CultureInfo.InvariantCulture) / 255f).ToArray();
                fog = new Color(c[0], c[1], c[2]);
                fogNear = float.Parse((string)f.Attribute("near"), CultureInfo.InvariantCulture);
                fogFar = float.Parse((string)f.Attribute("far"), CultureInfo.InvariantCulture);
            }
        }
        catch (Exception ex) { GD.PushWarning("mapxblock: " + ex.Message); }

        float ambient = 0.8f, sunDim = 0.8f;
        Color ambientColor = Colors.White, sunColor = Colors.White;
        var sunDir = new Vec3(0, 0, -1);
        foreach (var e in map.Entities)
        {
            if (e.ModelName.StartsWith("MS2AmbientLight"))
            {
                var c = e.Numbers("AmbientColor");
                if (c != null) ambientColor = new Color((float)c[0], (float)c[1], (float)c[2]);
                ambient = (float)e.Number("Dimmer", 1);
            }
            else if (e.ModelName.StartsWith("MS2DirectionalLight"))
            {
                var c = e.Numbers("DiffuseColor");
                if (c != null) sunColor = new Color((float)c[0], (float)c[1], (float)c[2]);
                sunDim = (float)e.Number("Dimmer", 1);
                sunDir = e.Transform.Apply(new Vec3(1, 0, 0)) - e.Transform.Apply(new Vec3(0, 0, 0));   // Gamebryo lights shine along +X
            }
        }

        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = fog,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = ambientColor,
            AmbientLightEnergy = ambient,
            FogEnabled = true,
            FogMode = Godot.Environment.FogModeEnum.Depth,
            FogLightColor = fog,
            FogDepthBegin = fogNear,
            FogDepthEnd = fogFar,
            FogDensity = 1f,
            TonemapMode = Godot.Environment.ToneMapper.Linear,
        };
        AddChild(new WorldEnvironment { Environment = env });
        var sun = new DirectionalLight3D { LightColor = sunColor, LightEnergy = sunDim, ShadowEnabled = true, DirectionalShadowMaxDistance = 4000 };
        AddChild(sun);
        var d = FieldBuilder.ToGodotPoint(sunDir).Normalized();
        sun.LookAtFromPosition(Vector3.Zero, d, Mathf.Abs(d.Y) > 0.99f ? Vector3.Forward : Vector3.Up);
    }

    void SpawnPlayer(ModelBuilder builder, FieldMap map)
    {
        var md = builder.LoadModel("female/f_body.nif");
        var visual = builder.Build(md, "Female");
        var anim = visual.GetNode<AnimationPlayer>("Model/AnimationPlayer");
        var lib = anim.GetAnimationLibrary("");
        foreach (var (name, kf) in new[] { ("idle", "idle_a"), ("walk", "walk_a"), ("run", "run_a") })
            lib.AddAnimation(name, ModelBuilder.BuildAnimation(builder.LoadKf($"female/{kf}.kf"), md));

        player = Player.Create(visual, anim);
        var sp = map.Entities.FirstOrDefault(e => e.ModelName == "SpawnPointPC")
              ?? map.Entities.FirstOrDefault(e => e.ModelName.StartsWith("SpawnPointPC"));
        var pos = sp?.Position ?? new Vec3(0, 0, 0);
        player.SpawnPoint = FieldBuilder.ToGodotPoint(pos) + new Vector3(0, 5, 0);
        AddChild(player);
        player.GlobalPosition = player.SpawnPoint;
        // spawn facing: the model's forward is MS2 -Y, turned by the spawn point's Z rotation
        double yaw = (sp?.RotationDeg.Z ?? 0) * Math.PI / 180;
        var face = FieldBuilder.ToGodotPoint(new Vec3(Math.Sin(yaw), -Math.Cos(yaw), 0));
        player.Face(face);
        camYaw = Mathf.Atan2(-face.X, -face.Z);       // camera behind the character
        player.Anim.Play("idle");
        player.CurrentAnim = "idle";
        GD.Print($"spawn {sp?.Name} ms2 {pos} -> godot {player.SpawnPoint}");
    }

    void UpdateCamera()
    {
        player.CameraYaw = camYaw;
        var target = player.GlobalPosition + new Vector3(0, 60, 0);
        var back = new Vector3(Mathf.Sin(camYaw) * Mathf.Cos(camPitch), Mathf.Sin(camPitch), Mathf.Cos(camYaw) * Mathf.Cos(camPitch));
        camera.LookAtFromPosition(target + back * camDist, target, Vector3.Up);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm && (mm.ButtonMask & MouseButtonMask.Right) != 0)
        {
            camYaw -= mm.Relative.X * 0.005f;
            camPitch = Mathf.Clamp(camPitch + mm.Relative.Y * 0.005f, Mathf.DegToRad(10), Mathf.DegToRad(80));
        }
        if (e is InputEventMouseButton mb && mb.Pressed)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp) camDist = Mathf.Max(200, camDist * 0.9f);
            if (mb.ButtonIndex == MouseButton.WheelDown) camDist = Mathf.Min(4000, camDist * 1.1f);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (player == null) return;
        if (shotDir == null)
        {
            var v = Vector2.Zero;
            if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) v.Y += 1;
            if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) v.Y -= 1;
            if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) v.X += 1;
            if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) v.X -= 1;
            player.Input2 = v;
            player.WalkMode = Input.IsKeyPressed(Key.Shift);
        }
        else RunScript((float)delta);
    }

    public override void _Process(double delta)
    {
        if (player == null) return;
        UpdateCamera();
        if (shotSettle > 0 && --shotSettle == 0)
        {
            var (_, _, _, _, shot) = script.Dequeue();
            var path = System.IO.Path.Combine(shotDir, shot);
            GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print($"shot {path} pos {player.GlobalPosition} anim {player.CurrentAnim} floor {player.IsOnFloor()}");
            shotSettle = -1;
            stepTime = 0;
            if (script.Count == 0) { packs.Dispose(); GetTree().Quit(0); }
        }
    }

    void RunScript(float dt)
    {
        if (shotSettle > 0) return;           // keep the step's input while the shot settles
        if (script.Count == 0) { player.Input2 = Vector2.Zero; return; }
        var (secs, input, walk, yawRate, shot) = script.Peek();
        player.Input2 = input;
        player.WalkMode = walk;
        camYaw += Mathf.DegToRad(yawRate) * dt;
        stepTime += dt;
        if (stepTime < secs) return;
        if (shot == null) { script.Dequeue(); stepTime = 0; return; }
        shotSettle = 2;                       // grab after the next drawn frames
    }
}
