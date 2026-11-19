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
public partial class HudScene : Node, IUiStageHost
{
    UiAssets assets;
    readonly StubHudData data = StubHudData.Instance;
    /// <summary>UIMessengerBrowser's windows (one per default messenger, LoadDefaultWindows) and UIGroupMessengerBrowser,
    /// run from their movies; built once and kept over a relayout, as the client keeps its dialogs.</summary>
    readonly List<(MovieDialog Movie, MessengerController Side)> messengers = new();
    /// <summary>
    /// UIMessengerBrowser's log, the lines all chat windows share. It lives in the game scene's UI, which a field move
    /// keeps, so it outlives this scene (made again per field); FUN_141275500 (unit_141274d10_uihelpers.cpp:327) clears it
    /// when the lobby is entered (CSceneStateMulti::OnEnter, scenestatemulti.cpp:345).
    /// </summary>
    public static readonly Ms2.Ui.Hud.MessengerBrowser ChatLog = new();
    /// <summary>UISceneGame::vfunc_48's s_bWelcomeShown (uiscenegame.cpp:192): the welcome line once per run.</summary>
    static bool welcomeShown;
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
    MovieDialog systemText;                 // uisystemtext
    /// <summary>UISystemText's C++ side: the system lines across the upper middle (the red warnings and the rest).</summary>
    public Ms2.Ui.Hud.SystemTextController SystemText { get; private set; }
    DamageText damageText;
    PlayerHpBar playerBar;
    DamageEffect damageEffect;              // UIDamageEffect, the low-HP screen effect
    int[] questShot;                        // --questlog TAB,SUBTAB[,QUEST]: the shot opens the quest log on these tabs, selecting the quest
    string questState;                      // --questlog-state LEVEL:ID,ID,..[:TRACKING]: the shot's quest state is these quests in progress at that level
    MovieDialog questLog;                   // UIQuestDialog, the quest log, run from its own movie while it is loaded
    Ms2.Ui.Hud.QuestDialogController questLogSide;   // its C++ side, kept while it is closed (m_180 / m_184 and the filters)
    Ms2.Ui.UiModel questLogModel;
    int optionShot = -1;                    // --uigen-options PAGE: the shot opens the options dialog on that page, as the system menu does
    long partyMenuShot;                     // --uigen-party-menu ID: the party slot menu of that member
    string hoverShot;                       // --uigen-hover MOVIE:a.b.c (or MOVIE:@x,y): the pointer on that child of a hosted movie, or at that point (its tooltip shows)
    int msgboxShot = -1;                    // --uigen-msgbox KIND: 1 the exit box (Esc, Exit Game); with --uigen-options, 2 the reset box, 3 the unsaved changes box
    int worldMapShot = -1;                  // --worldmap STEP: the shot opens the world map (M); 1 searches "Henesys", 2 clicks the result
    int sysmenuShot = -1;                   // --uigen-sysmenu STATE: the shot opens the system menu with Esc; 1 shows the channel combo, 2 opens its list
    string openShot;                        // --uigen-open inventory|myinfo: the shot opens that window, as I or P does
    string dragShot;                        // --uigen-drag FROM>TO: both windows open, then a mouse drag from one slot binding to another (FROM "worn": the first worn equip slot)
    Vector2 dragFrom, dragTo;
    static DungeonRooms dungeonRooms;
    bool questFixture;                      // --questlog-fixture: the shot's quest log lists made-up quest state (QuestShotState)

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--hudshot") shotDir = args[i + 1];
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--frametime") frameCase = args[i + 1];
        if (System.Array.IndexOf(args, "--oracle") >= 0) Ms2.Avm2.Interp.UseOracle = true;
        if (frameCase != null) DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        PerfLog.ApplyFpsCap();
        questFixture = args.Contains("--questlog-fixture");
        partyFixture = args.Contains("--party-fixture");
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--questlog") questShot = args[i + 1].Split(',').Select(int.Parse).ToArray();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--questlog-state") questState = args[i + 1];
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--uigen-sysmenu") sysmenuShot = int.Parse(args[i + 1]);
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--uigen-open") openShot = args[i + 1];
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--uigen-drag") dragShot = args[i + 1];
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--worldmap") worldMapShot = int.Parse(args[i + 1]);
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--uigen-options") optionShot = int.Parse(args[i + 1]);
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--uigen-msgbox") msgboxShot = int.Parse(args[i + 1]);
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--uigen-hover") hoverShot = args[i + 1];
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--uigen-party-menu") long.TryParse(args[i + 1], out partyMenuShot);
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--quickslots") quickSlotShot = args[i + 1];
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--quickslots-player") quickSlotPlayer = args[i + 1].Split(',').Select(int.Parse).ToArray();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--shop") shopShot = args[i + 1];
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--skillbook") skillShot = args[i + 1].Split(',').Select(int.Parse).ToArray();
        var dataDir = Settings.DataDir;
        if (string.IsNullOrEmpty(dataDir)) { GD.PushError("Set MS2_DATA (the client's Data folder), or fill in local.cfg."); GetTree().Quit(2); return; }
        assets = new UiAssets(PackSet.Shared(dataDir));   // the field's pack set: its archives and the per-pack-set UI caches are shared

        xblock = World.FieldContents.PendingXBlock ?? ArgField() ?? "02000001_tw_tria";   // as Field picks it; the field scene consumes the move
        AddChild(GD.Load<PackedScene>("res://field.tscn").Instantiate());      // the field scene, untouched
        BuildHud();
        WatchParty();
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
        Ms2Godot.Net.ProfileImageService.Images.Ready += ProfileImageReady;
    }

    /// <summary>
    /// Leaving the tree: the size handler is dropped first, because restoring the window's content scale mode emits the root
    /// viewport's size_changed at once, while this scene and its field are already on their way out.
    /// </summary>
    public override void _ExitTree()
    {
        unwatchQuickSlotState?.Invoke();
        unwatchBuffs?.Invoke();
        unwatchQuickSlotState = null;
        unwatchParty?.Invoke();
        unwatchParty = null;
        unwatchGemErrors?.Invoke();
        unwatchGemErrors = null;
        unwatchNotices?.Invoke();
        unwatchNotices = null;
        unwatchNewItems?.Invoke();
        unwatchNewItems = null;
        foreach (var (_, c) in messengers) ChatLog.Windows.Remove(c);
        unbindBoxAnswers?.Invoke();
        unbindBoxAnswers = null;
        Ms2Godot.Net.ProfileImageService.Images.Ready -= ProfileImageReady;
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

    /// <summary>The HUD's dialogs as the client's one stage: drops from one dialog onto another, and main.gfx's drag callbacks.</summary>
    public UiStage UiStage { get; } = new();

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
        systemText?.GetParent()?.RemoveChild(systemText);     // and the system lines
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
        partyMovie?.Relayout(GetWindow().Size);
        popupMovie?.Relayout(GetWindow().Size);
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
        var constants = Ms2.Ui.FeatureElements.Constants(assets.Packs);
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
        SystemTextLines(uiRoot);
        HudModel();
        Chat(uiRoot);
        Quests(uiRoot);
        PlayerInfo(uiRoot);
        GameMenu(uiRoot);
        EventWidgets(uiRoot);
        SystemMenuFacts();
        MessageBoxLayer.Ensure(this);       // the client's one message box layer (CUIService::CreateAllDialogs)
        BindBoxAnswers();
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
    bool AnyClosableDialogOpen() => (questLog != null && questLogSide?.IsClosed == false) || WorldMapOpen || optionHost?.IsClosed == false
                                    || inventory?.IsClosed == false || myInfo?.IsClosed == false;

    /// <summary>CUIService::CloseAllDialogs: closes every closable dialog (vfunc_67).</summary>
    void CloseAllDialogs()
    {
        if (questLog != null && questLogSide?.IsClosed == false) questLogSide.Close();
        if (WorldMapOpen) worldMapSide.Close();
        if (optionHost?.IsClosed == false) optionHost.Movie.Close();
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
        if (!optionHost.IsClosed) return;
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
        var model = new UiModel(GameTables.Shared(assets.Packs, assets.Language));
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

    /// <summary>
    /// The system lines (UISystemText, uisystemtext.cpp), run by their movie; kept across relayouts, placed under the target
    /// HP bar (vfunc_43). A message shown before the movie is loaded is parked and shown at OnLoadComplete.
    /// </summary>
    void SystemTextLines(Control ui)
    {
        if (systemText != null) { ui.AddChild(systemText); systemText.Relayout(GetWindow().Size); return; }
        SystemText = new Ms2.Ui.Hud.SystemTextController { UpperHpBar = TargetBar, TickMs = () => (uint)Time.GetTicksMsec() };
        // STILL-OPEN: PlaySystemSound (the emergency line's "emergency_notice"): this HUD has no system sound player
        SystemText.SystemSound += key => GD.Print($"system text: sound {key} is not played");
        systemText = new MovieDialog(assets, SystemText, NewModel(), GetWindow().Size);
        ui.AddChild(systemText);
        // OnBadgeEquip mode 4 (scenestategame.cpp:11075): the gem error's system notice, whatever window is open
        if (Ms2Godot.Net.LiveSession.Instance is { } live)
        {
            Action<short> onGemError = e => SystemNotice(Ms2.Ui.Hud.MyInfoController.GemEquipErrorKey(e, hudModel.IsFeatureEnabled(0x4a)));
            live.GemEquipError += onGemError;
            unwatchGemErrors = () => live.GemEquipError -= onGemError;
            Notices = NewNotices(live);
            Action<Ms2.Net.Packets.NoticeMessage> onNotice = Notices.OnNotice;
            live.Notice += onNotice;
            unwatchNotices = () => live.Notice -= onNotice;
        }
    }
    Action unwatchGemErrors, unwatchNotices;

    /// <summary>OnNotice (0x71)'s routing; its sinks are this HUD's system text, the shared chat log and the box queue.</summary>
    public Ms2.Ui.Hud.NoticeController Notices { get; private set; }

    Ms2.Ui.Hud.NoticeController NewNotices(Ms2Godot.Net.LiveSession live)
    {
        string lang = (assets.Language ?? "").ToUpperInvariant();
        return new Ms2.Ui.Hud.NoticeController
        {
            Tables = hudModel.Tables,
            Trigger = hudModel.Tables.TriggerStrings,
            ReplaceValueWords = t => hudModel.Tables.ReservedWords.ReplaceValueWords(t, hudModel.HasLocalPlayer ? hudModel.Player.Name : null),
            Language = lang,
            // DAT_143ab0248, the client's region: an assumption here from the string language (KR 0, JP 1, CN 3, the others 2)
            Region = lang switch { "KR" => 0, "JP" => 1, "CN" => 3, _ => 2 },
            ServerTime = () => live.GameSession?.ServerTime ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ShowMessage = (t, type, option) => SystemText?.ShowMessage(t, type, option),
            ChatLine = ChatLog.AddSystemLine,
            MessageBox = (t, tag) => MessageBoxQueue.ShowMessageBox(t, 0, null, tag),
            SystemSound = key => GD.Print($"notice: system sound {key} is not played"),
            ForcedQuit = () => GD.Print("notice: forced quit (SendRequestQuitAndDisconnect is not ported)"),
            MaintenanceExit = () => GD.Print("notice: the maintenance exit call is not ported"),   // FUN_140d86260()->vfunc_58(0x289, 0)
        };
    }

    /// <summary>
    /// FUN_141275260 (unit_141274d10_uihelpers.cpp:265): the common string of the id through ShowSystemNotice(text, 1, true)
    /// (@0x1412751f0), which is UISystemText::ShowMessage(text, 1, 0), the red warning line, and with bLog the same text as
    /// a chat line of kind 0x14 (FUN_1412751b0); nothing without the system text, nor for an empty string.
    /// </summary>
    void SystemNotice(string key)
    {
        string text = hudModel.Tables.Common(key);
        if (string.IsNullOrEmpty(text) || SystemText == null) return;
        SystemText.ShowMessage(text, Ms2.Ui.Hud.SystemTextController.Warning, 0);
        ChatLog.AddSystemLine(text, Ms2.Ui.Hud.TalkKind.SystemNotice);
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
        try
        {
            var fd = FieldData.FindByXBlock(assets.Packs, xblock);
            model.Field.MapLevel = fd?.MapLevel;
            if (fd != null) model.Field.MapId = fd.Id;
        }
        catch (Exception e) { GD.PushWarning("minimap field data: " + e.Message); }
        // PC::GetMastery(1): the user block's mastery list, index 1 = fishing
        if (data.Player.Mastery is { Length: > 1 } mastery) model.Player.FishingMastery = (uint)mastery[1];
        model.Field.Channel = (int)data.Player.Channel;
        model.Field.Channels = new[] { (int)data.Player.Channel };
        var live = Ms2Godot.Net.LiveSession.Instance;
        long selfId = live?.CharacterId ?? 0;
        var c = minimapSide = new Ms2.Ui.Hud.MinimapController
        {
            ImageName = info?.ImageEntry,
            Viewport = info,
            CharacterId = selfId,
            // FUN_141028be0(id, picture, 1): the downloaded picture, else (none, or not downloaded yet) the default image
            PlayerImage = Ms2Godot.Net.ProfileImageService.Images.Path(selfId, live?.Me?.User?.Picture ?? "", true),
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
        var icons = new MinimapIcons(assets.Packs, World.FieldContents.Npcs(assets.Packs), new GameStrings(assets.Packs, assets.Language));
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
            int bestLevel = (int)Ms2.Ui.FeatureElements.Constants(assets.Packs).Float("TaxiCallBestDriverLevel", 25);
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
            if (fd != null && new GameStrings(assets.Packs, assets.Language).Table("mapname", "name") is { } names
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
    /// the group chat bar, all over the shared log (<see cref="ChatLog"/>). The saved windows of the character folder
    /// (LoadWindows :3689) are not read yet. Lines typed are not shown: SendChat (:4208) only sends the command and the
    /// line arrives with the chat packet (0x1d), which the live session does not carry yet.
    /// The log's lines here: the welcome line (UISceneGame::vfunc_48, uiscenegame.cpp:187: s_msg_chatting_welcome, or
    /// s_msg_chatting_welcome_pubtest2 while the public test game event runs; game events are not modelled, so the plain
    /// one), the system notices' copies (<see cref="SystemNotice"/>) and the new items of a live session (ItemInventory mode
    /// 7 -> the item event dispatcher's vfunc_4, uiitemenchantdialog.cpp:2146 -> UIMessengerBrowser::vfunc_5). Of mode 7
    /// (scenestategame.cpp:2836) the item's name (FUN_140529500, only read for a score's title) and MyPC's cube gate
    /// (FUN_141527eb0: m_15e4, set by the cube responses 0x20 / 0x21, not ported) are left out.
    /// </summary>
    void Chat(Control ui)
    {
        var window = GetWindow().Size;
        if (messengers.Count == 0)
        {
            ChatLog.Model = hudModel;
            var docs = hudModel.Tables.DefaultMessengers;
            for (int i = 0; i < docs.Count; i++)
            {
                var c = new MessengerController(docs[i], $"messengerdialog{i}.xml", true, ChatLog) { IsKeyDown = DikHeld };
                messengers.Add((new MovieDialog(assets, c, hudModel, window), c));
            }
            groupMessenger = new MovieDialog(assets, new GroupMessengerBrowserController(), hudModel, window);
            if (!welcomeShown)
            {
                ChatLog.AddSystemLine(hudModel.Tables.Common("s_msg_chatting_welcome"), Ms2.Ui.Hud.TalkKind.SystemNotice);
                welcomeShown = true;
            }
            if (Ms2Godot.Net.LiveSession.Instance is { } live)
            {
                Action<long, int> onNew = (uid, count) =>
                {
                    if (live.Items.Get(uid) is { } e && Ms2.Ui.Hud.InventoryController.ToItem(e, hudModel.Tables.Items) is { } item)
                        ChatLog.AddItemLine(item, (uint)count);
                };
                live.Items.NotifiedNew += onNew;
                unwatchNewItems = () => live.Items.NotifiedNew -= onNew;
            }
        }
        foreach (var (m, _) in messengers) ui.AddChild(m);
        ui.AddChild(groupMessenger);
    }

    Action unwatchNewItems;

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
        if (k.PhysicalKeycode == Key.K) { ToggleSkillBook(); return true; }     // action 400005 (DIK 37, K): ToggleDialog(0x4f2d)
        bool open = questLog != null && questLogSide?.IsClosed == false;
        if (k.Keycode == Key.Escape && open) { questLogSide.Close(); return true; }
        if (k.Keycode == Key.Escape && inventory?.IsClosed == false) { inventory.Close(); return true; }
        if (k.Keycode == Key.Escape && myInfo?.IsClosed == false) { myInfo.Close(); return true; }
        if (k.Keycode == Key.Escape && skillBook?.IsClosed == false) { skillBook.Close(); return true; }
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
            var perf = PerfLog.On && fieldMissions == null ? new PerfLog.Steps("field missions") : null;
            fieldMissions ??= FieldMissions.Load(assets.Packs, new GameStrings(assets.Packs, assets.Language).Languages, data.Locale, f => !data.FeaturesOff.Contains(f),
                                                 questTable?.Words);     // the same languages, locale and features: its tables
            perf?.Step("load");
            perf?.Done();
        }
        catch (Exception e) { GD.PushWarning("field missions: " + e.Message); }
        var meta = fieldMeta;
        var constants = Ms2.Ui.FeatureElements.Constants(assets.Packs);
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
                    Map = QuestMapGraph.Shared(assets.Packs).Graph,
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
            var perf = PerfLog.On && questTable == null ? new PerfLog.Steps("quest tables") : null;
            fieldMeta ??= QuestMapGraph.SharedMeta(assets.Packs);
            perf?.Step("field-meta");
            questTable ??= QuestTable.Load(assets.Packs, new GameStrings(assets.Packs, assets.Language).Languages, data.Locale, featureOn);
            perf?.Step("quest-table");
            perf?.Done();
            fieldCodes ??= QuestTable.FieldCodes(FieldData.Document(assets.Packs), questTable.Available);   // the session's parsed table
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
    static Ms2.Ui.WorldMap.FieldMissionTable fieldMissionTable;    // CFieldMissionTable, read once

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
            fieldMeta ??= QuestMapGraph.SharedMeta(assets.Packs);
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
                Missions = fieldMissions,       // the quest tracker's, read when the HUD starts
                MissionRewards = fieldMissionTable ??= Ms2.Ui.WorldMap.FieldMissionTable.Load(assets.Packs),
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
    UiModel NewModel() => new(GameTables.Shared(assets.Packs, assets.Language)) { FeatureEnabledByName = f => Ms2.Ui.LobbyFeatures.IsEnabled(assets.Packs, f) };

    /// <summary>The HUD movies' model: the tables, features, key tables and the player.</summary>
    void HudModel()
    {
        if (!UiCode.Load(Settings.DataDir)) GD.PushWarning("UI code is not built for this data: the HUD movies run interpreted");
        hudModel = NewModel();
        MovieLayerHost.Layers.FeatureEnabled = hudModel.IsFeatureEnabled;   // the adventure level bar's layer (feature 0xbd)
        hudModel.HasLocalPlayer = true;     // the HUD runs in a field, where WorldService::GetLocalPlayer is the MyPC
        // UIAlignWidgetManager::LoadWidgets (uiservice.cpp:1941): table/alignwidget.xml, one manager for the scene
        hudModel.AlignWidgets = alignWidgets ??= AlignWidgetManager.Load(assets.Table("table/alignwidget.xml")?.Root);
        hudModel.Keys = new KeyTable(assets.Packs, assets.Language);
        // the server's key table when live (OnKeyTable), else the packet a new character gets
        hudModel.ServerKeys = Ms2Godot.Net.LiveSession.Instance?.KeyTable is { Loaded: true } live ? live
            : quickSlotShot != null ? QuickSlotShotTable(quickSlotShot) : UiFixture.NewCharacterKeyTable();
        hudModel.Keys.Apply(hudModel.ServerKeys);
        var p = hudModel.Player;
        p.Name = data.Player.Name; p.Level = data.Player.Level;
        // jobCode is the sub job (uimainplayerinfo.cpp:1405): 100 Knight, 200 Berserker ... 1100 Soul Binder; the stub
        // numbers its jobs 1, 10, 20 ... 100
        p.SubJob = data.Player.Job <= 1 ? 100 : (data.Player.Job / 10 + 1) * 100;
        p.JobGroup = data.Player.Job;       // the job group ids of table/job.xml (1, 10 ... 110)
        p.Gender = data.Player.Gender;
        p.HpCur = data.Player.HpCurrent; p.HpMax = data.Player.HpMax;
        p.Channel = (int)data.Player.Channel;    // the user block's channel (a party slot's "Ch. N" line reads its member's)
        p.SpCur = data.SpCurrent; p.SpMax = data.SpMax;
        p.EpCur = data.EpCurrent; p.EpMax = data.EpMax;
        p.Exp = (long)Math.Round(hudModel.NextLevelExp * data.ExpPercent / 100);
        if (quickSlotPlayer != null)
        {
            p.SubJob = quickSlotPlayer[0];
            p.JobGroup = quickSlotPlayer[0] == 100 ? 1 : (quickSlotPlayer[0] / 100 - 1) * 10;
            if (quickSlotPlayer.Length > 1) p.Level = quickSlotPlayer[1];
        }
        // GetItemUsableState for the slots' lock / disable marks (FUN_1409c3860 flag 4) and the shop's usable filter
        var session = Ms2Godot.Net.LiveSession.Instance;
        itemUse = new Ms2.Ui.Items.ItemUse
        {
            Player = p,
            ServerTime = () => session?.GameSession?.ServerTime ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
        (hudModel.ItemSlotSources ??= new()).UsableState = itemUse.UsableState;
    }

    /// <summary>Unk_Inventory::GetItemUseError for the HUD's player (<see cref="HudModel"/>).</summary>
    Ms2.Ui.Items.ItemUse itemUse;

    /// <summary>
    /// UIMainPlayerInfo (uimainplayerinfo), UIExpBar (uiexpbar) and UIAdventureLevelExpBar (uiadventurelevelexpbar), each run from its own movie with its C++ side as the
    /// controller (Ms2.Ui.Hud). The model is the HUD's numbers (<see cref="StubHudData"/>); feature 0xbd (the
    /// adventure level bar) is on in the Live feature table.
    /// </summary>
    void PlayerInfo(Control ui)
    {
        var p = hudModel.Player;
        var window = GetWindow().Size;
        playerInfo = new Ms2.Ui.Hud.MainPlayerInfoController
        {
            SlotWorld = QuickSlotWorld,
            TooltipPlayer = () => { QuickSlotWorld(); return SkillTooltipPlayer(slotJobs); },
        };
        OwnBuffs();
        ui.AddChild(playerInfoMovie = new MovieDialog(assets, playerInfo, hudModel, window));
        WatchQuickSlotState();
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
        else if (id == 0x4f2d) ToggleSkillBook();
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
        var c = new Ms2.Ui.Hud.MyInfoController
        {
            Record = () => me.ToPcInfo(now()), LocalGems = () => me.Gems, StatPoints = me.StatPoints, ServerTime = now,
            Inventory = live != null ? live.Inventory : OfflineInventory(),
            ActionKeyDown = ActionKeyDown,
            SymbolId = () => me.Insignia, GmGrade = () => me.User?.GmGrade ?? 0,
            AchieveTally = () => live?.AchieveSum ?? 0,   // the fixture: an empty trophy book
            LocalPlayerDead = () => me.Dead,              // PCBase::IsDead (FieldAddUser, UserState 12 / DeadUser, Revival)
        };
        c.SystemNotice += SystemNotice;
        c.ToggleDialog += id => GD.Print($"character: dialog 0x{id:x} is not in this HUD");
        c.OpenDialog += id => GD.Print($"character: dialog 0x{id:x} is not in this HUD");
        c.Button += name =>
        {
            if (name == "onchangeprofile") Ugc().OpenProfileUpload();   // UIMyInfoDialog::OnChangeProfile
            else GD.Print($"character: {name} is not sent (display only)");
        };
        // GetProfileImagePath (@0x141336c90): FUN_141028be0(id, picture, 1)
        c.ProfileImagePath = pc => Ms2Godot.Net.ProfileImageService.Images.Path(pc.CharacterId, pc.Picture, true);
        BindBoxAnswers();
        void Changed() => c.MarkChanged((long)Time.GetTicksMsec());
        me.Changed += Changed; me.StatPoints.Changed += Changed;
        if (live != null) live.AchievesChanged += Changed;
        var m = new MovieDialog(assets, c, hudModel, GetWindow().Size);
        c.Unloaded += () =>
        {
            me.Changed -= Changed; me.StatPoints.Changed -= Changed;
            if (live != null) live.AchievesChanged -= Changed;
            m.Host.Unload();
            m.QueueFree();
            if (myInfoMovie == m) myInfoMovie = null;
        };
        myInfo = c; myInfoMovie = m;
        c.RotateLimit = avatarLimit ??= Ms2.Ui.Hud.AvatarRotateLimit.FromConstants(assets.Packs);
        string avatar = "img://" + m.Host.Id + ":avatar/";
        AvatarView view = null;
        foreach (var n in m.GetChildren())
            if (n is As3View v) v.ImageLookup = url =>
            {
                if (!url.StartsWith(avatar, StringComparison.OrdinalIgnoreCase) || c.Avatar == null) return null;
                if (view == null && AvatarView.SizeOf(url) is { } size
                    && (view = AvatarView.Create(assets.Packs, Settings.NifXml, size, c.Avatar)) != null) AddChild(view);
                return view?.Image;
            };
        c.Unloaded += () => { view?.QueueFree(); view = null; };
        uiRoot.AddChild(m);
    }

    /// <summary>
    /// UIUGCEventDispatcher::OnProfileImage(character id, key) to the HUD's listeners: the minimap (changePlayer with
    /// GetProfileImagePath(key, 1)), the popup menu (GetProfileImagePath(key, 0)) and the character window (the key).
    /// </summary>
    void ProfileImageReady(long characterId, string key)
    {
        var images = Ms2Godot.Net.ProfileImageService.Images;
        minimapSide?.OnProfileImage(characterId, images.ProfileImagePath(key, true));
        popupMenu?.OnProfileImage(characterId, images.ProfileImagePath(key, false));
        myInfo?.OnProfileImage(characterId, key);
    }

    // ---- the UGC uploads: CUgcManager and the image upload dialog (UIUGCImageUploadDialog, 0x4e56) --------------------

    Ms2.Ui.Hud.UgcManager ugc;
    Ms2.Ui.Hud.UgcImageUploadController uploadDialog;   // the dialog's C++ side while its movie is loaded
    MovieDialog uploadDialogMovie;                      // from open until OnEndClose

    /// <summary>
    /// The run's CUgcManager for this HUD: its files under user://ugc/ (<see cref="Ms2Godot.Net.UgcFileService"/>), the
    /// upload dialog as the one dialog it opens, the character window as the profile viewer, the posts through the web
    /// host's urq.aspx and the keys to the game server.
    /// STILL-OPEN: the blocked-action check (CBlockManager::FindBlockedAction(3)); dialogs 0x4e69 and 0x4e70, which
    /// OpenExclusiveUploadDialog closes, are not in this HUD.
    /// </summary>
    Ms2.Ui.Hud.UgcManager Ugc()
    {
        if (ugc != null) return ugc;
        var m = new Ms2.Ui.Hud.UgcManager(new Ms2Godot.Net.UgcFileService(this), hudModel.Tables)
        {
            Mode = () => Ms2Godot.Net.ProfileImageService.Images.Mode,
            CommonNotice = id => ShowSystemText(Ms2.Ui.Hud.MessageBoxData.Common(id)),     // FUN_141275260(id, 1, 1)
            Notice = ShowSystemText,                                                         // ShowSystemNotice(text, 1, 1)
            Box = (id, listener, tag) => Ms2.Ui.Hud.MessageBoxQueue.ShowCommon(id, 0, listener, tag),   // FUN_140d73bc0(id, 0, ..)
            TextBox = text => Ms2.Ui.Hud.MessageBoxQueue.ShowMessageBox(text, 0, null, ""),
            DialogOpen = id => id == Ms2.Ui.Hud.UgcManager.UploadDialogId && uploadDialog is { IsClosed: false },
            CloseDialog = id => { if (id == Ms2.Ui.Hud.UgcManager.UploadDialogId && uploadDialog is { IsClosed: false }) uploadDialog.Close(); },
            ToggleDialog = id =>
            {
                if (id != Ms2.Ui.Hud.UgcManager.UploadDialogId) return;
                if (uploadDialog is { IsClosed: false }) uploadDialog.Close();
                else OpenUploadDialog();
            },
            UploadDialog = () => uploadDialog is { IsClosed: false } ? uploadDialog : null,
            MyInfo = () => myInfo is { IsClosed: false } ? myInfo : null,
            HasLocalPlayer = () => true,    // the HUD is the field's, over the own player
            InField = () => true,
            User = () => Ms2Godot.Net.LiveSession.Instance?.UgcUser ?? default,
            Post = Ms2Godot.Net.ProfileImageService.Post,
            SendProfileKey = key => Ms2Godot.Net.LiveSession.Instance?.SendUgc(new Ms2.Net.Packets.UgcProfileImageRequest(key)),
            SendUploadFailed = () => Ms2Godot.Net.LiveSession.Instance?.SendUgc(new Ms2.Net.Packets.UgcUploadFailedRequest()),
            LobbyProfile = (id, key) => GD.Print($"ugc: the lobby's profile key for {id} is not in the field"),
        };
        return ugc = m;
    }

    void ShowSystemText(string text) => SystemText?.ShowMessage(text, Ms2.Ui.Hud.SystemTextController.Warning, 0);

    /// <summary>CUIService::ToggleDialog(0x4e56) when it is not up: the upload dialog's movie over the HUD.</summary>
    void OpenUploadDialog()
    {
        if (uploadDialogMovie != null) { uploadDialogMovie.Host.Unload(); uploadDialogMovie.QueueFree(); uploadDialogMovie = null; }
        var c = new Ms2.Ui.Hud.UgcImageUploadController(Ugc());
        BindBoxAnswers();
        var m = new MovieDialog(assets, c, hudModel, GetWindow().Size);
        c.Unloaded += () =>
        {
            m.Host.Unload();
            m.QueueFree();
            if (uploadDialogMovie == m) uploadDialogMovie = null;
        };
        uploadDialog = c; uploadDialogMovie = m;
        uiRoot.AddChild(m);
    }

    /// <summary>The scripted live run's upload dialog, while it is up.</summary>
    public Ms2.Ui.Hud.UgcImageUploadController UploadDialog => uploadDialog is { IsClosed: false } ? uploadDialog : null;

    /// <summary>A scripted click in the upload dialog at the fraction (<paramref name="fx"/>, <paramref name="fy"/>) of a
    /// member's bounds (the list's first cell, the upload button); false while the dialog or the member is not there.</summary>
    public bool ClickUploadDialog(string member, double fx = 0.5, double fy = 0.5)
    {
        if (UploadDialog == null || uploadDialogMovie is not { } m) return false;
        if (m.Host.Child(member, m.Host.Entity) is not Ms2.Avm2.Flash.AsDisplayObject o) return false;
        var b = o.GetBounds(null);
        m.Player.Click(b.X + b.W * fx, b.Y + b.H * fy);
        return true;
    }

    /// <summary>The scripted live run's character window click on the profile picture (open the window first; false while
    /// it is not up).</summary>
    public bool ClickChangeProfile()
    {
        if (myInfo == null || myInfo.IsClosed) { OpenMyInfo(); return false; }
        myInfo.OnCallback("onchangeprofile", System.Array.Empty<object>());
        return true;
    }

    static Ms2.Ui.Hud.AvatarRotateLimit avatarLimit;   // UIAvatarPC::vfunc_1's constants, read once

    /// <summary>
    /// CKeyTable::IsActionKeyDown (keytable.cpp:1037): any key bound to the action is held. The bound keys are DirectInput codes
    /// (<see cref="GodotKeyOfDik"/>). The input gate (IsKeyDownGated) is not modelled.
    /// </summary>
    bool ActionKeyDown(int action) =>
        hudModel.Keys.KeysOfAction(action).Any(k => GodotKeyOfDik(k) is { } g && Input.IsPhysicalKeyPressed(g));

    /// <summary>The DirectInput keyboard codes (DIK_*, dinput.h) as Godot physical keys; null for a code not here.</summary>
    static Key? GodotKeyOfDik(int dik) => dik switch
    {
        0x01 => Key.Escape, >= 0x02 and <= 0x0a => Key.Key1 + (dik - 0x02), 0x0b => Key.Key0, 0x0c => Key.Minus, 0x0d => Key.Equal,
        0x0e => Key.Backspace, 0x0f => Key.Tab, 0x10 => Key.Q, 0x11 => Key.W, 0x12 => Key.E, 0x13 => Key.R, 0x14 => Key.T,
        0x15 => Key.Y, 0x16 => Key.U, 0x17 => Key.I, 0x18 => Key.O, 0x19 => Key.P, 0x1a => Key.Bracketleft, 0x1b => Key.Bracketright,
        0x1c => Key.Enter, 0x1d => Key.Ctrl, 0x1e => Key.A, 0x1f => Key.S, 0x20 => Key.D, 0x21 => Key.F, 0x22 => Key.G, 0x23 => Key.H,
        0x24 => Key.J, 0x25 => Key.K, 0x26 => Key.L, 0x27 => Key.Semicolon, 0x28 => Key.Apostrophe, 0x29 => Key.Quoteleft,
        0x2a => Key.Shift, 0x2b => Key.Backslash, 0x2c => Key.Z, 0x2d => Key.X, 0x2e => Key.C, 0x2f => Key.V, 0x30 => Key.B,
        0x31 => Key.N, 0x32 => Key.M, 0x33 => Key.Comma, 0x34 => Key.Period, 0x35 => Key.Slash, 0x36 => Key.Shift,
        0x37 => Key.KpMultiply, 0x38 => Key.Alt, 0x39 => Key.Space, 0x3a => Key.Capslock,
        >= 0x3b and <= 0x44 => Key.F1 + (dik - 0x3b), 0x57 => Key.F11, 0x58 => Key.F12,
        0x9d => Key.Ctrl, 0xb8 => Key.Alt, 0xc8 => Key.Up, 0xd0 => Key.Down, 0xcb => Key.Left, 0xcd => Key.Right,
        0xc7 => Key.Home, 0xcf => Key.End, 0xc9 => Key.Pageup, 0xd1 => Key.Pagedown, 0xd2 => Key.Insert, 0xd3 => Key.Delete,
        _ => null,
    };

    // ---- inventory: uiinventorydialog run by its own ActionScript, UIInventoryDialog's C++ side as InventoryController --

    MovieDialog inventoryMovie;                 // from open until OnEndClose
    Ms2.Ui.Hud.InventoryController inventory;

    /// <summary>
    /// The bind boxes' yes (UIMessageBox::HandleUseBindFuncItemButton / HandleUseBindEquipItemButton /
    /// HandleUseBindGemEquipItemButton, uimessagebox.cpp:1868-1925) find the item in the global inventory (g_143ab2a90) and
    /// use or put it on, whether or not a window is open. Here they go to the last inventory and character window controllers:
    /// those stay after their window closes, and what the confirm reads (the live inventory, the HUD model) is not the
    /// window's. Set on the one message box layer when the HUD is built and again at each window open (the layer may be made
    /// later); cleared when the HUD leaves, since the layer outlives it. No controller yet: the handler's 0 (no box of that
    /// kind can be up).
    /// </summary>
    void BindBoxAnswers()
    {
        if (MessageBoxLayer.Instance?.Controller is not { } boxes || unbindBoxAnswers != null && boundBoxes == boxes) return;
        unbindBoxAnswers?.Invoke();
        Func<long, bool> use = uid => inventory?.ConfirmBindUse(uid) ?? false;
        Func<long, uint, uint, bool> equip = (uid, slot, mode) => inventory?.ConfirmBindEquip(uid, slot, mode) ?? false;
        Func<long, int, bool> gem = (uid, slot) => myInfo?.ConfirmBindGemEquip(uid, slot) ?? false;
        boxes.UseBindFuncItem = use; boxes.UseBindEquipItem = equip; boxes.UseBindGemEquipItem = gem;
        boundBoxes = boxes;
        unbindBoxAnswers = () =>
        {
            if (boxes.UseBindFuncItem == use) boxes.UseBindFuncItem = null;
            if (boxes.UseBindEquipItem == equip) boxes.UseBindEquipItem = null;
            if (boxes.UseBindGemEquipItem == gem) boxes.UseBindGemEquipItem = null;
            boundBoxes = null;
        };
    }
    Action unbindBoxAnswers;
    Ms2.Ui.Hud.MessageBoxController boundBoxes;
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
        // the hover compares with the own character's equipment (MyPC); Shift is CUIService m_n128 (either Shift key)
        var me = live != null ? live.Me : (offlineMe ??= UiFixture.Character());
        Func<long> now = () => live?.GameSession?.ServerTime ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var c = new Ms2.Ui.Hud.InventoryController
        {
            View = view,
            BaseSlots = Ms2Godot.Net.BagSlotTable.Load(assets.Packs),
            MaxSlots = Ms2Godot.Net.BagSlotTable.LoadMax(assets.Packs),
            Wearer = () => me?.ToPcInfo(now()),
            LocalPlayerDead = () => me.Dead,              // PCBase::IsDead (FieldAddUser, UserState 12 / DeadUser, Revival)
            ShiftHeld = () => Input.IsKeyPressed(Key.Shift),
            ActionKeyDown = ActionKeyDown,
        };
        c.ToggleDialog += id => GD.Print($"inventory: dialog 0x{id:x} is not in this HUD");
        c.Button += name => GD.Print($"inventory: {name} is not sent (display only)");
        c.SystemNotice += SystemNotice;
        BindBoxAnswers();
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

    Ms2.Ui.Skills.JobSystem slotJobs;
    object slotJobsBook;
    Ms2.Ui.Hud.MainPlayerInfoController playerInfo;
    ulong slotRefreshTick;
    /// <summary>The local player's AdditionalCliList (its buff strip part).</summary>
    Ms2.Ui.Skills.AdditionalBuffList ownBuffs;
    Action unwatchBuffs;

    /// <summary>
    /// The local player's buffs onto the main player info panel (AddBuffUI / UpdateBuffUI / RemoveBuffUI): live, the session's
    /// own FieldAddUser list and OnBuff packets on the server tick; offline, <see cref="StubHudData.Buffs"/> on the local tick.
    /// </summary>
    void OwnBuffs()
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        ownBuffs = new Ms2.Ui.Skills.AdditionalBuffList(hudModel.Tables.Additional.GetLevel) { Strip = playerInfo };
        playerInfo.IsUpperTargetId = ownBuffs.IsUpperTargetId;
        if (live == null)
        {
            ownBuffs.ServerTick = () => (uint)System.Environment.TickCount64;
            ownBuffs.UpperTargetId = data.ObjectId;
            foreach (var b in data.Buffs) ownBuffs.Apply(b);
            return;
        }
        ownBuffs.ServerTick = () => live.GameSession is { } gs ? unchecked((uint)gs.ServerTick) : 0;
        Action<Ms2.Net.Packets.BuffPacket> onBuff = b =>
        {
            ownBuffs.UpperTargetId = live.UpperTargetId;
            if (b == null) ownBuffs.ClearAll();
            else ownBuffs.Apply(b);
        };
        foreach (var b in live.TakeBuffs()) onBuff(b);
        live.BuffReceived += onBuff;
        unwatchBuffs = () => live.BuffReceived -= onBuff;
    }
    Action unwatchQuickSlotState;
    Ms2.Ui.Skills.WeaponActionTable weaponActions;

    /// <summary>
    /// The live events that change what the quick slots show. The client queues a gray refresh on a bag item added or
    /// removed (UIMainPlayerInfo::vfunc_21 / vfunc_22, uimainplayerinfo.cpp:839), after UpdateSlotForItem rebinds the item's
    /// slot (:1357); here a bag change queues the gray state and item count of every slot. The skill book (the own
    /// FieldAddUser's, which arrives after the HUD is built, or a Job packet) and a key table packet rebind every slot.
    /// </summary>
    void WatchQuickSlotState()
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        if (live == null) return;
        Action<int> onBook = _ => playerInfo.RebindSlots();
        Action<long> onBag = _ => playerInfo.QueueSlotRefresh(Ms2.Ui.Hud.MainPlayerInfoController.RefreshGray | Ms2.Ui.Hud.MainPlayerInfoController.RefreshItemCount);
        Action<int> onKeys = _ => playerInfo.RebindSlots();
        live.SkillBookChanged += onBook;
        live.Items.Changed += onBag;
        live.KeyTableChanged += onKeys;
        unwatchQuickSlotState = () => { live.SkillBookChanged -= onBook; live.Items.Changed -= onBag; live.KeyTableChanged -= onKeys; };
    }
    Ms2Godot.Net.ItemKinds slotKinds;
    Ms2.Ui.Skills.SlotField slotField;
    int? slotFieldId;

    /// <summary>
    /// The player state the HUD quick slots read (SkillSlot): the live session's bag, skill book and own hands, else the offline
    /// inventory and a new character's book of the HUD's job; the field data of the current map id (offline: the field of the
    /// HUD's xblock). Cooldowns and the skill set have no feed yet and keep their idle values; the buffs only feed the buff strip.
    /// </summary>
    Ms2.Ui.Skills.SlotWorld QuickSlotWorld()
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        var tables = hudModel.Tables.Skills;
        object book = live != null ? live.SkillBook : (object)hudModel.Player.SubJob;
        if (slotJobs == null || !Equals(slotJobsBook, book))
        {
            slotJobs = new Ms2.Ui.Skills.JobSystem(tables) { IsFeatureEnabled = hudModel.IsFeatureEnabled };
            slotJobs.Read(live != null ? live.SkillBook : UiFixture.NewCharacterSkillBook(tables.Jobs, hudModel.Player.SubJob / 10));
            slotJobsBook = book;
        }
        slotKinds ??= new Ms2Godot.Net.ItemKinds(assets.Packs);
        int? fieldId = live?.FieldId;
        if (slotField == null || slotFieldId != fieldId)
        {
            slotFieldId = fieldId;
            FieldData fd = null;
            try { fd = fieldId is { } id ? FieldData.FindById(assets.Packs, id) : FieldData.FindByXBlock(assets.Packs, xblock); }
            catch (Exception e) { GD.PushWarning("quick slot field data: " + e.Message); }
            slotField = fd == null ? null : new Ms2.Ui.Skills.SlotField
            {
                Id = fd.Id, MapType = fd.MapType, CheckFly = fd.CheckFly, CheckClimb = fd.CheckClimb,
                UseEPSkill = fd.UseEPSkill, UsePet = fd.UsePet, SkillUseDisableMask = fd.SkillUseDisableMask,
            };
        }
        var p = hudModel.Player;
        // PC::GetWeaponAction: the action PC::UpdateWeaponAction (pc.cpp:2838) picks for the own hands, offline the fixture
        // character's. Its hands are the layer 5 item, else FUN_141655ee0(slot, 1), the layer 0 item for a PC with no clone;
        // the equips here are layer 0.
        // STILL-OPEN: no layer 5 hands, and the equips are FieldAddUser's (no ItemPutOn / ItemPutOff yet), so a weapon
        // change does not reach the slots.
        weaponActions ??= assets.Table("table/weaponaction.xml") is { } wa ? Ms2.Ui.Skills.WeaponActionTable.Load(wa) : Ms2.Ui.Skills.WeaponActionTable.Empty;
        var me = live != null ? live.Me : (offlineMe ??= UiFixture.Character());
        Ms2.Net.Packets.EquipItem Hand(int slot) => me.Equips?.FirstOrDefault(e => (int)e.Slot == slot);
        var world = new Ms2.Ui.Skills.SlotWorld
        {
            Tables = tables, ItemData = hudModel.Tables.Items.Get, Jobs = slotJobs, HasPc = hudModel.HasLocalPlayer,
            Level = p.Level, Hp = p.HpCur, Sp = p.SpCur, Ep = p.EpCur, Meso = p.Mesos,
            Inventory = live != null ? live.Items.All : (OfflineInventory() != null ? offlineBook.All : Array.Empty<Ms2.Net.Packets.EquipItem>()), TabOf = slotKinds.TabOf,
            Field = slotField,
        };
        world.Weapon = weaponActions.HeldAction(Hand(5), Hand(4), hudModel.Tables.Items.Get, world.CanUse) is { } action
            ? (action.RightHand, action.LeftHand) : null;
        return world;
    }

    Ms2.Net.Packets.ItemBook offlineBook;     // the offline inventory's items

    Ms2.Net.Packets.InventoryView OfflineInventory()
    {
        if (offlineInventory != null) return offlineInventory;
        var kinds = new Ms2Godot.Net.ItemKinds(assets.Packs);
        var book = new Ms2.Net.Packets.ItemBook { TabOf = kinds.TabOf, BaseSlots = Ms2Godot.Net.BagSlotTable.Load(assets.Packs) };
        foreach (var g in UiFixture.InventoryItems().GroupBy(i => kinds.TabOf(i.Id)))
            book.Apply(new Ms2.Net.Packets.ItemMessage.LoadTab(g.Key, g.ToList()));
        offlineBook = book;
        return offlineInventory = new Ms2.Net.Packets.InventoryView(book)
        {
            // no server: the requests are only logged
            RequestMove = (uid, slot) => GD.Print($"inventory: move {uid} to cell {slot} is not sent (offline)"),
            RequestEquip = (uid, slot, kind) => GD.Print($"inventory: equip {uid} at slot {slot} kind {kind} is not sent (offline)"),
            RequestUnequip = uid => GD.Print($"inventory: unequip {uid} is not sent (offline)"),
            RequestSwapEquip = (uid, slot) => GD.Print($"inventory: swap {uid} to slot {slot} is not sent (offline)"),
            RequestEquipGem = (uid, slot) => GD.Print($"inventory: gem {uid} at slot {slot} is not sent (offline)"),
        };
    }

    // ---- skill window: uiskillbooktreedialog run by its own ActionScript, UISkillBookTreeDialog's C++ side as SkillBookTreeController --

    // ---- npc shop: uishopdialog run by its own ActionScript, UIShopDialog's C++ side as ShopController ----------------

    MovieDialog shopMovie;
    Ms2.Ui.Shop.ShopController shop;
    string shopShot;                            // --shop all|weapons|armor|buyback: the fixture gear shop on that filter or tab

    /// <summary>
    /// Opens the npc shop over the fixture gear shop (<see cref="UiFixture.GearShop"/>, in the shop packet's shape): the
    /// header before the movie loads, the products after, as the server's commands 0 and 1 arrive. Buy, sell and restock
    /// requests are only logged.
    /// </summary>
    void OpenShop()
    {
        if (shopMovie != null) { shopMovie.Host.Unload(); shopMovie.QueueFree(); shopMovie = null; }
        long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var (info, products) = UiFixture.GearShop(Now());
        var c = new Ms2.Ui.Shop.ShopController
        {
            Categories = new Ms2.Ui.Shop.ShopCategoryTable(assets.Packs),
            Npcs = World.FieldContents.Npcs(assets.Packs),
            ServerTime = Now,
            ShopSeedResetTime = (int)Ms2.Ui.FeatureElements.Constants(assets.Packs).Float("ShopSeedResetTime", 0),
            ItemUseError = itemUse.Error,
        };
        c.Request += (name, _) => GD.Print($"shop: {name} is not sent (display only)");
        c.OpenDialog += id => GD.Print($"shop: dialog 0x{id:x} is not in this HUD");
        c.ReadShopInfo(info);
        var m = new MovieDialog(assets, c, hudModel, GetWindow().Size);
        c.ReadProducts(products);
        // the movie goes when the close tween ends (OnEndClose -> Unload), as the other dialogs' movies do
        c.Unloaded += () =>
        {
            m.Host.Unload();
            m.QueueFree();
            if (shopMovie == m) shopMovie = null;
        };
        shop = c; shopMovie = m;
        uiRoot.AddChild(m);
    }

    /// <summary>The shot's filter or tab, picked through the movie's own list and tab bar (their change events call back).</summary>
    void PickShopView(string view)
    {
        if (shopMovie == null || shop?.ShopView == null) return;
        var h = shopMovie.Host;
        if (view == "buyback") { h.Set(h.Get(shop.ShopView, "m_tab"), "selectedIndex", 1); return; }
        // shopcategory.xml category 5: the Weapons and Armor tabs' name ids
        int nameId = view switch { "weapons" => 2017, "armor" => 2018, _ => 0 };
        if (nameId == 0) return;
        int row = shop.Tabs(0).ToList().FindIndex(r => r.Tab.NameId == nameId);
        if (row >= 0) h.Set(h.Get(shop.ShopView, "m_categoryList"), "selectedIndex", row);
    }

    MovieDialog skillBookMovie;                 // from open until OnEndClose
    Ms2.Ui.Hud.SkillBookTreeController skillBook;
    int[] skillShot;                            // --skillbook subJob[,level]
    string quickSlotShot;                       // --quickslots slot:skill[:item[:uid]];...
    int[] quickSlotPlayer;                      // --quickslots-player subJob[,level]: the shot's job and level

    /// <summary>The shot's key table: a new character's packet with page 0 holding the given quick slot entries.</summary>
    static Ms2.Net.Packets.ServerKeyTable QuickSlotShotTable(string spec)
    {
        var entries = spec.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(e => e.Split(':').Select(long.Parse).ToArray())
            .Select(v => ((int)v[0], new Ms2.Net.Packets.QuickSlotEntry((int)v[1], v.Length > 2 ? (int)v[2] : 0, v.Length > 3 ? v[3] : 0))).ToArray();
        var t = new Ms2.Net.Packets.ServerKeyTable();
        t.Apply(new Maple2.PacketLib.Tools.ByteReader(Ms2.Net.Packets.ServerKeyTable.Write(0, true, null, 0, null)));
        t.Apply(new Maple2.PacketLib.Tools.ByteReader(Ms2.Net.Packets.ServerKeyTable.Write(7, false, null, 0,
            new IReadOnlyList<(int, Ms2.Net.Packets.QuickSlotEntry)>[] { entries })));
        return t;
    }

    /// <summary>CUIService::ToggleDialog(0x4f2d): open when closed, else vfunc_67.</summary>
    void ToggleSkillBook()
    {
        if (skillBook == null || skillBook.IsClosed) OpenSkillBook();
        else skillBook.Close();
    }

    /// <summary>
    /// The player a skill tooltip reads (ASTooltip's owner PC): its job system, level, quests, quick slots and hands. The hands
    /// are PC::GetEquipItemLayer0(5 / 4) (pc.cpp:1340), the own FieldAddUser's layer 0 equips (RH, LH), offline none.
    /// </summary>
    Ms2.Ui.Skills.SkillTooltipPlayer SkillTooltipPlayer(Ms2.Ui.Skills.JobSystem jobs)
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        return new Ms2.Ui.Skills.SkillTooltipPlayer
        {
            Jobs = jobs,
            Level = hudModel.Player.Level,
            IsQuestRewarded = id => live?.Quests.Get((int)id)?.State == 2,
            QuestName = id => questTable?.QuestName((int)id) ?? "",
            QuickSlotOf = id => hudModel.ServerKeys?.FindQuickSlotBySkillAndItemUid((int)id, 0) ?? -1,
            EquipSubtype = slot => live?.Me.Equips?.FirstOrDefault(e => (int)e.Slot == slot) is { } eq
                ? hudModel.Tables.Items.Get(eq.Id)?.EquipSubtype ?? 0 : 0,
        };
    }

    /// <summary>
    /// Opens the skill window over the live session's skill book and skill points, asking the server for the saved tabs
    /// (RequestSkillBookTree mode 0, a read request) and answering the dialog with its 0x10C reply; with no live session, over a
    /// new character's book of the HUD's job (<see cref="UiFixture.NewCharacterSkillBook"/>) and a reply with no tabs. A live
    /// session without its book yet opens nothing. Everything that would change the book is only logged.
    /// </summary>
    void OpenSkillBook()
    {
        if (skillBookMovie != null) { skillBookMovie.Host.Unload(); skillBookMovie.QueueFree(); skillBookMovie = null; }
        var live = Ms2Godot.Net.LiveSession.Instance;
        var tables = hudModel.Tables.Skills;
        var book = live != null ? live.SkillBook : UiFixture.NewCharacterSkillBook(tables.Jobs, hudModel.Player.SubJob / 10);
        if (book == null) { GD.Print("skill book: the live session has no skill book yet"); return; }
        var jobs = new Ms2.Ui.Skills.JobSystem(tables) { IsFeatureEnabled = hudModel.IsFeatureEnabled };
        jobs.Read(book);
        var c = new Ms2.Ui.Hud.SkillBookTreeController
        {
            JobChange01 = hudModel.IsFeatureEnabled(0xca),
            Jobs = jobs,
            Points = live?.SkillPoints ?? new Ms2.Ui.Skills.SkillPoints(),
            IsQuestRewarded = id => live?.Quests.Get((int)id)?.State == 2,
        };
        // ASTooltip(pPlayer, 0x10, 2).FUN_1409e1c50(id, level, 1) (uiskillbookautodistributedialog.cpp:2061)
        var tipPlayer = SkillTooltipPlayer(jobs);
        c.SkillTooltip = (d, id, level) =>
        {
            tipPlayer.Level = hudModel.Player.Level;
            tipPlayer.DialogSimulator = d.Simulator;
            return Ms2.Ui.Skills.SkillTooltip.Build(hudModel.Tables, tipPlayer, id, level, 0x10, true);
        };
        c.Button += name => GD.Print($"skill book: {name} is not sent (display only)");
        Action<Ms2.Net.Packets.SkillBookTreePacket> onTree = c.OnSkillBookTree;
        // the status events the open window listens to (UISkillBookTreeDialog vfunc_oa0_7 / 9 / 28): the local player's job
        // packet modes 0, 1, 2 and 7 close it, a level change and the skill points refresh the shown preview
        Action<int> onJob = mode => { if (mode is 0 or 1 or 2 or 7) c.OnJobChanged(); };
        Action onPoints = c.OnSkillPointsChanged;
        Action<Ms2.Ui.StatusEvent> onStatus = e => { if (e == Ms2.Ui.StatusEvent.Level) c.OnLevelChanged(hudModel.Player.Level); };
        hudModel.Changed += onStatus;
        if (live != null)
        {
            c.RequestTree += live.RequestSkillBookTree;
            live.SkillBookTree += onTree;
            live.SkillBookChanged += onJob;
            live.SkillPointsChanged += onPoints;
        }
        else c.RequestTree += () => Callable.From(() => c.OnSkillBookTree(UiFixture.NoSkillTabs())).CallDeferred();
        var m = new MovieDialog(assets, c, hudModel, GetWindow().Size);
        c.Unloaded += () =>
        {
            hudModel.Changed -= onStatus;
            if (live != null) { live.SkillBookTree -= onTree; live.SkillBookChanged -= onJob; live.SkillPointsChanged -= onPoints; }
            m.Host.Unload();
            m.QueueFree();
            if (skillBookMovie == m) skillBookMovie = null;
        };
        skillBook = c; skillBookMovie = m;
        uiRoot.AddChild(m);
    }

    // ---- MS2_PERF: the timing run (PerfLog) -------------------------------------------------------------------------

    string perfState = "loading";
    int perfFrame = -1;
    static readonly string[] PerfDialogs = (System.Environment.GetEnvironmentVariable("MS2_PERF_OPEN") ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Opens (or closes) one of the MS2_PERF_OPEN windows.</summary>
    void PerfToggle(string d, bool open)
    {
        switch (d)
        {
            case "inventory": ToggleInventory(); break;
            case "myinfo": ToggleMyInfo(); break;
            case "skill": ToggleSkillBook(); break;
            case "quest": ToggleQuestLog(); break;
            case "worldmap": ToggleWorldMap(); break;
            case "shop": if (open) OpenShop(); else shop?.Close(); break;
            case "options": if (open) OpenGameOption(0); else CloseAllDialogs(); break;
        }
    }

    static readonly bool PerfMem = !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("MS2_PERF_MEM"));
    static readonly int PerfMemHold = int.TryParse(System.Environment.GetEnvironmentVariable("MS2_PERF_MEM_HOLD"), out var h) ? h : 0;

    /// <summary>
    /// MS2_PERF_MEM: memory lines (<see cref="PerfLog.Memory"/>) after 600 idle frames (field with the HUD), then for each
    /// window in MS2_PERF_OPEN: 100 frames after opening it, 100 frames after closing it, and 100 frames after 20 more
    /// open/close cycles (10 frames apart). Then MS2_PERF_MEM_HOLD idle frames ("mem hold" printed first, for a heap dump
    /// from outside), and MS2_PERF_QUIT quits.
    /// </summary>
    void PerfMemory(string label)
    {
        PerfLog.Memory(label);
        lock (MovieDialog.Opened)
        {
            var alive = MovieDialog.Opened.Where(x => x.Player.TryGetTarget(out _)).GroupBy(x => x.Movie).Select(g => $"{g.Key} {g.Count()}");
            PerfLog.Print($"mem [{label}] players made {MovieDialog.Opened.Count}, alive: {string.Join(", ", alive)}");
        }
    }

    void PerfMemScript()
    {
        const int Per = 700;
        int at = perfFrame - 600;
        if (at == 0) { PerfMemory("field + HUD"); return; }
        if (at < 0) return;
        int i = at / Per, t = at % Per;
        if (i >= PerfDialogs.Length)
        {
            int e = at - PerfDialogs.Length * Per;
            if (e == 0) { PerfMemory("end"); PerfLog.Print($"mem hold {PerfMemHold} frames, pid {System.Environment.ProcessId}"); }
            if (e == PerfMemHold + 1 && System.Environment.GetEnvironmentVariable("MS2_PERF_QUIT") != null) GetTree().Quit(0);
            return;
        }
        string d = PerfDialogs[i];
        if (t == 1) PerfToggle(d, true);
        else if (t == 100) { PerfMemory(d + " open"); PerfToggle(d, false); }
        else if (t == 199) PerfMemory(d + " closed");
        else if (t >= 200 && t <= 200 + 39 * 10 && (t - 200) % 10 == 0) PerfToggle(d, (t - 200) / 10 % 2 == 0);
        else if (t == 690) PerfMemory(d + " after 20 cycles");
    }

    /// <summary>
    /// After the field is loaded: 600 idle frames, then for each window in MS2_PERF_OPEN (inventory, myinfo, skill, quest,
    /// worldmap, shop, options) an open (cold), a close, a second open (warm) and a close, 100 frames apart, each open's
    /// whole call timed; then MS2_PERF_QUIT quits.
    /// </summary>
    void PerfScript()
    {
        perfFrame++;
        if (perfFrame == 0) { perfState = "idle"; PerfLog.Restart(); }
        if (PerfMem) { PerfMemScript(); return; }
        int at = perfFrame - 600;
        if (at >= PerfDialogs.Length * 400) { PerfTail(at - PerfDialogs.Length * 400); return; }
        if (at < 0 || at % 100 != 0) return;
        int i = at / 400, step = at / 100 % 4;
        string d = PerfDialogs[i];
        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool open = step % 2 == 0;
        PerfToggle(d, open);
        PerfLog.Print($"{(open ? (step == 0 ? "cold open" : "warm open") : "close")} {d}: call {sw.Elapsed.TotalMilliseconds:0.0} ms");
        perfState = open ? d + " open" : "idle";
        PerfLog.Restart();
    }

    /// <summary>
    /// After the windows: 200 idle frames, 200 with a mouse move each frame (no window open), then inventory, character and skill
    /// book opened and 200 frames idle and 200 with mouse moves; then inventory opened and closed 10 times, a full collection, and
    /// the count of movie players still alive by movie.
    /// </summary>
    void PerfTail(int t)
    {
        void Mode(string s) { perfState = s; PerfLog.Restart(); }
        if (perfState.StartsWith("mouse")) Input.ParseInputEvent(new InputEventMouseMotion { Position = new Vector2(300 + t % 400, 200 + t % 300), Relative = new Vector2(1, 1) });
        switch (t)
        {
            case 0: CloseAllDialogs(); Mode("idle, 0 open"); break;
            case 200: Mode("mouse, 0 open"); break;
            case 400: ToggleInventory(); ToggleMyInfo(); ToggleSkillBook(); Mode("idle, 3 open"); break;
            case 500: Mode("idle, 3 open"); break;
            case 700: Mode("mouse, 3 open"); break;
            case 900: CloseAllDialogs(); skillBook?.Close(); Mode("idle"); break;
        }
        if (t == 990 || t == 1250) PerfViews(t);
        if (t >= 1000 && t < 1200 && t % 10 == 0) ToggleInventory();
        if (t == 1250)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            lock (MovieDialog.Opened)
            {
                var alive = MovieDialog.Opened.Where(x => x.Player.TryGetTarget(out _)).GroupBy(x => x.Movie).Select(g => $"{g.Key} {g.Count()}");
                PerfLog.Print($"players made {MovieDialog.Opened.Count}, alive after GC: {string.Join(", ", alive)}");
            }
            PerfLog.Print($"managed heap after GC {GC.GetTotalMemory(false) / 1048576.0:0} MB");
            if (As3View.CheckRuns > 0) PerfLog.Print($"MS2_PERF_SYNCCHECK: {As3View.CheckRuns} full syncs, {As3View.CheckDiffs} differences");
        }
        if (t == 1300 && System.Environment.GetEnvironmentVariable("MS2_PERF_QUIT") != null) GetTree().Quit(0);
    }

    public override void _Process(double delta)
    {
        PerfLog.Frame(perfState);
        ChatLog.Process();                                    // UIMessengerBrowser::vfunc_15
        foreach (var (_, c) in messengers) c.Process();      // UIMessenger::vfunc_15
        if (inventory != null && !inventory.IsClosed) inventory.Process();     // UIInventoryDialog::vfunc_15
        shop?.Process();                                                        // UIShopDialog::vfunc_72
        if (myInfo != null && !myInfo.IsClosed) myInfo.Process((long)Time.GetTicksMsec());     // UIMyInfoDialog::vfunc_15
        (notice?.Host.Controller as NoticeDialogController)?.Update((uint)Time.GetTicksMsec());     // UINoticeDialog::vfunc_72
        popupMenu?.Update();       // UIPopUpMenu::vfunc_15
        ugc?.Update();             // CUgcManager::Update: the picked file's refresh
        if (uploadDialog is { IsClosed: false } upload) upload.Tick();     // UIUGCImageUploadDialog::vfunc_72
        alignWidgets?.ProcessPendingLayout();      // the end of UISwfEntityManager::UpdateEntities
        playerInfo?.UpdateEffects();      // UIMainPlayerInfo::vfunc_15 (uimainplayerinfo.cpp:625): m_effectManager->Update()
        // UIMainPlayerInfo::vfunc_15 (uimainplayerinfo.cpp:639): the queued slot refresh, when its 200 ms timer fires
        if (playerInfo != null && Time.GetTicksMsec() - slotRefreshTick >= 200)
        {
            slotRefreshTick = Time.GetTicksMsec();
            playerInfo.FlushSlotRefresh();
        }
        if (!Field.LoadComplete) return;            // the field builds over several frames behind the loading screen
        if (minimap == null && minimapSlot != null && GetTree().GetFirstNodeInGroup(World.FieldContents.Group) is World.FieldContents { Portals: not null } mfc) OpenMinimap(mfc);
        minimapSide?.Process(Time.GetTicksMsec());
        upperHudSide?.Process(Time.GetTicksMsec());
        TargetBar?.Process();
        SystemText?.Process();               // UISystemText::vfunc_15: the timed lines' expiry
        worldMapSide?.Process();             // UIWorldMapDialog::vfunc_72: FlushQueuedTiles
        if (PerfLog.On) PerfScript();
        if (shotDir == null) return;
        if (questShot != null && frames == 200) OpenQuestLog();
        if (questShot != null && frames == 230 && questShot.Length > 2) ClickQuest(questShot[2]);
        if (sysmenuShot >= 0 && frames == 200) Escape();
        if (worldMapShot >= 0 && frames == 200) ToggleWorldMap();
        if (openShot == "inventory" && frames == 200) ToggleInventory();
        if (openShot == "myinfo" && frames == 200) ToggleMyInfo();
        if (skillShot != null && frames == 200)
        {
            // the shot's character: sub job and level (the capture's Lv 1 Archer is 500,1)
            hudModel.Player.SubJob = skillShot[0];
            if (skillShot.Length > 1) hudModel.Player.Level = skillShot[1];
            ToggleSkillBook();
        }
        if (shopShot != null && frames == 200)
        {
            hudModel.Player.Mesos = 1_001_000;          // the capture's purse
            OpenInventory();
            OpenShop();
        }
        if (shopShot != null && frames == 210) shop?.PlaceBesideInventory(inventory, inventory != null && !inventory.IsClosed);
        if (shopShot != null && frames == 230) PickShopView(shopShot);
        if (worldMapShot >= 1 && frames == 240) worldMapSide?.SetPendingSearch("Henesys");
        if (worldMapShot >= 2 && frames == 260) worldMapSide?.OnCallback("onClickSearchInfo", new object[] { 2000076u });
        if (optionShot >= 0 && frames == 200) OpenGameOption(optionShot);
        if (sysmenuShot >= 1 && frames == 230) ClickSystemMenu("m_selectChannel");
        if (sysmenuShot >= 2 && frames == 240) ClickSystemMenu("m_channel");
        if (msgboxShot == 1 && frames == 200) Escape();
        if (msgboxShot == 1 && frames == 230) ClickSystemMenu("m_exitGame");
        if (msgboxShot == 2 && frames == 230) optionDialog?.OpenResetBox();               // the movie's OnInit (Defaults)
        if (msgboxShot == 3 && frames == 230) optionHost?.MessageBox(0x84b, "OnSaveAndCheck");   // OnCloseDialog with a page changed
        if (frames == 250 && messengers.Count > 0) TypeChat(messengers[0], "HelloWorld");
        if (hoverShot != null && frames == 260) Hover(hoverShot);
        if (dragShot != null && frames == 200) { ToggleMyInfo(); ToggleInventory(); }
        if (dragShot != null && frames == 220) PlaceBeside(inventoryMovie, myInfoMovie);
        if (dragShot != null && frames >= 240 && frames <= 270) DragStep(frames - 240);
        if (partyMenuShot != 0 && frames >= 240 && frames <= 244) ShotPartyMenu(partyMenuShot, frames - 240);
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
            // "prop=value" picks the descendant whose AS property has that value (e.g. m_skillID=10500021), else by name
            if (d != o && (name.IndexOf('=') is var eq and > 0
                    ? Ms2.Avm2.Rt.ToString(h.Get(d, name[..eq])) == name[(eq + 1)..]
                    : Ms2.Avm2.Rt.ToString(h.Get(d, "name")) == name)) return d;
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
        if (parts[1].StartsWith('@'))       // "@x,y": the pointer at that window point (in the movie's stage units)
        {
            var xy = parts[1][1..].Split(',').Select(float.Parse).ToArray();
            var view = movie.GetChildren().OfType<As3View>().FirstOrDefault();
            var p = view != null ? view.GetGlobalTransformWithCanvas().AffineInverse() * new Vector2(xy[0], xy[1]) : new Vector2(xy[0], xy[1]);
            movie.Player.MouseMove(p.X, p.Y);
            return;
        }
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

    /// <summary>
    /// The drag shot, one step a frame, through the window's own input: the pointer onto the source slot, the left button
    /// down, 20 moves to the target slot, the shot drag.png (mid-drag, over the target), then the button up.
    /// </summary>
    void DragStep(int k)
    {
        if (k == 0)
        {
            var parts = dragShot.Split('>', 2);
            string from = parts[0] == "worn" && myInfo != null
                ? myInfo.GetBagSlotBindings(MyInfoController.EquipBag).FirstOrDefault(n => myInfo.WornSlot(n) != null) : parts[0];
            if (from == null || SlotPoint(from) is not { } a || SlotPoint(parts[1]) is not { } b) { GD.PushWarning("drag: no slot for " + dragShot); dragShot = null; return; }
            GD.Print($"drag: {from} -> {parts[1]}");
            (dragFrom, dragTo) = (a, b);
            SendMouse(dragFrom, null);
        }
        else if (k == 5) SendMouse(dragFrom, true);
        else if (k >= 6 && k <= 25) SendMouse(dragFrom.Lerp(dragTo, (k - 5) / 20f), null);
        else if (k == 26)
        {
            DirAccess.MakeDirRecursiveAbsolute(shotDir);
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(shotDir, "drag.png"));
        }
        else if (k == 28) SendMouse(dragTo, false);
    }

    /// <summary>The drag shot's layout, as a player would drag the window: <paramref name="beside"/>'s dialog near the left edge and
    /// <paramref name="moved"/>'s right of it, top aligned (both movies share the stage's coordinates).</summary>
    static void PlaceBeside(MovieDialog moved, MovieDialog beside)
    {
        if (moved?.Host.Entity is not Ms2.Avm2.Flash.AsDisplayObject m || beside?.Host.Entity is not Ms2.Avm2.Flash.AsDisplayObject b) return;
        b.x += 40 - b.GetBounds(null).X;
        var r = b.GetBounds(null);
        var own = m.GetBounds(null);
        m.x += r.X + r.W + 8 - own.X;
        m.y += r.Y - own.Y;
    }

    /// <summary>A window point on the slot bound to <paramref name="binding"/> in any hosted movie (one its hit test reaches).</summary>
    Vector2? SlotPoint(string binding)
    {
        foreach (var movie in FindMovies(GetTree().Root))
            if (Descendant(movie.Host, movie.Player.Stage, "binding=" + binding) is { } target && WindowPoint(movie, target) is { } p)
                return p;
        return null;
    }

    /// <summary>A window point on <paramref name="target"/> in <paramref name="movie"/> that its hit test reaches: the middle
    /// when it hits there, else the first hit on a 2-pixel grid; null when none does.</summary>
    static Vector2? WindowPoint(MovieDialog movie, Ms2.Avm2.Flash.AsDisplayObject target)
    {
        var view = movie.GetChildren().OfType<As3View>().FirstOrDefault();
        var r = target.GetBounds(null);
        for (double y = r.Y + 2; y < r.Y + r.H; y += 2)
            for (double x = r.X + 2; x < r.X + r.W; x += 2)
                for (var t = (Ms2.Avm2.Flash.AsDisplayObject)movie.Player.MouseTarget(x, y); t != null; t = t.Parent)
                    if (t == target)
                    {
                        var c = new Vector2((float)(r.X + r.W / 2), (float)(r.Y + r.H / 2));
                        var p = movie.Player.MouseTarget(c.X, c.Y) is Ms2.Avm2.Flash.AsDisplayObject hit && IsUnder(hit, target) ? c : new Vector2((float)x, (float)y);
                        return view != null ? view.GetGlobalTransformWithCanvas() * p : p;
                    }
        return null;
    }

    static bool IsUnder(Ms2.Avm2.Flash.AsDisplayObject o, Ms2.Avm2.Flash.AsDisplayObject ancestor)
    {
        for (; o != null; o = o.Parent) if (o == ancestor) return true;
        return false;
    }

    /// <summary>A mouse event at a window point, through Godot's input (the pointer moved there first): a press or release of
    /// <paramref name="button"/>, or a move when <paramref name="press"/> is null.</summary>
    void SendMouse(Vector2 at, bool? press, MouseButton button = MouseButton.Left)
    {
        Input.WarpMouse(at);
        var mask = button == MouseButton.Right ? MouseButtonMask.Right : button == MouseButton.Middle ? MouseButtonMask.Middle : MouseButtonMask.Left;
        InputEvent e = press is bool down
            ? new InputEventMouseButton { ButtonIndex = button, Pressed = down, Position = at, GlobalPosition = at, ButtonMask = down ? mask : 0 }
            : new InputEventMouseMotion { Position = at, GlobalPosition = at, ButtonMask = Input.IsMouseButtonPressed(MouseButton.Left) ? MouseButtonMask.Left : 0 };
        Input.ParseInputEvent(e);
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

    /// <summary>MS2_PERF: every movie view in the tree (processed each frame), with whether it is drawn.</summary>
    void PerfViews(int t)
    {
        var list = new List<string>();
        void Walk(Node n)
        {
            if (n is As3View v) list.Add($"{v.Player.Main.Key}{(v.IsVisibleInTree() ? "" : " (hidden)")}");
            foreach (var c in n.GetChildren()) Walk(c);
        }
        Walk(GetTree().Root);
        PerfLog.Print($"t={t}: {list.Count} movie views in the tree: {string.Join(", ", list)}");
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
