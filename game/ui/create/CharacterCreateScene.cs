using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2Godot.Look;
using Ms2Godot.Ui.Contracts;
using Ms2.Ui;

namespace Ms2Godot.Ui.Create;

/// <summary>
/// Character creation as CSceneStateMulti runs it, without a server.
/// Job pick: the lobby select field with the character list dimmed behind uicharactercreateselectdialog, run as the
/// client's own movie with UICharacterCreateSelectDialog's C++ side (<see cref="CharacterCreateSelectController"/>).
/// Create: SelectCreateJob loads the job's create map (table/lobbyfield.xml createMap, e.g. the "Character" xblock),
/// the camera is the field's Camera0 entity, the preview character stands at SpawnPointPC0 in its idle pose, dressed
/// by CreateLook, and uicharactercreatedialog runs on top. Create builds the CharacterManagement request and prints it.
/// A change of field reloads this scene (the page, job and gender carry over in static state).
/// User args (after --): --page select|create, --job CODE, --gender male|female, --shots DIR (scripted shots, then quit).
/// </summary>
public partial class CharacterCreateScene : LobbyFieldScene
{
    enum Page { Select, Create }

    static Page page = Page.Select;
    static int job, gender = -1;
    static bool argsRead;
    static readonly Random rng = new();

    // ---- entry and exit (the game flow's API; stable) ----

    /// <summary>The scene file the flow changes to.</summary>
    public const string ScenePath = "res://ui/charcreate.tscn";

    /// <summary>
    /// Fired once when the player confirms a name that passes the client's check: the CharacterManagement create
    /// request and its encoded bytes (opcode first). Nothing is sent; the flow sends it and changes scene on the answer.
    /// Static because the scene reloads itself between the job page and the create page.
    /// </summary>
    public static event Action<Ms2.Net.Packets.CreateCharacter, byte[]> CharacterCreated;

    /// <summary>Fired when the player leaves creation from the job page (its back button): return to the character list.</summary>
    public static event Action Cancelled;

    /// <summary>
    /// Opens character creation at the job page (UICharacterCreateSelectDialog), with <paramref name="startJob"/> selected
    /// (0: the table's start job). Clears the page, job and gender a previous visit left; command line args are not read.
    /// </summary>
    public static Error Enter(SceneTree tree, int startJob = 0)
    {
        page = Page.Select; job = startJob; gender = -1; argsRead = true;
        return tree.ChangeSceneToFile(ScenePath);
    }

    /// <summary>The server refused the request: shows its message in the name prompt, which opens again.</summary>
    /// <summary>Scripted runs: types the name into the open create screen and confirms it.</summary>
    public static void ConfirmScripted(string name) => current?.custom?.Confirm(name);
    /// <summary>Scripted runs: the job page's Create button for its selected job (onClickCreate), which opens the look page.</summary>
    public static void PickJobScripted() { if (current?.select != null && page == Page.Select) current.ClickCreate(); }
    /// <summary>Scripted runs: the job page's back button (CharacterCreateSelectDialog.onClickPrev), as a click on it.</summary>
    public static void ClickPrevScripted() { if (page == Page.Select && current?.selectDialog?.Host is { } h) h.Invoke(h.Entity, "onClickPrev", (object)null); }
    public static bool OnCreatePage => current != null && page == Page.Create && current.custom != null;
    public static void CreateFailed(string message) => current?.custom?.ShowCreateError(message);

    static CharacterCreateScene current;
    public override void _EnterTree() { base._EnterTree(); LobbyLayout.UseWindowPixels(this); current = this; }
    public override void _ExitTree()
    {
        if (current == this) current = null;
        if (layer != null) GetViewport().SizeChanged -= QueueRelayout;
        base._ExitTree();
    }

    PackSet uiPacks;
    CreateTables tables;
    UiAssets assets;
    CanvasLayer layer;
    MovieClipNode createRoot;
    /// <summary>uicharacterlistdialog behind the job page.</summary>
    MovieDialog listDialog;
    /// <summary>uicharactercreateselectdialog, its C++ side and the picture of its job preview VideoPlayer.</summary>
    MovieDialog selectDialog;
    CharacterCreateSelectController select;
    MovieVideo selectVideo;
    CharacterCreateLogic custom;
    UiWidgets.MouseRouter mouse;
    ModelBuilder builder;
    FieldMap map;
    string shotDir;
    Vector2I? resizeTo;
    int frame;

    protected override string FieldNameFor(string defaultName)
    {
        var args = OS.GetCmdlineUserArgs();
        if (!argsRead)
        {
            argsRead = true;
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] == "--page") page = args[i + 1] == "create" ? Page.Create : Page.Select;
                if (args[i] == "--job" && int.TryParse(args[i + 1], out int j)) job = j;
                if (args[i] == "--gender") gender = args[i + 1].StartsWith("m", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            }
        }
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--shots") shotDir = args[i + 1];
            if (args[i] == "--resize" && args[i + 1].Split('x') is [var w, var h] && int.TryParse(w, out int rw) && int.TryParse(h, out int rh)) resizeTo = new Vector2I(rw, rh);
        }
        if (string.IsNullOrEmpty(Settings.DataDir)) return defaultName;
        uiPacks = new PackSet(Settings.DataDir);
        tables = new CreateTables(uiPacks);
        if (job == 0 && page == Page.Create)
        {
            // the create page opened by itself (--page create): the job page's start job (FUN_140f30150)
            var t = new CharacterCreateSelectController { Rng = rng };
            t.Load(uiPacks, new UiAssets(uiPacks).Language);
            job = t.PickJob();
        }
        if (gender < 0) gender = rng.Next(2);
        int mapId = page == Page.Create ? tables.CreateMap(job) : tables.SelectMap(1);
        return tables.XBlockOf(mapId) ?? defaultName;
    }

    protected override void OnFieldBuilt(ModelBuilder builder, FieldMap map, string fieldName)
    {
        this.builder = builder; this.map = map;
        PlaceLobby(map);
        GD.Print($"character create: {page} page, field {fieldName}, job {job}, gender {gender}");

        assets = new UiAssets(packs);
        layer = new CanvasLayer { Name = "UI" };
        AddChild(layer);
        if (page == Page.Select) BuildSelect();
        else BuildCreate();
        // the client sizes every movie to the back buffer: on a window resize the open dialogs align again
        // (UIDialog::vfunc_43 -> alignment), as on the login screen and the character list
        GetViewport().SizeChanged += QueueRelayout;
    }

    bool relayoutQueued;
    void QueueRelayout()
    {
        if (relayoutQueued) return;
        relayoutQueued = true;
        CallDeferred(MethodName.Relayout);
    }

    /// <summary>The open page built again at the new window's UI scale, in the state the player left it.</summary>
    void Relayout()
    {
        relayoutQueued = false;
        if (!IsInsideTree()) return;
        var window = Screen().Window;
        // the job page's movies align again in place (UIDialog::vfunc_43 -> alignment), as on the character list
        listDialog?.Relayout(window);
        selectDialog?.Relayout(window);
        if (custom == null) return;
        bool optionsOpen = custom.Options.Visible;     // read while the old page is still in the tree
        if (createRoot != null) { layer.RemoveChild(createRoot); createRoot.QueueFree(); }
        createRoot = null;
        BuildCreate(custom, optionsOpen);
    }

    /// <summary>The window in pixels (LobbyLayout.UseWindowPixels), its UI scale and movie stage (LobbyLayout).</summary>
    (Vector2 Window, float U, Vector2 Stage) Screen()
    {
        var window = GetViewport().GetVisibleRect().Size;
        return (window, UiScale.For(window), UiScale.MovieStage(window));
    }

    // ---- job pick ----

    /// <summary>
    /// The job page: the character list behind it as the select scene left it, then uicharactercreateselectdialog
    /// (OnLoadComplete -> initData, which plays the page's popup). Its answers: OnEndPopdown after onClickCreate is
    /// CSceneStateMulti::SelectCreateJob (the create page on the job's create map); after onClickPrev the flow goes back
    /// to the character list.
    /// </summary>
    void BuildSelect()
    {
        var window = Screen().Window;
        // the character list stays behind, dimmed by the job page's m_fullBg
        try
        {
            // the select scene's dialog as it was when the job page opened over it (UICharacterListDialog::OnEndPopdown
            // 0x620001c -> SelectCreateJob): the flow's characters, at the popup's end, taking no input
            var list = CharacterListScene.NewController(assets, uiPacks ?? packs, new LobbyCharacterData(packs, assets.Language), Flow.Session.List());
            list.KeepRow = Flow.Session.SelectedIndex;
            listDialog = CharacterListScene.OpenList(assets, list, window);
            listDialog.Step(CharacterListScene.PopupFrames);
            list.SetInput(false);
            layer.AddChild(listDialog);
        }
        catch (Exception ex) { GD.PushWarning("character list: " + ex.Message); }

        select = new CharacterCreateSelectController { Rng = rng, KeepJob = job };
        select.Load(uiPacks ?? packs, assets.Language);
        select.SelectCreateJob += j => { job = j; gender = rng.Next(2); SwitchTo(Page.Create); };
        select.Closed += () => { GD.Print("job pick: back to the character list"); Cancelled?.Invoke(); };
        selectDialog = new MovieDialog(assets, select, new UiModel(new GameTables(assets.Packs, assets.Language)), window);
        layer.AddChild(selectDialog);
        selectVideo = new MovieVideo(selectDialog, uiPacks ?? packs, () =>
            selectDialog.Host.Get(selectDialog.Host.Entity, "m_content") is { } content ? selectDialog.Host.Get(content, "m_moviePlayer") : null);
        selectDialog.AddChild(selectVideo);
    }

    /// <summary>The page's Create button (CharacterCreateSelectContent.onClickCreate), as a click on it.</summary>
    void ClickCreate()
    {
        if (selectDialog?.Host is { } h && h.Get(h.Entity, "m_content") is { } content) h.Invoke(content, "onClickCreate", (object)null);
    }

    /// <summary>The tab renderers of m_tab and m_tab2 (CLIK ButtonBar), in arrTab order: even indices in m_tab when there are more than six.</summary>
    List<object> Tabs()
    {
        var tabs = new List<object>();
        if (selectDialog?.Host is not { } h || h.Get(h.Entity, "m_content") is not { } content) return tabs;
        var bars = new[] { h.Get(content, "m_tab"), h.Get(content, "m_tab2") };
        var lists = bars.Select(b => Renderers(h, b)).ToArray();
        bool two = lists[0].Count > 0;
        for (int i = 0; i < select.Jobs.Count; i++)
        {
            var l = two ? lists[i % 2] : lists[1];
            int k = two ? i / 2 : i;
            if (k < l.Count) tabs.Add(l[k]);
        }
        return tabs;
    }

    static List<object> Renderers(UiHost h, object bar)
    {
        var r = new List<object>();
        if (bar == null) return r;
        int n = (int)Ms2.Avm2.Rt.ToNumber(h.Get(h.Get(bar, "dataProvider"), "length"));
        for (int i = 0; i < n; i++) r.Add(h.Invoke(bar, "getButtonAt", i));
        return r;
    }

    /// <summary>The tab of arrTab index <paramref name="index"/>, clicked through the mouse.</summary>
    void ClickTab(int index)
    {
        var tabs = Tabs();
        if (index < 0 || index >= tabs.Count || tabs[index] is not Ms2.Avm2.Flash.AsDisplayObject t) return;
        var b = t.GetBounds(null);
        float s = UiScale.For(Screen().Window);
        ClickAt(UiScale.MovieOrigin + new Vector2((float)(b.X + b.W / 2), (float)(b.Y + b.H / 2)) * s);
    }

    /// <summary>The tab next to the selected one in the same bar (tabs alternate between the two bars): for the shots.</summary>
    int NeighbourTab()
    {
        int i = Math.Max(0, select.Jobs.FindIndex(j => j.JobCode == select.ShownJob));
        return i >= 2 ? i - 2 : i + 2;
    }

    // ---- create ----

    /// <summary>The create page; with <paramref name="old"/> it is rebuilt over that page's look and state.</summary>
    void BuildCreate(CharacterCreateLogic old = null, bool optionsOpen = false)
    {
        var (_, u, stage) = Screen();
        var movie = new UiMovie(assets, UICharacterCreateDialogContract.Movie);
        createRoot = movie.BuildRoot(u);
        createRoot.Scale = new Vector2(u, u);
        UiWidgets.ApplyInspector(createRoot, UICharacterCreateDialogContract.Inspector);
        layer.AddChild(createRoot);
        // the add-blend sparkles (m_eff, the title strips) draw as black boxes without blend modes: leave them out
        foreach (var clip in createRoot.AllClips().ToList())
        {
            bool any = false;
            foreach (var ch in clip.Children)
                if (ch.Def?["blend"] is { } b && (int)b == 8) { ch.Last = -1; any = true; }
            if (any) clip.GotoFrame(clip.Frame);
        }
        // CharacterCreateDialog.alignment: the dialog at the centre right for m_bg (applyScale(m_bg)), then
        // btnsAlignment(m_guide, getUIScale, m_guide.height, globalToLocal(0, stageHeight)): the guide button at the
        // stage's bottom left
        float guideH = createRoot.FindClip("m_guide") is { } g ? LobbyLayout.AsHeight(g) : 0;
        var (at, r) = LobbyLayout.CenterRightOfBg(createRoot, createRoot.FindClip("m_content.m_bg"), stage, u);
        if (createRoot.ChildNamed("m_guide") is { } guide)
        {
            guide.Override = new Transform2D(0, LobbyLayout.BtnsAlignment(LobbyLayout.StageBottomLeftInDialog(at, r, stage), guideH, stage));
            createRoot.GotoFrame(createRoot.Frame);
        }
        var look = old?.Look ?? new CreateLook(packs, new ColorPalettes(packs));
        custom = new CharacterCreateLogic(movie, createRoot, look);
        custom.LookChanged += RebuildPreview;
        custom.Back += () => SwitchTo(Page.Select);
        custom.Created += (packet, bytes) => CharacterCreated?.Invoke(packet, bytes);
        if (old != null) custom.Restore(old, Flow.Session.ServerInfo(assets), assets, optionsOpen);
        else
        {
            custom.InitData(gender, job, Flow.Session.ServerInfo(assets), assets);
            RebuildPreview();
        }
        mouse = new UiWidgets.MouseRouter(createRoot);
    }

    void RebuildPreview()
    {
        if (custom == null) return;
        gender = custom.Look.Gender;
        ClearPreview();
        try
        {
            var (visual, anim) = custom.Look.Build(builder);
            ShowPreview(visual, anim);
        }
        catch (Exception ex) { GD.PushWarning("preview: " + ex.Message); }
    }

    /// <summary>A page on another field: reload the scene (static state keeps the page, job and gender).</summary>
    void SwitchTo(Page next)
    {
        page = next;
        CallDeferred(MethodName.Reload);
    }

    void Reload()
    {
        uiPacks?.Dispose();
        GetTree().ReloadCurrentScene();
    }

    public override void _Input(InputEvent e)
    {
        if (custom?.PromptOpen == true) return;
        mouse?.Handle(e);
        if (e is InputEventMouse m) custom?.OnMouse(m);
        // the job page's Enter does nothing (CharacterCreateSelectDialog.onEnterKey returns)
        if (e is InputEventKey { Pressed: true, Echo: false } k && k.Keycode is Key.Enter or Key.KpEnter) custom?.OnCreate();
    }

    // ---- scripted shots ----

    void ClickAt(Vector2 at)
    {
        Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (shotDir == null) return;
        frame++;
        if (resizeTo is { } to) { ResizeShots(to); return; }
        if (page == Page.Select)
        {
            if (frame == 45) Save("create_jobselect.png");
            if (frame == 46) ClickTab(NeighbourTab());   // the selected tab's neighbour in its bar, through the mouse
            if (frame == 75) Save("create_jobselect_clicked.png");
            if (frame == 160) { var p = selectVideo?.Player; Save("create_jobselect_video.png"); GD.Print($"job video {p?.MovieName}: {p?.Video?.Width}x{p?.Video?.Height} at {p?.Video?.FrameRate} fps, t {p?.Time:F2} s"); }
            if (frame == 161) ClickCreate();
        }
        else
        {
            if (frame == 60) Save($"create_page_{job}.png");
            if (frame == 61) custom.SelectCategory(0, true);
            if (frame == 62 && custom.Look.SlotItems(Ms2.Net.Packets.EquipSlot.HR) is { Count: > 1 } hair) custom.ChangeItem(hair[1]);
            if (frame == 63) custom.ChangeColor(3);
            if (frame == 90) Save($"create_hair_{job}.png");
            if (frame == 91) custom.ChangeGender(1 - custom.Look.Gender, true);
            if (frame == 120) Save($"create_gender_{job}.png");
            if (frame == 121) custom.OnRandom();
            if (frame == 150) Save($"create_random_{job}.png");
            if (frame == 151) { custom.SelectCategory(2, true); PickEditable(Ms2.Net.Packets.EquipSlot.FD); }
            if (frame == 152) { custom.Options.SetSlider(0, 0.5f); custom.Options.SetSlider(1, 90); custom.Options.ClickPosition(1); }
            if (frame == 180) Save($"create_custom_makeup_{job}.png");
            if (frame == 181) { custom.SelectCategory(0, true); PickEditable(Ms2.Net.Packets.EquipSlot.HR); }
            if (frame == 182) custom.Options.SetSlider(0, 0.4f);
            if (frame == 210) Save($"create_custom_hair_{job}.png");
            if (frame == 211) { custom.OnCreate(); custom.Confirm("Tester01"); }
            if (frame == 215) { packs.Dispose(); uiPacks?.Dispose(); GetTree().Quit(0); }
        }
    }

    /// <summary>
    /// --resize WxH: the page in a changed state (job page: the neighbour tab clicked; create page: the makeup category with
    /// an editable item and its option panel open, and the name prompt open with a name typed), one shot, then the window
    /// resized and a second shot.
    /// </summary>
    void ResizeShots(Vector2I to)
    {
        string name = page == Page.Select ? "create_jobselect" : $"create_page_{job}";
        if (frame == 45)
        {
            if (page == Page.Select) ClickTab(NeighbourTab());
            else { custom.SelectCategory(2, true); PickEditable(Ms2.Net.Packets.EquipSlot.FD); custom.OnCreate(); custom.PromptText = "Tester01"; }
        }
        if (frame == 90) Save($"{name}_before.png");
        if (frame == 91) GetWindow().Size = to;
        if (frame == 150) { Save($"{name}_resized_{to.X}x{to.Y}.png"); packs.Dispose(); uiPacks?.Dispose(); GetTree().Quit(0); }
    }

    /// <summary>Scripted runs: wear the slot's first editable item, preferring one with position presets.</summary>
    void PickEditable(Ms2.Net.Packets.EquipSlot slot)
    {
        var ids = custom.Look.SlotItems(slot).Where(i => custom.Look.Customize(i).Editable).ToList();
        int id = ids.FirstOrDefault(i => custom.Look.Customize(i).Positions.Count > 0, ids.FirstOrDefault());
        if (id != 0) custom.ChangeItem(id);
        GD.Print($"custom {slot}: item {id}, flags {custom.Look.Customize(id).EditFlags}");
    }

    void Save(string name)
    {
        DirAccess.MakeDirRecursiveAbsolute(shotDir);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(shotDir, name));
        GD.Print("saved " + name);
    }
}
