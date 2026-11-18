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
    /// <summary>FUN_1418dd0c0 + FUN_1418dd110 (uibuiltindialog.cpp): the two bottom bars' height, (DAT_1439f8ccc - 2) +
    /// (DAT_1439f8cd0 - 2) = 19 + 19 with feature 0xbd (the adventure level bar) on.</summary>
    const float ExpBarLift = 21 - 2, AdventureBarLift = 21 - 2;
    string shotDir; int frames;
    string frameCase;                       // --frametime closed|open|scroll: frame times over 300 frames instead of a shot
    readonly List<double> frameDelta = new(), frameProcess = new(), frameMovie = new(), frameTick = new();
    long callsAtStart, interpAtStart;
    float u; Vector2 stage;                 // UI scale and stage size in stage units (see UiScale)
    Player player; Control uiRoot;
    TargetFrame target;                     // UIUpperHpBar
    DamageText damageText;
    PlayerHpBar playerBar;
    DamageEffect damageEffect;              // UIDamageEffect, the low-HP screen effect
    int[] questShot;                        // --questlog TAB,SUBTAB[,QUEST]: the shot opens the quest log on these tabs, selecting the quest
    string questState;                      // --questlog-state LEVEL:ID,ID,..[:TRACKING]: the shot's quest state is these quests in progress at that level
    MovieDialog questLog;                   // UIQuestDialog, the quest log, run from its own movie while it is loaded
    Ms2.Ui.Hud.QuestDialogController questLogSide;   // its C++ side, kept while it is closed (m_180 / m_184 and the filters)
    Ms2.Ui.UiModel questLogModel;
    int optionShot = -1;                    // --uigen-options PAGE: the shot opens the options dialog on that page, as the system menu does
    int msgboxShot = -1;                    // --uigen-msgbox KIND: 1 the exit box (Esc, Exit Game); with --uigen-options, 2 the reset box, 3 the unsaved changes box
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
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--uigen-options") optionShot = int.Parse(args[i + 1]);
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--uigen-msgbox") msgboxShot = int.Parse(args[i + 1]);
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
        if (messageBox != null && MessageBoxQueue.Layer == messageBox) MessageBoxQueue.Layer = null;
    }

    Viewport sizeSource;            // the viewport whose size_changed relays the HUD out, while connected

    void QueueRelayout()
    {
        if (relayoutQueued) return;
        relayoutQueued = true;
        CallDeferred(MethodName.Relayout);
    }

    CanvasLayer hudLayer; Node[] worldUi = System.Array.Empty<Node>();   // the HUD layer and the world-anchored UI layers
    PortraitView portrait;          // the menu's face portrait, handed to the next build on a relayout
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
        minimap?.GetParent()?.RemoveChild(minimap);           // so is the minimap
        questGuide?.GetParent()?.RemoveChild(questGuide);     // and the quest tracker
        foreach (var (m, _) in messengers) m.GetParent()?.RemoveChild(m);     // and the chat windows
        groupMessenger?.GetParent()?.RemoveChild(groupMessenger);
        upperHud?.GetParent()?.RemoveChild(upperHud);         // and the top bar
        hudLayer?.QueueFree();
        foreach (var n in worldUi) n.QueueFree();
        target = null;
        BuildHud();
        if (minimap != null) { minimapSlot.AddChild(minimap); minimap.Relayout(GetWindow().Size); }
        foreach (var (m, _) in messengers) m.Relayout(GetWindow().Size);
        groupMessenger?.Relayout(GetWindow().Size);
        questLog?.Relayout(GetWindow().Size);
        systemMenuMovie?.Relayout(GetWindow().Size);
        optionMovie?.Relayout(GetWindow().Size);       // GFxManager::ResizeMovies, then the dialog's alignment
        messageBoxMovie?.Relayout(GetWindow().Size);
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

        NoticeTab(uiRoot);
        Minimap(uiRoot);
        NavigatorWidget(uiRoot);
        UpperHud(uiRoot);
        target = new TargetFrame(assets, uiRoot, u, stage);
        HudModel();
        Chat(uiRoot);
        Quests(uiRoot);
        PlayerInfo(uiRoot);
        GameMenu(uiRoot);
        SystemMenuFacts();
        MessageBoxLayer();
        if (questLog != null) uiRoot.AddChild(questLog);       // dialogs draw over the HUD (UIDialog m_e0: layer 4)
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
    /// <summary>The exit box's yes, UIMessageBox::HandleGameexitButton(0) @0x1419fb480: SendRequestQuitAndDisconnect, then
    /// PostQuitMessage(0).</summary>
    public static event Action GameExit;

    MessageBoxController messageBox;    // UIMessageBox (0x4e48), the message box layer, loaded with the HUD and kept
    MovieDialog messageBoxMovie;        // its movie (uimessagebox)
    CanvasLayer messageBoxLayer;        // over the dialogs

    /// <summary>
    /// The message box layer: the client creates UIMessageBox with the game scene and keeps it; MessageBoxFactory's configUI
    /// hides the root until a box opens. Boxes reach it through <see cref="MessageBoxQueue"/>.
    /// </summary>
    void MessageBoxLayer()
    {
        if (messageBox != null) return;
        if (!UiCode.Load(Settings.DataDir)) GD.PushWarning("UI code is not built for this data: the message box runs interpreted");
        menuModel ??= new UiModel(new GameTables(assets.Packs, "en"));
        MessageBoxData.CommonByKey = commonStrings;
        var c = new MessageBoxController();
        c.GameExit += () => GameExit?.Invoke();
        c.UnportedTag += (b, t) => GD.Print($"message box: button {b} of tag {t}: its handler is not ported");
        AddChild(messageBoxLayer = new CanvasLayer { Layer = 12 });
        messageBoxMovie = new MovieDialog(assets, c, menuModel, GetWindow().Size);
        messageBoxLayer.AddChild(messageBoxMovie);
        messageBox = c;
        MessageBoxQueue.Layer = c;
    }

    /// <summary>OnGameExit (FUN_1418fc760, uichatballoon.cpp:467): FUN_140d73bc0(0x288 s_msg_game_over, 0x17, 0, "gameexit").</summary>
    static void ShowGameExitBox(Func<string, string> common) =>
        MessageBoxQueue.ShowMessageBox(common("s_msg_game_over") ?? "", 0x17, null, "gameexit");

    /// <summary>A scripted click on the shown box's button <paramref name="n"/> (m_button0..2, the centre of its bounds).</summary>
    public void ClickMessageBox(int n)
    {
        if (messageBoxMovie == null || messageBox?.IsIdle != false) return;
        var h = messageBoxMovie.Host;
        var boxes = h.Get(h.Entity, "m_msgBoxArray");          // MessageBoxFactory.getCurMsgBox: the last box
        int count = Ms2.Avm2.Rt.ToInt32(h.Get(boxes, "length"));
        if (count == 0) return;
        var box = h.Get(boxes, (count - 1).ToString());
        if (h.Child("m_button" + n, box) is not Ms2.Avm2.Flash.AsDisplayObject d) { GD.PushWarning($"message box: no m_button{n}"); return; }
        var b = d.GetBounds(null);
        messageBoxMovie.Player.Click(b.X + b.W / 2, b.Y + b.H / 2);
    }

    public bool MessageBoxShown => messageBox?.IsIdle == false;

    /// <summary>UIMessageBox::vfunc_13 @0x1419f7560 while a box is shown: Enter, Esc, the arrows and Tab go to the movie.</summary>
    bool MessageBoxKeys(InputEvent e)
    {
        if (messageBox == null || messageBox.IsIdle) return false;
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return false;
        string key = k.Keycode switch
        {
            Key.Enter or Key.KpEnter => "enter",
            Key.Escape => "escape",
            Key.Up => "up",
            Key.Left => "left",
            Key.Down => "down",
            Key.Right => "right",
            Key.Tab => "tab",
            _ => null,
        };
        return key != null && messageBox.ProcessKey(key);
    }

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
        menuModel ??= new UiModel(new GameTables(assets.Packs, "en"));
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
    bool AnyClosableDialogOpen() => (questLog != null && questLogSide?.IsClosed == false) || optionHost?.Vfunc55 == false;

    /// <summary>CUIService::CloseAllDialogs: closes every closable dialog (vfunc_67).</summary>
    void CloseAllDialogs()
    {
        if (questLog != null && questLogSide?.IsClosed == false) questLogSide.Close();
        if (optionHost?.Vfunc55 == false) optionHost.Movie.Close();
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
        menuModel ??= new UiModel(new GameTables(assets.Packs, "en"));
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
        foreach (int id in new[] { Ms2.Ui.Feature.SearchTooltip, Ms2.Ui.Feature.InputMode, Ms2.Ui.Feature.GamePad, Ms2.Ui.Feature.VersionText, Ms2.Ui.Feature.Merat })
            if (id <= Ms2.Ui.FeatureTable.Names.Length && Ms2.Ui.LobbyFeatures.IsEnabledByName(assets.Packs, Ms2.Ui.FeatureTable.Names[id - 1])
                && !data.FeaturesOff.Contains(Ms2.Ui.FeatureTable.Names[id - 1]))
                model.Features.Add(id);
        model.Player.Mesos = data.Mesos;
        model.Player.Merats = data.Merets;
        upperHudSide = new Ms2.Ui.Hud.UpperHudController();
        upperHud = new MovieDialog(assets, upperHudSide, model, GetWindow().Size);
        ui.AddChild(upperHud);
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
        var model = new Ms2.Ui.UiModel(new Ms2.Ui.GameTables(assets.Packs, "en"));
        model.Field.Name = MapName();
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
    /// UILevelUpPackageWidgetDialog ("Navigator", type name LevelUpPackageWidget): an event widget (uieventwidgetdialog).
    /// table/alignwidget.xml holds it in the alignType 6 widget at offsetX -298, so opening it requests the align layout
    /// (UISwfEntity::FUN_1422dc450 / vfunc_19) instead of its own default place (UIEventWidgetDialog::vfunc_43, top left at
    /// (0, 60)). UIAlignWidget::LayoutEntities -> UISwfEntity::vfunc_44 -> ASDisplayObject::vfunc_2(6, -298, 0): x = stage width
    /// - (int)width - 298, y = 0, with the widget's width from AS3 (EventWidgetDialog.get_width = m_dragBar.width). The widgets
    /// listed before it take their width off the offset only while shown; none is here. setData: title, reward slot, gauge or
    /// reward button.
    /// </summary>
    void NavigatorWidget(Control ui)
    {
        var r = Place(ui, "uieventwidgetdialog", 1f, Vector2.Zero);
        var dlg = r.AllClips().FirstOrDefault(c => c.Direct("m_title") != null) ?? r;     // the EventWidgetDialog instance
        // the movie draws the dialog mid-stage: dock its registration point instead (SetPositionInt of the dialog)
        int w = (int)dlg.ChildWidth(dlg.Direct("m_dragBar"));
        r.Position = (new Vector2(stage.X - 298 - w, 0) - dlg.Position) * u;
        dlg.SetNamedText("m_title", assets.Str("$s_levelup_package_widget"));
        if (dlg.Direct("m_title")?.Node is TextNode title) title.ShrinkToFit();     // TextFieldEx.setTextAutoSize(SHRINK)
        // EventWidgetDialog.setData: below max the gauge shows "text" and m_gage.width = current / max * (m_back.width - 4)
        int cur = data.NavigatorCurrent, max = data.NavigatorMax;
        bool ready = cur >= max;
        dlg.SetNamedVisible("m_rewardBtn", ready);
        dlg.SetNamedVisible("m_effect", ready);
        dlg.SetNamedVisible("m_gageBar", !ready);
        if (!ready && dlg.FindClip("m_gageBar") is { } gage)
        {
            gage.SetNamedText("m_text", $"{cur}/{max}");
            if (gage.Direct("m_gage") is { } fill && gage.Direct("m_back") is { } back)
            {
                // authored widths: the symbol's bounds times the placed x scale
                float DefWidth(MovieClipNode.Child c) =>
                    UiBuilder.F(r.Movie.Scope.Symbol((int)c.Def["id"])?["bounds"]?["w"], 0) * Math.Abs(UiBuilder.F(c.Def["matrix"]?["a"], 1));
                float full = DefWidth(back) - 4, fw = DefWidth(fill);
                var t = fill.Base;
                if (fw > 0) fill.Override = new Transform2D(t.X * (full * cur / max / fw), t.Y, t.Origin);
                gage.GotoFrame(gage.Frame);
            }
        }
        var slot = r.FindClip("m_slot");
        var icon = assets.ImageFile($"item/icon/{data.NavigatorRewardItem}.png");
        if (slot != null && icon != null)
        {
            var b = slot.ContentRect() ?? new Rect2(0, 0, 60, 60);
            var spr = new Sprite2D { Texture = icon, Centered = false, Position = b.Position, Scale = b.Size / icon.GetSize() };
            slot.AddChild(spr);
            // BaseSlot.setItemCount: an export_slot_itemcount at (0, 39) in m_iconOption, its textField holding the count
            if (data.NavigatorRewardCount > 0 && r.Movie.BuildClass("export_slot_itemcount", u) is { } count)
            {
                foreach (var c in count.AllClips()) foreach (var t in c.TextFields) t.Text = data.NavigatorRewardCount.ToString();
                count.Position = new Vector2(0, 39);
                (slot.FindClip("m_iconOption") ?? slot).AddChild(count);
            }
        }
    }

    /// <summary>
    /// UINoticeDialog, the web notice banner, closed: OnLoadComplete plays "hideComplete", which parks m_mainWindow off
    /// the left edge so only its m_showButton tab shows. vfunc_43 aligns it with vfunc_2(3, 0, 0): left, vertically
    /// centred at the dialog's AS3 height (UIComponent's, the instance bounds).
    /// </summary>
    void NoticeTab(Control ui)
    {
        var r = Place(ui, "uinoticedialog", 1f, Vector2.Zero);
        if (DialogBody(r) is not { } body) return;
        body.GotoLabel("hideComplete");
        body.FindClip("m_mainWindow.m_showButton")?.GotoLabel("up");
        var b = UiLayout.SymbolOfClass(r.Movie, "UINoticeDialog", out _)?["bounds"];
        AlignDialog(r, body, 3, new Vector2(UiBuilder.F(b?["w"], 905), UiBuilder.F(b?["h"], 1600)), 0, 0);
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
        if (MessageBoxKeys(e)) { GetViewport().SetInputAsHandled(); return; }
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
    /// action keys (IsActionKeyPressed 0x61a87, the quest log, action 400007: DIK 38, L, in table/default/defaultkey.xml)
    /// through CKeyTable::FUN_14142b300, which toggles the dialog (keytable.cpp: case 400007, ToggleDialog(0x4e3e)).
    /// UIDialog::vfunc_13: Escape closes an open dialog (vfunc_69 -> vfunc_67).
    /// </summary>
    bool DialogKeys(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k || UiFocus.IsTyping) return false;
        if (k.PhysicalKeycode == Key.L) { ToggleQuestLog(); return true; }       // a DirectInput key code is a physical key
        bool open = questLog != null && questLogSide?.IsClosed == false;
        if (k.Keycode == Key.Escape && open) { questLogSide.Close(); return true; }
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
        var model = new Ms2.Ui.UiModel(new Ms2.Ui.GameTables(assets.Packs, "en"));
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
            questLogModel = new Ms2.Ui.UiModel(new Ms2.Ui.GameTables(assets.Packs, "en"))
            { FeatureEnabledByName = f => Ms2.Ui.LobbyFeatures.IsEnabled(assets.Packs, f) };
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

    static FieldMissions fieldMissions;     // the quest and description tables, read once (6,780 quest files)
    static FieldMeta fieldMeta;


    // ---- HP/SP orb, skill slots and the EXP bar: their movies --------------------------------------------------------

    UiModel hudModel;
    MovieDialog playerInfoMovie, expBarMovie, adventureBarMovie;

    /// <summary>The HUD movies' model: the tables, features, key tables and the player.</summary>
    void HudModel()
    {
        if (!UiCode.Load(Settings.DataDir)) GD.PushWarning("UI code is not built for this data: the HUD movies run interpreted");
        hudModel = new UiModel(new GameTables(assets.Packs, "en"));
        hudModel.Features.Add(Ms2.Ui.Hud.MainPlayerInfoController.AdventureLevelFeature);
        hudModel.Keys = new KeyTable(assets.Packs, "en");
        // the server's key table when live (OnKeyTable), else the packet a new character gets
        hudModel.ServerKeys = Ms2Godot.Net.LiveSession.Instance?.KeyTable is { Loaded: true } live ? live : UiFixture.NewCharacterKeyTable();
        hudModel.Keys.Apply(hudModel.ServerKeys);
        var p = hudModel.Player;
        p.Name = data.Player.Name; p.Level = data.Player.Level;
        // jobCode is the sub job (uimainplayerinfo.cpp:1405): 100 Knight, 200 Berserker ... 1100 Soul Binder; the stub
        // numbers its jobs 1, 10, 20 ... 100
        p.SubJob = data.Player.Job <= 1 ? 100 : (data.Player.Job / 10 + 1) * 100;
        p.HpCur = data.Player.HpCurrent; p.HpMax = data.Player.HpMax;
        p.SpCur = data.SpCurrent; p.SpMax = data.SpMax;
        p.EpCur = data.EpCurrent; p.EpMax = data.EpMax;
        p.Exp = (long)Math.Round(hudModel.NextLevelExp * data.ExpPercent / 100);
    }

    /// <summary>
    /// UIMainPlayerInfo (uimainplayerinfo), UIExpBar (uiexpbar) and UIAdventureLevelExpBar (uiadventurelevelexpbar), each run from its own movie with its C++ side as the
    /// controller (Ms2.Ui.Hud). The model is the HUD's numbers (<see cref="StubHudData"/>); feature 0xbd is on, as the
    /// adventure level bar shows.
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

    void GameMenu(Control ui)
    {
        var r = Place(ui, "uigamemenudialog", 1f, Vector2.Zero);
        // UIGameMenuDialog::vfunc_43: vfunc_2(8, 5, -(FUN_1418dd0c0() - 6 + FUN_1418dd110())) with feature 0xbd on; the
        // dialog's size is UIComponent's, the bounds shown at its first frame when it was built (before configUI hides
        // m_quest). Those are 498 x 195: the symbol's 208.5 height also counts m_quest's glow on its later frames.
        if (DialogBody(r) is { } gmBody && gmBody.ContentRect() is { } gb)
            AlignDialog(r, gmBody, 8, gb.Size, 5, -(ExpBarLift - 6 + AdventureBarLift));
        // configUI hides the quest and pet attack buttons; no pet (initPetInfo) and no contract (initContract(true)) for now
        Hide(r, "m_quest", "m_petInfo", "m_petAttack", "m_contract");
        // OnLoadComplete -> LoadRenderTarget: the face portrait (faceView) in m_renderTarget
        if (DialogBody(r) is { } faceBody) portrait = PortraitView.Attach(faceBody, faceBody.Direct("m_renderTarget"), assets.Packs, Settings.NifXml, portrait);
        var menus = r.FindClip("m_menus");
        if (menus == null) return;
        // RefreshMenu -> addMenuEntity per top item; SystemDropDownContainer.addEntity builds the entry class, sets the icon
        // (getDefinitionByName("export_common_menu_" + layerName) into m_layerBtn and m_layerEffect) and the caption tag
        foreach (var item in GameMenuTable.TopItems(assets.Table("table/gamemenu.xml"), data.Locale, f => !data.FeaturesOff.Contains(f)))
        {
            var entry = r.Movie.BuildClass(item.Group ? "UIGameMenuDialog_group" : "UIGameMenuDialog_menu", u);
            if (entry == null) continue;
            entry.Name = item.Name;
            entry.Position = new Vector2(item.Col * 56, 120 - item.Row * 60);
            menus.AddChild(entry);
            menus.MoveChild(entry, 0);                                  // addChildAt(entry, 0): a later entry draws under the earlier ones
            var btn = entry.FindClip("m_button");
            if (btn != null)
            {
                btn.GotoLabel("up");
                Hide(btn, "m_notifyUp", "m_notifyCount", "m_notifyNew", "m_notifyRedCircle", "m_eventPopup", "m_hitArea");
                foreach (var layer in new[] { "m_layerBtn", "m_layerEffect" })
                {
                    var holder = btn.FindClip(layer);
                    if (holder == null) continue;
                    foreach (var n in holder.GetChildren()) n.QueueFree();
                    if (assets.Common.BuildClass("export_common_menu_" + item.Name, u) is { } icon) holder.AddChild(icon);
                }
            }
            // setMenuTag: tag.text, then TextFieldEx.setVerticalAlign(tag, VALIGN_BOTTOM)
            if (entry.Children.FirstOrDefault(c => c.Name == "tag")?.Node is TextNode tag) { tag.Text = assets.Str(item.Caption); tag.AlignBottom(); }
        }
    }

    public override void _Process(double delta)
    {
        foreach (var (_, c) in messengers) c.Process();      // UIMessenger::vfunc_15
        if (!Field.LoadComplete) return;            // the field builds over several frames behind the loading screen
        if (minimap == null && minimapSlot != null && GetTree().GetFirstNodeInGroup(World.FieldContents.Group) is World.FieldContents { Portals: not null } mfc) OpenMinimap(mfc);
        minimapSide?.Process(Time.GetTicksMsec());
        upperHudSide?.Process(Time.GetTicksMsec());
        target?.Process(delta);
        if (shotDir == null) return;
        if (questShot != null && frames == 200) OpenQuestLog();
        if (questShot != null && frames == 230 && questShot.Length > 2) ClickQuest(questShot[2]);
        if (sysmenuShot >= 0 && frames == 200) Escape();
        if (optionShot >= 0 && frames == 200) OpenGameOption(optionShot);
        if (sysmenuShot >= 1 && frames == 230) ClickSystemMenu("m_selectChannel");
        if (sysmenuShot >= 2 && frames == 240) ClickSystemMenu("m_channel");
        if (msgboxShot == 1 && frames == 200) Escape();
        if (msgboxShot == 1 && frames == 230) ClickSystemMenu("m_exitGame");
        if (msgboxShot == 2 && frames == 230) optionDialog?.FUN_141943a30();               // the movie's OnInit (Defaults)
        if (msgboxShot == 3 && frames == 230) optionHost?.MessageBox(0x84b, "OnSaveAndCheck");   // OnCloseDialog with a page changed
        if (frames == 250 && messengers.Count > 0) TypeChat(messengers[0], "HelloWorld");
        if (frameCase != null) { MeasureFrame(delta); return; }
        if (++frames == 300)
        {
            DirAccess.MakeDirRecursiveAbsolute(shotDir);
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(shotDir, "hud.png"));
            GetTree().Quit();
        }
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
