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

        NoticeTab(uiRoot);
        Minimap(uiRoot);
        NavigatorWidget(uiRoot);
        UpperHud(uiRoot);
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
            mapPlayer = MinimapEntities.Create(r.Movie, "player", "idle", u);
            if (mapPlayer != null) { mapPlayer.Position = MapMaskPos + MapMaskSize / 2; r.AddChild(mapPlayer); }
            mapImage.AddChild(new MinimapPlaces(r.Movie, mapInfo, assets.Packs, mapImage.Size, u));     // portals and npcs (MinimapPlaces.cs)
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
        chatInput = new TextInputWidget { Node = chat.FindClip("m_textInput") };
        chatInput.Init();
        chatInput.Edit.TextSubmitted += text =>
        {
            if (text.Trim().Length == 0) return;
            AddChatLine($"{data.Player.Name}: {text}");
            chatInput.Edit.Text = "";
        };
        GroupChat(ui);
    }

    /// <summary>The log keeps the last lines in view (TextAreaEx position = maxScrollV after each append).</summary>
    void AddChatLine(string line)
    {
        if (chatLog == null) return;
        chatLines.Add(line);
        if (chatLines.Count > 100) chatLines.RemoveAt(0);
        var l = chatLog.Label;
        l.LinesSkipped = 0;
        l.Text = string.Join("\n", chatLines);
        l.LinesSkipped = Math.Max(0, l.GetLineCount() - l.GetVisibleLineCount());
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
    /// (CAlignmentInfo(6, 0, 330) in its constructor); addMission puts a qg_mission_entity at x -1 in m_topContainer and
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
        var at = UiLayout.Align(6, stage, new Vector2(QuestGuideWidth, 0), 0, 330) + new Vector2(-1, 0);
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
        Hide(r, "m_rideeSP", "m_rideeHP", "m_ridee", "m_rideeName", "m_effect", "m_penalty", "m_hpWarning", "m_job", "m_infinity", "infinity", "m_message", "m_gamePadSkillCommon", "m_cubeItem", "m_cubeControl");
        r.FindClip("m_playerInfo")?.GotoLabel("stat_show");          // the stat orb fades in with this label
        var stat = r.FindClip("m_playerInfo.m_stat");
        SetBar(stat.FindClip("m_hp"), data.HpRatio);
        SetBar(stat.FindClip("m_sp"), data.SpRatio);
        stat.SetNamedText("m_curHp", data.Player.HpCurrent.ToString()); stat.SetNamedText("m_maxHp", data.Player.HpMax.ToString());
        stat.SetNamedText("m_curSp", data.SpCurrent.ToString()); stat.SetNamedText("m_maxSp", data.SpMax.ToString());
        stat.SetNamedText("hpTxt", "Health"); stat.SetNamedText("spTxt", "Spirit");
        // MainPlayerInfo.setData: m_tag = "LV " + level + " " + jobName + " " + name (m_rideeName.m_tag stays with its hidden parent)
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
        if (body.FindClip("m_bar") is { } bar && bar.ChildNamed("m_mask") is { } mask)
        {
            mask.Override = new Transform2D(new Vector2((float)Math.Max((stage.X - 8) * data.AdventureExpPercent / 100.0, 0.0001), 0), new Vector2(0, 1), mask.Base.Origin);
            bar.GotoFrame(bar.Frame);
        }
    }

    // ---- game menu ---------------------------------------------------------------------------------------------------

    void GameMenu(Control ui)
    {
        var r = Place(ui, "uigamemenudialog", 1f, Vector2.Zero);
        // UIGameMenuDialog::vfunc_43: vfunc_2(8, 5, -(FUN_1418dd0c0() - 6 + FUN_1418dd110())) with feature 0xbd on; the
        // dialog's size is UIComponent's, the instance's bounds when it was built (CLIK resets the scale to 1)
        if (DialogBody(r) is { } gmBody && UiLayout.SymbolOfClass(r.Movie, "UIGameMenuDialog", out _)?["bounds"] is JsonNode gb)
            AlignDialog(r, gmBody, 8, new Vector2(UiBuilder.F(gb["w"], 498), UiBuilder.F(gb["h"], 208)), 5, -(ExpBarLift - 6 + AdventureBarLift));
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
