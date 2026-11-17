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
///   loading picture over the scene change) -> Game (hud.tscn: the field, the player in the chosen character's look, the HUD).
/// In Game, Esc opens a menu: resume, back to character select (Lobby), quit.
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
    CanvasLayer loadingLayer;
    TextureRect picture;
    EscMenu menu;
    int loadingFrames = -1;
    string shotDir;

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
        Session.Reset();
        Session.GameScene = GameScene;
        UiBridge.ToHost += OnHost;
        CharacterCreateScene.CharacterCreated += OnCreated;
        CharacterCreateScene.Cancelled += OnCreateCancelled;

        loadingLayer = new CanvasLayer { Layer = 150, Visible = false };
        var black = new ColorRect { Color = Colors.Black };
        black.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        loadingLayer.AddChild(black);
        // placed per frame by PlacePicture (the loading movie's setLocalImageContent), not stretched by anchors
        picture = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale };
        black.AddChild(picture);
        AddChild(loadingLayer);

        menu = new EscMenu();
        AddChild(menu);
        menu.BackToSelect += () => Go(State.Lobby);
        menu.Quit += () => GetTree().Quit(0);

        Go(State.Login);
        if (shotDir != null) { DirAccess.MakeDirRecursiveAbsolute(shotDir); _ = Script(); }
    }

    public override void _ExitTree()
    {
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
        if (menu.IsOpen) menu.Close();
        switch (next)
        {
            case State.Login: Change(LoginScene); break;
            case State.Lobby: Change(LobbyScene); break;
            case State.Create: loadingLayer.Visible = false; CharacterCreateScene.Enter(GetTree()); break;
            case State.Loading: BeginLoading(); break;
        }
    }

    void Change(string path)
    {
        loadingLayer.Visible = false;
        GetTree().ChangeSceneToFile(path);
    }

    void OnHost(string movie, string method, object[] a)
    {
        if (Current == State.Login && movie == UILoginDialogContract.Movie && method == UILoginDialogContract.ToHost.OnConnect)
        {
            Session.AccountId = "player";           // stub login: any id and password are accepted
            Go(State.Lobby);
        }
        else if (Current == State.Lobby && movie == UICharacterListDialogContract.Movie)
        {
            if (method == UICharacterListDialogContract.ToHost.OnSelectCharacter && a.Length > 0) Session.SelectedIndex = Convert.ToInt32(a[0]);
            else if (method == UICharacterListDialogContract.ToHost.OnCreatePC) Go(State.Create);
            else if (method == UICharacterListDialogContract.ToHost.OnGameStart && a.Length > 0)
            {
                var chosen = Session.List().FirstOrDefault(c => c.User.CharacterId.ToString() == (string)a[0]);
                if (chosen == null) return;
                Session.Chosen = chosen;
                Go(State.Loading);
            }
        }
    }

    void OnCreateCancelled()
    {
        if (Current == State.Create) Go(State.Lobby);
    }

    // The create screen only builds the request (nothing is sent); offline, the flow accepts it at once.
    void OnCreated(CreateCharacter request, byte[] bytes)
    {
        if (Current != State.Create) return;
        var e = Session.Add(request);
        Session.SelectedIndex = Session.List().IndexOf(e);
        Go(State.Lobby);
    }

    // ---- loading: the target map's picture over the (blocking) scene change ----------------------------------------

    void BeginLoading()
    {
        int map = Session.Chosen.User.MapA;
        string xblock = null;
        try
        {
            using var packs = new PackSet(Settings.DataDir);
            xblock = FieldData.XBlockOf(packs, map);
            if (xblock == null || !packs.Exists(XBlock.Archive, $"xblock/{xblock.ToLowerInvariant()}.xblock")) { xblock = null; }
            Travel.LoadingImage = new LoadingImageTable(packs).Pick(map, new Random());
            picture.Texture = null;
            if (Travel.LoadingImage != null && packs.Exists(LoadingImageTable.ImageArchive, Travel.LoadingImage))
            {
                var img = new Image();
                if (img.LoadDdsFromBuffer(packs.Read(LoadingImageTable.ImageArchive, Travel.LoadingImage)) == Error.Ok)
                    picture.Texture = ImageTexture.CreateFromImage(img);
            }
        }
        catch (Exception ex) { GD.PushWarning("flow loading: " + ex.Message); }
        // the field scene reads this: the chosen character's field (Tria when the data has no such map), no portal
        Travel.Pending = xblock == null ? null : new Travel.Move(map, xblock, 0, 0);
        GD.Print($"flow: loading map {map} ({xblock ?? "default field"}), picture {Travel.LoadingImage}");
        loadingLayer.Visible = true;
        PlacePicture();
        loadingFrames = 0;                           // the picture draws for a few frames before the scene change blocks
    }

    /// <summary>
    /// The first entry's picture as UILoadingProgress places it (the same movie as a field move): setLocalImageContent on the
    /// movie stage, so a wide window crops the picture to its width instead of showing bars. The layer is in the viewport's
    /// (content-scaled) units; the rule is in window pixels, as the client's movie scale comes from the window width.
    /// </summary>
    void PlacePicture()
    {
        if (picture.Texture == null) { picture.Visible = false; return; }
        picture.Visible = true;
        var window = (Vector2)GetWindow().Size;
        var units = GetViewport().GetVisibleRect().Size;
        if (window.X <= 0 || window.Y <= 0) return;
        var r = LoadingProgressLogic.LocalImageRectInWindow(picture.Texture.GetSize(), window);
        var toUnits = units / window;
        picture.Position = r.Position * toUnits;
        picture.Size = r.Size * toUnits;
    }

    public override void _Process(double delta)
    {
        if (loadingFrames < 0) return;
        PlacePicture();
        loadingFrames++;
        if (loadingFrames == 4) GetTree().ChangeSceneToFile(GameScene);
        else if (loadingFrames > 4 && GetTree().CurrentScene?.SceneFilePath == GameScene && loadingFrames > 8)
        {
            loadingLayer.Visible = false;            // the field's own cover (same picture) fades out from here
            loadingFrames = -1;
            Current = State.Game;
            GD.Print("flow: Loading -> Game");
        }
    }

    public override void _Input(InputEvent e)
    {
        if (Current == State.Game && e is InputEventKey { Pressed: true, Echo: false, Keycode: Godot.Key.Escape })
        {
            menu.Toggle();
            GetViewport().SetInputAsHandled();
        }
    }

    // ---- scripted proof run ----------------------------------------------------------------------------------------

    async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    async Task Until(Func<bool> cond, int maxFrames = 3000) { for (int i = 0; i < maxFrames && !cond(); i++) await Frames(1); }
    // presses the job page's back button (the scene's members are private)
    void ClickJobBack()
    {
        const System.Reflection.BindingFlags f = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
        var scene = GetTree().CurrentScene;
        var jobs = scene?.GetType().GetField("jobs", f)?.GetValue(scene);
        var button = jobs?.GetType().GetField("PrevButton")?.GetValue(jobs);
        typeof(Widget).GetMethod("RaiseClick", f)?.Invoke(button, null);
    }
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

    async Task Script()
    {
        await Until(() => InScene(LoginScene)); await Frames(90);
        Shot("01_login.png");
        UiBridge.Raise(UILoginDialogContract.Movie, UILoginDialogContract.ToHost.OnConnect);     // the Login button
        await Until(() => Current == State.Lobby && InScene(LobbyScene)); await Frames(90);
        Shot("02_charselect.png");
        for (int i = 0; i < 3; i++) { Press(Godot.Key.Down); await Frames(5); }                      // the empty slot
        Shot("03_charselect_empty_slot.png");
        Press(Godot.Key.Enter);
        await Until(() => Current == State.Create && InScene(CreateScene)); await Frames(180);
        Shot("04_create.png");
        ClickJobBack();                                                                               // the job page's back button
        await Until(() => Current == State.Lobby && InScene(LobbyScene)); await Frames(60);
        for (int i = 0; i < 3; i++) { Press(Godot.Key.Down); await Frames(5); }
        Press(Godot.Key.Enter);                                                                       // the empty slot again
        await Until(() => Current == State.Create && InScene(CreateScene)); await Frames(120);
        OnCreated(new CreateCharacter(0, 20, "Neutral", 0, 0, Array.Empty<byte>(), 0), Array.Empty<byte>());   // stands in for a confirmed name (the event is raised inside the create scene)
        await Until(() => Current == State.Lobby && InScene(LobbyScene)); await Frames(90);
        Shot("05_charselect_new_character.png");
        Press(Godot.Key.Enter);                                                                       // start with the new character
        await Frames(2);
        Shot("06_loading.png");
        await Until(() => Current == State.Game); await Frames(240);
        Shot("07_field_hud.png");
        Press(Godot.Key.Escape); await Frames(10);
        Shot("08_esc_menu.png");
        menu.BackButton.EmitSignal(BaseButton.SignalName.Pressed);
        await Until(() => Current == State.Lobby && InScene(LobbyScene)); await Frames(90);
        Shot("09_back_at_charselect.png");
        Press(Godot.Key.Up); await Frames(5); Press(Godot.Key.Enter);
        await Until(() => Current == State.Game); await Frames(240);
        Shot("10_field_second_entry.png");
        Press(Godot.Key.Escape); await Frames(10);
        GD.Print("flow: quit from the menu");
        menu.QuitButton.EmitSignal(BaseButton.SignalName.Pressed);
    }
}
