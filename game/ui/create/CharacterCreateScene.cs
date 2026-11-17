using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2Godot.Look;
using Ms2Godot.Ui.Contracts;

namespace Ms2Godot.Ui.Create;

/// <summary>
/// Character creation as CSceneStateMulti runs it, without a server.
/// Job pick: the lobby select field with the character list dimmed behind uicharactercreateselectdialog.
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
    public static void CreateFailed(string message) => current?.custom?.ShowCreateError(message);

    static CharacterCreateScene current;
    public override void _EnterTree() { base._EnterTree(); LobbyLayout.UseWindowPixels(this); current = this; }
    public override void _ExitTree() { if (current == this) current = null; base._ExitTree(); }

    PackSet uiPacks;
    CreateTables tables;
    UiAssets assets;
    CanvasLayer layer;
    MovieClipNode selectRoot, createRoot, listRoot;
    JobSelectLogic jobs;
    CharacterCreateLogic custom;
    UiWidgets.MouseRouter mouse, listMouse;
    ModelBuilder builder;
    FieldMap map;
    string shotDir;
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
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--shots") shotDir = args[i + 1];
        if (string.IsNullOrEmpty(Settings.DataDir)) return defaultName;
        uiPacks = new PackSet(Settings.DataDir);
        tables = new CreateTables(uiPacks);
        if (job == 0) job = tables.StartJob(rng);
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
    }

    /// <summary>The window in pixels (LobbyLayout.UseWindowPixels), its UI scale and movie stage (LobbyLayout).</summary>
    (Vector2 Window, float U, Vector2 Stage) Screen()
    {
        var window = GetViewport().GetVisibleRect().Size;
        return (window, UiScale.For(window), UiScale.MovieStage(window));
    }

    // ---- job pick ----

    void BuildSelect()
    {
        var (_, u, stage) = Screen();
        // the character list stays behind, dimmed by the job page's m_fullBg
        try
        {
            var listMovie = new UiMovie(assets, UICharacterListDialogContract.Movie);
            listRoot = listMovie.BuildRoot(u);
            listRoot.Scale = new Vector2(u, u);
            UiWidgets.ApplyInspector(listRoot, UICharacterListDialogContract.Inspector);
            LobbyLayout.AlignCharacterList(listRoot, stage, u);
            (listRoot.Children.First(c => c.Node is MovieClipNode).Node as MovieClipNode)?.GotoLabel("popup");
            layer.AddChild(listRoot);
            var list = new CharacterListLogic(listMovie, listRoot);
            list.BuildCharacterList(Array.Empty<Ms2.Net.Packets.CharacterEntry>(), 0, 6, new LobbyCharacterData(packs, assets.Language), assets.ImageFile);
            listRoot.FindClip(UICharacterListDialogContract.Instances.M_content)?.SetNamedText("m_serverName", "Bigleaf  CH.1");
            listRoot.FindClip(UICharacterListDialogContract.Instances.M_title)?.SetText(assets.Str("$s_charselect_title"));
            foreach (var name in new[] { UICharacterListDialogContract.Instances.M_healthGameCN, UICharacterListDialogContract.Instances.M_characterCraeteShutdownCN, UICharacterListDialogContract.Instances.M_burningEventDescBtn })
                if (listRoot.FindClip(name) is { } extra) extra.Visible = false;
        }
        catch (Exception ex) { GD.PushWarning("character list: " + ex.Message); }

        var movie = new UiMovie(assets, UICharacterCreateSelectDialogContract.Movie);
        selectRoot = movie.BuildRoot(u);
        // CharacterCreateSelect.alignment: applyScale -> LobbyDialog.applyScale (getLobbyRatioY); the dialog stays at the
        // stage origin, its content is centred inside it (JobSelectLogic.Align)
        float r = LobbyLayout.LobbyRatio(stage);
        LobbyLayout.Place(selectRoot, Vector2.Zero, u, r);
        UiWidgets.ApplyInspector(selectRoot, UICharacterCreateSelectDialogContract.Inspector);
        layer.AddChild(selectRoot);
        jobs = new JobSelectLogic(movie, selectRoot, tables, uiPacks);
        jobs.InitData(job);
        jobs.Align(stage, r);
        jobs.Create += j => { job = j; gender = rng.Next(2); SwitchTo(Page.Create); };
        jobs.Prev += () => { GD.Print("job pick: back to the character list"); Cancelled?.Invoke(); };
        mouse = new UiWidgets.MouseRouter(selectRoot);
    }

    // ---- create ----

    void BuildCreate()
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
        var look = new CreateLook(packs, new ColorPalettes(packs));
        custom = new CharacterCreateLogic(movie, createRoot, look);
        custom.LookChanged += RebuildPreview;
        custom.Back += () => SwitchTo(Page.Select);
        custom.Created += (packet, bytes) => CharacterCreated?.Invoke(packet, bytes);
        custom.InitData(gender, job, "Bigleaf  CH.1");
        RebuildPreview();
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
        if (e is InputEventKey { Pressed: true, Echo: false } k && k.Keycode is Key.Enter or Key.KpEnter)
        {
            if (jobs != null) jobs.ClickCreate();
            else custom?.OnCreate();
        }
    }

    // ---- scripted shots ----

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (shotDir == null) return;
        frame++;
        if (page == Page.Select)
        {
            if (frame == 45) Save("create_jobselect.png");
            if (frame == 46) jobs.ChangeTab(0, true);
            if (frame == 75) Save("create_jobselect_knight.png");
            if (frame == 160) { Save("create_jobselect_video.png"); GD.Print($"job video {jobs.Player?.MovieName}: {jobs.Player?.Video?.Width}x{jobs.Player?.Video?.Height} at {jobs.Player?.Video?.FrameRate} fps, t {jobs.Player?.Time:F2} s"); }
            if (frame == 161) jobs.ClickCreate();
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
