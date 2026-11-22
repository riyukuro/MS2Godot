using System.Text.Json.Nodes;
using Godot;
using Ms2.Avm2;
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
    uint noticeOpenTicks;               // NoticeDialogOpenSeconds
    readonly NewsNotificationRouter newsRouter = new();
    MovieDialog notice;                 // uinoticedialog (UINoticeDialog)
    /// <summary>FUN_1418dd0c0 + FUN_1418dd110 (uibuiltindialog.cpp): the two bottom bars' height, (DAT_1439f8ccc - 2) +
    /// (DAT_1439f8cd0 - 2) = 19 + 19 with feature 0xbd (the adventure level bar) on.</summary>
    const float ExpBarLift = 21 - 2, AdventureBarLift = 21 - 2;
    string shotDir; int frames;
    string frameCase;                       // --frametime closed|open|scroll: frame times over 300 frames instead of a shot
    readonly List<double> frameDelta = new(), frameProcess = new(), frameMovie = new(), frameTick = new();
    long callsAtStart, interpAtStart;
    float u;                                // the movies' draw scale (see UiScale)
    Player player; Control uiRoot;
    MovieDialog targetBar;                  // uiupperhpbar
    /// <summary>UIUpperHpBar's C++ side: the target HP bar at the top (Hit / Remove from combat, Process per frame).</summary>
    public Ms2.Ui.Hud.UpperHpBarController TargetBar { get; private set; }
    MovieDialog systemText;                 // uisystemtext
    /// <summary>UISystemText's C++ side: the system lines across the upper middle (the red warnings and the rest).</summary>
    public Ms2.Ui.Hud.SystemTextController SystemText { get; private set; }
    MovieDialog castingBar;                 // uicastingbar
    /// <summary>UICastingBar's C++ side: the cast gauge a caster starts (StartCasting) and is called back from.</summary>
    public Ms2.Ui.Hud.CastingBarController CastingBar { get; private set; }
    MovieDialog guideEvent;                 // uiguideevent
    /// <summary>UIGuideEvent's C++ side (0x4e53): the guide's tutorial balloon, summary panel, text tips and arrows.</summary>
    public Ms2.Ui.Hud.GuideEventController GuideEvent { get; private set; }
    DamageText damageText;
    PlayerHpBar playerBar;
    TombGaugeBar tombBar;
    DamageEffect damageEffect;              // UIDamageEffect, the low-HP screen effect
    int[] questShot;                        // --questlog TAB,SUBTAB[,QUEST]: the shot opens the quest log on these tabs, selecting the quest
    string questState;                      // --questlog-state LEVEL:ID,ID,..[:TRACKING]: the shot's quest state is these quests in progress at that level
    MovieDialog questLog;                   // UIQuestDialog, the quest log, run from its own movie while it is loaded
    Ms2.Ui.Hud.QuestDialogController questLogSide;   // its C++ side, kept while it is closed (m_180 / m_184 and the filters)
    Ms2.Ui.UiModel questLogModel;
    int[][] windowShot;                     // --uigen-window FULL,W,H,FRAME[;...]: the options dialog's window requests (screen mode / resolution changes) at those frames
    int optionShot = -1;                    // --uigen-options PAGE: the shot opens the options dialog on that page, as the system menu does
    long partyMenuShot;                     // --uigen-party-menu ID: the party slot menu of that member
    string hoverShot;                       // --uigen-hover MOVIE:a.b.c (or MOVIE:@x,y): the pointer on that child of a hosted movie, or at that point (its tooltip shows)
    int msgboxShot = -1;                    // --uigen-msgbox KIND: 1 the exit box (Esc, Exit Game); with --uigen-options, 2 the reset box, 3 the unsaved changes box
    int worldMapShot = -1;                  // --worldmap STEP: the shot opens the world map (M); 1 searches "Henesys", 2 clicks the result; 3 and 4 close it (3 by the key, 4 by its close button) after hovering a tile and the air taxi icon, three open/hover/close cycles
    int sysmenuShot = -1;                   // --uigen-sysmenu STATE: the shot opens the system menu with Esc; 1 shows the channel combo, 2 opens its list
    string openShot;                        // --uigen-open inventory|myinfo|levelupguide|gamedictionary[:tab:text]|achieve: the shot opens that window, as I, P or Y does
    string guideShot;                       // --uigen-guide summary:TEXTID|tip:TEXTID|tutorial:TEXTID: the guide pane shows that text
    int arrowShot;                          // --uigen-arrow 170|200|60540|210|10012052: the shot points that table arrow (the rows' event ids)
    string dragShot;                        // --uigen-drag FROM>TO: both windows open, then a mouse drag from one slot binding to another (FROM "worn": the first worn equip slot)
    Vector2 dragFrom, dragTo;
    static DungeonRooms dungeonRooms;
    bool damageShot;                        // --damageshot: hits, exp, meso and a quest item's gain on the own player
    bool questFixture;                      // --questlog-fixture: the shot's quest log lists made-up quest state (QuestShotState)

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--hudshot") shotDir = args[i + 1];
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--frametime") frameCase = args[i + 1];
        if (System.Array.IndexOf(args, "--oracle") >= 0) Ms2.Avm2.Interp.UseOracle = true;
        if (frameCase != null) DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        PerfLog.ApplyFpsCap();
        toggleDynamicAction = ToggleDynamicAction;
        ChatLog.ToggleDynamicAction = toggleDynamicAction;   // the messenger's eventEmotion
        chatEmoticonFixture = args.Contains("--chatemoticon-fixture");
        // FUN_14116d7a0's mask 2, CUIService::IsTextFieldFocused (@0x141adbb00, uiservice.cpp:1364): a field of the dialogs'
        // stage (main.gfx) takes typing, or a Godot text control does
        Ms2.Ui.InputBlock.TextFieldFocused = () => UiStage.TextFieldFocused || UiFocus.IsTyping;
        questFixture = args.Contains("--questlog-fixture");
        damageShot = args.Contains("--damageshot");
        partyFixture = args.Contains("--party-fixture");
        minimapPcsFixture = args.Contains("--minimap-pcs-fixture");
        groupChatFixture = args.Contains("--groupchat-fixture");
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--questlog") questShot = args[i + 1].Split(',').Select(int.Parse).ToArray();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--questlog-state") questState = args[i + 1];
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--uigen-sysmenu") sysmenuShot = int.Parse(args[i + 1]);
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--uigen-open") openShot = args[i + 1];
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--uigen-guide") guideShot = args[i + 1];
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--uigen-arrow") arrowShot = int.Parse(args[i + 1]);
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--uigen-drag") dragShot = args[i + 1];
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--worldmap") worldMapShot = int.Parse(args[i + 1]);
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--uigen-window") windowShot = Array.ConvertAll(args[i + 1].Split(';'), r => Array.ConvertAll(r.Split(','), int.Parse));
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--uigen-options") optionShot = int.Parse(args[i + 1]);
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--uigen-msgbox") msgboxShot = int.Parse(args[i + 1]);
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--uigen-hover") hoverShot = args[i + 1];
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--uigen-party-menu") long.TryParse(args[i + 1], out partyMenuShot);
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--quickslots") quickSlotShot = args[i + 1];
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--quickslots-player") quickSlotPlayer = args[i + 1].Split(',').Select(int.Parse).ToArray();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--shop") shopShot = args[i + 1];
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--ui-folder" && args[i + 1].Split(',') is [var acct, var chr])
            CharacterFolder.Shot = new CharacterFolder(long.Parse(acct), long.Parse(chr));   // the dialogs' saved positions
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--skillbook") skillShot = args[i + 1].Split(',').Select(int.Parse).ToArray();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--skillcompact") skillCompactShot = args[i + 1].Split(',').Select(int.Parse).ToArray();
        var dataDir = Settings.DataDir;
        if (string.IsNullOrEmpty(dataDir)) { GD.PushError("Set MS2_DATA (the client's Data folder), or fill in local.cfg."); GetTree().Quit(2); return; }
        assets = new UiAssets(PackSet.Shared(dataDir));   // the field's pack set: its archives and the per-pack-set UI caches are shared

        xblock = World.FieldContents.PendingXBlock ?? ArgField() ?? Field.StartField;   // as Field picks it; the field scene consumes the move
        xblockFieldId = XblockFieldId();
        AddChild(fieldScene = GD.Load<PackedScene>("res://field.tscn").Instantiate());      // the field scene, untouched
        BuildHud();
        WatchParty();
        WatchPartySearch();
        WatchPcInfo();
        WatchItemMerge();
        // the portal and interact actions' notices & casting bar, the channel notices
        if (Ms2Godot.Net.LiveSession.Instance is { } livePortals)
        {
            livePortals.SystemNoticeSink = SystemNoticeText;
            livePortals.ChannelNoticeSink = ChannelNotice;
            livePortals.ChannelsChanged += ChannelsChanged;
            livePortals.CastingBar = CastingBar;
            livePortals.ItemRank = id => hudModel?.Tables?.Items?.Get(id)?.Rank ?? 0;
            livePortals.ItemExists = id => hudModel?.Tables?.Items is not { } items || items.Get(id) != null;
            livePortals.CommonBoxSink = id => MessageBoxQueue.ShowCommon(id, 0, null, "");
            livePortals.TaxiKey = TaxiKey;
            livePortals.ReactorRewardSink = (id, grade) => EnsureRewardNotify()?.ShowItemReward(id, grade);
        }
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
        MovieDialog.SceneResizer = this;    // the HUD resizes every movie, the message box and tooltip layers' too
        ScreenMode.AdoptWindow();       // started on its own: the options dialog shows this window (ScreenModes.Adopt)
        Ms2Godot.Net.ProfileImageService.Images.Ready += ProfileImageReady;
    }

    /// <summary>Feature 0xb5 (the game pad feature) from the data packs.</summary>
    bool PadFeature() => Ms2.Ui.LobbyFeatures.IsEnabledByName(assets.Packs, Ms2.Ui.FeatureTable.Names[0xb5 - 1]);

    /// <summary>The pad kind the key table's default loads follow (OnKeyTable mode 0, keytable.cpp:296-319): null with feature 0xb5 off.</summary>
    Func<int> PadDefaultsKind() => PadFeature() ? () => GameOptionHost.DetectPadKind(true) : null;

    /// <summary>MS2InputService::SetGamePadEnabled (ms2inputservice.cpp:604-614): no pad found after DetectGamePad shows the type 1 box
    /// "s_gamepad_notice_search_gamepad". Its callback (@0x140d8c8d0) only re-enumerates the devices on OK, which Godot does by itself.</summary>
    void OnGamepadSearchNotice()
    {
        if (GameOptionHost.DetectPadKind(PadFeature()) != 0 || commonStrings == null) return;
        Ms2.Ui.Hud.MessageBoxQueue.ShowMessageBox(commonStrings("s_gamepad_notice_search_gamepad"), 1, null, "");
    }

    /// <summary>SetGamepadEnabled's HUD calls: MainPlayerInfo ChangeSkillSlot, then UpperHud SetGamePadMode.</summary>
    void OnGamepadEnabled(bool use)
    {
        // MS2InputService::SetGamePadEnabled runs first (@0x140d8b900): with the pad on and no pad key set, the pad file loads
        if (use) hudModel?.Keys?.LoadDefaultPadKeysIfUnset(GameOptionHost.DetectPadKind(PadFeature()));
        playerInfo?.SetGamePadMode(use);
        upperHudSide?.SetGamePadMode(use);
    }

    /// <summary>
    /// Leaving the tree: the size handler is dropped first, because restoring the window's content scale mode emits the root
    /// viewport's size_changed at once, while this scene and its field are already on their way out.
    /// </summary>
    public override void _ExitTree()
    {
        GameOptions.GamepadEnabledChanged -= OnGamepadEnabled;
        GameOptions.GamepadSearchNotice -= OnGamepadSearchNotice;
        if (Ms2Godot.Net.LiveSession.Instance is { } livePortals)
        {
            if (livePortals.SystemNoticeSink == (Action<string>)SystemNoticeText) livePortals.SystemNoticeSink = null;
            if (livePortals.CastingBar == CastingBar) livePortals.CastingBar = null;
            if (livePortals.TaxiKey == (Action)TaxiKey) livePortals.TaxiKey = null;
        }
        unwatchDamage?.Invoke();
        unwatchDamage = null;
        unwatchMinimapPing?.Invoke();
        unwatchMinimapPing = null;
        CloseQuestNavigation();
        unwatchMinimapPcs?.Invoke();
        unwatchMinimapPcs = null;
        if (Ms2Godot.Net.LiveSession.Instance is { } liveChannel) { liveChannel.ChannelsChanged -= ChannelsChanged; if (liveChannel.ChannelNoticeSink == (Action<int, int>)ChannelNotice) liveChannel.ChannelNoticeSink = null; }
        RevivalUnwatch();
        unwatchQuickSlotState?.Invoke();
        unwatchBuffs?.Invoke();
        unwatchQuickSlotState = null;
        unwatchParty?.Invoke();
        unwatchParty = null;
        unwatchPartySearch?.Invoke();
        unwatchPartySearch = null;
        unwatchPartyDps?.Invoke();
        unwatchPartyDps = null;
        unwatchAchieves?.Invoke();
        unwatchAchieves = null;
        unwatchMails?.Invoke();
        unwatchMails = null;
        unwatchTrades?.Invoke();
        unwatchTrades = null;
        unwatchBlackMarket?.Invoke();
        unwatchBlackMarket = null;
        unwatchShop?.Invoke();
        unwatchShop = null;
        unwatchBeauty?.Invoke();
        unwatchPcInfo?.Invoke();
        unwatchPcInfo = null;
        unwatchBuddies?.Invoke();
        unwatchBuddies = null;
        unwatchGuild?.Invoke();
        unwatchGuild = null;
        unwatchGemErrors?.Invoke();
        unwatchGemErrors = null;
        CastingBar?.Hide();                 // leaving the game scene (FUN_141adad20, uiservice.cpp:884): vfunc_3
        GuideEvent?.Hide();                 // UIGuideEvent::vfunc_3 (:877): the tips are dropped with the scene
        if (Ms2Godot.Net.LiveSession.Instance is { } liveSession) { liveSession.GuidePane = null; liveSession.FindDialogOf = null; liveSession.DungeonRoomOf = null; }
        unwatchNotices?.Invoke();
        unwatchNotices = null;
        if (liveHome != null) { liveHome.HomeInfoApplied -= OnHomeInfoApplied; UnbindCubeBill(liveHome); liveHome = null; }
        unwatchChat?.Invoke();
        unwatchChat = null;
        unwatchNewItems?.Invoke();
        unwatchNewItems = null;
        unwatchLearningQuest?.Invoke();
        unwatchLearningQuest = null;
        unwatchItemMerge?.Invoke();
        unwatchItemMerge = null;
        unwatchPrestige?.Invoke();
        unwatchPrestige = null;
        unwatchCharacterAbility?.Invoke();
        unwatchCharacterAbility = null;
        unwatchFishingBook?.Invoke();
        unwatchFishingBook = null;
        unwatchMeso?.Invoke();
        unwatchMeso = null;
        foreach (var (_, c) in messengers) ChatLog.Windows.Remove(c);
        ChatLog.WindowCreated -= OnMessengerCreated;
        ChatLog.WindowClosed -= OnMessengerClosed;
        if (ReferenceEquals(ChatLog.ToggleDynamicAction, toggleDynamicAction)) ChatLog.ToggleDynamicAction = null;
        if (offlineChat != null && ReferenceEquals(ChatLog.SendPacket, offlineChat)) ChatLog.SendPacket = null;
        offlineChat = null;
        UnhookChatEmoticon();
        if (groupBrowser != null)
        {
            if (ReferenceEquals(ChatLog.GroupLink, groupLink)) ChatLog.GroupLink = null;
            groupBrowser.Book.Messenger = () => null;     // a live session's book outlives this scene
        }
        unwatchLivePlayer?.Invoke();
        unwatchLivePlayer = null;
        unbindBoxAnswers?.Invoke();
        unbindBoxAnswers = null;
        Ms2Godot.Net.ProfileImageService.Images.Ready -= ProfileImageReady;
        if (sizeSource != null) { sizeSource.SizeChanged -= QueueRelayout; sizeSource = null; }
        if (MovieDialog.SceneResizer == this) MovieDialog.SceneResizer = null;
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

    PortraitView portrait;          // the game menu's face portrait, kept across relayouts
    bool relayoutQueued;

    /// <summary>
    /// The window was resized or its screen mode changed. The client builds no dialog again: the render device is reset
    /// and every movie and dialog is kept (CUIService::OnDeviceReset @0x141ade1f0, uiservice.cpp:2829, only stores the new
    /// size and the movies' scale). On the restore, in the order the reset callbacks were added:
    /// <list type="number">
    /// <item>GFxService::vfunc_17 (gfxservice.cpp:101) -> GFxManager::ResizeMovies (gfxmanager.cpp:158): every movie keeps
    /// its stage size from before (UpdateStageSize), gets the new viewport (GFxMovie::vfunc_7) and a SceneResize, the AS3
    /// stage's Event.RESIZE (<see cref="MovieDialog.ResizeViewport"/>);</item>
    /// <item>UISwfEntityManager::vfunc_5 (uiswfentitymanager.cpp:483): every loaded entity's vfunc_17, an open dialog
    /// keeping its position's ratio to the stage (UIDialog::vfunc_17, uidialog.cpp:764);</item>
    /// <item>then UISwfEntityManager::vfunc_1 (:413): every loaded entity's layout, FUN_1422dc450 (uiswfentity.cpp:347);</item>
    /// <item>CUIService::OnDeviceReset: DAT_143af62d4 / DAT_143af62d8 = the new size, then GFxManager::SetMoviesScale
    /// (gfxmanager.cpp:754) with FUN_141276490(width, the UI scale option), stored on every movie but the background.</item>
    /// </list>
    /// The world UI layers (damage effect, the own name tag and HP bar, damage text, and the <see cref="IMovieLayer"/> group:
    /// other players' and npcs' name tags and HP bars) are movies too and take the same new viewport scale, kept with their
    /// state.
    /// STILL-OPEN: SetMoviesScale only stores the scale (GFxMovie::SetScale, gfxmovie.cpp:252) and GFxService's callback,
    /// added at its OnInit before CUIService's (uiservice.cpp:1886), sets the viewports first, so in the client the new
    /// scale may reach the viewports only at the next vfunc_7 or FUN_1422aff10. The callback order between the two services'
    /// OnInit is not traced; here each movie takes the new scale with its new viewport (UiScale.For(window)).
    /// </summary>
    void Relayout()
    {
        relayoutQueued = false;
        if (!IsInsideTree()) return;     // queued before the scene left the tree
        var window = ScreenMode.PixelSize(GetWindow());
        u = UiScale.For(window);
        resizing.Clear();
        CollectMovies(this, resizing);
        foreach (var m in new[] { MessageBoxLayer.LayerMovie, TooltipLayer.LayerMovie })     // the root's message box and tooltip layers
            if (m != null && m.IsInsideTree()) resizing.Add(m);
        var before = new (double W, double H)[resizing.Count];
        for (int i = 0; i < resizing.Count; i++) before[i] = resizing[i].ResizeViewport(window);   // ResizeMovies
        damageEffect?.Align(window);
        playerNameTag?.Resize(window);
        playerBar?.Resize(window);
        damageText?.Resize(window);
        foreach (var n in GetTree().GetNodesInGroup(IMovieLayer.Group))     // the other world UI movies (ResizeMovies covers every movie)
            if (n is IMovieLayer layer) layer.Resize(window);
        for (int i = 0; i < resizing.Count; i++) resizing[i].RestoreDevice(before[i]);            // vfunc_5: each vfunc_17
        for (int i = 0; i < resizing.Count; i++) resizing[i].LayoutLoaded();                      // vfunc_1: each layout
        resizing.Clear();
    }

    readonly List<MovieDialog> resizing = new();

    /// <summary>The scene's loaded movies (GFxManager's m_mapMovies), in tree order. The field's own movies follow its
    /// viewport themselves (NpcEmoticons), so the field is not walked; the message box and tooltip layers, under the root,
    /// are added by <see cref="Relayout"/>.</summary>
    void CollectMovies(Node n, List<MovieDialog> into)
    {
        for (int i = 0, count = n.GetChildCount(); i < count; i++)
        {
            var c = n.GetChild(i);
            if (c == fieldScene) continue;
            if (c is MovieDialog m) into.Add(m);
            CollectMovies(c, into);
        }
    }

    /// <summary>
    /// Lays the HUD out for the window. The client draws its movies into the whole back buffer: every movie's viewport is the
    /// active render target's size (GFxMovie::vfunc_6 / vfunc_7, GFxManager::ResizeMovies), one pixel larger on every side,
    /// with no letterbox and no fixed design size; the dialogs align themselves on the stage that gives (UiScale).
    /// </summary>
    void BuildHud()
    {
        var layer = new CanvasLayer { Layer = 10 };
        AddChild(layer);
        uiRoot = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        uiRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(uiRoot);
        var window = GetViewport().GetVisibleRect().Size;
        u = UiScale.For(window);
        layer.Offset = UiScale.MovieOrigin;                                  // the movies' viewport (GFxMovie::vfunc_7)
        var constants = Ms2.Ui.FeatureElements.Constants(assets.Packs);
        AddChild(damageEffect = new DamageEffect(assets, constants, window, GameOptions.Current.InterfaceOption.DamageEffectAlpha));                             // UIDamageEffect
        damageEffect.Hp(data.Player.HpCurrent, data.Player.HpMax);
        var nameTag = playerNameTag = new PlayerNameTag(assets, constants, data.Player.Name, window);     // the own PC's UINameTag
        AddChild(nameTag);
        AddChild(playerBar = new PlayerHpBar(assets, constants, nameTag, window));                        // and its UIHpBar
        playerBar.Spawn(data.Player.HpCurrent, data.Player.HpMax);
        AddChild(tombBar = new TombGaugeBar(assets, constants, () => nameTag.Anchor, window)                          // UITombStoneBar (0x4e64)
        {
            Tomb = () => Ms2Godot.Net.LiveSession.Instance?.Tomb,
            Dead = () => Ms2Godot.Net.LiveSession.Instance?.Me.Dead == true,
            RevivalBlocked = () => Ms2Godot.Net.LiveSession.Instance?.OwnRevivalBlocked == true,
        });
        AddChild(damageText = new DamageText(assets, constants, window));                // UIDamageText
        WatchDamageNumbers();
        WatchMinimapPings();

        HudModel();
        WatchOwnSkill();
        ScreenMask();
        NoticeTab(uiRoot, constants);
        QuestNavigationSlot(uiRoot);
        SmartPushStrip(uiRoot);
        Minimap(uiRoot);
        UpperHud(uiRoot);
        TargetHpBar(uiRoot);
        SystemTextLines(uiRoot);
        CastingGauge(uiRoot);
        GuideEventPane(uiRoot);
        HudModel();
        Chat(uiRoot);
        Quests(uiRoot);
        PlayerInfo(uiRoot);
        GameMenu(uiRoot);
        EventWidgets(uiRoot);
        LearningQuest();
        if (unwatchPrestige == null) WatchPrestige();
        CharacterAbility();
        if (unwatchMeso == null) WatchMeso();
        Mastery();
        SystemMenuFacts();
        MessageBoxLayer.Ensure(this);       // the client's one message box layer (CUIService::CreateAllDialogs)
        BindBoxAnswers();
        TooltipLayer.Ensure(this);          // and its one tooltip movie (GFxManager LoadTooltipMovie)
    }

    CanvasLayer screenMaskLayer;        // CommonDepth.ScreenMask (10): over the damage text and action guide, under the HUD
    MovieDialog screenMask;             // UIScreenMask (0x4e55), kept across relayouts

    /// <summary>
    /// UIScreenMask (uiscreenmask.cpp), shown at field entry: UISceneGame::vfunc_55 (WorldLoadComplete, @0x141ac78a0) runs
    /// FindObject(0x4e55)->vfunc_2 = LoadSwf(10). The scene's first field makes it here; a field move unloads it
    /// (<see cref="WorldUnLoadComplete"/>: vfunc_56 runs its vfunc_3, FUN_1422dc080) and the new field's load complete makes
    /// it again with that field's bgMask (<see cref="WorldLoadComplete"/>). Every field loads it; one without a bgMask
    /// makes no load call, so it shows nothing. A resize lays it out again (vfunc_43).
    /// </summary>
    void ScreenMask()
    {
        if (screenMask != null) return;
        if (worldLoadPending) return;       // unloaded by a field move: made again at the new field's load complete
        string bgMask = "";
        try { bgMask = FieldData.FindByXBlock(assets.Packs, xblock)?.BgMask ?? ""; } catch (Exception ex) { GD.PushWarning("screen mask field data: " + ex.Message); }
        // between the world UI layers (5) and the HUD (10); the movie draws only while its CommonDepth layer is shown
        screenMaskLayer = new CanvasLayer { Layer = 9, Offset = UiScale.MovieOrigin };
        MovieLayerHost.Bind(screenMaskLayer, Ms2.Ui.CommonDepth.ScreenMask);
        AddChild(screenMaskLayer);
        screenMask = new MovieDialog(assets, new Ms2.Ui.Hud.ScreenMaskController(bgMask), hudModel, ScreenMode.PixelSize(GetWindow()));
        screenMaskLayer.AddChild(screenMask);
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
        Ms2.Ui.Hud.QuitFlow.ShowExitConfirm(common);

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

    /// <summary>GetCurrentChannel: the live session's, else the offline server's one channel (1).</summary>
    static int LiveChannel() => Ms2Godot.Net.LiveSession.Instance is { } l && l.CurrentChannel != 0 ? l.CurrentChannel : 1;

    /// <summary>GetFieldChannelGroupId (@0x141bd98f0): the field's channel group id, 0 without field data.</summary>
    int FieldChannelGroup()
    {
        try { return FieldData.FindByXBlock(assets.Packs, xblock)?.ChannelGroupId ?? 0; }
        catch (Exception e) { GD.PushWarning("channel group: " + e.Message); return 0; }
    }

    /// <summary>UIServerListDialog::BuildChannelList's rows for the current server (DynamicChannel kind 1: ascending, those above the field's limit left out), else just the current one.</summary>
    ChannelRow[] LiveChannels()
    {
        if (Ms2Godot.Net.LiveSession.Instance is not { } l || l.ChannelTabs.Count == 0) return new[] { new ChannelRow(LiveChannel()) };
        var ids = l.ChannelIds(FieldChannelGroup());
        var rows = new ChannelRow[ids.Count];
        for (int i = 0; i < rows.Length; i++) rows[i] = new ChannelRow(ids[i]);
        return rows;
    }

    /// <summary>The server event listeners' Notify (UIServerEventDispatcher::vfunc_1, after a DynamicChannel): the open system menu and the minimap rebuild their channel lists.</summary>
    void ChannelsChanged()
    {
        if (systemMenu is { IsClosed: false } menu)
        {
            menu.CurrentChannel = LiveChannel();
            menu.Channels = LiveChannels();
            menu.Notify();
        }
        if (minimapSide != null)
        {
            var rows = LiveChannels();
            var ids = new int[rows.Length];
            for (int i = 0; i < ids.Length; i++) ids[i] = rows[i].Id;
            minimapSide.Model.Field.Channel = LiveChannel();
            minimapSide.Model.Field.Channels = ids.Length > 0 ? ids : new[] { LiveChannel() };
            minimapSide.ChannelGroupName = menuGroup;
            minimapSide.Notify();
        }
    }

    /// <summary>WorldService::FUN_141bc80c0 for the dialogs' OnChangeChannel: false offline (there is no server to ask).</summary>
    static bool ChangeChannel(int channel) => Ms2Godot.Net.LiveSession.Instance?.RequestChannel(channel) ?? false;

    /// <summary>A notice by common string id: OnServerEnter's 0x2d7 is a chat line (FUN_1412751b0(text, 0x14)); the
    /// refusals of FUN_141bc80c0 are system notices (FUN_141275260(id, 1, 1)).</summary>
    void ChannelNotice(int id, int arg)
    {
        string text = hudModel.Tables.Common(Ms2.Formats.Text.CommonStringIds.Key(id));
        if (string.IsNullOrEmpty(text)) return;
        if (arg >= 0) text = text.Replace("{0}", arg.ToString());
        if (id == 0x2d7) ChatLog.AddSystemLine(text, Ms2.Ui.Hud.TalkKind.SystemNotice);
        else SystemNoticeText(text);
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
            CurrentChannel = LiveChannel(),
            Channels = LiveChannels(),
            ChangeChannel = ChangeChannel,
            RedCircle = agreementReminder,   // m_198, kept by SetRedCircle (the dialog is made on each open here)
        };
        c.SelectCharacter += () => SystemMenuSelectCharacter?.Invoke();
        c.GameExit += () => ShowGameExitBox(commonStrings);
        c.Leave += t => GD.Print($"system menu: leave field (map type {t}): OnLeaveFieldClick is not ported");
        c.OpenGameOption += OpenGameOption;
        if (systemMenuLayer == null) AddChild(systemMenuLayer = new CanvasLayer { Layer = 11 });
        var m = new MovieDialog(assets, c, menuModel, ScreenMode.PixelSize(GetWindow()));
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
    /// Esc in the field, FUN_1418fcb70 (uichatballoon.cpp:523-555), in its order as far as this HUD has its parts
    /// (<see cref="Ms2.Ui.Hud.FieldEsc.Run"/> holds the UI's own three): the game option dialog takes it first, the
    /// shown tooltips close (GFxManager::CloseTooltips), the cut-scene movie and fishing are not in it, then the
    /// guide's text tip takes Esc (SkipTextTip); a focused text field keeps Esc (the chat input takes it in _Input), and
    /// an NPC talk takes it itself (FUN_141275080 is 0 while talking). With a closable dialog open
    /// (FUN_141274ff0, unit_141274d10_uihelpers.cpp) Esc closes all of them when the interface option EnableCloseAllDlg is
    /// on and is otherwise left; with none it toggles the system menu: ToggleDialog(0x4e21) = FUN_1419246f0, open when
    /// vfunc_55 (closed and unloaded), else vfunc_67 (close). STILL-OPEN: the cut-scene movie (0x4e7e,
    /// UICutSceneMovie::FUN_1419102c0) between the tooltips and the guide's tip.
    /// </summary>
    public void Escape()
    {
        if (assets == null) return;
        if (Ms2.Ui.Hud.FieldEsc.Run(
                () => optionDialog != null && optionDialog.OnKey(true),            // UIGameOptionDialog::vfunc_13 @0x141942b10 takes Esc
                () => TooltipLayer.Instance?.Controller?.DestroyTooltips() == true,  // CloseTooltips, the first step
                () => GuideEvent?.SkipTextTip() == true))                            // the guide's text tip (FUN_1418fcb70, uichatballoon.cpp:531)
            return;
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
                                    || inventory?.IsClosed == false || myInfo?.IsClosed == false
                                    || AchieveOpen || MailOpen || MailSendOpen || PcInfoOpen
                                    || CommunityOpen || CommunityQueryOpen || KeySettingSelectOpen;

    /// <summary>CUIService::CloseAllDialogs: closes every closable dialog (vfunc_67).</summary>
    void CloseAllDialogs()
    {
        if (questLog != null && questLogSide?.IsClosed == false) questLogSide.Close();
        if (WorldMapOpen) worldMapSide.Close();
        if (optionHost?.IsClosed == false) optionHost.Movie.Close();
        if (inventory?.IsClosed == false) inventory.Close();
        if (myInfo?.IsClosed == false) myInfo.Close();
        if (AchieveOpen) achieve.Close();
        if (MailOpen) mail.Close();
        if (MailSendOpen) mailSend.Close();
        if (PcInfoOpen) pcInfo.Close();
        if (CommunityQueryOpen) communityQuery.Close();
        if (CommunityOpen) community.Close();
        if (KeySettingSelectOpen) keySettingSelect.Close();
        if (SkillCompactOpen) skillCompact.Close();
    }

    Ms2.Formats.Options.GameOptionDialog optionDialog;      // UIGameOptionDialog (0x4ef3), its C++ side, kept between opens as the client keeps it
    GameOptionHost optionHost;
    MovieDialog optionMovie;            // its movie (uigameoptiondialog), from open until OnEndClose

    /// <summary>
    /// UISystemMenuDialog::OpenGameOptionDialog @0x141b14560 (page 1 or 6) and FUN_1418fc760's OnGameOption (page 0, no
    /// SetTab): SetTab, then CUIService::OpenDialog(0x4ef3) = FUN_141924680, which opens only a closed dialog (vfunc_65
    /// @0x141924a10 checks vfunc_55).
    /// </summary>
    bool agreementReminder;             // the agreement red circle on the menu icons (CPersonalInfoAgreement::FUN_141262090)

    /// <summary>FUN_141262090's menu icons: UIGameMenuDialog::SetNotifyRedCircle7Flag and UISystemMenuDialog::SetRedCircle.</summary>
    void SetAgreementReminder(bool show)
    {
        agreementReminder = show;
        gameMenu?.SetNotifyRedCircle7Flag(show);
        if (systemMenu != null) systemMenu.RedCircle = show;
    }

    /// <summary>The options host and its dialog model, made on first use (the house visit question needs the host's agreement before the dialog opens).</summary>
    void EnsureOptionHost()
    {
        if (optionDialog != null) return;
        optionHost = new GameOptionHost(this, assets.Packs, commonStrings) { DamageEffectAlpha = a => damageEffect?.SetAlpha(a) };
        optionHost.AgreementReminder += SetAgreementReminder;
        optionHost.HasTutorial = FieldHasTutorial;
        if (hudModel?.Keys is { } k && k != KeyTable.Empty) optionHost.Keys = k;
        optionHost.QuickSlots = new OptionQuickSlots(this);
        optionDialog = optionHost.Dialog = new Ms2.Formats.Options.GameOptionDialog(optionHost);
    }

    Ms2.Ui.Hud.HouseVisitQuestion houseVisit;

    /// <summary>The ResponseCube 0x14 tail (scenestategame.cpp:10231-10236): with a home, FUN_141262180 (the visit question) when the message box layer
    /// is free. STILL-OPEN: FUN_141262650, the notice 0x4f7c's close after an answer, has no dialog here.</summary>
    void OnHomeInfoApplied(bool _)
    {
        if (liveHome == null || liveHome.Home.HasNoHome || !Ms2.Ui.Hud.MessageBoxQueue.IsIdle) return;
        if (commonStrings == null) SystemMenuFacts();
        EnsureOptionHost();
        houseVisit ??= new Ms2.Ui.Hud.HouseVisitQuestion(new HouseVisitHost(liveHome, optionHost, commonStrings, FieldHasTutorial,
            show => gameMenu?.SetNotifyRedCircle16Flag(show)));
        houseVisit.Ask();
    }
    Ms2Godot.Net.LiveSession liveHome;

    void OpenGameOption(int page)
    {
        if (GuideRefusesOpen("GameOptionDialog")) return;
        EnsureOptionHost();
        if (page != 0) optionDialog.SetTab(page);
        if (!optionHost.IsClosed) return;
        if (!UiCode.Load(Settings.DataDir)) GD.PushWarning("UI code is not built for this data: the options dialog runs interpreted");
        menuModel ??= NewModel();
        if (systemMenuLayer == null) AddChild(systemMenuLayer = new CanvasLayer { Layer = 11 });
        var c = new GameOptionController { Dialog = optionDialog };
        optionHost.Movie = c;
        var m = new MovieDialog(assets, c, menuModel, ScreenMode.PixelSize(GetWindow()));
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
    /// <summary>Scripted runs: the inventory dialog is open.</summary>
    public bool InventoryOpen => inventory?.IsClosed == false;

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

    // ---- top bar: uiupperhuddialog run by its own ActionScript, UIUpperHudDialog's C++ side as UpperHudController ------

    MovieDialog upperHud;
    UiModel upperHudModel;
    Ms2.Ui.Hud.UpperHudController upperHudSide;

    /// <summary>
    /// The clock, mesos, merats and the buttons at the top (UIUpperHudDialog, uiupperhuddialog.cpp). The mesos and merats are
    /// shown only; the features the controller tests are the client's (feature.xml by name). Kept across relayouts.
    /// </summary>
    void UpperHud(Control ui)
    {
        var model = upperHudModel = new UiModel(GameTables.Shared(assets.Packs, assets.Language));
        if (GatePath != null) model.ServerTime = () => GateClock;   // the perf gate: no minute change mid-run
        model.Features.Clear();
        foreach (int id in new[] { Ms2.Ui.Feature.SearchTooltip, Ms2.Ui.Feature.InputMode, Ms2.Ui.Feature.GamePad, Ms2.Ui.Feature.VersionText, Ms2.Ui.Feature.Merat,
            Ms2.Ui.Feature.PvPRankingDuelMode, Ms2.Ui.Feature.ItemSocketSystem, Ms2.Ui.Feature.HabiToken, Ms2.Ui.Feature.ReverseRaid01,
            Ms2.Ui.Feature.Mentoring, Ms2.Ui.Feature.MesoMarketToken, Ms2.Ui.Feature.RedMerat, Ms2.Ui.Feature.MeratMessage })
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
        upperHudSide = new Ms2.Ui.Hud.UpperHudController { MouseMode = GameOptions.MouseControl.Value, GamePadMode = GameOptions.GamePad.Use };
        upperHudSide.GamePadClick += use => GameOptions.SetGamepadEnabled(GameOptions.GamePad, use, use);
        GameOptions.GamepadEnabledChanged -= OnGamepadEnabled;
        GameOptions.GamepadEnabledChanged += OnGamepadEnabled;
        GameOptions.GamepadSearchNotice -= OnGamepadSearchNotice;
        GameOptions.GamepadSearchNotice += OnGamepadSearchNotice;
        upperHudSide.ToggleDictionary += ToggleGameDictionary;
        upperHudSide.ToggleFishingBook += ToggleFishingBook;
        upperHudSide.ToggleTokenPocket += ToggleTokenPocket;
        upperHudSide.ToggleKeySettingSelect += ToggleKeySettingSelect;
        upperHud = new MovieDialog(assets, upperHudSide, model, ScreenMode.PixelSize(GetWindow()));
        ui.AddChild(upperHud);
        WatchLivePlayer(model);     // live, the mesos of ServerEnter and the Meso packets (status event 8)
    }

    /// <summary>The target HP bar (UIUpperHpBar, uiupperhpbar.cpp), run by its movie; kept across relayouts.</summary>
    void TargetHpBar(Control ui)
    {
        TargetBar = new Ms2.Ui.Hud.UpperHpBarController();
        targetBar = new MovieDialog(assets, TargetBar, NewModel(), ScreenMode.PixelSize(GetWindow()));
        ui.AddChild(targetBar);
    }

    /// <summary>
    /// The cast gauge (UICastingBar, uicameraeditdialog.cpp:903), run by its movie; loaded with the game scene
    /// (FUN_141ada6f0, uiservice.cpp:818) and kept across relayouts.
    /// </summary>
    void CastingGauge(Control ui)
    {
        CastingBar = new Ms2.Ui.Hud.CastingBarController();
        castingBar = new MovieDialog(assets, CastingBar, NewModel(), ScreenMode.PixelSize(GetWindow()));
        ui.AddChild(castingBar);
    }

    /// <summary>
    /// The guide event pane (UIGuideEvent, 0x4e53, uiguideeventarrow.cpp), run by its movie; loaded with the game scene
    /// (FUN_141ada6f0, uiservice.cpp:811) and kept across relayouts. The live session's guide reads it as its pane, so
    /// with it loaded an event can take a step (CGuideManager::UpdateEntities waits on the pane).
    /// </summary>
    void GuideEventPane(Control ui)
    {
        GuideEvent = new Ms2.Ui.Hud.GuideEventController
        {
            NowMs = () => (uint)Time.GetTicksMsec(),
            Strings = Ms2.Ui.Guide.GuideStrings.Shared(assets.Packs, assets.Language),
            // PlaySystemSound (table/systemsound.xml through the audio bank); the print is the no-bank fallback
            SystemSound = key => { if (FieldAudio.Instance?.PlayUi(key) != true) GD.Print($"guide: system sound {key} is not played"); },
            // the arrows' position sources (the client's singletons, uiguideeventarrow.cpp): the dialog registry, the
            // camera projection and the gates
            DialogOfName = FindGuideDialog,
            DialogOfId = FindGuideDialogOf,
            ArrowBlocked = GuidePopupOrBoxOpen,
            DialogArrowBlocked = GuideCinematicOrBox,
            ProjectWorld = ProjectGuideWorld,
            GameMenuChildPos = GameMenuChildPos,
            // CKeyTable::FindQuickSlotBySkillAndItemUid over the session's key table (the kind 1 arrows)
            SkillQuickSlot = skill => hudModel.ServerKeys?.FindQuickSlotBySkillAndItemUid(skill, 0) ?? -1,
            // the inventory's own state the kind 2-6 arrows ask
            InventoryOf = GuideInventoryOf,
            InventoryTabOf = GuideInventoryTabOf,
        };
        guideEvent = new MovieDialog(assets, GuideEvent, NewModel(), ScreenMode.PixelSize(GetWindow()));
        ui.AddChild(guideEvent);
        if (Ms2Godot.Net.LiveSession.Instance is { } live)
        {
            live.GuidePane = GuideEvent;
            live.FindDialogOf = FindGuideDialog;      // the guide's dialog conditions over the HUD's dialogs
            // the room table read here at the field's HUD build, on the main thread: no parse on the packet path
            var rooms = dungeonRooms ??= DungeonRooms.Load(assets.Packs, e => Ms2.Ui.FeatureElements.IsAvailable(assets.Packs, e));
            live.DungeonRoomOf = rooms.GetRoom;
        }
    }

    /// <summary>
    /// CUIService::FindObjectByName (the guide's dialog lookups): the open movies' controllers by their type name. The
    /// HUD holds one movie per dialog, as the client holds one dialog object per UI class, and its always-on movies
    /// (the game menu, the minimap, the main player info) answer like the client's.
    /// </summary>
    public Ms2.Ui.Guide.GuideDialog FindGuideDialog(string name)
    {
        Ms2.Ui.Guide.GuideDialog found = null;
        void Walk(Node n)
        {
            if (found != null) return;
            if (n is MovieDialog { Loaded: true } m && !m.Host.Controller.IsClosed
                && m.Host.Controller.TypeName.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                found = new Ms2.Ui.Guide.GuideControllerDialog(m.Host.Controller);
                return;
            }
            foreach (var c in n.GetChildren()) Walk(c);
        }
        Walk(this);
        return found;
    }

    /// <summary>
    /// FUN_141274f80 (unit_141274d10_uihelpers.cpp:181-191): CUIService::FUN_141adcbb0's any-shown-window gate first —
    /// an open dialog that is not an always-on one (the movie registry's list; the group messenger browser and the
    /// ad push the client adds are not in this HUD) — then a popup menu or a shown message box.
    /// </summary>
    bool GuidePopupOrBoxOpen() =>
        Ms2.Ui.Hud.DialogGates.AnyDialogShown(MovieDialog.Live)
        || (popupMenu != null && !popupMenu.IsClosed) || MessageBoxLayer.Instance?.Shown == true;

    /// <summary>
    /// CUIService::FindObject(id) as the guide's arrows use it (Unk_BreakDialog&lt;UIInventoryDialog&gt;(0x4e2d),
    /// uiguideeventarrow.cpp:719): the id's type name. The other ids the guide's tables name this way come with the
    /// arrow kinds that ask them (the minimap's marks, the party camera, the messenger, the cube palette).
    /// </summary>
    public Ms2.Ui.Guide.GuideDialog FindGuideDialogOf(int id) =>
        id == 0x4e2d ? FindGuideDialog("InventoryDialog") : null;

    /// <summary>
    /// The inventory the item arrows ask (UIInventoryDialog: GetItemsInCategory(GetSelectedTab) with each item's
    /// slot and what IsItemMatchingKind matches on, and the movie's scroll page, GetScrollPosition
    /// uiinventorydialog.cpp:1125). Null while the inventory is closed, as the client's dialog lookup fails then.
    /// The list and the record are reused across calls — the pane asks on the main thread once per frame an arrow
    /// is up and reads the answer at once, so no caller holds or re-enters between a fill and its read, and a
    /// steady frame allocates nothing.
    /// </summary>
    Ms2.Ui.Guide.GuideInventory GuideInventoryOf()
    {
        var inv = inventory;
        if (inv == null || inventoryMovie == null || inv.IsClosed) return null;
        long now = Ms2Godot.Net.LiveSession.Instance?.GameSession?.ServerTime ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        guideItems.Clear();
        foreach (var cell in inv.View.Cells(inv.View.Tab))          // the tab's cells by slot, the client's array order
        {
            if (cell.Empty) continue;
            var it = inv.View.FindByUid(cell.Uid);
            if (it == null) continue;
            var d = hudModel.Tables.Items.Get(it.Id);
            guideItems.Add(new Ms2.Ui.Guide.GuideInventoryItem(it.InventorySlot, it.Id, d?.Type ?? 0, d?.IconCode ?? 0,
                d?.HasCubeTextureMesh ?? false, it is { HasUgc: true, Ugc.Uid: not 0 },
                !(it.ExpiryTime > 0 && it.ExpiryTime <= now)));      // FUN_140528980: not expired
        }
        var host = inventoryMovie.Host;
        var list = host.Child("m_itemBagEx");
        guideInventory.Items = guideItems;
        guideInventory.ScrollPage = list == null ? 0 : (int)Rt.ToNumber(host.Get(list, "scrollPosition"));
        return guideInventory;
    }

    /// <summary>The reused list and record of <see cref="GuideInventoryOf"/> (cleared and refilled per call).</summary>
    readonly List<Ms2.Ui.Guide.GuideInventoryItem> guideItems = new();
    readonly Ms2.Ui.Guide.GuideInventory guideInventory = new() { Items = System.Array.Empty<Ms2.Ui.Guide.GuideInventoryItem>() };

    /// <summary>
    /// The inventory tab bar's and its button's places of a name (GetTabBar + FindTabIndexByName +
    /// GetButtonAt, uiguideeventarrow.cpp:755-778): the bar is the movie's m_tab, the name's index the tab list's
    /// (uiinventorydialog.cpp:1305 — the client's m_1f0 pairs). Null while the inventory is closed or the name is no
    /// tab of it.
    /// </summary>
    (double TabsX, double TabsY, double ButtonX, double ButtonY)? GuideInventoryTabOf(string name)
    {
        var inv = inventory;
        if (inv == null || inventoryMovie == null || inv.IsClosed) return null;
        int index = -1;
        for (int i = 0; i < inv.Tabs.Count; i++)
            if (string.Equals(inv.Tabs[i].Name, name, StringComparison.Ordinal)) { index = i; break; }
        if (index < 0) return null;
        var host = inventoryMovie.Host;
        var bar = host.Child("m_tab");
        if (bar == null) return null;
        var button = host.Invoke(bar, "getButtonAt", index);
        if (button == null || Rt.IsNullish(button)) return null;
        return (Rt.ToNumber(host.Get(bar, "x")), Rt.ToNumber(host.Get(bar, "y")),
            Rt.ToNumber(host.Get(button, "x")), Rt.ToNumber(host.Get(button, "y")));
    }

    /// <summary>UIGuideEventArrowDialog::vfunc_2's gates (uiguideeventarrow.cpp:569-575): the cinematic sub scene or a
    /// shown message box.</summary>
    bool GuideCinematicOrBox() =>
        Ms2Godot.Net.LiveSession.Instance?.Guide?.World.SubSceneId == 0x6200041 || MessageBoxLayer.Instance?.Shown == true;

    /// <summary>
    /// UIGuideEventArrow::vfunc_2's projection (@0x14195dee0): the camera's place of the point (FUN_140af0700) at the
    /// movie's stage units (the client's projection times the UI scale), its y shifted by FUN_1412762d0; null when the
    /// point is behind the camera. The guide's field points are the client's (x, y, z); Godot's are (x, -z, y).
    /// </summary>
    (double X, double Y)? ProjectGuideWorld(System.Numerics.Vector3 pos)
    {
        var c = GetViewport().GetCamera3D();
        var godot = new Vector3(pos.X, pos.Z, -pos.Y);
        if (c == null || c.IsPositionBehind(godot)) return null;
        var p = c.UnprojectPosition(godot);
        var f = GetTree().GetFirstNodeInGroup(Field.Group) as Field;
        return (p.X / u, p.Y / u + PlayerNameTag.PitchShift(f, godot));
    }

    /// <summary>
    /// GetGameMenuPathPosition's remap (uiguideeventarrow.cpp:911-929): the game menu's own moved-entry step — the
    /// menu's set is the game one (EGameMenuSet 0; the game pad menu set is not switched here).
    /// </summary>
    Ms2.Ui.Guide.GuideMenuMoved? GameMenuChildPos(string element, Ms2.Ui.Guide.GuideNode clip) =>
        Ms2.Ui.Hud.GameMenuController.MenuChildPos(gameMenu?.Table, element, clip);

    /// <summary>
    /// The system lines (UISystemText, uisystemtext.cpp), run by their movie; kept across relayouts, placed under the target
    /// HP bar (vfunc_43). A message shown before the movie is loaded is parked and shown at OnLoadComplete. The controller
    /// lives with the scene; a field move unloads its movie and loads it again (<see cref="WorldUnLoadComplete"/>,
    /// <see cref="WorldLoadComplete"/>), and the slot keeps its place among the HUD movies meanwhile.
    /// </summary>
    void SystemTextLines(Control ui)
    {
        ui.AddChild(systemTextSlot = new Control { Name = "uisystemtext", MouseFilter = Control.MouseFilterEnum.Ignore });
        if (SystemText != null) return;     // unloaded by a field move: loaded again at the new field's load complete
        SystemText = new Ms2.Ui.Hud.SystemTextController { UpperHpBar = TargetBar, TickMs = () => (uint)Time.GetTicksMsec() };
        // STILL-OPEN: PlaySystemSound (the emergency line's "emergency_notice"): this HUD has no system sound player
        SystemText.SystemSound += key => GD.Print($"system text: sound {key} is not played");
        LoadSystemText();
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
            WireUserChat(live);
            liveHome = live;
            live.HomeInfoApplied += OnHomeInfoApplied;
            BindCubeBill(live);
            OpenHomeMenuOnEntry();
            Action<Ms2.Net.Packets.ChatMessage, Func<Ms2.Net.Packets.WeddingChatHall>> onChat = ChatLog.OnUserChat;
            live.UserChat += onChat;
            Action<Ms2.Net.Packets.ChatItemLinks> onLinks = ChatLog.OnItemLinks;
            live.UserChatItemLink += onLinks;
            unwatchChat = () => { live.UserChat -= onChat; live.UserChatItemLink -= onLinks; ChatLog.SendPacket = null; ChatLog.SendSticker = null; ChatLog.ChatBalloon = null; };
        }
    }
    Action unwatchGemErrors, unwatchNotices, unwatchChat;
    Control systemTextSlot;                 // the system lines' place among the HUD movies

    /// <summary>UISystemText::vfunc_2 (via UIHeadUpDisplay): its movie loads; OnLoadComplete replays a parked line.</summary>
    void LoadSystemText()
    {
        systemText = new MovieDialog(assets, SystemText, NewModel(), ScreenMode.PixelSize(GetWindow()));
        systemTextSlot.AddChild(systemText);
    }

    /// <summary>
    /// The shared log's sinks for a user's chat line (UIMessengerBrowser::vfunc_84): ShowSystemNotice through this HUD's system
    /// text (@0x1412751f0: nothing without it; with bLog also the chat copy of kind 0x14), the local character id, the
    /// features and the server time. The sinks left null are the windows and managers not ported (see the browser's).
    /// </summary>
    void WireUserChat(Ms2Godot.Net.LiveSession live)
    {
        ChatLog.ShowSystemNotice = (text, type, log) =>
        {
            if (SystemText == null) return;
            SystemText.ShowMessage(text, type, 0);
            if (log) ChatLog.AddSystemLine(text, Ms2.Ui.Hud.TalkKind.SystemNotice);
        };
        ChatLog.SystemSound = key => GD.Print($"chat: system sound {key} is not played");
        ChatLog.LocalCharacterId = () => live.CharacterId;
        // ShowChatBalloon (:2940): the line over its speaker, through the live field's balloon layer
        ChatLog.ChatBalloon = (name, text, type) => Ms2Godot.World.NpcEmoticons.Live?.Speakers?.Show(name, text, type);
        ChatLog.ServerTime = () => live.GameSession?.ServerTime ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        ChatLog.SendPacket = chat =>
        {
            GD.Print($"chat: send kind {chat.Kind} ({chat.Message.Length} chars)");
            live.SendChat(chat);
        };
        ChatLog.SendSticker = sticker =>       // the chat window's emoticon sends (ChatSticker 0xB7)
        {
            GD.Print($"chat: sticker mode {sticker.Mode} id {sticker.EmoticonId}");
            live.SendChat(sticker);
        };
        ChatLog.IsInParty = () => live.Party.IsInParty;
        live.MasteryTable = hudModel.Tables.MasteryRecipes;   // OnMastery's grade of an exp value
        // FUN_140d2e2b0's held containers: the bag (g_143ab2a90.FindItemByUid)
        ChatLog.FindHeldItem = uid => live.Inventory?.FindByUid(uid) is { } e ? Ms2.Ui.Hud.InventoryController.ToItem(e, hudModel.Tables.Items) : null;
        // UIItemEnchantDialog's model: the UI item of a uid and the limit table's CanEnchant (needed by the 0x97 results).
        live.EnchantBook.Find = uid => live.Items.Get(uid) is { } e ? Ms2.Ui.Hud.InventoryController.ToItem(e, hudModel.Tables.Items) : null;
        live.EnchantBook.CanEnchantItem = hudModel.Tables.ItemOptions.CanEnchant;
        live.TalkDialogOpen -= OnTalkDialogOpen;
        live.TalkDialogOpen += OnTalkDialogOpen;
        ChatLog.CommandHooks.ChatLine = ChatLog.AddSystemLine;
        ChatLog.CommandHooks.GmLevel = ChatLog.GmLevel;
        ChatLog.CommandHooks.Unported = (name, args) =>
        {
            GD.Print($"chat: the user command {name} is not ported");
            return 1;
        };
    }

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
            Language = lang,                    // DAT_143ab0250: "KR", or SetLanguage (localepath.cpp:577) for "-language"
            ServerTime = () => live.GameSession?.ServerTime ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ShowMessage = (t, type, option) => SystemText?.ShowMessage(t, type, option),
            ChatLine = ChatLog.AddSystemLine,
            MessageBox = (t, tag) => MessageBoxQueue.ShowMessageBox(t, 0, null, tag),
            SystemSound = key => GD.Print($"notice: system sound {key} is not played"),
            ForcedQuit = () => { live.SendRequestQuitAndDisconnect(); Ms2Godot.Flow.GameFlow.ReturnToLogin(); },
            MaintenanceExit = () => GD.Print("notice: the maintenance exit call is not ported"),   // FUN_140d86260()->vfunc_58(0x289, 0)
        };
    }

    /// <summary>
    /// UIDialog::vfunc_73's guide test (uidialog.cpp:668), run by vfunc_65 before a closed dialog opens: while a running
    /// guide event holds the dialog of the type name shut (<see cref="Ms2.Ui.Guide.GuideManager.RefusesDialogOpen"/>) the
    /// open is refused with notice 2187 (FUN_141275260(0x88b, 1, 1)). STILL-OPEN: the system menu dialog has no type name
    /// in the port and is not tested; the HUD movies made at the field entry are not either.
    /// </summary>
    bool GuideRefusesOpen(string typeName)
    {
        if (!Ms2.Ui.Guide.GuideManager.RefusesDialogOpen(typeName)) return false;
        SystemNotice(Ms2.Ui.Guide.GuideManager.DialogLimitNotice);
        return true;
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
    /// <summary>WorldService::HasTutorial (worldservice_1.cpp:5448): the field's tutorialType is not 0.</summary>
    bool FieldHasTutorial()
    {
        try { return FieldData.FindByXBlock(assets.Packs, xblock)?.HasTutorialType ?? false; }
        catch (Exception e) { GD.PushWarning("field data: " + e.Message); return false; }
    }

    void OpenMinimap(World.FieldContents fc)
    {
        MinimapInfo info = null;
        try { info = MinimapInfo.Parse(Ms2.Formats.World.MapXBlock.Load(assets.Packs, xblock)); }
        catch (Exception e) { GD.PushWarning("minimap: " + e.Message); }
        var model = NewModel();
        model.Field.Name = MapName();
        bool pingAllowed = false;
        try
        {
            var fd = FieldData.FindByXBlock(assets.Packs, xblock);
            model.Field.MapLevel = fd?.MapLevel;
            pingAllowed = fd?.MinimapPing ?? false;
            if (fd != null) model.Field.MapId = fd.Id;
        }
        catch (Exception e) { GD.PushWarning("minimap field data: " + e.Message); }
        // PC::GetMastery(1): the user block's mastery list, index 1 = fishing
        if (data.Player.Mastery is { Length: > 1 } mastery) model.Player.FishingMastery = (uint)mastery[1];
        model.Field.Channel = LiveChannel() is int and > 0 and var chNow ? chNow : (int)data.Player.Channel;
        var chRows = LiveChannels();
        var chList = new int[chRows.Length];
        for (int i = 0; i < chList.Length; i++) chList[i] = chRows[i].Id;
        model.Field.Channels = chList.Length > 0 ? chList : new[] { model.Field.Channel };
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
            ChangeChannel = ChangeChannel,
            ChannelGroupName = menuGroup,
            PingAllowed = pingAllowed,
            Send = live != null ? live.SendMinimapPing : null,
            CharacterName = MinimapPingName,
            Relation = MinimapRelation,
            SystemSound = key => FieldAudio.Instance?.PlayUi(key),
        };
        c.WorldMap += ToggleWorldMap;       // OnWorldMap (uiminimapentity.cpp:1253): ToggleDialog(0x4e42)
        try { MinimapMarks(fc, c.Marks); }
        catch (Exception e) { GD.PushWarning("minimap marks: " + e.Message); }
        // the pcs already shown get their proxies with the rest (BuildEntities); later ones come through vfunc_3 / vfunc_4
        if (live != null) foreach (var pc in live.ShownPcs) c.AddOtherPlayer(MinimapPc(pc));
        else if (minimapPcsFixture) MinimapPcsFixture(c);
        minimap = new MovieDialog(assets, c, model, ScreenMode.PixelSize(GetWindow()));
        minimapSlot.AddChild(minimap);
    }

    Action unwatchMinimapPing;

    /// <summary>OnMinimapPing (scenestategame.cpp:6806-6815): FindMinimapDialog, then AddPing with the packet's uid, type and position.</summary>
    void WatchMinimapPings()
    {
        unwatchMinimapPing?.Invoke();
        if (Net.LiveSession.Instance is not { } live) return;
        Action<Ms2.Net.Packets.MinimapPingMessage> on = m =>
            minimapSide?.AddPing(m.Uid, m.Type, new Vec3(m.Position.X, m.Position.Y, m.Position.Z));
        live.MinimapPinged += on;
        unwatchMinimapPing = () => live.MinimapPinged -= on;
        unwatchMinimapPcs?.Invoke();
        Action<Net.RemoteLayer.ShownPc> shown = pc => minimapSide?.AddOtherPlayer(MinimapPc(pc));
        Action<int> left = id => minimapSide?.RemoveOtherPlayer(id);
        live.FieldPcShown += shown;
        live.FieldPcLeft += left;
        unwatchMinimapPcs = () => { live.FieldPcShown -= shown; live.FieldPcLeft -= left; };
    }

    Action unwatchMinimapPcs;
    bool minimapPcsFixture;             // --minimap-pcs-fixture: offline pcs around the own one, one per tier

    /// <summary>The offline shot's pcs: one per UpdateOtherPlayer tier (and a dead one) a few metres around the own pc.</summary>
    void MinimapPcsFixture(MinimapController c)
    {
        var tiers = new[] { MinimapController.PlayerRelation.Normal, MinimapController.PlayerRelation.PartyChief, MinimapController.PlayerRelation.Party,
            MinimapController.PlayerRelation.Guild, MinimapController.PlayerRelation.Buddy, MinimapController.PlayerRelation.Normal };
        c.Relation = (id, _) => tiers[(int)(id - 900) % tiers.Length];
        for (int i = 0; i < tiers.Length; i++)
        {
            double a = i * Math.PI * 2 / tiers.Length;
            float dx = (float)(Math.Cos(a) * 900), dy = (float)(Math.Sin(a) * 900);
            bool dead = i == tiers.Length - 1;
            c.AddOtherPlayer(new MinimapController.OtherPlayer
            {
                ObjectId = 900 + i, CharacterId = 900 + i, Name = "pc" + i, Image = Net.ProfileImageService.Images.Path(900 + i, "", true),
                Position = () => { var p = LocalMinimapPlayer().Item1; return new Vec3(p.X + dx, p.Y + dy, p.Z); },
                Dead = () => dead,
            });
        }
    }

    /// <summary>UIMinimapDialog::AddProxy's view of another pc: BuildPlayerData's picture (FUN_141028be0(id, portrait, 1)),
    /// name and today memo, its position and its dead flag.</summary>
    static MinimapController.OtherPlayer MinimapPc(Net.RemoteLayer.ShownPc pc)
    {
        var at = pc.Position;
        return new MinimapController.OtherPlayer
        {
            ObjectId = pc.ObjectId, CharacterId = pc.CharacterId, Name = pc.Name, TodayMemo = pc.Motto,
            Image = Net.ProfileImageService.Images.Path(pc.CharacterId, pc.Picture, true),
            Position = () => { var v = at(); return new Vec3(v.X, v.Y, v.Z); },
            Dead = pc.Dead,
        };
    }

    /// <summary>
    /// UpdateOtherPlayer's tests (uiminimapentity.cpp:3207-3223): CPartyManager::FindMemberByCharId (the leader is the chief),
    /// CGuildManager::FindMemberByName, CBuddyManager::FUN_140ae4d70. Loops, not the books' LINQ finds: the minimap asks
    /// each refresh round. Null with no own pc (GetLocalPlayer()==0, :3199; SelfObjectId is -1 before the own FieldAddUser,
    /// after a field change and after quit): the proxy keeps its state.
    /// </summary>
    static MinimapController.PlayerRelation? MinimapRelation(long characterId, string name)
    {
        if (Net.LiveSession.Instance is not { } live || live.SelfObjectId < 0) return null;
        if (live.Party.Find(characterId) != null)
            return live.Party.LeaderId == characterId ? MinimapController.PlayerRelation.PartyChief : MinimapController.PlayerRelation.Party;
        if (live.Guilds.Info?.Members is { } members)
            for (int i = 0; i < members.Count; i++)
                if (string.Equals(members[i].Name, name, StringComparison.OrdinalIgnoreCase)) return MinimapController.PlayerRelation.Guild;
        return live.Buddies.IsFriend(characterId) ? MinimapController.PlayerRelation.Buddy : MinimapController.PlayerRelation.Normal;
    }

    /// <summary>
    /// GetCharacterName @0x141a45d50 (uiminimapentity.cpp:2620): the field pc's name by character id, else "" (the
    /// controller then asks its pc proxies, :2625-2629).
    /// </summary>
    static string MinimapPingName(long characterId)
    {
        if (Net.LiveSession.Instance is not { } live) return "";
        foreach (var u in live.FieldUsers())
            if (u.CharacterId == characterId) return u.Name ?? "";
        return "";
    }

    /// <summary>
    /// The field's proxies and places for BuildEntities: AddProxy's npcs (UpdateNpc: SetFromNpcData), the portals
    /// (UpdatePortal / SetFromPortal, with their guide arrows), BuildTaxi (the first MS2TaxiStation world object),
    /// BuildNamed (MS2InteractObject with the server's state) and BuildMapObjects (MS2Minimap). The icons and layers are
    /// <see cref="MinimapIcons"/>'s ports of UIMinimapEntity's.
    /// </summary>
    void MinimapMarks(World.FieldContents fc, List<Ms2.Ui.Hud.MinimapController.Mark> marks)
    {
        // the field meta table the quest table already read (QuestTable.Load), not a second parse of fieldmetadata.xml
        // (none without the table: no portal npcs and no taxi, the other marks still placed)
        var meta = FieldMeta.EntryOf(assets.Packs) != null ? QuestMapGraph.SharedMeta(assets.Packs) : null;
        var icons = new MinimapIcons(assets.Packs, World.FieldContents.Npcs(assets.Packs), new GameStrings(assets.Packs, assets.Language),
            meta?.FieldNpcs ?? new Dictionary<int, List<int>>(), meta == null ? Array.Empty<int>() : meta.TaxiStations.Keys);
        foreach (var n in fc.SpawnedNpcs)
        {
            var e = MinimapIcons.Npc(n.Row);
            if (!e.Visible || !n.Root.Visible) continue;
            var npc = n;
            marks.Add(new() { Entity = e, Position = () => World.FieldPortals.ToMs2(npc.Position), Moves = true });
        }
        // STILL-OPEN: in a live field the minimap still marks the xblock's portals (their own flags), not the server-listed
        // ones (LivePortals: shown, minimap flag from FieldPortal 0x48)
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
            Place(MinimapIcons.Taxi(data.FoundTaxis.Contains(fc.FieldId), PlayerLevel >= bestLevel), taxi.Position);
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
        noticeOpenTicks = (uint)constants.Int("NoticeDialogOpenSeconds", 0);     // added raw to the tick (SetHideAt @0x141009950)
        if (notice == null)
        {
            var nc = new NoticeDialogController { NoticeUrl = constants.String("NoticeDialogUrl", "") };
            nc.NewsRequested += (flag, id) => Ms2Godot.Net.LiveSession.Instance?.SendTalk(new Ms2.Net.Packets.NewsNotificationRequest(flag, id));
            notice = new MovieDialog(assets, nc, hudModel, ScreenMode.PixelSize(GetWindow()));
        }
        ui.AddChild(notice);
    }

    MovieDialog smartPushMovie;         // uismartpush (UISmartPush, 0x4ef7)
    SmartPushManager smartPushManager;

    /// <summary>
    /// UISmartPush (0x4ef7), the strip of smart push buttons, and the CSmartPushManager that adds and removes them
    /// (uismartpush.cpp, smartpushmanager.cpp). The field answers none of the pushes' questions yet (riding, event buffs,
    /// fishing, instrument, nearby gathering objects: STILL-OPEN), so no button shows; the hide option and the click
    /// are wired. Loaded once and kept across relayouts.
    /// </summary>
    void SmartPushStrip(Control ui)
    {
        if (smartPushMovie == null)
        {
            var strip = new SmartPushStripController();
            smartPushManager = new SmartPushManager(hudModel.Tables.SmartPush, SmartPushNotices, key => hudModel.Tables.Common(key) ?? "", new SmartPushField
            {
                HasPlayer = () => true,
                IsRiding = () => player?.Mount != null,                                  // MyPC::IsRiding
                RideeType = () => player?.Mount?.Data.Type ?? 0,
                EventBuff = t => ownBuffs?.HasEnabledWithEventBuffType(t) == true,       // additionalcli_2.cpp:407
                AdventureReached = () => AdventureLevelTable().IsReached((uint)hudModel.Player.AdventureLevel),   // vfunc_141 = m_n1208, the prestige level
            })
            {
                Strip = strip,
                Suppressed = () => GameOptions.Current.StatusOption.DisplayHideSmartPush,
            };
            strip.PushClicked += smartPushManager.OnPushClicked;
            strip.Hidden += smartPushManager.ResetPushStates;
            smartPushMovie = new MovieDialog(assets, strip, hudModel, ScreenMode.PixelSize(GetWindow()));
        }
        ui.AddChild(smartPushMovie);
    }

    /// <summary>The xblock of the field this scene runs: the first field's, then each move's target (<see cref="SwapField"/>), else --field.</summary>
    string xblock;
    /// <summary>That field's id (0 when the xblock has no field row): the offline field id of the quest and map windows.</summary>
    int xblockFieldId;
    int XblockFieldId()
    {
        try { return FieldData.FindByXBlock(assets.Packs, xblock)?.Id ?? 0; }
        catch (Exception e) { GD.PushWarning("field id: " + e.Message); return 0; }
    }
    Node fieldScene;                        // field.tscn's instance, this scene's child
    bool worldLoadPending;                  // a move's field is loading: WorldLoadComplete runs at its load complete
    QuestButtonInputs questGuideInputs;

    /// <summary>The number of field moves this scene has run, for checks.</summary>
    public int FieldMoves { get; private set; }

    /// <summary>
    /// A field move under this HUD (FieldContents, where the client's LoadMap runs WorldService FUN_141bbc580,
    /// worldservice_1.cpp:9962). The client keeps CUIService, the scene and every dialog for the process; only the world
    /// goes. So the HUD and its dialogs stay, the field scene is freed and a new one is made at the same place, and the
    /// per-field parts are reset in the client's order:
    /// <list type="number">
    /// <item>FUN_141bbc580 :9973 CUIService FUN_141adc3d0 = CloseDialog(eUI_4f38) (uiservice.cpp:1567), the pet system
    /// dialog: not ported here, nothing to close.</item>
    /// <item>:10019 FUN_141adb900 (uiservice.cpp:1316): UIUpperHpBar FUN_141b4d0a0 (uiupperhpbar.cpp:367) removes the buff
    /// displays of its entities (the target bar has none here: no buff display is ported; the entities and shown state
    /// stay, as in the client); the loading screen's StartProgress (the move's cover); UnloadBackgroundMovie (the
    /// background movie is the login's here, none in a field); FUN_141ad9dc0 (uiservice.cpp:626) =
    /// GFxMovieDamage::UnloadExpiredFonts (@0x140b61ce0), whose UIDamageText::ClearAll is <see cref="DamageText.ClearAll"/>
    /// (its font expiry has no counterpart); the cutscene's ClearTalks (no cutscene dialog here); FUN_141adbcd0(0) =
    /// UISwfEntityManager::SetEnabled(false) (uiswfentitymanager.cpp:794, m_0a, the hide-UI toggle's state; STILL-OPEN:
    /// the toggle is not ported, so there is nothing to reset); m_b120 = 1 (a SetMode is deferred while it is set,
    /// uiservice.cpp:1375; no mode change is ported).</item>
    /// <item>UISceneGame::vfunc_56 (WorldUnLoadComplete, @0x141ac78f0, uiscenegame.cpp:300):
    /// <see cref="WorldUnLoadComplete"/>. STILL-OPEN: its first steps, the UISceneEntity::vfunc_56 base call and, when the
    /// sub scene is not 0x620001d, SetSubSceneId(0x620001d) with UITalkManager::SendNpcTalkMode0 (:303-306), have no
    /// counterpart; the NPC talk is likely covered by FieldContents.BeginMove's talk?.End().</item>
    /// </list>
    /// Then the new field's load complete runs vfunc_55 (<see cref="WorldLoadComplete"/>). The HUD's own hold on the old
    /// field (the own PC's name tag, HP bar and damage numbers, the minimap, the field facts) is dropped and found again in
    /// the new field. STILL-OPEN: whether the client's minimap is rebuilt at a move or refreshed in place
    /// (UIMinimapDialog::OnLoadComplete -> BuildUIData / BuildField, uiminimapentity.cpp:1104) is not traced; it is opened
    /// again for the new field, as at field entry.
    /// </summary>
    public void SwapField()
    {
        damageText?.ClearAll();                      // FUN_141ad9dc0 -> UIDamageText::ClearAll
        WorldUnLoadComplete();
        CloseHomeMenuOnLeave();
        if (fieldScene != null && IsInstanceValid(fieldScene))
        {
            int at = fieldScene.GetIndex();
            RemoveChild(fieldScene);                 // out of the groups at once: nothing finds the old field's nodes
            fieldScene.QueueFree();
            xblock = World.FieldContents.PendingXBlock ?? xblock;
            xblockFieldId = XblockFieldId();
            AddChild(fieldScene = GD.Load<PackedScene>("res://field.tscn").Instantiate());
            MoveChild(fieldScene, at);
        }
        player = null; field = null;
        playerNameTag?.FieldChanged();
        playerBar?.FieldChanged();
        tombBar?.FieldChanged();
        ResetQuestNavigation();
        if (minimap != null) { minimap.Host.Unload(); minimap.GetParent()?.RemoveChild(minimap); minimap.QueueFree(); }
        minimap = null; minimapSide = null;
        slotField = null; slotFieldId = null;       // the quick slots' field facts (FieldData of the field)
        communityFieldId = null;
        if (questGuideSide != null) questGuideSide.FieldId = xblockFieldId;
        if (questGuideInputs != null) questGuideInputs.FieldId = xblockFieldId;
        if (questLogSide?.Detail != null) questLogSide.Detail.FieldId = xblockFieldId;
        if (worldMapSide != null) { worldMapSide.FieldId = xblockFieldId; worldMapSide.VisitedFields = new[] { (uint)xblockFieldId }; }
        SystemMenuFacts();
        FieldMoves++;
        worldLoadPending = true;
    }

    /// <summary>
    /// UISceneGame::vfunc_56 (WorldUnLoadComplete, @0x141ac78f0, uiscenegame.cpp:300): FUN_141add0b0 runs vfunc_3 on the
    /// screen mask (0x4e55, FUN_1422dc080: its movie unloads) and on the system text (0x4e41, @0x141b15e80: its movie
    /// unloads and the parked line is forgotten). The shown lines go with the movie.
    /// </summary>
    void WorldUnLoadComplete()
    {
        if (screenMask != null) { screenMask.Host.Unload(); screenMaskLayer.QueueFree(); }
        screenMask = null; screenMaskLayer = null;
        if (systemText != null) { systemText.Host.Unload(); systemText.GetParent()?.RemoveChild(systemText); systemText.QueueFree(); }
        systemText = null;
        SystemText?.ForgetParked();
    }

    /// <summary>
    /// UISceneGame::vfunc_55 (WorldLoadComplete, @0x141ac78a0): FUN_141add040 runs vfunc_2 on the screen mask (LoadSwf(10),
    /// uiscreenmask.cpp:47, with the new field's bgMask) and on the system text (its movie loads; a line parked during the
    /// load is shown).
    /// </summary>
    void WorldLoadComplete()
    {
        worldLoadPending = false;
        ScreenMask();
        if (systemText == null && SystemText != null && systemTextSlot != null && IsInstanceValid(systemTextSlot)) LoadSystemText();
        OpenHomeMenuOnEntry();
    }

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
    /// The send of a line typed without a session. The client has no offline mode: its own line reaches the chat log only
    /// through the server's chat echo (0x1D, OnUserChat). Offline the HUD gives the line that echo straight away: the
    /// request's kind as the type and the local player as the speaker; a whisper sent (kind 4) names its target instead,
    /// as its line (ChatHtml string 0x674) does.
    /// </summary>
    void OfflineChat(Ms2.Net.Packets.UserChatRequest chat)
    {
        var pc = hudModel.Player;
        string name = chat.Kind == 4 ? chat.Target ?? "" : pc.Name ?? "";
        ChatLog.OnUserChat(new Ms2.Net.Packets.ChatMessage(0, 0, name, false, 0, chat.Message ?? "", chat.Kind, false, pc.Gender,
            chat.Kind == 4 ? chat.Target ?? "" : "", chat.Value, 0, 0));
    }
    Action<Ms2.Net.Packets.UserChatRequest> offlineChat;

    /// <summary>
    /// UIMessengerBrowser::LoadDefaultWindows (uimessenger.cpp:3149): a chat window (UIMessenger) per
    /// table/default/DefaultMessenger&lt;i&gt;.xml, named messengerdialog&lt;i&gt;.xml, first load; then UIGroupMessengerBrowser,
    /// the group chat bar, all over the shared log (<see cref="ChatLog"/>). The saved windows of the character folder
    /// (LoadWindows :3689) are not read yet. A line typed is shown when it comes back as the chat packet (0x1d,
    /// <see cref="Ms2.Ui.Hud.MessengerBrowser.OnUserChat"/>, wired with the system text); SendChat (:4208) only sends it.
    /// The log's other lines: the welcome line (UISceneGame::vfunc_48, uiscenegame.cpp:187: s_msg_chatting_welcome, or
    /// s_msg_chatting_welcome_pubtest2 while the public test game event runs; game events are not modelled, so the plain
    /// one), the system notices' copies (<see cref="SystemNotice"/>) and the new items of a live session (ItemInventory mode
    /// 7 -> the item event dispatcher's vfunc_4, uiitemenchantdialog.cpp:2146 -> UIMessengerBrowser::vfunc_5). Of mode 7
    /// (scenestategame.cpp:2836) the item's name (FUN_140529500, only read for a score's title) and MyPC's cube gate
    /// (FUN_141527eb0: m_15e4, set by the cube responses 0x20 / 0x21, not ported) are left out.
    /// </summary>
    void Chat(Control ui)
    {
        var window = ScreenMode.PixelSize(GetWindow());
        if (messengers.Count == 0)
        {
            ChatLog.Model = hudModel;
            if (Ms2Godot.Net.LiveSession.Instance == null) ChatLog.SendPacket = offlineChat = OfflineChat;
            HookChatEmoticon();
            var docs = hudModel.Tables.DefaultMessengers;
            for (int i = 0; i < docs.Count; i++)
            {
                var c = new MessengerController(docs[i], $"messengerdialog{i}.xml", true, ChatLog);
                messengers.Add((new MovieDialog(assets, c, hudModel, window), c));
            }
            ChatLog.WindowCreated += OnMessengerCreated;
            ChatLog.WindowClosed += OnMessengerClosed;
            ChatLog.OpenNewTabDialog = () => GD.Print("chat: the tab name dialog (0x4e62) is not in this HUD");
            groupBrowser = NewGroupBrowser();
            groupMessenger = new MovieDialog(assets, groupBrowser, hudModel, window);   // its load opens the joined chats' windows
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
        foreach (var (m, _) in groupWindows) if (m.GetParent() == null) ui.AddChild(m);
    }

    /// <summary>A window the browser made for a detached tab (CreateWindowWithTabs): its movie joins the chat layer.</summary>
    void OnMessengerCreated(Ms2.Ui.Hud.MessengerController c)
    {
        var m = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        messengers.Add((m, c));
        GD.Print($"chat: window {c.Name} load={m.Loaded} with {c.Tabs.Count} tab(s)");
        if (messengers.Count > 0 && messengers[0].Movie.GetParent() is { } layer) layer.AddChild(m);
    }

    /// <summary>A window CloseEmptyWindow closed: its movie goes.</summary>
    void OnMessengerClosed(Ms2.Ui.Hud.MessengerController c)
    {
        int i = messengers.FindIndex(x => x.Side == c);
        if (i < 0) return;
        var m = messengers[i].Movie;
        messengers.RemoveAt(i);
        m.Host.Unload();
        m.QueueFree();
    }

    GroupMessengerBrowserController groupBrowser;
    InviteGroupDialogController groupInvite;
    MovieDialog groupInviteMovie;
    bool GroupInviteOpen => groupInviteMovie != null && !groupInvite.IsClosed;

    /// <summary>CUIService::OpenDialog(0x4e90) from a group window's OnShowInviteDialog: the invite dialog's movie.</summary>
    void OpenGroupInvite()
    {
        if (GroupInviteOpen) { groupInvite.RefreshUserList(); return; }
        if (groupInviteMovie != null) { groupInviteMovie.Host.Unload(); groupInviteMovie.QueueFree(); groupInviteMovie = null; }
        groupInviteMovie = new MovieDialog(assets, groupInvite, hudModel, ScreenMode.PixelSize(GetWindow()));
        GD.Print($"groupchat: invite dialog load={groupInviteMovie.Loaded} (chat {groupInvite.GroupId})");
        uiRoot.AddChild(groupInviteMovie);
    }

    /// <summary>The invite dialog's vfunc_13: UIDialog's first (Escape closes), then its Enter. It is modal, so it is asked first.
    /// Only the main Return key (WasKeyPressed(0x1c), DIK_RETURN), not the keypad's (DIK 0x9c), as the chat windows.</summary>
    bool GroupInviteKeys(InputEventKey k)
    {
        if (!GroupInviteOpen) return false;
        if (k.Keycode == Key.Escape) { groupInvite.Close(); return true; }
        return groupInvite.OnKey(k.Keycode == Key.Enter);
    }
    Action<string> groupLink;
    readonly List<(MovieDialog Movie, GroupMessengerController Side)> groupWindows = new();
    bool groupChatFixture;

    /// <summary>
    /// UIGroupMessengerBrowser and CGroupChatManager: the session's group chats (GroupChat 0x8B, sent with 0x4C), or offline
    /// with --groupchat-fixture <see cref="GroupChatFixture"/>'s, else an empty book whose sends are logged. Each window the
    /// browser opens (OpenChatWindow) is its own movie in this HUD's chat layer; an item linked in the chat also goes to the
    /// current group window's input (FUN_141953660).
    /// </summary>
    GroupMessengerBrowserController NewGroupBrowser()
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        var book = live?.GroupChats ?? new GroupChatBook();
        Func<string> localName = () => hudModel.HasLocalPlayer ? hudModel.Player.Name : "";
        book.Common = hudModel.Tables.Common;
        book.LocalName = localName;
        book.Send = live != null ? live.SendGroupChat : packet => GD.Print($"groupchat: offline, not sent: {packet}");
        var b = new GroupMessengerBrowserController
        {
            Book = book,
            Names = ChatLog,
            Common = hudModel.Tables.Common,
            LocalName = localName,
            LocalCharacterId = () => live?.CharacterId ?? (groupChatFixture ? PartyFixture.SelfCharacterId : 0),
            PlaySound = key => FieldAudio.Instance?.PlayUi(key),
            ShowSystemNotice = (text, type, log) =>     // ShowSystemNotice, as the user chat's (WireUserChat)
            {
                SystemText?.ShowMessage(text, type, 0);
                if (log) ChatLog.AddSystemLine(text, TalkKind.SystemNotice);
            },
        };
        b.OpenWindow = w =>
        {
            var m = new MovieDialog(assets, w, hudModel, ScreenMode.PixelSize(GetWindow()));
            if (!m.Loaded) { m.QueueFree(); return false; }
            groupWindows.Add((m, w));
            w.Unloaded += () =>
            {
                groupWindows.RemoveAll(x => x.Side == w);
                m.Host.Unload();
                m.QueueFree();
            };
            groupMessenger?.GetParent()?.AddChild(m);   // during the browser's own load it is added after it (Chat)
            return true;
        };
        b.FindInviteDialog = () => groupInvite;
        b.OpenDialog = id => { if (id == InviteGroupDialogController.UiId) OpenGroupInvite(); };
        groupInvite = new InviteGroupDialogController(b)
        {
            PartyMembers = () => live?.Party.Members ?? (IEnumerable<Ms2.Net.Packets.PartyMember>)System.Array.Empty<Ms2.Net.Packets.PartyMember>(),
            Buddies = () => live?.Buddies.Buddies ?? (IEnumerable<Ms2.Net.Packets.BuddyInfo>)System.Array.Empty<Ms2.Net.Packets.BuddyInfo>(),
        };
        groupInvite.Unloaded += () => { groupInviteMovie?.Host.Unload(); groupInviteMovie?.QueueFree(); groupInviteMovie = null; };
        book.Messenger = () => b;
        groupLink = b.LinkToCurrent;
        ChatLog.GroupLink = groupLink;
        if (live == null && groupChatFixture && hudModel.HasLocalPlayer) GroupChatFixture.Apply(book, hudModel.Player);
        return b;
    }

    Action unwatchNewItems;

    const int PickupAction = 0x493e3;   // the "get" action (MyPC::FUN_141530db0's IsActionKeyDown)

    /// <summary>
    /// The drops' pickup key as held state: a press or release of a key bound to the "get" action sets it, so the drop layer asks a
    /// bool each frame instead of walking the key table. The client reads it through Unk_PCMove::IsActionKeyDown (@0x141682fe0),
    /// which gives nothing while Unk_PCMove::FUN_141683ef0 (@0x141683ef0) asks CInputKeyHandler::FUN_14116d7a0(0xf) and it holds:
    /// a key typed into a text field, an engine one or a movie's, does not count.
    /// </summary>
    void TrackPickupKey(InputEvent e)
    {
        if (e is not InputEventKey { Echo: false } k) return;
        if (k.Pressed && Ms2.Ui.InputBlock.Active) return;
        if (Ms2Godot.Net.LiveSession.Instance is not { } live) return;
        foreach (int dik in hudModel.Keys.KeysOfAction(PickupAction))
            if (GodotKeyOfDik(dik) is { } g && g == k.PhysicalKeycode) { live.PickupKeyHeld = k.Pressed; break; }
        // the attack key the same way, for the own mount's RunX (Ridee::UpdateRunEffects' IsActionKeyDown(0x49401))
        foreach (int dik in hudModel.Keys.KeysOfAction(AttackAction))
            if (GodotKeyOfDik(dik) is { } g && g == k.PhysicalKeycode) { live.AttackKeyHeld = k.Pressed; return; }
    }

    const int AttackAction = 0x49401;

    /// <summary>The scene's keys: the chat windows first, then the dialogs, then the own casts.</summary>
    public override void _Input(InputEvent e)
    {
        var vp = GetViewport();   // taken before the dispatches, which can change the scene
        TrackPickupKey(e);
        TrackLeftCtrl(e);
        // under a field move's loading screen no hotkey opens a dialog (Ms2.Ui.FieldMoveCover); the message box's keys are
        // its own layer's (MessageBoxLayer), which saw them first
        if (Ms2.Ui.FieldMoveCover.Up)
        {
            if (e is InputEventKey) vp.SetInputAsHandled();
            return;
        }
        if (ChatKeys(e)) { if (IsInstanceValid(vp)) vp.SetInputAsHandled(); return; }
        if (DialogKeys(e))
        {
            if (partyDpsCastThrough) { partyDpsCastThrough = false; CastKeys(e); }   // Ctrl+Q also casts Q (HudScene.PartyDps)
            if (IsInstanceValid(vp)) vp.SetInputAsHandled();
            return;
        }
        if (CastKeys(e) && IsInstanceValid(vp)) vp.SetInputAsHandled();   // the attack and quick slot keys (HudScene.Cast)
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
            if (ChatKey(k, m, c.OnKey, () => c.InputActive, c.Name) is { } used) return used;
        foreach (var (m, c) in groupWindows.ToArray())     // UIGroupMessenger::vfunc_13 (:523), the same keys
            if (ChatKey(k, m, c.OnKey, () => c.InputActive, c.Name) is { } used) return used;
        return false;
    }

    /// <summary>
    /// One chat window's turn at a key: null passes it to the next window. A Return the window does not take while its
    /// input has the focus is kept from the movie, and logged with the window's name, the current window and whether a
    /// Godot text control has the focus, so a run shows why a typed line stayed.
    /// </summary>
    static bool? ChatKey(InputEventKey k, MovieDialog m, Func<int, bool, bool> onKey, Func<bool> inputActive, string name)
    {
        if (!m.Loaded || !m.IsVisibleInTree()) return null;
        if (k.Pressed && !k.Echo && !UiFocus.IsTyping)
        {
            if ((k.Keycode == Key.Enter && onKey(MessengerController.DikReturn, false))
                || (k.Keycode == Key.Escape && onKey(DikEscape, true)))
                return true;
        }
        if (!inputActive()) return null;
        if (k.Keycode is Key.Enter or Key.Escape)
        {
            if (k.Keycode == Key.Enter && k.Pressed && !k.Echo)
                GD.Print($"chat: Return not taken by {name} with its input focused (current window {ChatLog.GetCurrentName()}, Godot text focus {UiFocus.IsTyping})");
            return true;
        }
        uint code = As3View.FlashKeyCode(k.Keycode);
        if (code == 0 && k.Unicode == 0) return true;
        m.Player.Key(code, k.Pressed ? (uint)k.Unicode : 0, k.Pressed, k.ShiftPressed, k.CtrlPressed);
        return true;
    }

    const int DikEscape = 1;

    /// <summary>
    /// The scene's action keys and an open dialog's Escape, while nothing takes typing. UISceneGame::vfunc_13 runs the
    /// action keys (IsActionKeyPressed 0x61a87, the quest log, action 400007: DIK 38, L; the inventory, action 400001: DIK 23, I;
    /// in table/default/defaultkey.xml) through CKeyTable::FUN_14142b300, which toggles the dialog (keytable.cpp: case 400007,
    /// ToggleDialog(0x4e3e); case 400001, ToggleDialog(0x4e2d)).
    /// UIDialog::vfunc_13: Escape closes an open dialog (vfunc_69 -> vfunc_67).
    /// ExecuteActionKey (keytable.cpp:729) runs an action only when CKeyTable::FUN_141431fc0 (@0x141431fc0, :2620) lets it:
    /// not while FUN_14116d7a0(0xf) holds input back, so a focused text field of a movie (CUIService::IsTextFieldFocused)
    /// keeps the toggles off and its letters go to the field. A dialog's own Enter and Escape (its vfunc_13) do not ask it.
    /// </summary>
    bool DialogKeys(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k || UiFocus.IsTyping) return false;
        if (SkillCompactKeys(k)) return true;
        if (PartyDpsKeys(k)) return true;
        bool actions = !Ms2.Ui.InputBlock.Active;   // FUN_141431fc0 (m_nMode taken as on)
        if (actions && k.PhysicalKeycode == Key.L) { ToggleQuestLog(); return true; }   // a DirectInput key code is a physical key
        if (actions && k.PhysicalKeycode == Key.M) { ToggleWorldMap(); return true; }  // action 400006, DIK 50 (keytable.cpp:744)
        if (WorldMapOpen && k.Keycode == Key.Enter && worldMapSide.EnterKey()) return true;
        if (k.Keycode == Key.Escape && WorldMapOpen) { worldMapSide.Close(); return true; }
        if (gameDictionary?.IsClosed == false && k.Keycode == Key.Enter && gameDictionary.EnterKey()) return true;
        if (actions && k.PhysicalKeycode == Key.I) { ToggleInventory(); return true; }     // action 400001 (DIK 23, I): ToggleDialog(0x4e2d)
        if (actions && k.PhysicalKeycode == Key.P) { ToggleMyInfo(); return true; }        // action 400002 (DIK 25, P): ToggleDialog(0x2711), keytable.cpp:738
        if (actions && k.PhysicalKeycode == Key.K) { ToggleSkillBook(); return true; }     // action 400005 (DIK 37, K): ToggleDialog(0x4f2d)
        if (actions && Ms2.Ui.Hud.SkillCompactControlController.OpensWithKey(hudModel.Keys, DikOfGodotKey(k.PhysicalKeycode)))
        { ToggleSkillCompact(); return true; }   // action 400031: ToggleDialog(0x4ef6), no default key
        bool open = questLog != null && questLogSide?.IsClosed == false;
        if (k.Keycode == Key.Escape && open) { questLogSide.Close(); return true; }
        if (k.Keycode == Key.Escape && inventory?.IsClosed == false) { inventory.Close(); return true; }
        if (k.Keycode == Key.Escape && myInfo?.IsClosed == false) { myInfo.Close(); return true; }
        if (k.Keycode == Key.Escape && PcInfoOpen) { pcInfo.Close(); return true; }
        if (k.Keycode == Key.Escape && skillBook?.IsClosed == false) { skillBook.RequestClose(); return true; }
        if (k.Keycode == Key.Escape && gameDictionary?.IsClosed == false) { gameDictionary.Close(); return true; }
        if (AchieveKeys(k)) return true;
        if (MailSendKeys(k)) return true;
        if (MailKeys(k)) return true;
        if (TradeKeys(k)) return true;
        if (StoreKeys(k)) return true;
        if (ItemLockKeys(k)) return true;
        if (ItemBreakKeys(k)) return true;
        if (ItemMergeKeys(k)) return true;
        if (ItemEnchantKeys(k)) return true;
        if (ItemMergeRevertKeys(k)) return true;
        if (SelectItemBoxKeys(k)) return true;
        if (ItemPreviewKeys(k)) return true;
        if (MarketBillKeys(k)) return true;
        if (BlackMarketKeys(k)) return true;
        if (ShopKeys(k)) return true;
        if (GroupInviteKeys(k)) return true;
        if (CommunityKeys(k)) return true;
        if (GuildKeys(k)) return true;
        if (PartySearchKeys(k)) return true;
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
        var log = QuestLogSource();
        if (log == null) return;
        if (questState != null)
        {
            var parts = questState.Split(':');
            log.State = new QuestShotState(questTable, int.Parse(parts[0]), parts[1].Split(',').Select(int.Parse), parts.Length > 2 ? int.Parse(parts[2]) : 0);
        }
        try
        {
            var perf = PerfLog.On && fieldMissions == null ? new PerfLog.Steps("field missions") : null;
            fieldMissions ??= FieldMissions.Load(assets.Packs, new GameStrings(assets.Packs, assets.Language).Languages, data.Locale, data.FeaturesOff,
                                                 questTable?.Words);     // the same languages, locale and features: its tables
            perf?.Step("load");
            perf?.Done();
        }
        catch (Exception e) { GD.PushWarning("field missions: " + e.Message); }
        var meta = fieldMeta;
        var constants = Ms2.Ui.FeatureElements.Constants(assets.Packs);
        var inputs = questGuideInputs = new QuestButtonInputs
        {
            FieldId = xblockFieldId, FieldExists = f => f == xblockFieldId, LocalPlayerAlive = true, HasFieldData = true,
            DirectQuestFindParty = Ms2.Ui.LobbyFeatures.IsEnabled(assets.Packs, Ms2.Ui.FeatureTable.Names[0xdc - 1]),
        };
        questGuideSide = new Ms2.Ui.Hud.QuestGuideController
        {
            Table = questTable,
            State = log.State,
            Missions = fieldMissions,
            ParentField = f => meta.ParentFields.TryGetValue(f, out int p) ? p : null,
            FieldId = xblockFieldId,
            GotoButtons = (d, q) => QuestCommon.Buttons(d, q, inputs, meta, log.State, dungeonRooms ??= DungeonRooms.Load(assets.Packs, questTable.Available)),
            FeatureOn = id => id >= 1 && id <= Ms2.Ui.FeatureTable.Names.Length && Ms2.Ui.LobbyFeatures.IsEnabledByName(assets.Packs, Ms2.Ui.FeatureTable.Names[id - 1]),
            PageCount = (int)constants.Float("QuestGuidePageCount", 4),
            MaxCount = (int)constants.Float("QuestGuideMaxCount", 40),
        };
        var model = NewModel();
        questGuide = new MovieDialog(assets, questGuideSide, model, ScreenMode.PixelSize(GetWindow()));
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
        if (GuideRefusesOpen("QuestDialog")) return;
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
            SetWordFeatures();
            questLogModel = NewModel();
            questLogSide = new Ms2.Ui.Hud.QuestDialogController
            {
                Log = log,
                Detail = new QuestDetailContext
                {
                    FieldId = xblockFieldId,
                    LocalTm = Ms2.Net.MsTime.LocalTm,
                    Map = QuestMapGraph.Shared(assets.Packs).Graph,
                    // offline: the player is alive in this field, no party search, the server time unknown (no end time line)
                    Buttons = (d, q) => QuestCommon.Buttons(d, q, new QuestButtonInputs
                    {
                        FieldId = xblockFieldId, FieldExists = f => f == xblockFieldId, LocalPlayerAlive = true, HasFieldData = true,
                        DirectQuestFindParty = Ms2.Ui.LobbyFeatures.IsEnabled(assets.Packs, Ms2.Ui.FeatureTable.Names[0xdc - 1]),
                    }, fieldMeta, log.State, dungeonRooms ??= DungeonRooms.Load(assets.Packs, questTable.Available)),
                },
            };
            if (questShot != null) (questLogSide.OpenTab, questLogSide.OpenSubTab) = (questShot[0], questShot[1]);
            questLogSide.Unloaded += () => { questLog?.Host.Unload(); questLog?.QueueFree(); questLog = null; };
        }
        var t = System.Diagnostics.Stopwatch.StartNew();
        questLog = new MovieDialog(assets, questLogSide, questLogModel, ScreenMode.PixelSize(GetWindow()));
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
            var perf = PerfLog.On && questTable == null ? new PerfLog.Steps("quest tables") : null;
            fieldMeta ??= QuestMapGraph.SharedMeta(assets.Packs);
            perf?.Step("field-meta");
            questTable ??= DataCache.Quests(assets.Packs, new DataCache.QuestConfig
            { Languages = new GameStrings(assets.Packs, assets.Language).Languages, Locale = data.Locale, FeaturesOff = data.FeaturesOff });
            perf?.Step("quest-table");
            perf?.Done();
            fieldCodes ??= QuestTable.FieldCodes(FieldData.Document(assets.Packs), questTable.Available);   // the session's parsed table
            var live = Ms2Godot.Net.LiveSession.Instance;
            var meta = fieldMeta;
            IQuestState state = questFixture ? new QuestShotState(questTable, data.Player.Level)
                : live?.GameSession != null
                    ? new Ms2Godot.Net.LiveQuestState(live.Quests, questTable, live.GameSession, live.CharacterId, PlayerLevel,
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
        eventWidgets = new List<MovieDialog>();
        var log = QuestLogSource();
        var widgets = new List<Ms2.Ui.Hud.EventWidgetController> { new Ms2.Ui.Hud.BurningEventWidgetController(hudModel) };
        if (log != null) widgets.Insert(0, new Ms2.Ui.Hud.LevelUpPackageWidgetController(hudModel) { Table = questTable, State = log.State });
        foreach (var c in widgets)
        {
            if (!c.ShouldOpen()) continue;
            var m = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
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
    /// <summary>TaxiStationAction::vfunc_2 (@0x14095bef0, actionkeyobject.cpp:811-821): the world map (0x4e42) opens when
    /// closed, SetTaxiMode(1), then guide trigger 0x11.</summary>
    void TaxiKey()
    {
        if (!WorldMapOpen) OpenWorldMap();
        if (worldMap != null && worldMapSide != null) worldMapSide.SetTaxiMode(1);   // Unk_GetUI(0x4e42)
        Ms2.Ui.Guide.GuideManager.Current?.AddTrigger(Ms2.Ui.Guide.GuideTrigger.TaxiAction, 0, 0);
    }

    /// <summary>The world map's TakeTaxi hooks over the live session (offline: no player, so no taxi runs).</summary>
    void WireTaxi(Ms2.Ui.Hud.WorldMapController c)
    {
        c.HasPlayer = () => Ms2Godot.Net.LiveSession.Instance is { TaxiPlayerReady: true };
        c.PlayerDead = () => Ms2Godot.Net.LiveSession.Instance?.TaxiPlayerDead ?? false;
        c.PlayerStateId = () => Ms2Godot.Net.LiveSession.Instance?.TaxiPlayerStateId ?? 0;
        c.CanEnterState = _ => Ms2Godot.Net.LiveSession.Instance is { TaxiCalling: false };   // STILL-OPEN: FUN_141527160's other gates; a call started while the own skill sequence runs ends on the next frame (TaxiStateLeft), where the client's ChangeState(0x25) replaces the skill state
        c.StationNear = () => Ms2Godot.Net.LiveSession.Instance?.TaxiStationNear ?? false;
        c.Mesos = () => hudModel.HasLocalPlayer ? hudModel.Player.Mesos : 0;   // DAT_143ab2898
        c.PlayerRiding = () => Ms2Godot.Net.LiveSession.Instance?.TaxiPlayerRiding ?? false;
        c.StartTaxiCall = field =>
        {
            if (Ms2Godot.Net.LiveSession.Instance is not { } live) return;
            var k = Ms2.Ui.FeatureElements.Constants(assets.Packs);
            live.TaxiCallDurationMs = (int)k.Float("TaxiCallDuration", 3000);
            live.TaxiBestDriverDurationMs = (int)k.Float("TaxiCallBestDriverDuration", 3000);
            live.TaxiBestDriverLevel = (int)k.Float("TaxiCallBestDriverLevel", 25);
            live.StartTaxiCall(field, PlayerLevel);
        };
    }

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
        if (GuideRefusesOpen("WorldMapDialog")) return;
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
            int fieldId = xblockFieldId;
            worldMapSide = new Ms2.Ui.Hud.WorldMapController
            {
                Table = worldMapTable,
                Meta = meta,
                FieldId = fieldId,
                PlayerName = Ms2Godot.Net.LiveSession.Active ? hudModel.Player.Name : data.Player.Name,
                PlayerLevel = PlayerLevel,
                // the job group: live the user block's (WatchLivePlayer), the fixture's sub job / 10 (HudModel)
                JobGroupId = Ms2Godot.Net.LiveSession.Active ? hudModel.Player.JobGroup : data.Player.Job <= 1 ? 10 : (data.Player.Job / 10 + 1) * 10,
                VisitedFields = new[] { (uint)fieldId },
                Npcs = World.FieldContents.Npcs(assets.Packs),
                Rooms = dungeonRooms ??= DungeonRooms.Load(assets.Packs, e => Ms2.Ui.FeatureElements.IsAvailable(assets.Packs, e)),
                Missions = fieldMissions,       // the quest tracker's, read when the HUD starts
                MissionRewards = fieldMissionTable ??= Ms2.Ui.WorldMap.FieldMissionTable.Load(assets.Packs),
            };
            WireTaxi(worldMapSide);
            worldMapSide.Unloaded += () => { worldMap?.Host.Unload(); worldMap?.QueueFree(); worldMap = null; };
        }
        // DAT_143ab3078, the found-taxi list the taxi tiles read (FUN_141b5ee80)
        var foundTaxis = new HashSet<uint>(data.FoundTaxis.Count);
        foreach (int field in data.FoundTaxis) foundTaxis.Add((uint)field);
        worldMapSide.FoundTaxis = foundTaxis;
        worldMap = new MovieDialog(assets, worldMapSide, model, ScreenMode.PixelSize(GetWindow()));
        uiRoot.AddChild(worldMap);
    }


    // ---- level-up guide: uilevelupguidedialog run by its own ActionScript, UILevelUpGuideDialog's C++ side as LevelUpGuideController

    MovieDialog levelUpGuideMovie;
    Ms2.Ui.Hud.LevelUpGuideController levelUpGuide;     // kept between opens, as the client keeps the dialog (check boxes, slot names)
    static SortedDictionary<int, Ms2.Ui.Hud.LevelUpGuideController.Contents> recommendContents;   // LoadRecommendContents, read once

    /// <summary>CUIService::ToggleDialog(0x4efa): the game menu's level-up guide.</summary>
    void ToggleLevelUpGuide()
    {
        if (levelUpGuideMovie != null && levelUpGuide?.IsClosed == false) levelUpGuide.Close();
        else OpenLevelUpGuide();
    }

    /// <summary>
    /// Opens the level-up guide over the quest log's tables and quest state (offline: no records, so every guide is locked
    /// and no event guide shows) and table/recommendContents.xml. A reward claim sends Quest 0x18 on a live session; the
    /// world map link opens the world map at the row's field or search text; the dungeon room dialog and the other
    /// linked dialogs but the inventory are not in this HUD yet and are only logged.
    /// </summary>
    void OpenLevelUpGuide()
    {
        if (GuideRefusesOpen("LevelUpGuideDialog")) return;
        if (levelUpGuideMovie != null) { levelUpGuideMovie.Host.Unload(); levelUpGuideMovie.QueueFree(); levelUpGuideMovie = null; }
        var log = QuestLogSource();
        if (log == null) return;
        if (levelUpGuide == null)
        {
            recommendContents ??= Ms2.Ui.Hud.LevelUpGuideController.LoadContents(assets.Table("table/recommendcontents.xml"),
                e => Ms2.Ui.FeatureElements.IsAvailable(assets.Packs, e));
            levelUpGuide = new Ms2.Ui.Hud.LevelUpGuideController
            {
                Rows = recommendContents,
                Npcs = World.FieldContents.Npcs(assets.Packs),
                Meta = fieldMeta,
            };
            levelUpGuide.SendGuideReward += quest =>
            {
                if (Ms2Godot.Net.LiveSession.Instance is { } live) live.SendTalk(new Ms2.Net.Packets.QuestGuideRewardRequest(quest));
                else GD.Print($"level-up guide: reward of quest {quest} not sent (offline)");
            };
            levelUpGuide.OpenWorldMap += (field, search) =>
            {
                if (!WorldMapOpen) OpenWorldMap();
                if (field != 0) worldMapSide?.SetPendingField(field);
                if (search.Length != 0) worldMapSide?.SetPendingSearch(search);
            };
            levelUpGuide.OpenDungeonRoom += code => GD.Print($"level-up guide: the dungeon room dialog (room {code}) is not in this HUD yet");
            levelUpGuide.OpenDialog += name =>
            {
                if (name == "InventoryDialog") { if (inventory == null || inventory.IsClosed) ToggleInventory(); }
                else GD.Print($"level-up guide: {name} is not in this HUD yet");
            };
            levelUpGuide.Unloaded += () =>
            {
                levelUpGuideMovie?.Host.Unload();
                levelUpGuideMovie?.QueueFree();
                levelUpGuideMovie = null;
            };
        }
        levelUpGuide.Table = questTable;
        levelUpGuide.State = log.State;
        levelUpGuideMovie = new MovieDialog(assets, levelUpGuide, hudModel, ScreenMode.PixelSize(GetWindow()));
        uiRoot.AddChild(levelUpGuideMovie);
    }


    /// <summary>CFeatureManager by id for the bold words' dictionary links (feature 0x2f) and the dictionary's tables.</summary>
    void SetWordFeatures() =>
        questTable.Words.FeatureEnabled = id => id >= 1 && id <= Ms2.Ui.FeatureTable.Names.Length
                                                && Ms2.Ui.LobbyFeatures.IsEnabledByName(assets.Packs, Ms2.Ui.FeatureTable.Names[id - 1]);


    // ---- dictionary: uigamedictionarydialog run by its own ActionScript, UIGameDictionaryDialog's C++ side as GameDictionaryController

    MovieDialog gameDictionaryMovie;
    Ms2.Ui.Hud.GameDictionaryController gameDictionary;  // kept between opens, as the client keeps the dialog (tab, search text)

    /// <summary>CUIService::ToggleDialog(0x4f0b): the game menu's dictionary.</summary>
    void ToggleGameDictionary()
    {
        if (gameDictionaryMovie != null && gameDictionary?.IsClosed == false) gameDictionary.Close();
        else OpenGameDictionary();
    }

    /// <summary>
    /// Opens the dictionary over the reserved word table's dictionary lists, the npc table and the field meta. Offline no
    /// npc field has an open time window (no server time), and the item tooltip's owner is the fixture character.
    /// </summary>
    void OpenGameDictionary()
    {
        if (GuideRefusesOpen("GameDictionaryDialog")) return;
        if (gameDictionaryMovie != null) { gameDictionaryMovie.Host.Unload(); gameDictionaryMovie.QueueFree(); gameDictionaryMovie = null; }
        if (QuestLogSource() == null) return;
        SetWordFeatures();
        if (gameDictionary == null)
        {
            gameDictionary = new Ms2.Ui.Hud.GameDictionaryController
            {
                Words = questTable.Words,
                Npcs = World.FieldContents.Npcs(assets.Packs),
                Meta = fieldMeta,
            };
            gameDictionary.Owner = () =>
            {
                var live = Ms2Godot.Net.LiveSession.Instance;
                var me = live != null ? live.Me : OfflineMe();
                return me?.ToPcInfo(live?.GameSession?.ServerTime ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()) is { } r ? (r.CharacterId, r.Name) : null;
            };
            gameDictionary.Unloaded += () =>
            {
                gameDictionaryMovie?.Host.Unload();
                gameDictionaryMovie?.QueueFree();
                gameDictionaryMovie = null;
            };
        }
        gameDictionaryMovie = new MovieDialog(assets, gameDictionary, hudModel, ScreenMode.PixelSize(GetWindow()));
        uiRoot.AddChild(gameDictionaryMovie);
    }


    // ---- learning quest: uilearningquestdialog run by its own ActionScript, UILearningQuestDialog's C++ side as LearningQuestController

    MovieDialog learningQuestMovie;
    MovieVideo learningQuestVideo;
    Ms2.Ui.Hud.LearningQuestController learningQuest;
    Action unwatchLearningQuest;
    static SortedDictionary<int, Ms2.Ui.Hud.LearningQuestController.Learning> learningRows;   // CLearningQuestTable, read once
    static Dictionary<int, int> learningNewMarks;    // the dialog's m_310 of this game session, kept across field scenes
    static long learningNewMarksFor = -1;            // the character it was made for

    /// <summary>
    /// The dialog object, made at field entry (the client builds it with the UI and keeps it): the seen files read, and for
    /// the first field of a character the game start's new marks (NotifyGameStart) and OnChangeServer's
    /// <see cref="Ms2.Ui.Hud.LearningQuestController.MarkDirty"/>. A quest record change (vfunc_o178_2) and a level change
    /// (vfunc_oa0_9) mark it again; <see cref="_Process"/> runs its vfunc_72, which opens it on a new learning quest.
    /// </summary>
    void LearningQuest()
    {
        if (learningQuest != null) return;       // a window relayout keeps it
        var log = QuestLogSource();
        if (log == null) return;
        try
        {
            learningRows ??= Ms2.Ui.Hud.LearningQuestController.Load(
                Ms2.Ui.FeatureElements.Collect(assets.Packs, assets.Table("table/learningquest.xml").Root, "learning", "id"), questTable);
        }
        catch (Exception e) { GD.PushWarning("learning quest table: " + e.Message); return; }
        var live = Ms2Godot.Net.LiveSession.Instance;
        var folders = GameOptions.Folders();
        long who = Ms2Godot.Flow.Session.Chosen?.User?.CharacterId ?? 0;
        bool gameStart = learningNewMarks == null || learningNewMarksFor != who;
        if (gameStart) { learningNewMarks = new Dictionary<int, int>(); learningNewMarksFor = who; }
        var c = learningQuest = new Ms2.Ui.Hud.LearningQuestController
        {
            Rows = learningRows,
            Table = questTable,
            State = log.State,
            NewMarks = learningNewMarks,
            HasLocalPlayer = () => true,
            PlayerLevel = () => hudModel.Player.Level,
            FieldId = () => live?.FieldId ?? xblockFieldId,
            AutoOpenOff = () => GameOptions.LearningQuest.Value,
            CharacterFile = folders == null ? null : System.IO.Path.Combine(folders.Value.Character, "learningQuestCharacter.xml"),
            AccountFile = folders == null ? null : System.IO.Path.Combine(folders.Value.Account, "learningQuestAccount.xml"),
        };
        c.SendStart += id =>
        {
            if (live != null) live.SendTalk(new Ms2.Net.Packets.QuestLearningRequest(id));
            else GD.Print($"learning quest: start of learning quest {id} not sent (offline)");
        };
        c.OpenModelHouse += () => GD.Print("learning quest: the model house dialog (0x4f3f) is not in this HUD yet");
        c.OpenRequested += OpenLearningQuestMovie;
        c.Unloaded += () =>
        {
            learningQuestMovie?.Host.Unload();
            learningQuestMovie?.QueueFree();
            learningQuestMovie = null;
            learningQuestVideo = null;
        };
        c.LoadSeen();
        if (gameStart) { c.OnGameStart(); c.MarkDirty(); }
        Action<int> onQuest = _ => c.MarkDirty();
        Action<Ms2.Ui.StatusEvent> onStatus = e => { if (e == Ms2.Ui.StatusEvent.Level) c.MarkDirty(); };
        hudModel.Changed += onStatus;
        if (live != null) live.Quests.Changed += onQuest;
        unwatchLearningQuest = () =>
        {
            hudModel.Changed -= onStatus;
            if (live != null) live.Quests.Changed -= onQuest;
        };
    }

    /// <summary>FUN_141924680, the dialog shown: its movie with the guide video's picture (setVideo's VideoPlayer in m_video).</summary>
    void OpenLearningQuestMovie()
    {
        if (learningQuestMovie != null) { learningQuestMovie.Host.Unload(); learningQuestMovie.QueueFree(); learningQuestMovie = null; }
        var m = learningQuestMovie = new MovieDialog(assets, learningQuest, hudModel, ScreenMode.PixelSize(GetWindow()));
        learningQuestVideo = new MovieVideo(m, assets.Packs, () =>
            m.Host.Child("m_video") is Ms2.Avm2.Flash.AsContainer video ? video.getChildByName("videoPlayer") : null);
        m.AddChild(learningQuestVideo);
        uiRoot.AddChild(m);
    }

    /// <summary>
    /// UIGameMenuDialog::OpenLearningQuestDialog (@0x14193bdf0), the game menu's learning quest entry: nothing while the
    /// dialog is open; on a field that does not allow it the notice s_learning_quest_dungeon, else the first learning quest
    /// that can be taken opens it, or the notice s_learning_quest_empty when there is none. It does not close the dialog.
    /// </summary>
    void OpenLearningQuest()
    {
        if (learningQuest == null || !learningQuest.IsClosed) return;
        if (!learningQuest.FieldAllows()) SystemNotice("s_learning_quest_dungeon");
        else if (!learningQuest.PickFirst(false)) SystemNotice("s_learning_quest_empty");
    }


    // ---- adventure level: uiadventureleveldialog run by its own ActionScript, UIAdventureLevelDialog's C++ side as AdventureLevelController

    MovieDialog adventureLevelMovie;
    Ms2.Ui.Hud.AdventureLevelController adventureLevel;
    Ms2.Ui.Hud.AdventureLevelExpBarController adventureBar;
    Action unwatchPrestige;
    static Ms2.Ui.Hud.AdventureLevelTable adventureLevelTable;     // CAdventureLevelTable, read once

    /// <summary>CUIService::ToggleDialog(0x4f67): the game menu's adventure level dialog and the adventure bar's click.</summary>
    void ToggleAdventureLevel()
    {
        if (adventureLevelMovie != null && adventureLevel?.IsClosed == false) adventureLevel.Close();
        else OpenAdventureLevel();
    }

    /// <summary>
    /// Opens the adventure level dialog over table/adventurelevelreward.xml, adventurelevelability.xml and, with feature
    /// 0xfb, adventurelevelmission.xml, the player's adventure data (the Prestige packets, <see cref="WatchPrestige"/>) and
    /// the own buff list. A reward claim sends Prestige mode 3 or 5 on a live session.
    /// </summary>
    void OpenAdventureLevel()
    {
        if (GuideRefusesOpen("AdventureLevelDialog")) return;
        if (adventureLevelMovie != null) { adventureLevelMovie.Host.Unload(); adventureLevelMovie.QueueFree(); adventureLevelMovie = null; }
        if (adventureLevel == null)
        {
            adventureLevel = new Ms2.Ui.Hud.AdventureLevelController
            {
                Table = AdventureLevelTable(),
                HasEnabledBuff = (id, level) => ownBuffs?.Buffs.Any(b => b.Enabled != 0 && b.Id == id && (level == 0 || b.Level == level)) == true,
            };
            adventureLevel.SendClaimLevel += level =>
            {
                if (Ms2Godot.Net.LiveSession.Instance is { } live) live.SendTalk(new Ms2.Net.Packets.PrestigeLevelRewardRequest(level));
                else GD.Print($"adventure level: reward of level {level} not sent (offline)");
            };
            adventureLevel.SendClaimMission += mission =>
            {
                if (Ms2Godot.Net.LiveSession.Instance is { } live) live.SendTalk(new Ms2.Net.Packets.PrestigeMissionRewardRequest(mission));
                else GD.Print($"adventure level: reward of mission {mission} not sent (offline)");
            };
            adventureLevel.Unloaded += () =>
            {
                adventureLevelMovie?.Host.Unload();
                adventureLevelMovie?.QueueFree();
                adventureLevelMovie = null;
            };
        }
        adventureLevelMovie = new MovieDialog(assets, adventureLevel, hudModel, ScreenMode.PixelSize(GetWindow()));
        uiRoot.AddChild(adventureLevelMovie);
    }

    Ms2.Ui.Hud.AdventureLevelTable AdventureLevelTable() =>
        adventureLevelTable ??= Ms2.Ui.Hud.AdventureLevelTable.Load(assets.Table("table/adventurelevelreward.xml"),
            assets.Table("table/adventurelevelability.xml"),
            hudModel.IsFeatureEnabled(Ms2.Ui.Hud.AdventureLevelTable.MissionFeature) ? assets.Table("table/adventurelevelmission.xml") : null,
            e => Ms2.Ui.FeatureElements.IsAvailable(assets.Packs, e),
            hudModel.IsFeatureEnabled(Ms2.Ui.Hud.AdventureLevelTable.MissionFeature), hudModel.Tables.Additional);

    /// <summary>
    /// The HUD's part of CSceneStateGame::OnPrestige (scenestategame.cpp:8815-8955) for each live Prestige packet. The
    /// session already wrote the adventure data (LiveSession.Adventure, the client's globals) and the PCs' levels, so this
    /// HUD only reacts, and a new HUD after a field move starts from the session's data:
    /// <list type="bullet">
    /// <item>1: the status event 0x12, then the bar's SetPoint(gain);</item>
    /// <item>2: for the local player the level, the status event 0x11; for any PC the bar's SetPoint(0) (:8833 and :8853
    /// break to it);</item>
    /// <item>0: the bar's SetPoint(0);</item>
    /// <item>4 with result 1: with feature 0xfb the open dialog's level guide (no SetPoint: the case returns);</item>
    /// <item>7 with feature 0xfb: the open dialog's mission list, then, with the game menu, SetPoint(0) (:8917-8926);</item>
    /// <item>6 with feature 0xfb and result 1: the open dialog's mission list, then SetPoint(0) (:8945-8950).</item>
    /// </list>
    /// STILL-OPEN: the exp gain notice (FUN_1413610f0), kind 2's level-up effect (any PC), reward notice 0x4e5d and
    /// FUN_141b16580, the game menu notice FUN_141938ae0 after kinds 4 and 7 and, without feature 0xfb, the older dialog
    /// 0x4f87 are not ported.
    /// </summary>
    void WatchPrestige()
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        if (live == null) return;
        Action<Ms2.Net.Packets.PrestigeMessage> onPrestige = m => ApplyPrestige(m, live.Me.ObjectId);
        live.PrestigeReceived += onPrestige;
        unwatchPrestige = () => live.PrestigeReceived -= onPrestige;
    }

    Action unwatchMeso;
    Ms2.Ui.Hud.CurrencyFeed currencyFeed;

    Ms2.Ui.Hud.CurrencyFeed MakeCurrencyFeed() => new(hudModel)
    {
        Sound = key => FieldAudio.Instance?.PlayUi(key),
        Line = (text, kind) => ChatLog.AddSystemLine(text, kind),
        Notice = SystemNoticeText,
        MeratNotice = v => EnsureRewardNotify().ShowMeratNotice(v),
        MeratRedNotice = v => EnsureRewardNotify().ShowMeratRedNotice(v),
        TokenMoneyNotice = (type, delta) => EnsureRewardNotify().NotifyChangedTokenMoney(type, delta),
    };

    /// <summary>The upper HUD runs on its own model: the balances the feed stored on the HUD player go to it and its status event follows
    /// (NotifyStatusChanged 0xb / 0xd, scenestategame.cpp:7089), so its merat text updates.</summary>
    void MirrorMerat(bool red)
    {
        if (upperHudModel == null || hudModel == null) return;
        var from = hudModel.Player; var to = upperHudModel.Player;
        to.Merats = from.Merats; to.RedMerat = from.RedMerat;
        System.Array.Copy(from.MeratWallet, to.MeratWallet, 4);
        upperHudModel.Notify(red ? Ms2.Ui.StatusEvent.RedMerat : Ms2.Ui.StatusEvent.Merat);
    }

    /// <summary>--uigen-open currency: the Meret and token packets' effects once, as the live receives run them.</summary>
    void CurrencyShot()
    {
        var feed = currencyFeed ??= MakeCurrencyFeed();
        var meret = new Ms2.Net.Packets.MeretUpdate(1200, 0, 0, 0, 500);
        feed.OnMeret(meret);
        MirrorMerat(false);
        feed.OnCurrencyToken(new Ms2.Net.Packets.CurrencyTokenUpdate(5, 1990, 150, 0, 40));
    }

    NewsNotificationRouter MakeNewsRouterFor()
    {
        var r = newsRouter;
        r.Register(NewsNotificationRouter.Notice, url => (notice?.Host.Controller as NoticeDialogController)?.OpenUrl(url));
        return r;
    }

    SmartPushNotices smartPushNotices;

    SmartPushNotices SmartPushNotices => smartPushNotices ??= new SmartPushNotices(
        hudModel.Tables.SmartPush, key => hudModel.Tables.Common(key) ?? "",
        (id, level) => hudModel.Tables.AdditionalDescription((uint)id, level, "name") ?? "")
    {
        SystemNotice = SystemNoticeText,
        ShowBox = MessageBoxQueue.Show,
        ChargeFeature = () => hudModel?.IsFeatureEnabled(0xc2) == true,
        SendAnswer = (id, n1c, accept) => Ms2Godot.Net.LiveSession.Instance?.SendTalk(new Ms2.Net.Packets.SmartPushAnswer(id, n1c, accept)),
        // STILL-OPEN: the charge page (a web page) is not drawn
        ChargeRequested = _ => { },
    };

    /// <summary>OnSmartPush (:7335): a position (id, value) goes to the push with that id (FUN_140dd86a0, vfunc_4); mode 1 asks
    /// about the lack of merat (ShowLackMeratAsk); another mode shows its string as a centre notice (FUN_141275260).</summary>
    void OnSmartPush(Ms2.Net.Packets.SmartPushMessage m)
    {
        if (m.Positional) { SmartPushNotices.OnPositional(m.X, m.Y); return; }
        if (m.Mode == 1) { SmartPushNotices.ShowLackMeratAsk(); return; }
        string text = hudModel.Tables.Common(m.StringId);
        if (!string.IsNullOrEmpty(text)) SystemNoticeText(text);
    }

    /// <summary>The Meso packets' chat line (OnMeso, scenestategame.cpp:7019); the balance reaches the HUD through the live
    /// player (status event 8).</summary>
    void WatchMeso()
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        if (live == null) return;
        Action<long, int> onMeso = (delta, kind) => MesoLine(delta, kind);
        live.MesoChanged += onMeso;
        Action<Ms2.Net.Packets.ItemDropNotice> onDrop = n => ChatLog.AddItemDropNotice(n.Name, n.ItemId, n.Grade, n.Uid);
        live.ItemDropNoticed += onDrop;
        currencyFeed = MakeCurrencyFeed();
        Action<Ms2.Net.Packets.MeretUpdate> onMeret = m => { currencyFeed.OnMeret(m); MirrorMerat(false); };
        Action<Ms2.Net.Packets.MeretUseUpdate> onMeretUse = m => { currencyFeed.OnMeretUse(m); MirrorMerat(true); };
        Action<Ms2.Net.Packets.CurrencyTokenUpdate> onToken = currencyFeed.OnCurrencyToken;
        Action<Ms2.Net.Packets.SystemSound> onSound = m => { if (m.Key.Length > 0) FieldAudio.Instance?.PlayUi(m.Key); };
        Action<Ms2.Net.Packets.SmartPushMessage> onPush = OnSmartPush;
        // NotifyLearnEmotion (scenestategame.cpp:2561) reaches UIRewardNotify::OnNotifyLearnEmotion
        Action<int> onEmotion = id => EnsureRewardNotify().OnLearnEmotion(id);
        live.Emotions.Learned += onEmotion;
        live.MeretReceived += onMeret; live.MeretUseReceived += onMeretUse; live.CurrencyTokenReceived += onToken;
        live.SystemSoundReceived += onSound; live.SmartPushReceived += onPush;
        var news = MakeNewsRouterFor();
        Action<Ms2.Net.Packets.NewsNotification> onNews = news.Handle;
        Action<Ms2.Net.Packets.NoticeDialogMessage> onNotice = m => (notice?.Host.Controller as NoticeDialogController)?.OnNoticeDialog(m.Flag, (uint)Time.GetTicksMsec(), noticeOpenTicks);
        live.NewsNotificationReceived += onNews; live.NoticeDialogReceived += onNotice;
        unwatchMeso = () =>
        {
            live.MesoChanged -= onMeso; live.ItemDropNoticed -= onDrop; live.Emotions.Learned -= onEmotion;
            live.MeretReceived -= onMeret; live.MeretUseReceived -= onMeretUse; live.CurrencyTokenReceived -= onToken;
            live.SystemSoundReceived -= onSound; live.SmartPushReceived -= onPush;
            live.NewsNotificationReceived -= onNews; live.NoticeDialogReceived -= onNotice;
        };
    }

    /// <summary>
    /// FUN_1413612c0 (uiworldshareinfoeventlistener.cpp:225): a gain is s_msg_take_meso (FormatStringId 0x2c3), a loss
    /// s_msg_consume_meso (0x2c4), each with the grouped amount (FUN_1409404f0), as a chat line of kind 5 (FUN_1412751b0,
    /// the Require "meso" switch); nothing for 0. STILL-OPEN: the PC cafe suffix s_msg_take_pcbang (0x2c5), added to a gain of
    /// kind 0x15 or 0xea61 when the PC's FUN_141658fa0 flags meet the client constant at +0x5968, is not ported.
    /// </summary>
    void MesoLine(long delta, int kind)
    {
        if (delta == 0) return;
        int id = delta > 0 ? 0x2c3 : 0x2c4;
        string text = Ms2.Quest.ClientFormat.Format(hudModel.Tables.Common(id), Ms2.Ui.AchieveTexts.Group(Math.Abs(delta)));
        ChatLog.AddSystemLine(text, Ms2.Ui.Hud.TalkKind.Meso);
    }

    void ApplyPrestige(Ms2.Net.Packets.PrestigeMessage m, int selfObject)
    {
        var p = hudModel.Player;
        bool missions = hudModel.IsFeatureEnabled(Ms2.Ui.Hud.AdventureLevelTable.MissionFeature);
        bool open = adventureLevelMovie != null && adventureLevel?.IsClosed == false;
        switch (m)
        {
            case Ms2.Net.Packets.PrestigeMessage.ExpGain g:
                hudModel.Notify(Ms2.Ui.StatusEvent.AdventureExp);
                adventureBar?.SetPoint(g.Gain);
                break;
            case Ms2.Net.Packets.PrestigeMessage.LevelUp u:
                if (u.ObjectId == selfObject)
                {
                    p.AdventureLevel = u.Value;
                    hudModel.Notify(Ms2.Ui.StatusEvent.AdventureLevel);
                }
                adventureBar?.SetPoint(0);
                break;
            case Ms2.Net.Packets.PrestigeMessage.Loaded:
                adventureBar?.SetPoint(0);
                break;
            case Ms2.Net.Packets.PrestigeMessage.ClaimResult c:
                if (c.Result != 1) break;
                if (missions) { if (open) adventureLevel.RefreshLevelGuide(); }
                else GD.Print("adventure level: the older adventure dialog 0x4f87 is not in this HUD");
                break;
            case Ms2.Net.Packets.PrestigeMessage.MissionList ml:
                if (!missions || ml.Result != 1 || !open) break;
                adventureLevel.RefreshMissionList();
                if (ml.Kind == 7 && gameMenuMovie == null) break;
                adventureBar?.SetPoint(0);
                break;
        }
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
        hudModel.CanBreakItem = it => Ms2Godot.Net.LiveSession.Instance?.BreakManager.CanBreakItem(it) == true;   // CItemBreakManager::CanBreakItem
        hudModel.LocalPlayer = () =>
        {
            var live = Ms2Godot.Net.LiveSession.Instance;
            return (live != null ? live.Me : OfflineMe()).ToPcInfo(live?.GameSession?.ServerTime ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        };
        // UIAlignWidgetManager::LoadWidgets (uiservice.cpp:1941): table/alignwidget.xml, one manager for the scene
        hudModel.AlignWidgets = alignWidgets ??= AlignWidgetManager.Load(assets.Table("table/alignwidget.xml")?.Root);
        hudModel.Keys = new KeyTable(assets.Packs, assets.Language);
        // the server's key table when live (OnKeyTable), else the packet a new character gets
        hudModel.ServerKeys = Ms2Godot.Net.LiveSession.Instance?.KeyTable is { Loaded: true } live ? live
            : quickSlotShot != null ? QuickSlotShotTable(quickSlotShot) : UiFixture.NewCharacterKeyTable();
        // the pad file load of the first table sends the keys (keytable.cpp:560): the sender is on before it
        if (Ms2Godot.Net.LiveSession.Instance is { } keySender)
        {
            if (keysSentHandler != null) hudModel.Keys.KeysSentToServer -= keysSentHandler;
            hudModel.Keys.KeysSentToServer += keysSentHandler = keySender.SendKeyTableSaveAll;
        }
        hudModel.Keys.Apply(hudModel.ServerKeys, PadDefaultsKind());
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
        p.AdventureLevel = data.Player.PrestigeLevel;      // the user block's m_530 (vfunc_141); live, WatchLivePlayer's
        // the session's adventure data (the client's globals), so a relayout or a field move keeps it
        if (Ms2Godot.Net.LiveSession.Instance is { } liveAdventure) { p.Adventure = liveAdventure.Adventure; p.Lapenshards = liveAdventure.Lapenshards; }
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
        WatchLivePlayer(hudModel);
    }

    /// <summary>Unk_Inventory::GetItemUseError for the HUD's player (<see cref="HudModel"/>).</summary>
    Ms2.Ui.Items.ItemUse itemUse;

    PlayerNameTag playerNameTag;
    Action unwatchLivePlayer;

    /// <summary>
    /// Live, the HUD player is the session's own character (<see cref="Ms2.Ui.PlayerStateLive"/>: the own FieldAddUser, Stat,
    /// ServerEnter / ExpUp and LevelUp, each field from the client's handler), the chosen character list entry before the own
    /// FieldAddUser; each change sends its status event, and the name tag, the own HP bar and the low-HP effect follow
    /// (OnAddUser's SetName, OnStat's PCBase::vfunc_74). Offline the fixture stays.
    /// </summary>
    void WatchLivePlayer(Ms2.Ui.UiModel model)
    {
        if (Ms2Godot.Net.LiveSession.Instance is not { } live) return;
        void Sync()
        {
            var changed = model.Player.Apply(live.Me, Ms2Godot.Flow.Session.Chosen?.User);
            foreach (var e in changed) model.Notify(e);
            if (model != hudModel) return;
            OwnMinimapPicture(live);
            if (changed.Contains(Ms2.Ui.StatusEvent.Name)) playerNameTag?.SetName(model.Player.Name);
            if (changed.Contains(Ms2.Ui.StatusEvent.Hp))
            {
                damageEffect?.Hp(model.Player.HpCur, model.Player.HpMax);
                playerBar?.Stat(model.Player.HpCur, model.Player.HpMax);
            }
        }
        Sync();
        live.Me.Changed += Sync;
        unwatchLivePlayer += () => live.Me.Changed -= Sync;
    }

    /// <summary>
    /// The minimap's own circle (createPlayer's imgpath, FUN_141028be0(id, picture, 1)) once the own PC is known: the
    /// first field's minimap is built before the own FieldAddUser brings the user block's picture, so it starts with the
    /// default image and gets the picture by changePlayer here (a picture not downloaded yet follows from
    /// <see cref="ProfileImageReady"/>).
    /// </summary>
    void OwnMinimapPicture(Ms2Godot.Net.LiveSession live)
    {
        if (minimapSide == null || live.Me.User == null) return;
        var path = Ms2Godot.Net.ProfileImageService.Images.Path(live.CharacterId, live.Me.User.Picture ?? "", true);
        if (path != minimapSide.PlayerImage) minimapSide.OnProfileImage(live.CharacterId, path);
    }

    /// <summary>The HUD player's level: the live character's (<see cref="WatchLivePlayer"/>) or the fixture's.</summary>
    int PlayerLevel => Ms2Godot.Net.LiveSession.Active && hudModel != null ? hudModel.Player.Level : data.Player.Level;

    /// <summary>
    /// UIMainPlayerInfo (uimainplayerinfo), UIExpBar (uiexpbar) and UIAdventureLevelExpBar (uiadventurelevelexpbar), each run from its own movie with its C++ side as the
    /// controller (Ms2.Ui.Hud). The model is the HUD's numbers (<see cref="StubHudData"/>); feature 0xbd (the
    /// adventure level bar) is on in the Live feature table.
    /// </summary>
    void PlayerInfo(Control ui)
    {
        var p = hudModel.Player;
        var window = ScreenMode.PixelSize(GetWindow());
        playerInfo = new Ms2.Ui.Hud.MainPlayerInfoController
        {
            SlotWorld = QuickSlotWorld,
            SlotExpand = GameOptions.GamePad.Use,
            PadKind = () => optionHost?.GetPadKind() ?? 0,
            TooltipPlayer = () => { QuickSlotWorld(); return SkillTooltipPlayer(slotJobs); },
            Send = packet =>
            {
                if (Ms2Godot.Net.LiveSession.Instance is { } s) s.SendKeyTable(packet);
                else GD.Print($"quick slot: offline, not sent: {packet}");
            },
            Notice = id => SystemNoticeText(hudModel.Tables.Common(id)),
            NoticeBox = id => MessageBoxQueue.ShowMessageBox(hudModel.Tables.Common(id), 0, null, ""),
            NoticeText = key => SystemNoticeText(hudModel.Tables.Common(key)),
            ShiftHeld = () => Input.IsKeyPressed(Key.Shift),
            PressKey = ClickedSlotKey,
            UseSkill = ClickedSlotSkill,
        };
        OwnBuffs();
        ui.AddChild(playerInfoMovie = new MovieDialog(assets, playerInfo, hudModel, window));
        WatchQuickSlotState();
        ui.AddChild(expBarMovie = new MovieDialog(assets, new Ms2.Ui.Hud.ExpBarController(), hudModel, window));
        if (Ms2Godot.Net.LiveSession.Instance == null)      // live, the Prestige packets fill it (WatchPrestige)
            p.AdventureExp = (long)Math.Round(hudModel.Tables.AdventureLevelLvUpExp * data.AdventureExpPercent / 100);
        adventureBar = new Ms2.Ui.Hud.AdventureLevelExpBarController();
        // vfunc_28 OnClickBar: ToggleDialog(0x4f67) with feature 0xfb, else the older 0x4f87
        adventureBar.BarClicked += () =>
        {
            if (hudModel.IsFeatureEnabled(Ms2.Ui.Hud.AdventureLevelTable.MissionFeature)) ToggleAdventureLevel();
            else GD.Print("adventure level: the older adventure dialog 0x4f87 is not in this HUD");
        };
        ui.AddChild(adventureBarMovie = new MovieDialog(assets, adventureBar, hudModel, window));
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
        var c = gameMenu = new Ms2.Ui.Hud.GameMenuController { Table = gameMenuTable, NotifyCount = CommunityNotifyCount };
        c.SetNotifyRedCircle7Flag(agreementReminder);
        c.ToggleDialog += GameMenuToggle;
        WatchAchieves();
        WatchMails();
        WatchTrades();
        WatchStorage();
        WatchBlackMarket();
        WatchShop();
        WatchBeauty();
        c.MenuAction += def =>
        {
            if (def.Action == GameMenuActions.OpenLearningQuest) OpenLearningQuest();
            else if (def.Action == GameMenuActions.ToggleSystemShop) ToggleShopTab(9);   // uigamemenudialog.cpp:2209
        };
        WatchBuddies();
        WatchGuild();
        if (portrait == null && (portrait = PortraitView.Create(assets.Packs, Settings.NifXml)) != null) AddChild(portrait);
        ui.AddChild(gameMenuMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow())));
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
        else if (id == 0x4efa) ToggleLevelUpGuide();
        else if (id == 0x4f0b) ToggleGameDictionary();
        else if (id == 0x4e49) ToggleMail();
        else if (id == 0x4e4d) ToggleCommunity();
        else if (id == GuildDialogController.UiId) ToggleGuild();
        else if (id == PartySearchDialogController.UiId) TogglePartySearch();   // entry 25 "partysearch"
        else if (id == 0x4e73) ToggleAchieve();
        else if (id == 0x4f2d) ToggleSkillBook();
        else if (id == 0x4ef6) ToggleSkillCompact();    // entry 27 "compactkey" (gameeventmanagercli.cpp:692)
        else if (id == 0x4f67) ToggleAdventureLevel();
        else if (id == GameMenuController.CharacterAbilityUiId) ToggleCharacterAbility();
        else if (id == 0x4f1e) ToggleMastery();
        else if (id == 0x4e7a) ToggleDynamicAction();   // UIDynamicActionDialog
        else if (id == 0x4f80) ToggleChatEmoticon();    // UIChatEmoticonDialog
    }

    // ---- character window: uimyinfodialog run by its own ActionScript, UIMyInfoDialog's C++ side as MyInfoController --

    MovieDialog myInfoMovie;                    // from open until OnEndClose
    Ms2.Ui.Hud.MyInfoController myInfo;
    Ms2.Net.Packets.MyCharacter offlineMe;
    /// <summary>The offline own character: the HUD fixture's record, wearing what the field shows (the starter look, as the
    /// worn model's items) so the equipment window's icons match the model.</summary>
    Ms2.Net.Packets.UserBlock offlineChosen;
    Ms2.Net.Packets.MyCharacter OfflineMe()
    {
        var chosen = Flow.Session.Chosen?.User;
        if (offlineMe != null && ReferenceEquals(offlineChosen, chosen)
            && (Ms2Godot.Look.PlayerLook.OfflineWorn == null || ReferenceEquals(offlineMe.Equips, Ms2Godot.Look.PlayerLook.OfflineWorn))) return offlineMe;
        var me = offlineMe ?? UiFixture.Character();
        if (Ms2Godot.Look.PlayerLook.OfflineWorn is { } worn)
        {
            me.Equips = worn;
            if (me.User != null) { me.User.Gender = (byte)Ms2Godot.Look.PlayerLook.OfflineGender; me.User.JobCode = Ms2Godot.Look.PlayerLook.OfflineJob; }
        }
        // the chosen list entry's own name, level, job, gender and skin (the fixture's only when no character was chosen)
        Ms2Godot.Look.PlayerLook.TakeChosen(me.User, chosen);
        offlineChosen = chosen;
        return offlineMe = me;
    }

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
        if (GuideRefusesOpen("MyInfoDialog")) return;
        if (myInfoMovie != null) { myInfoMovie.Host.Unload(); myInfoMovie.QueueFree(); myInfoMovie = null; }
        var live = Ms2Godot.Net.LiveSession.Instance;
        var me = live != null ? live.Me : OfflineMe();
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
        void EquipChanged() => c.MarkDirty((long)Time.GetTicksMsec());
        me.Changed += Changed; me.StatPoints.Changed += Changed; me.EquipChanged += EquipChanged;
        if (live != null) live.AchievesChanged += Changed;
        var m = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        c.Unloaded += () =>
        {
            me.Changed -= Changed; me.StatPoints.Changed -= Changed; me.EquipChanged -= EquipChanged;
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
    /// GetProfileImagePath(key, 1)), the popup menu (GetProfileImagePath(key, 0)) and the two character windows (the key).
    /// </summary>
    void ProfileImageReady(long characterId, string key)
    {
        var images = Ms2Godot.Net.ProfileImageService.Images;
        minimapSide?.OnProfileImage(characterId, images.ProfileImagePath(key, true));
        popupMenu?.OnProfileImage(characterId, images.ProfileImagePath(key, false));
        myInfo?.OnProfileImage(characterId, key);
        pcInfo?.OnProfileImage(characterId, key);
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
        if (GuideRefusesOpen("UGCImageUploadDialog")) return;
        if (uploadDialogMovie != null) { uploadDialogMovie.Host.Unload(); uploadDialogMovie.QueueFree(); uploadDialogMovie = null; }
        var c = new Ms2.Ui.Hud.UgcImageUploadController(Ugc());
        BindBoxAnswers();
        var m = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
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

    /// <summary>The scripted live run's chat line: typed into the first chat window and sent as Return would
    /// (<see cref="Ms2.Ui.Hud.MessengerController.SubmitLine"/>); false without a window.</summary>
    public bool SubmitChatLine(string line)
    {
        if (messengers.Count == 0) return false;
        messengers[0].Side.SubmitLine(Ms2.Ui.Hud.MessengerController.Escape(line), "");
        return true;
    }

    /// <summary>
    /// The scripted live run's item link, step 1: the inventory opened (as I does) and the first tab holding an item
    /// selected. False without a live bag holding one; with an empty bag the last worn piece is first taken off (the
    /// unequip request), so a later call finds it there.
    /// </summary>
    public bool OpenInventoryOnAnItem()
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        var view = live?.Inventory;
        if (view == null) return false;
        if (Enumerable.Range(0, Ms2.Net.Packets.ItemBook.Tabs).All(t => view.Used(t) == 0))
        {
            var worn = live.Me.Equips?.Where(e => !e.IsBadge).OrderByDescending(e => (int)e.Slot).FirstOrDefault();
            if (worn != null) { GD.Print($"live: bag empty, taking off {worn.Id} ({worn.Uid})"); view.RequestUnequip?.Invoke(worn.Uid); }
            return false;
        }
        if (inventory == null || inventory.IsClosed) OpenInventory();
        if (inventory?.Host == null) return false;
        for (int i = 0; i < inventory.Tabs.Count; i++)
            if (view.Used(inventory.Tabs[i].Tab) > 0)
            {
                inventory.Host.Set(inventory.Host.Child("m_tab"), "selectedIndex", i);
                return true;
            }
        return false;
    }

    /// <summary>The scripted live run's group chat: the browser movie's OnCreateDefaultGroupMessenger call (its insert button).</summary>
    public void CreateGroupChat() => groupBrowser?.OnCallback("OnCreateDefaultGroupMessenger", System.Array.Empty<object>());

    /// <summary>The scripted live run's invite button: the current group window's (else the last one's) OnShowInviteDialog.</summary>
    public string OpenGroupInviteDialog()
    {
        var windows = groupBrowser?.Windows.ToList();
        var w = windows?.FirstOrDefault(x => x.Name == ChatLog.GetCurrentName()) ?? windows?.LastOrDefault();
        if (w?.Host == null) return "no group window";
        w.OnCallback("OnShowInviteDialog", System.Array.Empty<object>());
        return GroupInviteOpen ? $"open for chat {groupInvite.GroupId}" : "not open";
    }

    /// <summary>The scripted live run's invite: the name typed into the open invite dialog, then Enter (its vfunc_13).</summary>
    public string InviteFromGroupDialog(string name)
    {
        if (!GroupInviteOpen || groupInvite.Host?.Entity == null) return "no invite dialog";
        var h = groupInvite.Host;
        h.Set(h.Child("m_textInput"), "text", name);
        return groupInvite.OnKey(true) ? "sent" : "keys not taken";
    }

    /// <summary>
    /// The scripted live run's group chat line: in the current group window (else the last one), shown first from its browser
    /// row when minimized (an invited chat's window opens minimized; the row's OnToggleDialog), Return to focus the input
    /// when it is not, the text typed, Return to send. False with no loaded group window.
    /// </summary>
    public bool SubmitGroupLine(string text)
    {
        var windows = groupBrowser?.Windows.ToList();
        var w = windows?.FirstOrDefault(x => x.Name == ChatLog.GetCurrentName()) ?? windows?.LastOrDefault();
        if (w?.Host == null) return false;
        if (w.Minimized) groupBrowser.OnCallback("OnToggleDialog", new object[] { w.Name });
        ChatLog.MakeCurrent(w.Name);
        if (!w.InputActive) w.OnKey(Ms2.Ui.Hud.MessengerController.DikReturn, false);
        var h = w.Host;
        h.Set(h.Get(h.Child("m_textInput"), "textField"), "text", text);
        return w.OnKey(Ms2.Ui.Hud.MessengerController.DikReturn, false);
    }

    /// <summary>Return in the first chat window, unfocused: after a whisper the input starts the next one to its target.</summary>
    public void PressChatReturn()
    {
        if (messengers.Count > 0) messengers[0].Side.OnKey(Ms2.Ui.Hud.MessengerController.DikReturn, false);
    }

    /// <summary>
    /// Step 2: the movie's notifySlotShiftClick on the first slot of the shown tab holding an item (InvenSlot::vfunc_15 links
    /// it into the chat input), then Return in the first chat window; the linked item's uid, 0 when nothing was linked.
    /// </summary>
    public long ShiftClickFirstItemAndSend()
    {
        var view = Ms2Godot.Net.LiveSession.Instance?.Inventory;
        if (view == null || inventory == null || inventory.IsClosed || messengers.Count == 0) return 0;
        var cells = view.Cells(view.Tab);
        for (int i = 0; i < cells.Count; i++)
        {
            if (cells[i].Empty) continue;
            inventory.OnCallback("notifySlotShiftClick", new object[] { Ms2.Ui.Hud.InventoryController.SlotName(i) });
            var side = messengers[0].Side;
            if (side.InputLinks.Count == 0) return 0;
            GD.Print($"live: chat input links {side.InputLinks[0]}");
            side.OnKey(Ms2.Ui.Hud.MessengerController.DikReturn, false);
            return cells[i].Uid;
        }
        return 0;
    }

    static Ms2.Ui.Hud.AvatarRotateLimit avatarLimit;   // UIAvatarPC::vfunc_1's constants, read once

    /// <summary>
    /// CKeyTable::IsActionKeyDown (keytable.cpp:1037): any key bound to the action is held. The bound keys are DirectInput codes
    /// (<see cref="GodotKeyOfDik"/>). MS2InputService::IsKeyDownGated's combo-key and key-group test is not modelled. Like the
    /// client's it does not test the text-field mask (a caller that must yield goes through Unk_PCMove::FUN_141683ef0, here
    /// InputBlock.Active). No caller at present.
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
        // HandleRequestTradeExButton -> FUN_14185b8e0 (the answer); HandleTradeInButton -> the inventory's count check and add
        Action<long, bool> tradeAnswer = (id, yes) => Trades?.SendAnswer(id, yes);
        Action<long, int> tradeIn = (uid, n) => inventory?.TradeIn(uid, n);
        Func<long, int, bool> sellValid = (uid, n) => inventory?.IsSellCountValid(uid, n) ?? true;
        Action<long, int> sellItem = (uid, n) => inventory?.SellCountStep(uid, n);
        Action<long, int> sellConfirm = (uid, n) => inventory?.SellConfirmStep(uid, n);
        boxes.RequestTradeAnswer = tradeAnswer; boxes.TradeIn = tradeIn;
        Action<long, int> storeIn = (uid, n) => store?.UseStoreIn(uid, n), storeOut = (uid, n) => store?.UseStoreOut(uid, n);
        Action<long, int, int> swapIn = (uid, slot, n) => store?.SwapStoreIn(uid, slot, n);
        boxes.UseStoreIn = storeIn; boxes.UseStoreOut = storeOut; boxes.SwapStoreIn = swapIn;
        boxes.SellCountValid = sellValid; boxes.SellItem = sellItem; boxes.SellConfirm = sellConfirm;
        boundBoxes = boxes;
        unbindBoxAnswers = () =>
        {
            if (boxes.UseBindFuncItem == use) boxes.UseBindFuncItem = null;
            if (boxes.UseBindEquipItem == equip) boxes.UseBindEquipItem = null;
            if (boxes.UseBindGemEquipItem == gem) boxes.UseBindGemEquipItem = null;
            if (boxes.RequestTradeAnswer == tradeAnswer) boxes.RequestTradeAnswer = null;
            if (boxes.TradeIn == tradeIn) boxes.TradeIn = null;
            if (boxes.UseStoreIn == storeIn) boxes.UseStoreIn = null;
            if (boxes.UseStoreOut == storeOut) boxes.UseStoreOut = null;
            if (boxes.SwapStoreIn == swapIn) boxes.SwapStoreIn = null;
            if (boxes.SellCountValid == sellValid) boxes.SellCountValid = null;
            if (boxes.SellItem == sellItem) boxes.SellItem = null;
            if (boxes.SellConfirm == sellConfirm) boxes.SellConfirm = null;
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
        if (GuideRefusesOpen("InventoryDialog")) return;
        if (inventoryMovie != null) { inventoryMovie.Host.Unload(); inventoryMovie.QueueFree(); inventoryMovie = null; }
        var live = Ms2Godot.Net.LiveSession.Instance;
        var view = live != null ? live.Inventory : OfflineInventory();
        if (view == null) { GD.Print("inventory: the live session has no bag yet"); return; }
        // the hover compares with the own character's equipment (MyPC); Shift is CUIService m_n128 (either Shift key)
        var me = live != null ? live.Me : OfflineMe();
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
            SlotWorld = QuickSlotWorld,
            LinkItem = ChatLog.LinkItem,                  // InvenSlot::vfunc_15: Shift+click links the item into the chat input
        };
        c.ToggleDialog += id =>
        {
            if (id == ItemLockDialogController.UiId) ToggleItemLock();
            else if (id == ItemBreakDialogController.UiId) ToggleItemBreak(false);
            else if (id == ItemMergeOptionDialogController.UiId) ToggleItemMerge();
            else if (id == ItemEnchantDialogController.UiId) OpenItemEnchant();
            else GD.Print($"inventory: dialog 0x{id:x} is not in this HUD");
        };
        c.Button += name => GD.Print($"inventory: {name} is not sent (display only)");
        c.SystemNotice += SystemNotice;
        // the trade path (InvenSlot::TryAddToTrade): the open trade dialog heads the right-click dialogs (DAT_143af2258)
        c.Trade = () => TradeOpen ? trade.Book : null;
        c.Shop = () => ShopOpen ? shop : null;
        // the client's head is the latest opened dialog (DAT_143af2258.GetHead(), invenslot.cpp:1961), no fixed order; the dialogs
        // here are not told apart by open time, so the check order below is ours
        c.DialogHead = () => TradeOpen ? Ms2.Ui.TradeBook.DialogId : ShopOpen ? Ms2.Ui.Shop.ShopController.UiId : StoreOpen ? Ms2.Ui.Hud.StoreDialogController.UiId : ItemLockOpen ? Ms2.Ui.Hud.ItemLockDialogController.UiId : ItemBreakOpen ? Ms2.Ui.Hud.ItemBreakDialogController.UiId : ItemEnchantOpen ? Ms2.Ui.Hud.ItemEnchantDialogController.UiId : ItemRevertOpen ? Ms2.Ui.Hud.ItemMergeRevertDialogController.UiId : 0;
        c.BankAllowed = item => store?.Storable(item) ?? true;       // the open bank heads the right-click dialogs (0x4e47)
        c.BankDeposit = item => store?.DepositFromInventory(item);
        c.LockSelect = item => itemLock?.SelectFromInventory(item);
        c.RevertCheck = item => itemRevert?.CheckItem(item) ?? 0;
        c.RevertSelect = item => itemRevert?.UseFromInventory(item);
        c.RevertOpen = OpenItemRevert;
        c.UseItemSkill = CastItemSkill;
        c.OpenSelectBox = OpenSelectItemBox;
        c.SelectBoxOpen = () => SelectItemBoxOpen;
        c.OpenMultiBox = OpenItemMultiOpen;
        c.LockSlotDropped = (uid, mine) => itemLock?.DroppedOnInventory(uid, mine);
        c.LockOrBreakDialogExists = () => InventoryController.LockOrBreakExists(itemLock, itemBreak);   // the controllers stay set after a close
        c.LockDialogOpen = () => ItemLockOpen;                       // UpdateButtonState: pLock->vfunc_55() == 0
        c.BreakDialogOpen = () => ItemBreakOpen && itemBreak.Id == ItemBreakDialogController.UiId;
        c.DonationDialogOpen = () => ItemBreakOpen && itemBreak.Id == ItemBreakDialogController.DonationUiId;
        c.BreakCanBreak = item => itemBreak?.Mgr?.CanBreakItem(item) ?? false;
        c.BreakAdd = item => itemBreak?.AddFromInventory(item);
        c.BreakSlotDropped = (uid, mine) => itemBreak?.DroppedOnInventory(uid, mine);
        c.ItemSound += item => GD.Print($"inventory: item sound {item?.ItemId} (the material sounds are not ported)");
        c.NoticeText += SystemNoticeText;
        BindBoxAnswers();
        var m = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
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
        ownBuffs.OwnerIsMyPc = true;
        var buffs = ownBuffs;
        live.Cooltimes.GroupReduceRate = g => buffs.ReduceSkillGroupCooltime(g);   // FUN_1417664e0, skillslot.cpp:2279
        // skillslot.cpp:2290-2291: the skill stat options 0x3ea (count, flat) and 0x3e9 (rate) of the own buffs
        live.Cooltimes.FlatReduce = id => buffs.SkillInvokeOption(0x3ea, id, live.SkillRows?.Invoke(id)?.GroupIds ?? 0, out _);
        live.Cooltimes.RateReduce = id =>
        {
            buffs.SkillInvokeOption(0x3e9, id, live.SkillRows?.Invoke(id)?.GroupIds ?? 0, out var rate);
            return rate;
        };
        // the worn part kept like m_abilities (rebuilt when FUN_1411e76c0 finds the equipment dirty, equipment.cpp:2922):
        // on an equipment list or level change; DAT_143aea120 on each own put-on / put-off call (MyPC::vfunc_127 /
        // vfunc_128, mypc.cpp:4892, :4905); the cast sample is in Cast
        Action onEquip = RefreshOwnWorn;
        Action onMe = () => { if (live.Me.Equips != ownWornEquips || (live.Me.User?.Level ?? 0) != ownWornLevel) RefreshOwnWorn(); };
        Action onPutOnOrOff = SampleAbilityRate;
        live.Me.EquipChanged += onEquip;
        live.Me.Changed += onMe;
        live.Me.PutOnOrOff += onPutOnOrOff;
        RefreshOwnWorn();
        SampleAbilityRate();
        // the hit reaction's CEquipment::GetAbilityTotal(0x27) (FUN_1411e0f90, equipment.cpp:705) and the buffs' part of
        // AdditionalCliList::GetSuperArmor (additionalcli_2.cpp:599)
        live.OwnPushResistLevel = () => Ms2.Ui.Items.WornEquipment.AbilityTotal(ownWornPushResist, 0x27, buffs, live.HotTimes,
            (live.Me.User?.PcBangGrade ?? 0) > 0);
        live.OwnBuffSuperArmor = buffs.SuperArmor;
        // the mount's RunX speed (Ridee::GetRideSpeed, ridee.cpp:1021-1024): GetAbilityRate(0xae) or (0xa5) on each call
        live.OwnAbilityRate = type => Ms2.Ui.Items.WornEquipment.AbilityRate(Ms2.Ui.Items.WornEquipment.AbilityTotal(
            OwnWorn(type), type, buffs, live.HotTimes, (live.Me.User?.PcBangGrade ?? 0) > 0));
        live.OwnEventBuff = buffs.HasEnabledWithEventBuffType;   // the swimming mount's RunX (Ridee::FUN_1417075e0)
        Action<Ms2.Net.Packets.BuffPacket> onBuff = b =>
        {
            ownBuffs.UpperTargetId = live.UpperTargetId;
            if (b == null) ownBuffs.ClearAll();
            else ownBuffs.Apply(b);
        };
        foreach (var b in live.TakeBuffs()) onBuff(b);
        live.BuffReceived += onBuff;
        // PC::FUN_141661370's custom buff icon of the invincible time (pc.cpp:3388-3406); one started before the HUD shows now
        playerInfo.OwnBuffs = ownBuffs;
        playerInfo.LocalObjectId = () => live.SelfObjectId;
        Action<int, int, int> onInvincibleBuff = playerInfo.InvincibleBuff;
        if (live.Invincible.Active) playerInfo.InvincibleBuff(0, live.Invincible.TimerId, live.InvincibleBuffEnd);
        live.InvincibleBuffChanged += onInvincibleBuff;
        unwatchBuffs = () =>
        {
            live.BuffReceived -= onBuff;
            live.InvincibleBuffChanged -= onInvincibleBuff;
            live.Me.EquipChanged -= onEquip;
            live.Me.Changed -= onMe;
            live.Me.PutOnOrOff -= onPutOnOrOff;
            live.OwnPushResistLevel = () => 0;
            live.OwnBuffSuperArmor = _ => 0;
            live.OwnAbilityRate = _ => 0f;
            live.OwnEventBuff = _ => false;
        };
    }

    int ownWornCooldown, ownWornPushResist, ownWornRideSameSpeed, ownWornRideSpeed, ownWornLevel, ownWeaponMaterial;

    /// <summary>The kept worn part of the abilities <see cref="Ms2Godot.Net.LiveSession.OwnAbilityRate"/> reads, 0 for any other.</summary>
    int OwnWorn(int type) => type switch
    {
        Ms2Godot.Player.RunXSameSpeedAbility => ownWornRideSameSpeed,
        Ms2Godot.Player.RunXSpeedAbility => ownWornRideSpeed,
        _ => 0,
    };
    object ownWornEquips;

    /// <summary>
    /// The worn RH weapon's attackMaterial (the own magics' hit sounds) and the worn items' "skillCooldown" (0x23), push resistance (0x27) and ride speed (0xae, 0xa5) parts of m_abilities (CEquipment::FUN_1411e76c0, equipment.cpp:2922), taken
    /// again only when the equipment lists or the level change. STILL-OPEN: FUN_1411e76c0 also rebuilds when a worn item
    /// expires; an item expiring while worn keeps its part here until the next change.
    /// </summary>
    void RefreshOwnWorn()
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        if (live == null) return;
        long now = live.GameSession?.ServerTime ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var t = hudModel.Tables;
        ownWornEquips = live.Me.Equips;
        ownWornLevel = live.Me.User?.Level ?? 0;
        var info = live.Me.ToPcInfo(now);
        Func<Ms2.Ui.Items.Item, bool> canUse = it => Ms2.Ui.Items.ItemLevels.Usable(it, now);
        var worn = Ms2.Ui.Items.WornEquipment.Abilities(info, t.Items, t.ItemModels, canUse);
        ownWeaponMaterial = Ms2.Ui.Items.WornEquipment.Of(info, t.Items)?.WeaponMaterial(canUse) ?? 0;
        ownWornCooldown = worn != null && worn.TryGetValue(0x23, out int v) ? v : 0;
        ownWornPushResist = worn != null && worn.TryGetValue(0x27, out int r) ? r : 0;
        ownWornRideSameSpeed = worn != null && worn.TryGetValue(Ms2Godot.Player.RunXSameSpeedAbility, out int rs) ? rs : 0;
        ownWornRideSpeed = worn != null && worn.TryGetValue(Ms2Godot.Player.RunXSpeedAbility, out int rx) ? rx : 0;
    }

    /// <summary>
    /// DAT_143aea120 = CEquipment::GetAbilityRate(0x23) (skillslot.cpp:2350, mypc.cpp:4892, :4905): the own "skillCooldown"
    /// ability total over 1000 into the cooldowns' <see cref="Ms2.Ui.Skills.SkillCooltimes.AbilityRate"/>: the kept worn
    /// part, the own buffs and the hot time sum, with the PC bang events for a PC bang grade above 0 (PC::FUN_141658f30;
    /// the grade from the field entry: STILL-OPEN the SetPcBang packet, OnSetPcbang scenestategame.cpp:7951). No allocation.
    /// </summary>
    void SampleAbilityRate()
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        if (live == null) return;
        live.Cooltimes.AbilityRate = Ms2.Ui.Items.WornEquipment.AbilityRate(Ms2.Ui.Items.WornEquipment.AbilityTotal(
            ownWornCooldown, 0x23, ownBuffs, live.HotTimes, (live.Me.User?.PcBangGrade ?? 0) > 0));
    }
    Action unwatchQuickSlotState;
    Ms2.Ui.Skills.WeaponActionTable weaponActions;

    Action<IReadOnlyList<Ms2.Net.Packets.KeyBinding>> keysSentHandler;   // KeysSentToServer -> SendKeyTableSaveAll, on from the first table

    /// <summary>
    /// The live events that change what the quick slots show. The client queues a gray refresh on a bag item added or
    /// removed (UIMainPlayerInfo::vfunc_21 / vfunc_22, uimainplayerinfo.cpp:839), after UpdateSlotForItem rebinds the item's
    /// slot (:1357); here a bag change queues the gray state and item count of every slot. The skill book (the own
    /// FieldAddUser's, which arrives after the HUD is built, or a Job packet) and a key table packet rebind every slot; a key
    /// table mode 0 / 7 first reloads the slots' own entries from the page (OnShortkeyPageNotify).
    /// </summary>
    void WatchQuickSlotState()
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        if (live == null) return;
        Action<int> onBook = _ => playerInfo.RebindSlots();
        Action<long> onBag = _ => playerInfo.QueueSlotRefresh(Ms2.Ui.Hud.MainPlayerInfoController.RefreshGray | Ms2.Ui.Hud.MainPlayerInfoController.RefreshItemCount);
        // CKeyTable::OnKeyTable (keytable.cpp:285-375): mode 0 loads bindings + quick-slot pages; mode 7 updates pages.
        // Only mode 0 / 7's SetQuickSlotPage posts NotifyShortkeyPage, for a page in range (keytable.cpp:2547): the slots reload
        // their page and the page number follows; a mode 0 with useDefault 1 reads no pages and does not call it (:293-303)
        // Client OnKeyTable never calls UIGameOptionDialog::ReopenCurrentTab (its only caller is uikeysettingselectdialog.cpp:117).
        Action<int> onKeys = mode =>
        {
            hudModel.ServerKeys = live.KeyTable;
            hudModel.Keys.Locked = live.KeyTable.Locked;
            if (mode == 0) hudModel.Keys.Apply(live.KeyTable, PadDefaultsKind());
            if (mode == 0 || mode == 7) playerInfo.OnKeyTable();
            playerInfo.RebindSlots();
        };
        Action<Ms2.Net.Packets.KeyBinding> onBind = live.SendKeyTableBind;
        Action<int> onPageChanged = live.SendKeyTableSetPage;
        live.SkillBookChanged += onBook;
        live.Items.Changed += onBag;
        live.KeyTableChanged += onKeys;
        hudModel.Keys.KeyBindingChanged += onBind;
        playerInfo.QuickSlotPageChanged += onPageChanged;
        unwatchQuickSlotState = () =>
        {
            live.SkillBookChanged -= onBook;
            live.Items.Changed -= onBag;
            live.KeyTableChanged -= onKeys;
            hudModel.Keys.KeyBindingChanged -= onBind;
            hudModel.Keys.KeysSentToServer -= keysSentHandler;
            keysSentHandler = null;
            playerInfo.QuickSlotPageChanged -= onPageChanged;
        };
    }
    Ms2Godot.Net.ItemKinds slotKinds;
    Ms2.Ui.Skills.SlotField slotField;
    int? slotFieldId;

    /// <summary>
    /// The player state the HUD quick slots read (SkillSlot): the live session's bag, skill book and own hands, else the offline
    /// inventory and a new character's book of the HUD's job; the field data of the current map id (offline: the field of the
    /// HUD's xblock); the cooldowns the live session's (idle offline). The skill set has no feed yet and keeps its idle value; the buffs only feed the buff strip.
    /// </summary>
    ClientConstants slotConstants;

    /// <summary>The job system the quick slots and the cooldown table read, rebuilt when the book changes.</summary>
    Ms2.Ui.Skills.JobSystem EnsureSlotJobs()
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
        return slotJobs;
    }
    Ms2.Ui.Skills.SlotWorld QuickSlotWorld()
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        var tables = hudModel.Tables.Skills;
        EnsureSlotJobs();
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
                UseEPSkill = fd.UseEPSkill, UsePet = fd.UsePet, SkillUseDisableMask = fd.SkillUseDisableMask, PkMode = fd.PkMode,
            };
        }
        var p = hudModel.Player;
        // PC::GetWeaponAction: the action PC::UpdateWeaponAction (pc.cpp:2838) picks for the own hands, offline the fixture
        // character's. Its hands are the layer 5 item, else FUN_141655ee0(slot, 1), the layer 0 item for a PC with no clone;
        // the equips here are layer 0.
        // STILL-OPEN: no layer 5 hands, and the equips are FieldAddUser's (no ItemPutOn / ItemPutOff yet), so a weapon
        // change does not reach the slots.
        weaponActions ??= assets.Table("table/weaponaction.xml") is { } wa ? Ms2.Ui.Skills.WeaponActionTable.Load(wa) : Ms2.Ui.Skills.WeaponActionTable.Empty;
        var me = live != null ? live.Me : OfflineMe();
        Ms2.Net.Packets.EquipItem Hand(int slot) => me.Equips?.FirstOrDefault(e => (int)e.Slot == slot);
        var world = new Ms2.Ui.Skills.SlotWorld
        {
            Tables = tables, ItemData = hudModel.Tables.Items.Get, Jobs = slotJobs, HasPc = hudModel.HasLocalPlayer,
            Level = p.Level, Hp = p.HpCur, Sp = p.SpCur, Ep = p.EpCur, Meso = p.Mesos,
            Inventory = live != null ? live.Items.All : (OfflineInventory() != null ? offlineBook.All : Array.Empty<Ms2.Net.Packets.EquipItem>()), TabOf = slotKinds.TabOf,
            Field = slotField,
        };
        slotConstants ??= Ms2.Ui.FeatureElements.Constants(assets.Packs);
        world.SummonRideSkill = slotConstants.Int("SummonRideSkillID", world.SummonRideSkill);
        world.GlideItemSkill = slotConstants.Int("GlideItemSkillID", world.GlideItemSkill);
        world.SummonPetSkill = slotConstants.Int("SummonPetSkillID", world.SummonPetSkill);
        world.Weapon = weaponActions.HeldAction(Hand(5), Hand(4), hudModel.Tables.Items.Get, world.CanUse) is { } action
            ? (action.RightHand, action.LeftHand) : null;
        world.IsFeatureEnabled = hudModel.IsFeatureEnabled;
        world.RunningSkill = () => RunningOwnCast()?.Skill;
        world.HasEffect = (id, level, count, compare) => ownBuffs?.Has(id, level, count, compare) == true;
        SlotCooldowns(world);
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

    // ---- npc shop lives in HudScene.Shop.cs ----

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
        if (GuideRefusesOpen(hudModel.IsFeatureEnabled(0xca) ? "SkillBookTreeDialog_jobchange01" : "SkillBookTreeDialog")) return;
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
            TabPrice = () => (uint)Ms2.Ui.FeatureElements.Constants(assets.Packs).Int("SkillBookTreeAddTabFeeMerat", 1000),
            CanPayTab = price => hudModel.Player.Merats >= price,
            CheckEntryName = name => CommunityChecker().CheckAutoDistributeEntryName(name, out var msg) != 0 ? msg : null,
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
        c.AutoTable = Ms2.Ui.Skills.AutoDistributeTable.Load(assets.Packs);
        c.OpenAutoDistribute = presets => OpenSkillAutoDistribute(c, presets);
        c.InBattle = () => live?.SelfInBattle ?? false;
        c.Sound += key => FieldAudio.Instance?.PlayUi(key);
        c.ChatLine += (text, kind) => ChatLog.AddSystemLine(text, kind);
        c.SystemNotice += SystemNoticeText;
        if (live != null) c.Send = live.SendSkillBook;
        Action<Ms2.Net.Packets.SkillBookTreePacket> onTree = c.OnSkillBookTree;
        // the status events the open window listens to (UISkillBookTreeDialog vfunc_oa0_7 / 9 / 28): the local player's job
        // packet modes 0, 1, 2 and 7 close it, a level change and the skill points refresh the shown preview
        Action<int> onJob = mode =>
        {
            if (mode is 0 or 1 or 2 or 7) c.OnJobChanged();
            // OnJob modes 9, 10 and 11 (scenestategame.cpp:7513): the job system takes the book, the open window refreshes
            else if (mode is 9 or 10 or 11 && live?.SkillBook != null) { jobs.Read(live.SkillBook); c.RefreshSlotsIfShown(); }
        };
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
        var m = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
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
        // UISkillBookTreeDialog::OnLoadInit (uiskillbookautodistributedialog.cpp:538) tells a loaded reward notify it opened, vfunc_67 (:514) that it closed
        // The movie is not loaded for the book: the HUD keeps the state and hands it over when the first toast creates the controller.
        skillBookOpen = true;
        rewardNotify?.NotifySkillBookDialog(true);
        c.Unloaded += () => { skillBookOpen = false; rewardNotify?.NotifySkillBookDialog(false); };
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

    static readonly int PerfRepeat = int.TryParse(System.Environment.GetEnvironmentVariable("MS2_PERF_REPEAT"), out var r) && r >= 2 ? r : 2;
    static readonly bool PerfMem = !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("MS2_PERF_MEM"));
    static readonly int PerfMemHold = int.TryParse(System.Environment.GetEnvironmentVariable("MS2_PERF_MEM_HOLD"), out var h) ? h : 0;

    /// <summary>
    /// The end of a perf run (MS2_PERF_QUIT): exit 0, or 3 when a library with a precompiled image was bound from memory
    /// before the images went in (<see cref="Precompiled.Early"/>: its code ran under the JIT, so the run's numbers are not
    /// the game's).
    /// </summary>
    void PerfQuit()
    {
        if (Precompiled.Early.Count > 0)
        {
            PerfLog.Print($"FAIL precompiled code: bound from memory before the images: {string.Join(", ", Precompiled.Early)}");
            GetTree().Quit(3);
            return;
        }
        GetTree().Quit(0);
    }

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
            if (e == PerfMemHold + 1 && System.Environment.GetEnvironmentVariable("MS2_PERF_QUIT") != null) PerfQuit();
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
    /// whole call timed; MS2_PERF_REPEAT=N opens each N times ("open 3" ... after the warm one), for the JIT's tier-up curve.
    /// Then MS2_PERF_QUIT quits.
    /// </summary>
    void PerfScript()
    {
        perfFrame++;
        if (perfFrame == 0) { perfState = "idle"; PerfLog.Restart(); }
        if (PerfMem) { PerfMemScript(); return; }
        int at = perfFrame - 600, per = 200 * PerfRepeat;
        if (at >= PerfDialogs.Length * per) { PerfTail(at - PerfDialogs.Length * per); return; }
        if (at < 0 || at % 100 != 0) return;
        int i = at / per, step = at / 100 % (2 * PerfRepeat);
        string d = PerfDialogs[i];
        long jit0 = System.Runtime.JitInfo.GetCompiledMethodCount();
        var jitTime0 = System.Runtime.JitInfo.GetCompilationTime();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool open = step % 2 == 0;
        PerfToggle(d, open);
        PerfLog.Print($"{(open ? (step == 0 ? "cold open" : step == 2 ? "warm open" : $"open {step / 2 + 1}") : "close")} {d}: call {sw.Elapsed.TotalMilliseconds:0.0} ms, " +
                      $"jit {System.Runtime.JitInfo.GetCompiledMethodCount() - jit0} methods {(System.Runtime.JitInfo.GetCompilationTime() - jitTime0).TotalMilliseconds:0.0} ms");
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
        if (t == 1300 && System.Environment.GetEnvironmentVariable("MS2_PERF_QUIT") != null) PerfQuit();
    }

    public override void _Process(double delta)
    {
        PerfLog.Frame(perfState);
        rewardNotify?.Update();                               // UIRewardNotify::vfunc_15: the held toasts
        TickOwnQueued();                                      // StateSkill::FUN_1417f11d0: the queued press
        TickOwnAttacks();                                     // StateSkill::TextKeyEvent: the own cast's attack points
        TickOwnSync();                                        // StateSkill::SendChargeState: the mode 2 sync
        TickOwnTagMagics();                                   // CMagic::FUN_14145de80: the own magics' tag areas
        TickCooltimes();                                      // CSkillSystem::Update: the cooldowns
        TickCompact();                                        // CSkillSystem::Update's compact lists, State::vfunc_8's pick
        ChatLog.Process();                                    // UIMessengerBrowser::vfunc_15
        foreach (var (_, c) in messengers) c.Process();      // UIMessenger::vfunc_15
        groupBrowser?.Process();                              // UIGroupMessengerBrowser::vfunc_15
        if (ChatEmoticonOpen) chatEmoticon.Process();          // UIChatEmoticonDialog::vfunc_15
        if (ItemMultiOpenOpen) itemMultiOpen.Tick();                          // UIItemMultiOpenBoxDialog::vfunc_15
        if (itemBreak is { IsClosed: false }) itemBreak.Tick();               // UIItemBreakDialog::vfunc_15
        TickPartySearch();                                                   // UIPartySearchDialog::vfunc_15
        TickPartyDps();                                                      // UIPartyDPSDialog::vfunc_72
        if (inventory != null && !inventory.IsClosed) inventory.Process();     // UIInventoryDialog::vfunc_15
        if (myInfo != null && !myInfo.IsClosed) myInfo.Process((long)Time.GetTicksMsec());     // UIMyInfoDialog::vfunc_15
        (notice?.Host.Controller as NoticeDialogController)?.Update((uint)Time.GetTicksMsec());     // UINoticeDialog::vfunc_72
        smartPushManager?.Update((uint)Time.GetTicksMsec());     // CSmartPushManager::UpdatePushes (mypc.cpp:2979)
        popupMenu?.Update();       // UIPopUpMenu::vfunc_15
        optionDialog?.Update();    // UIGameOptionDialog::vfunc_15 (@0x141942bf0)
        if (PartyWindowOpen) party.Process();      // UIPartyDialog::vfunc_15: the vote page's time, the ready marks' deadline
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
        if (worldLoadPending) WorldLoadComplete();
        if (minimap == null && minimapSlot != null && GetTree().GetFirstNodeInGroup(World.FieldContents.Group) is World.FieldContents { Portals: not null } mfc) OpenMinimap(mfc);
        if (!questNavigationOpened && questNavigationSlot != null && GetTree().GetFirstNodeInGroup(World.FieldContents.Group) is World.FieldContents { Portals: not null } qfc) OpenQuestNavigation(qfc);
        QuestNavigationTick();               // CQuestNavigator FUN_140c99ee0, then UIQuestNavigation::vfunc_15
        minimapSide?.Process(Time.GetTicksMsec());
        upperHudSide?.Process(Time.GetTicksMsec());
        TargetBar?.Process();
        SystemText?.Process();               // UISystemText::vfunc_15: the timed lines' expiry
        mastery?.Process(Godot.Time.GetTicksMsec());
        CastingBar?.Process();               // UICastingBar::vfunc_15: an expired cast finishes
        GuideEvent?.Process();                // UIGuideEvent::vfunc_15: the arrows, the summary's expiry
        worldMapSide?.Process();             // UIWorldMapDialog::vfunc_72: FlushQueuedTiles
        if (AchieveOpen) achieve.Process();  // UIAchieveDialog::vfunc_15: the trophies a packet named
        StoreTick();                         // UIStoreDialog::vfunc_15
        MailSendTick();
        MailTick();                          // FUN_1408e48f0 and UIMailListDialog::vfunc_15
        RevivalTick();                       // UIRevivalDialog::vfunc_72 and its open on the own death
        BlackMarketTick();                   // UIBlackMarketDialog::vfunc_15
        ShopTick();                          // UIShopDialog::vfunc_72
        learningQuest?.Update();             // UILearningQuestDialog::vfunc_72: the new learning quest after MarkDirty
        CommunityTick();                     // UICommunityDialog::vfunc_72
        characterAbility?.Process(Time.GetTicksMsec());     // UICharacterAbilityDialog::vfunc_72: the reset button's time
        if (PerfLog.On) PerfScript();
        if (GatePath != null) GateStep();
        if (shotDir == null) return;
        if (questShot != null && frames == 200) OpenQuestLog();
        if (questShot != null && frames == 230 && questShot.Length > 2) ClickQuest(questShot[2]);
        if (sysmenuShot >= 0 && frames == 200) Escape();
        if (worldMapShot >= 0 && frames == 200) ToggleWorldMap();
        if (openShot == "mailsend" && frames == 200) OpenMailSendShot();
        if (openShot == "chatemoticon" && frames == 200) OpenChatEmoticon();    // with --chatemoticon-fixture: every pack owned
        if (openShot == "inventory" && frames == 200) ToggleInventory();
        if (openShot is "selectitembox" or "multiopenbox" && frames == 200) OpenSelectItemBoxShot(openShot == "multiopenbox");
        if (openShot == "itempreview" && frames == 200) OpenItemPreview(Ms2.Ui.Items.Item.Create(hudModel.Tables.Items, 11300001, 1), 1);
        if (openShot == "myinfo" && frames == 200) ToggleMyInfo();
        if (openShot == "levelupguide" && frames == 200) ToggleLevelUpGuide();
        if (openShot == "learningquest" && frames == 200) OpenLearningQuest();
        if (openShot == "adventurelevel" && frames == 200) ToggleAdventureLevel();
        if (openShot == "characterability" && frames == 200) ToggleCharacterAbility();
        if (openShot == "mastery" && frames == 200) ToggleMastery();
        if (openShot == "fishingbook" && frames == 200) ToggleFishingBook();
        if (openShot == "tokenpocket" && frames == 200) ToggleTokenPocket();
        if (openShot == "currency" && frames == 200) CurrencyShot();
        if (openShot == "currency" && frames is 215 or 235)
        {
            DirAccess.MakeDirRecursiveAbsolute(shotDir);
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(shotDir, $"toast_{frames}.png"));
        }
        if (openShot != null && openShot.StartsWith("gamedictionary") && frames == 200)
        {
            // gamedictionary:<tab>:<text>: as a dictionary text link opens it (OpenTabWithSearch, the first row selected)
            var parts = openShot.Split(':', 3);
            if (parts.Length == 3)
            {
                OpenGameDictionary();
                gameDictionary?.OpenTabWithSearch(int.Parse(parts[1]), parts[2], true);
            }
            else ToggleGameDictionary();
        }
        if (openShot == "achieve" && frames == 200) ToggleAchieve();
        if (openShot == "mail" && frames == 200) ToggleMail();
        if (openShot == "community" && frames == 200) ToggleCommunity();
        if (openShot is "partysearch" or "partysearchregister" or "partysearchmenu" && frames == 200) OpenPartySearchShot();
        if (openShot == "partysearchmenu" && frames == 260) partySearch?.RightClickChiefShot(102);
        if (openShot == "partysearchregister" && frames == 230) OpenPartySearchRegister();
        if (openShot == "partydps" && frames == 200) OpenPartyDpsShot();
        if (openShot == "guild" && frames == 200) ToggleGuild();
        if (openShot == "itemenchant" && frames == 200) OpenItemEnchantShot();
        if (openShot == "itembreak" && frames == 200) OpenItemBreakShot();
        if (guideShot != null && frames == 200)
        {
            // --uigen-guide kind:textId: the pane shows the guide text as its summary, text tip or tutorial balloon
            var parts = guideShot.Split(':');
            int textId = parts.Length > 1 ? int.Parse(parts[1]) : 0;
            switch (parts[0])
            {
                case "summary": GuideEvent?.ShowSummary(1, textId, 20000); break;
                case "tip": GuideEvent?.QueueOrShowTextTip(1, textId); break;
                case "tutorial": GuideEvent?.ShowTutorial(1, textId, true); break;
            }
        }
        if (arrowShot != 0 && frames == 200)
        {
            // --uigen-arrow: the tables' own uiArrowDialog rows — the game menu's inventory button and character info
            // one (guide_tutorial_100.xml events 170 and 200) and the menu group (guide_mapleguide.xml event 60540);
            // the equip lesson's weapon in the open inventory (event 210's keyboard row) and the skill lesson's
            // quick slot (guide_tutorial_10012000.xml event 10012052's row)
            Ms2.Ui.Guide.GuideDialogArrow a = arrowShot switch
            {
                170 => new(170, new(-80, 23, 0), 2, 1, 2, 0, "GameMenuDialog", -1, new[] { "m_menus", "inven", "m_button" }, Array.Empty<string>(), "", 0),
                200 => new(200, new(-80, 23, 0), 1, 1, 3, 0, "GameMenuDialog", -1, new[] { "m_menus", "characterinfo", "m_button" }, Array.Empty<string>(), "", 0),
                60540 => new(60540, new(-20, 0, 0), 2, 1, 5, 0, "GameMenuDialog", -1, new[] { "m_menus", "charactergroup" }, Array.Empty<string>(), "", 0),
                210 => new(214, new(110, 50, 0), 0, 1, 3191, 0x4e2d, "InventoryDialog", -1, Array.Empty<string>(),
                    new[] { "InventoryDialog", "MyInfoDialog" }, "inventoryItemID", 15000224),
                10012052 => new(10012052, new(-4, 0, 0), 2, 2, 10012053, 0, "MainPlayerInfo", -1, Array.Empty<string>(),
                    Array.Empty<string>(), "myInfoSkillID", 10900031),
                _ => null,
            };
            if (a != null)
            {
                GuideEvent?.AddArrow(a);
                // the tutorial's scene for the two buttons: the pointer over the character group's button opens its
                // dropdown, so the moved entry's place resolves (the mouse-over lesson of event 60540's text)
                if (arrowShot is 170 or 200) HoverGroupDropdown();
                // the equip lesson's scene: the character's bag holds the starter weapon and the inventory is open
                if (arrowShot == 210)
                {
                    var kinds = new Ms2Godot.Net.ItemKinds(assets.Packs);
                    OfflineInventory();
                    offlineBook.Apply(new Ms2.Net.Packets.ItemMessage.LoadTab(kinds.TabOf(15000224),
                        new[] { new Ms2.Net.Packets.EquipItem { Id = 15000224, Uid = 5001, InventorySlot = 1, Amount = 1 } }));
                    OpenInventory();
                }
                // the skill lesson's scene: the lesson's skill sits in the current quick slot page's first slot
                if (arrowShot == 10012052)
                {
                    var keys = new Ms2.Net.Packets.ServerKeyTable();
                    keys.Apply(new Maple2.PacketLib.Tools.ByteReader(Ms2.Net.Packets.ServerKeyTable.Write(0, true, null, 0,
                        new[] { new[] { (0, new Ms2.Net.Packets.QuickSlotEntry(10900031, 0, 0)) } })));
                    hudModel.ServerKeys = keys;
                    playerInfo.OnShortkeyPageNotify(-1, keys.CurrentPage);
                    playerInfo.RebindSlots();
                }
            }
        }
        if (skillShot != null && frames == 200)
        {
            // the shot's character: sub job and level (the capture's Lv 1 Archer is 500,1)
            hudModel.Player.SubJob = skillShot[0];
            if (skillShot.Length > 1) hudModel.Player.Level = skillShot[1];
            ToggleSkillBook();
        }
        if (skillCompactShot != null && frames == 200)
        {
            hudModel.Player.SubJob = skillCompactShot[0];
            if (skillCompactShot.Length > 1) hudModel.Player.Level = skillCompactShot[1];
            ToggleSkillCompact();
        }
        if (shopShot != null && frames == 200)
        {
            hudModel.Player.Mesos = 1_001_000;          // the capture's purse
            OpenInventory();
            OpenShop();
        }
        if (shopShot != null && frames == 210) shop?.PlaceBesideInventory(inventory, inventory != null && !inventory.IsClosed);
        if (shopShot != null && frames == 230) PickShopView(shopShot);
        if (worldMapShot is 1 or 2 && frames == 240) worldMapSide?.SetPendingSearch("Henesys");
        if (worldMapShot == 2 && frames == 260) worldMapSide?.OnCallback("onClickSearchInfo", new object[] { 2000076u });
        if (worldMapShot is 3 or 4 && frames >= 200) MapTipCycles(frames - 200, worldMapShot == 4);
        if (windowShot != null)
            foreach (var w in windowShot)
                if (frames == w[3]) global::Ms2Godot.ScreenMode.ApplyRequest(new Ms2.Formats.Options.WindowRequest(w[0] == 1, false, w[1], w[2]));
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
        if (damageShot && frames is 282 or 286 or 290) ShotDamage(frames);
        if (damageShot && frames is 284 or 288 or 292)
        {
            DirAccess.MakeDirRecursiveAbsolute(shotDir);
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(shotDir, $"damage_{frames}.png"));
            GD.Print($"damage shot {frames}: {damageText?.Count} numbers");
        }
        if (frameCase != null) { MeasureFrame(delta); return; }
        if (++frames == (worldMapShot is 3 or 4 ? 660 : 300))
        {
            DirAccess.MakeDirRecursiveAbsolute(shotDir);
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(shotDir, "hud.png"));
            GetTree().Quit();
        }
    }

    /// <summary>The damage text shot's numbers on the own player: a hit and a critical, exp, meso, then quest exp (queued)
    /// and the first type 3 (quest) item of the 3000000xx ids with its icon.</summary>
    void ShotDamage(int frame)
    {
        if (damageText == null) return;
        if (frame == 282) { damageText.OnPlayer(-1234, 0); damageText.OnExp(5280, 0); damageText.OnMeso(null, 750); }
        if (frame == 286) { damageText.OnPlayer(-56789, 1); damageText.OnExp(320, 0xc20); damageText.OnExp(40, 0xc21); }
        if (frame == 290)
        {
            var kinds = new Ms2Godot.Net.ItemKinds(assets.Packs);
            for (int id = 30000001; id < 30000400; id++)
                if (kinds.TypeOf(id) == 3) { GD.Print($"damage shot item {id} {kinds.IconPath(id)}"); damageText.OnItem(null, 3, kinds.IconPath(id)); break; }
        }
    }

    /// <summary>
    /// The arrow shots' tutorial scene: the pointer over the game menu's character group button, which opens the
    /// group's dropdown (the movie's own roll-over handler), so the arrow at the moved entry inside it resolves.
    /// </summary>
    void HoverGroupDropdown()
    {
        if (gameMenuMovie?.Host.Entity is not Ms2.Avm2.Flash.AsContainer root) return;
        var group = Descendant(gameMenuMovie.Host, root, "charactergroup") as Ms2.Avm2.Flash.AsContainer;
        var button = group?.Children.FirstOrDefault(k => k.InstanceName == "m_button");
        if (button == null) { GD.PushWarning("arrow shot: no character group button"); return; }
        var b = group.GetBounds(null);
        for (double y = b.Y; y < b.Y + b.H; y += 2)
            for (double x = b.X; x < b.X + b.W; x += 2)
                for (var t = gameMenuMovie.Player.MouseTarget(x, y); t != null; t = t.Parent)
                    if (t == button) { gameMenuMovie.Player.MouseMove(x + 1, y + 1); return; }
        GD.PushWarning("arrow shot: no hit on the group button");
    }

    /// <summary>The first display object named <paramref name="name"/> under <paramref name="o"/> (breadth first).</summary>
    static Ms2.Avm2.Flash.AsDisplayObject Descendant(UiHost h, Ms2.Avm2.Flash.AsDisplayObject o, string name)    {
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
        if (parts[1].StartsWith('@'))       // "@x,y": the pointer at that window point (in the movie's stage units)
        {
            var movie = FindMovies(GetTree().Root).FirstOrDefault(m => m.Host.Id == parts[0]);
            if (movie == null) { GD.PushWarning("hover: no movie " + parts[0]); return; }
            var xy = parts[1][1..].Split(',').Select(float.Parse).ToArray();
            var view = movie.GetChildren().OfType<As3View>().FirstOrDefault();
            var p = view != null ? view.GetGlobalTransformWithCanvas().AffineInverse() * new Vector2(xy[0], xy[1]) : new Vector2(xy[0], xy[1]);
            movie.Player.MouseMove(p.X, p.Y);
            return;
        }
        if (!TryHover(parts[0], parts[1])) GD.PushWarning("hover: no hit on " + spec);
    }

    /// <summary>The named descendant of the hosted movie with that id, the pointer at the first point of its art the movie's
    /// hit test reaches; false when there is none (a shot can try several).</summary>
    bool TryHover(string movieId, string path)
    {
        var movie = FindMovies(GetTree().Root).FirstOrDefault(m => m.Host.Id == movieId);
        if (movie == null) return false;
        var h = movie.Host;
        object o = h.Entity;
        foreach (var n in path.Split('.'))
            o = o is Ms2.Avm2.Flash.AsDisplayObject p ? Descendant(h, p, n) : null;
        if (o is not Ms2.Avm2.Flash.AsDisplayObject target) return false;
        var b = target.GetBounds(null);
        for (double y = b.Y; y < b.Y + b.H; y += 2)
            for (double x = b.X; x < b.X + b.W; x += 2)
                for (var t = (Ms2.Avm2.Flash.AsDisplayObject)movie.Player.MouseTarget(x, y); t != null; t = t.Parent)
                    if (t == target) { movie.Player.MouseMove(x + 4, y + 4); return true; }
        return false;
    }

    /// <summary>
    /// The --worldmap 3/4 shot: three open/hover/close cycles of the map — a tile's hitarea, then its air taxi icon, so
    /// their tooltips show — closed by the world map key (3: ToggleWorldMap, as M) or the dialog's close button (4: the
    /// movie's m_close, its OnClose), the map opening again between the cycles; the shot (at 660, over the field) is after
    /// the last close, where the closed map's tooltips must not stay on screen.
    /// </summary>
    void MapTipCycles(int at, bool byButton)
    {
        int cycle = at / 140, step = at % 140;
        if (cycle >= 3) return;
        if (step == 0 && at > 0 && !WorldMapOpen) ToggleWorldMap();     // the next cycle's open
        else if (step == 40) { if (!TryHover("uiworldmapdialog", "m_hitarea")) GD.PushWarning($"worldmap: no tile to hover in cycle {cycle}"); }
        else if (step == 55)
        {
            // the air taxi icon: the tile's found taxi, the good taxi, else the cash one
            bool taxi = TryHover("uiworldmapdialog", "m_taxiIcon");
            if (!taxi) taxi = TryHover("uiworldmapdialog", "m_goodtaxiIcon");
            if (!taxi) taxi = TryHover("uiworldmapdialog", "m_cashtaxiIcon");
            if (!taxi) GD.PushWarning($"worldmap: no air taxi icon to hover in cycle {cycle}");
        }
        else if (step == 85)
        {
            if (byButton)
            {
                var movie = FindMovies(GetTree().Root).FirstOrDefault(m => m.Host.Id == "uiworldmapdialog");
                if (movie?.Host.Entity is Ms2.Avm2.Flash.AsDisplayObject root && Descendant(movie.Host, root, "m_close") is { } btn)
                {
                    var b = btn.GetBounds(null);
                    movie.Player.Click(b.X + b.W / 2, b.Y + b.H / 2);
                }
                else GD.PushWarning($"worldmap: no close button to click in cycle {cycle}");
            }
            else if (WorldMapOpen) ToggleWorldMap();        // the M key's ToggleDialog(0x4e42)
        }
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
