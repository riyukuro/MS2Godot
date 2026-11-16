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
    MovieClipNode chat; TextInputWidget chatInput; Label chatLog;
    string shotDir; int frames;
    float u; Vector2 stage;                 // UI scale and stage size in stage units (see UiScale)
    Player player; Control uiRoot;

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
        NavigatorWidget(uiRoot);
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

    MinimapInfo mapInfo; Control mapClip; TextureRect mapImage; MovieClipNode mapPlayer;
    static readonly Vector2 MapMaskPos = new(2, 82), MapMaskSize = new(288, 222);   // the movie's map area (m_mask) in movie units
    const float MapZoom = 1f;       // MinimapDialog m_zoom (0.5 .. 1.5): movie units per image pixel in player-centred mode

    void Minimap(Control ui)
    {
        var origin = new Vector2(stage.X - 295, 0);            // the panel's right edge (movie x 295) sits on the stage's right edge
        // MinimapDialog loads Image/map/minimap/<field>.dds into m_field under m_mask and keeps the player at the mask's centre;
        // the client places entities with the projection from mapxblock/<xblock>.xml <minimap>
        try
        {
            var x = System.Xml.Linq.XDocument.Parse(System.Text.Encoding.UTF8.GetString(assets.Packs.Read("Xml", $"mapxblock/{FieldName()}.xml")));
            mapInfo = MinimapInfo.Parse(x);
        }
        catch (Exception e) { GD.PushWarning("minimap: " + e.Message); }
        var tex = mapInfo == null ? null : assets.ImageFile(mapInfo.ImageEntry);
        if (tex != null)
        {
            mapClip = new Control { Position = (origin + MapMaskPos) * u, Size = MapMaskSize * u, ClipContents = true, MouseFilter = Control.MouseFilterEnum.Ignore };
            mapImage = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, Texture = tex, Size = tex.GetSize() * MapZoom * u, MouseFilter = Control.MouseFilterEnum.Ignore };
            mapClip.AddChild(mapImage);
            ui.AddChild(mapClip);
        }
        var r = Place(ui, "uiminimapdialog", 1f, origin);
        Hide(r, "m_controller", "m_resize", "m_sliderani", "m_panel", "m_mask", "m_report");    // m_report: reportVisible is off in the reference
        r.FindClip("m_channel")?.SetText($"Ch. {data.Player.Channel}");
        r.FindClip("m_fieldname")?.SetText(data.FieldName);
        if (tex != null)
        {
            mapPlayer = r.Movie.BuildClass("minimap_entity_player", u);
            if (mapPlayer != null) { mapPlayer.Position = MapMaskPos + MapMaskSize / 2; r.AddChild(mapPlayer); }
        }
    }

    /// <summary>
    /// UILevelUpPackageWidgetDialog ("Navigator"): an event widget (uieventwidgetdialog) docked by table/alignwidget.xml at the
    /// top right, offsetX -298, i.e. its right edge on the minimap's left edge. setData: title, reward slot, gauge or reward button.
    /// </summary>
    void NavigatorWidget(Control ui)
    {
        var r = Place(ui, "uieventwidgetdialog", 1f, Vector2.Zero);
        var box = r.ContentRect() ?? new Rect2(0, 0, 89, 114);      // the movie draws the dialog mid-stage; dock its box instead
        r.Position = (new Vector2(stage.X - 298, 0) - new Vector2(box.End.X, box.Position.Y)) * u;
        var dlg = r.AllClips().FirstOrDefault(c => c.Direct("m_title") != null) ?? r;     // the EventWidgetDialog instance
        dlg.SetNamedText("m_title", assets.Str("$s_levelup_package_widget"));
        bool ready = data.NavigatorRewardReady;
        dlg.SetNamedVisible("m_rewardBtn", ready);
        dlg.SetNamedVisible("m_effect", ready);
        dlg.SetNamedVisible("m_gageBar", !ready);
        var slot = r.FindClip("m_slot");
        var icon = assets.ImageFile($"item/icon/{data.NavigatorRewardItem}.png");
        if (slot != null && icon != null)
        {
            var b = slot.ContentRect() ?? new Rect2(0, 0, 60, 60);
            var spr = new Sprite2D { Texture = icon, Centered = false, Position = b.Position, Scale = b.Size / icon.GetSize() };
            slot.AddChild(spr);
            if (data.NavigatorRewardCount > 0)
            {
                var count = new Label { Text = data.NavigatorRewardCount.ToString(), Position = b.Position + new Vector2(b.Size.X - 20, b.Size.Y - 16), Size = new Vector2(18, 14), HorizontalAlignment = HorizontalAlignment.Right };
                count.AddThemeFontSizeOverride("font_size", 12);
                count.AddThemeColorOverride("font_outline_color", Colors.Black); count.AddThemeConstantOverride("outline_size", 3);
                slot.AddChild(count);
            }
        }
    }

    static string FieldName()
    {
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--field") return args[i + 1];
        return "02000001_tw_tria";
    }

    Player FindPlayer(Node n)
    {
        if (n is Player p) return p;
        foreach (var c in n.GetChildren()) { var f = FindPlayer(c); if (f != null) return f; }
        return null;
    }

    static Vec3 ToMs2(Vector3 g) => new(g.X, -g.Z, g.Y);

    void UpdateMinimap()
    {
        if (player == null) { player = FindPlayer(this); if (player == null) return; }
        var size = mapImage.Texture.GetSize();
        var p = ToMs2(player.GlobalPosition);
        var (pu, pv) = mapInfo.ToImage(p);
        var px = new Vector2((float)pu, (float)pv) * size * MapZoom;           // player in movie units on the scaled image
        mapImage.Position = (MapMaskSize / 2 - px) * u;
        if (mapPlayer != null && player.Visual != null)
        {
            var (fu, fv) = mapInfo.ToImage(ToMs2(player.GlobalPosition + player.Visual.GlobalBasis.Z * 100));
            var d = new Vector2((float)(fu - pu), (float)(fv - pv)) * size;
            if (d.LengthSquared() > 1e-6f) mapPlayer.Rotation = Mathf.Atan2(d.X, -d.Y);       // the arrow art points up
        }
    }

    // ---- chat ---------------------------------------------------------------------------------------------------

    void Chat(Control ui)
    {
        const float f = 1f;
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
        const float f = 1f;
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
        // configUI hides the quest and pet attack buttons; no pet (initPetInfo) and no contract (initContract(true)) for now
        Hide(r, "m_quest", "m_petInfo", "m_petAttack", "m_contract");
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
            entry.SetNamedText("tag", assets.Str(item.Caption));
        }
    }

    int tick;
    public override void _Process(double delta)
    {
        if (mapImage != null && ++tick % 6 == 1) UpdateMinimap();
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
