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
/// character at SpawnPointPC and lets her walk. WASD / arrows move (camera relative), Shift walks, the wheel zooms.
/// The camera follows the client's default field camera (CTargetCameraMode): a fixed yaw from the field data, and
/// pitch and target height that move with the zoom distance.
/// User args (after --): --field XBLOCK, --shots DIR (scripted walk with screenshots, then quit), --nav (navmesh overlay on; F9 toggles).
/// </summary>
public partial class Field : Node3D
{
    protected PackSet packs;
    protected Player player;
    protected Camera3D camera;
    FieldCamera cam = new();
    float camYaw;                         // Ctrl + left/right arrows turn the camera round the player
    float? camPitch;                      // Ctrl + up/down arrows set the angle (0.15..1.57), a wheel step goes back to the distance's own
    float camDist;                        // current zoom distance, MinCameraDistance .. cam.Distance
    const float MinCameraDistance = 400f; // CCameraMode default nearest zoom
    string shotDir;
    // scripted steps: seconds, input (right, forward), walk, shot file at the end
    readonly Queue<(float secs, Vector2 input, bool walk, string shot)> script = new();
    float stepTime;
    int shotSettle = -1;

    public override void _Ready()
    {
        string fieldName = FieldNameFor("02000001_tw_tria");
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--shots") shotDir = args[i + 1];
            if (args[i] == "--field") fieldName = args[i + 1];
        }
        bool navOn = System.Array.IndexOf(args, "--nav") >= 0;
        int questNpc = 0;
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--quest-npc") int.TryParse(args[i + 1], out questNpc);
        if (SpawnsLocalPlayer && World.FieldContents.PendingXBlock != null) fieldName = World.FieldContents.PendingXBlock;   // sandbox portal move
        var data = Settings.DataDir;
        var nifxml = Settings.NifXml;
        if (string.IsNullOrEmpty(data) || string.IsNullOrEmpty(nifxml))
        {
            GD.PushError("Set MS2_DATA (client Data folder) and MS2_NIFXML (niftools nif.xml), or fill in local.cfg (see settings.example.cfg).");
            GetTree().Quit(2);
            return;
        }

        GlowPass.Wanted = OS.GetEnvironment("MS2_NOGLOW") != "1";   // glow twins are built only for a field with the glow pass
        var sw = System.Diagnostics.Stopwatch.StartNew();
        packs = new PackSet(data);
        var spec = new NifSpec(nifxml);
        var builder = new ModelBuilder(spec, packs);
        var assets = new AssetIndex(packs);
        var flats = new FlatLibrary(packs);
        var map = FieldMap.Load(packs, flats, fieldName);
        long tMap = sw.ElapsedMilliseconds;
        var fb = new FieldBuilder(packs, assets, builder);
        var contents = new World.FieldContents(packs, builder, assets);   // animated props, actors, npcs, portals
        fb.DrawsInstance = contents.Props.TakesModel;
        AddChild(fb.Build(map));
        contents.Props.Entities.AddRange(fb.DrawnElsewhere);
        AddChild(World.FieldEffects.Build(packs, builder, fb.EffectEntities));
        long tBuild = sw.ElapsedMilliseconds;
        GD.Print($"MS2Godot field {fieldName}: {map.Entities.Count} entities, {fb.Instances} instances in {fb.MultiMeshes} multimeshes, " +
                 $"colliders {fb.Boxes} boxes / {fb.Meshes} PhysX-mesh entities / {fb.Whiteboxes} whiteboxes, {fb.Missing} unresolved models; " +
                 $"entity set {tMap} ms, build {tBuild - tMap} ms");

        BuildEnvironment(map, fieldName);
        GD.Print($"MS2Godot lights: {FieldLights.Add(this, map)}");
        if (SpawnsLocalPlayer) SpawnPlayer(builder, map);
        AddChild(contents);
        contents.Start(map, fieldName, player, SpawnsLocalPlayer);
        var hdr = new FieldHdr();
        try
        {
            var fd = FieldData.FindByXBlock(packs, fieldName);
            cam = fd?.Camera ?? cam;
            hdr = fd?.Hdr ?? hdr;
            if (fd != null) BlobShadow.Strength = fd.SimpleShadow;
            if (player != null && fd != null) player.CheckClimb = fd.CheckClimb;
        }
        catch (Exception ex) { GD.PushWarning("fielddata: " + ex.Message); }
        camDist = cam.Distance;
        // Gamebryo's camera FOV is vertical, as Godot's (keep height); near / far are the CCameraMode defaults
        camera = new Camera3D { Fov = cam.FovDeg, Near = 250, Far = 18000, Current = true };
        var bloom = new Ms2Bloom { Threshold = hdr.Threshold, Offset = hdr.Offset, BloomScale = hdr.BloomScale, GlowScale = hdr.GlowScale };
        camera.Compositor = new Compositor { CompositorEffects = new Godot.Collections.Array<CompositorEffect> { bloom } };
        if (OS.GetEnvironment("MS2_NOGLOW") != "1") bloom.Glow = GlowPass.Attach(this, camera);   // MS2_NOGLOW=1: the bloom without the glow share (before shots)
        AddChild(camera);
        if (player != null) { UpdateCamera(); AttachMovementOptions(builder); }
        OnFieldBuilt(builder, map, fieldName);
        Audio = FieldAudio.Start(this, data, packs, fieldName, map, player, player?.Anim);   // sound: game/audio
        NavDebug.Start(this, packs, assets, spec, flats, fieldName, navOn);   // F9 / --nav: navmesh overlay
        if (SpawnsLocalPlayer) Quest = World.QuestGuide.Start(this, packs, builder, map, player, hdrFieldId(fieldName), questNpc);   // --quest-npc ID: the quest guide beam

        if (shotDir != null)
        {
            DirAccess.MakeDirRecursiveAbsolute(shotDir);
            script.Enqueue((1.0f, Vector2.Zero, false, "field_spawn.png"));
            script.Enqueue((1.2f, new Vector2(0, 1), true, "field_walk.png"));
            script.Enqueue((0.5f, Vector2.Zero, false, null));
            script.Enqueue((2.0f, new Vector2(0, 1), false, "field_run_fwd.png"));
            script.Enqueue((2.1f, new Vector2(1, 0), false, "field_run_right.png"));
            script.Enqueue((0.6f, new Vector2(0, 1), false, "field_stairs_1.png"));
            script.Enqueue((0.6f, new Vector2(0, 1), false, "field_stairs_2.png"));
            script.Enqueue((0.8f, new Vector2(0, 1), false, "field_stairs_3.png"));
            script.Enqueue((3.0f, new Vector2(0, -1), false, "field_run_back.png"));
            script.Enqueue((3.0f, new Vector2(-1, 0.3f), false, "field_run_left.png"));
            script.Enqueue((1.0f, Vector2.Zero, false, "field_idle_end.png"));
        }
    }

    // Hooks for scenes that build on the field (the replay scene in net/): the field to load, whether the walkable player
    // exists, and a call once the field, environment and camera are in place.
    protected virtual string FieldNameFor(string defaultName) => defaultName;
    protected virtual bool SpawnsLocalPlayer => true;
    protected virtual void OnFieldBuilt(ModelBuilder builder, FieldMap map, string fieldName) { }

    protected FieldAudio Audio;
    protected World.QuestGuide Quest;

    int hdrFieldId(string fieldName) { try { return FieldData.FindByXBlock(packs, fieldName)?.Id ?? 0; } catch { return 0; } }

    void BuildEnvironment(FieldMap map, string fieldName)
    {
        Color fog = new(199 / 255f, 237 / 255f, 1f);
        float fogNear = 5276, fogFar = 20000;
        XDocument x = null;
        try
        {
            x = XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", $"mapxblock/{fieldName}.xml")));
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
        Color ambientColor = Colors.White, sunColor = Colors.White, sunAmbient = Colors.Black, sunSpecular = Colors.White;
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
                var a = e.Numbers("AmbientColor");
                if (a != null) sunAmbient = new Color((float)a[0], (float)a[1], (float)a[2]);
                var sp = e.Numbers("SpecularColor");
                if (sp != null) sunSpecular = new Color((float)sp[0], (float)sp[1], (float)sp[2]);
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
        FieldBackdrop.Add(this, env, packs, x);
        var sun = new DirectionalLight3D { LightColor = sunColor, LightEnergy = sunDim, ShadowEnabled = true, DirectionalShadowMaxDistance = 4000 };
        AddChild(sun);
        var d = FieldBuilder.ToGodotPoint(sunDir).Normalized();
        sun.LookAtFromPosition(Vector3.Zero, d, Mathf.Abs(d.Y) > 0.99f ? Vector3.Forward : Vector3.Up);
        Vector3 Rgb(Color c, float k) => new(c.R * k, c.G * k, c.B * k);
        RenderingServer.GlobalShaderParameterSet("ms2_ambient", Rgb(ambientColor, ambient));
        RenderingServer.GlobalShaderParameterSet("ms2_sun_color", Rgb(sunColor, sunDim));
        RenderingServer.GlobalShaderParameterSet("ms2_sun_ambient", Rgb(sunAmbient, sunDim));
        RenderingServer.GlobalShaderParameterSet("ms2_sun_specular", Rgb(sunSpecular, sunDim));
        RenderingServer.GlobalShaderParameterSet("ms2_sun_dir", d);
    }

    void SpawnPlayer(ModelBuilder builder, FieldMap map)
    {
        var (visual, anim, md, bodyDir) = Look.PlayerLook.Build(builder, packs);

        player = Player.Create(visual, anim);
        BlobShadow.Load(packs);
        BlobShadow.Add(player, 100f);
        player.Anims = Movement.ActionAnims.Load(packs, builder, md, anim, bodyDir);
        player.Climbs = Movement.Climbables.From(map);
        player.Cubes = Movement.CubeGrid.From(map);
        Movement.MoveConfig.LoadConstants(packs);
        player.Gravity = Movement.MoveConfig.FieldGravity(map);
        var sp = map.Entities.FirstOrDefault(e => e.ModelName == "SpawnPointPC")
              ?? map.Entities.FirstOrDefault(e => e.ModelName.StartsWith("SpawnPointPC"));
        var pos = sp?.Position ?? new Vec3(0, 0, 0);
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i] == "--spawn")   // x,y,z in field units, for looking at a given spot
            {
                var v = args[i + 1].Split(',');
                pos = new Vec3(double.Parse(v[0], CultureInfo.InvariantCulture), double.Parse(v[1], CultureInfo.InvariantCulture), double.Parse(v[2], CultureInfo.InvariantCulture));
            }
        player.SpawnPoint = FieldBuilder.ToGodotPoint(pos) + new Vector3(0, 5, 0);
        player.FallReturnY = (map.Entities.Count > 0 ? (float)map.Entities.Min(e => e.Position.Z) : player.SpawnPoint.Y) - 1500f;
        AddChild(player);
        player.GlobalPosition = player.SpawnPoint;
        // spawn facing: the model's forward is MS2 -Y, turned by the spawn point's Z rotation
        double yaw = (sp?.RotationDeg.Z ?? 0) * Math.PI / 180;
        var face = FieldBuilder.ToGodotPoint(new Vec3(Math.Sin(yaw), -Math.Cos(yaw), 0));
        player.Face(face);
        GD.Print($"spawn {sp?.Name} ms2 {pos} -> godot {player.SpawnPoint}");
    }

    /// <summary>
    /// Command line movement options, all off by default as in the client: --mouse-move (interface/EnableMouseControl),
    /// --gamepad (gamepad/bGamePadUse), --ride RIDING_ID (get on a mount, as a RideOnAction would).
    /// </summary>
    void AttachMovementOptions(ModelBuilder builder)
    {
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--mouse-move") AddChild(new Movement.MouseMove(player, camera) { Name = "MouseMove" });
            if (args[i] == "--gamepad") Movement.MoveInput.GamePad = true;
            if (args[i] == "--ride" && i + 1 < args.Length && int.TryParse(args[i + 1], out int ride))
            {
                try { player.RideOn(Mount.Load(packs, builder, ride)); GD.Print($"riding {ride}"); }
                catch (Exception ex) { GD.PushWarning($"ride {ride}: {ex.Message}"); }
            }
        }
    }

    /// <summary>
    /// CTargetCameraMode: with t = (distance - nearest) / (farthest - nearest), the angle from the up axis goes from
    /// 90 (level) at the nearest zoom to vangle at the farthest, and the target height from zoomInHeightOffset to
    /// zoomOutHeightOffset. Eye = target + (d sin yaw, d cos yaw, d cos pitch) in MS2 axes, yaw = hangle + 180.
    /// </summary>
    void UpdateCamera()
    {
        var ms2yaw = Mathf.DegToRad(cam.HAngleDeg + 180f) + camYaw;
        var target = player.GlobalPosition;
        Vector3 eye = Vector3.Zero, look = Vector3.Zero, offset = Vector3.Zero;
        float maxDist = cam.Distance + PlayerHeight;
        // CTargetCameraMode::vfunc_38 / vfunc_8: while the near plane is blocked the eye is pushed outward in steps of 10
        // (the client repeats the step up to 100 times a frame), the angle following the distance.
        float d = camDist;
        for (int i = 0; i < 100; i++)
        {
            Place(d, ms2yaw, out eye, out look, out offset);
            if (d >= maxDist || !NearPlaneBlocked(eye, look)) break;
            d = Mathf.Min(d + 10f, maxDist);
        }
        player.CameraYaw = Mathf.Atan2(offset.X, offset.Z);   // movement is camera relative
        camera.LookAtFromPosition(eye, look, Vector3.Up);
    }

    const float PlayerHeight = 150f;            // CTargetCameraMode m_b4 (the player's height, added to the farthest zoom)

    void Place(float dist, float yaw, out Vector3 eye, out Vector3 look, out Vector3 offset)
    {
        float range = cam.Distance - MinCameraDistance;
        float t = range > 0 ? Mathf.Min((dist - MinCameraDistance) / range, 1f) : 1f;
        float pitch = camPitch ?? Mathf.DegToRad(90f + (cam.VAngleDeg - 90f) * t);
        float height = cam.ZoomInHeightOffset + (cam.ZoomOutHeightOffset - cam.ZoomInHeightOffset) * t;
        var ms2 = new Vec3(dist * Math.Sin(yaw), dist * Math.Cos(yaw), dist * Math.Cos(pitch));
        offset = FieldBuilder.ToGodotPoint(ms2);
        look = player.GlobalPosition + new Vector3(0, height, 0);
        eye = look + offset;
    }

    /// <summary>The client's nine points of the near plane (corners, edge midpoints between them) each cast to the target
    /// and back; a hit both ways, or a hit from the target side alone, means the view is blocked.</summary>
    bool NearPlaneBlocked(Vector3 eye, Vector3 look)
    {
        var fwd = (look - eye).Normalized();
        var right = fwd.Cross(Vector3.Up).Normalized();
        var up = right.Cross(fwd);
        float near = camera.Near, tan = Mathf.Tan(Mathf.DegToRad(camera.Fov) * 0.5f);
        float asp = GetViewport().GetVisibleRect().Size.Aspect();
        var c = eye + fwd * near;
        Vector3 hx = right * near * tan * asp, hy = up * near * tan;
        var p = new Vector3[9];
        p[0] = c - hx + hy; p[1] = c + hx + hy; p[2] = c - hx - hy; p[3] = c + hx - hy;
        p[4] = (p[0] + p[3]) * 0.5f; p[5] = (p[0] + p[1]) * 0.5f; p[6] = (p[0] + p[2]) * 0.5f;
        p[7] = (p[1] + p[3]) * 0.5f; p[8] = (p[2] + p[3]) * 0.5f;
        var space = GetWorld3D().DirectSpaceState;
        var skip = new Godot.Collections.Array<Rid> { player.GetRid() };
        foreach (var from in p)
        {
            var q = PhysicsRayQueryParameters3D.Create(from, look); q.Exclude = skip;
            var hit = space.IntersectRay(q);
            if (hit.Count == 0)
            {
                var b = PhysicsRayQueryParameters3D.Create(look, from); b.Exclude = skip;
                if (space.IntersectRay(b).Count > 0) return true;
            }
            else
            {
                var b = PhysicsRayQueryParameters3D.Create((Vector3)hit["position"], from); b.Exclude = skip;
                if (space.IntersectRay(b).Count > 0) return true;
            }
        }
        return false;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.Pressed)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp || mb.ButtonIndex == MouseButton.WheelDown) camPitch = null;
            if (mb.ButtonIndex == MouseButton.WheelUp) camDist = Mathf.Max(MinCameraDistance, camDist - 70f);
            if (mb.ButtonIndex == MouseButton.WheelDown) camDist = Mathf.Min(cam.Distance, camDist + 70f);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (player == null) return;
        if (shotDir != null)                     // otherwise Player reads the keyboard (Movement.MoveInput)
        {
            player.ScriptDriven = true;
            RunScript((float)delta);
        }
    }

    public override void _Process(double delta)
    {
        if (player == null) return;
        if (Input.IsKeyPressed(Key.Ctrl))        // CTargetCameraMode::vfunc_8: about one radian a second
        {
            float step = (float)Math.Min(delta, 1.0 / 15);
            if (Input.IsKeyPressed(Key.Left)) camYaw += step;
            if (Input.IsKeyPressed(Key.Right)) camYaw -= step;
            float pitch0 = camPitch ?? Mathf.DegToRad(90f + (cam.VAngleDeg - 90f) * Mathf.Min((camDist - MinCameraDistance) / Math.Max(cam.Distance - MinCameraDistance, 1f), 1f));
            if (Input.IsKeyPressed(Key.Up)) camPitch = Mathf.Clamp(pitch0 + step, 0.15f, 1.57f);
            if (Input.IsKeyPressed(Key.Down)) camPitch = Mathf.Clamp(pitch0 - step, 0.15f, 1.57f);
        }
        UpdateCamera();
        if (shotSettle > 0 && --shotSettle == 0)
        {
            var (_, _, _, shot) = script.Dequeue();
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
        var (secs, input, walk, shot) = script.Peek();
        player.Input2 = input;
        player.WalkMode = walk;
        stepTime += dt;
        if (stepTime < secs) return;
        if (shot == null) { script.Dequeue(); stepTime = 0; return; }
        shotSettle = 2;                       // grab after the next drawn frames
    }
}
