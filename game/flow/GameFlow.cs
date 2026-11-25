using Godot;
using Ms2.Audio;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Ui.Launch;
using Ms2Godot.Ui;
using Ms2Godot.Ui.Contracts;
using Ms2Godot.Ui.Create;
using Ms2Godot.World;

namespace Ms2Godot.Flow;

/// <summary>
/// The client's scene states offline (CStateService: Logo, Login, Multi = lobby, Game, Loading, Move), as a small state machine
/// that lives beside the current scene and switches scenes:
///   Launcher (launcher.tscn; MS2Godot's own page, before the client's Logo/Login states) -> Login (login.tscn) ->
///   Lobby (charselect.tscn; an empty slot goes to Create, charcreate.tscn) -> Loading (the field's
///   loading screen over the scene change) -> Game (hud.tscn: the field, the player in the chosen character's look, the HUD).
/// In Game, Esc toggles the client's system menu (Ui.HudScene, uisystemmenudialog): character select goes back to Lobby, exit asks and quits.
/// The login accepts any id; the character list is the stub list. Args after `--`: --flow-shots DIR runs the whole
/// flow by script, with a shot at each step, and quits.
/// </summary>
public partial class GameFlow : Node
{
    public enum State { Launcher, Login, Lobby, Create, Loading, Game }

    public const string LauncherScene = "res://ui/launcher.tscn", LoginScene = "res://ui/login.tscn",
        LobbyScene = "res://ui/charselect.tscn", CreateScene = "res://ui/charcreate.tscn", GameScene = "res://ui/hud.tscn";

    static GameFlow instance;
    public State Current { get; private set; } = State.Launcher;
    /// <summary>
    /// The login backdrop movie picked by LoginBgBgmTable::SelectCurrent when entering Login (WorldService::LoadMap(1),
    /// worldservice_1.cpp:3309-3313), shared with LoginScene so the backdrop and BGM come from the same row.
    /// </summary>
    public static string SelectedLoginBgDay { get; private set; }
    LoginBgBgmTable loginBgBgm;
    FieldAudio loginAudio;
    int loadingFrames = -1;
    string shotDir;
    bool boxScript;
    Action onSelectCharacter;
    Action onGameExit, onAlreadyReconnect;
    Action<string, bool> onExternalBlock;
    Action onGameToLogin, onListReturned;
    Action<int> onLoginLost;
    Action<long, string> onRename;
    Action onRenameCancelled;

    /// <summary>Returns the flow to the login scene (e.g. forced-quit notice, scenestategame.cpp:9268).</summary>
    public static void ReturnToLogin() => instance?.Go(State.Login);

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
        Ui.MouseCursorLayer.Ensure(this);  // and then the cursor movie (LoadMouseCursorMovie, gfxmanager.cpp:137)
        Session.Reset();
        Session.GameScene = GameScene;
        UiBridge.ToHost += OnHost;
        CharacterCreateScene.CharacterCreated += OnCreated;
        CharacterCreateScene.Cancelled += OnCreateCancelled;
        Ui.LauncherScene.SinglePlayer += OnLauncherSinglePlayer;
        Ui.LauncherScene.Connect += OnLauncherConnect;


        // Intercept OS window close so WM_CLOSE can show the confirm box outside Login (windowservice.cpp:772-784).
        GetTree().AutoAcceptQuit = false;

        // UISystemMenuDialog::OnSelectCharacter (@0x141b14600, uisystemmenudialog.cpp:461-464): sends RequestQuit(0)
        // when live (GameToLogin, then the login server's list, returns to Lobby); offline returns to Lobby immediately.
        onSelectCharacter = () =>
        {
            if (Current != State.Game) return;
            if (Net.LiveSession.Instance is { } live) live.SendRequestQuit();
            else Go(State.Lobby);
        };
        Ui.HudScene.SystemMenuSelectCharacter += onSelectCharacter;

        // UIMessageBox::HandleGameexitButton(0) (@0x1419fb480, uimessagebox.cpp:1225-1237):
        // SendRequestQuitAndDisconnect (RequestQuit(1) + close socket), restore display mode, then exit.
        onGameExit = QuitNow;
        Ui.MessageBoxLayer.GameExit += onGameExit;
        Ui.MessageBoxLayer.MaintenanceExit += onGameExit;   // HandleMaintenanceExitButton: the same quit (uimessagebox.cpp:1515-1519)
        // HandleAlreadyReconnectButton: ConnectToConfiguredServer, and the login server's RequestLogin gets the login again
        onAlreadyReconnect = () =>
        {
            if (Current == State.Login && Net.LiveSession.Instance is { Busy: false } live) LoginLive(live);
            else if (Current == State.Lobby) _ = LobbyLogin(true);   // from the server list's connect: the type 2 login again
        };
        Ui.MessageBoxLayer.AlreadyReconnect += onAlreadyReconnect;
        // HandleExternalBlockButton: ShellExecuteEx of the page; type 0 then quits as the maintenance exit does
        onExternalBlock = (url, quit) => { OS.ShellOpen(url); if (quit) QuitNow(); };
        Ui.MessageBoxLayer.ExternalBlock += onExternalBlock;
        // HandleChangeInvalidCharNameButton: the rename request (SendCharacterManagementMode5), or the cancel (FUN_1418f20d0)
        onRename = RenameLive;
        Ui.MessageBoxLayer.RenameInvalidName += onRename;
        onRenameCancelled = () => { if (GetTree().CurrentScene is CharacterListScene { Controller: { } dialog }) dialog.RenameCancelled(); };
        Ui.MessageBoxLayer.RenameInvalidNameCancelled += onRenameCancelled;

        Net.LiveSession.Attach(this);
        WireLive();
        // The launcher is MS2Godot's own pre-login page. A live address in the environment skips it, as do the test and
        // perf scenes and a scripted flow run, so those start exactly as they did before it existed.
        // --ip/--port with --ticket (the Lith launcher's own start) or MS2_LIVE_ACCOUNT start the live mode without the page
        string liveLogin = Net.LiveSession.CommandLineLogin;
        var start = LaunchRoute.Decide(GetTree().CurrentScene?.SceneFilePath, liveLogin, shotDir != null, null);
        // a launcher ticket with its address: the Nexon platform's passport start, the login scene connects with no dialog
        PassportStart = liveLogin != null && start.Mode == LaunchMode.Live && LaunchRoute.LoginStartFor(Net.LiveSession.LaunchArgs()) == LoginStart.Passport;
        if (PassportStart) GD.Print("flow: the launcher ticket's start: no login dialog, the login scene connects by itself");
        Go(start.Mode == LaunchMode.Launcher ? State.Launcher : State.Login);
        if (Net.LiveSession.Enabled) _ = Net.LiveSession.AutoDrive(this);
        if (shotDir != null) { DirAccess.MakeDirRecursiveAbsolute(shotDir); _ = boxScript ? BoxScript() : Script(); }
    }

    /// <summary>
    /// The live session's way back to character select, wired once a session is attached: at start (the command line's address)
    /// or later by the launcher's Connect (<see cref="OnLauncherConnect"/>). GameToLogin's OnMoveResult 0 switches to character
    /// select at once (LiveSession.ReturnAsync); the login server's list fills it in when it arrives. A session attached after
    /// start had neither, so the system menu's character select sent RequestQuit and the game stayed on screen.
    /// </summary>
    void WireLive()
    {
        if (onGameToLogin != null || Net.LiveSession.Instance is not { } liveSession) return;
        onGameToLogin = () => { if (Ms2.Ui.Launch.CharacterSelectReturn.Applies(Current == State.Game)) Go(State.Lobby); };
        liveSession.ReturnedToCharacterSelect += onGameToLogin;
        onListReturned = () => { if (Current == State.Lobby && GetTree().CurrentScene is CharacterListScene list) list.ReplaceList(); };
        liveSession.CharacterListReturned += onListReturned;
        onLoginLost = LoginConnectionLost;
        liveSession.LoginConnectionLost += onLoginLost;
    }

    /// <summary>
    /// Window message 0x465 outside the field (windowservice.cpp:454-545): the login connection closed with socket error
    /// <paramref name="code"/> on the login, character select or create scene. No reconnect starts there
    /// (ShouldReconnect needs the field scene), so the scene goes to the login scene (FUN_1417e25a0(0x6200009)) and the
    /// box shows the error's reason and its code (<see cref="Ms2.Ui.FrontEndConnection.Disconnected"/>); the next login
    /// connects again. The login scene is not built again when it is already up.
    /// </summary>
    void LoginConnectionLost(int code)
    {
        if (Current is not (State.Login or State.Lobby or State.Create)) return;
        string text = Ms2.Ui.FrontEndConnection.Disconnected(code, Ms2.Ui.Hud.MessageBoxData.Common);
        GD.Print($"flow: the login connection was lost (socket error {code}): to the login scene, box '{text}'");
        if (Current != State.Login) Go(State.Login);
        Ui.MessageBoxLayer.Ensure(this);
        Ms2.Ui.Hud.MessageBoxQueue.ShowMessageBox(text, 0, null, "");
    }

    static int SceneIdFor(State state) => state switch
    {
        State.Launcher => Ms2.Ui.Hud.QuitFlow.LauncherSceneId,
        State.Login => Ms2.Ui.Hud.QuitFlow.LoginSceneId,
        State.Lobby => Ms2.Ui.Hud.QuitFlow.LobbySceneId,
        State.Create => Ms2.Ui.Hud.QuitFlow.CreateSceneId,
        State.Loading => Ms2.Ui.Hud.QuitFlow.LoadingSceneId,
        State.Game => Ms2.Ui.Hud.QuitFlow.GameSceneId,
        _ => throw new System.ArgumentOutOfRangeException(nameof(state)),
    };

    bool quitting;

    async void QuitNow()
    {
        if (quitting) return;   // a second click during the wait changes nothing
        quitting = true;
        // the queued disconnect packet is written before the process ends, as the client's net thread does it; the frame loop keeps
        // running while it is awaited, and a stuck socket only holds the quit for a second
        if (Net.LiveSession.Instance?.SendRequestQuitAndDisconnect() is { } flushed)
            await System.Threading.Tasks.Task.WhenAny(flushed, System.Threading.Tasks.Task.Delay(1000));
        QuitWithoutSend();
    }

    /// <summary>WM_DESTROY (windowservice.cpp:786-791): restore the display mode and post the quit message; no packet.</summary>
    void QuitWithoutSend()
    {
        DisplayNative.RestoreDisplayMode();
        GetTree().Quit(0);
    }

    /// <summary>WindowService WM_CLOSE and WM_DESTROY (windowservice.cpp:772-791): restore display mode; outside Login
    /// show the "gameexit" confirm box instead of exiting immediately.</summary>
    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            Ms2.Ui.Hud.QuitFlow.HandleWindowClose(
                SceneIdFor(Current),
                CommonStrings(),
                Ui.MessageBoxLayer.Instance != null,
                DisplayNative.RestoreDisplayMode,
                QuitWithoutSend);
        }
        else if (what == NotificationPredelete)
        {
            DisplayNative.RestoreDisplayMode();
        }
        else if (what == NotificationApplicationFocusIn || what == NotificationApplicationFocusOut)
            SoundBuses.AppFocus(what == NotificationApplicationFocusIn);   // WM_ACTIVATEAPP's sound side (windowservice.cpp:801-831)
    }

    public override void _ExitTree()
    {
        StopLoginAudio();
        MusicController.ResetShared();   // WorldService::OnShutdown (worldservice_1.cpp:10583) -> CSoundEventMgr::ResetMusicState
        SelectedLoginBgDay = null;
        DisplayNative.RestoreDisplayMode();
        NifCache.Clear();   // CNifLoadingService::OnShutdown: ClearTextureCache, ClearNifCache (@0x141555e90)
        UiBridge.ToHost -= OnHost;
        CharacterCreateScene.CharacterCreated -= OnCreated;
        CharacterCreateScene.Cancelled -= OnCreateCancelled;
        Ui.LauncherScene.SinglePlayer -= OnLauncherSinglePlayer;
        Ui.LauncherScene.Connect -= OnLauncherConnect;
        Net.LiveSession.ClearServer();
        if (onSelectCharacter != null) { Ui.HudScene.SystemMenuSelectCharacter -= onSelectCharacter; onSelectCharacter = null; }
        if (onGameExit != null) { Ui.MessageBoxLayer.GameExit -= onGameExit; Ui.MessageBoxLayer.MaintenanceExit -= onGameExit; onGameExit = null; }
        if (onAlreadyReconnect != null) { Ui.MessageBoxLayer.AlreadyReconnect -= onAlreadyReconnect; onAlreadyReconnect = null; }
        if (onExternalBlock != null) { Ui.MessageBoxLayer.ExternalBlock -= onExternalBlock; onExternalBlock = null; }
        if (onRename != null) { Ui.MessageBoxLayer.RenameInvalidName -= onRename; onRename = null; }
        if (onRenameCancelled != null) { Ui.MessageBoxLayer.RenameInvalidNameCancelled -= onRenameCancelled; onRenameCancelled = null; }
        if (onGameToLogin != null && Net.LiveSession.Instance is { } liveSession) { liveSession.ReturnedToCharacterSelect -= onGameToLogin; onGameToLogin = null; }
        if (onListReturned != null && Net.LiveSession.Instance is { } liveSession2) { liveSession2.CharacterListReturned -= onListReturned; onListReturned = null; }
        if (onLoginLost != null && Net.LiveSession.Instance is { } liveSession3) { liveSession3.LoginConnectionLost -= onLoginLost; onLoginLost = null; }
        Session.GameScene = null;
        if (instance == this) instance = null;
    }

    // ---- transitions -------------------------------------------------------------------------------------------

    public void Go(State next)
    {
        GD.Print($"flow: {Current} -> {next}");
        if (Current == State.Game && next != State.Game) ModelBuilder.DropUi();   // logout or character select: the kept look is of this character
        if (next != State.Login) StopLoginAudio();
        Current = next;
        switch (next)
        {
            case State.Launcher: Change(LauncherScene); break;
            case State.Login:
                StartLoginBgBgm();
                Change(LoginScene);
                break;
            case State.Lobby:
                Ui.HudScene.ChatLog.Clear();                 // CSceneStateMulti::OnEnter -> FUN_141275500 (scenestatemulti.cpp:345)
                if (GetTree().CurrentScene is Ui.LoginScene login) EnterLobbyFrom(login);
                else Change(LobbyScene);
                break;
            case State.Create: CharacterCreateScene.Enter(GetTree(), int.TryParse(System.Environment.GetEnvironmentVariable("MS2_LIVE_CREATE_JOB"), out int scriptedJob) ? scriptedJob : 0); break;   // scripted runs: the job page opens on this job code
            case State.Loading: BeginLoading(); break;
        }
    }

    /// <summary>
    /// Selects the monthly LoginBgBgmTable row and starts the login screen BGM as the client does on CSceneStateLogin::OnEnter
    /// -> WorldService::LoadMap(1) (scenestatelogin.cpp:92, worldservice_1.cpp:3309-3313) and WorldService::FUN_141bc7b80
    /// (worldservice_1.cpp:4856-4872). Lobby/select maps (2..10) inherit Field and start their own FieldAudio.
    /// </summary>
    void StartLoginBgBgm()
    {
        var data = Settings.DataDir;
        if (string.IsNullOrEmpty(data)) return;
        try
        {
            var packs = PackSet.Shared(data);
            if (loginBgBgm == null && packs.Exists("Xml", "table/loginbgbgm.xml"))
                loginBgBgm = LoginBgBgmTable.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", "table/loginbgbgm.xml")));
            Func<int, int> pick = shotDir != null ? _ => 0 : n => (int)(GD.Randi() % (uint)n);
            if (loginBgBgm != null)
            {
                long serverTime = Net.LiveSession.Instance?.ServerTime ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                int month = LoginBgBgmTable.MonthForServerTime(serverTime, Ms2.Net.MsTime.ZoneSet, Ms2.Net.MsTime.CalcZoneOffset);
                loginBgBgm.SelectCurrent(month, pick);
                SelectedLoginBgDay = loginBgBgm.GetBgDay();
            }
            if (string.IsNullOrEmpty(SelectedLoginBgDay))
            {
                // No table value (GMS2 data has no loginbgbgm.xml): the login field's own xblock names the backdrop.
                var xblock = Ms2.Formats.World.FieldData.XBlockOf(packs, 1);
                if (xblock != null && packs.Exists(Ms2.Formats.World.MapXBlock.Archive, Ms2.Formats.World.MapXBlock.Entry(xblock)))
                    SelectedLoginBgDay = Ms2.Formats.World.MapXBlock.BgDay(Ms2.Formats.World.MapXBlock.Load(packs, xblock), pick);
            }
            if (loginAudio == null || !IsInstanceValid(loginAudio))
            {
                loginAudio = new FieldAudio { Name = "LoginAudio" };
                AddChild(loginAudio);
                if (!loginAudio.Open(data, packs))
                {
                    loginAudio.QueueFree();
                    loginAudio = null;
                    return;
                }
            }
            loginAudio.PlayLoginBgm(loginBgBgm, FieldAudio.FieldBgmById(packs, 1), pick);
        }
        catch (Exception ex) { GD.PushWarning("flow login bgm: " + ex.Message); }
    }

    void StopLoginAudio()
    {
        if (loginAudio == null) return;
        if (IsInstanceValid(loginAudio))
        {
            loginAudio.StopMusic();
            loginAudio.QueueFree();
        }
        loginAudio = null;
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

    /// <summary>The launcher's Single player: the offline flow, with no live session attached.</summary>
    void OnLauncherSinglePlayer()
    {
        if (Current != State.Launcher) return;
        Go(State.Login);
    }

    /// <summary>
    /// A launcher entry's Connect: the live mode against that login server. The login screen that follows is the
    /// client's own; its dialog's id and password go to <see cref="Net.LiveSession.LoginAsync"/>.
    /// </summary>
    void OnLauncherConnect(string host, int port)
    {
        if (Current != State.Launcher) return;
        Net.LiveSession.UseServer(host, port);
        Net.LiveSession.Attach(this);
        WireLive();
        Go(State.Login);
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
                if (GetTree().CurrentScene is not CharacterListScene { Controller: { } dialog }) return;
                Ui.MessageBoxLayer.Ensure(this);
                var k = DeleteConstants();
                FrontEndBoxes.DeleteConfirm(victim, k.Level, k.Wait, dialog);
            }
            else if (method == UICharacterListDialogContract.ToHost.OnGameStart && a.Length > 0)
            {
                var chosen = Session.List().FirstOrDefault(c => c.User.CharacterId.ToString() == (string)a[0]);
                if (chosen == null) { StartFailed(); return; }   // FUN_1418f1790: no character to enter, ShowPopup
                Session.Choose(chosen);
                if (Net.LiveSession.Instance is { } live)
                {
                    EnterLive(live, chosen);
                    return;
                }
                Go(State.Loading);
            }
        }
    }

    void DeferredGo(int state) => Go((State)state);

    // The live requests are awaited, so the continuation runs on Godot's context and may touch the session and the scene tree.
    /// <summary>
    /// The launcher ticket's start (<see cref="LaunchRoute.LoginStartFor"/>): the login scene opens no dialog and connects once
    /// its load is done; the ticket login then goes to the server list and, with the saved server and channel picked, to
    /// character select as an id and password login does.
    /// </summary>
    public static bool PassportStart { get; private set; }

    async void LoginLive(Net.LiveSession live)
    {
        // The dialog's own fields (UILoginDialog::SendLoginRequest reads m_inputID and m_inputPW, uilogindialog.cpp:204-246);
        // MS2_LIVE_ACCOUNT overrides them when the run set it.
        var login = GetTree().CurrentScene as Ui.LoginScene;
        if (await live.LoginAsync(login?.Controller.Id, login?.Controller.Password)) { Go(State.Lobby); return; }
        var scene = GetTree().CurrentScene as Ui.LoginScene;
        if (live.LastLoginRefusal is { } r)
        {
            // the refusal's box, then the dialog back up for the next try (CNetworkService::OnLoginResult ends every refusal on
            // the login scene with UISceneLogin::FUN_141acf470). Before, the dialog stayed popped down after the box and the
            // login could not be tried again.
            GD.Print($"login: refused, state {r.State}; the dialog shows its failure state");
            Ui.MessageBoxLayer.Ensure(this);
            // the passport start has no login dialog: only the scene's failConnect (FUN_141acf470, uiscenelogin.cpp:320-338)
            Ms2.Ui.LoginConnectFailure.AfterRefusal(() => FrontEndBoxes.LoginRefused(r),
                scene is { DialogOpen: true } ? scene.Controller.ShowConnectFailure : null, scene?.Controller != null ? scene.Scene.FailConnect : null);
        }
        else if (scene is { } shown && shown.Controller != null)
        {
            // A login that failed without a server answer. A failed connect is window message 0x467: box 0x20e with the
            // socket error, whose OK runs UISceneLogin::vfunc_64, the dialog's failure state (windowservice.cpp:606-621,
            // FUN_141902530). A connection lost after the connect with a socket error is the session's LoginConnectionLost
            // (0x465); any other failure goes to the failure state at once.
            // A ServerList without servers shows the same box with 0 and the same callback (CSceneStateLogin::OnPacket,
            // scenestatelogin.cpp:121-129).
            int code = live.LastConnectError;
            if (code != 0 || live.LastServerListEmpty)
            {
                string text = Ms2.Ui.FrontEndConnection.ConnectFailed(code, Ms2.Ui.Hud.MessageBoxData.Common);
                GD.Print(code != 0 ? $"login: connect failed (socket error {code}), box '{text}', then the dialog's failure state" : $"login: empty server list, box '{text}', then the dialog's failure state");
                Ui.MessageBoxLayer.Ensure(this);
                Ms2.Ui.Hud.MessageBoxQueue.ShowWithCallback(text, 0, (_, _) =>
                {
                    if (GetTree().CurrentScene is Ui.LoginScene { Controller: not null } now)
                        Ms2.Ui.LoginConnectFailure.Run(now.DialogOpen ? now.Controller.ShowConnectFailure : null, now.Scene.FailConnect);
                    return true;
                });
                return;
            }
            GD.Print("login: connect failed, the dialog shows its failure state");
            Ms2.Ui.LoginConnectFailure.Run(shown.DialogOpen ? shown.Controller.ShowConnectFailure : null, shown.Scene.FailConnect);
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
            var packs = PackSet.Shared(Settings.DataDir);
            var c = Ms2.Ui.FeatureElements.Constants(packs);
            k = ((int)c.Float("CharacterDestroyDivisionLevel", 20), (uint)c.Float("CharacterDestroyWaitSecond", 86400));
        }
        catch (Exception ex) { GD.PushWarning("flow constants: " + ex.Message); k = (20, 86400); }
        deleteConstants = k;
        return k;
    }

    /// <summary>
    /// The character list's delete requests (CharacterManagement 2 / 3 / 4: SendCharacterManagementMode2, CancelDelete,
    /// CheckDelete) for character <paramref name="sn"/>, sent on the live session; the answer goes to the dialog
    /// (UICharacterListDialog::OnCharacterListPacket modes 2, 5 and 6). Offline nothing is sent and the dialog takes
    /// requests again.
    /// </summary>
    public static void Manage(CharacterIdCommand command, string sn) => instance?.ManageLive(command, sn);

    /// <summary>
    /// The character list's server list popdown ended (OnEndPopdown, m_1d4 0x620001a): RequestServerList, then the lobby's
    /// server scene, whose dialog on the KMS client's platform connects again to the saved server and channel at once and
    /// logs in (uiserverlistdialog.cpp:346-371, FUN_141ad2560). The new character list enters character select again.
    /// The server scene's own movie is not hosted: the popped-down list stays on screen while it connects.
    /// </summary>
    public static void ReturnToServerList() => instance?.ServerListAgain();

    /// <summary>
    /// The character list's login scene popdown ended (OnEndPopdown, m_1d4 0x6200009, only where the platform's vfunc_1 is
    /// 0): QuitAndDisconnect (uicharacterlistdialog.cpp:1665-1681), SendRequestQuitAndDisconnect and the login scene.
    /// </summary>
    public static void QuitToLoginScene()
    {
        if (instance == null || instance.Current != State.Lobby) return;
        _ = Net.LiveSession.Instance?.SendRequestQuitAndDisconnect();
        instance.Go(State.Login);
    }

    async void ServerListAgain() => await LobbyLogin(false);

    /// <summary>One server list connect at a time from the lobby (the popdown's end and the AlreadyLogin box's OK).</summary>
    readonly ServerListConnect serverListConnect = new();

    /// <summary>
    /// The server list's connect from the lobby: after the list's RequestServerList, or (<paramref name="configured"/>) the
    /// AlreadyLogin box's OK. A good login enters character select again with the new list. AlreadyLogin (4, 5) shows its box
    /// and the lobby stays (CNetworkService::OnLoginResult, networkservice.cpp:1033-1036; UIServerListDialog::OnLoginResult only
    /// fails the hidden dialog, :604-611), so the box's OK connects again; the box answers only that OK, Escape included. Any
    /// other failure goes to the login scene with the box a first login shows: the server scene, where the client stays, is not
    /// hosted. A second start while one connects is ignored (<see cref="ServerListConnect"/>), and when the flow left the lobby
    /// meanwhile (the new login connection lost: the login scene and its box) nothing more is shown and a good login is dropped
    /// as the login scene's own quit drops it (QuitAndDisconnect).
    /// </summary>
    async System.Threading.Tasks.Task LobbyLogin(bool configured)
    {
        if (Current != State.Lobby) return;
        if (Net.LiveSession.Instance is not { } live) { if (!configured) Go(State.Login); return; }   // offline: no server list to connect to
        if (live.Busy) return;   // a request in flight owns the session (a server list connect too: set before its first await)
        var end = await serverListConnect.RunAsync(() => Current == State.Lobby, async () =>
            new ServerListConnectResult(configured ? await live.ConnectConfiguredAgainAsync() : await live.ServerListAgainAsync(), live.LastLoginRefusal?.State));
        if (Net.LiveSession.LiveCheck) GD.Print($"lobby: the server list's connect ended: {end}");
        switch (end)
        {
            case ServerListConnectEnd.Entered:
                Change(LobbyScene);   // CSceneStateMulti back to its select scene with the new list
                break;
            case ServerListConnectEnd.AlreadyLogin:
                GD.Print($"lobby: the server list's login was refused, state {live.LastLoginRefusal.State}; its box, and the lobby stays");
                Ui.MessageBoxLayer.Ensure(this);
                FrontEndBoxes.LoginRefused(live.LastLoginRefusal);
                break;
            case ServerListConnectEnd.Failed:
                GD.Print("lobby: the server list's login failed; back to the login scene");
                Go(State.Login);
                Ui.MessageBoxLayer.Ensure(this);
                if (live.LastLoginRefusal is { } r) FrontEndBoxes.LoginRefused(r);
                else Ms2.Ui.Hud.MessageBoxQueue.ShowMessageBox(Ms2.Ui.FrontEndConnection.ConnectFailed(live.LastConnectError, Ms2.Ui.Hud.MessageBoxData.Common), 0, null, "");
                break;
            case ServerListConnectEnd.LeftLoggedIn:
                GD.Print("lobby: the flow left character select during the server list's login; the new login is dropped");
                _ = live.SendRequestQuitAndDisconnect();
                break;
        }
    }

    async void ManageLive(CharacterIdCommand command, string sn)
    {
        var entry = Session.List().FirstOrDefault(c => c.User.CharacterId.ToString(System.Globalization.CultureInfo.InvariantCulture) == sn);
        var m = entry != null && Net.LiveSession.Instance is { Busy: false } live ? await live.ManageAsync(command, entry) : null;
        if (GetTree().CurrentScene is not CharacterListScene { Controller: { } dialog }) return;
        if (m == null) { dialog.DeleteUnanswered(); return; }
        Ui.MessageBoxLayer.Ensure(this);
        string id = m.CharacterId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        switch (m.Command)
        {
            case CharacterListCommand.DeleteEntry: dialog.OnDeleteEntry(m.Error, id); break;
            case CharacterListCommand.BeginDelete: dialog.OnBeginDelete(id, m.Error, m.DeleteTime); break;
            case CharacterListCommand.CancelDelete: dialog.OnCancelDelete(id, m.Error); break;
        }
    }

    /// <summary>
    /// The enter request at the popdown's end (FUN_1418f1790 -> UIServerListDialog::EnterGame) and how it ended. The
    /// client sends at once; a request still in flight here (a delete's answer) is waited for first, so the start is never
    /// dropped. A refused start shows OnLoginToGame's box and pops the list up (<see cref="Ms2.Ui.StartRefusal"/>); a
    /// failed game connect shows box 0x20e with the socket error, whose OK pops the list up (window message 0x467,
    /// UISceneLobbySelect::vfunc_64); a login connection lost with a socket error is the 0x465 path
    /// (<see cref="LoginConnectionLost"/>, raised by the session).
    /// <para>Chosen here, where the client has no such end: after a clean close of the login connection the client queues
    /// the request and waits for ever (EnterGame returns 1, gamesocket.cpp SendPacket keeps it pending), and with no
    /// LoginToGame answer it waits too; the port pops the list up instead, so the list never stays without input. A move
    /// refusal from the game server, or a game connection lost before the field, comes when the client is already past
    /// the list (its field load began with the LoginToGame); here the list is still up, so the move error's box shows and
    /// the list pops up.</para>
    /// </summary>
    async void EnterLive(Net.LiveSession live, CharacterEntry chosen)
    {
        while (live.Busy) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var r = await live.EnterAsync(chosen);
        switch (r.End)
        {
            case Net.LiveSession.EnterEnd.Entered:
                Go(State.Loading);
                return;
            case Net.LiveSession.EnterEnd.Refused:
                Ui.MessageBoxLayer.Ensure(this);
                if (Ms2.Ui.StartRefusal.Build(r.Code, r.Answer?.Message, Ms2.Ui.Hud.MessageBoxData.CommonByKey, r.Answer?.CharacterId ?? 0) is { } box)
                    Ms2.Ui.Hud.MessageBoxQueue.Show(box);
                StartFailed();
                return;
            case Net.LiveSession.EnterEnd.ConnectFailed:
            {
                Ui.MessageBoxLayer.Ensure(this);
                string text = Ms2.Ui.FrontEndConnection.ConnectFailed(r.Code, Ms2.Ui.Hud.MessageBoxData.Common);
                GD.Print($"flow: the game connect failed (socket error {r.Code}): box '{text}'");
                Ms2.Ui.Hud.MessageBoxQueue.ShowWithCallback(text, 0, (_, _) => { StartFailed(); return true; });
                return;
            }
            case Net.LiveSession.EnterEnd.MoveRefused:
                Ui.MessageBoxLayer.Ensure(this);
                Ms2.Ui.Hud.MessageBoxQueue.ShowMessageBox(Ms2.Ui.FieldMoveError.Text(r.Code, Ms2.Ui.Hud.MessageBoxData.CommonByKey), 0, null, "");
                StartFailed();
                return;
            case Net.LiveSession.EnterEnd.Dropped when r.Code != 0:
                return;   // the session raises LoginConnectionLost: the login scene and its box
            default:
                StartFailed();
                return;
        }
    }

    /// <summary>
    /// The rename box's request (SendCharacterManagementMode5) on the live session, and the login server's answer for the list:
    /// CharacterList 7 goes to the dialog (<see cref="Ms2.Ui.CharacterListController.OnNameChanged"/>); a list sent again
    /// (the server took the name) replaces the shown one, as a list after the return does. With no answer the list takes
    /// input again. Offline nothing is sent.
    /// </summary>
    async void RenameLive(long characterId, string name)
    {
        if (Net.LiveSession.Instance is not { } live) return;
        while (live.Busy) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var a = await live.RenameAsync(characterId, name);
        if (GetTree().CurrentScene is not CharacterListScene { Controller: { } dialog } list) return;
        Ui.MessageBoxLayer.Ensure(this);
        if (a?.List != null) { list.ReplaceList(); return; }
        if (a?.NameChanged is { } m) { dialog.OnNameChanged(m.Error, m.CharacterId.ToString(System.Globalization.CultureInfo.InvariantCulture), m.Name); return; }
        dialog.RenameCancelled();
    }

    /// <summary>ShowPopup on the character list, when it is still the scene: the list takes input again.</summary>
    void StartFailed()
    {
        if (GetTree().CurrentScene is CharacterListScene { Controller: { } dialog })
        {
            GD.Print("flow: the game start failed, the character list pops up again");
            dialog.ShowPopup();
        }
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
        // FUN_141880600: the new character's picture, shot from the create page and uploaded (its answer sets the list's key)
        var png = await CharacterCreateScene.ProfileShot();
        if (png != null) live.UploadNewCharacterPicture(e.User.CharacterId, png);
        Ui.CharacterListScene.SelectCharacter(Session.List()[^1]);
        Go(State.Lobby);
    }

    Func<string, string> commonStrings;
    Func<string, string> CommonStrings()
    {
        if (commonStrings != null) return commonStrings;
        try { var packs = PackSet.Shared(Settings.DataDir); commonStrings = new GameStrings(packs, GameOptions.StringLanguage).Common(); }
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
            var packs = PackSet.Shared(Settings.DataDir);
            subJob1 = new Ui.Create.CreateTables(packs).SubJob1(request.JobGroup);
        }
        catch (Exception ex) { GD.PushWarning("flow create: " + ex.Message); }
        var e = Session.Add(request, subJob1);
        Ui.CharacterListScene.SelectCharacter(e);
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
            var packs = PackSet.Shared(Settings.DataDir);
            xblock = FieldData.XBlockOf(packs, map);
            if (xblock == null || !packs.Exists(XBlock.Archive, $"xblock/{xblock.ToLowerInvariant()}.xblock")) { xblock = null; }
            tip = World.FieldContents.PickTip(packs);
        }
        catch (Exception ex) { GD.PushWarning("flow loading: " + ex.Message); }
        Travel.LoadingImage = null;
        Travel.LoadingTip = tip;
        Travel.ProgressMap = map;   // OnLoginToGame's FUN_141bbc580(0, field): the screen is a server move's (showMove)
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
            if (Travel.Pending != null) World.FieldContents.CoverLoad(PackSet.Shared(Settings.DataDir), this, shot);
            loadingFrames = 0;                       // the cover draws for a few frames before the scene change blocks
        }
        RenderingServer.FramePostDraw += Captured;
    }

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
        if (Current == State.Game && Ms2.Ui.FieldMoveCover.TakesInput && e is InputEventKey { Pressed: true, Echo: false, Keycode: Godot.Key.Escape })
        {
            var vp = GetViewport();
            (GetTree().CurrentScene as Ui.HudScene)?.Escape();
            if (IsInstanceValid(vp)) vp.SetInputAsHandled();
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
        FrontEndBoxes.LoginRefused(new Ms2.Net.Packets.LoginResult(3, 0, "", 0, 0, 0, 0, 0, null)); await Frames(30);                      // LoginResult state 3: s_login_err_pwd
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
