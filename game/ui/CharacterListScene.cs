using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2Godot.Look;
using Ms2Godot.Net;
using Ms2Godot.Ui.Contracts;
using Ms2Godot.Ui.Create;

namespace Ms2Godot.Ui;

/// <summary>
/// Character select as CSceneStateMulti runs it in select mode, without a server. The field is the lobby map of the
/// selected character's job (EnterSelectMode: GetSelectMapForSelectedJob, LobbyFieldTable::GetSelectMap of the
/// character's job group, job 1's map with no character), the camera its Camera0 entity, and the selected character
/// stands at its spawn point dressed in its equips (UICharacterListDialog::PreviewCharacter -> FUN_14107b7e0).
/// uicharacterlistdialog (built from the player's own Gfx.m2d) runs on top, filled from the game flow's list (the stub
/// list when the scene runs alone).
/// Selecting another character goes through OnSelectCharacter -> EnterSelectScene -> EnterSelectMode -> LoadMap of the
/// new character's select map; the preview follows once the map is in (CSceneStateMulti vfunc_1 ->
/// PreviewSelectedCharacter). As the restored client shows it: on the same map the field stays and only the character
/// is swapped (a hard cut); on another map the old frame crossfades into the new field; the selected row again does nothing.
/// Start / Enter / a double click start the game (OnGameStart) or begin character creation on an empty slot (OnCreatePC).
/// Args after `--`: --shots DIR renders shots and quits; with --resize WxH the window is resized after the first shot and
/// a second one taken.
/// </summary>
public partial class CharacterListScene : LobbyFieldScene
{
    PackSet uiPacks;
    CreateTables tables;
    UiAssets assets;
    LobbyCharacterData data;
    CanvasLayer layer;
    MovieClipNode root;
    CharacterListLogic logic;
    UiWidgets.MouseRouter mouse;
    ModelBuilder builder;
    int mapId;
    string shotDir;
    Vector2I? resizeTo;
    bool enterShots;
    int frames;

    /// <summary>The list is built and takes input (no lobby change under way).</summary>
    public bool ListReady => logic != null && logic.MouseEnabled;

    public override void _EnterTree() { base._EnterTree(); LobbyLayout.UseWindowPixels(this); UiBridge.ToHost += OnHost; }
    public override void _ExitTree()
    {
        UiBridge.ToHost -= OnHost;
        if (layer != null) GetViewport().SizeChanged -= QueueRelayout;
        base._ExitTree();
    }

    static CharacterEntry Selected()
    {
        var list = Flow.Session.List();
        int i = Flow.Session.SelectedIndex;
        return i >= 0 && i < list.Count ? list[i] : null;
    }

    /// <summary>GetSelectMapForSelectedJob: the selected character's job group (m_3c), 0 without one.</summary>
    static int SelectedJobGroup() => Selected()?.User.JobCode ?? 0;

    protected override string FieldNameFor(string defaultName)
    {
        var args = OS.GetCmdlineUserArgs();
        enterShots = args.Contains("--enter");
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--shots") shotDir = args[i + 1];
            if (args[i] == "--resize" && args[i + 1].Split('x') is [var w, var h] && int.TryParse(w, out int rw) && int.TryParse(h, out int rh)) resizeTo = new Vector2I(rw, rh);
        }
        if (string.IsNullOrEmpty(Settings.DataDir)) return defaultName;
        uiPacks = new PackSet(Settings.DataDir);
        tables = new CreateTables(uiPacks);
        mapId = tables.SelectMap(SelectedJobGroup());
        return tables.XBlockOf(mapId) ?? defaultName;
    }

    protected override void OnFieldBuilt(ModelBuilder builder, FieldMap map, string fieldName)
    {
        this.builder = builder;
        PlaceLobby(map);
        GD.Print($"character select: field {fieldName} (map {mapId})");
        assets = new UiAssets(packs);
        data = new LobbyCharacterData(packs, assets.Language);
        StubCharacterList.Dress(packs, Flow.Session.List());
        layer = new CanvasLayer { Name = "UI" };
        AddChild(layer);

        // the dialog opens with its popup (CharacterListDialog.buildCharacterList -> popup) when the select scene is
        // entered; a map change keeps the open dialog (only the field is loaded again)
        bool reselect = reselecting;
        reselecting = false;
        bool popup = !reselect && (shotDir == null || enterShots);
        BuildList(popup);
        PreviewSelected();
        var fade = AddCrossfade(reselect ? null : EnterFrom);
        EnterFrom = null;
        StartAfterFirstFrame(popup, fade);
        // the client sizes every movie to the back buffer: on a window resize the open dialogs align again
        // (UIDialog::vfunc_43 -> alignment), as on the login screen
        GetViewport().SizeChanged += QueueRelayout;
    }

    bool relayoutQueued;
    void QueueRelayout()
    {
        if (relayoutQueued) return;
        relayoutQueued = true;
        CallDeferred(MethodName.Relayout);
    }

    /// <summary>The list built again at the new window's UI scale, on the same selection and mouse state.</summary>
    void Relayout()
    {
        relayoutQueued = false;
        if (!IsInsideTree() || root == null) return;
        bool mouseOn = logic.MouseEnabled;
        layer.RemoveChild(root);
        root.QueueFree();
        BuildList(false);
        logic.MouseEnabled = mouseOn;
    }

    /// <summary>
    /// uicharacterlistdialog for the current window, filled from the flow's list (CharacterListDialog.buildCharacterList).
    /// With <paramref name="popup"/> it plays its popup; otherwise it is put at the popup's end.
    /// </summary>
    void BuildList(bool popup)
    {
        var window = GetViewport().GetVisibleRect().Size;    // window pixels (LobbyLayout.UseWindowPixels)
        float u = UiScale.For(window);
        var movie = new UiMovie(assets, UICharacterListDialogContract.Movie);
        root = movie.BuildRoot(u);
        root.Scale = new Vector2(u, u);
        UiWidgets.ApplyInspector(root, UICharacterListDialogContract.Inspector);
        LobbyLayout.AlignCharacterList(root, UiScale.MovieStage(window), u);
        layer.AddChild(root);

        logic = new CharacterListLogic(movie, root);
        // CharacterListDialog.buildCharacterList -> popup: LobbyDialog.popup gotoAndPlay("popup") on the dialog,
        // m_title.gotoAndPlay("popup"), btnsPopup(m_btns)
        logic.BuildCharacterList(Flow.Session.List(), Flow.Session.SelectedIndex, 6, data, assets.ImageFile);
        logic.ShowDialog(assets, uiPacks ?? packs, popup);
        mouse = new UiWidgets.MouseRouter(root);

    }

    /// <summary>
    /// UICharacterListDialog::PreviewCharacter: the selected character at the spawn point (FUN_14107b7e0: the gender's
    /// body, its skin colours and its items), or no preview without one.
    /// </summary>
    void PreviewSelected()
    {
        var e = Selected();
        if (e == null) { ClearPreview(); return; }
        try
        {
            int g = e.User.Gender;
            string dir = g == 0 ? "male" : "female";
            var md = builder.LoadModel($"{dir}/{(g == 0 ? "m_body.nif" : "f_body.nif")}");
            var visual = builder.Build(md, g == 0 ? "Male" : "Female");
            var anim = visual.GetNode<AnimationPlayer>("Model/AnimationPlayer");
            anim.GetAnimationLibrary("").AddAnimation("idle", ModelBuilder.BuildAnimation(builder.LoadKf($"{dir}/idle_a.kf"), md));
            var equip = new EquipApplier(packs, builder) { Palettes = new ColorPalettes(packs) };
            equip.Apply(visual, md, g, e.Equips, e.User.Skin.Primary, e.User.Skin.Secondary);
            ShowPreview(visual, anim);
        }
        catch (Exception ex) { GD.PushWarning("character select preview: " + ex.Message); }
    }

    void OnHost(string movieName, string method, object[] a)
    {
        if (movieName != UICharacterListDialogContract.Movie || method != UICharacterListDialogContract.ToHost.OnSelectCharacter) return;
        // UICharacterListDialog OnSelectCharacter(charSN): m_188 = the character of that id (none for an empty slot). Only
        // `spPrev != m_188 || (m_188 == 0 && m_220 == 0)` goes on: the selected character clicked again does nothing.
        // Otherwise the dialog's input goes off (FUN_1412761e0(m_30, 0), m_180 = 1, back on in PreviewCharacter) and 0x4c8
        // is posted -> EnterSelectScene -> EnterSelectMode -> WorldService::LoadMap(select map, 1, 0).
        var list = Flow.Session.List();
        string sn = a.Length > 0 ? a[0] as string : null;
        int index = list.FindIndex(c => c.User.CharacterId.ToString() == sn);
        var prev = Selected();
        var next = index >= 0 ? list[index] : null;
        if (next != null && ReferenceEquals(prev, next)) return;
        Flow.Session.SelectedIndex = index >= 0 ? index : logic.SelectedIndex;
        logic.MouseEnabled = false;
        // The restored client (front-end captures, clips 1_b/1_d/1_g): on the select map already up the field is not loaded
        // again and keeps animating, and the new character appears in one frame (a hard cut); on another job's map the old
        // map and character crossfade into the new ones, with no black frame and no loading screen.
        int nextMap = tables?.SelectMap(SelectedJobGroup()) ?? -1;
        if (tables != null && nextMap == mapId) CallDeferred(MethodName.SwapPreview);
        else CrossfadeReload();
    }


    /// <summary>The same select map: the field stays, the preview is replaced (PreviewSelectedCharacter) and input comes back.</summary>
    void SwapPreview()
    {
        PreviewSelected();
        logic.MouseEnabled = true;
    }

    /// <summary>The frame on screen when the map changes; the next scene fades it out over its own first frames.</summary>
    static ImageTexture fadeFrom;
    static bool reselecting;
    /// <summary>The login backdrop the select scene is entered from (set by the flow); the new field fades in over it.</summary>
    public static ImageTexture EnterFrom;
    /// <summary>Crossfade length, from the restored client's 10 fps strip of a job change (about 0.4-0.5 s).</summary>
    const double CrossfadeSeconds = 0.45;

    /// <summary>
    /// Another map: the frame with the new highlight is kept (rendered once more, so the moved highlight is in it), the scene
    /// is loaded again on the new map, and the kept frame fades out over it.
    /// </summary>
    async void CrossfadeReload()
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if (!IsInsideTree()) return;
        fadeFrom = ImageTexture.CreateFromImage(GetViewport().GetTexture().GetImage());
        reselecting = true;
        Reload();
    }

    void Reload()
    {
        uiPacks?.Dispose();
        GetTree().ReloadCurrentScene();
    }

    double fadeStart = -1;
    /// <summary>
    /// The kept frame (the previous map, or the login backdrop) over the new field, under the dialog: the restored client
    /// crossfades the old map or the login background into the new map with no black frame.
    /// </summary>
    TextureRect AddCrossfade(ImageTexture enter)
    {
        var from = fadeFrom ?? enter;
        fadeFrom = null;
        if (from == null) return null;
        var top = new CanvasLayer { Name = "Crossfade", Layer = layer.Layer - 1 };
        var rect = new TextureRect
        {
            Texture = from, Size = GetViewport().GetVisibleRect().Size,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        top.AddChild(rect);
        AddChild(top);
        return rect;
    }

    /// <summary>
    /// The popup and the crossfade start once the new scene has been drawn: the frame that builds the field takes long,
    /// and its time would otherwise be taken off the start of both.
    /// </summary>
    async void StartAfterFirstFrame(bool popup, TextureRect fade)
    {
        if (!popup && fade == null) return;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if (!IsInsideTree()) return;
        if (popup) logic.PlayPopup();
        if (fade == null) return;
        var tween = CreateTween();
        tween.TweenProperty(fade, "modulate:a", 0f, CrossfadeSeconds);
        tween.TweenCallback(Callable.From(fade.GetParent().QueueFree));
        fadeStart = Time.GetTicksMsec() / 1000.0;
    }

    public override void _Input(InputEvent e)
    {
        if (logic == null) return;
        if (logic.MouseEnabled) mouse?.Handle(e);
        if (e is InputEventMouseButton mb) logic.OnMouse(mb);
        if (e is InputEventKey { Pressed: true, Echo: false } k && logic.MouseEnabled)
        {
            if (k.Keycode is Key.Enter or Key.KpEnter) logic.OnEnterKey();
            else if (k.Keycode == Key.Down) logic.Select(Math.Min(logic.SelectedIndex + 1, CharacterListLogic.MaxRow - 1), true);
            else if (k.Keycode == Key.Up) logic.Select(Math.Max(logic.SelectedIndex - 1, 0), true);
        }
    }

    /// <summary>Shots step, kept over the reloads of a map change.</summary>
    static int shotStep;
    bool midSaved;
    double enterT, enterNext;

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (shotDir == null || logic == null) return;
        frames++;
        if (!midSaved && fadeStart >= 0 && Time.GetTicksMsec() / 1000.0 - fadeStart >= 0.2)
        {
            midSaved = true;
            Save($"charselect_{shotStep}_crossfade.png");
        }
        if (enterShots)
        {
            // --enter: the popup, a shot every 133 ms (every 4th frame at 30 fps) from the first frame, then quit
            enterT += delta;
            if (enterT >= enterNext) { Save($"enter_{(int)Math.Round(enterNext * 1000):D4}ms.png"); enterNext += 0.133; }
            if (enterNext > 2.0) GetTree().Quit();
            return;
        }
        if (frames == 60) Save($"charselect_{shotStep}_{Flow.Session.SelectedIndex}.png");
        if (resizeTo != null)
        {
            // --resize WxH: the window resized after the scene is up, then one shot
            if (frames == 61) GetWindow().Size = resizeTo.Value;
            if (frames == 90) { Save($"charselect_resized_{resizeTo.Value.X}x{resizeTo.Value.Y}.png"); GetTree().Quit(); }
            return;
        }
        if (frames != 61 && frames != 120 && frames != 180) return;
        // 0 -> 1 (another job's map: crossfade), 1 -> 3 (the first empty slot, map 2: crossfade), 3 -> 4 (another empty
        // slot on the same map: no reload), 4 again (the selected row: nothing), then quit
        if (frames == 61)
        {
            int step = shotStep++;
            if (step == 0) logic.Select(1, true);
            else if (step == 1) logic.Select(3, true);
            else if (step == 2) { GD.Print($"same map: scene {GetInstanceId()}"); logic.Select(4, true); }
            else GetTree().Quit();
        }
        else if (frames == 120 && shotStep == 3)
        {
            GD.Print($"same map: scene {GetInstanceId()} after the select (no reload)");
            Save($"charselect_3_{Flow.Session.SelectedIndex}.png");
            logic.Select(4, true);
        }
        else if (frames == 180 && shotStep == 3) { Save("charselect_3_reclick.png"); GetTree().Quit(); }
    }

    void Save(string name)
    {
        DirAccess.MakeDirRecursiveAbsolute(shotDir);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(shotDir, name));
        GD.Print("saved " + name);
    }
}
