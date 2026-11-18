using System.Text.Json.Nodes;
using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Quest;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The in-field HUD over the field scene: top bar (uiupperhuddialog), minimap frame (uiminimapdialog), chat (uimessengerdialog),
/// HP/SP orb and quick slots (uimainplayerinfo), EXP bar (uiexpbar) and game menu (uigamemenudialog), built from the player's
/// own Gfx.m2d and fed from <see cref="StubHudData"/>.
/// Args after `--`: --hudshot DIR saves a shot after the field has loaded and quits; --field is passed to the field scene.
/// </summary>
public partial class HudScene : Node
{
    UiAssets assets;
    readonly StubHudData data = StubHudData.Instance;
    /// <summary>UIMessengerBrowser's windows (one per default messenger, LoadDefaultWindows) and UIGroupMessengerBrowser,
    /// run from their movies; built once and kept over a relayout, as the client keeps its dialogs.</summary>
    readonly List<(MovieDialog Movie, MessengerController Side)> messengers = new();
    MovieDialog groupMessenger;
    MovieDialog notice;                 // uinoticedialog (UINoticeDialog)
    /// <summary>FUN_1418dd0c0 + FUN_1418dd110 (uibuiltindialog.cpp): the two bottom bars' height, (DAT_1439f8ccc - 2) +
    /// (DAT_1439f8cd0 - 2) = 19 + 19 with feature 0xbd (the adventure level bar) on.</summary>
    const float ExpBarLift = 21 - 2, AdventureBarLift = 21 - 2;
    string shotDir; int frames;
    string frameCase;                       // --frametime closed|open|scroll: frame times over 300 frames instead of a shot
    readonly List<double> frameDelta = new(), frameProcess = new(), frameMovie = new(), frameTick = new();
    long callsAtStart, interpAtStart;
    float u; Vector2 stage;                 // UI scale and stage size in stage units (see UiScale)
    Player player; Control uiRoot;
    MovieDialog targetBar;                  // uiupperhpbar
    /// <summary>UIUpperHpBar's C++ side: the target HP bar at the top (Hit / Remove from combat, Process per frame).</summary>
    public Ms2.Ui.Hud.UpperHpBarController TargetBar { get; private set; }
    DamageText damageText;
    PlayerHpBar playerBar;
    DamageEffect damageEffect;              // UIDamageEffect, the low-HP screen effect
    int[] questShot;                        // --questlog TAB,SUBTAB[,QUEST]: the shot opens the quest log on these tabs, selecting the quest
    string questState;                      // --questlog-state LEVEL:ID,ID,..[:TRACKING]: the shot's quest state is these quests in progress at that level
    MovieDialog questLog;                   // UIQuestDialog, the quest log, run from its own movie while it is loaded
    Ms2.Ui.Hud.QuestDialogController questLogSide;   // its C++ side, kept while it is closed (m_180 / m_184 and the filters)
    Ms2.Ui.UiModel questLogModel;
    int optionShot = -1;                    // --uigen-options PAGE: the shot opens the options dialog on that page, as the system menu does
    string hoverShot;                       // --uigen-hover MOVIE:a.b.c: the pointer on that child of a hosted movie (its tooltip shows)
    int msgboxShot = -1;                    // --uigen-msgbox KIND: 1 the exit box (Esc, Exit Game); with --uigen-options, 2 the reset box, 3 the unsaved changes box
    int worldMapShot = -1;                  // --worldmap STEP: the shot opens the world map (M); 1 searches "Henesys", 2 clicks the result
    int sysmenuShot = -1;                   // --uigen-sysmenu STATE: the shot opens the system menu with Esc; 1 shows the channel combo, 2 opens its list
    static DungeonRooms dungeonRooms;
    bool questFixture;                      // --questlog-fixture: the shot's quest log lists made-up quest state (QuestShotState)

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--hudshot") shotDir = args[i + 1];
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--frametime") frameCase = args[i + 1];
        if (System.Array.IndexOf(args, "--oracle") >= 0) Ms2.Avm2.Interp.UseOracle = true;
        if (frameCase != null) DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        questFixture = args.Contains("--questlog-fixture");
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--questlog") questShot = args[i + 1].Split(',').Select(int.Parse).ToArray();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--questlog-state") questState = args[i + 1];
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--uigen-sysmenu") sysmenuShot = int.Parse(args[i + 1]);
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--worldmap") worldMapShot = int.Parse(args[i + 1]);
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--uigen-options") optionShot = int.Parse(args[i + 1]);
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--uigen-msgbox") msgboxShot = int.Parse(args[i + 1]);
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--uigen-hover") hoverShot = args[i + 1];
        var dataDir = Settings.DataDir;
        if (string.IsNullOrEmpty(dataDir)) { GD.PushError("Set MS2_DATA (the client's Data folder), or fill in local.cfg."); GetTree().Quit(2); return; }
        assets = new UiAssets(new PackSet(dataDir));

        xblock = World.FieldContents.PendingXBlock ?? ArgField() ?? "02000001_tw_tria";   // as Field picks it; the field scene consumes the move
        AddChild(GD.Load<PackedScene>("res://field.tscn").Instantiate());      // the field scene, untouched
        BuildHud();
        sizeSource = GetViewport();
        sizeSource.SizeChanged += QueueRelayout;
    }

    Action restoreScale;

    /// <summary>
    /// The field draws to the whole window, as the client's back buffer is the window's client area and every movie's
    /// viewport is that render target (GFxMovie::vfunc_6 / vfunc_7). So the project's 1280 x 720 canvas stretch (which
    /// letterboxes) is off while this scene runs, and the HUD lays itself out for the window instead (BuildHud).
    /// </summary>
    public override void _EnterTree()
    {
        restoreScale = ScreenMode.UseWindowPixels(GetWindow());
    }

    /// <summary>
    /// Leaving the tree: the size handler is dropped first, because restoring the window's content scale mode emits the root
    /// viewport's size_changed at once, while this scene and its field are already on their way out.
    /// </summary>
    public override void _ExitTree()
    {
        if (sizeSource != null) { sizeSource.SizeChanged -= QueueRelayout; sizeSource = null; }
        restoreScale?.Invoke();
        restoreScale = null;
    }

    Viewport sizeSource;            // the viewport whose size_changed relays the HUD out, while connected

    void QueueRelayout()
    {
        if (relayoutQueued) return;
        relayoutQueued = true;
        CallDeferred(MethodName.Relayout);
    }

    CanvasLayer hudLayer; Node[] worldUi = System.Array.Empty<Node>();   // the HUD layer and the world-anchored UI layers
    PortraitView portrait;          // the game menu's face portrait, kept across relayouts
    bool relayoutQueued;

    /// <summary>
    /// The window was resized. The client resets the device on WM_SIZE (GFxMovieEventHandler: OnDeviceLost, then
    /// GFxService::vfunc_17 on restore), and GFxManager::ResizeMovies gives every movie the new render target size
    /// (GFxMovie::vfunc_7) and sends SceneResize, so each dialog aligns itself to the new stage. Here the HUD is built
    /// again at the new size; the dialogs run from their own movies are kept and laid out again.
    /// </summary>
    void Relayout()
    {
        relayoutQueued = false;
        if (!IsInsideTree()) return;     // queued before the scene left the tree
        questLog?.GetParent()?.RemoveChild(questLog);         // the open dialog is kept and laid out again (vfunc_18)
        worldMap?.GetParent()?.RemoveChild(worldMap);
        minimap?.GetParent()?.RemoveChild(minimap);           // so is the minimap
        questGuide?.GetParent()?.RemoveChild(questGuide);     // and the quest tracker
        foreach (var (m, _) in messengers) m.GetParent()?.RemoveChild(m);     // and the chat windows
        groupMessenger?.GetParent()?.RemoveChild(groupMessenger);
        notice?.GetParent()?.RemoveChild(notice);
        upperHud?.GetParent()?.RemoveChild(upperHud);         // and the top bar
        targetBar?.GetParent()?.RemoveChild(targetBar);       // and the target HP bar
        hudLayer?.QueueFree();
        foreach (var n in worldUi) n.QueueFree();
        BuildHud();
        if (minimap != null) { minimapSlot.AddChild(minimap); minimap.Relayout(GetWindow().Size); }
        foreach (var (m, _) in messengers) m.Relayout(GetWindow().Size);
        groupMessenger?.Relayout(GetWindow().Size);
        notice?.Relayout(GetWindow().Size);
        questLog?.Relayout(GetWindow().Size);
        worldMap?.Relayout(GetWindow().Size);
        systemMenuMovie?.Relayout(GetWindow().Size);
        optionMovie?.Relayout(GetWindow().Size);       // GFxManager::ResizeMovies, then the dialog's alignment
    }

    /// <summary>
    /// Lays the HUD out for the window. The client draws its movies into the whole back buffer: every movie's viewport is the
    /// active render target's size (GFxMovie::vfunc_6 / vfunc_7, GFxManager::ResizeMovies), one pixel larger on every side,
    /// with no letterbox and no fixed design size; the dialogs align themselves on the stage that gives (UiScale).
    /// </summary>
    void BuildHud()
    {
        var layer = hudLayer = new CanvasLayer { Layer = 10 };
        AddChild(layer);
        uiRoot = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        uiRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(uiRoot);
        var window = GetViewport().GetVisibleRect().Size;
        u = UiScale.For(window); stage = UiScale.MovieStage(window);
        layer.Offset = UiScale.MovieOrigin;                                  // the movies' viewport (GFxMovie::vfunc_7)
        var constants = new ClientConstants(assets.Packs);
        AddChild(damageEffect = new DamageEffect(assets, constants, window));                             // UIDamageEffect
        damageEffect.Hp(data.Player.HpCurrent, data.Player.HpMax);
        var nameTag = new PlayerNameTag(assets, constants, data.Player.Name, window);                     // the own PC's UINameTag
        AddChild(nameTag);
        AddChild(playerBar = new PlayerHpBar(assets, constants, nameTag, window));                        // and its UIHpBar
        playerBar.Spawn(data.Player.HpCurrent, data.Player.HpMax);
        AddChild(damageText = new DamageText(assets, constants, window));                // UIDamageText
        worldUi = new Node[] { damageEffect, nameTag, playerBar, damageText };

        HudModel();
        NoticeTab(uiRoot, constants);
        Minimap(uiRoot);
        UpperHud(uiRoot);
        TargetHpBar(uiRoot);
        HudModel();
        Chat(uiRoot);
        Quests(uiRoot);
        PlayerInfo(uiRoot);
        GameMenu(uiRoot);
        EventWidgets(uiRoot);
        SystemMenuFacts();
        MessageBoxLayer.Ensure(this);       // the client's one message box layer (CUIService::CreateAllDialogs)
        TooltipLayer.Ensure(this);          // and its one tooltip movie (GFxManager LoadTooltipMovie)
        if (questLog != null) uiRoot.AddChild(questLog);       // dialogs draw over the HUD (UIDialog m_e0: layer 4)
        if (worldMap != null) uiRoot.AddChild(worldMap);
    }

    SystemMenuController systemMenu;    // UISystemMenuDialog (0x4e21), its C++ side while the menu is loaded
    MovieDialog systemMenuMovie;        // its movie (uisystemmenudialog), from open until OnEndClose
    CanvasLayer systemMenuLayer;        // dialogs over the HUD (UIDialog m_e0: layer 4); no offset, MovieDialog places itself
    Func<string, string> commonStrings;
    Dictionary<string, string> channelGroupNames;
    int? menuMapType; string menuGroup = "";
    UiModel menuModel;

    /// <summary>OnSelectCharacter (@0x141b14600): SendRequestQuit; the game flow goes back to character select.</summary>
    public static event Action SystemMenuSelectCharacter;
    /// <summary>OnGameExit (FUN_1418fc760, uichatballoon.cpp:467): FUN_140d73bc0(0x288 s_msg_game_over, 0x17, 0, "gameexit").</summary>
    static void ShowGameExitBox(Func<string, string> common) =>
        MessageBoxQueue.ShowMessageBox(common("s_msg_game_over") ?? "", 0x17, null, "gameexit");

    /// <summary>A scripted click on the shown box's button <paramref name="n"/>.</summary>
    public void ClickMessageBox(int n) => MessageBoxLayer.Instance?.Click(n);

    public bool MessageBoxShown => MessageBoxLayer.Instance?.Shown == true;

    /// <summary>
    /// The system menu's field facts, read with the HUD: the map type (the leave entry) and the channel group (the channel
    /// rows' prefix; none when the client country is 2, DAT_143ab0248).
    /// </summary>
    void SystemMenuFacts()
    {
        if (commonStrings == null)
        {
            var strings = new GameStrings(assets.Packs, assets.Language);
            commonStrings = strings.Common();
            try { channelGroupNames = strings.Table("channelgroupname", "name"); } catch (Exception ex) { GD.PushWarning("channel group names: " + ex.Message); channelGroupNames = new(); }
        }
        FieldData fd = null;
        try { fd = FieldData.FindByXBlock(assets.Packs, xblock); } catch (Exception ex) { GD.PushWarning("system menu field data: " + ex.Message); }
        menuMapType = fd?.MapType;
        menuGroup = fd != null && channelGroupNames.TryGetValue(fd.ChannelGroupId.ToString(), out var g) ? g : "";
    }

    /// <summary>
    /// UISystemMenuDialog::vfunc_65 (OpenDialog, FUN_141924680): CloseAllDialogs, then the movie loads (LoadSwf) and the
    /// controller's OnLoadInit / OnLoadComplete fill it. Offline the server has one channel, the current one (1).
    /// </summary>
    void OpenSystemMenu()
    {
        CloseAllDialogs();
        if (!UiCode.Load(Settings.DataDir)) GD.PushWarning("UI code is not built for this data: the system menu runs interpreted");
        menuModel ??= NewModel();
        var c = new SystemMenuController
        {
            MapType = menuMapType,
            LeaveText = menuMapType is int t ? SystemMenuController.LeaveButtonText(t, commonStrings) : null,
            ChannelWord = commonStrings("s_word_ch") ?? "",
            ChannelGroupName = menuGroup,
            CurrentChannel = 1,
            Channels = new[] { new ChannelRow(1) },
        };
        c.SelectCharacter += () => SystemMenuSelectCharacter?.Invoke();
        c.GameExit += () => ShowGameExitBox(commonStrings);
        c.Leave += t => GD.Print($"system menu: leave field (map type {t}): OnLeaveFieldClick is not ported");
        c.OpenGameOption += OpenGameOption;
        if (systemMenuLayer == null) AddChild(systemMenuLayer = new CanvasLayer { Layer = 11 });
        var m = new MovieDialog(assets, c, menuModel, GetWindow().Size);
        c.Unloaded += () =>
        {
            m.Host.Unload();
            m.QueueFree();
            if (systemMenuMovie == m) { systemMenuMovie = null; systemMenu = null; }
        };
        systemMenu = c; systemMenuMovie = m;
        systemMenuLayer.AddChild(m);
    }

    /// <summary>
    /// Esc in the field, FUN_1418fcb70 (uichatballoon.cpp:523-555), as far as this HUD has its parts: tooltips, the cutscene
    /// movie, the guide's text tip and fishing are not in it, a focused text field keeps Esc (the chat input takes it in
    /// _Input), and an NPC talk takes it itself (FUN_141275080 is 0 while talking). With a closable dialog open
    /// (FUN_141274ff0, unit_141274d10_uihelpers.cpp) Esc closes all of them when the interface option EnableCloseAllDlg is
    /// on and is otherwise left; with none it toggles the system menu: ToggleDialog(0x4e21) = FUN_1419246f0, open when
    /// vfunc_55 (closed and unloaded), else vfunc_67 (close).
    /// </summary>
    public void Escape()
    {
        if (assets == null) return;
        if (optionDialog != null && optionDialog.OnKey(true)) return;      // UIGameOptionDialog::vfunc_13 @0x141942b10 takes Esc
        if (AnyClosableDialogOpen())
        {
            if (GameOptions.Current.InterfaceOption.EnableCloseAllDlg) CloseAllDialogs();
            return;
        }
        if (systemMenu == null || systemMenu.IsClosed) OpenSystemMenu();
        else systemMenu.Close();
    }

    /// <summary>FUN_141274ff0: a closable dialog (one without flag 1, so not the system menu) is open: of this HUD's
    /// dialogs, the quest log.</summary>
    bool AnyClosableDialogOpen() => (questLog != null && questLogSide?.IsClosed == false) || WorldMapOpen || optionHost?.Vfunc55 == false
                                    || inventory?.IsClosed == false || myInfo?.IsClosed == false;

    /// <summary>CUIService::CloseAllDialogs: closes every closable dialog (vfunc_67).</summary>
    void CloseAllDialogs()
    {
        if (questLog != null && questLogSide?.IsClosed == false) questLogSide.Close();
        if (WorldMapOpen) worldMapSide.Close();
        if (optionHost?.Vfunc55 == false) optionHost.Movie.Close();
        if (inventory?.IsClosed == false) inventory.Close();
        if (myInfo?.IsClosed == false) myInfo.Close();
    }

    Ms2.Formats.Options.GameOptionDialog optionDialog;      // UIGameOptionDialog (0x4ef3), its C++ side, kept between opens as the client keeps it
    GameOptionHost optionHost;
    MovieDialog optionMovie;            // its movie (uigameoptiondialog), from open until OnEndClose

    /// <summary>
    /// UISystemMenuDialog::OpenGameOptionDialog @0x141b14560 (page 1 or 6) and FUN_1418fc760's OnGameOption (page 0, no
    /// SetTab): SetTab, then CUIService::OpenDialog(0x4ef3) = FUN_141924680, which opens only a closed dialog (vfunc_65
    /// @0x141924a10 checks vfunc_55).
    /// </summary>
    void OpenGameOption(int page)
    {
        if (optionDialog == null)
        {
            optionHost = new GameOptionHost(this, assets.Packs, commonStrings);
            if (hudModel?.Keys is { } k && k != KeyTable.Empty) optionHost.Keys = k;
            optionDialog = optionHost.Dialog = new Ms2.Formats.Options.GameOptionDialog(optionHost);
        }
        if (page != 0) optionDialog.SetTab(page);
        if (!optionHost.Vfunc55) return;
        if (!UiCode.Load(Settings.DataDir)) GD.PushWarning("UI code is not built for this data: the options dialog runs interpreted");
        menuModel ??= NewModel();
        if (systemMenuLayer == null) AddChild(systemMenuLayer = new CanvasLayer { Layer = 11 });
        var c = new GameOptionController { Dialog = optionDialog };
        optionHost.Movie = c;
        var m = new MovieDialog(assets, c, menuModel, GetWindow().Size);
        c.Unloaded += () =>
        {
            m.Host.Unload();
            m.QueueFree();
            if (optionMovie == m) { optionMovie = null; optionHost.Movie = null; }
        };
        optionMovie = m;
        systemMenuLayer.AddChild(m);
    }

    public bool SystemMenuOpen => systemMenu?.IsOpen == true;

    /// <summary>A scripted click on one of the system menu movie's named children (the centre of its bounds), for proof
    /// runs; <paramref name="row"/> clicks that row of a list's renderers instead.</summary>
    public void ClickSystemMenu(string name, int row = -1)
    {
        if (systemMenuMovie == null) return;
        var h = systemMenuMovie.Host;
        object target = h.Child(name);
        if (target != null && row >= 0) target = h.Invoke(target, "getRendererAt", row);
        if (target is not Ms2.Avm2.Flash.AsDisplayObject d) { GD.PushWarning($"system menu: no {name} {row}"); return; }
        var b = d.GetBounds(null);
        systemMenuMovie.Player.Click(b.X + b.W / 2, b.Y + b.H / 2);
        systemMenuMovie.Player.MouseMove(0, 0);       // the pointer leaves the menu, as after a hand click (no hover state in a shot)
    }

    /// <summary>Places a movie at a position in stage units; <paramref name="factor"/> is a per-dialog size the client applies on top of the UI scale.</summary>
    MovieClipNode Place(Control parent, string movie, float factor, Vector2 stagePos)
    {
        var m = new UiMovie(assets, movie);
        float s = u * factor;
        var root = m.BuildRoot(s);
        root.Scale = new Vector2(s, s); root.Position = stagePos * u;
        var holder = new Node2D(); holder.AddChild(root); parent.AddChild(holder);
        return root;
    }

    /// <summary>
    /// ASDisplayObject::vfunc_2(mode, dx, dy) on the dialog instance <paramref name="body"/> in a built root: the dialog's
    /// registration point goes to the aligned spot, with <paramref name="size"/> its AS3 width and height (truncated).
    /// </summary>
    void AlignDialog(MovieClipNode root, MovieClipNode body, int mode, Vector2 size, float dx, float dy)
    {
        var reg = UiLayout.Align(mode, stage, new Vector2((int)size.X, (int)size.Y), dx, dy);
        root.Position = (reg - body.Position) * u;
    }

    /// <summary>The dialog instance a HUD movie's root timeline places (its first clip).</summary>
    static MovieClipNode DialogBody(MovieClipNode root) => root.Children.Select(c => c.Node).OfType<MovieClipNode>().FirstOrDefault();

    static void Hide(MovieClipNode root, params string[] names) { foreach (var n in names) root.HideNamed(n); }

    // ---- top bar: uiupperhuddialog run by its own ActionScript, UIUpperHudDialog's C++ side as UpperHudController ------

    MovieDialog upperHud;
    Ms2.Ui.Hud.UpperHudController upperHudSide;

    /// <summary>
    /// The clock, mesos, merats and the buttons at the top (UIUpperHudDialog, uiupperhuddialog.cpp). The mesos and merats are
    /// shown only; the features the controller tests are the client's (feature.xml by name). Kept across relayouts.
    /// </summary>
    void UpperHud(Control ui)
    {
        if (upperHud != null) { ui.AddChild(upperHud); upperHud.Relayout(GetWindow().Size); return; }
        var model = new UiModel(new GameTables(assets.Packs, "en"));
        model.Features.Clear();
        foreach (int id in new[] { Ms2.Ui.Feature.SearchTooltip, Ms2.Ui.Feature.InputMode, Ms2.Ui.Feature.GamePad, Ms2.Ui.Feature.VersionText, Ms2.Ui.Feature.Merat,
            Ms2.Ui.Feature.PvPRankingDuelMode, Ms2.Ui.Feature.ItemSocketSystem, Ms2.Ui.Feature.HabiToken, Ms2.Ui.Feature.ReverseRaid01,
            Ms2.Ui.Feature.Mentoring, Ms2.Ui.Feature.MesoMarketToken, Ms2.Ui.Feature.RedMerat })
            if (id <= Ms2.Ui.FeatureTable.Names.Length && Ms2.Ui.LobbyFeatures.IsEnabledByName(assets.Packs, Ms2.Ui.FeatureTable.Names[id - 1])
                && !data.FeaturesOff.Contains(Ms2.Ui.FeatureTable.Names[id - 1]))
                model.Features.Add(id);
        model.Player.Mesos = data.Mesos;
        model.Player.Merats = data.Merets;
        // OnServerEnter (scenestategame.cpp:2087-2104): 4 merat wallet kinds (FUN_1409304d0), the red merat balance, then
        // the token currencies 4, 5, 6, 7, 0xc, 0xd, 0xe, 0xf, 0x10 (DAT_143ab28b0..28f0)
        if (data.Wallet is { Length: >= 14 } w)
        {
            System.Array.Copy(w, model.Player.MeratWallet, 4);
            model.Player.RedMerat = w[4];
            int[] tokens = { 4, 5, 6, 7, 0xc, 0xd, 0xe, 0xf, 0x10 };
            for (int i = 0; i < tokens.Length; i++) model.Player.Tokens[tokens[i]] = w[5 + i];
        }
        upperHudSide = new Ms2.Ui.Hud.UpperHudController();
        upperHud = new MovieDialog(assets, upperHudSide, model, GetWindow().Size);
        ui.AddChild(upperHud);
    }

    /// <summary>The target HP bar (UIUpperHpBar, uiupperhpbar.cpp), run by its movie; kept across relayouts.</summary>
    void TargetHpBar(Control ui)
    {
        if (targetBar != null) { ui.AddChild(targetBar); targetBar.Relayout(GetWindow().Size); return; }
        TargetBar = new Ms2.Ui.Hud.UpperHpBarController();
        targetBar = new MovieDialog(assets, TargetBar, NewModel(), GetWindow().Size);
        ui.AddChild(targetBar);
    }

    // ---- minimap: uiminimapdialog run by its own ActionScript, UIMinimapDialog's C++ side as MinimapController ----------

    Control minimapSlot;                    // the minimap's place among the HUD movies
    MovieDialog minimap;
    Ms2.Ui.Hud.MinimapController minimapSide;

    void Minimap(Control ui) => ui.AddChild(minimapSlot = new Control { Name = "uiminimapdialog", MouseFilter = Control.MouseFilterEnum.Ignore });

    /// <summary>
    /// The minimap opens once the field's contents exist (the client opens it on field entry, after the load): the field's
    /// minimap image and camera (mapxblock/&lt;xblock&gt;.xml &lt;minimap&gt;), the marks BuildEntities sends (uiminimapentity.cpp:2900:
    /// the npcs, then the portals, the taxi, the named interact objects and the map objects), and the local player.
    /// </summary>
    void OpenMinimap(World.FieldContents fc)
    {
        MinimapInfo info = null;
        try { info = MinimapInfo.Parse(System.Xml.Linq.XDocument.Parse(System.Text.Encoding.UTF8.GetString(assets.Packs.Read("Xml", $"mapxblock/{xblock}.xml")))); }
        catch (Exception e) { GD.PushWarning("minimap: " + e.Message); }
        var model = NewModel();
        model.Field.Name = MapName();
        try { model.Field.MapLevel = FieldData.FindByXBlock(assets.Packs, xblock)?.MapLevel; } catch (Exception e) { GD.PushWarning("minimap field data: " + e.Message); }
        model.Field.Channel = (int)data.Player.Channel;
        model.Field.Channels = new[] { (int)data.Player.Channel };
        var path = (string)assets.Table("table/commonresource.xml")?.Root?.Element("DEFAULT_PROFILE_IMAGE_PATH")?.Attribute("value");
        var c = minimapSide = new Ms2.Ui.Hud.MinimapController
        {
            ImageName = info?.ImageEntry,
            Viewport = info,
            PlayerImage = path ?? "",       // FUN_141028be0: no UGC profile image -> CommonResource m_strDefaultProfileImagePath
            LocalPlayer = LocalMinimapPlayer,
        };
        c.WorldMap += ToggleWorldMap;       // OnWorldMap (uiminimapentity.cpp:1253): ToggleDialog(0x4e42)
        try { MinimapMarks(fc, c.Marks); }
        catch (Exception e) { GD.PushWarning("minimap marks: " + e.Message); }
        minimap = new MovieDialog(assets, c, model, GetWindow().Size);
        minimapSlot.AddChild(minimap);
    }

    /// <summary>
    /// The field's proxies and places for BuildEntities: AddProxy's npcs (UpdateNpc: SetFromNpcData), the portals
    /// (UpdatePortal / SetFromPortal, with their guide arrows), BuildTaxi (the first MS2TaxiStation world object),
    /// BuildNamed (MS2InteractObject with the server's state) and BuildMapObjects (MS2Minimap). The icons and layers are
    /// <see cref="MinimapIcons"/>'s ports of UIMinimapEntity's.
    /// </summary>
    void MinimapMarks(World.FieldContents fc, List<Ms2.Ui.Hud.MinimapController.Mark> marks)
    {
        var icons = new MinimapIcons(assets.Packs, World.FieldContents.Npcs(assets.Packs), new GameStrings(assets.Packs, "en"));
        foreach (var n in fc.SpawnedNpcs)
        {
            var e = MinimapIcons.Npc(n.Row);
            if (!e.Visible || !n.Root.Visible) continue;
            var npc = n;
            marks.Add(new() { Entity = e, Position = () => World.FieldPortals.ToMs2(npc.Position), Moves = true });
        }
        foreach (var p in fc.Portals.All)
        {
            var e = icons.Portal(fc.FieldId, p.TargetField, (int)p.Entity.Number("PortalType"), p.Visible, p.Entity.Bool("MinimapIconVisible", true));
            if (e == null || !e.Visible) continue;
            var at = p.Position;
            marks.Add(new() { Entity = e, Position = () => at, ShowGuide = true });
        }
        static bool IsModel(FieldEntity e, string model) =>
            string.Equals(e.ModelName, model, StringComparison.OrdinalIgnoreCase) || e.HasMixin(model);
        void Place(MinimapIcons.Entity e, Vec3 at) { if (e != null) marks.Add(new() { Entity = e, Position = () => at }); }
        if (icons.HasTaxi(fc.FieldId) && fc.Map?.Entities.FirstOrDefault(e => IsModel(e, "MS2TaxiStation")) is { } taxi)
        {
            int bestLevel = (int)new ClientConstants(assets.Packs).Float("TaxiCallBestDriverLevel", 25);
            Place(MinimapIcons.Taxi(data.FoundTaxis.Contains(fc.FieldId), data.Player.Level >= bestLevel), taxi.Position);
        }
        foreach (var e in fc.Map?.Entities.Where(e => IsModel(e, "MS2InteractObject")) ?? Enumerable.Empty<FieldEntity>())
        {
            int id = (int)e.Number("interactID");
            var (icon, type) = id > 0 ? icons.InteractRow(assets.Packs, id) : ("", 0);
            if (MinimapIcons.Named(icon, type, data.InteractState) is { Visible: true } named) Place(named, e.Position);
        }
        foreach (var e in fc.Map?.Entities.Where(e => IsModel(e, "MS2Minimap")) ?? Enumerable.Empty<FieldEntity>())
            Place(MinimapIcons.MapObject(e.Get("MinimapIconFrame")), e.Position);
    }

    /// <summary>The local player for RefreshLocalPlayer: its position, GetPlayerHeading and dead state (none offline).</summary>
    (Vec3, float, bool) LocalMinimapPlayer()
    {
        if (player == null) player = FindPlayer(this);
        if (player == null) return (default, 0, false);
        float heading = 0;
        field ??= player.GetParent() as Field ?? FindField(this);
        if (field != null && player.Visual != null) heading = PlayerHeading(Mathf.RadToDeg(field.CameraYaw), player.FacingRotZ);
        return (ToMs2(player.GlobalPosition), heading, false);
    }

    /// <summary>
    /// UINoticeDialog (0x4ee9), run from its movie with <see cref="NoticeDialogController"/>: the field's HUD opens it when
    /// the NoticeDialogUrl constant is set (CUIService, uiservice.cpp:742; Define default "", unit_1402ab440_constantsblocks
    /// :1510). Loaded once and kept across relayouts.
    /// </summary>
    void NoticeTab(Control ui, ClientConstants constants)
    {
        if (string.IsNullOrEmpty(constants.String("NoticeDialogUrl", ""))) return;
        notice ??= new MovieDialog(assets, new NoticeDialogController(), hudModel, GetWindow().Size);
        ui.AddChild(notice);
    }

    /// <summary>The xblock of the field this scene runs: a portal move's target (the scene reloads per move), else --field.</summary>
    string xblock;

    static string ArgField()
    {
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--field") return args[i + 1];
        return null;
    }

    /// <summary>
    /// The minimap's field name (UIMinimapDialog::BuildUIData "fieldname", CubeMapInformationService::FUN_1410c3330): a house's
    /// name, else WorldService m_290, which the field load sets to the mapname string of the map id
    /// (CStringTable::FUN_141811980(3, mapId), worldservice_1.cpp:3317). A map id the table lacks, or a table that cannot be
    /// read, gives an empty string (stringtable.cpp FUN_141811980: the failed Lookup leaves the text empty).
    /// </summary>
    string MapName()
    {
        try
        {
            var fd = FieldData.FindByXBlock(assets.Packs, xblock);
            if (fd != null && new GameStrings(assets.Packs, "en").Table("mapname", "name") is { } names
                && (names.TryGetValue(fd.Id.ToString(), out var n) || names.TryGetValue(fd.Id.ToString("D8"), out n)))
                return n;
        }
        catch (Exception e) { GD.PushWarning("map name: " + e.Message); }
        return "";
    }

    Player FindPlayer(Node n)
    {
        if (n is Player p) return p;
        foreach (var c in n.GetChildren()) { var f = FindPlayer(c); if (f != null) return f; }
        return null;
    }

    static Vec3 ToMs2(Vector3 g) => new(g.X, -g.Z, g.Y);

    /// <summary>
    /// UIMinimapDialog::GetPlayerHeading, the rotation RefreshPlayer gives the player entity's m_arrow (its art points east, to
    /// the right of the dot): FUN_14166dad0((0, -1, 0), side) - vfunc_20().z + 90, with side = -dir x Z and dir the camera
    /// mode's rotation (MyPC::vfunc_67 / Ridee: m_posBF4 = camera m_setting.m_18) rebuilt and flattened (PCBase::vfunc_67).
    /// For the target camera that is the horizontal view direction (-sin yaw, -cos yaw), which works out to
    /// 270 - yaw - rot.z: 270 (up) while the player faces away from the camera, turning with the camera. rot.z is the
    /// ridee's while riding.
    /// </summary>
    static float PlayerHeading(float cameraYawDeg, float rotZ) => Mathf.PosMod(270f - cameraYawDeg - rotZ, 360f);

    Field field;
    static Field FindField(Node n)
    {
        if (n is Field f) return f;
        foreach (var c in n.GetChildren()) if (FindField(c) is { } r) return r;
        return null;
    }

    // ---- chat: uimessengerdialog and uigroupmessengerbrowser, run from their movies ------------------------------------

    /// <summary>
    /// UIMessengerBrowser::LoadDefaultWindows (uimessenger.cpp:3149): a chat window (UIMessenger) per
    /// table/default/DefaultMessenger&lt;i&gt;.xml, named messengerdialog&lt;i&gt;.xml, first load; then UIGroupMessengerBrowser,
    /// the group chat bar. The saved windows of the character folder (LoadWindows :3689) are not read yet. Lines typed
    /// are not shown: SendChat (:4208) only sends the command and the line arrives with the chat packet (0x1d), which the
    /// live session does not carry yet.
    /// </summary>
    void Chat(Control ui)
    {
        var window = GetWindow().Size;
        if (messengers.Count == 0)
        {
            var docs = hudModel.Tables.DefaultMessengers;
            for (int i = 0; i < docs.Count; i++)
            {
                var c = new MessengerController(docs[i], $"messengerdialog{i}.xml", true) { IsKeyDown = DikHeld };
                messengers.Add((new MovieDialog(assets, c, hudModel, window), c));
            }
            groupMessenger = new MovieDialog(assets, new GroupMessengerBrowserController(), hudModel, window);
        }
        foreach (var (m, _) in messengers) ui.AddChild(m);
        ui.AddChild(groupMessenger);
    }

    /// <summary>Unk_MsgKeys::FUN_14116d7a0, a key held now, for the DIK codes the chat asks about (Tab).</summary>
    static bool DikHeld(int dik) => dik == MessengerController.DikTab && Input.IsPhysicalKeyPressed(Key.Tab);

    /// <summary>The scene's keys: the chat windows first, then the dialogs.</summary>
    public override void _Input(InputEvent e)
    {
        if (ChatKeys(e)) { GetViewport().SetInputAsHandled(); return; }
        if (DialogKeys(e)) GetViewport().SetInputAsHandled();
    }

    /// <summary>
    /// UIMessenger::vfunc_13 (uimessenger.cpp:810), the window's keys while it is shown: Return (DIK_RETURN) and Escape go
    /// to the controller, which returns whether it used them (an unfocused input leaves Escape to the rest). While its
    /// input has the focus the other keys go to that movie, as Scaleform sends keys to the movie with the focus.
    /// </summary>
    bool ChatKeys(InputEvent e)
    {
        if (e is not InputEventKey k) return false;
        foreach (var (m, c) in messengers)
        {
            if (!m.Loaded || !m.IsVisibleInTree()) continue;
            if (k.Pressed && !k.Echo && !UiFocus.IsTyping)
            {
                if ((k.Keycode == Key.Enter && c.OnKey(MessengerController.DikReturn, false))
                    || (k.Keycode == Key.Escape && c.OnKey(DikEscape, true)))
                    return true;
            }
            if (!c.InputActive) continue;
            if (k.Keycode is Key.Enter or Key.Escape) return true;
            uint code = As3View.FlashKeyCode(k.Keycode);
            if (code == 0 && k.Unicode == 0) return true;
            m.Player.Key(code, k.Pressed ? (uint)k.Unicode : 0, k.Pressed, k.ShiftPressed, k.CtrlPressed);
            return true;
        }
        return false;
    }

    const int DikEscape = 1;

    /// <summary>
    /// The scene's action keys and an open dialog's Escape, while nothing takes typing. UISceneGame::vfunc_13 runs the
    /// action keys (IsActionKeyPressed 0x61a87, the quest log, action 400007: DIK 38, L; the inventory, action 400001: DIK 23, I;
    /// in table/default/defaultkey.xml) through CKeyTable::FUN_14142b300, which toggles the dialog (keytable.cpp: case 400007,
    /// ToggleDialog(0x4e3e); case 400001, ToggleDialog(0x4e2d)).
    /// UIDialog::vfunc_13: Escape closes an open dialog (vfunc_69 -> vfunc_67).
    /// </summary>
    bool DialogKeys(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k || UiFocus.IsTyping) return false;
        if (k.PhysicalKeycode == Key.L) { ToggleQuestLog(); return true; }       // a DirectInput key code is a physical key
        if (k.PhysicalKeycode == Key.M) { ToggleWorldMap(); return true; }      // action 400006, DIK 50 (keytable.cpp:744)
        if (WorldMapOpen && k.Keycode == Key.Enter && worldMapSide.EnterKey()) return true;
        if (k.Keycode == Key.Escape && WorldMapOpen) { worldMapSide.Close(); return true; }
        if (k.PhysicalKeycode == Key.I) { ToggleInventory(); return true; }     // action 400001 (DIK 23, I): ToggleDialog(0x4e2d)
        if (k.PhysicalKeycode == Key.P) { ToggleMyInfo(); return true; }        // action 400002 (DIK 25, P): ToggleDialog(0x2711), keytable.cpp:738
        bool open = questLog != null && questLogSide?.IsClosed == false;
        if (k.Keycode == Key.Escape && open) { questLogSide.Close(); return true; }
        if (k.Keycode == Key.Escape && inventory?.IsClosed == false) { inventory.Close(); return true; }
        if (k.Keycode == Key.Escape && myInfo?.IsClosed == false) { myInfo.Close(); return true; }
        return false;
    }


    // ---- quest guide: uiquestguidedialog run by its own ActionScript, UIQuestGuideDialog's C++ side as QuestGuideController --

    MovieDialog questGuide;
    Ms2.Ui.Hud.QuestGuideController questGuideSide;

    /// <summary>
    /// The quest tracker: its movie with UIQuestGuideDialog's C++ side, over the character's quest state (the quest log's
    /// source, with the shot's --questlog-state) and this field's missions. It is kept across a window relayout.
    /// </summary>
    void Quests(Control ui)
    {
        if (questGuide != null) { ui.AddChild(questGuide); questGuide.Relayout(GetWindow().Size); return; }
        var log = QuestLogSource();
        if (log == null) return;
        if (questState != null)
        {
            var parts = questState.Split(':');
            log.State = new QuestShotState(questTable, int.Parse(parts[0]), parts[1].Split(',').Select(int.Parse), parts.Length > 2 ? int.Parse(parts[2]) : 0);
        }
        int fieldId = FieldData.FindByXBlock(assets.Packs, xblock)?.Id ?? 0;
        try
        {
            fieldMissions ??= FieldMissions.Load(assets.Packs, new GameStrings(assets.Packs, "en").Languages, data.Locale, f => !data.FeaturesOff.Contains(f));
        }
        catch (Exception e) { GD.PushWarning("field missions: " + e.Message); }
        var meta = fieldMeta;
        var constants = new ClientConstants(assets.Packs);
        var inputs = new QuestButtonInputs
        {
            FieldId = fieldId, FieldExists = f => f == fieldId, LocalPlayerAlive = true, HasFieldData = true,
            DirectQuestFindParty = Ms2.Ui.LobbyFeatures.IsEnabled(assets.Packs, Ms2.Ui.FeatureTable.Names[0xdc - 1]),
        };
        questGuideSide = new Ms2.Ui.Hud.QuestGuideController
        {
            Table = questTable,
            State = log.State,
            Missions = fieldMissions,
            ParentField = f => meta.ParentFields.TryGetValue(f, out int p) ? p : null,
            FieldId = fieldId,
            GotoButtons = (d, q) => QuestCommon.Buttons(d, q, inputs, meta, log.State, dungeonRooms ??= DungeonRooms.Load(assets.Packs, questTable.Available)),
            FeatureOn = id => id >= 1 && id <= Ms2.Ui.FeatureTable.Names.Length && Ms2.Ui.LobbyFeatures.IsEnabledByName(assets.Packs, Ms2.Ui.FeatureTable.Names[id - 1]),
            PageCount = (int)constants.Float("QuestGuidePageCount", 4),
            MaxCount = (int)constants.Float("QuestGuideMaxCount", 40),
        };
        var model = NewModel();
        questGuide = new MovieDialog(assets, questGuideSide, model, GetWindow().Size);
        ui.AddChild(questGuide);
    }

    /// <summary>
    /// The quest log key (CUIService::ToggleDialog -> UIDialog::FUN_1419246f0, uidialog.cpp:456): a closed log (vfunc_55)
    /// opens, else it closes (vfunc_67), also while its close tween runs.
    /// </summary>
    void ToggleQuestLog()
    {
        if (questLog == null || questLogSide == null || questLogSide.IsClosed) OpenQuestLog();
        else questLogSide.Close();
    }

    /// <summary>
    /// Opens the quest log: its movie (uiquestdialog) runs its own ActionScript (the translated code when it is built for this
    /// data, else the interpreter) with UIQuestDialog's C++ side (QuestDialogController), which is kept between opens. The
    /// movie is dropped when its close tween ends (OnEndClose -> Unload).
    /// </summary>
    void OpenQuestLog()
    {
        if (questLog != null) { questLog.Host.Unload(); questLog.QueueFree(); questLog = null; }
        if (questLogSide == null)
        {
            if (!UiCode.Load(Settings.DataDir)) GD.PushWarning("UI code is not built for this data: the quest log runs interpreted");
            var log = QuestLogSource();
            if (log == null) return;
            if (questState != null)
            {
                var parts = questState.Split(':');
                log.State = new QuestShotState(questTable, int.Parse(parts[0]), parts[1].Split(',').Select(int.Parse), parts.Length > 2 ? int.Parse(parts[2]) : 0);
            }
            // CFeatureManager by id for the bold words' dictionary links (feature 0x2f)
            questTable.Words.FeatureEnabled = id => id >= 1 && id <= Ms2.Ui.FeatureTable.Names.Length
                                                    && Ms2.Ui.LobbyFeatures.IsEnabledByName(assets.Packs, Ms2.Ui.FeatureTable.Names[id - 1]);
            questLogModel = NewModel();
            var fieldId = FieldData.FindByXBlock(assets.Packs, xblock)?.Id ?? 0;
            questLogSide = new Ms2.Ui.Hud.QuestDialogController
            {
                Log = log,
                Detail = new QuestDetailContext
                {
                    FieldId = fieldId,
                    LocalTm = Ms2.Net.MsTime.LocalTm,
                    Map = QuestMapGraph.From(fieldMeta),
                    // offline: the player is alive in this field, no party search, the server time unknown (no end time line)
                    Buttons = (d, q) => QuestCommon.Buttons(d, q, new QuestButtonInputs
                    {
                        FieldId = fieldId, FieldExists = f => f == fieldId, LocalPlayerAlive = true, HasFieldData = true,
                        DirectQuestFindParty = Ms2.Ui.LobbyFeatures.IsEnabled(assets.Packs, Ms2.Ui.FeatureTable.Names[0xdc - 1]),
                    }, fieldMeta, log.State, dungeonRooms ??= DungeonRooms.Load(assets.Packs, questTable.Available)),
                },
            };
            if (questShot != null) (questLogSide.OpenTab, questLogSide.OpenSubTab) = (questShot[0], questShot[1]);
            questLogSide.Unloaded += () => { questLog?.Host.Unload(); questLog?.QueueFree(); questLog = null; };
        }
        var t = System.Diagnostics.Stopwatch.StartNew();
        questLog = new MovieDialog(assets, questLogSide, questLogModel, GetWindow().Size);
        GD.Print($"quest log: load={questLog.Loaded} in {t.ElapsedMilliseconds} ms, compiled bodies {Ms2.Avm2.GeneratedCode.Count}");
        uiRoot.AddChild(questLog);
    }

    /// <summary>A click on the quest's row (the row the client's getIndex gives, ScrollToQuest), as the player selects it.</summary>
    void ClickQuest(int id)
    {
        if (questLog == null) return;
        var h = questLog.Host;
        int index = Ms2.Avm2.Rt.ToInt32(h.Invoke("getIndex", (uint)id));
        var list = h.Child("m_questList");
        if (index < 0 || list == null) { GD.PushWarning($"quest {id} is not in the list"); return; }
        int scroll = Ms2.Avm2.Rt.ToInt32(h.Get(list, "scrollPosition"));
        if (h.Invoke(list, "getRendererAt", index - scroll) is not Ms2.Avm2.Flash.AsDisplayObject r) { GD.PushWarning("no renderer for row " + index); return; }
        var b = r.GetBounds(null);
        questLog.Player.Click(b.X + b.W / 2, b.Y + b.H / 2);
        questLog.Step(10);
    }

    static QuestTable questTable;           // the quest log's tables, read when the log first lists (QuestDataTable, CQuestUIData)
    static Dictionary<int, (int Continent, int Region)> fieldCodes;

    /// <summary>
    /// The quest log's data: the quest tables and the character's quest state. The state comes from the server (the quest
    /// packets) of a live session (<see cref="Ms2Godot.Net.LiveQuestState"/>); offline the character has no quest records
    /// and can accept nothing (<see cref="NoQuestState"/>), so every list is empty. A shot with --questlog-fixture lists made-up state.
    /// </summary>
    QuestLog QuestLogSource()
    {
        try
        {
            bool featureOn(string f) => !data.FeaturesOff.Contains(f);
            questTable ??= QuestTable.Load(assets.Packs, new GameStrings(assets.Packs, "en").Languages, data.Locale, featureOn);
            fieldMeta ??= FieldMeta.Load(assets.Packs);
            fieldCodes ??= QuestTable.FieldCodes(assets.Table("table/fielddata.xml"), questTable.Available);
            var live = Ms2Godot.Net.LiveSession.Instance;
            var meta = fieldMeta;
            IQuestState state = questFixture ? new QuestShotState(questTable, data.Player.Level)
                : live?.GameSession != null
                    ? new Ms2Godot.Net.LiveQuestState(live.Quests, questTable, live.GameSession, live.CharacterId, data.Player.Level,
                                                      live.FieldId, f => meta.ParentFields.TryGetValue(f, out int p) ? p : null)
                    : new NoQuestState(data.Player.Level);
            return new QuestLog(questTable, state) { Meta = fieldMeta, FieldCodes = fieldCodes };
        }
        catch (Exception e) { GD.PushWarning("quest log: " + e.Message); return null; }
    }

    List<MovieDialog> eventWidgets;     // the event widgets opened at field entry, kept across a window relayout

    /// <summary>
    /// UIEventWidgetDialog::FUN_140c59350 (uiservice.cpp:777), at field entry: each event widget whose event is on
    /// (vfunc_84) opens. The level-up package reads the quest tables and the character's quest state; the burning event's
    /// contexts are not fed, so it stays closed.
    /// </summary>
    void EventWidgets(Control ui)
    {
        if (eventWidgets != null)
        {
            foreach (var w in eventWidgets) { ui.AddChild(w); w.Relayout(GetWindow().Size); }
            return;
        }
        eventWidgets = new List<MovieDialog>();
        var log = QuestLogSource();
        var widgets = new List<Ms2.Ui.Hud.EventWidgetController> { new Ms2.Ui.Hud.BurningEventWidgetController(hudModel) };
        if (log != null) widgets.Insert(0, new Ms2.Ui.Hud.LevelUpPackageWidgetController(hudModel) { Table = questTable, State = log.State });
        foreach (var c in widgets)
        {
            if (!c.ShouldOpen()) continue;
            var m = new MovieDialog(assets, c, hudModel, GetWindow().Size);
            c.Unloaded += () => { eventWidgets.Remove(m); m.Host.Unload(); m.QueueFree(); };
            eventWidgets.Add(m);
            ui.AddChild(m);
        }
    }

    static FieldMissions fieldMissions;     // the quest and description tables, read once (6,780 quest files)
    static FieldMeta fieldMeta;


    // ---- world map: uiworldmapdialog run by its own ActionScript, UIWorldMapDialog's C++ side as WorldMapController -----

    MovieDialog worldMap;
    Ms2.Ui.Hud.WorldMapController worldMapSide;     // kept between opens, as the client keeps the dialog (the island indices)
    static Ms2.Ui.WorldMap.WorldMapTable worldMapTable;    // WorldMapManager, read once

    bool WorldMapOpen => worldMap != null && worldMapSide?.IsClosed == false;

    /// <summary>CUIService::ToggleDialog(0x4e42): M (action 400006), the game menu's entry and the minimap's button.</summary>
    void ToggleWorldMap()
    {
        if (WorldMapOpen) worldMapSide.Close();
        else OpenWorldMap();
    }

    /// <summary>
    /// Opens the world map: its movie with UIWorldMapDialog's C++ side over WorldMapManager's tables (worldmap.xml and its
    /// files, fielddata, the map names). Offline there is no server: the icon request is answered with no icons and every
    /// field but this one shows sealed (the visited fields are the server's).
    /// </summary>
    void OpenWorldMap()
    {
        if (worldMap != null) { worldMap.Host.Unload(); worldMap.QueueFree(); worldMap = null; }
        var model = NewModel();
        if (worldMapSide == null)
        {
            fieldMeta ??= FieldMeta.Load(assets.Packs);
            var meta = fieldMeta;
            try
            {
                worldMapTable ??= Ms2.Ui.WorldMap.WorldMapTable.Load(assets.Packs, id => model.Tables.MapName(id),
                    id => meta.ParentFields.TryGetValue(id, out int p) ? p : null);
            }
            catch (Exception e) { GD.PushWarning("world map: " + e.Message); return; }
            int fieldId = FieldData.FindByXBlock(assets.Packs, xblock)?.Id ?? 0;
            worldMapSide = new Ms2.Ui.Hud.WorldMapController
            {
                Table = worldMapTable,
                Meta = meta,
                FieldId = fieldId,
                PlayerName = data.Player.Name,
                PlayerLevel = data.Player.Level,
                JobGroupId = data.Player.Job <= 1 ? 10 : (data.Player.Job / 10 + 1) * 10,     // the job group: sub job / 10 (HudModel)
                VisitedFields = new[] { (uint)fieldId },
                Npcs = World.FieldContents.Npcs(assets.Packs),
                Rooms = dungeonRooms ??= DungeonRooms.Load(assets.Packs, e => Ms2.Ui.FeatureElements.IsAvailable(assets.Packs, e)),
            };
            worldMapSide.Unloaded += () => { worldMap?.Host.Unload(); worldMap?.QueueFree(); worldMap = null; };
        }
        worldMap = new MovieDialog(assets, worldMapSide, model, GetWindow().Size);
        uiRoot.AddChild(worldMap);
    }


    // ---- HP/SP orb, skill slots and the EXP bar: their movies --------------------------------------------------------

    UiModel hudModel;
    AlignWidgetManager alignWidgets;
    MovieDialog playerInfoMovie, expBarMovie, adventureBarMovie;

    /// <summary>
    /// A model for the field's movies: the tables, and the features as the client runs them (CFeatureManager by id and by
    /// name: table/feature.xml within the Live server level, <see cref="Ms2.Ui.LobbyFeatures"/>).
    /// </summary>
    UiModel NewModel() => new(new GameTables(assets.Packs, "en")) { FeatureEnabledByName = f => Ms2.Ui.LobbyFeatures.IsEnabled(assets.Packs, f) };

    /// <summary>The HUD movies' model: the tables, features, key tables and the player.</summary>
    void HudModel()
    {
        if (!UiCode.Load(Settings.DataDir)) GD.PushWarning("UI code is not built for this data: the HUD movies run interpreted");
        hudModel = NewModel();
        hudModel.HasLocalPlayer = true;     // the HUD runs in a field, where WorldService::GetLocalPlayer is the MyPC
        // UIAlignWidgetManager::LoadWidgets (uiservice.cpp:1941): table/alignwidget.xml, one manager for the scene
        hudModel.AlignWidgets = alignWidgets ??= AlignWidgetManager.Load(assets.Table("table/alignwidget.xml")?.Root);
        hudModel.Keys = new KeyTable(assets.Packs, "en");
        // the server's key table when live (OnKeyTable), else the packet a new character gets
        hudModel.ServerKeys = Ms2Godot.Net.LiveSession.Instance?.KeyTable is { Loaded: true } live ? live : UiFixture.NewCharacterKeyTable();
        hudModel.Keys.Apply(hudModel.ServerKeys);
        var p = hudModel.Player;
        p.Name = data.Player.Name; p.Level = data.Player.Level;
        // jobCode is the sub job (uimainplayerinfo.cpp:1405): 100 Knight, 200 Berserker ... 1100 Soul Binder; the stub
        // numbers its jobs 1, 10, 20 ... 100
        p.SubJob = data.Player.Job <= 1 ? 100 : (data.Player.Job / 10 + 1) * 100;
        p.JobGroup = data.Player.Job;       // the job group ids of table/job.xml (1, 10 ... 110)
        p.HpCur = data.Player.HpCurrent; p.HpMax = data.Player.HpMax;
        p.SpCur = data.SpCurrent; p.SpMax = data.SpMax;
        p.EpCur = data.EpCurrent; p.EpMax = data.EpMax;
        p.Exp = (long)Math.Round(hudModel.NextLevelExp * data.ExpPercent / 100);
    }

    /// <summary>
    /// UIMainPlayerInfo (uimainplayerinfo), UIExpBar (uiexpbar) and UIAdventureLevelExpBar (uiadventurelevelexpbar), each run from its own movie with its C++ side as the
    /// controller (Ms2.Ui.Hud). The model is the HUD's numbers (<see cref="StubHudData"/>); feature 0xbd (the
    /// adventure level bar) is on in the Live feature table.
    /// </summary>
    void PlayerInfo(Control ui)
    {
        var p = hudModel.Player;
        var window = GetWindow().Size;
        ui.AddChild(playerInfoMovie = new MovieDialog(assets, new Ms2.Ui.Hud.MainPlayerInfoController(), hudModel, window));
        ui.AddChild(expBarMovie = new MovieDialog(assets, new Ms2.Ui.Hud.ExpBarController(), hudModel, window));
        p.AdventureExp = (long)Math.Round(hudModel.Tables.AdventureLevelLvUpExp * data.AdventureExpPercent / 100);
        ui.AddChild(adventureBarMovie = new MovieDialog(assets, new Ms2.Ui.Hud.AdventureLevelExpBarController(), hudModel, window));
    }

    // ---- game menu ---------------------------------------------------------------------------------------------------

    MovieDialog gameMenuMovie;
    static Ms2.Ui.Hud.GameMenuTable gameMenuTable;    // CGameMenuTable, read once

    /// <summary>
    /// UIGameMenuDialog: its movie (uigamemenudialog) with its C++ side (GameMenuController). The movie loads the face
    /// portrait as "img://&lt;movie&gt;:faceView/92/106" (loadRenderTarget), which vfunc_25 fills with CreatePortrait(92, 106):
    /// the portrait view's image here.
    /// </summary>
    void GameMenu(Control ui)
    {
        gameMenuTable ??= Ms2.Ui.Hud.GameMenuTable.Load(assets.Packs, hudModel.IsFeatureEnabled);
        var c = new Ms2.Ui.Hud.GameMenuController { Table = gameMenuTable };
        c.ToggleDialog += GameMenuToggle;
        if (portrait == null && (portrait = PortraitView.Create(assets.Packs, Settings.NifXml)) != null) AddChild(portrait);
        ui.AddChild(gameMenuMovie = new MovieDialog(assets, c, hudModel, GetWindow().Size));
        string face = "img://" + gameMenuMovie.Host.Id + ":faceView/";
        foreach (var n in gameMenuMovie.GetChildren())
            if (n is As3View v) v.ImageLookup = url => url.StartsWith(face, StringComparison.OrdinalIgnoreCase) ? portrait?.Image : null;
    }

    /// <summary>CUIService::ToggleDialog for a game menu entry, for the dialogs this HUD has: the system menu (0x4e21, as
    /// Esc toggles it), the quest log (0x4e3e) and the world map (0x4e42).</summary>
    void GameMenuToggle(int id)
    {
        if (id == 0x4e21) { if (systemMenu == null || systemMenu.IsClosed) OpenSystemMenu(); else systemMenu.Close(); }
        else if (id == 0x4e3e) ToggleQuestLog();
        else if (id == 0x4e42) ToggleWorldMap();
        else if (id == 0x4e2d) ToggleInventory();
        else if (id == 0x2711) ToggleMyInfo();      // "characterinfo" (gameeventmanagercli.cpp:486-494)
    }

    // ---- character window: uimyinfodialog run by its own ActionScript, UIMyInfoDialog's C++ side as MyInfoController --

    MovieDialog myInfoMovie;                    // from open until OnEndClose
    Ms2.Ui.Hud.MyInfoController myInfo;
    Ms2.Net.Packets.MyCharacter offlineMe;

    /// <summary>CUIService::ToggleDialog(0x2711): open when closed, else vfunc_67.</summary>
    void ToggleMyInfo()
    {
        if (myInfo == null || myInfo.IsClosed) OpenMyInfo();
        else myInfo.Close();
    }

    /// <summary>
    /// Opens the character window over the live session's own character (FieldAddUser, Stat, AttributePoint, UserEnv,
    /// HomeCommand) or, with no live session, a fixture in those packets' shapes (<see cref="UiFixture.Character"/>). Before
    /// the own FieldAddUser there is no record and, as UIMyInfoDialog::vfunc_65 without a local player, nothing opens.
    /// Nothing is sent: the stat point, motto, title, profile and screenshot requests are only logged.
    /// </summary>
    void OpenMyInfo()
    {
        if (myInfoMovie != null) { myInfoMovie.Host.Unload(); myInfoMovie.QueueFree(); myInfoMovie = null; }
        var live = Ms2Godot.Net.LiveSession.Instance;
        var me = live != null ? live.Me : (offlineMe ??= UiFixture.Character());
        Func<long> now = () => live?.GameSession?.ServerTime ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (me.ToPcInfo(now()) == null) { GD.Print("character: the live session has no own character yet"); return; }
        var c = new Ms2.Ui.Hud.MyInfoController { Record = () => me.ToPcInfo(now()), StatPoints = me.StatPoints, ServerTime = now };
        c.ToggleDialog += id => GD.Print($"character: dialog 0x{id:x} is not in this HUD");
        c.OpenDialog += id => GD.Print($"character: dialog 0x{id:x} is not in this HUD");
        c.Button += name => GD.Print($"character: {name} is not sent (display only)");
        void Changed() => c.MarkChanged((long)Time.GetTicksMsec());
        me.Changed += Changed; me.StatPoints.Changed += Changed;
        var m = new MovieDialog(assets, c, hudModel, GetWindow().Size);
        c.Unloaded += () =>
        {
            me.Changed -= Changed; me.StatPoints.Changed -= Changed;
            m.Host.Unload();
            m.QueueFree();
            if (myInfoMovie == m) myInfoMovie = null;
        };
        myInfo = c; myInfoMovie = m;
        uiRoot.AddChild(m);
    }

    // ---- inventory: uiinventorydialog run by its own ActionScript, UIInventoryDialog's C++ side as InventoryController --

    MovieDialog inventoryMovie;                 // from open until OnEndClose
    Ms2.Ui.Hud.InventoryController inventory;
    Ms2.Net.Packets.InventoryView offlineInventory;

    /// <summary>CUIService::ToggleDialog(0x4e2d): open when closed (vfunc_55), else vfunc_67.</summary>
    void ToggleInventory()
    {
        if (inventory == null || inventory.IsClosed) OpenInventory();
        else inventory.Close();
    }

    /// <summary>
    /// Opens the inventory over the live session's bag (the item packets) or, with no live session, a fixture bag in the
    /// item packet's shape (<see cref="UiFixture.InventoryItems"/>). A live session without its bag yet (before the field
    /// enter) opens nothing. The sort is the client's (its request is sent live); the slot expansion, the bank call, item use
    /// and the dialogs the buttons open are only logged.
    /// </summary>
    void OpenInventory()
    {
        if (inventoryMovie != null) { inventoryMovie.Host.Unload(); inventoryMovie.QueueFree(); inventoryMovie = null; }
        var live = Ms2Godot.Net.LiveSession.Instance;
        var view = live != null ? live.Inventory : OfflineInventory();
        if (view == null) { GD.Print("inventory: the live session has no bag yet"); return; }
        var c = new Ms2.Ui.Hud.InventoryController
        {
            View = view,
            BaseSlots = Ms2Godot.Net.BagSlotTable.Load(assets.Packs),
            MaxSlots = Ms2Godot.Net.BagSlotTable.LoadMax(assets.Packs),
        };
        c.ToggleDialog += id => GD.Print($"inventory: dialog 0x{id:x} is not in this HUD");
        c.Button += name => GD.Print($"inventory: {name} is not sent (display only)");
        var m = new MovieDialog(assets, c, hudModel, GetWindow().Size);
        c.Unloaded += () =>
        {
            m.Host.Unload();
            m.QueueFree();
            if (inventoryMovie == m) inventoryMovie = null;
        };
        inventory = c; inventoryMovie = m;
        uiRoot.AddChild(m);
    }

    Ms2.Net.Packets.InventoryView OfflineInventory()
    {
        if (offlineInventory != null) return offlineInventory;
        var kinds = new Ms2Godot.Net.ItemKinds(assets.Packs);
        var book = new Ms2.Net.Packets.ItemBook { TabOf = kinds.TabOf, BaseSlots = Ms2Godot.Net.BagSlotTable.Load(assets.Packs) };
        foreach (var g in UiFixture.InventoryItems().GroupBy(i => kinds.TabOf(i.Id)))
            book.Apply(new Ms2.Net.Packets.ItemMessage.LoadTab(g.Key, g.ToList()));
        return offlineInventory = new Ms2.Net.Packets.InventoryView(book);
    }

    public override void _Process(double delta)
    {
        foreach (var (_, c) in messengers) c.Process();      // UIMessenger::vfunc_15
        if (inventory != null && !inventory.IsClosed) inventory.Process();     // UIInventoryDialog::vfunc_15
        if (myInfo != null && !myInfo.IsClosed) myInfo.Process((long)Time.GetTicksMsec());     // UIMyInfoDialog::vfunc_15
        (notice?.Host.Controller as NoticeDialogController)?.Update((uint)Time.GetTicksMsec());     // UINoticeDialog::vfunc_72
        alignWidgets?.ProcessPendingLayout();      // the end of UISwfEntityManager::UpdateEntities
        if (!Field.LoadComplete) return;            // the field builds over several frames behind the loading screen
        if (minimap == null && minimapSlot != null && GetTree().GetFirstNodeInGroup(World.FieldContents.Group) is World.FieldContents { Portals: not null } mfc) OpenMinimap(mfc);
        minimapSide?.Process(Time.GetTicksMsec());
        upperHudSide?.Process(Time.GetTicksMsec());
        TargetBar?.Process();
        worldMapSide?.Process();             // UIWorldMapDialog::vfunc_72: FlushQueuedTiles
        if (shotDir == null) return;
        if (questShot != null && frames == 200) OpenQuestLog();
        if (questShot != null && frames == 230 && questShot.Length > 2) ClickQuest(questShot[2]);
        if (sysmenuShot >= 0 && frames == 200) Escape();
        if (worldMapShot >= 0 && frames == 200) ToggleWorldMap();
        if (worldMapShot >= 1 && frames == 240) worldMapSide?.SetPendingSearch("Henesys");
        if (worldMapShot >= 2 && frames == 260) worldMapSide?.OnCallback("onClickSearchInfo", new object[] { 2000076u });
        if (optionShot >= 0 && frames == 200) OpenGameOption(optionShot);
        if (sysmenuShot >= 1 && frames == 230) ClickSystemMenu("m_selectChannel");
        if (sysmenuShot >= 2 && frames == 240) ClickSystemMenu("m_channel");
        if (msgboxShot == 1 && frames == 200) Escape();
        if (msgboxShot == 1 && frames == 230) ClickSystemMenu("m_exitGame");
        if (msgboxShot == 2 && frames == 230) optionDialog?.FUN_141943a30();               // the movie's OnInit (Defaults)
        if (msgboxShot == 3 && frames == 230) optionHost?.MessageBox(0x84b, "OnSaveAndCheck");   // OnCloseDialog with a page changed
        if (frames == 250 && messengers.Count > 0) TypeChat(messengers[0], "HelloWorld");
        if (hoverShot != null && frames == 260) Hover(hoverShot);
        if (frameCase != null) { MeasureFrame(delta); return; }
        if (++frames == 300)
        {
            DirAccess.MakeDirRecursiveAbsolute(shotDir);
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(shotDir, "hud.png"));
            GetTree().Quit();
        }
    }

    /// <summary>The first display object named <paramref name="name"/> under <paramref name="o"/> (breadth first).</summary>
    static Ms2.Avm2.Flash.AsDisplayObject Descendant(UiHost h, Ms2.Avm2.Flash.AsDisplayObject o, string name)
    {
        var queue = new Queue<Ms2.Avm2.Flash.AsDisplayObject>();
        queue.Enqueue(o);
        while (queue.Count > 0)
        {
            var d = queue.Dequeue();
            if (d != o && Ms2.Avm2.Rt.ToString(h.Get(d, "name")) == name) return d;
            if (d is Ms2.Avm2.Flash.AsContainer c) foreach (var k in c.Children) queue.Enqueue(k);
        }
        return null;
    }

    /// <summary>The shot's pointer on a descendant (dotted path of names, each searched under the one before) of the hosted movie with that id,
    /// at a point of its own art.</summary>
    void Hover(string spec)
    {
        var parts = spec.Split(':', 2);
        var movie = FindMovies(GetTree().Root).FirstOrDefault(m => m.Host.Id == parts[0]);
        if (movie == null) { GD.PushWarning("hover: no movie " + parts[0]); return; }
        var h = movie.Host;
        object o = h.Entity;
        foreach (var n in parts[1].Split('.'))
            o = o is Ms2.Avm2.Flash.AsDisplayObject p ? Descendant(h, p, n) : null;
        if (o is not Ms2.Avm2.Flash.AsDisplayObject target) { GD.PushWarning("hover: no " + spec); return; }
        var b = target.GetBounds(null);
        for (double y = b.Y; y < b.Y + b.H; y += 2)
            for (double x = b.X; x < b.X + b.W; x += 2)
                for (var t = (Ms2.Avm2.Flash.AsDisplayObject)movie.Player.MouseTarget(x, y); t != null; t = t.Parent)
                    if (t == target) { movie.Player.MouseMove(x + 4, y + 4); return; }
        GD.PushWarning("hover: no hit on " + spec);
    }

    static IEnumerable<MovieDialog> FindMovies(Node n)
    {
        if (n is MovieDialog m) yield return m;
        foreach (var c in n.GetChildren())
            foreach (var x in FindMovies(c)) yield return x;
    }

    /// <summary>The shot's chat: Return focuses the input, then the text is typed into the movie.</summary>
    static void TypeChat((MovieDialog Movie, MessengerController Side) m, string text)
    {
        m.Side.OnKey(MessengerController.DikReturn, false);
        foreach (var ch in text)
        {
            uint code = char.IsAsciiLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : 0u;
            m.Movie.Player.Key(code, ch, true, char.IsUpper(ch), false);
            m.Movie.Player.Key(code, 0, false, false, false);
        }
    }

    /// <summary>--frametime: from frame 400 to 700 the frame's delta and Godot's process time; the scroll case wheels the quest list each frame.</summary>
    void MeasureFrame(double delta)
    {
        frames++;
        if (frameCase == "scroll" && frames >= 380 && questLog != null && questLog.Host.Child("m_questList") is Ms2.Avm2.Flash.AsDisplayObject list)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            var b = list.GetBounds(null);
            questLog.Player.MouseMove(b.X + b.W / 2, b.Y + b.H / 2);
            questLog.Player.Wheel((frames / 15) % 2 == 0 ? -1 : 1);
            As3View.SpentMs += System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        }
        if (frames > 400 && frames <= 700)
        {
            frameDelta.Add(delta * 1000);
            frameProcess.Add(Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000);
            frameMovie.Add(As3View.SpentMs);
            frameTick.Add(As3View.TickMs);
        }
        if (frames == 400) (callsAtStart, interpAtStart) = (Ms2.Avm2.Interp.CompiledCalls, Ms2.Avm2.Interp.InterpretedCalls);
        As3View.SpentMs = 0;
        As3View.TickMs = 0;
        if (frames == 700)
        {
            static string Stat(List<double> v) { var o = v.OrderBy(x => x).ToList(); return $"mean {v.Average():F2} ms p99 {o[(int)Math.Ceiling(o.Count * 0.99) - 1]:F2} ms"; }
            GD.Print($"FRAMETIME {frameCase} {(Ms2.Avm2.Interp.UseOracle ? "interpreter" : "compiled")} bodies={Ms2.Avm2.GeneratedCode.Count} n={frameDelta.Count} frame: {Stat(frameDelta)} | process: {Stat(frameProcess)} | movies: {Stat(frameMovie)} | of it ticks: {Stat(frameTick)} | calls compiled {Ms2.Avm2.Interp.CompiledCalls - callsAtStart} interpreted {Ms2.Avm2.Interp.InterpretedCalls - interpAtStart}"
                + (questLog?.Host.Child("m_questList") is { } ql ? $" | list scrollPosition {questLog.Host.Get(ql, "scrollPosition")} rows {questLog.Host.Get(questLog.Host.Get(ql, "dataProvider"), "length")}" : ""));
            GetTree().Quit();
        }
    }
}
