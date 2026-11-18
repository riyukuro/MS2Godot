using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2Godot.Ui;
using Ms2Godot.Ui.Contracts;

namespace Ms2Godot.World;

/// <summary>
/// What a field shows besides its static meshes, with no server (offline sandbox): self-animating props (FieldProps), actors and
/// stand-in npcs (FieldNpcs) with their name tags, bodies (NpcPresence) and talk (NpcTalk), and the portals (FieldPortals, with
/// the client's quest masks for a fresh character) with the field move and its loading screen (LoadingMovie).
/// Field creates it before FieldBuilder runs (FieldBuilder.DrawsInstance hands it the animated models) and starts it once the
/// player exists. Keys: the action key (Space) in a portal moves to its target field; elsewhere it talks to the npc in front.
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

    // the loading screen (UILoadingProgress) over the field move
    CanvasLayer overlay;
    LoadingMovie loading;
    /// <summary>The cover of a field move under way, held by the tree root across the scene change (see <see cref="Enter"/>).</summary>
    static (CanvasLayer Overlay, LoadingMovie Loading) carried;
    int settleFrames = -1;
    /// <summary>The field build's entity count (the client's GetBlockInfo count / total), shown by the loading screen.</summary>
    public LoadCount Load;
    bool moving;

    NpcTalk talk;
    List<FieldNpcs.Placed> spawned = new();
    int shotNpc;
    List<int> shotNpcs = new();
    bool liveQuestSends;            // --sandbox-quest-sends: the live shot run presses next on quest pages too
    bool actionHeld;

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
        AddToGroup(Group);
        Props = new FieldProps(builder);
    }

    public FieldContents() { }      // Godot needs a parameterless constructor for the type

    /// <summary>The scene-tree group every field's contents node joins, so the HUD finds it without walking the tree.</summary>
    public const string Group = "field_contents";

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
            if (args[i] == "--sandbox-quest-sends") liveQuestSends = true;
            if (args[i] == "--sandbox-npc" && i + 1 < args.Length)
            {
                // one npc, or a comma list (a live run talks to each in turn)
                shotNpcs = args[i + 1].Split(',').Select(x => int.TryParse(x, out int v) ? v : 0).Where(v => v != 0).ToList();
                shotNpc = shotNpcs.FirstOrDefault();
            }
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
            try { fnpcs.SpawnNpcs(people, map, Npcs(packs), fieldId, offline ? Masks(packs) : null); }
            catch (Exception ex) { GD.PushWarning("npcs: " + ex.Message); }
            try { fnpcs.SpawnMobs(people, map, Npcs(packs), Regions(packs), fieldId); }
            catch (Exception ex) { GD.PushWarning("mobs: " + ex.Message); }
        }
        long tNpcs = sw.ElapsedMilliseconds;
        portals = new FieldPortals(map, fieldId, offline ? Masks(packs) : null);
        string presence = "";
        if (!offline && player != null && Ms2Godot.Net.LiveSession.Instance is { } liveSession)
        {
            // a live field: the server's npcs (FieldAddNpc) are the ones to talk to, and the server answers the talk
            try { AddChild(talk = new NpcTalk(packs, null, player, Constants(packs), liveSession)); }
            catch (Exception ex) { GD.PushWarning("live npc talk: " + ex.Message); }
        }
        if (offline && fnpcs.Spawned.Count > 0)
        {
            try
            {
                var constants = Constants(packs);
                var np = new NpcPresence(constants) { Viewer = player };
                AddChild(np);
                np.Build(fnpcs.Spawned, Ui(packs));
                presence = $"; {np.Bodies} npc bodies, {np.NameTags} name tags";
                spawned = fnpcs.Spawned;
                if (player != null) AddChild(talk = new NpcTalk(packs, fnpcs.Spawned.Where(x => !x.Mob).ToList(), player, constants));
            }
            catch (Exception ex) { GD.PushWarning("npc presence: " + ex.Message); }
        }
        GD.Print($"field contents {fieldName} ({fieldId}): {Props.Built} animated props ({Props.Animated} playing) in {tProps} ms; " +
                 $"{fnpcs.Actors} actors, {fnpcs.Npcs} npcs ({fnpcs.Masked} quest-masked), {fnpcs.Mobs} mobs, {fnpcs.Missing} without a model in {tNpcs - tProps} ms; " +
                 $"{portals.All.Count} portals ({portals.Masked} quest-masked, {portals.Hidden} hidden){presence}");
        var closed = portals.All.Where(x => !x.Enabled).Select(x => x.Id).OrderBy(x => x).ToList();
        if (closed.Count > 0) GD.Print($"closed portals: {string.Join(" ", closed)}");

        bool adopted = BuildOverlay();
        var move = Travel.Pending;
        Travel.Pending = null;
        if (move != null && player != null) Arrive(move);
        if (move != null)
        {
            // the field is loaded: the loading screen shows 100%, then fades once the new field has drawn. A portal move's
            // cover was started before the move and is still up; a move without one (first entry) starts it here.
            if (!adopted) ShowLoading(Travel.LoadingImage, Travel.LoadingTip);
            if (Load != null && Load.Total > 0) loading?.Progress(Load.Count, Load.Total); else loading?.Progress(1, 1);
            settleFrames = 3;
        }
        if (shotDir != null && player != null) { if (offline) PlanShots(); else PlanLiveTalk(); }
    }

    static NpcTable npcTable;
    static readonly object tableLock = new();
    /// <summary>The npc table, loaded once per run (a prefetch may be building it; the lock makes the caller wait for it).</summary>
    public static NpcTable Npcs(PackSet packs) { lock (tableLock) return npcTable ??= new NpcTable(packs, GameOptions.StringLanguage); }
    static RegionSpawnTable regionTable;
    static RegionSpawnTable Regions(PackSet packs) { lock (tableLock) return regionTable ??= new RegionSpawnTable(packs); }

    /// <summary>Parses the npc and region spawn tables on a worker thread while the field builds (pure data, no engine objects).</summary>
    public static void Prefetch(PackSet packs)
        => System.Threading.Tasks.Task.Run(() => { try { Npcs(packs); Regions(packs); } catch { } });

    /// <summary>The field's portals (with the quest masks) and spawned npcs, for the HUD's minimap; null until Start.</summary>
    public FieldPortals Portals => portals;
    public IReadOnlyList<FieldNpcs.Placed> SpawnedNpcs => spawned;
    public int FieldId => fieldId;
    /// <summary>The field's world objects (the minimap's taxi station and tagged objects).</summary>
    public FieldMap Map => map;
    static QuestMaskTable maskTable;
    static QuestMaskTable Masks(PackSet packs) => maskTable ??= new QuestMaskTable(packs);
    static ClientConstants constantTable;
    static ClientConstants Constants(PackSet packs) => constantTable ??= Ms2.Ui.FeatureElements.Constants(packs);
    static UiAssets uiAssets;
    static UiAssets Ui(PackSet packs) => uiAssets != null && uiAssets.Packs == packs ? uiAssets : uiAssets = new UiAssets(packs);
    static LoadingTipTable tipTable;

    /// <summary>The server's field entry after a portal move: the player stands at the target portal, facing its way.</summary>
    void Arrive(Travel.Move move)
    {
        if (move.At is { } at)
        {
            var spot = FieldBuilder.ToGodotPoint(at) + new Vector3(0, 5, 0);
            player.GlobalPosition = spot; player.SpawnPoint = spot; player.Velocity = Vector3.Zero;
            double a = move.RotZDeg * Math.PI / 180;
            player.Face(FieldBuilder.ToGodotPoint(new Vec3(Math.Sin(a), -Math.Cos(a), 0)));
            GD.Print($"arrived at the server's position, ms2 {at}");
            return;
        }
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

    /// <summary>The loading screen last started (by <see cref="CoverLoad"/> or a portal move), while it is up.</summary>
    static LoadingMovie shownCover;
    /// <summary>True while a field's loading screen covers the screen (GameFlow hands its first-entry picture over to it).</summary>
    public static bool CoverUp => shownCover != null && GodotObject.IsInstanceValid(shownCover) && shownCover.Active && shownCover.IsVisibleInTree();

    /// <summary>
    /// The start of a field load: the loading screen comes up before the field is built (WorldService::FUN_141bbc1d0 puts
    /// up LoadBackgroundMovie before EntityWorldManager::LoadMap). A portal move's cover is already up and carried at the
    /// tree root. A move without one (first entry) starts it here with the move's picture and tip, at the tree root, outside
    /// the field that is being built and kept hidden. Start adopts it either way (BuildOverlay). No move, no loading screen. True when it started one here.
    /// </summary>
    public static bool CoverLoad(PackSet packs, Node any, Texture2D screenShot = null)
    {
        coverOwner = any;
        if (carried.Overlay != null && GodotObject.IsInstanceValid(carried.Overlay)) return false;
        carried = default;
        if (Travel.Pending == null) return false;
        var root = any.GetTree().Root;
        var overlay = new CanvasLayer { Layer = 100, Name = "Loading" };
        // the root is busy while the scene that holds the field enters it: the layer goes in at the end of this frame's
        // process, before the frame is drawn (the field waits a frame for it)
        root.CallDeferred(Node.MethodName.AddChild, overlay);
        LoadingMovie cover;
        try
        {
            cover = new LoadingMovie(Ui(packs), root.Size);
            overlay.AddChild(cover);
        }
        catch (Exception ex) { GD.PushWarning("loading screen: " + ex.Message); carried = (overlay, null); return true; }
        // the first entry: CNetworkService::OnLoginToGame sets the mode to 0x3ed, UILoadingProgress (networkservice.cpp:910),
        // so the screen is the framed one (vfunc_52 is true on the lobby scene), not UILoadingProgressLobby's
        var picture = Picture(packs, Travel.LoadingImage);
        cover.Start(picture, picture == null ? screenShot ?? ImageTexture.CreateFromImage(root.GetViewport().GetTexture().GetImage()) : null,
                    Travel.LoadingTip, true, true);
        shownCover = cover;
        carried = (overlay, cover);
        return true;
    }

    static Node coverOwner;

    /// <summary>
    /// A field freed before it adopted its loading screen (its scene changed during the load) takes the screen down with
    /// it, unless a newer field load has taken the screen over (CoverLoad).
    /// </summary>
    public static void ReleaseCover(Node owner)
    {
        if (coverOwner != owner) return;
        coverOwner = null;
        if (carried.Overlay != null && GodotObject.IsInstanceValid(carried.Overlay)) carried.Overlay.QueueFree();
        if (shownCover != null && shownCover == carried.Loading) shownCover = null;   // an adopted screen is the field's own
        carried = default;
    }

    /// <summary>
    /// A field load that stopped before Start (an error in the build, or the field left the tree meanwhile): the loading
    /// screen it carried comes down (ReleaseCover) and the move it took is dropped, so the next field load (character
    /// creation's included) does not take that move and its cover; a move or screen a newer load has taken is left alone.
    /// </summary>
    public static void AbandonLoad(Node owner, Travel.Move move)
    {
        if (move != null && ReferenceEquals(Travel.Pending, move)) Travel.Pending = null;
        ReleaseCover(owner);
    }

    /// <summary>True while a loading screen is carried for the field being built (one started by CoverLoad or a portal move's).</summary>
    public static bool Covered => carried.Overlay != null && GodotObject.IsInstanceValid(carried.Overlay);

    /// <summary>While the field loads: the entity count on the carried loading screen (UILoadingProgress::SetProgress).</summary>
    public static void CoverProgress(int count, int total)
    {
        if (carried.Overlay == null || !GodotObject.IsInstanceValid(carried.Overlay)) return;
        carried.Loading?.Progress(count, total);
    }

    /// <summary>The loading screen layer; true when it is the cover a field move carried over from the previous field.</summary>
    bool BuildOverlay()
    {
        if (carried.Overlay != null && GodotObject.IsInstanceValid(carried.Overlay))
        {
            (overlay, loading) = carried;
            carried = default;
            overlay.Reparent(this, false);
            if (loading != null) loading.Controller.FadeInComplete += OnLoadingClosed;
            return loading != null;
        }
        carried = default;
        overlay = new CanvasLayer { Layer = 100, Name = "Loading" };
        AddChild(overlay);
        try
        {
            loading = new LoadingMovie(Ui(packs), GetTree().Root.Size);
            overlay.AddChild(loading);
            loading.Controller.FadeInComplete += OnLoadingClosed;
        }
        catch (Exception ex) { GD.PushWarning("loading screen: " + ex.Message); loading = null; }
        return false;
    }

    public override void _ExitTree()
    {
        if (loading != null && GodotObject.IsInstanceValid(loading)) loading.Controller.FadeInComplete -= OnLoadingClosed;
    }

    void OnLoadingClosed() => GD.Print("loading screen closed");

    /// <summary>
    /// UILoadingProgress::StartProgress for a field move: the picture (or a screenshot when there is none), a random tip, and the
    /// keyboard guide for a low-level character (the sandbox's is level 1).
    /// </summary>
    void ShowLoading(string image, string tip, Texture2D screenShot = null)
    {
        if (loading == null) return;
        var picture = Picture(packs, image);
        loading.Start(picture, picture == null ? screenShot ?? ImageTexture.CreateFromImage(GetViewport().GetTexture().GetImage()) : null, tip, true, true);
        shownCover = loading;
    }

    /// <summary>
    /// The loading screen over a screenshot of this field, then <paramref name="then"/>. The client's screenshot is the scene
    /// without the UI: UILoadingProgress::FUN_1419e07a0 copies GetScreenshotBuffer(1), the GFx manager's render buffer
    /// (GFxManager::GetRenderBuffer), into its background texture when the movie asks for img://...:screenShot, and every
    /// HUD, name tag and prompt is a GFx movie. Ours are canvas layers, so they are hidden for the one frame captured.
    /// </summary>
    void ShowLoadingOverField(string tip, Action then)
    {
        var hidden = new List<CanvasLayer>();
        void Hide(Node n)
        {
            if (n is CanvasLayer c && c.Layer >= 0 && c.Visible) { c.Visible = false; hidden.Add(c); }
            foreach (var ch in n.GetChildren()) Hide(ch);
        }
        Hide(GetTree().Root);
        void Captured()
        {
            RenderingServer.FramePostDraw -= Captured;
            var shot = ImageTexture.CreateFromImage(GetViewport().GetTexture().GetImage());
            foreach (var c in hidden) if (IsInstanceValid(c)) c.Visible = true;
            ShowLoading(null, tip, shot);
            then();
        }
        RenderingServer.FramePostDraw += Captured;
    }

    /// <summary>The picture entry when the data has it, else null (the screenshot).</summary>
    static string Picture(PackSet packs, string entry)
    {
        try { return entry != null && packs.Exists(LoadingImageTable.ImageArchive, entry) ? entry : null; }
        catch { return null; }
    }

    string PickTip() => PickTip(packs);

    /// <summary>CLoadingDescriptionTable::GetRandomTip: a random loading tip, "" when the data has none.</summary>
    public static string PickTip(PackSet packs)
    {
        try
        {
            // the tips' words are resolved as the table loads them (no tip uses $script, so no script table is needed)
            if (tipTable == null)
            {
                var text = new GameStrings(packs, GameOptions.StringLanguage);
                tipTable = new LoadingTipTable(packs, text, new ReservedWords(text, null));
            }
            return tipTable.Pick(new Random()) ?? "";
        }
        catch { return ""; }
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
        talk?.End();
        // UILoadingProgress::GetLoadingImagePath: a picture of the target map, or a screenshot of this field (see PickForMove)
        Travel.LoadingImage = new LoadingImageTable(packs).PickForMove(p.TargetField, fieldId, new Random());
        Travel.LoadingTip = PickTip();
        GD.Print($"portal {p.Id} ({p.Entity.Name}) -> field {p.TargetField} {xblock} portal {p.TargetPortal}, loading picture {Travel.LoadingImage ?? "(screenshot)"}");
        // the loading screen comes up at once at 0%; the load starts once it has been drawn
        if (Travel.LoadingImage != null) { ShowLoading(Travel.LoadingImage, Travel.LoadingTip); Go(); }
        else ShowLoadingOverField(Travel.LoadingTip, Go);
        return true;

        void Go() => GetTree().CreateTimer(0.15).Timeout += () =>
        {
            if (shotDir != null) Shot("loading.png");
            Travel.Pending = new Travel.Move(p.TargetField, xblock, p.TargetPortal, fieldId);
            // The client's loading screen (UILoadingProgress) is a UI object CUIService registers once (uiservice.cpp:2337),
            // not part of the field: StartProgress at the move (FUN_141adb900), StopProgress after the load, the same movie
            // on screen all the way through the field change. So the cover leaves this field for the tree root, as the
            // flow's first-entry cover (GameFlow's loading layer) does, and the next field takes it over; freed with
            // this scene, the screen would show the empty clear colour while the next field builds.
            if (overlay != null && loading != null)
            {
                overlay.Reparent(GetTree().Root, false);
                carried = (overlay, loading);
            }
            GetTree().ChangeSceneToFile(Flow.Session.GameScene ?? "res://field.tscn");
        };
    }

    public override void _Process(double delta)
    {
        if (loading == null) return;
        if (settleFrames > 0 && --settleFrames == 0)
        {
            settleFrames = -1;
            if (shotDir != null) Shot("loading_100.png");
            loading.Stop(true);       // UILoadingProgress::StopProgress: the 500 ms fade
        }
        if (shotWait > 0 && --shotWait == 0) { shotWait = -1; NextStep(true); }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (player == null || portals == null || moving) return;
        if (shotDir != null) { RunSteps((float)delta); return; }
        bool action = Input.IsKeyPressed(Key.Space);
        bool pressed = action && !actionHeld;
        actionHeld = action;
        if (!offline)
        {
            // live: the talk only; the server moves the player through portals
            if (pressed) talk?.ActionKey();
            return;
        }
        if (talk != null && talk.Open) { if (pressed) talk.ActionKey(); return; }
        if (action && TryEnter()) return;
        if (pressed) talk?.ActionKey();
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
        player.ScriptDriven = true;
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
            PlanTalk();
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

    /// <summary>Proof of the npc presence and talk: stand before an npc with a name tag and a script, talk, page, close, walk into it.</summary>
    void PlanTalk()
    {
        var n = spawned.FirstOrDefault(x => shotNpc != 0 ? x.Row.Id == shotNpc
            : x.Row.Nametag && x.Row.Friendly == 2 && !x.Row.CapsuleIgnore && x.Row.Illust != null
              && packs.Exists("Resource/Image", $"quest/illust/{x.Row.Illust.ToLowerInvariant()}.dds"));
        if (n == null || talk == null) { GD.Print("no npc for the talk shots"); return; }
        double yaw = n.Root.Rotation.Y;
        var front = new Vector3((float)Math.Sin(yaw), 0, (float)Math.Cos(yaw));
        steps.Enqueue(("teleport", 0, () => { player.GlobalPosition = n.Position + front * 120 + new Vector3(0, 20, 0); player.Face(-front); return Vector3.Zero; }, null));
        steps.Enqueue(("settle", 1.2f, null, "1a_npc_nametag.png"));
        // a chosen npc is talked to twice: a quest giver's first talk accepts (the select's first choice is its first quest),
        // the second shows the started quest's progress or complete talk
        for (int round = 1; round <= (shotNpc != 0 ? 2 : 1); round++)
        {
            string r = round == 1 ? "" : $"r{round}_";
            steps.Enqueue(("teleport", 0, () => { GD.Print($"talkable: {talk.FindTalkable()?.Row.Id}"); talk.ActionKey(); return Vector3.Zero; }, null));
            steps.Enqueue(("settle", 2.0f, null, $"1b_{r}talk.png"));
            // the action key through the pages (a choice page picks its first choice), a shot of each, up to 12
            for (int k = 1; k <= 12; k++)
            {
                int at = k;
                steps.Enqueue(("teleport", 0, () =>
                {
                    if (talk.Open) { talk.ActionKey(); GD.Print($"talk page {at}: script {talk.ScriptId} page {talk.Page + 1}"); }
                    return Vector3.Zero;
                }, null));
                steps.Enqueue(("settle", 0.8f, null, $"1c_{r}talk_page{at:00}.png"));
            }
            steps.Enqueue(("teleport", 0, () => { talk.End(); return Vector3.Zero; }, null));
            steps.Enqueue(("settle", 1.5f, null, $"1d_{r}talk_closed.png"));
        }
        steps.Enqueue(("walk", 2.0f, () => n.Position - player.GlobalPosition, null));
        steps.Enqueue(("settle", 0.3f, null, "1e_walked_into_npc.png"));
        steps.Enqueue(("teleport", 0, () =>
        {
            var d = player.GlobalPosition - n.Position; d.Y = 0;
            GD.Print($"walked into npc {n.Row.Id}: stopped {d.Length():0} from its centre (capsule radius {n.Row.CapsuleRadius})");
            return Vector3.Zero;
        }, null));
    }

    /// <summary>
    /// The live talk proof (--sandbox-shots DIR --sandbox-npc ID[,ID...] on a live session): wait for the server's npcs, then for each
    /// npc in turn stand in front of it and talk to it through every page, with a shot of each. One npc is talked to twice.
    /// On a quest page (accept, complete, progress) the run stops pressing next and ends the talk, since that next makes the server
    /// start or complete the quest and grant its rewards; --sandbox-quest-sends lets it press next there too (only for a character
    /// whose quest records may change).
    /// </summary>
    void PlanLiveTalk()
    {
        DirAccess.MakeDirRecursiveAbsolute(shotDir);
        player.ProcessPhysicsPriority = 10;
        Travel.ShotLeg++;
        var live = Ms2Godot.Net.LiveSession.Instance;
        FieldNpcs.Placed Find(int id) => live?.TalkNpcs.FirstOrDefault(x => x.Row.Id == id);
        var order = shotNpcs.Count == 1 ? new List<int> { shotNpcs[0], shotNpcs[0] } : shotNpcs;
        steps.Enqueue(("wait", 40f, () => order.All(id => Find(id) != null) ? Vector3.One : Vector3.Zero, "1_live_field.png"));
        steps.Enqueue(("teleport", 0, () =>
        {
            GD.Print($"live npcs: {string.Join(" ", live?.TalkNpcs.Select(x => x.Row.Id).Distinct().OrderBy(x => x) ?? Enumerable.Empty<int>())}");
            return Vector3.Zero;
        }, null));
        for (int round = 0; round < order.Count; round++)
        {
            int npcId = order[round];
            string r = round == 0 ? "" : $"r{round + 1}_";
            steps.Enqueue(("teleport", 0, () =>
            {
                var n = Find(npcId);
                if (n == null) { GD.PushError($"no live npc {npcId}"); return Vector3.Zero; }
                double yaw = n.Root.Rotation.Y;
                var front = new Vector3((float)Math.Sin(yaw), 0, (float)Math.Cos(yaw));
                player.GlobalPosition = n.Position + front * 120 + new Vector3(0, 20, 0); player.Face(-front);
                return Vector3.Zero;
            }, null));
            steps.Enqueue(("settle", 1.0f, null, $"1a_{r}npc_{npcId}.png"));
            steps.Enqueue(("teleport", 0, () => { GD.Print($"talkable: {talk?.FindTalkable()?.Row.Id}"); talk?.ActionKey(); return Vector3.Zero; }, null));
            steps.Enqueue(("settle", 3.0f, null, $"1b_{r}talk.png"));
            for (int k = 1; k <= 12; k++)
            {
                int at = k;
                steps.Enqueue(("teleport", 0, () =>
                {
                    if (talk == null || !talk.Open) return Vector3.Zero;
                    if (talk.OnQuestPage && !liveQuestSends)
                    {
                        GD.Print($"talk page {at}: script {talk.ScriptId} is a quest page, the talk ends (--sandbox-quest-sends to go on)");
                        talk.End();
                        return Vector3.Zero;
                    }
                    talk.ActionKey(); GD.Print($"talk page {at}: script {talk.ScriptId} page {talk.Page + 1}");
                    return Vector3.Zero;
                }, null));
                steps.Enqueue(("settle", 1.5f, null, $"1c_{r}talk_page{at:00}.png"));
            }
            steps.Enqueue(("teleport", 0, () => { talk?.End(); return Vector3.Zero; }, null));
            steps.Enqueue(("settle", 2.0f, null, $"1d_{r}talk_closed.png"));
        }
        steps.Enqueue(("quit", 0, null, null));
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
            case "wait":
                player.Input2 = Vector2.Zero;
                stepTime += dt;
                if (dir() != Vector3.Zero || stepTime >= secs) Finish(shot);
                return;
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
