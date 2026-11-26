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
/// character at SpawnPointPC and lets her walk. The arrows move (camera relative), comma walks, the wheel zooms.
/// The camera follows the client's default field camera (CTargetCameraMode): a fixed yaw from the field data, and
/// pitch and target height that move with the zoom distance.
/// User args (after --): --field XBLOCK, --shots DIR (scripted walk with screenshots, then quit), --nav (navmesh overlay on; F9 toggles).
/// </summary>
public partial class Field : Node3D, World.ITargetCamera, ITargetCameraView, Ms2.Ui.Hud.IFieldLights
{
    /// <summary>The field a session opens in when nothing names another (Tria): the setup step builds its navmesh.</summary>
    public const string StartField = "02000001_tw_tria";

    /// <summary>The scene-tree group every field joins, so the HUD finds it without walking the tree.</summary>
    public const string Group = "field";
    public Field() { AddToGroup(Group); }
    public override void _EnterTree() { if (FrameProbe.Enabled) AddChild(new FrameProbe()); }

    // CTargetCameraMode / CHomeCameraMode's near-plane test casts with RayCastFromPoint (targetcameramode.cpp:763-771,
    // homecameramode.cpp:617-625), which is CPhysXHelper::CastRay (physxhelper.cpp:433-449): one-sided on mesh triangles.
    // Every one of those casts passes the groups mask 0x209: groups 0, 3 and 9, so an npc capsule (group 4) or the PC
    // (group 2) never blocks the view
    const uint OcclusionGroups = 0x209;
    readonly RayQuery occRay = RayQuery.CastRay(OcclusionGroups);
    protected PackSet packs;
    protected Player player;
    protected Camera3D camera;
    /// <summary>Where the field's 3D sounds are heard. CSoundService's update (soundservice.cpp:975-999) sets the FMOD listener
    /// at the camera target's position (CameraMgr::FUN_140a38d60, bosstargetcamera.cpp:438: the own PC) with the camera's
    /// forward and up; Godot's default listener is the camera itself, about 1500 units back.</summary>
    AudioListener3D listener;
    FieldBackdrop backdrop;      // the sky picture, moved each frame as the client's background movie (FieldBackdrop)
    /// <summary>The field in the tree (the one the option dialog's HDR switch reaches, as the client's SetHdr reaches the
    /// running post-processing service and light service).</summary>
    public static Field Active { get; private set; }
    FieldData fieldData;
    Ms2Bloom bloom;
    GlowPass glow;
    Godot.Environment environment;
    DirectionalLight3D sunLight;
    float baseAmbient, baseSunDim, lightScale = 1f;
    Color ambientColor, sunColor, sunAmbient, sunSpecular;
    /// <summary>The prop fade between the player and the camera (BGTransparencyMgr).</summary>
    public World.FieldFade Fade { get; private set; }
    /// <summary>The field's npc spawn points by spawn id (WorldService::FUN_141bcfff0), for the npcs' spawn state.</summary>
    public FieldSpawnPoints SpawnPoints { get; private set; }
    /// <summary>The field's trigger objects (CTriggerService), which the live session's Trigger packets drive.</summary>
    public World.FieldTriggers Triggers { get; private set; }
    /// <summary>The field's breakable objects as drawn (CBreakableObject::SetState), which the live session's Breakable packets drive.</summary>
    public World.FieldBreakables Breakables { get; private set; }
    /// <summary>The field's vibrate objects' instances and colliders, driven by the session's vibrators.</summary>
    public World.FieldVibrateDraws Vibrates { get; private set; }
    /// <summary>The interact objects' state draws (CInteractMeshObject::vfunc_3: visibility, actor sequence, box, state effect).</summary>
    public World.FieldInteractDraws Interacts { get; private set; }
    /// <summary>The field lifts' fades and removals (live only).</summary>
    public World.FieldLiftables Liftables { get; private set; }
    /// <summary>The portal entities' effects, by entity; <see cref="PortalDraws"/> shows them by their CPortal's flag.</summary>
    readonly Dictionary<Ms2.Formats.World.FieldEntity, Node3D> portalEffects = new();
    Ms2.Ui.PortalDraws portalDraws;
    /// <summary>The live check: whether a portal entity's effect is drawn; null when the entity has none.</summary>
    public bool? PortalDrawn(Ms2.Formats.World.FieldEntity e) =>
        e != null && portalEffects.TryGetValue(e, out var fx) && IsInstanceValid(fx) ? fx.Visible : null;
    /// <summary>The lift target boxes' quest markers (CLiftableTarget's effect).</summary>
    public World.LiftTargetMarkers LiftMarkers { get; private set; }
    Ms2.Ui.BreakableObjects devBreakables;    // offline only, with MS2_BREAKABLE_STATE

    /// <summary>Dev: MS2_BREAKABLE_STATE="name=state[,name=state]" (name * = every marker) gives an offline field breakable list for those states.</summary>
    Ms2.Ui.BreakableObjects DevBreakables() =>
        Net.LiveSession.Active || string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("MS2_BREAKABLE_STATE")) ? null : devBreakables = new();

    void DevBreakableStates()
    {
        if (devBreakables == null) return;
        var list = new List<Ms2.Net.Packets.BreakableEntry>();
        foreach (var pair in System.Environment.GetEnvironmentVariable("MS2_BREAKABLE_STATE").Split(','))
            if (pair.Split('=') is [var name, var st] && byte.TryParse(st, out byte state))
            {
                if (name.Trim() != "*") list.Add(new(name.Trim(), state, 0, 0, 0));
                else foreach (var m in devBreakables.Sorted) list.Add(new(m.Name, state, 0, 0, 0));   // every marker
            }
        devBreakables.Apply(new Ms2.Net.Packets.BreakableUpdate(1, list));
        GD.Print($"MS2Godot breakables: {devBreakables.Count} markers, {Breakables.Count} drawn, {list.Count} dev states");
        foreach (var m in devBreakables.Sorted) GD.Print($"  breakable {m.Name} at {m.Position} state {m.State} drawn {Breakables.Shown(m.Entity)}");
    }
    FieldCamera cam = new();
    float camYaw;                         // Ctrl + left/right arrows turn the camera round the player
    readonly TargetCameraZoom zoom = new();   // CTargetCameraMode's distance, angle and passes
    public const float MinCameraDistance = TargetCameraZoom.Nearest; // CCameraMode default nearest zoom (m_58)
    string shotDir;
    // scripted steps: seconds, input (right, forward), walk, shot file at the end
    readonly Queue<(float secs, Vector2 input, bool walk, string shot)> script = new();
    float stepTime;
    int shotSettle = -1;

    public override async void _Ready()
    {
        try { await LoadField(); }
        catch (OperationCanceledException)   // freed while it loaded (NextFrame); _ExitTree has dropped its move and cover
        {
            abandon?.Invoke();
        }
        catch (Exception ex)
        {
            GD.PushError("field load: " + ex);
            // a loading screen this load carried but never adopted would stay over the field, and its move would be taken by the next load
            World.FieldContents.AbandonLoad(this, loadMove);
            DropUnbuiltPlayer();
            if (IsInstanceValid(this) && !IsLoaded) EndLoad();   // what was built is shown, as when the build ran in one frame
        }
    }

    /// <summary>
    /// A load that threw between Player.Create and AddChild(player) (SpawnPlayer: the look, the action animations) leaves
    /// `player` set but outside the tree, and the camera never made. The field then ticks on it every frame: UpdateCamera
    /// reads its GlobalPosition ("!is_inside_tree") and aims the camera at it (Basis.LookingAt "the vector cannot be zero"),
    /// thousands of errors over the one that matters. A player that never entered the tree is freed and the field goes on
    /// as one without a local player (the replay field's case), which every per-frame site already handles.
    /// </summary>
    void DropUnbuiltPlayer()
    {
        if (player == null || player.IsInsideTree()) return;
        if (IsInstanceValid(player)) player.Free();
        player = null;
    }

    async System.Threading.Tasks.Task LoadField()
    {
        string fieldName = FieldNameFor(StartField);
        var args = OS.GetCmdlineUserArgs();
        bool fieldArg = false;
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--shots") shotDir = args[i + 1];
            if (args[i] == "--field") { fieldName = args[i + 1]; fieldArg = true; }
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
        // --field takes an xblock name or a map id; a map id gives the xblock of that map's own fielddata entry (its <xblock name>,
        // which is not the id: map 52000065 is xblock "52000065_QD")
        if (fieldArg && int.TryParse(fieldName, out int fieldId))
        {
            try { fieldName = FieldData.FindById(packs, fieldId)?.XBlock ?? fieldName; }
            catch (Exception ex) { GD.PushWarning("fielddata: " + ex.Message); }
        }
        BeginLoad();
        if (coverStarted) await NextFrame();         // the new loading screen enters the tree and is drawn before the build
        AssetIndex.Prefetch(packs);
        packs.Prefetch("Resource/Exported", "Resource/Model/Map", "Resource/Model/Effect", "Resource/Model/Npc", "Resource/Model/Textures", "Resource/Image", "Resource/Model/Character", "Resource/Model/Item");   // every field's build opens these; their indexes open side by side
        World.FieldContents.Prefetch(packs);   // the npc and region spawn tables
        Ms2.Ui.GameTables.Shared(packs, GameOptions.StringLanguage).PrefetchSkillDamage();   // the first hit reads it
        Ui.UiAssets.Prefetch(packs, "gfxfontlib", "commonNameTag", "component_v1");   // shared UI movie parses overlap the field build
        var spec = ModelBuilder.SharedSpec(nifxml);
        var builder = new ModelBuilder(spec, packs);
        var assets = AssetIndex.For(packs);
        var flats = new FlatLibrary(packs);
        await Budget();
        var map = FieldMap.Load(packs, flats, fieldName);
        SpawnPoints = new FieldSpawnPoints(map.Entities);
        long tMap = sw.ElapsedMilliseconds;
        await Budget();
        FieldData fd = null;
        try { fd = FieldData.FindByXBlock(packs, fieldName); }
        catch (Exception ex) { GD.PushWarning("fielddata: " + ex.Message); }
        fieldData = fd;
        FieldShadows.Begin(packs, fd?.ShadowOptionHighLevel ?? 0);
        bool hdrOn = Ms2.Formats.Options.GraphicGates.IsHdrEnabled(fd, GameOptions.Current.GraphicOption);
        var fb = new FieldBuilder(packs, assets, builder) { InstancingGrid = fd?.InstancingGrid ?? 0, CastsShadows = FieldShadows.MapObjects, ShadowLevel = FieldShadows.Level };
        var contents = new World.FieldContents(packs, builder, assets);   // animated props, actors, npcs, portals
        fb.DrawsInstance = contents.Props.TakesModel;
        fb.Lease = models;
        fb.Load = contents.Load = new World.LoadCount();   // the entity count the loading screen shows
        fb.Lights = FieldLightSet.Read(map.Entities);       // before the build: each draw takes the lights that reach it
        FieldLights.Clear();   // the region skills replayed during the build and packets in the load wait for this field's Add
        lightSet = fb.Lights;
        abandon = () =>   // what a stopped load holds off the tree
        {
            fb.Abandon();
            if (GodotObject.IsInstanceValid(contents) && contents.GetParent() == null) contents.Free();
        };
        int shownPct = -1;
        contents.Load.Changed += (c, t) =>
        {
            int pct = t > 0 ? (int)((double)c / t * 100) : 0;
            if (pct != shownPct) { shownPct = pct; World.FieldContents.CoverProgress(c, t); }
        };
        // the field enter's FUN_141739450(false) (CSceneStateGame::vfunc_1, scenestategame.cpp:2006): a closed taxi station
        // draws its DisabledAsset, so the swap comes before the build reads NifAsset; the open one's effect after it
        int taxiField = fd?.Id ?? 0;
        var taxis = World.TaxiStations.BeforeBuild(map, World.TaxiStations.IsOpen(packs, taxiField));
        await Budget();
        foreach (var _ in fb.BuildSteps(map)) await Budget();
        var fieldWorld = fb.World;
        AddChild(fieldWorld);
        AddChild(new AlphaSort(fb.AlphaEntries, fieldWorld));   // the client's alpha key on the field's blended draws
        Fade = new World.FieldFade(fb.FadeDraws, () => player?.GlobalPosition, () => camera);   // BGTransparencyMgr
        AddChild(Fade);
        Triggers = new World.FieldTriggers(map.Entities, fb.TriggerMeshDraws, fb.TriggerColliders, fb.TriggerFadeDraws) { FieldName = fieldName };
        AddChild(Triggers.Fader);
        Breakables = new World.FieldBreakables(Net.LiveSession.Instance?.Breakables ?? DevBreakables(), fb.BreakableMeshDraws, fb.BreakableFadeDraws, fb.Colliders);
        AddChild(Breakables.Fader);
        Vibrates = new World.FieldVibrateDraws(Net.LiveSession.Instance?.Vibrates, fb.VibrateMeshDraws, fb.VibrateFadeDraws, fb.VibrateColliders);
        AddChild(Vibrates.Fader);
        Interacts = new World.FieldInteractDraws(fb.InteractMeshDraws, fb.Colliders, fb.InteractReactableDraws, fb.InteractColliders) { Fade = Fade };
        Interacts.UseProps(contents.Props, fb.DrawnElsewhere, fb.ReactableElsewhere);   // the animated ones, built with the contents
        AddChild(Interacts.Fader);
        Liftables = new World.FieldLiftables(Net.LiveSession.Instance?.Liftables, fb.LiftableMeshDraws, fb.LiftableFadeDraws);
        AddChild(Liftables.Fader);
        int liftsElsewhere = 0;
        foreach (var e in fb.DrawnElsewhere) if (FieldBuilder.IsLiftable(e)) liftsElsewhere++;
        if (Liftables.Count + liftsElsewhere > 0) GD.Print($"MS2Godot lifts: {Liftables.Count} drawn, {liftsElsewhere} drawn as props (no fade)");
        contents.Props.Entities.AddRange(fb.DrawnElsewhere);
        contents.Props.NotReceiving.UnionWith(fb.NotReceivingElsewhere);
        foreach (var (e, off) in fb.LightOffElsewhere) contents.Props.LightOff[e] = off;
        AddChild(World.FieldEffects.Build(packs, builder, fb.EffectEntities, portalEffects));
        if (fb.PortalsNotFollowing > 0) GD.Print($"MS2Godot portals: {fb.PortalsNotFollowing} without an effect model, drawn by their load IsVisible");
        AddChild(World.FieldEffects.BuildParticles(builder, fb.ParticleEntities, fb.StaticParticleModels));
        if (World.FieldWeather.Create(packs, builder, fd?.MapEffectId) is { } weather) AddChild(weather);
        if (taxis.Count > 0) AddChild(World.TaxiStations.Start(packs, builder, taxis, taxiField));
        var regionSkills = new World.RegionSkills(packs, builder);   // WorldService's region skill objects (OnRegionSkill 0x4C)
        AddChild(regionSkills);
        Triggers.Bind(regionSkills);   // the effect trigger objects' effects go through the same effect service
        Vibrates.Bind(regionSkills);   // and the vibrate objects' XmlFilePath effects
        Interacts.Bind(regionSkills);  // and the interact objects' state effects
        LiftMarkers = new World.LiftTargetMarkers(Net.LiveSession.Instance?.LiftTargets, regionSkills);   // and the lift target markers
        long tBuild = sw.ElapsedMilliseconds;
        await Budget();
        GD.Print($"MS2Godot load count {contents.Load.Count} / {contents.Load.Total} entities placed");
        GD.Print($"MS2Godot field {fieldName}: {map.Entities.Count} entities, {fb.Instances} instances in {fb.MultiMeshes} multimeshes ({fb.OwnShells} own-group shells, {fb.SplitObjects} split blended objects, {fb.FrozenGroups} static meshes held at the add time, grid {fb.InstancingGrid}, {fb.NotReceivingDraws} draws without the sun shadow, {fb.NotReceivingBlended} of them blended), " +
                 $"colliders {fb.Boxes} boxes / {fb.Meshes} PhysX-mesh entities ({fb.SkippedNifMeshes} GeneratePhysX entities without their NIF meshes) / {fb.Whiteboxes} whiteboxes, {fb.Missing} unresolved models; " +
                 $"entity set {tMap} ms, build {tBuild - tMap} ms");

        BuildEnvironment(map, fieldName, hdrOn);
        await Budget();
        GD.Print($"MS2Godot lights: {FieldLights.Add(fb.Lights, Ms2.Formats.Options.GraphicGates.LightDimmerScale(hdrOn))}, {fb.LightMaskedDraws} draws masked");
        await Budget();
        Movement.MoveProof.Effects = (packs, builder);   // the scripted run's fx step
        if (SpawnsLocalPlayer) SpawnPlayer(builder, map);
        if (player != null) { Triggers.Bind(player.Climbs); player.ForceRegions = regionSkills.Objects; }   // FUN_141bc8420's zones
        await Budget();
        // From here to the end of the load, one frame: FieldContents.Start adopts the loading screen into the field, so no
        // frame is drawn between it and Loaded. The field is enabled again first, as it was throughout the one-frame build:
        // its colliders are back in the physics space for what follows (the camera's near-plane test in its first placement).
        Restore();
        AddChild(contents);
        abandon = null;
        contents.Start(map, fieldName, player, SpawnsLocalPlayer && !Net.LiveSession.Active);
        Triggers.Bind(contents.TriggerActors);   // the actor trigger objects' drawn actors
        // the portal entities' draws: live, the registered portals' shown flags (contents.Start attached them), then each change;
        // offline, the sandbox's list (its quest masks for a fresh character)
        portalDraws = new Ms2.Ui.PortalDraws((e, shown) => { if (portalEffects.TryGetValue(e, out var fx)) World.FieldEffects.Show(fx, shown); });
        if (Net.LiveSession.Active) portalDraws.Bind(Net.LiveSession.Instance?.Portals);
        else if (contents.Portals != null)
            foreach (var p in contents.Portals.All)
                if (portalEffects.TryGetValue(p.Entity, out var fx)) World.FieldEffects.Show(fx, p.Visible);
        Breakables.BindActors(contents.BreakableActors);   // the breakable actors, at their markers' states
        if (Net.LiveSession.Instance is { } lsInteract)   // the objects contents.Start attached, then each state change
        {
            Interacts.Bind(lsInteract.InteractObjects, contents.InteractActors);
            lsInteract.InteractStateDrawn = Interacts.Apply;
            lsInteract.InteractRemoved = Interacts.Remove;
            lsInteract.InteractEffect = Interacts.Effect;
        }
        devBreakables?.AttachField(map.Entities);
        DevBreakableStates();
        var hdr = new FieldHdr();
        cam = fd?.Camera ?? cam;
        hdr = fd?.Hdr ?? hdr;
        if (fd != null) BlobShadow.Strength = fd.SimpleShadow;
        if (player != null && fd != null) player.CheckClimb = fd.CheckClimb;
        zoom.Setting = cam;
        zoom.Reset();
        try
        {
            var k = Ms2.Ui.FeatureElements.Constants(packs);
            looseFinal = k.Float("CameraFinalLoose", looseFinal);
            looseNow = k.Float("CameraCurrentLoose", looseNow);
            looseUpdate = k.Float("CameraUpdateLoose", looseUpdate);
            Player.EscapeHit = Ms2.Ui.Skills.EscapeHitMethods.From(k);   // StateHit::vfunc_8's constants (statehit.cpp:184-187)
            Player.CancelSwingKeyIsDown = k.Int("CancelSwing_KeyIsDown", 0);   // StateSkill::FUN_1417ed7f0 (stateskill.cpp:1523)
        }
        catch (Exception ex) { GD.PushWarning("constants: " + ex.Message); }
        // Gamebryo's camera FOV is vertical, as Godot's (keep height). The planes are the field camera entity's
        // (MS2GameplayCamera.flat: 10 / 100000, CameraFrustum); the camera modes' 250 / 18000 are its depth of field
        var planes = CameraFrustum.Gameplay(flats);
        camera = new Camera3D { Fov = cam.FovDeg, Near = planes.Near, Far = planes.Far, Current = true, CullMask = MainCullMask };
        RasterOrigin();
        rasterSource = GetViewport();
        rasterSource.SizeChanged += RasterOrigin;
        bloom = new Ms2Bloom { Threshold = hdr.Threshold, Offset = hdr.Offset, BloomScale = hdr.BloomScale, GlowScale = hdr.GlowScale };
        camera.Compositor = new Compositor { CompositorEffects = new Godot.Collections.Array<CompositorEffect> { bloom } };
        if (OS.GetEnvironment("MS2_NOGLOW") != "1") glow = GlowPass.Attach(this, camera);   // MS2_NOGLOW=1: the bloom without the glow share (before shots)
        bloom.Glow = glow;   // set once: the render callback reads it, and with HDR off ConvertOnly returns before the read
        Active = this;
        Net.CameraShakes.Effects.Clear();   // the camera set up in this field (camera.cpp:803): the last field's shakes are gone
        SwitchPostProcess(hdrOn);
        AddChild(camera);
        listener = new AudioListener3D();
        AddChild(listener);
        listener.MakeCurrent();
        if (player != null) { UpdateCamera(); AttachMovementOptions(builder); }
        TriggerCamera = new World.TriggerCamera(this);
        Triggers.Bind(TriggerCamera);   // the camera trigger objects' steps
        OnFieldBuilt(builder, map, fieldName);
        Built?.Invoke(this, builder);
        Audio = FieldAudio.Start(this, data, packs, fieldName, map, player, player?.Anim);   // sound: game/audio
        if (Audio != null) Triggers.Bind(Audio);   // the sound trigger objects' music and ambient sounds
        // feature 0x60 is FeatureTable.Names[0x5f], "MusicConcert" (CFieldData::FUN_1403756f0)
        if (Audio != null && packs != null)
            Audio.InMusicConcert = EffectPart.IsMusicConcert(hdrFieldId(fieldName), Ms2.Ui.LobbyFeatures.IsEnabledByName(packs, Ms2.Ui.FeatureTable.Names[0x60 - 1]));
        if (Audio != null) Audio.Viewer = () => player != null && IsInstanceValid(player) && player.IsInsideTree() ? player.GlobalPosition : null;
        if (player != null && Audio?.Footsteps is { } steps)
        {
            // the ground's effect files of a step or a landing: the sound parts play in FootstepSounds, the nif parts here
            var ground = new World.GroundEffects(packs, builder);
            AddChild(ground);
            steps.EffectFile = f => ground.Play(f, player.GlobalPosition, player.Visual?.GlobalRotation.Y ?? 0);
            steps.Ground = () => player.GroundMaterial;
            player.Landed += steps.Land;
        }
        var nav = NavDebug.Start(this, packs, assets, spec, flats, fieldName, navOn);   // the navmesh; F9 / --nav: its overlay
        if (player != null) player.ValidPosition = nav.ValidPosition;
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
        EndLoad();
    }

    // ---- the field load across frames, behind the loading screen ----------------------------------------------------
    //
    // The client builds the world over several frames while the loading screen keeps drawing: LoadMap pauses the scene
    // graph's update (MS2SceneGraphService::SetPauseUpdate(1)) and requests the block (WorldService m_state 2), the
    // entities enter the world as their NIFs finish loading on the asset loader thread (each OwnedEntityEnterWorld moves
    // the loading percent), and at elr_EntityCreationCompleted (m_state 4) FUN_141bcda90 runs the field's one-shot setup
    // and resumes the update; IsLoadingComplete (FUN_141bcfbd0) is m_state 4 with m_2b4. Godot creates nodes on the main
    // thread only, so here the build gives the frame back whenever it has run BudgetMs: the same steps in the same order,
    // with the field hidden and paused (process, physics and input off, colliders out of the physics space, its canvas
    // layers hidden) at every frame boundary. The last frame enables and shows it again, runs FieldContents.Start and the
    // camera, HUD and audio setup, and raises Loaded (LoadComplete), so everything after the world build runs in one frame
    // as before. The loading screen is not in the field while it loads (FieldContents.CoverLoad), so hiding the field
    // never hides it.
    // The pause is the scene graph's only: StartProgress (CUIService FUN_141adb900) pauses no dialog (its
    // FUN_141adbcd0(0) is UISwfEntityManager::SetEnabled(false), uiswfentitymanager.cpp:794, which only resets the
    // hide-UI toggle on every movie), and CUIService::OnTick (uiservice.cpp:1894) keeps ticking every open, visible
    // dialog under the loading screen. So the HUD keeps running here too. The field's own entities are paused with the
    // field (UpdateDynamicEntities, ms2scenegraphservice.cpp:239); the old field's are freed with its scene.

    /// <summary>Main-thread time a field load takes per frame before it gives the frame back.</summary>
    const long BudgetMs = 10;
    readonly System.Diagnostics.Stopwatch frameClock = new();
    ProcessModeEnum loadProcessMode;
    bool loadVisible;
    StagedLayers layers;            // the field's canvas layers, hidden while it loads

    /// <summary>True once the field is built (the client's IsLoadingComplete); false while a field load is under way.</summary>
    public static bool LoadComplete { get; private set; }
    /// <summary>Raised once, when the whole field is built and shown.</summary>
    public event Action Loaded;
    public bool IsLoaded { get; private set; }

    void BeginLoad()
    {
        LoadComplete = false;
        began = true;
        loadProcessMode = ProcessMode;
        loadVisible = Visible;
        ProcessMode = ProcessModeEnum.Disabled;     // no process, physics or input below the field while it builds
        Visible = false;
        loadMove = World.Travel.Pending;
        coverStarted = World.FieldContents.CoverLoad(packs, this);
        staged = World.FieldContents.Covered;
        frameClock.Restart();
    }
    bool coverStarted, staged;
    Action abandon;
    World.Travel.Move loadMove;   // the move this load took (FieldContents.Start consumes it)

    /// <summary>Gives the frame back once this frame's share of the load has run; the field stays hidden meanwhile. Only
    /// behind a loading screen: a field built without one (no move: the character creation scene, a replay, a dev run)
    /// is built in one frame as before, with nothing drawn until it is whole.</summary>
    async System.Threading.Tasks.Task Budget()
    {
        if (!staged || frameClock.ElapsedMilliseconds < BudgetMs) return;
        await NextFrame();
    }

    async System.Threading.Tasks.Task NextFrame()
    {
        HideLayers();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        // the awaiter belongs to the tree, not to the field: a field freed meanwhile (its scene changed) stops its load here
        if (!IsInstanceValid(this) || !IsInsideTree()) throw new OperationCanceledException();
        frameClock.Restart();
    }

    /// <summary>A Node3D's visibility does not reach canvas layers: the field's own (the backdrop) are hidden one by one.
    /// The layers are tracked as they enter the field (StagedLayers), not searched for in the whole field every frame.</summary>
    void HideLayers() => (layers ??= new StagedLayers(this)).Hide();

    /// <summary>A field that leaves the tree while it loads (its scene changed) drops the move and the loading screen it
    /// took here, before the next scene's field starts its own load in the same scene change (its awaiter only stops it
    /// on the next frame).</summary>
    public override void _ExitTree()
    {
        if (Active == this) { Active = null; Net.CameraShakes.Effects.Clear(); }   // CameraMgr::FUN_140aead90 (camera.cpp:853-866)
        Breakables?.Detach();
        Vibrates?.Detach();
        if (Interacts != null)
        {
            if (Net.LiveSession.Instance is { } ls && ls.InteractStateDrawn == Interacts.Apply) { ls.InteractStateDrawn = null; ls.InteractRemoved = null; ls.InteractEffect = null; }
            Interacts.Detach();
        }
        Liftables?.Detach();
        LiftMarkers?.Detach();
        portalDraws?.Detach();
        FieldLights.Clear(lightSet);
        if (began && !IsLoaded) World.FieldContents.AbandonLoad(this, loadMove);
        layers?.Stop();
        FaceGrids.Clear();  // a freed body's RID may be reused by the next field's
        ColliderGroups.Clear();   // one field's colliders at a time
        models.Dispose();   // the models stay cached for the next field until the sweep finds them unused (NifCache)
        // the root viewport outlives the field: a size change during the scene change (the HUD scene restoring the window's
        // content scale mode as it leaves) must not reach a field that is no longer in the tree
        if (rasterSource != null) { rasterSource.SizeChanged -= RasterOrigin; rasterSource = null; }
    }

    FieldLightSet lightSet;         // this field's lights (FieldLights.Add), dropped when it leaves
    Viewport rasterSource;          // the viewport whose size_changed moves the projection window, while connected
    readonly NifCache.Lease models = new();   // the field's hold on the cached models it built from (NifCache)
    /// <summary>The world-space faces and grids of this field's mesh shapes, shared by the player's and every remote PC's
    /// controller (NxController.FaceGrids), dropped when the field leaves the tree.</summary>
    public readonly Movement.NxController.FaceGrids FaceGrids = new();

    bool began, restored;
    void Restore()
    {
        if (!began || restored) return;
        restored = true;
        layers?.Restore();
        layers = null;
        Visible = loadVisible;
        ProcessMode = loadProcessMode;
    }

    void EndLoad()
    {
        Restore();
        IsLoaded = true;
        LoadComplete = true;
        Loaded?.Invoke();
    }

    // Hooks for scenes that build on the field (the replay scene in net/): the field to load, whether the walkable player
    // exists, and a call once the field, environment and camera are in place.
    protected virtual string FieldNameFor(string defaultName) => defaultName;
    protected virtual bool SpawnsLocalPlayer => true;
    protected virtual void OnFieldBuilt(ModelBuilder builder, FieldMap map, string fieldName) { }
    /// <summary>Raised after the field is built (live mode adds its remote players and npcs here).</summary>
    public static event Action<Field, ModelBuilder> Built;
    public PackSet Packs => packs;
    /// <summary>The field's cube map, the player's CubeGrid (null before the player is made).</summary>
    public Movement.CubeGrid Cubes => player?.Cubes;

    protected FieldAudio Audio;
    protected World.QuestGuide Quest;

    int hdrFieldId(string fieldName) { try { return FieldData.FindByXBlock(packs, fieldName)?.Id ?? 0; } catch { return 0; } }

    /// <summary>The HDR switch of SetSoftParticlesOption @0x1409a4c60 (applyeffect.cpp:473-494), run after the field load by
    /// ApplyEffect::Initialize (applyeffect.cpp:727-735, from worldservice_1.cpp:6696): BloomRenderStep (step 3) on or off, and
    /// GlowRenderClick (click 8) on only with step 3 active. Off, the frame is only converted out (Ms2Bloom.ConvertOnly) and
    /// the glow target is not drawn. STILL-OPEN: the soft-particle shader constants it also sets (soft depth is not drawn).</summary>
    void SwitchPostProcess(bool hdrOn)
    {
        if (bloom != null) bloom.ConvertOnly = !hdrOn;
        glow?.SetActive(hdrOn);
    }

    /// <summary>SetHdr @0x14125e300 (gameoptionstruct.cpp:2081-2090) on the running field: the post-processing switch
    /// (SwitchPostProcess), then MS2LightService::ScaleLightIntensity @0x1414c0820 (ms2lightservice.cpp:395-403), every
    /// light's stored Dimmer times 1.0 (HDR on) or 1.1. <paramref name="g"/> is the option being stored (the dialog's state).
    /// SetHdr tests the field's raw m_bHdr where this calls IsHdrEnabled; the two agree while FieldHdr.Enabled is always true
    /// (fielddatacli.cpp:75 and :250).</summary>
    public void ApplyHdr(Ms2.Formats.Options.GameOption.Graphic g)
    {
        bool hdrOn = Ms2.Formats.Options.GraphicGates.IsHdrEnabled(fieldData, g);
        SwitchPostProcess(hdrOn);
        float k = Ms2.Formats.Options.GraphicGates.LightDimmerScale(hdrOn);
        SetSunAndAmbient(k);
        FieldLights.Rescale(k);
    }

    /// <summary>The ambient light's AmbientColor (entity property 0xb086f4a8), which the field property of kind 8 sets
    /// (FieldPropertyAmbientLightCli::vfunc_4, fieldpropertycli.cpp:202; the fade, WorldService::UpdateAmbientLight,
    /// worldservice_1.cpp:4362). The Dimmer stays the field's own.</summary>
    System.Numerics.Vector3 Ms2.Ui.Hud.IFieldLights.Ambient
    {
        get => new(ambientColor.R, ambientColor.G, ambientColor.B);
        set
        {
            ambientColor = new Color(value.X, value.Y, value.Z);
            if (environment != null) environment.AmbientLightColor = ambientColor;
            SetSunAndAmbient(lightScale);
        }
    }

    /// <summary>The directional light's DiffuseColor (0x2d8cf386); setting it sets its SpecularColor (0xc3f0329e) too, as the field
    /// property of kind 9 does (fieldpropertycli.cpp:222-237, UpdateDirectionalLight, worldservice_1.cpp:4396). Its AmbientColor
    /// is untouched.</summary>
    System.Numerics.Vector3 Ms2.Ui.Hud.IFieldLights.Sun
    {
        get => new(sunColor.R, sunColor.G, sunColor.B);
        set
        {
            sunColor = sunSpecular = new Color(value.X, value.Y, value.Z);
            if (sunLight != null) sunLight.LightColor = sunColor;
            SetSunAndAmbient(lightScale);
        }
    }

    /// <summary>The ambient and directional lights' Dimmer, times the HDR light scale <paramref name="k"/>.</summary>
    void SetSunAndAmbient(float k)
    {
        lightScale = k;
        float ambient = baseAmbient * k, sunDim = baseSunDim * k;
        if (environment != null) environment.AmbientLightEnergy = ambient;
        if (sunLight != null) sunLight.LightEnergy = sunDim;
        Vector3 Rgb(Color c, float m) => new(c.R * m, c.G * m, c.B * m);
        RenderingServer.GlobalShaderParameterSet("ms2_ambient", Rgb(ambientColor, ambient));
        RenderingServer.GlobalShaderParameterSet("ms2_sun_color", Rgb(sunColor, sunDim));
        RenderingServer.GlobalShaderParameterSet("ms2_sun_ambient", Rgb(sunAmbient, sunDim));
        RenderingServer.GlobalShaderParameterSet("ms2_sun_specular", Rgb(sunSpecular, sunDim));
    }

    void BuildEnvironment(FieldMap map, string fieldName, bool hdrOn)
    {
        Color fog = new(199 / 255f, 237 / 255f, 1f);
        float fogNear = 5276, fogFar = 20000;
        XDocument x = null;
        try
        {
            x = MapXBlock.Load(packs, fieldName);
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

        loadedFog = null; loadedFogApplied = (fogNear, fogFar, fog); loadedHeightFog = null;
        // mapxblock heightfog: colour, upper, lower, percentage (a standard height blend in field_lit)
        RenderingServer.GlobalShaderParameterSet("ms2_hfog", hfogShown = Vector3.Zero);
        if (x?.Root?.Element("heightfog") is { } hfog)
        {
            try
            {
                var hc = ((string)hfog.Attribute("color")).Split(',').Select(s => float.Parse(s, CultureInfo.InvariantCulture) / 255f).ToArray();
                float up = float.Parse((string)hfog.Attribute("upper"), CultureInfo.InvariantCulture);
                float lo = float.Parse((string)hfog.Attribute("lower"), CultureInfo.InvariantCulture);
                float pct = float.Parse((string)hfog.Attribute("percentage"), CultureInfo.InvariantCulture) / 100f;
                RenderingServer.GlobalShaderParameterSet("ms2_hfog_color", hfogColorShown = new Vector3(hc[0], hc[1], hc[2]));
                RenderingServer.GlobalShaderParameterSet("ms2_hfog", hfogShown = new Vector3(up, lo, Mathf.Clamp(pct, 0f, 1f)));
                loadedHeightFog = (new Color(hc[0], hc[1], hc[2]), up, lo, pct * 100f);
            }
            catch (Exception ex) { GD.PushWarning("heightfog: " + ex.Message); }
        }

        float ambient = 0.8f, sunDim = 0.8f;
        ambientColor = Colors.White; sunColor = Colors.White; sunAmbient = Colors.Black; sunSpecular = Colors.White;
        var sunDir = new Vec3(0, 0, -1);
        bool sunCasts = false, sunBackfaces = false;
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
                sunCasts = e.Bool("CastShadows", true);
                sunBackfaces = e.Bool("RenderBackfaces", false);
                sunDir = e.Transform.Apply(new Vec3(1, 0, 0)) - e.Transform.Apply(new Vec3(0, 0, 0));   // Gamebryo lights shine along +X
            }
        }

        // The client's depth fog for field_lit and character_lit (ms2_fog.gdshaderinc): the colour as its gamma value, and the
        // bias the camera distance, set each frame by UpdateCamera; a view without a target camera mode (the lobby) keeps the
        // CCameraMode default m_50 = 1200 (cameramode.cpp:284, :297). The environment fog below serves the other materials.
        RenderingServer.GlobalShaderParameterSet("ms2_fog_color", fogColorShown = new Vector3(fog.R, fog.G, fog.B));
        fogParams = new Vector4(fogNear, fogFar, DefaultFogBias, 0);
        RenderingServer.GlobalShaderParameterSet("ms2_fog", fogParams);
        // The colour buffer holds gamma values (field_lit.gdshader) and Godot turns an environment colour from sRGB to
        // linear, so the background and fog colours are passed through LinearToSrgb: Godot's value is the client's
        // gamma value, and the depth fog mixes in gamma space as the client's does.
        // Each light's Dimmer as it enters the world: times 1.1 unless HDR is on (HandleEntityEnterWorld @0x1414c08e0,
        // ms2lightservice.cpp:407-433; GraphicGates.LightDimmerScale). STILL-OPEN: the client tests the bloom step's active
        // flag as the light enters, and on the first field the lights may enter before ApplyEffect::Initialize turns that
        // step on; here the scale follows the switch as Initialize leaves it.
        baseAmbient = ambient; baseSunDim = sunDim;
        float k = Ms2.Formats.Options.GraphicGates.LightDimmerScale(hdrOn);
        ambient *= k; sunDim *= k;
        var env = environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = fog.LinearToSrgb(),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = ambientColor,
            AmbientLightEnergy = ambient,
            FogEnabled = true,
            FogMode = Godot.Environment.FogModeEnum.Depth,
            FogLightColor = fog.LinearToSrgb(),
            FogDepthBegin = fogNear,
            FogDepthEnd = fogFar,
            FogDensity = 1f,
            TonemapMode = Godot.Environment.ToneMapper.Linear,
        };
        AddChild(new WorldEnvironment { Environment = env });
        backdrop = FieldBackdrop.Add(this, env, packs, x, fieldData);
        // The field shader draws the sun itself (ms2_sun_*); from this light it reads only the shadow, which scales the
        // shader's own sun term (field_lit.gdshader light()), so the sun is counted once. Its shadow is the light's
        // CastShadows, forced off below the mapObject shadow level (MS2LightService::UpdateLightShadow, ms2lightservice.cpp:356-385)
        // The casters' faces follow the light entity's RenderBackfaces (ShadowGenerator mixin; the MS2DirectionalLight flat
        // default is False): MS2LightService::CreateQueuedLights (ms2lightservice.cpp:561) calls the SDK
        // LightService::UpdateShadowCaster (@0x14202b2b0, ecr/LightService.cpp:1072-1075), which passes it to
        // NiShadowGenerator::SetRenderBackfaces, overriding the generator constructor's true (NiShadowGenerator.cpp:56, 99).
        // NiShadowRenderClick hands it to NiDX9ShadowWriteShader (NiShadowRenderClick.cpp:32-33), which flips each draw
        // mode's cull only when it is set (NiDX9ShadowWriteShader.cpp:19-31, 56-67). ShadowReverseCullFace is that flip.
        // The follow camera's sun shadow is one map, not camera-fitted splits: HandleCameraFollowDirectionalLight
        // (@0x141509e60, ms2shadowclickgenerator.cpp:597-674) builds a fixed orthographic box (half size 2000, near 1,
        // far 25000) around the camera target, pushed 1500 along the view. It does not change with zoom. Godot's default
        // PSSM split fit follows the camera and dropped the player's and npcs' shadows near the feet when zoomed in, so the
        // sun uses one orthogonal map over the same 4000 range. STILL-OPEN: the box is camera-fitted, not target-centred
        // as in the client; Godot exposes no custom directional shadow projection.
        // The edge filter: the light flat (MS2DirectionalLight_Outside_ preset, ms2directionallight.flat) sets ShadowTechnique
        // NiPCFShadowTechnique and SizeHint 2048 over the 4000 box, 1.95 units a texel. That technique's PCFShadowMap fragment
        // (NiShadowManager.cpp:180-202; StandardMaterialNodeLibraryFragments.xml:2049) is a 4x4 tap grid weighted
        // (1-frac, 1, 1, frac) per axis, a 3-texel box: a smooth linear ramp about 5.9 units wide, with no noise.
        // Godot's orthogonal map is not that map: it is the whole directional atlas (4096) fitted to the bounding sphere of the
        // camera's view slice, so its texel size follows the field camera's fov and distance (about 1.5 units at fov 40), and
        // the ramp width is not matched to the client's in units. What was measured, against our own shadow-off render at
        // 2560x1440 and camera distance 600: the default Soft Low filter (4 rotated samples per pixel) has a 10-90% edge of
        // 8 px with a grainy dither the client's PCF does not have; Soft High (16 samples) with blur 2/3 keeps the 8 px edge
        // and halves the high-frequency residual (5.0 to 2.5). STILL-OPEN: derive the blur from the map's texel size.
        // The filter quality is global (RenderingServer), so it is set once for every scene in project.godot
        // (rendering/lights_and_shadows/directional_shadow/soft_shadow_filter_quality = 4, Soft High), not here.
        var sun = sunLight = new DirectionalLight3D { LightColor = sunColor, LightEnergy = sunDim, ShadowEnabled = sunCasts && FieldShadows.MapObjects, DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal, DirectionalShadowMaxDistance = 4000, ShadowBlur = 2f / 3f, ShadowReverseCullFace = sunBackfaces };
        AddChild(sun);
        var d = FieldBuilder.ToGodotPoint(sunDir).Normalized();
        sun.LookAtFromPosition(Vector3.Zero, d, Mathf.Abs(d.Y) > 0.99f ? Vector3.Forward : Vector3.Up);
        SetSunAndAmbient(k);
        RenderingServer.GlobalShaderParameterSet("ms2_sun_dir", d);
    }

    void SpawnPlayer(ModelBuilder builder, FieldMap map)
    {
        var (visual, anim, md, bodyDir) = Look.PlayerLook.Build(builder, packs);

        player = Player.Create(visual, anim, FaceGrids);
        player.Gender = bodyDir == "male" ? 0 : 1;
        player.RootName = md.RootName;
        player.Weapons = Net.EquipApplier.RigOf(visual);
        player.SetFace(Look.FaceTexture.For(visual, builder, packs, player.Gender));
        BlobShadow.Load(packs);
        // the player's CObjectShadow, of the myPC row (MyPC::vfunc_49, mypc.cpp:2879-2887)
        FieldShadows.Character(player, visual, Ms2.Formats.Options.GraphicOptionTable.MyPC, 100f, 3f);
        player.Anims = Movement.ActionAnims.Load(packs, builder, md, anim, bodyDir);
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--battle") >= 0 || Net.LiveSession.Instance?.SelfInBattle == true)
            player.SetBattleStance(true);
        player.Climbs = Movement.Climbables.From(map);
        player.Cubes = Movement.CubeGrid.From(map);
        player.Cubes.Cells.InstallObjectOf = Net.LiveSession.InstallObjectOf;
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
        // --effect-light <effect xml>: starts the light parts of that effect at the spawn spot, for a before/after look at an effect light
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i] == "--effect-light")
                foreach (var part in new EffectTable(packs).Get(args[i + 1]) ?? new List<EffectPart>())
                    if (part.Light != null)
                        GD.Print("MS2Godot effect light " + (FieldLights.Start(part.Light, new Vec3(pos.X + part.Translate[0], pos.Y + part.Translate[1], pos.Z + part.Translate[2])) != null ? "started" : "not started"));
        player.SpawnPoint = FieldBuilder.ToGodotPoint(pos) + new Vector3(0, 5, 0);
        // WorldService's world box: reset inverted (worldservice_1.cpp:10125-10126), then grown by every MS2Bounding entity's
        // position (worldservice_1.cpp:10394-10405); a field without one keeps the inverted box
        var bounds = map.Entities.Where(e => e.ModelName == "MS2Bounding" || e.HasMixin("MS2Bounding")).Select(e => e.Position).ToList();
        player.BoundsMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        player.BoundsMax = new Vector3(-float.MaxValue, -float.MaxValue, -float.MaxValue);
        foreach (var b in bounds)
        {
            player.BoundsMin = player.BoundsMin.Min(new Vector3((float)b.X, (float)b.Y, (float)b.Z));
            player.BoundsMax = player.BoundsMax.Max(new Vector3((float)b.X, (float)b.Y, (float)b.Z));
        }
        // the server's bounding prism (kms2525 MapEntityMapper.cs:162-174, MapEntityStorage.cs:105-113): the first two
        // MS2Bounding entities in the xblock's order span x/y; base = the lower z, height = the z distance; a third is ignored
        if (bounds.Count >= 2)
        {
            Vec3 a = bounds[0], b = bounds[1];
            player.PrismMin = new Vector3((float)Math.Min(a.X, b.X), (float)Math.Min(a.Y, b.Y), (float)Math.Min(a.Z, b.Z));
            player.PrismMax = new Vector3((float)Math.Max(a.X, b.X), (float)Math.Max(a.Y, b.Y), (float)Math.Min(a.Z, b.Z) + (float)Math.Abs(b.Z - a.Z));
        }
        AddChild(player);
        player.GlobalPosition = player.SpawnPoint;
        // spawn facing: the model's forward is MS2 -Y, turned by the spawn point's Z rotation
        double yaw = (sp?.RotationDeg.Z ?? 0) * Math.PI / 180;
        var face = FieldBuilder.ToGodotPoint(new Vec3(Math.Sin(yaw), -Math.Cos(yaw), 0));
        player.Face(face);
        GD.Print($"spawn {sp?.Name} ms2 {pos} -> godot {player.SpawnPoint}");
    }

    Movement.MouseMove mouseMove;

    /// <summary>CMouseController::SetEnabled (gameoptionstruct.cpp:1321-1328): toggles click-to-move on the active field.</summary>
    public void SetMouseControl(bool enabled)
    {
        if (player == null || camera == null) return;
        if (enabled)
        {
            if (mouseMove != null && IsInstanceValid(mouseMove)) return;
            AddChild(mouseMove = new Movement.MouseMove(player, camera) { Name = "MouseMove" });
        }
        else if (mouseMove != null && IsInstanceValid(mouseMove))
        {
            player.MouseInput = Vector2.Zero;
            mouseMove.QueueFree();
            mouseMove = null;
        }
    }

    /// <summary>
    /// Command line movement options, all off by default as in the client: --mouse-move (interface/EnableMouseControl),
    /// --gamepad (gamepad/bGamePadUse), --ride RIDING_ID (get on a mount, as a RideOnAction would).
    /// </summary>
    void AttachMovementOptions(ModelBuilder builder)
    {
        bool enableMouse = GameOptions.MouseControl.Value;
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--mouse-move") enableMouse = true;
            if (args[i] == "--gamepad") Movement.MoveInput.GamePad = true;
            if (args[i] == "--ride" && i + 1 < args.Length && int.TryParse(args[i + 1], out int ride))
            {
                try { player.RideOn(Mount.Load(packs, builder, ride)); GD.Print($"riding {ride}"); }
                catch (Exception ex) { GD.PushWarning($"ride {ride}: {ex.Message}"); }
            }
        }
        if (enableMouse) SetMouseControl(true);
    }

    /// <summary>The cull mask bit of a camera that draws the client's depth and height fog (ms2_fog.gdshaderinc FOG_LAYER):
    /// a layer no instance uses, so it only marks the camera; the UI character views leave it out.</summary>
    public const uint FogLayer = 1u << 20;
    /// <summary>The field camera's cull mask: every render layer but the glow layer, and the fog bit.</summary>
    public const uint MainCullMask = GlowPass.MainMask | FogLayer;
    /// <summary>CCameraMode's distance before any mode sets it (cameramode.cpp:284, :297), the fog bias of a view without one.</summary>
    const float DefaultFogBias = 1200f;

    /// <summary>CameraMgr::Update (camera.cpp:828-829): the fog bias is the camera mode's distance (m_50) every frame.</summary>
    void SetFogBias(float bias)
    {
        if (fogParams.Z == bias) return;
        fogParams.Z = bias;
        RenderingServer.GlobalShaderParameterSet("ms2_fog", fogParams);
    }
    Vector4 fogParams;   // ms2_fog: near, far, bias, off (1: ApplyEffect::SetFogRange(false, ...))
    Vector3 fogColorShown, hfogShown, hfogColorShown;   // the other fog globals as last set (the game cannot read them back)

    /// <summary>The fog globals as last set, for the live checks: near, far and off of ms2_fog (its bias follows the camera), its
    /// colour, ms2_hfog and its colour.</summary>
    public string FogShown => $"{fogParams.X},{fogParams.Y},{fogParams.W} {fogColorShown} {hfogShown} {hfogColorShown}";

    /// <summary>The fog this field's load applied, for the telescope's skip to put back (CInteractTelescopeObject::OnCutSceneSkip
    /// sets the field's CFieldDataCli fog again, interactobject.cpp:1016-1022): the depth fog's near, far and colour and the
    /// height fog's colour, heights and percentage, exactly as the load set them, so the skip shows what the load showed.
    /// The load always turns the depth fog on (a mapxblock without a fog element gets the defaults above), where the client's
    /// m_bFog is false for such a block: STILL-OPEN, the load's own fog for such a field is not the client's.
    /// Built when asked (the telescope's skip), so the field load does no more work.</summary>
    public Ms2.Ui.FieldFog LoadedFog
    {
        get
        {
            if (loadedFog != null) return loadedFog;
            var (near, far, c) = loadedFogApplied;
            var f = new Ms2.Ui.FieldFog { Fog = true, FogNear = near, FogFar = far, FogColor = Packed(c) };
            if (loadedHeightFog is var (hc, up, lo, pct))
            {
                f.HeightFog = true; f.HeightFogColor = Packed(hc);
                f.HeightFogUpper = up; f.HeightFogLower = lo; f.HeightFogPercentage = pct;
            }
            return loadedFog = f;
        }
    }
    static uint Packed(Color c) => 0xff000000u | (uint)(c.R8 << 16 | c.G8 << 8 | c.B8);
    Ms2.Ui.FieldFog loadedFog;
    (float Near, float Far, Color Color) loadedFogApplied;
    (Color Color, float Upper, float Lower, float Percentage)? loadedHeightFog;

    /// <summary>ApplyEffect::SetFogRange (@0x1409a4450, applyeffect.cpp:586-622): the depth fog on or off (NiFogProperty
    /// SetFog), its near and far, and its colour (0xAARRGGBB; the rgb is the gamma value, as the mapxblock's). The field_lit
    /// family's fog (ms2_fog, its w the off switch) and the environment fog of the other materials both follow.</summary>
    public void SetFogRange(bool on, float near, float far, uint color)
    {
        var c = new Color(((color >> 16) & 0xff) / 255f, ((color >> 8) & 0xff) / 255f, (color & 0xff) / 255f);
        RenderingServer.GlobalShaderParameterSet("ms2_fog_color", fogColorShown = new Vector3(c.R, c.G, c.B));
        fogParams = new Vector4(near, far, fogParams.Z, on ? 0f : 1f);
        RenderingServer.GlobalShaderParameterSet("ms2_fog", fogParams);
        if (environment != null)
        {
            environment.FogEnabled = on;
            environment.FogLightColor = c.LinearToSrgb();
            environment.FogDepthBegin = near;
            environment.FogDepthEnd = far;
        }
    }

    /// <summary>ApplyEffect::SetFogEffect (@0x1409a46a0, applyeffect.cpp:625): the height fog's colour, upper and lower heights
    /// and percentage, as the load sets ms2_hfog; off is no height fog here (density 0), as a mapxblock without one.
    /// STILL-OPEN: the client's off sets the upper height to -10000 and keeps the rest; field_lit's blend has no such case.</summary>
    public void SetFogEffect(bool on, uint color, float upper, float lower, float percentage)
    {
        if (!on) { RenderingServer.GlobalShaderParameterSet("ms2_hfog", hfogShown = Vector3.Zero); return; }
        RenderingServer.GlobalShaderParameterSet("ms2_hfog_color",
            hfogColorShown = new Vector3(((color >> 16) & 0xff) / 255f, ((color >> 8) & 0xff) / 255f, (color & 0xff) / 255f));
        var (u, l) = Ms2.Ui.FieldFog.BlendRange(upper, lower);
        RenderingServer.GlobalShaderParameterSet("ms2_hfog", hfogShown = new Vector3(u, l, Mathf.Clamp(percentage / 100f, 0f, 1f)));
    }

    /// <summary>CTargetCameraMode::vfunc_8: the distance, coast and passes run in <see cref="TargetCameraZoom"/>; each pass shows
    /// the camera through <see cref="ITargetCameraView.Show"/>.</summary>
    void UpdateCamera(double dt = 0)
    {
        if (player == null || !player.IsInsideTree()) return;   // a player that failed to build never entered the tree
        var ms2yaw = Mathf.DegToRad(cam.HAngleDeg + 180f) + camYaw;
        CameraYaw = ms2yaw;
        var p = player.GlobalPosition;
        zoom.Update(new System.Numerics.Vector3(p.X, p.Y, p.Z), ms2yaw, (float)dt, zoomInput, this);
        zoomInput = 0;
        SetFogBias(zoom.Distance);
        Shake();
    }

    /// <summary>
    /// The camera effects on the follow camera (<see cref="Net.CameraShakes.Apply"/>): CTargetCameraMode::vfunc_3 takes the
    /// moved eye and clears m_f0, so the next vfunc_39 takes the placed camera at once instead of easing to it
    /// (<see cref="shakeSnap"/>).
    /// </summary>
    void Shake()
    {
        if (Net.CameraShakes.Apply(camera, Net.CameraShakes.Now)) shakeSnap = true;
    }

    /// <summary>The last frame's shake moved the eye (m_f0 0): the shown camera takes the placed one.</summary>
    bool shakeSnap;

    /// <summary>One pass's shown camera (vfunc_39): the player turns with the placed camera, the sync record's camera field is
    /// set, and the shown camera eases to the placed one; returns whether its near plane is blocked.</summary>
    bool ITargetCameraView.Show(System.Numerics.Vector3 placed, System.Numerics.Vector3 at, System.Numerics.Vector3 offset, float dt)
    {
        player.CameraYaw = Mathf.Atan2(offset.X, offset.Z);   // movement is camera relative
        var look = new Vector3(at.X, at.Y, at.Z);
        var e = new Vector3(placed.X, placed.Y, placed.Z);
        {   // the sync record's camera field: FUN_140afdd40 over the placed eye and look, in MS2 axes (x, -z, y)
            var (ex, ey, ez) = Ms2.Net.CameraMath.Euler(e.X, -e.Z, e.Y, look.X, -look.Z, look.Y);
            player.CameraEuler = new Vector3(ex, ey, ez);
            if (!eulerLogged) { eulerLogged = true; GD.Print($"field: camera hAngle {cam.HAngleDeg} vAngle {cam.VAngleDeg} eye {e} look {look} -> CameraEuler ({ex:F2}, {ey:F2}, {ez:F2})"); }
        }
        var ideal = Transform3D.Identity.LookingAt(look - e, Vector3.Up);
        ideal.Origin = e;
        var shown = EaseCamera(ideal, dt);
        camera.GlobalTransform = shown;
        return NearPlaneBlocked(shown, look);
    }

    bool eulerLogged;
    int zoomInput;
    /// <summary>A wheel notch, as the mouse handler sets it (+1 in, -1 out), for scripted runs (MoveProof wheel).</summary>
    public void Wheel(int notch) => zoomInput = notch;
    /// <summary>The zoom distance (m_50), for scripted runs (MoveProof cam).</summary>
    public float CameraDistance => zoom.Distance;
    /// <summary>CCameraMode::vfunc_17 (m_54): the field camera's farthest zoom, for the popup menu's height offset.</summary>
    public float CameraFarthest => zoom.Setting.Distance;
    /// <summary>
    /// Scripted runs (MoveProof view): the zoom distance (m_50, clamped to MinCameraDistance .. farthest + player height),
    /// the angle from the up axis in degrees as Ctrl + up/down sets it (null: the distance's own), and the Ctrl + left/right
    /// turn in degrees. Any zoom coast stops; the shown camera then eases to it as after a wheel notch.
    /// </summary>
    public void SetCameraView(float dist, float? pitchDeg, float? turnDeg)
    {
        zoom.SetView(dist, pitchDeg is float d ? Mathf.DegToRad(d) : null);
        if (turnDeg is float t) camYaw += Mathf.DegToRad(t);
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
        if (shakeSnap) { shakeSnap = false; return (shownCam = placed).Value; }
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
        // vfunc_39 closes t = dt * distance * loose (+ the blend) of the gap each frame: at the client's 60 fps that is
        // t60 = distance * loose / 60 + blend a frame, taken here as the same share per second at any frame rate
        float t60 = dist * looseNow / FrameRate.Reference + blend;
        if (t60 > 1f) return (shownCam = placed).Value;
        float t = FrameRate.Ease(t60, dt);
        var view = (shown.Basis.Z + t * (placed.Basis.Z - shown.Basis.Z)).Normalized();
        var up = (shown.Basis.Y + t * (placed.Basis.Y - shown.Basis.Y)).Normalized();
        var side = up.Cross(view).Normalized();
        up = view.Cross(side).Normalized();
        return (shownCam = new Transform3D(new Basis(side, up, view), shown.Origin + t * gap)).Value;
    }

    /// <summary>The camera manager the camera trigger objects drive; while its mode is not the follow one it places the camera.</summary>
    public World.TriggerCamera TriggerCamera { get; private set; }
    bool triggerCameraShown;   // the trigger camera placed the camera last frame

    /// <summary>The follow mode's setting as CameraMgr reads it: the camera's fov, the CCameraMode near and far
    /// (cameramode.cpp:305-307), the placed eye and the camera's euler, in MS2 axes.</summary>
    World.CameraSetting World.ITargetCamera.Setting => new()
    {
        Fov = cam.FovDeg, DofNear = 250, DofFar = 18000,
        Position = zoom.PlacedEye is { } e ? new Vector3(e.X, -e.Z, e.Y) : default,
        Rotation = player?.CameraEuler ?? default,
    };

    /// <summary>CTargetCameraMode m_6c: the camera's angle from the up axis in radians, as last placed.</summary>
    public float CameraPitch => zoom.PlacedPitch;
    /// <summary>The camera mode's position (m_setting.m_c = m_c0.m_pos): the placed eye before the shown camera's easing.</summary>
    public Vector3? CameraEye => zoom.PlacedEye is { } e ? new Vector3(e.X, e.Y, e.Z) : null;
    /// <summary>CTargetCameraMode m_68: the camera's yaw in radians (hangle + 180 plus the Ctrl+arrow turn); the eye sits at
    /// (d sin yaw, d cos yaw) from the target in MS2 axes.</summary>
    public float CameraYaw { get; private set; }

    /// <summary>
    /// The client draws the world with D3D9 (NiDX9Renderer::Do_SetCameraData: CameraService's symmetric frustum, vertical FOV,
    /// aspect W/H, no half-pixel term), and D3D9 puts pixel centres on whole coordinates, where Vulkan puts them at +0.5: the
    /// client's world lands half a pixel right and down of the same projection here. The same frustum, as a Frustum
    /// projection whose window is moved half a pixel left and up, reproduces that raster. Fov stays the client's value, and
    /// the client-side uses of the frustum (NearPlaneBlocked) keep the symmetric one. The window follows the camera's
    /// current Near and Fov: a scene that changes either after the field camera is made calls this again.
    /// </summary>
    protected void RasterOrigin()
    {
        float h = GetViewport().GetTexture().GetHeight();
        if (h <= 0) return;
        float size = 2f * camera.Near * Mathf.Tan(Mathf.DegToRad(camera.Fov) * 0.5f);   // the near-plane height (keep height)
        float px = size / h;
        camera.Projection = Camera3D.ProjectionType.Frustum;
        camera.Size = size;
        camera.FrustumOffset = new Vector2(-0.5f * px, 0.5f * px);
    }

    /// <summary>The half width and half height of the near plane the blocked check casts from: CTargetCameraMode::vfunc_38
    /// (targetcameramode.cpp:732-736) takes the frustum's near plane and aspect with tan of the whole FOV, not its half,
    /// so its points lie outside the drawn near plane.</summary>
    public Vector2 BlockedCheckHalfExtents()
    {
        float near = camera.Near, tan = Mathf.Tan(Mathf.DegToRad(camera.Fov));
        float asp = GetViewport().GetVisibleRect().Size.Aspect();
        return new Vector2(near * tan * asp, near * tan);
    }

    /// <summary>The client's nine points of the near plane (corners, edge midpoints between them) each cast to the target
    /// and back; a hit both ways, or a hit from the target side alone, means the view is blocked.</summary>
    bool NearPlaneBlocked(Transform3D pose, Vector3 look)
    {
        var eye = pose.Origin;
        var fwd = -pose.Basis.Z.Normalized();
        var right = pose.Basis.X.Normalized();
        var up = pose.Basis.Y.Normalized();
        float near = camera.Near;
        var half = BlockedCheckHalfExtents();
        var c = eye + fwd * near;
        Vector3 hx = right * half.X, hy = up * half.Y;
        var p = new Vector3[9];
        p[0] = c - hx + hy; p[1] = c + hx + hy; p[2] = c - hx - hy; p[3] = c + hx - hy;
        p[4] = (p[0] + p[3]) * 0.5f; p[5] = (p[0] + p[1]) * 0.5f; p[6] = (p[0] + p[2]) * 0.5f;
        p[7] = (p[1] + p[3]) * 0.5f; p[8] = (p[2] + p[3]) * 0.5f;
        var space = GetWorld3D().DirectSpaceState;
        var skip = player.GetRid();
        foreach (var from in p)
        {
            using var hit = space.IntersectRay(occRay.Set(from, look, skip));
            if (hit.Count == 0)
            {
                using var behind = space.IntersectRay(occRay.Set(look, from, skip));
                if (behind.Count > 0) return true;
            }
            else
            {
                // the back cast from the hit runs the whole corner-to-target length, past the corner
                // (targetcameramode.cpp:768-772, homecameramode.cpp:622-626)
                var at = (Vector3)hit[PhysicsKeys.Position];
                var dir = (from - look).Normalized();
                var to = at + dir * from.DistanceTo(look);
                var back = space.IntersectRay(occRay.Set(at, to, skip));
                try
                {
                    // The back cast starts on the face the front ray hit, and Godot reports that shape again at distance 0 (and
                    // a grid neighbour sharing the face too). PhysX ignores a box whose ray starts inside it, on the face included
                    // ("Rays starting from inside the box are ignored", NxIntersectionSegmentBox.h:81); the restored client's
                    // view at such a spot does not change either. So a hit at the start point does not count, whatever shape it
                    // is: the cast goes on from just past it, and any shape behind still blocks.
                    if (back.Count > 0 && ((Vector3)back[PhysicsKeys.Position]).DistanceTo(at) < StartEps)
                    {
                        back.Dispose();
                        back = space.IntersectRay(occRay.Set(at + dir * StartEps, to, skip));
                    }
                    if (back.Count > 0) return true;
                }
                finally { back.Dispose(); }
            }
        }
        return false;
    }

    const float StartEps = 0.01f;

    public override void _UnhandledInput(InputEvent e)
    {
        if (Ms2.Ui.FieldMoveCover.Up) return;       // no field under a move's loading screen
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
        Ui.PerfLog.Frame("field");                 // MS2_PERF: frame time and draw calls also without the HUD
        if (player != null)
        {
            // CTargetCameraMode::vfunc_9 (@0x14183b270, targetcameramode.cpp:310) reads Ctrl and the arrows with
            // CInputKeyHandler::IsKeyDown(key, bDebugOnly = true, 0), and IsKeyDown (@0x14116d950) returns false for a
            // debug-only key while IsDebugMode is off, so the camera turns only in debug mode. It does not test the text-field
            // mask (IsKeyDownGated only), so a focused text field does not stop it there either.
            if (Ms2.Ui.FieldMoveCover.TakesInput && Ms2.Ui.InputKeyHandler.IsKeyDown(Input.IsKeyPressed(Key.Ctrl), debugOnly: true))   // vfunc_8: about one radian a second
            {
                float step = (float)Math.Min(delta, 1.0 / 15);
                if (Input.IsKeyPressed(Key.Left)) camYaw += step;
                if (Input.IsKeyPressed(Key.Right)) camYaw -= step;
                float pitch0 = zoom.Pitch ?? zoom.PitchOf(zoom.Distance);
                if (Input.IsKeyPressed(Key.Up)) zoom.Pitch = Mathf.Clamp(pitch0 + step, 0.15f, 1.57f);
                if (Input.IsKeyPressed(Key.Down)) zoom.Pitch = Mathf.Clamp(pitch0 - step, 0.15f, 1.57f);
            }
            TriggerCamera?.Update((float)delta);
            if (TriggerCamera?.Shown is { } s)
            {
                // a lock or fly-by mode (CameraMgr::Update, camera.cpp:819-825) places the camera; the follow mode waits
                camera.GlobalTransform = s.Transform();
                camera.Fov = s.Fov;
                triggerCameraShown = true;
                // STILL-OPEN: a lock or fly-by mode takes the shake too (its own vfunc_3); here the shakes only run their time
                var none = System.Numerics.Vector3.Zero;
                Net.CameraShakes.Effects.Update(Net.CameraShakes.Now, ref none);
            }
            else
            {
                // the field's fov is set once at load, so it is put back only when the trigger camera stops placing it
                if (triggerCameraShown) { camera.Fov = cam.FovDeg; triggerCameraShown = false; }
                UpdateCamera(delta);
            }
            // the listener at the camera target, turned as the camera (soundservice.cpp:975-999); without a target the client
            // leaves the listener where it was
            if (player.IsInsideTree()) listener.GlobalTransform = new Transform3D(camera.GlobalBasis, player.GlobalPosition);
        }
        // GFxMovieBackground::vfunc_8 (gfxmoviebackground.cpp:486-513): resize and setCameraDistance run without a local player too
        backdrop?.Update(camera, player);
        if (player == null) return;
        if (shotSettle > 0 && --shotSettle == 0)
        {
            var (_, _, _, shot) = script.Dequeue();
            var path = System.IO.Path.Combine(shotDir, shot);
            GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print($"shot {path} pos {player.GlobalPosition} anim {player.CurrentAnim} floor {player.Grounded}");
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
