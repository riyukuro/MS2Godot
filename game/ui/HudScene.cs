using Godot;
using Ms2.Formats.Pack;

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
    MovieClipNode chat; TextInputWidget chatInput; Label chatLog;
    string shotDir; int frames;
    float u; Vector2 stage;                 // UI scale and stage size in stage units (see UiScale)
    SubViewport mapView; Camera3D mapCam; Node3D player; Control uiRoot;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--hudshot") shotDir = args[i + 1];
        var dataDir = Settings.DataDir;
        if (string.IsNullOrEmpty(dataDir)) { GD.PushError("Set MS2_DATA (the client's Data folder), or fill in local.cfg."); GetTree().Quit(2); return; }
        assets = new UiAssets(new PackSet(dataDir));

        AddChild(GD.Load<PackedScene>("res://field.tscn").Instantiate());      // the field scene, untouched
        var layer = new CanvasLayer { Layer = 10 };
        AddChild(layer);
        uiRoot = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        uiRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(uiRoot);
        var window = new Vector2(1280, 720);
        u = UiScale.For(window); stage = UiScale.Stage(window);

        Minimap(uiRoot);
        UpperHud(uiRoot);
        Chat(uiRoot);
        Quests(uiRoot);
        Orb(uiRoot);
        ExpBar(uiRoot);
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

    static void Hide(MovieClipNode root, params string[] names) { foreach (var n in names) root.HideNamed(n); }

    void UpperHud(Control ui)
    {
        var r = Place(ui, "uiupperhuddialog", 1f, Vector2.Zero);
        Hide(r, "m_searchInput", "m_version", "m_eventText", "m_searchDungeon", "m_pcBangButton", "m_pcBangButtonCn1", "m_pcBangButtonCn2", "m_effectMerat", "m_effectTokenPocket",
            "m_effectHotTime", "m_gamePadOn", "m_gamePadOff", "m_mouseMode", "m_searchButton");
        var bar = r.FindClip("m_noticeButton");
        bar.FindClip("m_time")?.SetNamedText("timeText", DateTime.Now.ToString("HH:mm"));
        bar.FindClip("m_meso")?.SetNamedText("mesoText", data.Mesos.ToString());
        bar.FindClip("m_marat")?.SetNamedText("maratText", data.Merets.ToString());
    }

    // ---- minimap: the field seen from above, rendered into the frame -------------------------------------------

    void Minimap(Control ui)
    {
        var origin = new Vector2(stage.X - 295, 0);            // the panel's right edge (movie x 295) sits on the stage's right edge
        // the client's MiniMapViewport renders the field from above into a texture; here a second camera does the same
        mapView = new SubViewport { Size = new Vector2I((int)(288 * u), (int)(222 * u)), RenderTargetUpdateMode = SubViewport.UpdateMode.Once, TransparentBg = false, HandleInputLocally = false };
        mapCam = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 2600, Near = 1, Far = 20000, Current = true };
        mapView.AddChild(mapCam);
        AddChild(mapView);
        var tex = new TextureRect { Texture = mapView.GetTexture(), Position = (origin + new Vector2(2, 82)) * u, Size = new Vector2(288, 222) * u, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = Control.MouseFilterEnum.Ignore };
        ui.AddChild(tex);
        var r = Place(ui, "uiminimapdialog", 1f, origin);
        Hide(r, "m_players", "m_controller", "m_resize", "m_sliderani", "m_panel", "m_mask");
        r.FindClip("m_channel")?.SetText($"Ch. {data.Player.Channel}");
        r.FindClip("m_fieldname")?.SetText(data.FieldName);
    }

    Node3D FindPlayer(Node n)
    {
        if (n is Player p) return p;
        foreach (var c in n.GetChildren()) { var f = FindPlayer(c); if (f != null) return f; }
        return null;
    }

    void UpdateMinimap()
    {
        if (player == null) { player = FindPlayer(this); if (player == null) return; mapView.World3D = GetViewport().World3D; }
        var p = player.GlobalPosition;
        mapCam.GlobalPosition = new Vector3(p.X, p.Y + 6000, p.Z);
        mapCam.RotationDegrees = new Vector3(-90, 0, 0);
        mapView.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;       // refreshed a few times a second, not every frame
    }

    // ---- chat ---------------------------------------------------------------------------------------------------

    void Chat(Control ui)
    {
        const float f = 0.8f;
        var origin = new Vector2(10 - 319 * f, stage.Y - 20 - 402 * f);
        chat = Place(ui, "uimessengerdialog", f, origin);
        Hide(chat, "m_chatEmoticonPreview", "m_superEffect", "m_resize", "m_textOutputMask", "m_textBgColor");
        if (chat.FindClip("m_subject") is { } subject) subject.SetText("Local");
        if (chat.FindClip("m_buttonBar") is { } bar) { int n = 0; foreach (var c in bar.AllClips()) foreach (var t in c.TextFields) t.Text = n++ == 0 ? "Main" : "Combat"; }
        // log: a translucent panel with a label over the movie's log area (the movie's own output field is driven by the client's chat service)
        var area = new Rect2((origin + new Vector2(362, 160) * f) * u, new Vector2(491, 206) * f * u);
        ui.AddChild(new ColorRect { Color = new Color(0, 0, 0, 0.5f), Position = area.Position, Size = area.Size, MouseFilter = Control.MouseFilterEnum.Ignore });
        chatLog = new Label
        {
            Position = area.Position + new Vector2(4, 0), Size = area.Size - new Vector2(8, 4), VerticalAlignment = VerticalAlignment.Bottom, AutowrapMode = TextServer.AutowrapMode.WordSmart, ClipText = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            LabelSettings = new LabelSettings { Font = assets.FontFor("$Font_SD_Neo_1"), FontSize = 12, FontColor = Colors.White, OutlineSize = 2, OutlineColor = new Color(0, 0, 0, 0.8f) },
        };
        ui.AddChild(chatLog);
        chatInput = new TextInputWidget { Node = chat.FindClip("m_textInput") };
        chatInput.Init();
        chatInput.Edit.TextSubmitted += text =>
        {
            if (text.Trim().Length == 0) return;
            AddChatLine($"{data.Player.Name}: {text}");
            chatInput.Edit.Text = "";
        };
        AddChatLine("Welcome to MapleStory 2.");
        // the group chat button above the window
        var btn = new ColorRect { Color = new Color(0.1f, 0.12f, 0.15f, 0.9f), Position = (origin + new Vector2(319, 110) * f) * u, Size = new Vector2(70, 32) * u, MouseFilter = Control.MouseFilterEnum.Ignore };
        ui.AddChild(btn);
        btn.AddChild(new Label { Text = "Group Chat", Position = new Vector2(4, 8) * u, MouseFilter = Control.MouseFilterEnum.Ignore, LabelSettings = new LabelSettings { Font = assets.FontFor("$Font_SD_Neo_3"), FontSize = (int)(11 * u / 0.67f), FontColor = Colors.White } });
    }

    void AddChatLine(string line)
    {
        chatLines.Add(line);
        if (chatLines.Count > 12) chatLines.RemoveAt(0);
        chatLog.Text = string.Join("\n", chatLines);
    }

    // ---- quest guide: the field mission panel with stub goals ---------------------------------------------------

    void Quests(Control ui)
    {
        var m = new UiMovie(assets, "uiquestguidedialog");
        var panel = m.BuildClass("qg_mission_entity", u);
        if (panel == null) return;
        panel.Scale = new Vector2(u, u); panel.Position = new Vector2(stage.X - 298, 350) * u;
        var holder = new Node2D(); holder.AddChild(panel); ui.AddChild(holder);
        panel.SetNamedText("m_chapterName", "Exploration Goals");
        Hide(panel, "m_toggle");
        string[] goals = { "Stub goal one (0/1)", "Stub goal two (0/1)", "Stub goal three (0/3)", "Stub goal four (0/5)" };
        var font = assets.FontFor("$Font_SD_Neo_1");
        for (int i = 0; i < goals.Length; i++)
        {
            var l = new Label { Text = goals[i], Position = new Vector2(12, 36 + i * 18), MouseFilter = Control.MouseFilterEnum.Ignore, Scale = Vector2.One, LabelSettings = new LabelSettings { Font = font, FontSize = 13, FontColor = new Color(0.9f, 0.9f, 0.9f) } };
            var h = new Node2D { Position = Vector2.Zero }; panel.AddChild(h); h.AddChild(l);
        }
    }

    // ---- HP/SP orb and the skill slots ----------------------------------------------------------------------------

    void Orb(Control ui)
    {
        const float f = 1.2f;
        var r = Place(ui, "uimainplayerinfo", f, new Vector2(stage.X / 2 - 507 * f, stage.Y - 548 * f - 24));
        Hide(r, "m_rideeSP", "m_rideeHP", "m_ridee", "m_rideeName", "m_effect", "m_penalty", "m_hpWarning", "m_job", "m_infinity", "infinity", "m_message", "m_tag", "m_gamePadSkillCommon", "m_cubeItem", "m_cubeControl");
        r.FindClip("m_playerInfo")?.GotoLabel("stat_show");          // the stat orb fades in with this label
        var stat = r.FindClip("m_playerInfo.m_stat");
        SetBar(stat.FindClip("m_hp"), data.HpRatio);
        SetBar(stat.FindClip("m_sp"), data.SpRatio);
        stat.SetNamedText("m_curHp", data.Player.HpCurrent.ToString()); stat.SetNamedText("m_maxHp", data.Player.HpMax.ToString());
        stat.SetNamedText("m_curSp", data.SpCurrent.ToString()); stat.SetNamedText("m_maxSp", data.SpMax.ToString());
        stat.SetNamedText("hpTxt", "Health"); stat.SetNamedText("spTxt", "Spirit");
        r.SetNamedText("m_tag", $"LV {data.Player.Level} {StubCharacterList.JobNames.GetValueOrDefault(data.Player.Job, "Beginner")} {data.Player.Name}");

        // slots: playerinfo_dialog_slot shows its "skill" label; the skill list is a fixed grid of 16 slot components (Q W E R / A S D F, 1 2 3 4 / 5 6 7 8)
        if (r.FindClip("m_slot") is { } slot)
        {
            slot.GotoLabel("skill");
            var list = slot.FindClip("m_skillCommon");
            list?.GotoLabel("show");
            string keys = "QWERASDF12345678";
            int n = 0;
            if (list != null)
                foreach (var c in list.Children.OrderBy(c => c.Def["depth"]?.GetValue<int>() ?? 0))
                    if (c.Node is MovieClipNode slotClip && slotClip.ClassName == "playerinfo_slot_skill" && n < keys.Length)
                    {
                        string key = keys[n++].ToString();
                        var sk = slotClip.FindClip("m_shortcutKey");
                        if (sk != null) foreach (var cl in sk.AllClips()) foreach (var t in cl.TextFields) t.Text = key;
                        slotClip.GotoLabel("up");
                        foreach (var cl in slotClip.AllClips()) foreach (var t in cl.TextFields) if (t.Text != key) t.Text = "";
                    }
        }
    }

    /// <summary>The bar fill is clipped by m_mask, a rectangle the movie scales to the value (bottom up).</summary>
    static void SetBar(MovieClipNode bar, double ratio)
    {
        var mask = bar?.ChildNamed("m_mask");
        if (mask == null) return;
        var b = mask.MaskBounds; var baseT = mask.Base;
        float a = baseT.X.X, y0 = baseT.Origin.Y, x0 = baseT.Origin.X;
        mask.Override = new Transform2D(new Vector2(a, 0), new Vector2(0, (float)Math.Max(ratio, 0.0001)), new Vector2(x0, y0 + b.Size.Y * (float)(1 - ratio)));
        bar.GotoFrame(bar.Frame);
    }

    // ---- EXP bar (from the movie) ----------------------------------------------------------------------------------

    void ExpBar(Control ui)
    {
        var r = Place(ui, "uiexpbar", 1f, new Vector2(0, stage.Y - 237));
        Hide(r, "m_restBar", "m_restPin", "m_restBg");
        var body = r.FindClip("__id0_");
        body?.FindClip("m_point")?.SetNamedText("textField", $"{data.ExpPercent:0.00}%");
        var bar = body?.FindClip("m_bar");
        var mask = bar?.ChildNamed("m_mask");
        if (mask != null)
        {
            mask.Override = new Transform2D(new Vector2((float)(1912 * data.ExpPercent / 100.0), 0), new Vector2(0, 1), mask.Base.Origin);
            bar.GotoFrame(bar.Frame);
        }
    }

    // ---- game menu ---------------------------------------------------------------------------------------------------

    void GameMenu(Control ui)
    {
        var r = Place(ui, "uigamemenudialog", 1f, new Vector2(stage.X - 1182, stage.Y - 600));
        // the client fills m_menus from the menu table and loads each icon as an image at run time; here the labels sit on flat stand-in tiles
        string[] labels = { "Character", "Adventure", "Lifestyle", "Community", "Home", "Menu" };
        Color[] tints = { new(0.9f, 0.5f, 0.2f), new(0.4f, 0.6f, 0.9f), new(0.8f, 0.8f, 0.9f), new(0.3f, 0.6f, 0.9f), new(0.9f, 0.3f, 0.3f), new(0.8f, 0.7f, 0.5f) };
        var font = assets.FontFor("$Font_SD_Neo_3");
        float tile = 44 * u, gap = 44 * u;
        float x0 = (stage.X - 20) * u - labels.Length * (tile + gap), y0 = (stage.Y - 100) * u;
        for (int i = 0; i < labels.Length; i++)
        {
            var t = new ColorRect { Color = tints[i], Position = new Vector2(x0 + i * (tile + gap), y0), Size = new Vector2(tile, tile), MouseFilter = Control.MouseFilterEnum.Ignore };
            ui.AddChild(t);
            t.AddChild(new Label { Text = labels[i], Position = new Vector2(-6 * u, tile + 1), MouseFilter = Control.MouseFilterEnum.Ignore, LabelSettings = new LabelSettings { Font = font, FontSize = (int)(14 * u), FontColor = Colors.White, OutlineSize = 3, OutlineColor = new Color(0, 0, 0, 0.8f) } });
        }
    }

    int tick;
    public override void _Process(double delta)
    {
        if (mapView != null && ++tick % 30 == 1) UpdateMinimap();
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
