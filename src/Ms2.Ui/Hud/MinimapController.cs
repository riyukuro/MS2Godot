using Ms2.Avm2;
using Ms2.Formats.Model;
using Ms2.Formats.World;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIMinimapDialog (ms2/client/ms2/uiminimapentity.cpp), the C++ side of uiminimapdialog: it sends the dialog's data and the
/// field image to the movie, and once the movie has loaded the image (onFieldLoadComplete) it creates an entity for each
/// proxy and place (createEntity with UIMinimapEntity::BuildData) and every 60 ms puts the player's entity and the map under
/// it. The movie's own MinimapDialogMax draws everything (icons, labels, guide arrows, zoom, the player row).
/// </summary>
public sealed class MinimapController : UiController
{
    /// <summary>The ctor @0x141a35220 (uiminimapentity.cpp:1071) does not call SetTweenEnabled: m_c4 stays 0, so no
    /// startOpen / startClose.</summary>
    protected override bool TweenEnabled => false;

    public override string Movie => "uiminimapdialog";
    protected override string EntityMethod => "createEntity";

    /// <summary>UIMinimapDialog's ctor sets the flag 0 (uiminimapentity.cpp:1082): vfunc_56, an always-on one.</summary>
    public override bool AlwaysOn => true;

    /// <summary>OnWorldMap: CUIService::ToggleDialog(0x4e42), the world map.</summary>
    public event Action WorldMap;

    /// <summary>The field's minimap image (field minimap table m_strImageName, e.g. map/minimap/02000001.dds); null = none.</summary>
    public string ImageName;
    /// <summary>The field's minimap camera and image rectangles (CMinimap, read by MinimapViewport::InitFromFieldMinimap).</summary>
    public MinimapInfo Viewport;
    /// <summary>The field hides its minimap (m_nMinimapInvisible).</summary>
    public bool MinimapInvisible;
    public bool Minimize, FullMap;
    /// <summary>The player's profile image (BuildPlayerData imgpath, FUN_141028be0) and character id.</summary>
    public string PlayerImage = "";
    public long CharacterId;

    /// <summary>vfunc_ugc_1 (@0x141a383b0), UIUGCEventDispatcher's profile picture: changePlayer("%I64u" id, path) once the
    /// movie is up. <paramref name="path"/> is GetProfileImagePath(key, 1).</summary>
    public void OnProfileImage(long characterId, string path)
    {
        if (characterId == CharacterId) PlayerImage = path ?? "";
        Host?.Invoke("changePlayer", new object[] { characterId.ToString(System.Globalization.CultureInfo.InvariantCulture), path ?? "" });
    }

    /// <summary>A proxy or place the field holds (WorldService's proxies, the taxi, named and map objects).</summary>
    public sealed class Mark
    {
        public MinimapIcons.Entity Entity;
        public Func<Vec3> Position;
        /// <summary>m_showGuide: a portal's guide arrow (UIMinimapEntity::SetFromPortal).</summary>
        public bool ShowGuide;
        /// <summary>The proxy moves (UpdateProxyPosition each refresh); places never do.</summary>
        public bool Moves;
        /// <summary>The entity the movie made for it (null before the field image loads).</summary>
        public AsObject Clip { get; internal set; }
        internal bool Visible;
    }

    /// <summary>The marks in BuildEntities' order: non-portal proxies, the portals, then the taxi, named and map objects.</summary>
    public readonly List<Mark> Marks = new();

    /// <summary>The local player: world position, GetPlayerHeading (degrees) and dead (GetCurStateId() == 12).</summary>
    public Func<(Vec3 Pos, float Heading, bool Dead)> LocalPlayer;

    public AsObject FieldClip { get; private set; }      // m_clip228
    AsObject playerClip, arrow;                          // m_player's clip, m_clip238
    string playerState;
    bool fieldLoaded;                                    // m_1fc
    double viewScale = 1;                                // MinimapViewport m_fScale
    double centerX, centerY, scale = 1;                  // m_208, m_scale (OnUpdateCenter)
    ulong lastRefresh;                                   // m_timer474

    /// <summary>OnLoadComplete (uiminimapentity.cpp:1104): UIDialog's, then BuildUIData and BuildField.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        BuildUIData();
        BuildField();
    }

    /// <summary>BuildUIData (uiminimapentity.cpp:3456): initData(obj).</summary>
    void BuildUIData()
    {
        bool force = MinimapInvisible || string.IsNullOrEmpty(ImageName);
        var f = Model.Field;
        string chPrefix = Model.Tables.Common("s_word_ch") ?? "";
        var channels = new List<object>();
        int selected = 0;
        foreach (var id in f.Channels)
        {
            // BuildChannelList (uiserverlistdialog.cpp): "{0} {1}" of the channel word and the id
            string label = (chPrefix.Length > 0 ? chPrefix + " " : "") + id;
            if (id == f.Channel) selected = channels.Count;
            channels.Add(new Dictionary<string, object> { ["id"] = id, ["label"] = label, ["labelDropdown"] = label });
        }
        Host.Invoke("initData", Host.Obj(
            ("fieldname", f.Name),
            ("minimize", Minimize),
            ("fullmap", FullMap),
            ("forceMinimize", force),
            ("reportVisible", false),
            ("width", 0), ("height", 0), ("scale", 1.0f), ("bgAlpha", 30), ("mapAlpha", 100),
            ("showentity", ShowEntities()),
            ("channel", channels),
            ("selectedchannel", selected),
            ("channelprefix", chPrefix),
            ("channelgroup", ""),
            ("mapAlphaPrefix", Model.Tables.Common("s_word_minimap_alpha") ?? ""),
            ("bgAlphaPrefix", Model.Tables.Common("s_word_mapbg_alpha") ?? "")));
    }

    /// <summary>
    /// m_showAttrs as BuildUIData sends it (uiminimapentity.cpp:3505-3521): the layers the icon options toggle, with
    /// BuildShowAttributes' default state (the friendly npc layer starts off); the player has no saved iconCheck options.
    /// </summary>
    List<object> ShowEntities()
    {
        var l = new List<object>();
        int order = 0;
        foreach (var (layer, on) in new[] { ("shop", true), ("friendly", false), ("taming_pet", true), ("portal", true), ("buddy", true), ("pc", true), ("text", true) })
            l.Add(new Dictionary<string, object> { ["layer"] = layer, ["state"] = on ? 1 : 0, ["textKey"] = "", ["displayOrder"] = order++, ["iconSuffix"] = layer });
        return l;
    }

    /// <summary>BuildField (uiminimapentity.cpp:2697): removeField, then createFieldEntity(0, image name).</summary>
    void BuildField()
    {
        Host.Invoke("removeField");
        fieldLoaded = false;
        if (string.IsNullOrEmpty(ImageName)) return;
        FieldClip = Host.Invoke("createFieldEntity", 0, ImageName) as AsObject;
    }

    /// <summary>
    /// MinimapViewport::WorldToMinimap (minimapviewport.cpp FUN_141486450 / ProjectPoint): the point on the field image in
    /// its loaded pixels; false outside the minimap camera's view.
    /// </summary>
    bool WorldToMinimap(Vec3 p, out double x, out double y)
    {
        x = y = 0;
        if (Viewport is not { } v) return false;
        var (right, _, up) = v.Axes();
        var d = new Vec3(p.X - v.CamPos.X, p.Y - v.CamPos.Y, p.Z - v.CamPos.Z);
        double fx = (right.X * d.X + right.Y * d.Y + right.Z * d.Z - v.FrLeft) / (v.FrRight - v.FrLeft);
        double fy = (up.X * d.X + up.Y * d.Y + up.Z * d.Z - v.FrBottom) / (v.FrTop - v.FrBottom);
        if (fx < 0 || fx > 1 || fy < 0 || fy > 1) return false;
        x = (fx * (v.ScrRight - v.ScrLeft) - v.ImgLeft) * viewScale;
        y = ((1 - fy) * (v.ScrBottom - v.ScrTop) - v.ImgTop) * viewScale;
        return true;
    }

    /// <summary>MinimapViewport::InitFromFieldMinimap(nScale): m_fScale = nScale / the image rectangle's width.</summary>
    void InitViewport(int width)
    {
        viewScale = 1;
        if (Viewport is { } v && v.ImgRight - v.ImgLeft > 0 && width > 0) viewScale = width / (v.ImgRight - v.ImgLeft);
    }

    /// <summary>
    /// BuildEntities (uiminimapentity.cpp:2900): every mark through FUN_141a3e7d0 (createEntity with BuildData), the local
    /// player's entity first (AddProxy's IsSelf, with createPlayer for the player row), then buildPlayers.
    /// </summary>
    void BuildEntities()
    {
        // RemoveAllEntities: removeEntity for each entity the dialog made
        if (playerClip != null) Host.Invoke("removeEntity", playerClip);
        foreach (var m in Marks) if (m.Clip != null) { Host.Invoke("removeEntity", m.Clip); m.Clip = null; }
        playerClip = arrow = null;
        playerState = "idle";
        if (LocalPlayer != null)
        {
            // AddProxy (uiminimapentity.cpp:3539): createPlayer(BuildPlayerData), then the entity: player / layer player / idle
            Host.Invoke("createPlayer", Host.Obj(("imgpath", PlayerImage ?? ""), ("charid", CharacterId.ToString())));
            var pos = LocalPlayer().Pos;
            playerClip = Create("player", playerState, "player", "", false, false, false, pos, true);
        }
        foreach (var m in Marks)
        {
            m.Visible = m.Entity.Visible;
            m.Clip = Create(m.Entity.Icon, m.Entity.Frame, m.Entity.Layer, m.Entity.Label ?? "", !string.IsNullOrEmpty(m.Entity.Label),
                m.ShowGuide && m.Visible, m.Entity.InFieldNpc, m.Position(), m.Visible);
        }
        Host.Invoke("buildPlayers");
    }

    /// <summary>
    /// FUN_141a3e7d0 (uiminimapentity.cpp:3590): createEntity with UIMinimapEntity::BuildData (uiminimapentity.cpp:373): the
    /// icon, frame, layer, label and guide, and x / y from WorldToMinimap, visible only when the point projects.
    /// </summary>
    AsObject Create(string icon, string frame, string layer, string text, bool showText, bool showGuide, bool inFieldNpc, Vec3 pos, bool visible)
    {
        if (string.IsNullOrEmpty(icon)) return null;
        bool inView = WorldToMinimap(pos, out var x, out var y);
        return Host.Invoke("createEntity", Host.Obj(("mcname", icon), ("frame", frame ?? ""), ("layer", layer ?? ""), ("showtext", showText),
            ("showguide", showGuide), ("name", text), ("guideName", ""), ("inFieldNpc", inFieldNpc), ("groupType", 1),
            ("x", (float)x), ("y", (float)y), ("visible", inView && visible))) as AsObject;
    }

    /// <summary>
    /// vfunc_15 (uiminimapentity.cpp:1173): once the field image is in and the dialog is not minimized, every 60 ms
    /// (m_timer474.Check(60)): RefreshLocalPlayer, then each moving proxy's UpdateProxyPosition. Call each frame with the
    /// time in ms.
    /// </summary>
    public void Process(ulong nowMs)
    {
        if (!fieldLoaded || Minimize) return;
        if (lastRefresh != 0 && nowMs - lastRefresh < 60) return;
        lastRefresh = nowMs;
        RefreshLocalPlayer();
        foreach (var m in Marks)
        {
            // UpdateProxyPosition (uiminimapentity.cpp:2963): a shown entity that no longer projects is hidden
            if (!m.Moves || !m.Visible || m.Clip == null) continue;
            if (!ProjectClip(m.Position(), m.Clip)) { m.Visible = false; Host.SetVisible(m.Clip, false); }
        }
    }

    /// <summary>ProjectClip (uiminimapentity.cpp:2424): shown at the projected point; false when it does not project.</summary>
    bool ProjectClip(Vec3 pos, AsObject clip)
    {
        if (clip == null || !WorldToMinimap(pos, out var x, out var y)) return false;
        Host.SetVisible(clip, true);
        Host.Set(clip, "x", (float)x);
        Host.Set(clip, "y", (float)y);
        return true;
    }

    /// <summary>
    /// RefreshLocalPlayer (uiminimapentity.cpp:2978): the player's entity at its projected point, and (player-centred mode)
    /// the field clip at m_208 - point * m_scale; frame die / idle; m_arrow shown while alive, turned to GetPlayerHeading.
    /// </summary>
    void RefreshLocalPlayer()
    {
        if (playerClip == null || LocalPlayer == null) return;
        var (pos, heading, dead) = LocalPlayer();
        double x = 0, y = 0;
        if (WorldToMinimap(pos, out x, out y))
        {
            Host.SetVisible(playerClip, true);
            Host.Set(playerClip, "x", (float)x);
            Host.Set(playerClip, "y", (float)y);
        }
        if (!FullMap && FieldClip != null)
        {
            Host.SetVisible(FieldClip, true);
            Host.Set(FieldClip, "x", (float)(centerX - (float)x * scale));
            Host.Set(FieldClip, "y", (float)(centerY - (float)y * scale));
        }
        string state = dead ? "die" : "idle";
        if (state != playerState) { playerState = state; Host.Invoke(playerClip, "gotoAndPlay", state); }
        arrow ??= Host.Get(playerClip, "m_arrow") as AsObject;
        if (arrow != null)
        {
            Host.SetVisible(arrow, !dead);
            if (!dead) Host.Set(arrow, "rotation", heading);
        }
    }

    /// <summary>vfunc_28 (uiminimapentity.cpp:1235): the movie's calls.</summary>
    /// <summary>
    /// vfunc_49 @0x141a37150 (uiminimapentity.cpp:1397): "m_fieldname" gets s_minimap_tooltip_field_info with the map's name
    /// (string table 3) and m_nMapLevel, plus "\n" and CFishingManager::GetSpotDescription for a fishing spot (the player's
    /// fishing mastery against the spot's); a text tooltip with onDelay 300, offDelay 60000, align 7.
    /// No field data = no tooltip.
    /// </summary>
    public override IDictionary<string, object> GetTooltip(object[] args)
    {
        if (Rt.ToString(args.ElementAtOrDefault(0)) != "m_fieldname") return null;
        if (Model.Field.MapLevel is not int level) return null;
        var tip = new AsTooltip();
        tip["tooltip"] = "export_common_tooltip_tooltipText";
        tip["onDelay"] = 300;
        tip["offDelay"] = 60000;
        tip.SetTooltipAlign(7);
        var text = Ms2.Quest.ClientFormat.Format(Model.Tables.Common("s_minimap_tooltip_field_info"), Model.Field.Name, (uint)level);
        var extra = Model.Tables.Fishing.Description(Model.Field.MapId, Pc.FishingMastery, Model.Tables.Common);
        if (extra.Length > 0) text += "\n" + extra;
        tip["text"] = text;
        return tip;
    }

    public override bool OnCallback(string name, object[] args)
    {
        switch (name)
        {
            case "onFieldLoadComplete":
                // InitFromFieldMinimap(clip.GetWidthCeil()); m_1fc = true; BuildEntities
                if (args.Length > 0 && args[0] is AsObject clip)
                {
                    InitViewport((int)Math.Ceiling(Num(Host.Get(clip, "width"))));
                    fieldLoaded = true;
                    BuildEntities();
                }
                return true;
            case "OnUpdateCenter":
                // m_208.x, m_208.y, m_scale
                if (args.Length >= 3) { centerX = (float)Num(args[0]); centerY = (float)Num(args[1]); scale = (float)Num(args[2]); }
                return true;
            case "OnChangeMode":
                if (args.Length > 0) FullMap = args[0] is bool b && b;     // m_fullmap (m_212: the named refresh, none here)
                return true;
            case "OnWorldMap": WorldMap?.Invoke(); return true;      // ToggleDialog(0x4e42) (uiminimapentity.cpp:1253)
            case "OnToggleMode": case "OnChangeChannel": case "OnCheckIcon": case "OnWheelStepChange":
            case "OnMorePlayers": case "OnReport": case "OnPopupMenu":
                return true;        // the other dialogs, server and options they reach are not here
        }
        return false;
    }

    static double Num(object o) => o switch { double d => d, int i => i, float f => f, uint u => u, _ => 0 };
}
