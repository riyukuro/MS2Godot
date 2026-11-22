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
/// --sandbox-portal ID (the Tria portal that walk uses, default 20), --no-npcs. The talk shots (--sandbox-npc ID) talk to the
/// first spawned npc of that id; --sandbox-npc-near X,Y (map x, y) picks the one closest to that point instead, and
/// --sandbox-stand X,Y stands the player there facing it (default: 120 in front of it).
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
    /// <summary>The live action key's current action and its 100 ms poll clock.</summary>
    Ms2.Ui.ActionKeyPoll actionKeys;
    NpcEmoticons emoticons;
    List<FieldNpcs.Placed> spawned = new();
    int shotNpc;
    List<int> shotNpcs = new();
    List<int> emoticonNpcs;
    Vector2? shotNpcNear, shotStand;    // --sandbox-npc-near, --sandbox-stand (map x, y)
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
            if (args[i] == "--sandbox-emoticons" && i + 1 < args.Length)
                emoticonNpcs = args[i + 1].Split(',').Select(x => int.TryParse(x, out int v) ? v : 0).Where(v => v != 0).ToList();
            if (args[i] == "--sandbox-npc-near" && i + 1 < args.Length) shotNpcNear = MapXY(args[i + 1]);
            if (args[i] == "--sandbox-stand" && i + 1 < args.Length) shotStand = MapXY(args[i + 1]);
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
        // live: the server's portal list (FieldPortal 0x48) and interact objects (InteractObject 0x63) bind this field's entities
        if (!offline && Ms2Godot.Net.LiveSession.Instance is { } lsPortals)
        {
            lsPortals.AttachPortals(fieldName, map.Entities);
            lsPortals.AttachInteractObjects(fieldName, map.Entities);
        }
        string presence = "";
        if (!offline && player != null && Ms2Godot.Net.LiveSession.Instance is { } liveSession)
        {
            // a live field: the server's npcs (FieldAddNpc) are the ones to talk to, and the server answers the talk
            try { AddChild(talk = new NpcTalk(packs, null, player, Constants(packs), liveSession) { FieldId = fieldId }); }
            catch (Exception ex) { GD.PushWarning("live npc talk: " + ex.Message); }
            try { AddChild(new NpcEmoticons(Ui(packs), Constants(packs), talk, null) { Viewer = player }); }
            catch (Exception ex) { GD.PushWarning("live npc emoticons: " + ex.Message); }
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
                if (player != null) AddChild(talk = new NpcTalk(packs, fnpcs.Spawned.Where(x => !x.Mob).ToList(), player, constants) { FieldId = fieldId });
                // the emoticons after the name tags: their layer is drawn above
                AddChild(emoticons = new NpcEmoticons(Ui(packs), constants, talk, fnpcs.Spawned.Where(x => !x.Mob)) { Viewer = player });
            }
            catch (Exception ex) { GD.PushWarning("npc presence: " + ex.Message); }
        }
        GD.Print($"field contents {fieldName} ({fieldId}): {Props.Built} animated props ({Props.Animated} playing, {Props.Frozen} static held at their add pose) in {tProps} ms; " +
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
            // entered under the loading screen: no input until it has faded (OnLoadingClosed); with none, at once
            if (loading != null) Ms2.Ui.FieldMoveCover.Raise(); else Ms2.Ui.FieldMoveCover.Lower();
            if (Load != null && Load.Total > 0) loading?.Progress(Load.Count, Load.Total); else loading?.Progress(1, 1);
            settleFrames = 3;
        }
        // a field shown without a loading screen (the sandbox's first field) is faded in at once: the client enters every
        // field through the loading screen, so its NotifyFadeInComplete always comes
        else if (talk != null) Callable.From(OnLoadingClosed).CallDeferred();
        else Ms2.Ui.FieldMoveCover.Lower();
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
    internal static QuestMaskTable Masks(PackSet packs) => maskTable ??= new QuestMaskTable(packs);
    static ClientConstants constantTable;
    internal static ClientConstants Constants(PackSet packs) => constantTable ??= Ms2.Ui.FeatureElements.Constants(packs);
    static UiAssets uiAssets;
    internal static UiAssets Ui(PackSet packs) => uiAssets != null && uiAssets.Packs == packs ? uiAssets : uiAssets = new UiAssets(packs);
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

    /// <summary>
    /// The loading screen's showGuide (UILoadingProgress::StartProgress @0x1419dfa60, uiloadingprogress.cpp:89-95): the
    /// client's is true while the local player's level is below a client constant (GetClientConstants() + 0x6e28); the
    /// sandbox character is level 1, below it. The movie's startProgress makes a new input guide each time and only hides
    /// the last one, and the client keeps one UILoadingProgress for the process (uiservice.cpp:2337), so each move adds a
    /// hidden guide to the loading screen while it is true, there as here. STILL-OPEN: the constant's name and value.
    /// </summary>
    public static bool ShowInputGuide = true;

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
            cover = new LoadingMovie(Ui(packs), ScreenMode.PixelSize(root));
            overlay.AddChild(cover);
        }
        catch (Exception ex) { GD.PushWarning("loading screen: " + ex.Message); carried = (overlay, null); return true; }
        // the first entry: CNetworkService::OnLoginToGame sets the mode to 0x3ed, UILoadingProgress (networkservice.cpp:910),
        // so the screen is the framed one (vfunc_52 is true on the lobby scene), not UILoadingProgressLobby's
        var picture = Picture(packs, Travel.LoadingImage);
        cover.Start(picture, picture == null ? screenShot ?? ImageTexture.CreateFromImage(root.GetViewport().GetTexture().GetImage()) : null,
                    Travel.LoadingTip, ShowInputGuide, true);
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
            loading = new LoadingMovie(Ui(packs), ScreenMode.PixelSize(GetTree().Root));
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

    void OnLoadingClosed()
    {
        GD.Print("loading screen closed");
        Ms2.Ui.FieldMoveCover.Lower();      // NotifyFadeInComplete: the field takes input
        talk?.OnFadeInComplete();       // UIPopupNpcTalkDialog's NotifyFadeInComplete
    }

    /// <summary>
    /// UILoadingProgress::StartProgress for a field move: the picture (or a screenshot when there is none), a random tip, and the
    /// keyboard guide for a low-level character (the sandbox's is level 1).
    /// </summary>
    void ShowLoading(string image, string tip, Texture2D screenShot = null)
    {
        if (loading == null) return;
        var picture = Picture(packs, image);
        loading.Start(picture, picture == null ? screenShot ?? Capture() : null, tip, ShowInputGuide, true);
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
        AfterDrawnFrame(GetTree(), drawn =>
        {
            var shot = drawn ? Capture() : null;
            foreach (var c in hidden) if (IsInstanceValid(c)) c.Visible = true;
            ShowLoading(null, tip, shot);
            then();
        });
    }

    /// <summary>The viewport's last drawn frame, or null when no window can draw (headless or minimized: nothing to take).</summary>
    Texture2D Capture() => DisplayServer.WindowCanDraw() ? ImageTexture.CreateFromImage(GetViewport().GetTexture().GetImage()) : null;

    /// <summary>
    /// Runs <paramref name="then"/> once: after the next frame Godot draws (true), or at the next process frame when no
    /// window can draw (false). Headless, or with the window minimized, Godot draws no frame and emits no FramePostDraw,
    /// so a wait on it alone would never end. It also never fires while a window can draw but Godot skips the draw
    /// (low-processor mode with nothing changed, or a disabled render loop); neither is used by the game.
    /// </summary>
    internal static void AfterDrawnFrame(SceneTree tree, Action<bool> then)
    {
        bool done = false;
        void Finish(bool drawn)
        {
            if (done) return;
            done = true;
            RenderingServer.FramePostDraw -= Drawn;
            tree.ProcessFrame -= Process;
            then(drawn);
        }
        void Drawn() => Finish(true);
        void Process() { if (!DisplayServer.WindowCanDraw()) Finish(false); }
        RenderingServer.FramePostDraw += Drawn;
        tree.ProcessFrame += Process;
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
                tipTable = new LoadingTipTable(packs, text, new ReservedWords(text, null), Ms2.Ui.LobbyFeatures.LocaleOf(packs));
            }
            return tipTable.Pick(new Random()) ?? "";
        }
        catch { return ""; }
    }

    /// <summary>PortalAction::vfunc_2 + the server's answer: cover the screen with the target map's loading picture, then load it.</summary>
    public bool Enter(FieldPortals.Portal p)
    {
        if (moving) return false;
        string xblock = XBlockOf(packs, p.TargetField);
        if (xblock == null)
        {
            GD.Print($"portal {p.Id}: field {p.TargetField} is not in the data");
            return false;
        }
        GD.Print($"portal {p.Id} ({p.Entity.Name}) -> field {p.TargetField} {xblock} portal {p.TargetPortal}");
        BeginMove(p.TargetField, () => new Travel.Move(p.TargetField, xblock, p.TargetPortal, fieldId));
        return true;
    }

    /// <summary>The field's xblock when the data has it, else null.</summary>
    static string XBlockOf(PackSet packs, int field)
    {
        try
        {
            string xblock = FieldData.XBlockOf(packs, field);
            return xblock != null && packs.Exists(XBlock.Archive, $"xblock/{xblock.ToLowerInvariant()}.xblock") ? xblock : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// A field move from this field: the talk ends, the loading screen comes up at once at 0% (the target map's picture or a
    /// screenshot of this field), and once it has been drawn the field scene changes with the move <paramref name="move"/>
    /// builds then (a newer move asked meanwhile replaces it: the last one the server sent is the one loaded).
    /// </summary>
    void BeginMove(int target, Func<Travel.Move> move, Func<bool> ready = null)
    {
        nextMove = move;
        moveReady = ready;
        if (moving) return;
        moving = true;
        Ms2.Ui.FieldMoveCover.Raise();      // the field and MyPC go at the move (FUN_141bbc580): no input reaches them from now
        talk?.End();
        // UILoadingProgress::GetLoadingImagePath: a picture of the target map, or a screenshot of this field (see PickForMove)
        Travel.LoadingImage = new LoadingImageTable(packs, Ms2.Ui.LobbyFeatures.LocaleOf(packs)).PickForMove(target, fieldId, new Random());
        Travel.LoadingTip = PickTip();
        GD.Print($"field move -> {target}, loading picture {Travel.LoadingImage ?? "(screenshot)"}");
        // the loading screen comes up at once at 0%; the load starts once a frame with it has been drawn. The client has no
        // wait of its own here: WorldService::LoadMap (worldservice_1.cpp:3320) calls FUN_141bbc1d0, which first runs the
        // move's teardown (FUN_141bbc580, :3353; it starts the progress screen, CUIService FUN_141adb900) and goes on
        // loading in the same call (EntityWorldManager::LoadMap, :3383); the one frame is what puts the screen up before
        // the old field goes. Headless or minimized no frame is drawn, and the load starts at the next frame.
        if (Travel.LoadingImage != null) { ShowLoading(Travel.LoadingImage, Travel.LoadingTip); Go(); }
        else ShowLoadingOverField(Travel.LoadingTip, () => Go());

        void Go() => AfterDrawnFrame(GetTree(), _ => Fire());
        void Fire()
        {
            if (!IsInstanceValid(this) || !IsInsideTree()) return;
            // a move to another server: the loading screen stays up until the new server's field entry came
            if (moveReady != null && !moveReady()) { GetTree().CreateTimer(0.05, ignoreTimeScale: true).Timeout += Fire; return; }
            moveReady = null;
            if (shotDir != null) Shot("loading.png");
            Travel.Pending = nextMove?.Invoke();
            nextMove = null;
            // The client's loading screen (UILoadingProgress) is a UI object CUIService registers once (uiservice.cpp:2337),
            // not part of the field: StartProgress at the move (FUN_141adb900), StopProgress after the load, the same movie
            // on screen all the way through the field change. So the cover leaves this field for the tree root, as the
            // flow's first-entry cover (GameFlow's loading layer) does, and the next field takes it over; freed with
            // this scene, the screen would show the empty clear colour while the next field builds.
            if (overlay != null && loading != null && Travel.Pending != null)
            {
                overlay.Reparent(GetTree().Root, false);
                carried = (overlay, loading);
            }
            ChangeField(GetTree());
        }
    }
    Func<Travel.Move> nextMove;
    Func<bool> moveReady;

    /// <summary>
    /// The move's field change, at the end of the frame. Under the HUD only the field scene is swapped
    /// (<see cref="HudScene.SwapField"/>): the client's CUIService and its dialogs live for the process and a move only
    /// unloads the world (WorldService FUN_141bbc580, worldservice_1.cpp:9962). A field run on its own (field.tscn as the
    /// scene) changes the scene.
    /// </summary>
    static void ChangeField(SceneTree tree)
    {
        if (tree.CurrentScene is HudScene hud) hud.CallDeferred(HudScene.MethodName.SwapField);
        else tree.ChangeSceneToFile(Flow.Session.GameScene ?? "res://field.tscn");
    }

    /// <summary>
    /// The server's field entry in game (LiveSession, RequestFieldEnter error 0): the same move a portal walk takes, to the
    /// entry's map, arriving where the entry says (LiveSession.Decorate), no portal of ours. The login entry builds its
    /// field through the same scene and Travel.Pending (GameFlow.BeginLoading). A map the data does not have loads the
    /// default field, as the login entry's does. No field up (a move while the previous one still loads): the field
    /// changes at once and the new field's load puts up its own loading screen (Field.BeginLoad -> CoverLoad).
    /// With <paramref name="ready"/> (a GameToGame: the old field goes at once, the entry comes from the next server) the
    /// field is built only once it returns true; the loading screen stays up until then.
    /// </summary>
    public static void ServerMove(SceneTree tree, int map, Func<bool> ready = null)
    {
        Ms2.Ui.FieldMoveCover.Raise();
        var packs = PackSet.Shared(Settings.DataDir);
        Travel.Move Build()
        {
            string xblock = XBlockOf(packs, map);
            if (xblock == null) GD.Print($"field move: map {map} is not in the data, the default field loads");
            return Net.LiveSession.Decorate(xblock == null ? null : new Travel.Move(map, xblock, 0, (tree.GetFirstNodeInGroup(Group) as FieldContents)?.fieldId ?? 0));
        }
        if (tree.GetFirstNodeInGroup(Group) is FieldContents here && here.IsInsideTree() && here.player != null)
        {
            here.BeginMove(map, Build, ready);
            return;
        }
        if (ready != null && !ready())
        {
            tree.CreateTimer(0.05, ignoreTimeScale: true).Timeout += () => ServerMove(tree, map, ready);
            return;
        }
        Travel.LoadingImage = null;
        Travel.LoadingTip = PickTip(packs);
        Travel.Pending = Build();
        ChangeField(tree);
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
        if (Ms2.Ui.FieldMoveCover.Up) { actionHeld = false; return; }     // no interaction under a move's loading screen
        // the action key reads nothing while a text field has the focus: the key table runs no action key then
        // (CKeyTable::FUN_141431fc0) and the actions test the move gate themselves (Unk_PCMove::FUN_141683ef0 in
        // TaxiStationAction::vfunc_1 and HoldAction's, actionkeyobject.cpp:776, 2211, 2257, 2315)
        bool action = !Ms2.Ui.InputBlock.Active && Input.IsKeyPressed(Key.Space);
        bool pressed = action && !actionHeld;
        actionHeld = action;
        if (!offline)
        {
            // live: at most every 100 ms the action key's poll runs its actions by ascending key and the first that applies
            // is the current one (Unk_ActionKeyManager::Update, a CRBMap): gathering (4), the interact npc (0x11) and
            // interact (0x12) objects, the server-listed portal the player stands in (0x13, PortalAction vfunc_1), the
            // liftable cube in reach (0x15, LiftCubeAction vfunc_1, from the ridee when riding), the billboard (0x18).
            // TalkAction (0x14) is polled at the press: its npc search allocates. The key runs only the
            // current action (ActivateCurrentAction), so a refused interact does not reach the talk. Active
            // StateInteraction casts tick every frame (0 B/frame).
            // STILL-OPEN: a billboard or a lift keeps its highlight next to a talkable npc (the talk takes the key at the press).
            // In cube mode (PC::IsInCubeMode) the poll runs the cube-mode map instead (GetActiveActionMap,
            // actionkeyobject.cpp:707-717): put down liftable (0xc), portal (0x13), talk (0x14, at the press).
            var playerMs2 = FieldPortals.ToMs2(player.GlobalPosition);
            bool cubeMode = Ms2Godot.Net.LiveSession.Instance is { } cs && cs.CubeCraft.InCubeMode;
            if (actionKeys.Due((uint)Time.GetTicksMsec()))
            {
                if (cubeMode)
                {
                    bool onPutDown = LivePutDownPoll(playerMs2);
                    actionKeys.Current = Ms2.Ui.ActionKeyPoll.PickCubeMode(onPutDown, !onPutDown && LivePortalPoll(), false);
                }
                else
                {
                    bool onInteract = LiveInteractPoll(playerMs2);
                    bool onPortal = !onInteract && LivePortalPoll();
                    bool onLift = !onInteract && !onPortal && LiveLiftPoll(playerMs2);
                    bool onBoard = !onInteract && !onPortal && !onLift && LiveInteractBillBoardPoll(playerMs2);
                    bool onReactor = !onInteract && !onPortal && !onLift && !onBoard && LiveReactorPoll(playerMs2);
                    bool onHoldPc = !onInteract && !onPortal && !onLift && !onBoard && !onReactor
                        && Ms2Godot.Net.LiveSession.Instance is { } hls && hls.PollHoldPc();
                    actionKeys.Current = Ms2.Ui.ActionKeyPoll.Pick(onInteract, onPortal, false, onLift, onBoard, onReactor, onHoldPc);
                }
            }
            LiveInteractTick(playerMs2, player.State != Player.MoveState.Idle);
            if (!pressed) return;
            if (talk != null && talk.Open) { talk.ActionKey(); return; }
            // ActivateCurrentAction (actionkeyobject.cpp:619-620): with an item held (GetHeldItem) the key does nothing
            bool held = Ms2Godot.Net.LiveSession.Instance is { } hs && hs.HeldItem.Held;
            var target = actionKeys.OnPress(heldItem: held);
            if (held || !actionKeys.InActiveMap(cubeMode)) return;
            if (target is Ms2.Ui.ActionKeyTarget.Lift or Ms2.Ui.ActionKeyTarget.BillBoard or Ms2.Ui.ActionKeyTarget.Reactor
                    or Ms2.Ui.ActionKeyTarget.HoldPc
                && talk?.FindTalkable() != null)
                target = Ms2.Ui.ActionKeyTarget.Talk;                  // talk 0x14 comes before lift 0x15, billboard 0x18, reactor 0x1a, hold pc 0x1b
            switch (target)
            {
                case Ms2.Ui.ActionKeyTarget.Interact:
                case Ms2.Ui.ActionKeyTarget.BillBoard: LiveInteractUse(playerMs2); break;
                case Ms2.Ui.ActionKeyTarget.Portal: LivePortalUse(); break;
                case Ms2.Ui.ActionKeyTarget.Lift: if (Ms2Godot.Net.LiveSession.Instance is { } lls) lls.UseLift(); break;
                case Ms2.Ui.ActionKeyTarget.Reactor: if (!moving && Ms2Godot.Net.LiveSession.Instance is { } rls) rls.UseReactor(); break;
                case Ms2.Ui.ActionKeyTarget.HoldPc: if (Ms2Godot.Net.LiveSession.Instance is { } hps) hps.UseHoldPc(); break;
                // PutDownLiftableAction::vfunc_2 (@0x14095ad50): the active craft's ConfirmPlacement, which sends
                // RequestCube 10 for a free cell (see CubeCraftState.ConfirmPlacement for what stays open).
                case Ms2.Ui.ActionKeyTarget.PutDownLiftable:
                    if (Ms2Godot.Net.LiveSession.Instance is { } pls) pls.ConfirmPlacement();
                    break;
                default: talk?.ActionKey(); break;
            }
            return;
        }
        if (talk != null && talk.Open) { if (pressed) talk.ActionKey(); return; }
        if (action && TryEnter()) return;
        if (pressed) talk?.ActionKey();
    }

    /// <summary>Live: the action key's poll at the player's point; true when the player stands in a portal the key goes to.</summary>
    public bool LivePortalPoll()
    {
        if (offline || player == null || moving || Ms2Godot.Net.LiveSession.Instance is not { } ls) return false;
        return ls.PollPortal(FieldPortals.ToMs2(player.GlobalPosition));
    }

    /// <summary>Live: the action key on the polled portal (its checks, then the move request or nothing).</summary>
    public void LivePortalUse()
    {
        if (offline || moving || Ms2Godot.Net.LiveSession.Instance is not { } ls) return;
        ls.UsePortal();
    }

    /// <summary>Live: the interact poll at the player's point; the npc search measures from the ridee when riding
    /// (WorldService::FindNearestNpc, worldservice_1.cpp:6443-6448).</summary>
    public bool LiveInteractPoll(Ms2.Formats.Model.Vec3 at)
    {
        if (offline || player == null || moving || Ms2Godot.Net.LiveSession.Instance is not { } ls) return false;
        var ridee = player.Mount?.Visual;
        var npcAt = ridee != null && IsInstanceValid(ridee) ? FieldPortals.ToMs2(ridee.GlobalPosition) : at;
        return ls.PollInteract(at, npcAt);
    }

    /// <summary>Live: the lift poll from the player's point, or the ridee's when riding (PCBase::FUN_1416767d0,
    /// pcbase.cpp:3235).</summary>
    public bool LiveLiftPoll(Ms2.Formats.Model.Vec3 at)
    {
        if (offline || player == null || moving || Ms2Godot.Net.LiveSession.Instance is not { } ls) return false;
        var ridee = player.Mount?.Visual;
        return ls.PollLift(ridee != null && IsInstanceValid(ridee) ? FieldPortals.ToMs2(ridee.GlobalPosition) : at);
    }

    /// <summary>Live: the put-down poll (key 0xc) from the player's point and facing, the ridee's when riding
    /// (PCBase::FUN_1416767d0, GetRideeAwareRotation), against the field's cubes.</summary>
    public bool LivePutDownPoll(Ms2.Formats.Model.Vec3 at)
    {
        if (offline || player == null || moving || Ms2Godot.Net.LiveSession.Instance is not { } ls) return false;
        var ridee = player.Mount?.Visual;
        return ls.PollPutDown(ridee != null && IsInstanceValid(ridee) ? FieldPortals.ToMs2(ridee.GlobalPosition) : at, player.FacingRotZ,
            player.Cubes);
    }

    /// <summary>Live: the reactor poll (key 0x1a) from the player's point, or the ridee's when riding
    /// (ReactorAction::FUN_14095fcb0, PCBase::FUN_1416767d0).</summary>
    public bool LiveReactorPoll(Ms2.Formats.Model.Vec3 at)
    {
        if (offline || player == null || moving || Ms2Godot.Net.LiveSession.Instance is not { } ls) return false;
        var ridee = player.Mount?.Visual;
        return ls.PollReactor(ridee != null && IsInstanceValid(ridee) ? FieldPortals.ToMs2(ridee.GlobalPosition) : at);
    }

    public bool LiveInteractBillBoardPoll(Ms2.Formats.Model.Vec3 at) =>
        !offline && player != null && !moving && Ms2Godot.Net.LiveSession.Instance is { } ls && ls.PollInteractBillBoard(at);

    public void LiveInteractTick(Ms2.Formats.Model.Vec3 at, bool playerMoved)
    {
        if (offline || moving || Ms2Godot.Net.LiveSession.Instance is not { } ls) return;
        ls.TickInteract(at, playerMoved);
        ls.TickReactor(at, playerMoved);
    }

    public bool LiveInteractUse(Ms2.Formats.Model.Vec3 at) =>
        !offline && !moving && Ms2Godot.Net.LiveSession.Instance is { } ls && ls.UseInteract(at);

    /// <summary>A scripted live run: puts the player in the registered portal <paramref name="id"/>'s box (as a walk into it
    /// ends); false when the server listed no such portal with a box here.</summary>
    public bool StandInPortal(int id)
    {
        if (player == null || Ms2Godot.Net.LiveSession.Instance?.Portals?.ById(id) is not { HasBox: true } p) return false;
        player.GlobalPosition = FieldBuilder.ToGodotPoint(p.Center) + new Vector3(0, 5, 0);
        player.Velocity = Vector3.Zero;
        return true;
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
        player.ScriptDriven = true;
        int leg = Travel.ShotLeg++;
        if (leg == 0 && emoticonNpcs != null) { PlanEmoticons(); return; }
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

    static Vector2? MapXY(string arg)
    {
        var v = arg.Split(',');
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return v.Length == 2 && float.TryParse(v[0], System.Globalization.NumberStyles.Float, inv, out float x)
            && float.TryParse(v[1], System.Globalization.NumberStyles.Float, inv, out float y) ? new Vector2(x, y) : null;
    }

    /// <summary>The talk shots' npc of this id: the first spawned, or with --sandbox-npc-near the one closest to that map point.</summary>
    FieldNpcs.Placed Pick(IEnumerable<FieldNpcs.Placed> npcs, int id)
    {
        var all = npcs.Where(x => x.Row.Id == id);
        if (shotNpcNear is not { } near) return all.FirstOrDefault();
        var at = FieldBuilder.ToGodotPoint(new Vec3(near.X, near.Y, 0));
        return all.OrderBy(x => new Vector2(x.Position.X - at.X, x.Position.Z - at.Z).LengthSquared()).FirstOrDefault();
    }

    /// <summary>Stands the player before the talk shots' npc, facing it: at --sandbox-stand, else 120 in front of it.</summary>
    void StandBefore(FieldNpcs.Placed n)
    {
        if (shotStand is { } s)
        {
            var p = FieldBuilder.ToGodotPoint(new Vec3(s.X, s.Y, 0));
            p.Y = n.Position.Y + 20;
            player.GlobalPosition = p;
            var to = n.Position - p; to.Y = 0;
            if (to.LengthSquared() > 0) player.Face(to.Normalized());
            GD.Print($"talk npc {n.Row.Id} at {n.Position.X:0},{-n.Position.Z:0}; player stands at {s.X:0},{s.Y:0}");
            return;
        }
        double yaw = n.Root.Rotation.Y;
        var front = new Vector3((float)Math.Sin(yaw), 0, (float)Math.Cos(yaw));
        player.GlobalPosition = n.Position + front * 120 + new Vector3(0, 20, 0); player.Face(-front);
        var at = player.GlobalPosition;
        GD.Print($"talk npc {n.Row.Id} at {n.Position.X:0},{-n.Position.Z:0}; player stands at {at.X:0},{-at.Z:0}");
    }

    /// <summary>Proof of the npc presence and talk: stand before an npc with a name tag and a script, talk, page, close, walk into it.</summary>
    /// <summary>
    /// The emoticon shots (--sandbox-shots DIR --sandbox-emoticons ID[,ID...]): a shot before each npc named, then a quest giver
    /// of the field (an npc that starts and completes a quest it can offer now) through its marks: can start, in progress,
    /// can complete. Then the emoticons' frame cost.
    /// </summary>
    void PlanEmoticons()
    {
        steps.Enqueue(("settle", 1.5f, null, "e0_spawn.png"));
        if (talk == null || emoticons?.Controller == null) { GD.Print("no emoticons"); steps.Enqueue(("quit", 0, null, null)); return; }
        var friendly = spawned.Where(x => !x.Mob && x.Row.Friendly != 0).ToList();
        foreach (var n in friendly)
        {
            var m = talk.QuestMark(n.Row.Id);
            if (!string.IsNullOrEmpty(n.Row.Icon) || m.State != 4)
                GD.Print($"emoticon npc {n.Row.Id} kind {n.Row.Kind} icon '{n.Row.Icon}' mark {m.State} quest {m.QuestId}");
        }
        foreach (int id in emoticonNpcs)
        {
            var n = Pick(spawned, id);
            if (n == null) { GD.Print($"no npc {id}"); continue; }
            steps.Enqueue(("teleport", 0, () => { StandBefore(n); return Vector3.Zero; }, null));
            steps.Enqueue(("settle", 1.5f, null, $"e1_{id}.png"));
        }
        var quests = NpcTalk.ClientQuests(packs);
        var giver = friendly.Select(n => (n, m: talk.QuestMark(n.Row.Id)))
                            .FirstOrDefault(x => x.m.State == 0 && quests.Quest(x.m.QuestId) is { } d && d.CompleteNpc == x.n.Row.Id);
        if (giver.n != null)
        {
            int q = giver.m.QuestId;
            GD.Print($"emoticon quest giver {giver.n.Row.Id}: quest {q}");
            steps.Enqueue(("teleport", 0, () => { StandBefore(giver.n); return Vector3.Zero; }, null));
            steps.Enqueue(("settle", 1.5f, null, "e2_quest_start.png"));
            steps.Enqueue(("teleport", 0, () => { GD.Print($"accept {q} (in progress): {talk.SandboxAccept(q, false)}"); return Vector3.Zero; }, null));
            steps.Enqueue(("settle", 1.5f, null, "e3_quest_progress.png"));
            steps.Enqueue(("teleport", 0, () => { talk.SandboxForget(q); GD.Print($"accept {q} (met): {talk.SandboxAccept(q, true)}"); return Vector3.Zero; }, null));
            steps.Enqueue(("settle", 1.5f, null, "e4_quest_complete.png"));
        }
        else GD.Print("no quest giver with a startable quest it also completes");
        steps.Enqueue(("teleport", 0, () =>
        {
            var c = emoticons.Controller;
            GD.Print($"emoticons: {c.Entities.Count} entities, {c.Entities.Values.Count(e => e.Active)} active, {c.ClipsMade} clips, " +
                     $"{emoticons.FrameMs / Math.Max(1, emoticons.Frames):0.000} ms a frame over {emoticons.Frames} frames");
            return Vector3.Zero;
        }, null));
        steps.Enqueue(("quit", 0, null, null));
    }

    void PlanTalk()
    {
        var n = shotNpc != 0 ? Pick(spawned, shotNpc) : spawned.FirstOrDefault(x => x.Row.Nametag && x.Row.Friendly == 2 && !x.Row.CapsuleIgnore && x.Row.Illust != null
              && packs.Exists("Resource/Image", $"quest/illust/{x.Row.Illust.ToLowerInvariant()}.dds"));
        if (n == null || talk == null) { GD.Print("no npc for the talk shots"); return; }
        steps.Enqueue(("teleport", 0, () => { StandBefore(n); return Vector3.Zero; }, null));
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
        Travel.ShotLeg++;
        var live = Ms2Godot.Net.LiveSession.Instance;
        FieldNpcs.Placed Find(int id) => live == null ? null : Pick(live.TalkNpcs, id);
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
                StandBefore(n);
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
