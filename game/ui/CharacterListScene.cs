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
/// Selecting another character goes through OnSelectCharacter -> EnterSelectScene -> EnterSelectMode, which loads the
/// select map of the new character's job; the preview follows once the map is in (CSceneStateMulti vfunc_1 ->
/// PreviewSelectedCharacter). Here every selection change reloads the scene.
/// Start / Enter / a double click start the game (OnGameStart) or begin character creation on an empty slot (OnCreatePC).
/// Args after `--`: --shots DIR renders shots and quits.
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
    int frames;

    /// <summary>The list is built and takes input (no lobby change under way).</summary>
    public bool ListReady => logic != null && logic.MouseEnabled;

    public override void _EnterTree() { base._EnterTree(); LobbyLayout.UseWindowPixels(this); UiBridge.ToHost += OnHost; }
    public override void _ExitTree() { UiBridge.ToHost -= OnHost; base._ExitTree(); }

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
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--shots") shotDir = args[i + 1];
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

        var window = GetViewport().GetVisibleRect().Size;    // window pixels (LobbyLayout.UseWindowPixels)
        float u = UiScale.For(window);
        var movie = new UiMovie(assets, UICharacterListDialogContract.Movie);
        root = movie.BuildRoot(u);
        root.Scale = new Vector2(u, u);
        UiWidgets.ApplyInspector(root, UICharacterListDialogContract.Inspector);
        LobbyLayout.AlignCharacterList(root, UiScale.MovieStage(window), u);
        layer.AddChild(root);

        logic = new CharacterListLogic(movie, root);
        // CharacterListDialog.buildCharacterList -> popup: the dialog and m_title play their popup
        logic.BuildCharacterList(Flow.Session.List(), Flow.Session.SelectedIndex, 6, data, assets.ImageFile);
        (LobbyLayout.Dialog(root).Node as MovieClipNode)?.GotoLabel("popup");
        root.FindClip(UICharacterListDialogContract.Instances.M_title)?.GotoLabel("popup");
        root.FindClip(UICharacterListDialogContract.Instances.M_content)?.SetNamedText("m_serverName", "Bigleaf  CH.1");
        root.FindClip(UICharacterListDialogContract.Instances.M_content)?.SetNamedText("m_regionName", "");
        root.FindClip(UICharacterListDialogContract.Instances.M_title)?.SetText(assets.Str("$s_charselect_title"));
        // region and event extras the host attaches on demand (attachHealthGameCN, attachBurningLevelupDescBtn, ...)
        foreach (var name in new[] { UICharacterListDialogContract.Instances.M_healthGameCN, UICharacterListDialogContract.Instances.M_characterCraeteShutdownCN, UICharacterListDialogContract.Instances.M_burningEventDescBtn })
            if (root.FindClip(name) is { } extra) extra.Visible = false;
        mouse = new UiWidgets.MouseRouter(root);
        PreviewSelected();
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
        // OnSelectCharacter(charSN): m_188 = the character of that id (none for an empty slot); a change turns the dialog's
        // input off (FUN_1412761e0(m_30, 0), m_180) and posts 0x4c8 -> EnterSelectScene -> EnterSelectMode, which loads the
        // select map of the new selection's job group again (WorldService::LoadMap only skips a map still loading), and
        // the preview comes with the loaded map. Here the scene reloads.
        var list = Flow.Session.List();
        string sn = a.Length > 0 ? a[0] as string : null;
        int index = list.FindIndex(c => c.User.CharacterId.ToString() == sn);
        Flow.Session.SelectedIndex = index >= 0 ? index : logic.SelectedIndex;
        logic.MouseEnabled = false;
        CallDeferred(MethodName.Reload);
    }

    void Reload()
    {
        uiPacks?.Dispose();
        GetTree().ReloadCurrentScene();
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

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (shotDir == null || logic == null) return;
        frames++;
        if (frames == 60) Save($"charselect_{Flow.Session.SelectedIndex}.png");
        if (frames == 61)
        {
            // each selection loads its lobby map again: 0 -> 1 (another job's map), 1 -> 3 (the first empty slot), then quit
            if (Flow.Session.SelectedIndex == 0) logic.Select(1, true);
            else if (Flow.Session.SelectedIndex == 1) logic.Select(3, true);
            else GetTree().Quit();
        }
    }

    void Save(string name)
    {
        DirAccess.MakeDirRecursiveAbsolute(shotDir);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(shotDir, name));
        GD.Print("saved " + name);
    }
}
