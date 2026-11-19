using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2Godot.Look;
using Ms2Godot.Net;
using Ms2Godot.Ui.Contracts;
using Ms2Godot.Ui.Create;
using Ms2.Ui;

namespace Ms2Godot.Ui;

/// <summary>
/// Character select as CSceneStateMulti runs it in select mode, without a server. The field is the lobby map of the
/// selected character's job (EnterSelectMode: GetSelectMapForSelectedJob, LobbyFieldTable::GetSelectMap of the
/// character's job group, job 1's map with no character), the camera its Camera0 entity, and the selected character
/// stands at its spawn point dressed in its equips (UICharacterListDialog::PreviewCharacter -> FUN_14107b7e0).
/// uicharacterlistdialog runs on top as the client's own movie with UICharacterListDialog's C++ side
/// (<see cref="CharacterListController"/>), filled from the game flow's list (the stub list when the scene runs alone).
/// Selecting another character goes through OnSelectCharacter -> EnterSelectScene -> EnterSelectMode -> LoadMap of the
/// new character's select map; the preview follows once the map is in (CSceneStateMulti vfunc_1 ->
/// PreviewSelectedCharacter). As the restored client shows it: on the same map the field stays and only the character
/// is swapped (a hard cut); on another map the old frame crossfades into the new field; the selected row again does nothing.
/// The movie's Start, a double click and Enter (vfunc_13 -> onEnterKey) start the game (OnGameStart) or begin character
/// creation on an empty slot (OnCreatePC). The list takes no keys (CharacterList.configUI preventKeyInput).
/// Args after `--`: --shots DIR renders shots and quits; with --resize WxH the window is resized after the first shot and
/// a second one taken; --limit N sets the account's character limit (CharMaxCount), 6 without it or a live session.
/// </summary>
public partial class CharacterListScene : LobbyFieldScene
{
    PackSet uiPacks;
    CreateTables tables;
    UiAssets assets;
    LobbyCharacterData data;
    CanvasLayer layer;
    MovieDialog dialog;
    CharacterListController controller;
    /// <summary>The dialog of the scene a map change left; the new one carries its state (the client keeps the dialog open).</summary>
    static CharacterListController carried;
    /// <summary>GameOption m_nCharSN as the client keeps it: the last previewed character (SelectCharacter, :1263).</summary>
    static string lastSn = "";
    /// <summary>The character previewed in this scene, null for none.</summary>
    string previewSn;
    int? limitArg;
    ModelBuilder builder;
    int mapId;
    string shotDir;
    Vector2I? resizeTo;
    bool enterShots;
    int frames;

    /// <summary>The list is built and takes input (no lobby change under way).</summary>
    public bool ListReady => controller != null && controller.InputOn && !controller.Selecting;

    /// <summary>The row the movie's list has selected.</summary>
    public int ListIndex => controller?.ListIndex ?? -1;

    /// <summary>A click on row <paramref name="row"/>: the list's selectedIndex set as CLIK's itemClick sets it (INDEX_CHANGE -> onChangeCharacter).</summary>
    public void SelectRow(int row)
    {
        if (controller?.Host is not { } h || h.Get(h.Entity, "m_charlist") is not { } list) return;
        h.Set(list, "selectedIndex", row);
    }

    /// <summary>The movie's Start button (onClickStart), as a click on it.</summary>
    public void ClickStart() { if (controller?.Host is { } h) h.Invoke(h.Entity, "onClickStart", (object)null); }

    public override void _EnterTree()
    {
        base._EnterTree();
        LobbyLayout.UseWindowPixels(this);
        Net.ProfileImageService.Images.Ready += ProfileImageReady;
    }

    public override void _ExitTree()
    {
        if (layer != null) GetViewport().SizeChanged -= QueueRelayout;
        Net.ProfileImageService.Images.Ready -= ProfileImageReady;
        base._ExitTree();
    }

    /// <summary>UICharacterListDialog's UIUGCEventDispatcher listener (<see cref="CharacterListController.OnProfileImage"/>):
    /// the character's row gets GetProfileImagePath(key, 1).</summary>
    void ProfileImageReady(long characterId, string key) =>
        controller?.OnProfileImage(characterId, Net.ProfileImageService.Images.ProfileImagePath(key, true));

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
            if (args[i] == "--limit" && int.TryParse(args[i + 1], out int lim)) limitArg = lim;
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
        PreviewSelected();
        if (!popup) BuildList(reselect, false);
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

    /// <summary>The dialog gets the new stage and aligns again (UIDialog::vfunc_43 -> alignment).</summary>
    void Relayout()
    {
        relayoutQueued = false;
        if (!IsInsideTree() || dialog == null) return;
        dialog.Relayout(GetViewport().GetVisibleRect().Size);
    }

    /// <summary>
    /// uicharacterlistdialog for the current window: the controller filled from the flow's list (FUN_1418f42c0 per
    /// character), the movie loaded (OnLoadComplete -> initData, RefreshLobby -> buildCharacterList -> popup). With
    /// <paramref name="reselect"/> it carries the dialog of the scene a map change left; without <paramref name="popupPlays"/>
    /// the movie is stepped to the popup's end (the dialog was already open).
    /// </summary>
    void BuildList(bool reselect, bool popupPlays)
    {
        controller = NewController(assets, uiPacks ?? packs, data, Flow.Session.List(), limitArg);
        if (reselect && carried != null) controller.CarryFrom(carried);
        else controller.SavedSn = lastSn;
        carried = null;
        controller.Previewed(previewSn);
        if (previewSn != null) lastSn = previewSn;
        controller.SelectCharacter += OnSelectCharacter;
        controller.GameStart += sn => UiBridge.Raise(UICharacterListDialogContract.Movie, UICharacterListDialogContract.ToHost.OnGameStart, sn);
        controller.CreateCharacter += () => UiBridge.Raise(UICharacterListDialogContract.Movie, UICharacterListDialogContract.ToHost.OnCreatePC);
        controller.DeleteCharacter += () => UiBridge.Raise(UICharacterListDialogContract.Movie, UICharacterListDialogContract.ToHost.OnDeletePC);
        controller.ServerList += () => UiBridge.Raise(UICharacterListDialogContract.Movie, UICharacterListDialogContract.ToHost.OnSceneLobbyServer);
        controller.Swapped += (a, b) =>
        {
            var list = Flow.Session.List();
            if (a < list.Count && b < list.Count) (list[a], list[b]) = (list[b], list[a]);
        };
        dialog = OpenList(assets, controller, GetViewport().GetVisibleRect().Size);
        layer.AddChild(dialog);
        if (!popupPlays) dialog.Step(PopupFrames);
    }

    /// <summary>Frames stepped to put a dialog that was already open at the end of its popup (the popup labels are shorter).</summary>
    public const int PopupFrames = 60;

    /// <summary>
    /// A character list controller over the flow's characters: the account's limit from CharMaxCount (the live session's,
    /// else <paramref name="limit"/>, else 6), the server line (FUN_141900750) and the DeleteAccount feature.
    /// </summary>
    public static CharacterListController NewController(UiAssets assets, PackSet features, LobbyCharacterData data, IEnumerable<CharacterEntry> characters, int? limit = null)
    {
        var info = Flow.Session.ServerInfo(assets);
        var c = new CharacterListController
        {
            CharacterLimit = Net.LiveSession.Instance is { CharacterLimit: > 0 } live ? live.CharacterLimit : limit ?? 6,
            Server = (info.Name, info.IconPath),
            // UICharacterListDialog::OnLoadComplete: feature 0x112; feature.xml has DeleteAccount at KR level 100, over the
            // Live server level (feature_setting.xml Live KR 78)
            DeleteAccountEnabled = LobbyFeatures.IsEnabled(features, "DeleteAccount"),
        };
        foreach (var e in characters) c.Characters.Add(data.Row(e));
        return c;
    }

    /// <summary>The movie for <paramref name="controller"/>, with the translated UI code when it is built.</summary>
    public static MovieDialog OpenList(UiAssets assets, CharacterListController controller, Vector2 window)
    {
        if (!UiCode.Load(Settings.DataDir)) GD.PushWarning("UI code is not built for this data: the character list runs interpreted");
        return new MovieDialog(assets, controller, new UiModel(GameTables.Shared(assets.Packs, assets.Language)), window);
    }

    /// <summary>
    /// UICharacterListDialog::PreviewCharacter: the selected character at the spawn point (FUN_14107b7e0: the gender's
    /// body, its skin colours and its items), or no preview without one.
    /// </summary>
    void PreviewSelected()
    {
        var e = Selected();
        previewSn = e?.User.CharacterId.ToString();
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
            // the job's select pose (CHARACTER_SELECT_BORE, then CHARACTER_SELECT_BORE_IDLE for good), else the idle
            var pose = SelectPose.For(packs, e.User.JobCode, g);
            if (pose != null && SelectPosePlayer.AddSequences(pose, anim, builder, md, dir))
            {
                ShowPreview(visual, null);
                visual.AddChild(new SelectPosePlayer(pose, anim, visual.GetNode<Node3D>("Model"), builder, packs));
            }
            else ShowPreview(visual, anim);
        }
        catch (Exception ex) { GD.PushWarning("character select preview: " + ex.Message); }
    }

    /// <summary>
    /// The controller let an OnSelectCharacter through (the dialog's input is off, m_180 set): 0x4c8 is posted ->
    /// EnterSelectScene -> EnterSelectMode -> WorldService::LoadMap(select map, 1, 0).
    /// </summary>
    void OnSelectCharacter(string sn)
    {
        UiBridge.Raise(UICharacterListDialogContract.Movie, UICharacterListDialogContract.ToHost.OnSelectCharacter, sn);
        Flow.Session.SelectedIndex = controller.ListIndex;
        carried = controller;
        // WorldService::LoadMap returns early only while that map is still loading (m_state 2; a loaded map is at 4,
        // HandleWorldLoadedMessage result 4), so the select map already up is loaded again too, and the preview comes
        // back through the world-loaded callback (CSceneStateMulti::vfunc_1 -> PreviewSelectedCharacter). The restored
        // client's captures agree: on the same job the old character crossfades into the new one over about 0.4 s,
        // starting about 0.6 s after the click, as on a job change (front-end clips 1_b, eagle e2/e3).
        CrossfadeReload();
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
        var image = await CaptureField();
        if (!IsInsideTree()) return;
        fadeFrom = image != null ? ImageTexture.CreateFromImage(image) : null;
        reselecting = true;
        Reload();
    }

    /// <summary>
    /// The field alone as the camera sees it: the dialog stays on screen over the crossfade, so the kept frame must not
    /// hold it (a frame with the dialog showed through the new, translucent dialog: the old row's swap arrows stayed as
    /// a faint pair). One render of the same world through a copy of the camera, in a viewport the size of the window.
    /// </summary>
    async Task<Image> CaptureField()
    {
        var vp = GetViewport();
        if (vp.GetCamera3D() is not { } cam) return null;
        var sub = new SubViewport
        {
            Size = (Vector2I)vp.GetTexture().GetSize(), World3D = vp.World3D, RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
            Msaa3D = vp.Msaa3D, ScreenSpaceAA = vp.ScreenSpaceAA, UseTaa = vp.UseTaa, UseDebanding = vp.UseDebanding,
            Scaling3DMode = vp.Scaling3DMode, Scaling3DScale = vp.Scaling3DScale, PositionalShadowAtlasSize = vp.PositionalShadowAtlasSize,
        };
        var copy = new Camera3D
        {
            Projection = cam.Projection, Fov = cam.Fov, Size = cam.Size, Near = cam.Near, Far = cam.Far, KeepAspect = cam.KeepAspect,
            HOffset = cam.HOffset, VOffset = cam.VOffset, CullMask = cam.CullMask, Environment = cam.Environment, Attributes = cam.Attributes,
            Compositor = cam.Compositor,   // the bloom and its gamma -> linear conversion (the buffer holds gamma values)
        };
        sub.AddChild(copy);
        // the field backdrop (FieldBackdrop) is a canvas layer of the window, which the environment draws as the 3D
        // background; the copy viewport has its own canvas, so it gets a copy of those layers (without, the sky was the empty clear colour)
        if ((cam.Environment ?? vp.World3D?.Environment) is { BackgroundMode: Godot.Environment.BGMode.Canvas } env)
            foreach (var node in vp.FindChildren("*", nameof(CanvasLayer), true, false))
                if (node is CanvasLayer { Visible: true } bg && bg.Layer <= env.BackgroundCanvasMaxLayer && bg.GetViewport() == vp)
                    sub.AddChild(bg.Duplicate());
        AddChild(sub);
        copy.GlobalTransform = cam.GlobalTransform;
        copy.Current = true;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = sub.GetTexture().GetImage();
        sub.QueueFree();
        return image;
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
    /// The scene's fade-in ends with the crossfade (or at once without one): UISceneLobbySelect::vfunc_58 -> the dialog's
    /// FUN_1418f1fd0 turns its input on.
    /// </summary>
    async void StartAfterFirstFrame(bool popup, TextureRect fade)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if (!IsInsideTree()) return;
        if (popup) BuildList(false, true);
        if (fade == null) { controller.FadeInComplete(); return; }
        var tween = CreateTween();
        tween.TweenProperty(fade, "modulate:a", 0f, CrossfadeSeconds);
        tween.TweenCallback(Callable.From(fade.GetParent().QueueFree));
        tween.TweenCallback(Callable.From(() => controller?.FadeInComplete()));
        fadeStart = Time.GetTicksMsec() / 1000.0;
    }

    /// <summary>UICharacterListDialog::vfunc_13 (uicharacterlistdialog.cpp:787-800): Enter -> onEnterKey, ahead of the movie.</summary>
    public override void _Input(InputEvent e)
    {
        if (controller == null) return;
        // the answer may change the scene at once (OnCreatePC / OnGameStart): this one is then out of the tree
        if (e is InputEventKey { Pressed: true, Echo: false } k && k.Keycode is Key.Enter or Key.KpEnter && controller.EnterKey() && IsInsideTree())
            GetViewport().SetInputAsHandled();
    }

    /// <summary>Shots step, kept over the reloads of a map change.</summary>
    static int shotStep;
    bool midSaved;
    double enterT, enterNext;

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (shotDir == null || controller == null) return;
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
            if (step == 0) SelectRow(1);
            else if (step == 1) SelectRow(3);
            else if (step == 2) { GD.Print($"same map: scene {GetInstanceId()}"); SelectRow(4); }
            else GetTree().Quit();
        }
        else if (frames == 120 && shotStep == 3)
        {
            GD.Print($"same map: scene {GetInstanceId()} after the select (no reload)");
            Save($"charselect_3_{Flow.Session.SelectedIndex}.png");
            SelectRow(4);
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
