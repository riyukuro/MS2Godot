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
        var ui = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        ui.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(ui);
        var size = new Vector2(1280, 720);

        UpperHud(ui);
        Minimap(ui, size);
        Chat(ui, size);
        Orb(ui, size);
        ExpBar(ui, size);
        GameMenu(ui, size);
    }

    MovieClipNode Place(Control parent, string movie, float scale, Vector2 pos)
    {
        var m = new UiMovie(assets, movie);
        var root = m.BuildRoot(scale);
        root.Scale = new Vector2(scale, scale); root.Position = pos;
        var holder = new Node2D(); holder.AddChild(root); parent.AddChild(holder);
        return root;
    }

    static void Hide(MovieClipNode root, params string[] names) { foreach (var n in names) root.HideNamed(n); }

    void UpperHud(Control ui)
    {
        var r = Place(ui, "uiupperhuddialog", 0.75f, Vector2.Zero);
        Hide(r, "m_searchInput", "m_version", "m_eventText", "m_searchDungeon", "m_pcBangButton", "m_pcBangButtonCn1", "m_pcBangButtonCn2", "m_effectMerat", "m_effectTokenPocket",
            "m_effectHotTime", "m_gamePadOn", "m_gamePadOff", "m_mouseMode", "m_searchButton");
        var bar = r.FindClip("m_noticeButton");
        bar.FindClip("m_time")?.SetNamedText("timeText", DateTime.Now.ToString("HH:mm"));
        bar.FindClip("m_meso")?.SetNamedText("mesoText", data.Mesos.ToString());
        bar.FindClip("m_marat")?.SetNamedText("maratText", data.Merets.ToString());
    }

    void Minimap(Control ui, Vector2 size)
    {
        const float s = 0.70f;
        var r = Place(ui, "uiminimapdialog", s, new Vector2(size.X - 298 * s + 3 * s, 0));
        Hide(r, "m_players", "m_controller", "m_resize", "m_sliderani");
        r.FindClip("m_channel")?.SetText($"Ch. {data.Player.Channel}");
        r.FindClip("m_fieldname")?.SetText(data.FieldName);
        // the map itself is a render target in the client and is not drawn here: the panel stays a translucent dark frame
        if (r.ChildNamed("m_panel") is { } mp) { mp.BaseColor = new Color(1, 1, 1, 0.55f); foreach (var c in r.AllClips()) c.GotoFrame(c.Frame); }
    }

    void Chat(Control ui, Vector2 size)
    {
        const float s = 0.53f;
        chat = Place(ui, "uimessengerdialog", s, new Vector2(8 - 316 * s, size.Y - 404 * s - 38));
        Hide(chat, "m_chatEmoticonPreview", "m_superEffect", "m_resize");
        // the log background is a flat cyan design colour: the client tints it dark and translucent
        if (chat.ChildNamed("m_textBgColor") is { } bg) { bg.BaseColor = new Color(0, 0, 0, 0.55f); chat.GotoFrame(chat.Frame); foreach (var c in chat.AllClips()) c.GotoFrame(c.Frame); }
        Hide(chat, "m_textOutputMask");
        if (chat.FindClip("m_subject") is { } subject) subject.SetText("Local");
        if (chat.FindClip("m_buttonBar") is { } bar) { int n = 0; foreach (var c in bar.AllClips()) foreach (var t in c.TextFields) t.Text = n++ == 0 ? "Main" : "Combat"; }
        // log: a translucent panel with a label over the movie's log area (the movie's own output field is driven by the client's chat service)
        const float cs = s; var origin = new Vector2(8 - 316 * s, size.Y - 404 * s - 38);
        var area = new Rect2(origin + new Vector2(362, 160) * cs, new Vector2(491, 206) * cs);
        var back = new ColorRect { Color = new Color(0, 0, 0, 0.5f), Position = area.Position, Size = area.Size, MouseFilter = Control.MouseFilterEnum.Ignore };
        ui.AddChild(back);
        chatLog = new Label { Position = area.Position + new Vector2(4, 0), Size = area.Size - new Vector2(8, 4), VerticalAlignment = VerticalAlignment.Bottom, AutowrapMode = TextServer.AutowrapMode.WordSmart, ClipText = true, MouseFilter = Control.MouseFilterEnum.Ignore,
            LabelSettings = new LabelSettings { Font = assets.FontFor("$Font_SD_Neo_1"), FontSize = 12, FontColor = Colors.White, OutlineSize = 2, OutlineColor = new Color(0, 0, 0, 0.8f) } };
        ui.AddChild(chatLog);
        var input = chat.FindClip("m_textInput");
        chatInput = new TextInputWidget { Node = input };
        chatInput.Init();
        chatInput.Edit.TextSubmitted += text =>
        {
            if (text.Trim().Length == 0) return;
            AddChatLine($"{data.Player.Name}: {text}");
            chatInput.Edit.Text = "";
        };
        AddChatLine("Welcome to MapleStory 2.");
    }

    void AddChatLine(string line)
    {
        chatLines.Add(line);
        if (chatLines.Count > 12) chatLines.RemoveAt(0);
        chatLog.Text = string.Join("\n", chatLines);
    }

    void Orb(Control ui, Vector2 size)
    {
        const float s = 0.82f;
        var r = Place(ui, "uimainplayerinfo", s, new Vector2(size.X / 2 - 507 * s, size.Y - 548 * s - 28));
        Hide(r, "m_rideeSP", "m_rideeHP", "m_ridee", "m_rideeName", "m_effect", "m_penalty", "m_hpWarning", "m_job", "m_infinity", "infinity", "m_message", "m_tag");
        r.FindClip("m_playerInfo")?.GotoLabel("stat_show");          // the stat orb fades in with this label
        if (r.FindClip("m_slot") is { } slot) slot.GotoLabel("skill");
        var stat = r.FindClip("m_playerInfo.m_stat");
        SetBar(stat.FindClip("m_hp"), data.HpRatio);
        SetBar(stat.FindClip("m_sp"), data.SpRatio);
        stat.SetNamedText("m_curHp", data.Player.HpCurrent.ToString()); stat.SetNamedText("m_maxHp", data.Player.HpMax.ToString());
        stat.SetNamedText("m_curSp", data.SpCurrent.ToString()); stat.SetNamedText("m_maxSp", data.SpMax.ToString());
        stat.SetNamedText("hpTxt", "Health"); stat.SetNamedText("spTxt", "Spirit");
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

    void ExpBar(Control ui, Vector2 size)
    {
        // the movie's bar art is masked and sliced from several atlases that are not reproduced yet: a flat bar stands in
        var track = new ColorRect { Color = new Color(0.05f, 0.1f, 0.1f, 0.8f), Position = new Vector2(0, size.Y - 9), Size = new Vector2(size.X, 9), MouseFilter = Control.MouseFilterEnum.Ignore };
        var fill = new ColorRect { Color = new Color(0.25f, 0.85f, 0.7f), Position = new Vector2(1, size.Y - 7), Size = new Vector2((size.X - 2) * (float)(data.ExpPercent / 100.0), 5), MouseFilter = Control.MouseFilterEnum.Ignore };
        ui.AddChild(track); ui.AddChild(fill);
        var label = new Label { Text = $"{data.ExpPercent:0.00}%", Position = new Vector2(size.X / 2 - 30, size.Y - 20), MouseFilter = Control.MouseFilterEnum.Ignore,
            LabelSettings = new LabelSettings { Font = assets.FontFor("$Font_SD_Neo_1"), FontSize = 10, FontColor = Colors.White, OutlineSize = 2, OutlineColor = Colors.Black } };
        ui.AddChild(label);
    }

    void GameMenu(Control ui, Vector2 size)
    {
        const float s = 0.62f;
        Place(ui, "uigamemenudialog", s, new Vector2(size.X - 1182 * s, size.Y - 600 * s));
    }

    public override void _Process(double delta)
    {
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
