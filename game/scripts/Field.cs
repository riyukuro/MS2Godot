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
    float? camPitch;                      // Ctrl + up/down arrows set the angle (0.15..1.57); a distance change goes back to the distance's own
    float camDist;                        // m_50: zoom distance, MinCameraDistance .. cam.Distance + PlayerHeight
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
        packs = PackSet.Shared(data);
        AssetIndex.Prefetch(packs);
        World.FieldContents.Prefetch(packs);   // the npc and region spawn tables
        Ui.UiAssets.Prefetch(packs, "gfxfontlib", "commonNameTag", "component_v1");   // shared UI movie parses overlap the field build
        var spec = new NifSpec(nifxml);
        var builder = new ModelBuilder(spec, packs);
        var assets = AssetIndex.For(packs);
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
        try
        {
            var k = new ClientConstants(packs);
            looseFinal = k.Float("CameraFinalLoose", looseFinal);
            looseNow = k.Float("CameraCurrentLoose", looseNow);
            looseUpdate = k.Float("CameraUpdateLoose", looseUpdate);
        }
        catch (Exception ex) { GD.PushWarning("constants: " + ex.Message); }
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

        // mapxblock heightfog: colour, upper, lower, percentage (a standard height blend in field_lit)
        RenderingServer.GlobalShaderParameterSet("ms2_hfog", Vector3.Zero);
        if (x?.Root?.Element("heightfog") is { } hfog)
        {
            try
            {
                var hc = ((string)hfog.Attribute("color")).Split(',').Select(s => float.Parse(s, CultureInfo.InvariantCulture) / 255f).ToArray();
                float up = float.Parse((string)hfog.Attribute("upper"), CultureInfo.InvariantCulture);
                float lo = float.Parse((string)hfog.Attribute("lower"), CultureInfo.InvariantCulture);
                float pct = float.Parse((string)hfog.Attribute("percentage"), CultureInfo.InvariantCulture) / 100f;
                RenderingServer.GlobalShaderParameterSet("ms2_hfog_color", new Vector3(hc[0], hc[1], hc[2]));
                RenderingServer.GlobalShaderParameterSet("ms2_hfog", new Vector3(up, lo, Mathf.Clamp(pct, 0f, 1f)));
            }
            catch (Exception ex) { GD.PushWarning("heightfog: " + ex.Message); }
        }

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
    void UpdateCamera(double dt = 0)
    {
        var ms2yaw = Mathf.DegToRad(cam.HAngleDeg + 180f) + camYaw;
        Vector3 eye, look, offset;
        Vector3? prevLook = camLook, prevEye = placedEye;
        // CTargetCameraMode::vfunc_8: one pass, plus another while the target outruns the eye's step or while the shown
        // view is blocked, at most 100; every pass eases the shown camera again with the frame's time.
        int passes = 1;
        for (int i = 0; i < passes && i < 100; i++)
        {
            Place(camDist, ms2yaw, out eye, out look, out offset);
            if (!camVelActive || placedEye == null) placedEye = eye;
            if (Mathf.Abs(camVel) >= 5f)
            {
                camVelActive = true;
                if (!camBlocked) camVel *= 0.9f;
                var pos = placedEye.Value + (look - placedEye.Value).Normalized() * camVel;
                if (Planar(prevLook ?? look, look) > Planar(prevEye ?? pos, pos)) passes++;
                float maxDist = cam.Distance + PlayerHeight;
                bool outside = camDist < MinCameraDistance || camDist > maxDist;
                SetCameraDistance(pos, look, !(outside && ((camVel > 0 && camDist > maxDist) || (camVel < 0 && camDist < MinCameraDistance))));
                Place(camDist, ms2yaw, out eye, out look, out offset);
                placedEye = eye;
            }
            else if (camVelActive)
            {
                camVel = 0;
                camVelActive = false;
                placedEye = eye;                               // vfunc_37
            }
            // the zoom action (one frame per wheel step): m_84 += x * min(dt, 1/15) * 2000, within +-150
            if (i == 0 && zoomInput != 0)
            {
                camVel = Mathf.Clamp(camVel + zoomInput * (float)Math.Min(dt, 1.0 / 15) * 2000f, -150f, 150f);
                zoomInput = 0;
            }
            player.CameraYaw = Mathf.Atan2(offset.X, offset.Z);   // movement is camera relative
            var ideal = Transform3D.Identity.LookingAt(look - placedEye.Value, Vector3.Up);
            ideal.Origin = placedEye.Value;
            var shown = EaseCamera(ideal, (float)dt);
            camera.GlobalTransform = shown;
            // vfunc_39: the near plane of the SHOWN camera; while blocked the next pass pushes the eye out by 10
            camBlocked = NearPlaneBlocked(shown, look);
            if (camBlocked) { camVel = -10f; passes++; }
            camLook = look;
        }
    }

    // CTargetCameraMode distance state: camDist is m_50 (the eye's distance from the target in the ground plane), camVel
    // m_84 (+ toward the target), camVelActive m_88, camBlocked m_f4; placedEye is m_d8's position, camLook m_78.
    float camVel;
    bool camVelActive, camBlocked;
    int zoomInput;
    Vector3? placedEye, camLook;

    static float Planar(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    /// <summary>CTargetCameraMode::FUN_14183bfd0: the distance becomes the eye's ground-plane distance from the target,
    /// within nearest .. farthest + player height when clamped. A step that crosses the farthest zoom (m_54) stops on it
    /// and ends the motion; the angle and height then follow the distance.</summary>
    void SetCameraDistance(Vector3 pos, Vector3 look, bool clamp)
    {
        float prev = camDist;
        camDist = Planar(look, pos);
        if (clamp) camDist = Mathf.Clamp(camDist, MinCameraDistance, cam.Distance + PlayerHeight);
        if ((camVel < 0 && camDist > cam.Distance && prev < cam.Distance) || (camVel > 0 && prev > cam.Distance && camDist < cam.Distance))
        {
            camDist = cam.Distance;
            camVel = 0;
        }
        camPitch = null;
    }

    // CTargetCameraMode::vfunc_39: the shown camera (m_c0) follows the one placed above (m_d8). Each frame it closes
    // t = dt * distance * loose of the eye's gap, plus a blend that grows over 3 s ((e^5x - 1) / (e^5 - 1)) once within
    // 10 units, and takes the placed camera when t > 1 or the gap is nil. The rotation goes the same t of the way by
    // its view and up axes (CCameraMode::FUN_140afdec0). loose (m_a0) starts at CameraCurrentLoose and moves toward
    // CameraFinalLoose (m_9c) at CameraUpdateLoose (m_a4) a second; the client reads them in the CTargetCameraMode
    // constructor unscaled, the camera-speed option at its default (100 %) leaves them so.
    float looseFinal = 0.08f, looseNow = 0.002f, looseUpdate = 0.03f;
    Transform3D? shownCam;
    bool camBlending;
    float camBlendTime;

    Transform3D EaseCamera(Transform3D placed, float dt)
    {
        if (shownCam is not { } shown || dt <= 0) return (shownCam = placed).Value;
        var gap = placed.Origin - shown.Origin;
        float dist = gap.Length(), blend = 0;
        if (camBlending)
        {
            if (dist >= 10f) camBlending = false;
            else
            {
                camBlendTime += dt;
                if (camBlendTime >= 3f) { blend = 1f; camBlending = false; }
                else blend = (Mathf.Exp(camBlendTime / 3f * 5f) - 1f) / 147.41316f;
            }
        }
        else if (!Mathf.IsZeroApprox(dist) && dist <= 10f) { camBlending = true; camBlendTime = 0; }
        if (Mathf.IsZeroApprox(dist)) return (shownCam = placed).Value;
        float speed = Mathf.Abs(looseUpdate) * dt;
        if (looseNow != looseFinal)
            looseNow = Mathf.Abs(looseNow - looseFinal) < speed ? looseFinal : looseNow + (looseNow > looseFinal ? -speed : speed);
        float t = dt * dist * looseNow + blend;
        if (t > 1f) return (shownCam = placed).Value;
        var view = (shown.Basis.Z + t * (placed.Basis.Z - shown.Basis.Z)).Normalized();
        var up = (shown.Basis.Y + t * (placed.Basis.Y - shown.Basis.Y)).Normalized();
        var side = up.Cross(view).Normalized();
        up = view.Cross(side).Normalized();
        return (shownCam = new Transform3D(new Basis(side, up, view), shown.Origin + t * gap)).Value;
    }

    const float PlayerHeight = 150f;            // CTargetCameraMode m_b4 (the player's height, added to the farthest zoom)

    void Place(float dist, float yaw, out Vector3 eye, out Vector3 look, out Vector3 offset)
    {
        float range = cam.Distance - MinCameraDistance;
        float t = range > 0 ? Mathf.Min((dist - MinCameraDistance) / range, 1f) : 1f;
        float pitch = camPitch ?? Mathf.DegToRad(90f + (cam.VAngleDeg - 90f) * t);
        // vfunc_27: the height follows the distance past the farthest zoom too (only the angle is capped)
        float th = range > 0 ? (dist - MinCameraDistance) / range : 1f;
        float height = cam.ZoomInHeightOffset + (cam.ZoomOutHeightOffset - cam.ZoomInHeightOffset) * th;
        var ms2 = new Vec3(dist * Math.Sin(yaw), dist * Math.Cos(yaw), dist * Math.Cos(pitch));
        offset = FieldBuilder.ToGodotPoint(ms2);
        look = player.GlobalPosition + new Vector3(0, height, 0);
        eye = look + offset;
    }

    /// <summary>The client's nine points of the near plane (corners, edge midpoints between them) each cast to the target
    /// and back; a hit both ways, or a hit from the target side alone, means the view is blocked.</summary>
    bool NearPlaneBlocked(Transform3D pose, Vector3 look)
    {
        var eye = pose.Origin;
        var fwd = -pose.Basis.Z.Normalized();
        var right = pose.Basis.X.Normalized();
        var up = pose.Basis.Y.Normalized();
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
            // Unk_PCMove::IsActionKeyDown: wheel forward reads as zoom in (300008), back as zoom out (300009), for the one
            // frame whose mouse delta holds it; zoom in wins when both come in one frame
            if (mb.ButtonIndex == MouseButton.WheelUp) zoomInput = 1;
            if (mb.ButtonIndex == MouseButton.WheelDown && zoomInput != 1) zoomInput = -1;
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
        UpdateCamera(delta);
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
