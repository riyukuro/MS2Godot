using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2Godot.World;

/// <summary>
/// What a field shows besides its static meshes, with no server (offline sandbox): self-animating props (FieldProps), actors and
/// stand-in npcs (FieldNpcs), and the portals (FieldPortals) with the field move and its loading picture.
/// Field creates it before FieldBuilder runs (FieldBuilder.DrawsInstance hands it the animated models) and starts it once the
/// player exists. Keys: the action key (Space) in a portal moves to its target field.
/// User args: --sandbox-shots DIR (scripted walk Tria -> portal -> target field -> back, with screenshots, then quit),
/// --sandbox-portal ID (the Tria portal that walk uses, default 20), --no-npcs.
/// </summary>
public partial class FieldContents : Node3D
{
    readonly PackSet packs;
    readonly ModelBuilder builder;
    readonly AssetIndex assets;
    public readonly FieldProps Props;
    FieldMap map;
    FieldPortals portals;
    Player player;
    int fieldId;
    bool offline;

    // the loading transition: a full-screen picture over a black backdrop
    CanvasLayer overlay;
    ColorRect black;
    TextureRect picture;
    float fade;               // 1 = covered
    int fadeDir;              // -1 uncovering, +1 covering
    Action whenCovered;
    int settleFrames;
    bool moving;

    // proof run
    string shotDir;
    int shotPortal = 20;
    readonly Queue<(string what, float secs, Func<Vector3> dir, string shot)> steps = new();
    float stepTime;
    int shotWait = -1;

    public FieldContents(PackSet packs, ModelBuilder builder, AssetIndex assets)
    {
        this.packs = packs; this.builder = builder; this.assets = assets;
        Name = "FieldContents";
        Props = new FieldProps(builder);
    }

    public FieldContents() { }      // Godot needs a parameterless constructor for the type

    /// <summary>The xblock to load: the pending sandbox move's target, or null.</summary>
    public static string PendingXBlock => Travel.Pending?.XBlock;

    /// <summary>After the field is built: props, actors, npcs, portals; places the player at the arrival portal.</summary>
    public void Start(FieldMap map, string fieldName, Player player, bool offline)
    {
        this.map = map; this.player = player; this.offline = offline;
        var args = OS.GetCmdlineUserArgs();
        bool npcs = true;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--sandbox-shots" && i + 1 < args.Length) shotDir = args[i + 1];
            if (args[i] == "--sandbox-portal" && i + 1 < args.Length) int.TryParse(args[i + 1], out shotPortal);
            if (args[i] == "--no-npcs") npcs = false;
        }
        try { fieldId = FieldData.FindByXBlock(packs, fieldName)?.Id ?? 0; } catch { }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        AddChild(Props.Build());
        long tProps = sw.ElapsedMilliseconds;

        var fnpcs = new FieldNpcs(packs, assets, builder);
        var people = new Node3D { Name = "Characters" };
        AddChild(people);
        fnpcs.PlaceActors(people, map);
        if (offline && npcs)
        {
            try { fnpcs.SpawnNpcs(people, map, Npcs(packs)); }
            catch (Exception ex) { GD.PushWarning("npcs: " + ex.Message); }
        }
        long tNpcs = sw.ElapsedMilliseconds;
        portals = new FieldPortals(map);
        GD.Print($"field contents {fieldName} ({fieldId}): {Props.Built} animated props ({Props.Animated} playing) in {tProps} ms; " +
                 $"{fnpcs.Actors} actors, {fnpcs.Npcs} npcs, {fnpcs.Missing} without a model in {tNpcs - tProps} ms; {portals.All.Count} portals");

        BuildOverlay();
        var move = Travel.Pending;
        Travel.Pending = null;
        if (move != null && player != null) Arrive(move);
        if (move != null) { fade = 1; fadeDir = -1; settleFrames = 3; }      // uncover once the new field has drawn
        if (shotDir != null && player != null) PlanShots();
    }

    static NpcTable npcTable;
    static NpcTable Npcs(PackSet packs) => npcTable ??= new NpcTable(packs);

    /// <summary>The server's field entry after a portal move: the player stands at the target portal, facing its way.</summary>
    void Arrive(Travel.Move move)
    {
        var p = portals.ById(move.PortalId);
        if (p == null) { GD.Print($"arrival portal {move.PortalId} not in this field; staying at the spawn point"); return; }
        var pos = FieldBuilder.ToGodotPoint(p.Position) + new Vector3(0, 5, 0);
        player.GlobalPosition = pos;
        player.SpawnPoint = pos;
        player.Velocity = Vector3.Zero;
        double yaw = p.RotZDeg * Math.PI / 180;
        player.Face(FieldBuilder.ToGodotPoint(new Vec3(Math.Sin(yaw), -Math.Cos(yaw), 0)));
        GD.Print($"arrived at portal {p.Id} ({p.Entity.Name}) from field {move.FromFieldId}, ms2 {p.Position}");
    }

    void BuildOverlay()
    {
        overlay = new CanvasLayer { Layer = 100, Name = "Loading" };
        AddChild(overlay);
        black = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
        black.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        overlay.AddChild(black);
        picture = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        picture.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        black.AddChild(picture);
        if (Travel.LoadingImage != null) picture.Texture = LoadPicture(Travel.LoadingImage);
        SetFade(0);
    }

    Texture2D LoadPicture(string entry)
    {
        try
        {
            if (!packs.Exists(LoadingImageTable.ImageArchive, entry)) return null;
            var img = new Image();
            return img.LoadDdsFromBuffer(packs.Read(LoadingImageTable.ImageArchive, entry)) == Error.Ok ? ImageTexture.CreateFromImage(img) : null;
        }
        catch { return null; }
    }

    void SetFade(float f)
    {
        fade = f;
        black.Visible = f > 0;
        black.Modulate = new Color(1, 1, 1, f);
    }

    /// <summary>PortalAction::vfunc_2 + the server's answer: cover the screen with the target map's loading picture, then load it.</summary>
    public bool Enter(FieldPortals.Portal p)
    {
        if (moving) return false;
        string xblock = null;
        try { xblock = FieldData.XBlockOf(packs, p.TargetField); } catch { }
        if (xblock == null || !packs.Exists(XBlock.Archive, $"xblock/{xblock.ToLowerInvariant()}.xblock"))
        {
            GD.Print($"portal {p.Id}: field {p.TargetField} is not in the data");
            return false;
        }
        moving = true;
        Travel.LoadingImage = new LoadingImageTable(packs).Pick(p.TargetField, new Random());
        picture.Texture = Travel.LoadingImage != null ? LoadPicture(Travel.LoadingImage) : null;
        GD.Print($"portal {p.Id} ({p.Entity.Name}) -> field {p.TargetField} {xblock} portal {p.TargetPortal}, loading picture {Travel.LoadingImage}");
        fadeDir = 1;
        whenCovered = () =>
        {
            Travel.Pending = new Travel.Move(p.TargetField, xblock, p.TargetPortal, fieldId);
            GetTree().ChangeSceneToFile(Flow.Session.GameScene ?? "res://field.tscn");
        };
        return true;
    }

    public override void _Process(double delta)
    {
        if (black == null) return;
        if (settleFrames > 0) { settleFrames--; return; }
        if (fadeDir != 0)
        {
            // MS2 covers in about a quarter second and uncovers a little slower once the field is ready
            float f = Mathf.Clamp(fade + fadeDir * (float)delta / (fadeDir > 0 ? 0.25f : 0.5f), 0, 1);
            SetFade(f);
            if (fadeDir > 0 && f >= 1)
            {
                fadeDir = 0;
                if (shotDir != null) Shot("loading.png");
                var a = whenCovered; whenCovered = null;
                // one more drawn frame with the picture before the synchronous load
                if (a != null) GetTree().CreateTimer(0.1).Timeout += a;
            }
            else if (fadeDir < 0 && f <= 0) fadeDir = 0;
        }
        if (shotWait > 0 && --shotWait == 0) { shotWait = -1; NextStep(true); }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (player == null || portals == null || !offline || moving) return;
        if (shotDir != null) { RunSteps((float)delta); return; }
        if (Input.IsKeyPressed(Key.Space)) TryEnter();
    }

    bool TryEnter()
    {
        var p = portals.At(FieldPortals.ToMs2(player.GlobalPosition));
        return p != null && Enter(p);
    }

    // ---- proof run: Tria -> portal -> target field, walk off and back in, back in Tria ----

    void PlanShots()
    {
        DirAccess.MakeDirRecursiveAbsolute(shotDir);
        player.ProcessPhysicsPriority = 10;      // after Field (keyboard input) and this node (scripted input)
        int leg = Travel.ShotLeg++;
        if (leg == 0)
        {
            var p = portals.ById(shotPortal);
            if (p == null) { GD.PushError($"no portal {shotPortal}"); GetTree().Quit(4); return; }
            // stand 300 in front of the portal, facing it, then walk in and use it
            double yaw = p.RotZDeg * Math.PI / 180;
            var front = FieldBuilder.ToGodotPoint(new Vec3(Math.Sin(yaw), -Math.Cos(yaw), 0));
            var target = FieldBuilder.ToGodotPoint(p.Position);
            steps.Enqueue(("settle", 1.5f, null, "1_spawn.png"));
            steps.Enqueue(("teleport", 0, () => { player.GlobalPosition = target + front * 300 + new Vector3(0, 20, 0); player.Face(-front); return Vector3.Zero; }, null));
            steps.Enqueue(("settle", 1.0f, null, "2_before_portal.png"));
            steps.Enqueue(("walk", 4f, () => target - player.GlobalPosition, null));
            steps.Enqueue(("enter", 0, null, "3_in_portal.png"));
        }
        else if (leg == 1)
        {
            steps.Enqueue(("settle", 1.5f, null, "4_arrived.png"));
            var start = player.GlobalPosition;
            var face = new Vector3(Mathf.Sin(player.Visual.Rotation.Y), 0, Mathf.Cos(player.Visual.Rotation.Y));
            // walk off along the arrival facing; when something blocks the body, turn a quarter and try again
            var tries = new[] { face, face.Rotated(Vector3.Up, Mathf.Pi / 2), face.Rotated(Vector3.Up, -Mathf.Pi / 2), -face };
            int tryIndex = 0; float tryTime = 0; var tryFrom = start;
            steps.Enqueue(("walk", 1.6f, () =>
            {
                tryTime += (float)GetPhysicsProcessDeltaTime();
                if (tryTime > 0.3f && (player.GlobalPosition - tryFrom).Length() < 20 && tryIndex < tries.Length - 1) { tryIndex++; tryTime = 0; tryFrom = player.GlobalPosition; }
                return tries[tryIndex];
            }, "5_walked_off.png"));
            steps.Enqueue(("walk", 4f, () => start - player.GlobalPosition, null));
            steps.Enqueue(("enter", 0, null, "6_back_in_portal.png"));
        }
        else
        {
            steps.Enqueue(("settle", 1.5f, null, "7_back_in_tria.png"));
            steps.Enqueue(("settle", 0.6f, null, "8_later.png"));      // the props' controllers move between the two
            steps.Enqueue(("quit", 0, null, null));
        }
    }

    void RunSteps(float dt)
    {
        if (shotWait > 0 || steps.Count == 0) { player.Input2 = Vector2.Zero; return; }
        var (what, secs, dir, shot) = steps.Peek();
        switch (what)
        {
            case "teleport": dir(); steps.Dequeue(); return;
            case "quit": GD.Print("sandbox shots done"); packs.Dispose(); GetTree().Quit(0); steps.Clear(); return;
            case "walk":
                {
                    var d = dir();
                    d.Y = 0;
                    var p = portals.At(FieldPortals.ToMs2(player.GlobalPosition));
                    bool arrived = shot == null && p != null && d.Length() < 40;
                    SetInput(d.Length() > 1e-3f ? d.Normalized() : Vector3.Zero);
                    stepTime += dt;
                    if (arrived || stepTime >= secs) Finish(shot);
                    return;
                }
            case "enter":
                {
                    player.Input2 = Vector2.Zero;
                    var p = portals.At(FieldPortals.ToMs2(player.GlobalPosition));
                    if (p == null) { GD.PushError($"not in a portal at {FieldPortals.ToMs2(player.GlobalPosition)}"); Shot("error_not_in_portal.png"); GetTree().Quit(5); steps.Clear(); return; }
                    Shot(shot);
                    steps.Clear();
                    Enter(p);
                    return;
                }
            default:
                player.Input2 = Vector2.Zero;
                stepTime += dt;
                if (stepTime >= secs) Finish(shot);
                return;
        }
    }

    void Finish(string shot)
    {
        player.Input2 = Vector2.Zero;
        if (shot == null) { steps.Dequeue(); stepTime = 0; return; }
        shotWait = 3;
    }

    void NextStep(bool takeShot)
    {
        var (_, _, _, shot) = steps.Dequeue();
        stepTime = 0;
        if (takeShot && shot != null) Shot(shot);
    }

    void Shot(string file)
    {
        var path = System.IO.Path.Combine(shotDir, $"{Travel.ShotLeg - 1}_{file}");
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"shot {path} pos ms2 {FieldPortals.ToMs2(player.GlobalPosition)}");
    }

    /// <summary>World direction -> the player's camera-relative input (Player: forward = (-sin, 0, -cos) of CameraYaw).</summary>
    void SetInput(Vector3 d)
    {
        var fwd = new Vector3(-Mathf.Sin(player.CameraYaw), 0, -Mathf.Cos(player.CameraYaw));
        var right = new Vector3(-fwd.Z, 0, fwd.X);
        player.Input2 = new Vector2(d.Dot(right), d.Dot(fwd));
        player.WalkMode = false;
    }
}
