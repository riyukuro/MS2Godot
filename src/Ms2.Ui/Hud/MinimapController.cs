using Ms2.Avm2;
using Ms2.Formats.Model;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Net.Wire;

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
    /// <summary>WorldService::FUN_141bc80c0(channel): asks to change channel; false keeps the current one selected.</summary>
    public Func<int, bool> ChangeChannel = _ => false;

    /// <summary>The field's channel group name (string table 0x17), Notify's last argument; "" when the client country is 2.</summary>
    public string ChannelGroupName = "";

    /// <summary>UIMinimapDialog::Notify @0x141a38b60 (FUN_141a42800): the server event after a DynamicChannel; the movie's buildChannelList(rows, selected, s_word_ch, group).</summary>
    public void Notify()
    {
        if (Host?.Entity == null) return;
        var f = Model.Field;
        string chPrefix = Model.Tables.Common("s_word_ch") ?? "";
        var rows = new List<object>();
        int selected = 0;
        foreach (var id in f.Channels)
        {
            string label = $"{chPrefix} {id}";   // StringFormat("{0} {1}", GetStringCommon(0x579), id)
            if (id == f.Channel) selected = rows.Count;
            rows.Add(new Dictionary<string, object> { ["id"] = id, ["label"] = label, ["labelDropdown"] = label });
        }
        Host.Invoke("buildChannelList", rows, selected, chPrefix, ChannelGroupName ?? "");
    }

    /// <summary>UIMinimapDialog::ApplySelectedChannel @0x141a44ed0: the movie's setSelectedChannel with m_channel.</summary>
    public void ApplySelectedChannel()
    {
        if (Host?.Entity != null) Host.Invoke("setSelectedChannel", Model.Field.Channel);
    }

    public AsObject FieldClip { get; private set; }      // m_clip228
    AsObject playerClip, arrow;                          // m_player's clip, m_clip238
    string playerState;
    bool fieldLoaded;                                    // m_1fc
    double viewScale = 1;                                // MinimapViewport m_fScale
    double centerX, centerY, scale = 1;                  // m_208, m_scale (OnUpdateCenter)
    ulong lastRefresh;                                   // m_timer474

    /// <summary>
    /// OnLoadComplete (uiminimapentity.cpp:1104): UIDialog's, then BuildUIData and BuildField; last (:1127-1131), when the
    /// field allows pings, the dialog root's "click" (FUN_1422c2a70, asmovieclip.cpp:236) sends a red ping and its
    /// "rightClick" (FUN_1422c2c30, asmovieclip.cpp:268) a blue one. The client adds them to m_90, which UIDialog::vfunc_41
    /// (uidialog.cpp:930-935) makes from m_30's own value, so it is the same AS object as m_30: the dialog root, which is
    /// Host.Entity. The client loads a movie once per OnLoadComplete; here the listeners are added once per root object,
    /// because each Invoke wraps the delegates anew and AS3 would not see a second add as the same listener.
    /// </summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        BuildUIData();
        BuildField();
        if (PingAllowed && Host.Entity != null && !ReferenceEquals(pingListenersOn, Host.Entity))
        {
            pingListenersOn = Host.Entity;
            Host.Invoke(Host.Entity, "addEventListener", "click", redPing ??= _ => { RedPing(); return null; }, false, 0, false);
            Host.Invoke(Host.Entity, "addEventListener", "rightClick", bluePing ??= _ => { BluePing(); return null; }, false, 0, false);
        }
    }

    Func<object[], object> redPing, bluePing;
    object pingListenersOn;

    // ---- pings ---------------------------------------------------------------------------------------------------------

    /// <summary>IsPingAllowed @0x141a45ce0 (uiminimapentity.cpp:2521): the field data's m_bMinimapPing (&lt;property minimapPing&gt;).</summary>
    public bool PingAllowed;
    /// <summary>The ping send (MinimapPing 0xB1); null = not connected, nothing is sent.</summary>
    public Action<IOutPacket> Send;
    /// <summary>
    /// GetCharacterName @0x141a45d50 (uiminimapentity.cpp:2620): the name of the field pc with that character id
    /// (WorldService::FUN_141bd1530, vfunc_77), else "".
    /// STILL-OPEN: the proxies' m_name fallback (:2625-2629); this minimap holds no player proxies.
    /// </summary>
    public Func<long, string> CharacterName = _ => "";
    /// <summary>PlaySystemSound(key, ""); null = no sound player.</summary>
    public Action<string> SystemSound;

    /// <summary>A UIMinimapEntityPing: its clip and m_text / m_guideName.</summary>
    sealed class Ping
    {
        public AsObject Clip;
        public string Text = "";
    }

    /// <summary>m_pings: the pings by uid.</summary>
    readonly Dictionary<long, Ping> pings = new();

    /// <summary>The ping with that uid has a clip on the movie; its m_text (the sender's name) when it does.</summary>
    public bool TryGetPing(long uid, out AsObject clip, out string text)
    {
        clip = null; text = null;
        if (!pings.TryGetValue(uid, out var p)) return false;
        clip = p.Clip; text = p.Text;
        return true;
    }

    public const string RedPingSound = "System_RedPing_01", BluePingSound = "System_BluePing_01";   // :338, :337

    /// <summary>FUN_141a456c0 (uiminimapentity.cpp:2471): GetCursorClientPos, then SendMinimapPing(0). The cursor point is
    /// only read by GetPingPosition's world path, which SendMinimapPing does not take; a click on the movie has the cursor
    /// in the client area.</summary>
    public void RedPing() => SendMinimapPing(0);
    /// <summary>FUN_141a45720 (uiminimapentity.cpp:2481): SendMinimapPing(1).</summary>
    public void BluePing() => SendMinimapPing(1);

    /// <summary>
    /// SendMinimapPing @0x141a45020 (uiminimapentity.cpp:2545-2564): nothing without a local pc, the movie or IsPingAllowed;
    /// then, when the movie's mouseContains() is true and GetPingPosition(true) has the point, MinimapPing 0xB1 with the
    /// pc's character id, the kind and the point.
    /// </summary>
    public void SendMinimapPing(int kind)
    {
        if (LocalPlayer == null || Host?.Entity == null || !PingAllowed) return;
        var contains = Host.Invoke("mouseContains");
        if (contains is true && GetPingPosition(out var pos))
            Send?.Invoke(new MinimapPingRequest(CharacterId, (byte)kind, pos));
    }

    /// <summary>GetPingPosition @0x141a451b0, bLocal true (uiminimapentity.cpp:2641-2653): the movie's getLocal(); when it is
    /// an object, its x and y (z stays 0) and true.</summary>
    bool GetPingPosition(out System.Numerics.Vector3 pos)
    {
        pos = default;
        if (Host.Invoke("getLocal") is not AsObject local) return false;
        if (Host.Get(local, "x") is { } x) pos.X = (float)Num(x);
        if (Host.Get(local, "y") is { } y) pos.Y = (float)Num(y);
        return true;
    }

    /// <summary>
    /// AddPing @0x141a3b130 (uiminimapentity.cpp:2127-2143), from OnMinimapPing: only when IsPingAllowed. A known uid is
    /// updated; a new one is a UIMinimapEntityPing (SetPing @0x141a34f40: icon "ping", layer player, frame red_flag, guide
    /// and text shown, group type 10) made through FUN_141a3e7d0, and once its clip is in, the movie's onTabKeyDown and
    /// onTabKeyUp, then it is kept and updated.
    /// </summary>
    public void AddPing(long uid, int color, Vec3 pos)
    {
        if (!PingAllowed) return;
        if (pings.TryGetValue(uid, out var p)) { UpdatePing(uid, color, p, pos); return; }
        if (Host?.Entity == null) return;
        var clip = Create("ping", "red_flag", "player", "", true, true, false, pos, true, 10);
        if (clip == null) return;
        Host.Invoke("onTabKeyDown");
        Host.Invoke("onTabKeyUp");
        p = new Ping { Clip = clip };
        pings[uid] = p;
        UpdatePing(uid, color, p, pos);
    }

    /// <summary>UIMinimapEntityPing::UpdatePing @0x141a35000 (uiminimapentity.cpp): with a clip, color 0 is OnNotice, 1 is
    /// OnAlert, each then SetPositionFloat(x, y) and its sound; m_guideName and m_text become GetCharacterName(uid).</summary>
    void UpdatePing(long uid, int color, Ping p, Vec3 pos)
    {
        if (p.Clip == null) return;
        if (color is 0 or 1)
        {
            Host.Invoke(p.Clip, color == 0 ? "OnNotice" : "OnAlert");
            Host.Set(p.Clip, "x", (float)pos.X);
            Host.Set(p.Clip, "y", (float)pos.Y);
            SystemSound?.Invoke(color == 0 ? RedPingSound : BluePingSound);
        }
        p.Text = CharacterName(uid) ?? "";
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
            string label = $"{chPrefix} {id}";   // StringFormat("{0} {1}", GetStringCommon(0x579), id)
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
        // RemovePingEntities @0x141a47190 (FUN_141a3f730: removeEntity(clip, "")), then ClearEntityContainers' m_pings.RemoveAll
        foreach (var p in pings.Values) if (p.Clip != null) Host.Invoke("removeEntity", p.Clip, "");
        pings.Clear();
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
    AsObject Create(string icon, string frame, string layer, string text, bool showText, bool showGuide, bool inFieldNpc, Vec3 pos, bool visible,
        int groupType = 1)
    {
        if (string.IsNullOrEmpty(icon)) return null;
        bool inView = WorldToMinimap(pos, out var x, out var y);
        return Host.Invoke("createEntity", Host.Obj(("mcname", icon), ("frame", frame ?? ""), ("layer", layer ?? ""), ("showtext", showText),
            ("showguide", showGuide), ("name", text), ("guideName", ""), ("inFieldNpc", inFieldNpc), ("groupType", groupType),
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
            case "OnChangeChannel":
                // PopInt; WorldService::FUN_141bc80c0(channel) false: ApplySelectedChannel (uiminimapentity.cpp:1274)
                if (args.Length > 0 && !ChangeChannel(Rt.ToInt32(args[0]))) ApplySelectedChannel();
                return true;
            case "OnToggleMode": case "OnCheckIcon": case "OnWheelStepChange":
            case "OnMorePlayers": case "OnReport": case "OnPopupMenu":
                return true;        // the other dialogs, server and options they reach are not here
        }
        return false;
    }

    static double Num(object o) => o switch { double d => d, int i => i, float f => f, uint u => u, _ => 0 };
}
