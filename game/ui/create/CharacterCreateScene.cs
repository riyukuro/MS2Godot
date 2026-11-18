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
/// by CreateLook, and uicharactercreatedialog runs on top as the client's own movie with UICharacterCreateDialog's C++
/// side (<see cref="CharacterCreateController"/>). Create asks for the name (a STAND-IN box: uimessagebox has no movie
/// port yet) and builds the CharacterManagement request.
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

    /// <summary>Scripted runs: the page's Create, then the name typed into the box and OK.</summary>
    public static void ConfirmScripted(string name)
    {
        if (current?.create is not { } c) return;
        c.OnCallback("OnCreate", Array.Empty<object>());
        current.SubmitName(name);
    }
    /// <summary>Scripted runs: the job page's Create button for its selected job (onClickCreate), which opens the look page.</summary>
    public static void PickJobScripted() { if (current?.select != null && page == Page.Select) current.ClickCreate(); }
    /// <summary>Scripted runs: the job page's back button (CharacterCreateSelectDialog.onClickPrev), as a click on it.</summary>
    public static void ClickPrevScripted() { if (page == Page.Select && current?.selectDialog?.Host is { } h) h.Invoke(h.Entity, "onClickPrev", (object)null); }
    public static bool OnCreatePage => current != null && page == Page.Create && current.create?.State == 1;
    /// <summary>The server refused the request (the flow shows its message): failCreate, the page takes input again.</summary>
    public static void CreateFailed() => current?.create?.CreateRefused();

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
    /// <summary>uicharacterlistdialog behind the job page.</summary>
    MovieDialog listDialog;
    /// <summary>uicharactercreateselectdialog, its C++ side and the picture of its job preview VideoPlayer.</summary>
    MovieDialog selectDialog;
    CharacterCreateSelectController select;
    MovieVideo selectVideo;
    /// <summary>uicharactercreatedialog, its C++ side and the picture of the open category's guide VideoPlayer.</summary>
    MovieDialog createDialog;
    CharacterCreateController create;
    MovieVideo createVideo;
    NamePromptStandIn prompt;
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
        createDialog?.Relayout(window);
        prompt?.Center();
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

    /// <summary>
    /// The create page: the preview actor (CSceneStateMulti::FUN_14174ec40 on the map's load: a random gender's look with the
    /// job outfit's select rows), then uicharactercreatedialog (OnLoadComplete -> initData; the movie pops up after
    /// buildCurrentItems). Its OnEndPopdown goes back to the job page (0x4c8: the select scene, then the job pick reopens).
    /// </summary>
    void BuildCreate()
    {
        var window = Screen().Window;
        var look = new CreateLook(packs, new ColorPalettes(packs), assets.Language) { Rng = rng };
        look.Create(gender, job, true);
        create = new CharacterCreateController
        {
            Look = look,
            Server = Flow.Session.ServerInfo(assets),
            NoCharacters = Flow.Session.List().Count == 0,
            Common = k => assets.Str("$" + k) is var t && !t.StartsWith('$') ? t : null,
            CheckName = CheckName,
        };
        create.LookChanged += RebuildPreview;
        create.NameAsked += OpenPrompt;
        create.NameError += m => prompt?.SetError(m);
        create.CreateRequested += Created;
        create.Back += () => SwitchTo(Page.Select);
        create.GuideAsked += () => GD.Print("create: the mouse guide (0x4ea7) is not ported");
        RebuildPreview();
        createDialog = new MovieDialog(assets, create, new UiModel(new GameTables(assets.Packs, assets.Language)), window);
        layer.AddChild(createDialog);
        // CustomOption.playMovie: the open category's m_custom VideoPlayer (in m_moviedummy)
        createVideo = new MovieVideo(createDialog, uiPacks ?? packs, () =>
            CurrentContent() is { } content && createDialog.Host.Get(content, "m_custom") is Ms2.Avm2.AsObject custom
                ? createDialog.Host.Get(custom, "m_moviePlayer") : null);
        createDialog.AddChild(createVideo);
    }

    void RebuildPreview()
    {
        if (create == null) return;
        gender = create.Look.Gender;
        ClearPreview();
        try
        {
            var (visual, anim) = create.Look.Build(builder);
            ShowPreview(visual, anim);
        }
        catch (Exception ex) { GD.PushWarning("preview: " + ex.Message); }
    }

    /// <summary>The name box (STAND-IN for the client's uimessagebox input box: one line, up to 12 characters, OK and Cancel).</summary>
    void OpenPrompt()
    {
        if (prompt != null) return;
        prompt = new NamePromptStandIn(assets.Str("$s_charcreate_input_name") is var t && !t.StartsWith('$') ? t : "");
        prompt.Ok += text => { if (create.SubmitName(text)) ClosePrompt(); };
        prompt.Cancel += ClosePrompt;
        layer.AddChild(prompt);
        prompt.Center();
    }

    void ClosePrompt() { prompt?.QueueFree(); prompt = null; }

    /// <summary>Scripted runs: the name into the open box, then OK.</summary>
    void SubmitName(string name)
    {
        if (create.SubmitName(name)) ClosePrompt();
    }

    void Created(Ms2.Net.Packets.CreateCharacter packet)
    {
        var w = new Maple2.PacketLib.Tools.ByteWriter();
        w.WriteShort((short)packet.Op);
        packet.Write(w);
        var bytes = w.ToArray();
        GD.Print($"create request: {packet.Op} {bytes.Length} bytes, gender {packet.Gender}, job {packet.JobGroup}, name \"{packet.Name}\", " +
                 $"items {string.Join(" ", Ms2.Net.Packets.CreateCharacter.DecodeItems(packet.Items).Select(i => $"{i.Slot}:{i.Id}/{i.Color.Index}"))}");
        CharacterCreated?.Invoke(packet, bytes);
    }

    Ms2.Formats.Text.BanTextChecker nameChecker;

    /// <summary>FUN_141276a30: the client's text checker (BanTextChecker.CheckCharacterName); null when the name passes.</summary>
    string CheckName(string name)
    {
        nameChecker ??= Ms2.Formats.Text.BanTextChecker.FromPacks(assets.Packs, assets.Language);
        return nameChecker.CheckCharacterName(name, out var message) == 0 ? null : message;
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
        if (prompt != null) return;
        // UICharacterCreateDialog::vfunc_13: Enter (key 0x1c) while the page takes input -> onEnterKey; the job page's Enter
        // does nothing (CharacterCreateSelectDialog.onEnterKey returns)
        if (e is InputEventKey { Pressed: true, Echo: false } k && k.Keycode is Key.Enter or Key.KpEnter && create?.OnEnterKey() == true)
            GetViewport().SetInputAsHandled();
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
        create?.Update();
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
            // the guide video's first frame decoded before a shot (scripted runs only)
            if (frame is 59 or 89 or 349 or 399) { createVideo?.Sync(); createVideo?.Player.ShowFirstFrame(); }
            if (frame == 60) Save($"create_page_{job}.png");
            if (frame == 61) { var hair = Renderer("m_editItemList", 1); ClickObj(hair); }
            if (frame == 66) ClickObj(Renderer("m_colorList", 3));
            if (frame == 90) Save($"create_hair_{job}.png");
            if (frame == 91) ClickObj(Content(create.Look.Gender == 0 ? "m_female" : "m_male"));
            if (frame == 120) Save($"create_gender_{job}.png");
            if (frame == 121) ClickObj(Content("m_random"));
            if (frame == 150) Save($"create_random_{job}.png");
            for (int c = 1; c < 7; c++)
            {
                if (frame == 150 + c * 20) ClickCategory(c);
                if (frame == 150 + c * 20 + 19) Save($"create_category{c}_{job}.png");
            }
            if (frame == 300) { ClickCategory(2); }
            if (frame == 320) { PickEditable(); }
            if (frame == 350) Save($"create_custom_makeup_{job}.png");
            if (frame == 351) ClickCategory(0);
            if (frame == 371) PickEditable();
            if (frame == 400) Save($"create_custom_hair_{job}.png");
            if (frame == 401) ClickObj(Content("m_create"));
            if (frame == 420) { Save($"create_name_prompt_{job}.png"); SubmitName("Tester01"); }
            if (frame == 430) { packs.Dispose(); uiPacks?.Dispose(); GetTree().Quit(0); }
        }
    }

    /// <summary>
    /// --resize WxH: the page in a changed state (job page: the neighbour tab clicked; create page: the makeup category with
    /// an editable item and its option panel open, and the name box open), one shot, then the window resized and a second shot.
    /// </summary>
    void ResizeShots(Vector2I to)
    {
        string name = page == Page.Select ? "create_jobselect" : $"create_page_{job}";
        if (frame == 45)
        {
            if (page == Page.Select) ClickTab(NeighbourTab());
            else ClickCategory(2);
        }
        if (frame == 65 && page == Page.Create) PickEditable();
        if (frame == 80 && page == Page.Create) ClickObj(Content("m_create"));
        if (frame == 90) Save($"{name}_before.png");
        if (frame == 91) GetWindow().Size = to;
        if (frame == 150) { Save($"{name}_resized_{to.X}x{to.Y}.png"); packs.Dispose(); uiPacks?.Dispose(); GetTree().Quit(0); }
    }

    // ---- the create movie's objects, for the scripted shots ----

    /// <summary>The open category's CharacterCreateContent (m_content.m_page.getCurrentContent()), or null.</summary>
    Ms2.Avm2.AsObject CurrentContent()
    {
        var h = createDialog?.Host;
        if (h?.Entity == null || h.Get(h.Get(h.Entity, "m_content"), "m_page") is not Ms2.Avm2.AsObject page) return null;
        return h.Invoke(page, "getCurrentContent") as Ms2.Avm2.AsObject;
    }

    object Content(string child)
    {
        var h = createDialog.Host;
        return h.Get(h.Get(h.Entity, "m_content"), child);
    }

    /// <summary>The renderer of the open category's list (m_editItemList or m_colorList) at <paramref name="index"/>.</summary>
    object Renderer(string list, int index)
    {
        var h = createDialog.Host;
        var content = CurrentContent();
        return content == null ? null : h.Invoke(h.Get(content, list), "getRendererAt", index);
    }

    /// <summary>A click at the middle of a movie display object.</summary>
    void ClickObj(object o)
    {
        if (o is not Ms2.Avm2.Flash.AsDisplayObject d) { GD.Print("create shots: nothing to click"); return; }
        var b = d.GetBounds(null);
        float s = UiScale.For(Screen().Window);
        ClickAt(UiScale.MovieOrigin + new Vector2((float)(b.X + b.W / 2), (float)(b.Y + b.H / 2)) * s);
    }

    /// <summary>The category button <paramref name="index"/> of m_page (its ButtonGroup), clicked.</summary>
    void ClickCategory(int index)
    {
        var h = createDialog.Host;
        var group = h.Get(h.Get(h.Get(h.Entity, "m_content"), "m_page"), "m_categoryGroup");
        ClickObj(group == null ? null : h.Invoke(group, "getButtonAt", index));
    }

    /// <summary>Scripted runs: the open category's first editable item, preferring one with position presets, clicked.</summary>
    void PickEditable()
    {
        var h = createDialog.Host;
        var content = CurrentContent();
        var dp = content == null ? null : h.Get(h.Get(content, "m_editItemList"), "dataProvider");
        int n = dp == null ? 0 : (int)Ms2.Avm2.Rt.ToNumber(h.Get(dp, "length"));
        int pick = -1, fallback = -1;
        for (int i = 0; i < n; i++)
        {
            var row = h.Get(dp, i.ToString());
            if (!Ms2.Avm2.Rt.ToBoolean(h.Get(row, "editable"))) continue;
            int id = int.TryParse(Ms2.Avm2.Rt.ToString(h.Get(row, "itemid")), out int v) ? v : 0;
            if (fallback < 0) fallback = i;
            if (create.Look.Customize(id).Positions.Count > 0) { pick = i; break; }
        }
        if (pick < 0) pick = fallback;
        GD.Print($"create shots: editable row {pick} of {n}");
        if (pick >= 0) ClickObj(Renderer("m_editItemList", pick));
    }

    void Save(string name)
    {
        DirAccess.MakeDirRecursiveAbsolute(shotDir);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(shotDir, name));
        GD.Print("saved " + name);
    }
}
