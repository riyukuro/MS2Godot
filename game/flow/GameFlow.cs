using Godot;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2Godot.Ui;
using Ms2Godot.Ui.Contracts;
using Ms2Godot.Ui.Create;
using Ms2Godot.World;

namespace Ms2Godot.Flow;

/// <summary>
/// The client's scene states offline (CStateService: Logo, Login, Multi = lobby, Game, Loading, Move), as a small state machine
/// that lives beside the current scene and switches scenes:
///   Login (login.tscn) -> Lobby (charselect.tscn; an empty slot goes to Create, charcreate.tscn) -> Loading (the field's
///   loading screen over the scene change) -> Game (hud.tscn: the field, the player in the chosen character's look, the HUD).
/// In Game, Esc toggles the client's system menu (Ui.HudScene, uisystemmenudialog): character select goes back to Lobby, exit asks and quits.
/// The login accepts any id; the character list is the stub list. Args after `--`: --flow-shots DIR runs the whole
/// flow by script, with a shot at each step, and quits.
/// </summary>
public partial class GameFlow : Node
{
    public enum State { Login, Lobby, Create, Loading, Game }

    public const string LoginScene = "res://ui/login.tscn", LobbyScene = "res://ui/charselect.tscn",
        CreateScene = "res://ui/charcreate.tscn", GameScene = "res://ui/hud.tscn";

    static GameFlow instance;
    public State Current { get; private set; } = State.Login;
    int loadingFrames = -1;
    string shotDir;
    bool boxScript;

    public static void Start(SceneTree tree)
    {
        if (instance != null) return;
        instance = new GameFlow { Name = "GameFlow", ProcessMode = ProcessModeEnum.Always };
        tree.Root.CallDeferred(Node.MethodName.AddChild, instance);
    }

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--flow-shots") shotDir = args[i + 1];
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--flow-msgbox") { shotDir = args[i + 1]; boxScript = true; }
        GameOptions.Load();   // the client builds CGameOption (LoadLanguageOption, LoadGameOption) at startup, gameoption.cpp:1678-1685
        ScreenMode.ApplyAtStartup();   // then MS2RenderService::OnInit sizes the window from it (ms2renderservice.cpp:699-743)
        Ui.MessageBoxLayer.Ensure(this);   // CUIService::CreateAllDialogs registers the one message box layer (uiservice.cpp:2345)
        Ui.TooltipLayer.Ensure(this);      // GFxManager's init loads the one tooltip movie (LoadTooltipMovie, gfxmanager.cpp:135)
        Session.Reset();
        Session.GameScene = GameScene;
        UiBridge.ToHost += OnHost;
        CharacterCreateScene.CharacterCreated += OnCreated;
        CharacterCreateScene.Cancelled += OnCreateCancelled;


        // the system menu's entries that leave the field (Ui.HudScene, uisystemmenudialog): OnSelectCharacter sends RequestQuit, whose answer
        // returns to character select; offline that is the lobby at once (the request packet is not ported)
        Ui.HudScene.SystemMenuSelectCharacter += () => { if (Current == State.Game) Go(State.Lobby); };
        // OnGameExit: the HUD's message box 0x288 (s_msg_game_over, tag "gameexit"); its yes answer quits
        // (UIMessageBox::HandleGameexitButton: SendRequestQuitAndDisconnect is not ported, PostQuitMessage(0))
        Ui.MessageBoxLayer.GameExit += () => GetTree().Quit(0);

        Net.LiveSession.Attach(this);
        Go(State.Login);
        if (Net.LiveSession.Enabled) _ = Net.LiveSession.AutoDrive(this);
        if (shotDir != null) { DirAccess.MakeDirRecursiveAbsolute(shotDir); _ = boxScript ? BoxScript() : Script(); }
    }

    /// <summary>The client restores the desktop's display mode on WM_CLOSE and WM_DESTROY (windowservice.cpp:772-790).</summary>
    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest || what == NotificationPredelete) DisplayNative.RestoreDisplayMode();
    }

    public override void _ExitTree()
    {
        DisplayNative.RestoreDisplayMode();
        UiBridge.ToHost -= OnHost;
        CharacterCreateScene.CharacterCreated -= OnCreated;
        CharacterCreateScene.Cancelled -= OnCreateCancelled;
        Session.GameScene = null;
        if (instance == this) instance = null;
    }

    // ---- transitions -------------------------------------------------------------------------------------------

    public void Go(State next)
    {
        GD.Print($"flow: {Current} -> {next}");
        Current = next;
        switch (next)
        {
            case State.Login: Change(LoginScene); break;
            case State.Lobby:
                Ui.HudScene.ChatLog.Clear();                 // CSceneStateMulti::OnEnter -> FUN_141275500 (scenestatemulti.cpp:345)
                if (GetTree().CurrentScene is Ui.LoginScene login) EnterLobbyFrom(login);
                else Change(LobbyScene);
                break;
            case State.Create: CharacterCreateScene.Enter(GetTree()); break;
            case State.Loading: BeginLoading(); break;
        }
    }

    /// <summary>From the login screen: its background is kept for the select scene to crossfade from (CharacterListScene.EnterFrom).</summary>
    async void EnterLobbyFrom(Ui.LoginScene login)
    {
        var backdrop = await login.CaptureBackdrop();
        if (Current != State.Lobby) return;
        Ui.CharacterListScene.EnterFrom = backdrop;
        Change(LobbyScene);
    }

    void Change(string path)
    {
        GetTree().ChangeSceneToFile(path);
    }

    void OnHost(string movie, string method, object[] a)
    {
        if (Current == State.Login && movie == UILoginDialogContract.Movie && method == UILoginDialogContract.ToHost.OnConnect)
        {
            if (Net.LiveSession.Instance is { } live)
            {
                if (!live.Busy) LoginLive(live);
                return;
            }
            Session.AccountId = "player";           // stub login: any id and password are accepted
            Go(State.Lobby);
        }
        else if (Current == State.Lobby && movie == UICharacterListDialogContract.Movie)
        {
            if (method == UICharacterListDialogContract.ToHost.OnSelectCharacter) { }   // the character select scene keeps Session.SelectedIndex
            else if (method == UICharacterListDialogContract.ToHost.OnCreatePC) Go(State.Create);
            else if (method == UICharacterListDialogContract.ToHost.OnDeletePC)
            {
                // vfunc_28 "OnDeletePC": the confirmation box; its OK with the character's name sends the delete
                var list = Session.List();
                var victim = Session.SelectedIndex >= 0 && Session.SelectedIndex < list.Count ? list[Session.SelectedIndex] : null;
                if (victim == null) return;
                Ui.MessageBoxLayer.Ensure(this);
                var k = DeleteConstants();
                FrontEndBoxes.DeleteConfirm(victim, k.Level, k.Wait, new FrontEndBoxes.DeleteListener(victim.User.Name, () =>
                {
                    if (Net.LiveSession.Instance is { } live && !live.Busy) DeleteLive(live, victim);
                }));
            }
            else if (method == UICharacterListDialogContract.ToHost.OnGameStart && a.Length > 0)
            {
                var chosen = Session.List().FirstOrDefault(c => c.User.CharacterId.ToString() == (string)a[0]);
                if (chosen == null) return;
                Session.Chosen = chosen;
                if (Net.LiveSession.Instance is { } live)
                {
                    if (!live.Busy) EnterLive(live, chosen);
                    return;
                }
                Go(State.Loading);
            }
        }
    }

    void DeferredGo(int state) => Go((State)state);

    // The live requests are awaited, so the continuation runs on Godot's context and may touch the session and the scene tree.
    async void LoginLive(Net.LiveSession live)
    {
        if (await live.LoginAsync()) { Go(State.Lobby); return; }
        if (live.LastLoginRefusal is { } r)
        {
            Ui.MessageBoxLayer.Ensure(this);
            FrontEndBoxes.LoginRefused(r.State, r.Const1);
        }
    }

    (int Level, uint Wait)? deleteConstants;
    /// <summary>CharacterDestroyDivisionLevel and CharacterDestroyWaitSecond (constantsblocks.cpp:979-980, defaults 20 and
    /// 86400; table/constants.xml overrides them).</summary>
    (int Level, uint Wait) DeleteConstants()
    {
        if (deleteConstants is { } k) return k;
        try
        {
            using var packs = new PackSet(Settings.DataDir);
            var c = Ms2.Ui.FeatureElements.Constants(packs);
            k = ((int)c.Float("CharacterDestroyDivisionLevel", 20), (uint)c.Float("CharacterDestroyWaitSecond", 86400));
        }
        catch (Exception ex) { GD.PushWarning("flow constants: " + ex.Message); k = (20, 86400); }
        deleteConstants = k;
        return k;
    }

    async void DeleteLive(Net.LiveSession live, CharacterEntry victim)
    {
        var m = await live.DeleteAsync(victim);
        if (m is { Command: CharacterListCommand.DeleteEntry, Error: 0 }) Go(State.Lobby);
    }

    async void EnterLive(Net.LiveSession live, CharacterEntry chosen)
    {
        if (await live.EnterAsync(chosen)) Go(State.Loading);
    }

    /// <summary>OnCreateCharacterAnswer: a refusal shows the message its kind picks (the create screen stays).</summary>
    async void CreateLive(Net.LiveSession live, CreateCharacter request)
    {
        var e = await live.CreateAsync(request);
        if (e == null)
        {
            if (live.LastRefusal is { } r)
            {
                var text = Net.CreateRefusalText.For(r.Kind, r.Message, CommonStrings(), live.CharacterLimit > 0 ? live.CharacterLimit : 6);
                // the page answers first (failCreate, state 1, a name error into the name box); other refusals: a message box
                if (!CharacterCreateScene.CreateFailed(r.Kind, text)) ShowNotice(text);
            }
            return;
        }
        Ms2.Ui.Hud.MessageBoxQueue.Next();   // vfunc_79: the create answer closes the name box (FUN_14151f200)
        Session.SelectedIndex = Session.List().Count - 1;
        Go(State.Lobby);
    }

    Func<string, string> commonStrings;
    Func<string, string> CommonStrings()
    {
        if (commonStrings != null) return commonStrings;
        try { using var packs = new PackSet(Settings.DataDir); commonStrings = new GameStrings(packs, GameOptions.StringLanguage).Common(); }
        catch (Exception ex) { GD.PushWarning("flow strings: " + ex.Message); commonStrings = _ => null; }
        return commonStrings;
    }
    /// <summary>ShowMessageBox(text, 0, 0, "") @0x140d73aa0: a one-button notice in the client's message box layer.</summary>
    void ShowNotice(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        GD.Print("flow: notice: " + text);
        Ui.MessageBoxLayer.Ensure(this);
        Ms2.Ui.Hud.MessageBoxQueue.ShowMessageBox(text, 0, null, "");
    }

    void OnCreateCancelled()
    {
        if (Current == State.Create) Go(State.Lobby);
    }

    // The create screen only builds the request (nothing is sent); offline, the flow accepts it at once.
    void OnCreated(CreateCharacter request, byte[] bytes)
    {
        if (Current != State.Create) return;
        if (Net.LiveSession.Instance is { } live)
        {
            // UICharacterCreateDialog::OnCreate / FUN_1418e6430 return at once unless m_nState == 1; the request sets it to 3
            // until the answer (uicameraeditdialog.cpp:1719, 2123, 2141), so a second click while one is pending does nothing.
            if (!live.Busy) CreateLive(live, request);
            return;
        }
        int subJob1 = 0;
        try
        {
            using var packs = new PackSet(Settings.DataDir);
            subJob1 = new Ui.Create.CreateTables(packs).SubJob1(request.JobGroup);
        }
        catch (Exception ex) { GD.PushWarning("flow create: " + ex.Message); }
        var e = Session.Add(request, subJob1);
        Session.SelectedIndex = Session.List().IndexOf(e);
        Ms2.Ui.Hud.MessageBoxQueue.Next();   // as on the server's answer: the name box closes (vfunc_79, FUN_14151f200)
        Go(State.Lobby);
    }

    // ---- loading: the field's loading screen from the first frame of the scene change --------------------------------

    /// <summary>
    /// CNetworkService::OnLoginToGame (networkservice.cpp:907-914): the mode goes to 0x3ed (UILoadingProgress), DAT_143ab2874
    /// to 0, and WorldService::FUN_141bbc580 starts the loading screen at once (CUIService::FUN_141adb900 -> StartProgress,
    /// worldservice_1.cpp:10019, uiservice.cpp:1316-1332), still on the lobby scene. There the target map is DAT_143ab2874 = 0
    /// (UILoadingProgress::GetTargetMapId, uiloadingprogress.cpp:243-248), which has no picture (CollectImages skips every
    /// common row for it, loadingimagetable.cpp:283-297), so the screen shows the screenshot: the select scene without its UI.
    /// The field's own load carries that screen on (FieldContents.CoverLoad); the scene changes once it has been drawn.
    /// </summary>
    void BeginLoading()
    {
        int map = Session.Chosen.User.MapA;
        string xblock = null;
        string tip = "";
        try
        {
            using var packs = new PackSet(Settings.DataDir);
            xblock = FieldData.XBlockOf(packs, map);
            if (xblock == null || !packs.Exists(XBlock.Archive, $"xblock/{xblock.ToLowerInvariant()}.xblock")) { xblock = null; }
            tip = World.FieldContents.PickTip(packs);
        }
        catch (Exception ex) { GD.PushWarning("flow loading: " + ex.Message); }
        Travel.LoadingImage = null;
        Travel.LoadingTip = tip;
        // the field scene reads this: the chosen character's field (Tria when the data has no such map), no portal
        Travel.Pending = xblock == null ? null : new Travel.Move(map, xblock, 0, 0);
        Travel.Pending = Net.LiveSession.Decorate(Travel.Pending);
        GD.Print($"flow: loading map {map} ({xblock ?? "default field"}), screenshot of the select scene");
        // the screenshot is the scene without the UI (GetScreenshotBuffer(1), uiloadingprogress.cpp:304-325): the canvas
        // layers are hidden for the one frame captured
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
            if (Travel.Pending != null) World.FieldContents.CoverLoad(CoverPacks ??= new PackSet(Settings.DataDir), this, shot);
            loadingFrames = 0;                       // the cover draws for a few frames before the scene change blocks
        }
        RenderingServer.FramePostDraw += Captured;
    }

    /// <summary>The packs the flow's loading screen reads its movie from (kept: the screen outlives the call).</summary>
    static PackSet CoverPacks;

    public override void _Process(double delta)
    {
        if (loadingFrames < 0) return;
        loadingFrames++;
        if (loadingFrames == 4) GetTree().ChangeSceneToFile(GameScene);
        else if (loadingFrames > 4 && GetTree().CurrentScene?.SceneFilePath == GameScene)
        {
            // the game state (the ESC menu) starts with the built field
            if (Field.LoadComplete)
            {
                loadingFrames = -1;
                Current = State.Game;
                GD.Print("flow: Loading -> Game");
                Net.LiveSession.Instance?.FieldLoaded();
            }
        }
    }

    public override void _Input(InputEvent e)
    {
        if (Current == State.Game && e is InputEventKey { Pressed: true, Echo: false, Keycode: Godot.Key.Escape })
        {
            (GetTree().CurrentScene as Ui.HudScene)?.Escape();
            GetViewport().SetInputAsHandled();
        }
    }

    // ---- scripted proof run ----------------------------------------------------------------------------------------

    Ui.HudScene Hud => GetTree().CurrentScene as Ui.HudScene;
    async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    async Task Until(Func<bool> cond, int maxFrames = 3000) { for (int i = 0; i < maxFrames && !cond(); i++) await Frames(1); }
    void Shot(string name)
    {
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(shotDir, name));
        GD.Print("shot " + name);
    }
    void Press(Godot.Key k)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = false });
    }
    bool InScene(string path) => GetTree().CurrentScene?.SceneFilePath == path;
    Task LobbyReady() => Until(() => GetTree().CurrentScene is Ui.CharacterListScene { ListReady: true });
    /// <summary>A click on a row of the select scene's list (the list takes no keys: CharacterList.configUI preventKeyInput).</summary>
    void ClickRow(Func<Ui.CharacterListScene, int> row)
    {
        if (GetTree().CurrentScene is Ui.CharacterListScene s) s.SelectRow(row(s));
    }

    /// <summary>--flow-msgbox DIR: the front-end's message boxes, one shot each, then quit.</summary>
    async Task BoxScript()
    {
        var layer = Ui.MessageBoxLayer.Instance;
        await Until(() => InScene(LoginScene)); await Frames(90);
        FrontEndBoxes.LoginRefused(3, 0); await Frames(30);                      // LoginResult state 3: s_login_err_pwd
        Shot("mb_login_pwd.png");
        layer?.Click(0); await Frames(30);
        UiBridge.Raise(UILoginDialogContract.Movie, UILoginDialogContract.ToHost.OnConnect);
        await Until(() => Current == State.Lobby && InScene(LobbyScene)); await Frames(90); await LobbyReady();
        ClickRow(_ => 2); await Frames(5); await LobbyReady(); await Frames(30);              // a character under CharacterDestroyDivisionLevel
        UiBridge.Raise(UICharacterListDialogContract.Movie, UICharacterListDialogContract.ToHost.OnDeletePC); await Frames(30);
        Shot("mb_delete.png");
        layer?.Tick(0); await Frames(10);
        Shot("mb_delete_ticked.png");
        layer?.Click(0); await Frames(30);                                        // OK with no name: s_char_err_delete_name
        Shot("mb_delete_wrong_name.png");
        layer?.Click(0); await Frames(30);
        ClickRow(_ => Session.List().Count); await Frames(5); await LobbyReady();          // the empty slot
        Press(Godot.Key.Enter);
        await Until(() => Current == State.Create && InScene(CreateScene)); await Frames(120);
        Ui.Create.CharacterCreateScene.PickJobScripted();
        await Until(() => Ui.Create.CharacterCreateScene.OnCreatePage); await Frames(120);
        Ui.Create.CharacterCreateScene.OpenNameBoxScripted(); await Frames(30);
        Shot("mb_name.png");
        layer?.Click(0); await Frames(30);                                        // OK with no name: s_char_err_input under the input
        Shot("mb_name_empty.png");
        GetTree().Quit(0);
    }

    async Task Script()
    {
        await Until(() => InScene(LoginScene)); await Frames(90);
        Shot("01_login.png");
        UiBridge.Raise(UILoginDialogContract.Movie, UILoginDialogContract.ToHost.OnConnect);     // the Login button
        await Until(() => Current == State.Lobby && InScene(LobbyScene)); await Frames(90);
        Shot("02_charselect.png");
        await LobbyReady(); ClickRow(_ => 3); await Frames(5); await LobbyReady();                   // the empty slot (another job's lobby map reloads the scene)
        Shot("03_charselect_empty_slot.png");
        Press(Godot.Key.Enter);
        await Until(() => Current == State.Create && InScene(CreateScene)); await Frames(180);
        Shot("04_create.png");
        Ui.Create.CharacterCreateScene.ClickPrevScripted();                                           // the job page's back button
        await Until(() => Current == State.Lobby && InScene(LobbyScene)); await Frames(60);
        await LobbyReady();                                                                           // the reopened list selects the last previewed character (RefreshLobby m_1a0), which loads its map
        ClickRow(_ => 3); await Frames(5); await LobbyReady();
        Press(Godot.Key.Enter);                                                                       // the empty slot
        await Until(() => Current == State.Create && InScene(CreateScene)); await Frames(120);
        OnCreated(new CreateCharacter(0, 20, "Neutral", 0, 0, Array.Empty<byte>(), 0), Array.Empty<byte>());   // stands in for a confirmed name (the event is raised inside the create scene)
        await Until(() => Current == State.Lobby && InScene(LobbyScene)); await Frames(90);
        Shot("05_charselect_new_character.png");
        Press(Godot.Key.Enter);                                                                       // start with the new character
        await Until(() => World.FieldContents.CoverUp, 600); await Frames(10);
        Shot("06_loading.png");
        await Until(() => Current == State.Game); await Frames(240);
        Shot("07_field_hud.png");
        Press(Godot.Key.Escape); await Frames(30);
        Shot("08_esc_menu.png");
        Hud?.ClickSystemMenu("m_selectChannel"); await Frames(5);
        Shot("08b_esc_menu_channels.png");
        Hud?.ClickSystemMenu("m_channel"); await Frames(5);
        Shot("08c_esc_menu_channel_list.png");
        Hud?.ClickSystemMenu("m_selectCharacter");
        await Until(() => Current == State.Lobby && InScene(LobbyScene)); await Frames(90);
        Shot("09_back_at_charselect.png");
        await LobbyReady(); ClickRow(s => s.ListIndex - 1); await Frames(5); await LobbyReady(); Press(Godot.Key.Enter);
        await Until(() => Current == State.Game); await Frames(240);
        Shot("10_field_second_entry.png");
        Press(Godot.Key.Escape); await Frames(30);
        GD.Print("flow: quit from the menu");
        Hud?.ClickSystemMenu("m_exitGame"); await Frames(10);
        Shot("11_exit_confirm.png");
        Hud?.ClickMessageBox(0);
    }
}
