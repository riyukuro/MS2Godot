using System.Text.Json.Nodes;
using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2Godot.Ui;

/// <summary>
/// The in-field HUD over the field scene: top bar (uiupperhuddialog), minimap frame (uiminimapdialog), chat (uimessengerdialog),
/// HP/SP orb and quick slots (uimainplayerinfo), EXP bar (uiexpbar) and game menu (uigamemenudialog), built from the player's
/// own Gfx.m2d and fed from <see cref="StubHudData"/>. Chat: the input box echoes into the log locally.
/// Args after `--`: --hudshot DIR saves a shot after the field has loaded and quits; --field is passed to the field scene.
/// </summary>
public partial class HudScene : Node
{
    UiAssets assets;
    readonly StubHudData data = new();
    readonly List<string> chatLines = new();
    MovieClipNode chat; TextInputWidget chatInput; TextNode chatLog;
    /// <summary>FUN_1418dd0c0 + FUN_1418dd110 (uibuiltindialog.cpp): the two bottom bars' height, (DAT_1439f8ccc - 2) +
    /// (DAT_1439f8cd0 - 2) = 19 + 19 with feature 0xbd (the adventure level bar) on.</summary>
    const float BottomBars = ExpBarLift + AdventureBarLift;
    const float ExpBarLift = 21 - 2, AdventureBarLift = 21 - 2;
    string shotDir; int frames;
    float u; Vector2 stage;                 // UI scale and stage size in stage units (see UiScale)
    Player player; Control uiRoot;
    TargetFrame target;                     // UIUpperHpBar
    DamageText damageText;
    PlayerHpBar playerBar;
    DamageEffect damageEffect;              // UIDamageEffect, the low-HP screen effect

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--hudshot") shotDir = args[i + 1];
        var dataDir = Settings.DataDir;
        if (string.IsNullOrEmpty(dataDir)) { GD.PushError("Set MS2_DATA (the client's Data folder), or fill in local.cfg."); GetTree().Quit(2); return; }
        assets = new UiAssets(new PackSet(dataDir));

        xblock = World.FieldContents.PendingXBlock ?? ArgField() ?? "02000001_tw_tria";   // as Field picks it; the field scene consumes the move
        AddChild(GD.Load<PackedScene>("res://field.tscn").Instantiate());      // the field scene, untouched
        BuildHud();
        GetViewport().SizeChanged += () => { if (!relayoutQueued) { relayoutQueued = true; CallDeferred(MethodName.Relayout); } };
    }

    Window.ContentScaleModeEnum outerScaleMode;

    /// <summary>
    /// The field draws to the whole window, as the client's back buffer is the window's client area and every movie's
    /// viewport is that render target (GFxMovie::vfunc_6 / vfunc_7). So the project's 1280 x 720 canvas stretch (which
    /// letterboxes) is off while this scene runs, and the HUD lays itself out for the window instead (BuildHud).
    /// </summary>
    public override void _EnterTree()
    {
        outerScaleMode = GetWindow().ContentScaleMode;
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
    }

    public override void _ExitTree() => GetWindow().ContentScaleMode = outerScaleMode;

    CanvasLayer hudLayer; Node[] worldUi = System.Array.Empty<Node>();   // the HUD layer and the world-anchored UI layers
    bool relayoutQueued;

    /// <summary>
    /// The window was resized. The client resets the device on WM_SIZE (GFxMovieEventHandler: OnDeviceLost, then
    /// GFxService::vfunc_17 on restore), and GFxManager::ResizeMovies gives every movie the new render target size
    /// (GFxMovie::vfunc_7) and sends SceneResize, so each dialog aligns itself to the new stage. Here the HUD is built
    /// again at the new size; the chat log and the typed text are kept.
    /// </summary>
    void Relayout()
    {
        relayoutQueued = false;
        string typed = chatInput?.Edit.Text;
        hudLayer?.QueueFree();
        foreach (var n in worldUi) n.QueueFree();
        chat = null; chatInput = null; chatLog = null; chatThumb = null; target = null;
        mapImage = null; mapPlayer = null; mapArrow = null; mapClip = null;
        BuildHud();
        if (chatInput != null && typed != null) chatInput.Edit.Text = typed;
        if (chatLines.Count > 0) ShowChatLines();
    }

    /// <summary>
    /// Lays the HUD out for the window. The client draws its movies into the whole back buffer: every movie's viewport is the
    /// active render target's size (GFxMovie::vfunc_6 / vfunc_7, GFxManager::ResizeMovies), one pixel larger on every side,
    /// with no letterbox and no fixed design size; the dialogs align themselves on the stage that gives (UiScale).
    /// </summary>
    void BuildHud()
    {
        var layer = hudLayer = new CanvasLayer { Layer = 10 };
        AddChild(layer);
        uiRoot = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        uiRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(uiRoot);
        var window = GetViewport().GetVisibleRect().Size;
        u = UiScale.For(window); stage = UiScale.MovieStage(window);
        layer.Offset = UiScale.MovieOrigin;                                  // the movies' viewport (GFxMovie::vfunc_7)
        var constants = new ClientConstants(assets.Packs);
        AddChild(damageEffect = new DamageEffect(assets, constants, window));                             // UIDamageEffect
        damageEffect.Hp(data.Player.HpCurrent, data.Player.HpMax);
        var nameTag = new PlayerNameTag(assets, constants, data.Player.Name, window);                     // the own PC's UINameTag
        AddChild(nameTag);
        AddChild(playerBar = new PlayerHpBar(assets, constants, nameTag, window));                        // and its UIHpBar
        playerBar.Spawn(data.Player.HpCurrent, data.Player.HpMax);
        AddChild(damageText = new DamageText(assets, window));                // UIDamageText
        worldUi = new Node[] { damageEffect, nameTag, playerBar, damageText };

        NoticeTab(uiRoot);
        Minimap(uiRoot);
        NavigatorWidget(uiRoot);
        UpperHud(uiRoot);
        target = new TargetFrame(assets, uiRoot, u, stage);
        Chat(uiRoot);
        Quests(uiRoot);
        Orb(uiRoot);
        ExpBar(uiRoot);
        AdventureBar(uiRoot);
        GameMenu(uiRoot);
    }

    /// <summary>Places a movie at a position in stage units; <paramref name="factor"/> is a per-dialog size the client applies on top of the UI scale.</summary>
    MovieClipNode Place(Control parent, string movie, float factor, Vector2 stagePos)
    {
        var m = new UiMovie(assets, movie);
        float s = u * factor;
        var root = m.BuildRoot(s);
        root.Scale = new Vector2(s, s); root.Position = stagePos * u;
        var holder = new Node2D(); holder.AddChild(root); parent.AddChild(holder);
        return root;
    }

    /// <summary>
    /// ASDisplayObject::vfunc_2(mode, dx, dy) on the dialog instance <paramref name="body"/> in a built root: the dialog's
    /// registration point goes to the aligned spot, with <paramref name="size"/> its AS3 width and height (truncated).
    /// </summary>
    void AlignDialog(MovieClipNode root, MovieClipNode body, int mode, Vector2 size, float dx, float dy)
    {
        var reg = UiLayout.Align(mode, stage, new Vector2((int)size.X, (int)size.Y), dx, dy);
        root.Position = (reg - body.Position) * u;
    }

    /// <summary>The dialog instance a HUD movie's root timeline places (its first clip).</summary>
    static MovieClipNode DialogBody(MovieClipNode root) => root.Children.Select(c => c.Node).OfType<MovieClipNode>().FirstOrDefault();

    static void Hide(MovieClipNode root, params string[] names) { foreach (var n in names) root.HideNamed(n); }

    void UpperHud(Control ui)
    {
        var r = Place(ui, "uiupperhuddialog", 1f, Vector2.Zero);
        // the constructor hides the effects, version and game pad buttons; the client shows one of the game pad pair and the
        // keyboard/mouse mode pair, and no PC bang buttons outside PC bangs
        Hide(r, "m_searchInput", "m_version", "m_eventText", "m_searchDungeon", "m_pcBangButton", "m_pcBangButtonCn1", "m_pcBangButtonCn2", "m_effectMerat", "m_effectTokenPocket",
            "m_effectHotTime", "m_gamePadOn", "m_mouseMode");
        var bar = r.FindClip("m_noticeButton");
        bar.FindClip("m_time")?.SetNamedText("timeText", DateTime.Now.ToString("HH:mm"));
        bar.FindClip("m_meso")?.SetNamedText("mesoText", data.Mesos.ToString());
        bar.FindClip("m_marat")?.SetNamedText("maratText", data.Merets.ToString());
        if (bar.FindClip("m_maidStatusButton") is { } maid) { maid.GotoLabel("up"); maid.HideNamed("badge"); }   // the count badge shows only with waiting maid messages
        ArrangeUpperHud(bar);
    }

    /// <summary>UpperHudDialog.arrangePos: each visible control follows the previous one, by its width (+10) or by a fixed step (+32).</summary>
    static void ArrangeUpperHud(MovieClipNode d)
    {
        MovieClipNode.Child prev = d.Direct("m_time");
        bool Visible(MovieClipNode.Child c) => c != null && c.Last >= 0 && c.Node.Visible;
        void AfterWidth(string name, int gap)
        {
            var c = d.Direct(name);
            if (!Visible(c) || prev == null) return;
            d.SetChildX(c, prev.Base.Origin.X + d.ChildWidth(prev, true) + gap); prev = c;
        }
        void After(string name, int gap, bool always = false)
        {
            var c = d.Direct(name);
            if (c == null || prev == null || (!always && !Visible(c))) return;
            d.SetChildX(c, prev.Base.Origin.X + gap); prev = c;
        }
        AfterWidth("m_meso", 10);
        AfterWidth("m_marat", 10);
        AfterWidth("m_tokenPocketButton", 10);
        After("m_maidStatusButton", 32, true);
        After("m_fishingBookButton", 32, true);
        After("m_keyboardGuideButton", 32, true);
        After("m_keyboardMode", 32);
        After("m_gamePadOff", 32);
        After("m_searchButton", 32, true);
    }

    // ---- minimap: the field's illustrated map image, centred on the player ---------------------------------------

    MinimapInfo mapInfo; Control mapClip; TextureRect mapImage; MovieClipNode mapPlayer, mapArrow;
    static readonly Vector2 MapMaskPos = new(2, 82), MapMaskSize = new(288, 222);   // the movie's map area (m_mask) in movie units
    const int BgAlpha = 30;         // UIMinimapDialog::BuildUIData: bgAlpha for a dialog without saved data
    const float MapZoom = 1f;       // MinimapDialog m_zoom (0.5 .. 1.5): movie units per image pixel in player-centred mode

    void Minimap(Control ui)
    {
        var origin = new Vector2(stage.X - 295, 0);            // the panel's right edge (movie x 295) sits on the stage's right edge
        // MinimapDialog loads Image/map/minimap/<field>.dds into m_field under m_mask and keeps the player at the mask's centre;
        // the client places entities with the projection from mapxblock/<xblock>.xml <minimap>
        try
        {
            var x = System.Xml.Linq.XDocument.Parse(System.Text.Encoding.UTF8.GetString(assets.Packs.Read("Xml", $"mapxblock/{xblock}.xml")));
            mapInfo = MinimapInfo.Parse(x);
        }
        catch (Exception e) { GD.PushWarning("minimap: " + e.Message); }
        var tex = mapInfo == null ? null : assets.ImageFile(mapInfo.ImageEntry);
        if (tex != null)
        {
            mapClip = new Control { Position = (origin + MapMaskPos) * u, Size = MapMaskSize * u, ClipContents = true, MouseFilter = Control.MouseFilterEnum.Ignore };
            mapImage = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, Texture = tex, Size = tex.GetSize() * MapZoom * u, MouseFilter = Control.MouseFilterEnum.Ignore };
            mapClip.AddChild(mapImage);
        }
        var r = Place(ui, "uiminimapdialog", 1f, origin);
        Hide(r, "m_controller", "m_resize", "m_sliderani", "m_report");    // m_report: reportVisible is off in the reference
        var maxPanel = r.FindClip("m_maxpanel");
        if (maxPanel != null) Hide(maxPanel, "m_mask");     // the map panel's mask: mapClip clips the map instead
        // initData -> setAlpha(bgAlpha, mapAlpha) -> MinimapDialogMax.changePanelAlpha(bgAlpha * 0.01): m_panel.alpha, with
        // UIMinimapDialog::BuildUIData's bgAlpha 30 for a dialog without saved data (mapAlpha 100 leaves the map opaque)
        if (maxPanel?.Direct("m_panel") is { } panel) { panel.BaseColor = new Color(panel.BaseColor, BgAlpha / 100f); maxPanel.GotoFrame(maxPanel.Frame); }
        if (mapClip != null)
        {
            // MinimapDialogMax's display list is m_frame, m_panel, m_mask over m_field (the map) and m_entities, then m_guide:
            // the map draws above the panel, so it goes into m_maxpanel right after m_panel, held at the screen scale
            if (maxPanel?.Direct("m_panel")?.Node is { } panelNode && panelNode.GetParent() is Node2D host)
            {
                var t = Transform2D.Identity;                    // the panel's parent in the root's movie units
                for (Node n = host; n != r && n is Node2D n2; n = n.GetParent()) t = n2.Transform * t;
                var sc = t.Scale * u;
                mapClip.Position = t.AffineInverse() * MapMaskPos;
                mapClip.Scale = new Vector2(1 / sc.X, 1 / sc.Y);
                host.AddChild(mapClip);
                host.MoveChild(mapClip, panelNode.GetIndex() + 1);
            }
            else { ui.AddChild(mapClip); ui.MoveChild(mapClip, r.GetParent().GetIndex()); }     // under the movie, as before
        }
        r.FindClip("m_channel")?.SetText($"Ch. {data.Player.Channel}");
        r.FindClip("m_fieldname")?.SetText(MapName());
        MinimapPlayers(r);
        if (tex != null)
        {
            mapPlayer = MinimapEntities.Create(r.Movie, "player", "idle", u);
            if (mapPlayer != null) { mapPlayer.Position = MapMaskPos + MapMaskSize / 2; r.AddChild(mapPlayer); mapArrow = mapPlayer.Direct("m_arrow")?.Node as MovieClipNode; }
            // m_guide: the guide arrows' layer, above the map and its entities, at the mask's top left and not clipped by it
            var guide = new Node2D { Name = "m_guide", Position = MapMaskPos };
            r.AddChild(guide);
            mapImage.AddChild(new MinimapPlaces(r.Movie, mapInfo, assets.Packs, mapImage.Size, u, guide, new Rect2(MapMaskPos, MapMaskSize))
            {
                PlayerLevel = data.Player.Level, FoundTaxis = data.FoundTaxis, InteractState = data.InteractState,
            });     // portals, npcs, the taxi and interact objects (MinimapPlaces.cs)
        }
    }

    /// <summary>
    /// The minimap's player row (m_players, MinimapDialogPlayers). UIMinimapDialog::AddProxy sends createPlayer for every
    /// other player and for the player itself, with BuildPlayerData's imgpath: the character's UGC profile image, or
    /// CommonResource m_strDefaultProfileImagePath (table/commonResource.xml DEFAULT_PROFILE_IMAGE_PATH) when it has none
    /// (ugcimagepath.cpp FUN_141028be0). BuildEntities ends with buildPlayers, which fills m_playerlist (a TileCacheList of
    /// minimap_renderer_players, 42 x 42, spacing 1, no padding, at the list's top left) and shows the row. The player here
    /// has no profile image and is alone in the field. ProfileRenderer loads the image into m_profile and sizes it to the
    /// renderer less twice m_profile's offset (configUI, Common.loadCompleteImage).
    /// </summary>
    void MinimapPlayers(MovieClipNode r)
    {
        var players = r.FindClip("m_players");
        var list = players?.Direct("m_playerlist");
        var path = (string)assets.Table("table/commonresource.xml")?.Root?.Element("DEFAULT_PROFILE_IMAGE_PATH")?.Attribute("value");
        const string imageRoot = "./Data/Resource/Image/";
        var tex = path != null && path.StartsWith(imageRoot, StringComparison.OrdinalIgnoreCase) ? assets.ImageFile(path[imageRoot.Length..]) : null;
        var item = list == null ? null : r.Movie.BuildClass("minimap_renderer_players", u);
        if (item == null) return;
        item.Position = list.Base.Origin;                       // the first tile at the list's top left (padding 0)
        players.AddChild(item);
        if (tex != null && item.Direct("m_profile") is { Node: MovieClipNode profile } pc)
        {
            var size = (item.Bounds.Size == Vector2.Zero ? new Vector2(42, 42) : item.Bounds.Size) - pc.Base.Origin * 2;     // m_imgwidth, m_imgheight
            profile.AddChild(new Sprite2D { Texture = tex, Centered = false, Scale = size / tex.GetSize() });
        }
    }

    /// <summary>
    /// UILevelUpPackageWidgetDialog ("Navigator"): an event widget (uieventwidgetdialog) docked by table/alignwidget.xml at the
    /// top right, offsetX -298, i.e. its right edge on the minimap's left edge. setData: title, reward slot, gauge or reward button.
    /// </summary>
    void NavigatorWidget(Control ui)
    {
        var r = Place(ui, "uieventwidgetdialog", 1f, Vector2.Zero);
        var dlg = r.AllClips().FirstOrDefault(c => c.Direct("m_title") != null) ?? r;     // the EventWidgetDialog instance
        // the movie draws the dialog mid-stage: dock its registration point instead, top right with offsetX -298, where the
        // dialog's width is m_dragBar.width (EventWidgetDialog.get_width)
        float w = dlg.ChildWidth(dlg.Direct("m_dragBar"));
        if (w <= 0) w = 70;
        r.Position = (new Vector2(stage.X - 298 - w, 0) - dlg.Position) * u;
        dlg.SetNamedText("m_title", assets.Str("$s_levelup_package_widget"));
        if (dlg.Direct("m_title")?.Node is TextNode title) title.ShrinkToFit();     // TextFieldEx.setTextAutoSize(SHRINK)
        // EventWidgetDialog.setData: below max the gauge shows "text" and m_gage.width = current / max * (m_back.width - 4)
        int cur = data.NavigatorCurrent, max = data.NavigatorMax;
        bool ready = cur >= max;
        dlg.SetNamedVisible("m_rewardBtn", ready);
        dlg.SetNamedVisible("m_effect", ready);
        dlg.SetNamedVisible("m_gageBar", !ready);
        if (!ready && dlg.FindClip("m_gageBar") is { } gage)
        {
            gage.SetNamedText("m_text", $"{cur}/{max}");
            if (gage.Direct("m_gage") is { } fill && gage.Direct("m_back") is { } back)
            {
                // authored widths: the symbol's bounds times the placed x scale
                float DefWidth(MovieClipNode.Child c) =>
                    UiBuilder.F(r.Movie.Scope.Symbol((int)c.Def["id"])?["bounds"]?["w"], 0) * Math.Abs(UiBuilder.F(c.Def["matrix"]?["a"], 1));
                float full = DefWidth(back) - 4, fw = DefWidth(fill);
                var t = fill.Base;
                if (fw > 0) fill.Override = new Transform2D(t.X * (full * cur / max / fw), t.Y, t.Origin);
                gage.GotoFrame(gage.Frame);
            }
        }
        var slot = r.FindClip("m_slot");
        var icon = assets.ImageFile($"item/icon/{data.NavigatorRewardItem}.png");
        if (slot != null && icon != null)
        {
            var b = slot.ContentRect() ?? new Rect2(0, 0, 60, 60);
            var spr = new Sprite2D { Texture = icon, Centered = false, Position = b.Position, Scale = b.Size / icon.GetSize() };
            slot.AddChild(spr);
            // BaseSlot.setItemCount: an export_slot_itemcount at (0, 39) in m_iconOption, its textField holding the count
            if (data.NavigatorRewardCount > 0 && r.Movie.BuildClass("export_slot_itemcount", u) is { } count)
            {
                foreach (var c in count.AllClips()) foreach (var t in c.TextFields) t.Text = data.NavigatorRewardCount.ToString();
                count.Position = new Vector2(0, 39);
                (slot.FindClip("m_iconOption") ?? slot).AddChild(count);
            }
        }
    }

    /// <summary>
    /// UINoticeDialog, the web notice banner, closed: OnLoadComplete plays "hideComplete", which parks m_mainWindow off
    /// the left edge so only its m_showButton tab shows. vfunc_43 aligns it with vfunc_2(3, 0, 0): left, vertically
    /// centred at the dialog's AS3 height (UIComponent's, the instance bounds).
    /// </summary>
    void NoticeTab(Control ui)
    {
        var r = Place(ui, "uinoticedialog", 1f, Vector2.Zero);
        if (DialogBody(r) is not { } body) return;
        body.GotoLabel("hideComplete");
        body.FindClip("m_mainWindow.m_showButton")?.GotoLabel("up");
        var b = UiLayout.SymbolOfClass(r.Movie, "UINoticeDialog", out _)?["bounds"];
        AlignDialog(r, body, 3, new Vector2(UiBuilder.F(b?["w"], 905), UiBuilder.F(b?["h"], 1600)), 0, 0);
    }

    /// <summary>The xblock of the field this scene runs: a portal move's target (the scene reloads per move), else --field.</summary>
    string xblock;

    static string ArgField()
    {
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--field") return args[i + 1];
        return null;
    }

    /// <summary>
    /// The minimap's field name (UIMinimapDialog::BuildUIData "fieldname", CubeMapInformationService::FUN_1410c3330): a house's
    /// name, else WorldService m_290, which the field load sets to the mapname string of the map id
    /// (CStringTable::FUN_141811980(3, mapId), worldservice_1.cpp:3317).
    /// </summary>
    string MapName()
    {
        try
        {
            var fd = FieldData.FindByXBlock(assets.Packs, xblock);
            if (fd != null && new GameStrings(assets.Packs, "en").Table("mapname", "name") is { } names
                && (names.TryGetValue(fd.Id.ToString(), out var n) || names.TryGetValue(fd.Id.ToString("D8"), out n)))
                return n;
        }
        catch (Exception e) { GD.PushWarning("map name: " + e.Message); }
        return data.FieldName;
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

    void UpdateMinimap()
    {
        if (player == null) { player = FindPlayer(this); if (player == null) return; }
        var size = mapImage.Texture.GetSize();
        var p = ToMs2(player.GlobalPosition);
        var (pu, pv) = mapInfo.ToImage(p);
        var px = new Vector2((float)pu, (float)pv) * size * MapZoom;           // player in movie units on the scaled image
        mapImage.Position = (MapMaskSize / 2 - px) * u;
        if (mapArrow != null && player.Visual != null)
        {
            field ??= player.GetParent() as Field ?? FindField(this);
            if (field != null) mapArrow.RotationDegrees = PlayerHeading(Mathf.RadToDeg(field.CameraYaw), player.FacingRotZ);
        }
    }

    // ---- chat: uimessengerdialog sized and placed from table/default/defaultmessenger0.xml ----------------------------

    /// <summary>
    /// The chat window as UIMessenger builds it (uimessenger.cpp): vfunc_54 reads the window's width/height, alignType and
    /// offsets and its tabs from the option file (the default one: table/default/defaultmessenger0.xml), AddTab fills the
    /// tab bar, SetSizeInt runs MessengerDialog's Constraints reflow (configUI), and vfunc_43 aligns it with vfunc_2.
    /// SetBackgroundAlpha drives m_panel.alpha; the log is m_textOutput (AppendHtml).
    /// </summary>
    void Chat(Control ui)
    {
        var cfg = assets.Table("table/default/defaultmessenger0.xml")?.Root;
        float A(string n, float d) => float.TryParse((string)cfg?.Attribute(n), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : d;
        var size = new Vector2(A("width", 408), A("height", 270));
        var tabs = cfg?.Elements("Tab").ToList() ?? new();
        var screen = tabs.FirstOrDefault()?.Element("PanelSetting")?.Element("Screen");
        float bgAlpha = float.TryParse((string)screen?.Attribute("backgroundAlpha"), out var ba) ? ba : 50;
        float fontSize = float.TryParse((string)screen?.Attribute("fontSize"), out var fs) ? fs : 16;

        var movie = new UiMovie(assets, "uimessengerdialog");
        UiLayout.ResetRootScale(movie, "messenger_dialog");
        // MessengerDialog.configUI constraints; the authored dialog is 350 x 270
        UiLayout.Reflow(movie, "messenger_dialog", new Vector2(350, 270), size,
            ("m_panel", UiLayout.Edge.All), ("m_textBgColor", UiLayout.Edge.All), ("m_textOutput", UiLayout.Edge.All), ("m_textOutputMask", UiLayout.Edge.All),
            ("m_resize", UiLayout.Edge.Top | UiLayout.Edge.Right), ("m_emotion", UiLayout.Edge.Bottom | UiLayout.Edge.Right),
            ("m_subject", UiLayout.Edge.Left | UiLayout.Edge.Bottom), ("m_superEffect", UiLayout.Edge.Left | UiLayout.Edge.Bottom),
            ("m_textInput", UiLayout.Edge.Left | UiLayout.Edge.Right | UiLayout.Edge.Bottom), ("m_buttonBar", UiLayout.Edge.Left | UiLayout.Edge.Right | UiLayout.Edge.Top),
            ("m_scrollBar", UiLayout.Edge.Left | UiLayout.Edge.Top | UiLayout.Edge.Bottom),
            ("m_chatEmoticonOpen", UiLayout.Edge.Bottom | UiLayout.Edge.Right), ("m_chatEmoticonPreview", UiLayout.Edge.Bottom | UiLayout.Edge.Right));
        var root = movie.BuildRoot(u);
        root.Scale = new Vector2(u, u);
        var holder = new Node2D(); holder.AddChild(root); ui.AddChild(holder);
        var dlgChild = root.Children.FirstOrDefault(c => c.Node is MovieClipNode m && m.ClassName == "messenger_dialog");
        if (dlgChild == null) return;
        chat = (MovieClipNode)dlgChild.Node;
        // UIComponent resets the authored scale; vfunc_43 aligns the window (alignType 7 = bottom left, offsetY -38 =
        // above the two bottom bars, FUN_1418dd0c0 + FUN_1418dd110)
        var pos = UiLayout.Align((int)A("alignType", 7), stage, size, A("offsetX", 0), A("offsetY", -BottomBars));
        dlgChild.Base = new Transform2D(Vector2.Right, Vector2.Down, pos);
        root.GotoFrame(root.Frame);
        // the emoticon preview opens on demand and the super chat effect shows only for super world chat
        Hide(chat, "m_chatEmoticonPreview", "m_superEffect");
        // SetBackgroundAlpha -> backgroundAlpha(a): m_panel.alpha = a / 100 (replaces the authored 0.3)
        if (chat.Direct("m_panel") is { } panel) { panel.BaseColor = new Color(panel.BaseColor, bgAlpha / 100f); chat.GotoFrame(chat.Frame); }
        // the talk target dropdown shows the first target not hidden (FUN_141a14110); in the field that is the local
        // talk, labelled $s_talkpanelsetting_social (uimessenger.cpp:4567)
        if (chat.FindClip("m_subject") is { } subject) subject.SetText(assets.Str("$s_talkpanelsetting_social"));
        // AddTab: one messenger_rclickDropdown per tab (buttonWidth 62, spacing 1) on the bar at m_buttonBar; index 0 selected
        if (chat.Direct("m_buttonBar") is { } barChild)
        {
            barChild.Node.Visible = false; barChild.Last = -1;
            for (int i = 0; i < tabs.Count; i++)
            {
                var tab = movie.BuildClassSized("messenger_rclickDropdown", u, 62, 24);
                if (tab == null) continue;
                tab.Position = barChild.Base.Origin + new Vector2(i * (62 + 1), 0);
                tab.HideNamed("m_alime");
                tab.GotoLabel(i == 0 ? "selected_up" : "up");
                tab.SetNamedText("textField", assets.Str((string)tabs[i].Attribute("tabName")));
                chat.AddChild(tab);
            }
            chat.GotoFrame(chat.Frame);
        }
        chatLog = chat.FindClip("m_textOutput")?.TextFields.FirstOrDefault();
        if (chatLog != null)
        {
            var ls = (LabelSettings)chatLog.Label.LabelSettings.Duplicate();
            ls.FontSize = Math.Max(1, (int)Math.Round(ls.FontSize * fontSize / UiBuilder.F(chatLog.Def["size"], 16)));
            chatLog.Label.LabelSettings = ls;
            chatLog.Label.ClipText = true;
            chatLog.Text = "";
        }
        UpdateChatScroll();
        chatInput = new TextInputWidget { Node = chat.FindClip("m_textInput") };
        chatInput.Init();
        // UIMessenger::FUN_141a11e30, Return with the input focused: an empty (trimmed) line only releases the input, any
        // other line is submitted and then the input is released (FUN_141a12260)
        chatInput.Edit.TextSubmitted += text =>
        {
            if (text.Trim().Length > 0) AddChatLine($"{data.Player.Name}: {text.Trim()}");
            ReleaseChat();
        };
        GroupChat(ui);
    }

    /// <summary>
    /// UIMessenger::vfunc_13, the messenger's keys while it is shown. Return (DIK_RETURN) with the input not focused
    /// focuses it with the caret after its text (FUN_141a11e30: SetFocused, FocusAndSetSelection(length, length)); with
    /// it focused the LineEdit submits (TextSubmitted above). Escape with the input focused releases it
    /// (FUN_141a12260) and is used up, so it does not reach the game menu; unfocused, it is left to the rest.
    /// </summary>
    public override void _Input(InputEvent e)
    {
        if (chatInput?.Edit == null || chat == null || !chat.IsVisibleInTree()) return;
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
        if (k.Keycode == Key.Enter && !chatInput.Focused && !UiFocus.IsTyping)
        {
            chatInput.Focus();
            chatInput.Edit.CaretColumn = chatInput.Length;
            GetViewport().SetInputAsHandled();
        }
        else if (k.Keycode == Key.Escape && chatInput.Focused)
        {
            ReleaseChat();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>UIMessenger::FUN_141a12260: the input loses the focus and its text is cleared.</summary>
    void ReleaseChat()
    {
        chatInput.Edit.ReleaseFocus();
        chatInput.Edit.Text = "";
    }

    /// <summary>The log keeps the last lines in view (TextAreaEx position = maxScrollV after each append).</summary>
    void AddChatLine(string line)
    {
        if (chatLog == null) return;
        chatLines.Add(line);
        if (chatLines.Count > 100) chatLines.RemoveAt(0);
        ShowChatLines();
    }

    void ShowChatLines()
    {
        if (chatLog == null) return;
        var l = chatLog.Label;
        l.LinesSkipped = 0;
        l.Text = string.Join("\n", chatLines);
        l.LinesSkipped = Math.Max(0, l.GetLineCount() - l.GetVisibleLineCount());
        UpdateChatScroll();
    }

    MovieClipNode chatThumb;

    /// <summary>
    /// m_scrollBar (messenger_scrollbar : ScrollBarEx, scrollTarget m_textOutput) as CLIK draws it. ScrollIndicator.draw takes
    /// the field's scroll state: setScrollProperties(bottomScrollV - scrollV, 1, maxScrollV), position scrollV.
    /// ScrollBar.updateThumbPosition hides the thumb when (position - min) / (max - min) is NaN (nothing to scroll) or max
    /// is not positive. The arrows are enabled only with the thumb shown and room to move that way, and the track is
    /// enabled likewise (it is authored at alpha 0). ScrollBarEx.updateThumb sizes the thumb to page / (max - min + page) of
    /// the track, between minThumbSize 10 and maxThumbSize 20, and it sits at round(fraction * (track - thumb)) below the
    /// track's top. The bar is placed with a 9-slice grid (y 30..40 of 2..70), so the arrows keep their size at the ends
    /// and the track (authored y 25, height 22) takes the stretch.
    /// </summary>
    void UpdateChatScroll()
    {
        if (chatLog == null || chat?.Direct("m_scrollBar") is not { Node: MovieClipNode bar } barChild) return;
        var l = chatLog.Label;
        int total = l.GetLineCount(), visible = Math.Max(1, l.GetVisibleLineCount());
        int scrollV = l.LinesSkipped + 1;
        int maxScrollV = Math.Max(1, total - visible + 1);
        int bottomScrollV = Math.Min(total, scrollV + visible - 1);
        double page = bottomScrollV - scrollV, min = 1, max = maxScrollV, pos = scrollV;
        double frac = (pos - min) / (max - min);
        bool thumbOn = !(double.IsNaN(frac) || double.IsNaN(page) || max <= 0 || double.IsInfinity(max));
        bool on = thumbOn;      // enabled is true (__setProp_m_scrollBar)
        void State(string name, bool enabled) { if (bar.Direct(name)?.Node is MovieClipNode b) b.GotoLabel(enabled ? "up" : "disabled"); }
        State("upArrow", on && pos > min);
        State("downArrow", on && pos < max);
        State("track", on);
        if (bar.Direct("thumb") is { } authored) { authored.Last = -1; bar.GotoFrame(bar.Frame); }
        chatThumb?.QueueFree(); chatThumb = null;
        if (!thumbOn) return;
        float d = UiBuilder.F(barChild.Def?["matrix"]?["d"], 1);
        float trackY = 2 * d + 23, trackH = 68 * d - 46;          // the track between the arrows, in the bar's drawn space
        double span = Math.Max(1, max - min + page);
        double th = Math.Max(10, Math.Min(trackH, page / span * trackH));
        th = Math.Min(th, 20);
        float thumbH = (float)th;
        double y = Math.Round(frac * (trackH - thumbH) + trackY);
        y = Math.Max(trackY, Math.Min(trackY + trackH - thumbH, y));
        chatThumb = chat.Movie.BuildClassSized("btn_chat_scrollbar", u, 22, thumbH);
        if (chatThumb == null) return;
        chatThumb.GotoLabel("up");
        chatThumb.Position = new Vector2(6, (float)y);
        bar.AddChild(chatThumb);
    }

    /// <summary>
    /// UIGroupMessengerBrowser (uigroupmessengerbrowser): vfunc_43 aligns it bottom left at offset (0, -300); with no group
    /// chat setData([]) leaves only the insert button (btn_group_chat_a, label $s_groupmessenger_addbutton) in m_container
    /// at (6, 25). m_background and m_line fade in only under the mouse (configUI tweens them to alpha 0).
    /// </summary>
    void GroupChat(Control ui)
    {
        var movie = new UiMovie(assets, "uigroupmessengerbrowser");
        var root = movie.BuildRoot(u);
        root.Scale = new Vector2(u, u);
        var holder = new Node2D(); holder.AddChild(root); ui.AddChild(holder);
        var dlgChild = root.Children.FirstOrDefault(c => c.Node is MovieClipNode m && m.ClassName == "group_messenger_browser_dialog");
        if (dlgChild == null) return;
        var dlg = (MovieClipNode)dlgChild.Node;
        var btn = movie.BuildClass("btn_group_chat_a", u);
        if (btn != null)
        {
            btn.Position = new Vector2(6, 25);
            btn.GotoLabel("up");
            btn.SetNamedText("textField", assets.Str("$s_groupmessenger_addbutton"));
            dlg.AddChild(btn);
        }
        foreach (var n in new[] { "m_background", "m_line" })
            if (dlg.Direct(n) is { } c) { c.BaseColor = new Color(c.BaseColor, 0); }
        dlg.GotoFrame(dlg.Frame);
        var box = dlg.ContentRect() ?? new Rect2(0, 0, 131, 94);
        dlgChild.Base = new Transform2D(Vector2.Right, Vector2.Down, UiLayout.Align(7, stage, box.Size, 0, -300));
        root.GotoFrame(root.Frame);
    }

    // ---- quest guide: the field mission panel ---------------------------------------------------------------------

    /// <summary>
    /// UIQuestGuideDialog with only the field mission entity: the dialog aligns top right at (0, 330)
    /// (CAlignmentInfo(6, 0, 330) in its constructor, x = stage width - ceil(dialog width)). qg_dialog_body places
    /// m_topContainer at x -1, and addMission puts a qg_mission_entity at x -1 inside it, so the entity sits at dialog x - 2.
    /// FieldMissionGuideEntity.setType("field") / setData run as below.
    /// </summary>
    void Quests(Control ui)
    {
        var m = new UiMovie(assets, "uiquestguidedialog");
        var sym = UiLayout.SymbolOfClass(m, "qg_mission_entity", out _);
        if (sym == null) return;
        var goals = data.FieldMissionGoals;

        // addCondition: a qg_label_condition per goal at y = m_container.height + 3, height = textHeight + 5
        var labels = new List<(MovieClipNode Node, float Y)>();
        float contH = 0, top = 0;
        foreach (var (text, _) in goals)
        {
            var l = m.BuildClass("qg_label_condition", u);
            if (l == null) break;
            l.SetNamedText("textField", text);
            float h = (l.TextFields.FirstOrDefault()?.FlashTextHeight() ?? 16) + 5;
            float y = contH + 3;
            if (labels.Count == 0) top = y;
            labels.Add((l, y));
            contH = y + h - top;            // DisplayObject.height: the children's bounds, from the first label's top
        }

        // m_bg.height = m_container.y + m_container.height + 15, set before the build so the 9-slice grid rebuilds
        var children = sym["timeline"]?["children"] as JsonArray;
        var bgDef = children?.OfType<JsonObject>().FirstOrDefault(c => (string)c["name"] == "m_bg");
        var contDef = children?.OfType<JsonObject>().FirstOrDefault(c => (string)c["name"] == "m_container");
        float contY = UiBuilder.F(contDef?["matrix"]?["y"], 34);
        if (bgDef?["matrix"] is JsonObject bm && m.Scope.Symbol((int)bgDef["id"])?["bounds"] is JsonNode bb)
            bm["d"] = (double)((contY + contH + 15) / UiBuilder.F(bb["h"], 37));

        var panel = m.BuildClass("qg_mission_entity", u);
        if (panel == null) return;
        var at = UiLayout.Align(6, stage, new Vector2(QuestGuideWidth, 0), 0, 330) + new Vector2(-2, 0);   // m_topContainer -1, entity -1
        panel.Scale = new Vector2(u, u); panel.Position = at * u;
        var holder = new Node2D(); holder.AddChild(panel); ui.AddChild(holder);
        panel.SetNamedText("m_chapterName", assets.Str("$s_questguide_mission_field"));
        panel.FindClip("m_toggle")?.GotoLabel("up");                       // _toggle false: expanded, shows "-"

        var cont = panel.FindClip("m_container");
        foreach (var (l, y) in labels) { l.Position = new Vector2(0, y); cont?.AddChild(l); }

        // the stars: the last goals.Length of mission1..5, "off" until the goal is met
        for (int i = 1; i <= 5; i++) panel.SetNamedVisible("mission" + i, false);
        for (int i = 0; i < goals.Length && i < 5; i++)
        {
            string name = "mission" + (5 - goals.Length + i + 1);
            panel.SetNamedVisible(name, true);
            panel.FindClip(name)?.GotoLabel(goals[i].Satisfied ? "on" : "off");
        }
    }

    /// <summary>qg_dialog_body's width: the dialog's AS3 width when only the mission entity shows.</summary>
    const float QuestGuideWidth = 298;

    // ---- HP/SP orb and the skill slots ----------------------------------------------------------------------------

    void Orb(Control ui)
    {
        const float f = 1f;
        var r = Place(ui, "uimainplayerinfo", f, Vector2.Zero);
        // UIMainPlayerInfo::vfunc_43: vfunc_2(2, 0, -(FUN_1418dd0c0() + FUN_1418dd110())); MainPlayerInfo is 705 x 193
        if (DialogBody(r) is { } body) AlignDialog(r, body, 2, new Vector2(705, 193), 0, -BottomBars);
        Hide(r, "m_rideeSP", "m_rideeHP", "m_ridee", "m_rideeName", "m_penalty", "m_infinity", "infinity", "m_message", "m_gamePadSkillCommon", "m_cubeItem", "m_cubeControl");
        r.FindClip("m_playerInfo")?.GotoLabel("stat_show");          // the stat orb fades in with this label
        var stat = r.FindClip("m_playerInfo.m_stat");
        StatBar(stat.FindClip("m_hp"), data.HpRatio);
        StatBar(stat.FindClip("m_sp"), data.SpRatio);
        stat.SetNamedVisible("m_hpWarning", data.HpRatio <= 0.3);     // StatInfo.setHP: shown at cur / max <= 0.3
        // m_hpWarning (sym 367) is a plain 40-frame clip with no script: it loops, alpha 0.051 -> 0.398 at f20 -> 0.051
        if (stat.Direct("m_hpWarning")?.Node is MovieClipNode warning) warning.Loop();
        // StatInfo.initStat -> setEP(cur, cur, max): pips below uint(cur / max * 6) play "normal", the rest "empty" (calcFrame)
        if (stat.FindClip("m_ep") is { } ep)
        {
            uint n = (uint)((double)data.EpCurrent / data.EpMax * 6);
            for (int i = 0; i < 6; i++) ep.FindClip("gauge" + i)?.GotoLabel(i < n ? "normal" : "empty");
        }
        stat.SetNamedText("m_curHp", data.Player.HpCurrent.ToString()); stat.SetNamedText("m_maxHp", data.Player.HpMax.ToString());
        stat.SetNamedText("m_curSp", data.SpCurrent.ToString()); stat.SetNamedText("m_maxSp", data.SpMax.ToString());
        stat.SetNamedText("hpTxt", "Health"); stat.SetNamedText("spTxt", "Spirit");
        // MainPlayerInfo.setData: m_tag = "LV " + level + " " + jobName + " " + name (m_rideeName.m_tag stays with its hidden parent)
        // StatInfo.initStat: setJob(jobCode) sends m_job.icon and m_job.battle.icon to "_" + jobCode, and setBattle(battle)
        // plays m_job.battle from "normal" (out of battle), which fades the battle icon out and stops on its last frame.
        // jobCode is the sub job (uimainplayerinfo.cpp:1405): 100 Knight, 200 Berserker ... 1100 Soul Binder; the stub
        // numbers its jobs 1, 10, 20 ... 100.
        int jobCode = data.Player.Job <= 1 ? 100 : (data.Player.Job / 10 + 1) * 100;
        static void StopAt(MovieClipNode.Child c, string label)       // gotoAndStop(label): the label's own frame
        {
            if (c?.Node is MovieClipNode m && m.Labels.TryGetValue(label, out int f)) m.GotoFrame(f);
        }
        if (stat?.Direct("m_job")?.Node is MovieClipNode job)
        {
            StopAt(job.Direct("icon"), "_" + jobCode);
            if (job.Direct("battle")?.Node is MovieClipNode battle)
            {
                StopAt(battle.Direct("icon"), "_" + jobCode);
                battle.GotoLabel("normal");                           // gotoAndPlay("normal") runs to the stop() on frame 29
            }
        }
        r.FindClip("m_playerInfo")?.SetNamedText("m_tag", $"LV {data.Player.Level} {StubCharacterList.JobNames.GetValueOrDefault(data.Player.Job, "Beginner")} {data.Player.Name}");

        // slots: playerinfo_dialog_slot shows its "skill" label; the skill list is a fixed grid of 16 slot components (Q W E R / A S D F, 1 2 3 4 / 5 6 7 8)
        if (r.FindClip("m_slot") is { } slot)
        {
            slot.GotoLabel("skill");
            var list = slot.FindClip("m_skillCommon");
            list?.GotoLabel("show");
            string shortcutLetters = "QWERASDF12345678";
            int n = 0;
            if (list != null)
                foreach (var c in list.Children.OrderBy(c => c.Def["depth"]?.GetValue<int>() ?? 0))
                    if (c.Node is MovieClipNode slotClip && slotClip.ClassName == "playerinfo_slot_skill" && n < shortcutLetters.Length)
                    {
                        string key = shortcutLetters[n++].ToString();
                        var sk = slotClip.FindClip("m_shortcutKey");
                        if (sk != null) foreach (var cl in sk.AllClips()) foreach (var t in cl.TextFields) t.Text = key;
                        slotClip.GotoLabel("up");
                        foreach (var cl in slotClip.AllClips()) foreach (var t in cl.TextFields) if (t.Text != key) t.Text = "";
                    }
            // the action slots in m_commonExpansion: pick up, attack, jump (slotIndex 22-24 on Z X C), each with a customBG
            if (list?.FindClip("m_commonExpansion") is { } expansion)
            {
                var actions = new[] { ("__id26_", "slot_skill_itempickup", "Z"), ("__id27_", "slot_skill_fight", "X"), ("__id28_", "slot_skill_jump", "C") };
                foreach (var (name, bg, key) in actions)
                    if (expansion.FindClip(name) is { } slotClip)
                    {
                        slotClip.GotoLabel("up");
                        foreach (var cl in slotClip.AllClips()) foreach (var t in cl.TextFields) t.Text = "";
                        if (slotClip.FindClip("m_shortcutKey") is { } sk) foreach (var cl in sk.AllClips()) foreach (var t in cl.TextFields) t.Text = key;
                        CustomBG(r.Movie, slotClip, bg);
                    }
            }
            // the skill expansion slots: the default key table puts Insert Home Page Up over Delete End Page Dn; the captions
            // are the key names (string/<lang>/keyname.xml by key id)
            if (list?.FindClip("m_skillExpansion") is { } skillExt)
            {
                var names = assets.Table($"string/{assets.Language}/keyname.xml")?.Root?.Elements("key")
                    .GroupBy(k => (string)k.Attribute("id")).ToDictionary(g => g.Key, g => (string)g.First().Attribute("name"));
                string[] keyIds = { "128", "120", "122", "129", "125", "127" };
                var extSlots = skillExt.Children.Where(c => c.Node is MovieClipNode m && m.ClassName == "playerinfo_slot_skill")
                    .OrderBy(c => Math.Round(c.Base.Origin.Y)).ThenBy(c => c.Base.Origin.X).Select(c => (MovieClipNode)c.Node).ToList();
                for (int i = 0; i < extSlots.Count && i < keyIds.Length; i++)
                {
                    var slotClip = extSlots[i];
                    slotClip.GotoLabel("up");
                    foreach (var cl in slotClip.AllClips()) foreach (var t in cl.TextFields) t.Text = "";
                    string key = names?.GetValueOrDefault(keyIds[i]) ?? "";
                    if (slotClip.FindClip("m_shortcutKey") is { } sk) foreach (var cl in sk.AllClips()) foreach (var t in cl.TextFields) t.Text = key;
                }
            }
            // BaseSlot.clearSlot: no charge gauge on an empty slot
            foreach (var cl in slot.AllClips().Where(c => c.ClassName == "playerinfo_slot_skill").ToList()) cl.SetNamedVisible("m_recharge", false);
            // UIMainPlayerInfo::SetShortkeyPageNumber(key table page + 1) -> setShortkeyPageNumber -> m_page.m_pageNum
            foreach (var cl in slot.AllClips().Where(c => c.Direct("m_pageNum") != null).ToList()) cl.SetNamedText("m_pageNum", "1");
        }
    }

    /// <summary>
    /// The slot's customBG setter: hides m_bg and adds the named class to m_bgLayer at (-1, -1), sized to
    /// m_bgLayer.width - 2 by m_bgLayer.height - 2 (in the slot's units, set on the child).
    /// </summary>
    void CustomBG(UiMovie movie, MovieClipNode slotClip, string className)
    {
        if (slotClip.Direct("m_bgLayer") is not { } layerChild || layerChild.Node is not MovieClipNode layer) return;
        if (movie.BuildClass(className, u) is not { } bg) return;
        slotClip.SetNamedVisible("m_bg", false);
        float lw = slotClip.ChildWidth(layerChild), lh = lw;     // m_bgLayer is square (60 x 60 at its scale)
        if (layer.ContentRect() is { } lb && lb.Size.X > 0) lh = lw * lb.Size.Y / lb.Size.X;
        var b = bg.ContentRect() ?? new Rect2(0, 0, 60, 60);
        bg.Position = new Vector2(-1, -1);
        if (b.Size.X > 0 && b.Size.Y > 0) bg.Scale = new Vector2((lw - 2) / b.Size.X, (lh - 2) / b.Size.Y);
        layer.AddChild(bg);
    }

    /// <summary>
    /// StatusBar.setValue for the orb's vertical bars (m_fullwidth 152, m_vertical): m_mask.height = max(1, 152 x cur / max)
    /// from its own origin (the bar is placed turned 180 degrees, so it fills from the bottom); onChangeHp/onChangeSp then put
    /// "line" at y = getTailPosition() + 5 (the mask height as uint, or 152 once it is within 2 of it).
    /// </summary>
    static void StatBar(MovieClipNode bar, double ratio)
    {
        var mask = bar?.Direct("m_mask");
        if (mask == null) return;
        const float full = 152;
        float h = Math.Max(1f, full * (float)ratio);
        var b = mask.MaskBounds; var baseT = mask.Base;
        mask.Override = new Transform2D(baseT.X, new Vector2(0, h / b.Size.Y), baseT.Origin);
        if (bar.Direct("line") is { } line)
        {
            uint tail = (uint)h; int t = tail - 2 < full ? (int)tail : (int)full;
            line.Base = new Transform2D(line.Base.X, line.Base.Y, new Vector2(line.Base.Origin.X, t + 5));
        }
        bar.GotoFrame(bar.Frame);
    }

    // ---- EXP bar (from the movie) ----------------------------------------------------------------------------------

    /// <summary>
    /// ExpBar (expbar_entity_body). Its width is the stage width, and configUI's Constraints (REFLOW) stretch m_bg, m_restBg
    /// and m_bar and keep m_point centred. setSize gives m_bar.m_fullwidth = m_bg.width - 6, and setExp fills the status bar
    /// (StatusBar.setValue: the mask width is fullwidth x cur / max, at least 1). makeSplit and onStatusChange lay out the
    /// ticks, m_split and the gain effect (<see cref="BarSplits"/>).
    /// </summary>
    void ExpBar(Control ui)
    {
        var m = new UiMovie(assets, "uiexpbar");
        const float bgW = 1920, bgH = 21;
        UiLayout.Reflow(m, "expbar_entity_body", new Vector2(bgW, bgH), new Vector2(stage.X, bgH),
            ("m_bg", UiLayout.Edge.Left | UiLayout.Edge.Right), ("m_restBg", UiLayout.Edge.Left | UiLayout.Edge.Right),
            ("m_point", UiLayout.Edge.CenterH), ("m_bar", UiLayout.Edge.Left | UiLayout.Edge.Right));
        var r = m.BuildRoot(u);
        r.Scale = new Vector2(u, u); r.Position = new Vector2(0, stage.Y - 237) * u;
        var holder = new Node2D(); holder.AddChild(r); ui.AddChild(holder);
        Hide(r, "m_restBar", "m_restPin", "m_restBg");
        var body = r.FindClip("__id0_");
        if (body == null) return;
        body.FindClip("m_point")?.SetNamedText("textField", $"{data.ExpPercent:0.00}%");
        float full = stage.X - 6;                                            // setSize: m_bg.width - 6
        float tail = SetBarValue(body, full, data.ExpPercent);
        BarSplits(body, "expbar_status_split", full, tail);
    }

    /// <summary>
    /// StatusBar.setValue(cur, 100, false) on the body's m_bar: the mask is <paramref name="full"/> x cur / 100 wide, at least 1.
    /// Returns StatusBar.getTailPosition: the mask width as an unsigned int, or full once it reaches full + 2.
    /// </summary>
    static float SetBarValue(MovieClipNode body, float full, double percent)
    {
        float w = (float)Math.Max(full * percent / 100.0, 1);
        if (body.FindClip("m_bar") is { } bar && bar.ChildNamed("m_mask") is { } mask)
        {
            mask.Override = new Transform2D(new Vector2(w, 0), new Vector2(0, 1), mask.Base.Origin);
            bar.GotoFrame(bar.Frame);
        }
        float t = (uint)w;                                                    // AS3: uint l2, (l2 - 2) compared as a Number
        return t - 2 < full ? t : (int)full;
    }

    /// <summary>
    /// ExpBar / AdventureLevelExpBar.makeSplit: a <paramref name="splitClass"/> at 0.1 .. 0.9 x m_fullwidth, y 2, each added
    /// at numChildren - 1 (under the top child, m_point). onStatusChange: m_split.x = tail + 2, m_eff.x = tail - 80, and
    /// m_eff plays from frame 1. It is export_effect_particle1, whose frame 60 script stops it on its empty last frame, so
    /// it plays once when the value is set and then shows nothing.
    /// </summary>
    static void BarSplits(MovieClipNode body, string splitClass, float full, float tail)
    {
        var top = body.Children.LastOrDefault(c => !c.IsMask)?.Node;
        foreach (var f in new[] { 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9 })
        {
            if (body.Movie.BuildClass(splitClass, body.sx, body.sy) is not { } split) break;
            split.Position = new Vector2((float)(full * f), 2);
            body.AddChild(split);
            if (top != null && top.GetParent() == body) body.MoveChild(split, top.GetIndex());
        }
        body.SetChildX(body.Direct("m_split"), tail + 2);
        var eff = body.Direct("m_eff");
        body.SetChildX(eff, tail - 80);
        if (eff?.Node is MovieClipNode particle && particle.FrameCount > 1) particle.PlayRange(0, particle.FrameCount - 1);
    }

    /// <summary>
    /// UIAdventureLevelExpBar (feature 0xbd): AdventureLevelExpBar.alignment puts it top centre at
    /// stageHeight - expBarHeight - m_bg.height, i.e. on top of the EXP bar; its width is the stage width and Constraints
    /// reflow the background, the bar and the point text. setPoint shows the percentage with a " %" suffix.
    /// </summary>
    void AdventureBar(Control ui)
    {
        var m = new UiMovie(assets, "uiadventurelevelexpbar");
        var sym = UiLayout.SymbolOfClass(m, "adventure_expbar_entity_body", out _);
        if (sym == null) return;
        const float bgW = 1920, bgH = 21;
        UiLayout.Reflow(m, "adventure_expbar_entity_body", new Vector2(bgW, bgH), new Vector2(stage.X, bgH),
            ("m_bg", UiLayout.Edge.Left | UiLayout.Edge.Right), ("m_notPenaltyBg", UiLayout.Edge.Left | UiLayout.Edge.Right),
            ("m_point", UiLayout.Edge.CenterH), ("m_bar", UiLayout.Edge.Left | UiLayout.Edge.Right));
        var root = m.BuildRoot(u);
        root.Scale = new Vector2(u, u);
        var holder = new Node2D(); holder.AddChild(root); ui.AddChild(holder);
        var body = DialogBody(root);
        if (body == null) return;
        root.Position = (new Vector2(0, stage.Y - ExpBarLift - bgH) - body.Position) * u;
        // setPoint: the not-penalty bar shows only past the current value; the bar fills to the value (0 for now)
        Hide(root, "m_notPenaltyBar", "m_notPenaltyPin", "m_notPenaltyBg", "m_tooltipPoint");
        body.FindClip("m_point")?.SetNamedText("textField", $"{data.AdventureExpPercent:0.00} %");
        float full = stage.X - 6;                                            // setSize: m_bg.width - 6
        float tail = SetBarValue(body, full, data.AdventureExpPercent);
        BarSplits(body, "adventure_expbar_status_split", full, tail);
    }

    // ---- game menu ---------------------------------------------------------------------------------------------------

    void GameMenu(Control ui)
    {
        var r = Place(ui, "uigamemenudialog", 1f, Vector2.Zero);
        // UIGameMenuDialog::vfunc_43: vfunc_2(8, 5, -(FUN_1418dd0c0() - 6 + FUN_1418dd110())) with feature 0xbd on; the
        // dialog's size is UIComponent's, the bounds shown at its first frame when it was built (before configUI hides
        // m_quest). Those are 498 x 195: the symbol's 208.5 height also counts m_quest's glow on its later frames.
        if (DialogBody(r) is { } gmBody && gmBody.ContentRect() is { } gb)
            AlignDialog(r, gmBody, 8, gb.Size, 5, -(ExpBarLift - 6 + AdventureBarLift));
        // configUI hides the quest and pet attack buttons; no pet (initPetInfo) and no contract (initContract(true)) for now
        Hide(r, "m_quest", "m_petInfo", "m_petAttack", "m_contract");
        // OnLoadComplete -> LoadRenderTarget: the face portrait (faceView) in m_renderTarget
        if (DialogBody(r) is { } faceBody) PortraitView.Attach(faceBody, faceBody.Direct("m_renderTarget"), assets.Packs, Settings.NifXml);
        var menus = r.FindClip("m_menus");
        if (menus == null) return;
        // RefreshMenu -> addMenuEntity per top item; SystemDropDownContainer.addEntity builds the entry class, sets the icon
        // (getDefinitionByName("export_common_menu_" + layerName) into m_layerBtn and m_layerEffect) and the caption tag
        foreach (var item in GameMenuTable.TopItems(assets.Table("table/gamemenu.xml"), data.Locale, f => !data.FeaturesOff.Contains(f)))
        {
            var entry = r.Movie.BuildClass(item.Group ? "UIGameMenuDialog_group" : "UIGameMenuDialog_menu", u);
            if (entry == null) continue;
            entry.Name = item.Name;
            entry.Position = new Vector2(item.Col * 56, 120 - item.Row * 60);
            menus.AddChild(entry);
            menus.MoveChild(entry, 0);                                  // addChildAt(entry, 0): a later entry draws under the earlier ones
            var btn = entry.FindClip("m_button");
            if (btn != null)
            {
                btn.GotoLabel("up");
                Hide(btn, "m_notifyUp", "m_notifyCount", "m_notifyNew", "m_notifyRedCircle", "m_eventPopup", "m_hitArea");
                foreach (var layer in new[] { "m_layerBtn", "m_layerEffect" })
                {
                    var holder = btn.FindClip(layer);
                    if (holder == null) continue;
                    foreach (var n in holder.GetChildren()) n.QueueFree();
                    if (assets.Common.BuildClass("export_common_menu_" + item.Name, u) is { } icon) holder.AddChild(icon);
                }
            }
            // setMenuTag: tag.text, then TextFieldEx.setVerticalAlign(tag, VALIGN_BOTTOM)
            if (entry.Children.FirstOrDefault(c => c.Name == "tag")?.Node is TextNode tag) { tag.Text = assets.Str(item.Caption); tag.AlignBottom(); }
        }
    }

    int tick;
    public override void _Process(double delta)
    {
        if (!Field.LoadComplete) return;            // the field builds over several frames behind the loading screen
        if (mapImage != null && ++tick % 6 == 1) UpdateMinimap();
        target?.Process(delta);
        if (shotDir == null) return;
        if (frames == 250 && chatInput != null) { chatInput.Edit.Text = "hello from the chat box"; chatInput.Edit.EmitSignal(LineEdit.SignalName.TextSubmitted, chatInput.Edit.Text); }
        if (++frames == 300)
        {
            DirAccess.MakeDirRecursiveAbsolute(shotDir);
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(shotDir, "hud.png"));
            GetTree().Quit();
        }
    }
}
