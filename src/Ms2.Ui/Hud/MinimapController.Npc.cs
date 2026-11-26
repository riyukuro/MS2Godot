using Ms2.Avm2;
using Ms2.Formats.Model;
using Ms2.Formats.World;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIMinimapDialog's npc proxies (UIMinimapEntityProxy kind 2: AddProxy, UpdateNpc, RefreshNpc), its named objects (the
/// field's interact objects, m_named: AddNamedObject, UpdateNamed, RefreshNamedObject, RefreshNamed) and the overlap pass
/// over the quest targets (FUN_141a42bb0, m_212), with the entities' "tooltip" calls (OnUserFunction, BuildTooltip).
/// </summary>
public sealed partial class MinimapController
{
    /// <summary>An npc's proxy (CProxyGameObject, FUN_1416c2090) as the minimap reads it.</summary>
    public sealed class Npc
    {
        /// <summary>The proxy's object id, m_proxies' key.</summary>
        public int ObjectId;
        /// <summary>GetNpcData; null = none (UpdateNpc then keeps only the quest half).</summary>
        public NpcRow Row;
        /// <summary>The npc's name, the entity's m_text.</summary>
        public string Name = "";
        /// <summary>The proxy's position now (UpdateProxyPosition, RefreshNpc's ProjectClip).</summary>
        public Func<Vec3> Position;
        /// <summary>The proxy's m_68: the npc is dead.</summary>
        public Func<bool> Dead = () => false;
        /// <summary>IsProxyVisible: the npc's actor is shown.</summary>
        public Func<bool> Shown = () => true;
        /// <summary>The entity the movie made for it; null when createEntity gave none (the proxy is then not kept).</summary>
        public AsObject Clip { get; internal set; }
        /// <summary>The entity's m_icon, m_state, m_layer and m_guideName as UpdateNpc last set them.</summary>
        public string Icon { get; internal set; } = "";
        public string State { get; internal set; } = "";
        public string Layer { get; internal set; } = "";
        public string GuideName { get; internal set; } = "";
        /// <summary>m_showGuide: the npc has a quest mark.</summary>
        public bool ShowGuide { get; internal set; }
        /// <summary>m_64: the npc is a target of an active quest (CQuestNavigator::HasMap), so the overlap pass places it.</summary>
        public bool QuestTarget { get; internal set; }
        internal bool Visible;
        /// <summary>m_position: the point AddProxy stored (BuildData's x / y, the overlap pass's projection test).</summary>
        internal Vec3 Pos;
        internal MinimapIcons.Entity Base;
        internal int questStamp = -1;
        internal NpcQuestMarks.Minimap Quest = NpcQuestMarks.Minimap.None;
        internal Func<object[], object> UserFunction;
        internal bool Kept;
    }

    /// <summary>A field interact object (CInteractObject) as AddNamedObject reads it.</summary>
    public sealed class InteractObject
    {
        /// <summary>The object's name (the entity uuid), m_named's key and FindProp's.</summary>
        public string Name = "";
        /// <summary>The interact row id (table/interactobject.xml), its row's minimap icon and type.</summary>
        public int RowId;
        public string Icon = "";
        public int Type;
        /// <summary>GetState: 0 normal, 1 reactable, 2 hidden.</summary>
        public Func<int> State = () => 1;
        public Vec3 Position;
    }

    /// <summary>A UIMinimapEntity of kind 6 for an interact object.</summary>
    internal sealed class Named
    {
        public InteractObject Source;
        public string Icon = "", State = "", Layer = "", Text = "";
        public bool QuestTarget, Visible;
        public Vec3 Pos;
        public AsObject Clip;
        public Func<object[], object> UserFunction;
    }

    /// <summary>m_proxies' npcs by object id, in the order they were added.</summary>
    readonly List<Npc> npcs = new();
    public IReadOnlyList<Npc> Npcs => npcs;
    /// <summary>m_named: the named entities by object name, in the order they were added.</summary>
    readonly List<Named> named = new();
    readonly HashSet<long> occupied = new();

    /// <summary>The npc's quest state for the minimap (CProxyGameObject::GetQuestState, <see cref="NpcQuestMarks.GetMinimap"/>), read
    /// again after each quest event (m_978).</summary>
    public Func<int, NpcQuestMarks.Minimap> NpcQuest = _ => NpcQuestMarks.Minimap.None;
    /// <summary>CQuestNavigator::HasMap(npc id): the npc is a target of an active quest in the field (TraceContext.NpcIds).</summary>
    public Func<int, bool> QuestTargetNpc = _ => false;
    /// <summary>CQuestNavigator::HasNpc(row id): the interact row is a target of an active quest (TraceContext.InteractIds).</summary>
    public Func<int, bool> QuestTargetObject = _ => false;
    /// <summary>A client constant colour by key (NameTagColorFriendly, NameTagColorBoss, ...), 0 when unset.</summary>
    public Func<string, uint> ColorConstant = _ => 0;
    /// <summary>m_minimapCell: the overlap pass's cell in pixels.</summary>
    public int OverlapCell = 50;
    /// <summary>FUN_1418fd2f0: the field is a house (map type 3); no named objects then.</summary>
    public bool HouseField;
    /// <summary>WorldService::GetInteractObjects: the field's interact objects for BuildNamed and RefreshNamed.</summary>
    public Func<IReadOnlyList<InteractObject>> InteractObjects = () => Array.Empty<InteractObject>();

    int questStamp;                                      // m_978: bumped by each quest event
    bool overlapInvalid;                                 // m_212
    AsObject maxPanel;                                   // m_maxPanel (BuildUIData's GetChild "m_maxpanel")

    /// <summary>
    /// vfunc_3 -> AddProxy (uiminimapentity.cpp:3539) for an npc: nothing for one it holds; before the movie it is only kept,
    /// and BuildEntities makes its entity.
    /// </summary>
    public void AddNpc(Npc n)
    {
        if (n == null || FindNpc(n.ObjectId) >= 0) return;
        npcs.Add(n);
        if (Host?.Entity != null && fieldLoaded) CreateNpc(n);
    }

    /// <summary>vfunc_4 -> RemoveProxy (uiminimapentity.cpp:3632): removeEntity(clip, "") (an npc has no character id), then the
    /// proxy leaves m_proxies.</summary>
    public void RemoveNpc(int objectId)
    {
        int i = FindNpc(objectId);
        if (i < 0) return;
        var n = npcs[i];
        npcs.RemoveAt(i);
        if (n.Clip != null && Host?.Entity != null) Host.Invoke("removeEntity", n.Clip, "");
        n.Clip = null;
        n.Kept = false;
    }

    int FindNpc(int objectId)
    {
        for (int i = 0; i < npcs.Count; i++)
            if (npcs[i].ObjectId == objectId) return i;
        return -1;
    }

    /// <summary>Whether the minimap's entity of the npc shows; null without the npc or before its entity is made.</summary>
    public bool? NpcMarkShown(int objectId) => FindNpc(objectId) is var i and >= 0 && npcs[i].Clip is { } c ? ClipVisible(c) : null;

    /// <summary>Whether the minimap's entity of the interact object shows; null without the entity.</summary>
    public bool? NamedMarkShown(string name) => FindNamed(name) is var i and >= 0 && named[i].Clip is { } c ? ClipVisible(c) : null;

    /// <summary>The named entity's icon and layer (UpdateNamed's), null without it.</summary>
    public (string Icon, string Layer)? NamedMark(string name) => FindNamed(name) is var i and >= 0 ? (named[i].Icon, named[i].Layer) : null;

    /// <summary>
    /// AddProxy's npc half (uiminimapentity.cpp:3560-3588): a UIMinimapEntityProxy of kind 2 with m_text the name and
    /// m_position the npc's point now, UpdateNpc, then FUN_141a3e7d0; the proxy is kept (m_proxies) only when that made a
    /// clip, and a quest target sets m_212.
    /// </summary>
    void CreateNpc(Npc n)
    {
        n.Pos = n.Position();
        n.Icon = n.State = n.Layer = n.GuideName = "";
        n.Visible = false;
        UpdateNpc(n);
        var npc = n;
        n.UserFunction ??= a => OnUserFunction(a, () => NpcTooltip(npc));
        n.Clip = Create(n.Icon, n.State, n.Layer, n.Name ?? "", false, n.ShowGuide, n.Base?.InFieldNpc ?? false, n.Pos, n.Visible,
            userFunction: n.UserFunction, guideName: n.GuideName);
        n.Kept = n.Clip != null;
        if (n.QuestTarget) overlapInvalid = true;
    }

    /// <summary>
    /// UpdateNpc @0x141a434a0 (uiminimapentity.cpp:3229-3270):
    /// - a quest mark (GetQuestState's icon is not 0xb): icon "quest", ResolveQuestIcon's frame and layer, the guide on with
    ///   GetQuestGuideText, shown as the npc is (IsProxyVisible);
    /// - else SetFromNpcData (GetNpcIcon, GetStateForIcon, GetLayerForNpc, shown with an icon other than "empty"), then
    ///   IsProxyVisible; a dead npc keeps a boss icon at frame "die" and hides any other;
    /// - an npc that is not a dead boss (bBoss is set in the dead branch alone) and that an active quest targets (HasMap)
    ///   takes icon quest_target_monster on the npc layer and m_64;
    /// - SetFromNpcData leaves the guide name: it keeps the last quest's text with the guide off.
    /// The quest state is read again when a quest event came since the last read.
    /// </summary>
    void UpdateNpc(Npc n)
    {
        n.QuestTarget = false;
        n.ShowGuide = false;
        if (n.questStamp != questStamp)
        {
            n.questStamp = questStamp;
            n.Quest = n.Row != null ? NpcQuest(n.Row.Id) : NpcQuestMarks.Minimap.None;
        }
        var q = n.Quest;
        if (q.HasQuest)
        {
            n.Icon = "quest";
            n.State = q.Frame ?? "";
            n.Layer = q.Layer ?? "";
            n.Visible = n.Shown();
            n.ShowGuide = true;
            n.GuideName = q.Guide ?? "";
            return;
        }
        if (n.Row == null) return;
        var e = n.Base ??= MinimapIcons.Npc(n.Row);
        n.Icon = e.Icon;
        n.State = e.Frame;
        n.Layer = e.Layer;
        n.Visible = e.Visible;
        if (n.Visible) n.Visible = n.Shown();
        bool boss = false;
        if (n.Dead())
        {
            boss = e.Icon is "boss" or "elite_boss" or "event_boss";      // IsBossIcon
            if (boss) n.State = "die";
            else n.Visible = false;
        }
        if (!boss && QuestTargetNpc(n.Row.Id))
        {
            n.Icon = "quest_target_monster";
            n.Layer = "npc";
            n.QuestTarget = true;
        }
    }

    /// <summary>
    /// RefreshNpc @0x141a43760 (uiminimapentity.cpp:3271-3313): UpdateNpc, and when the new icon is set and the icon, frame,
    /// layer or guide moved: the frame alone goes through the clip's gotoAndPlay, anything else through
    /// changeEntityData(clip, {mcname, frame, layer, showguide, guideName}), whose returned object is the clip from then on.
    /// Then a shown npc is placed at its point (hidden when it does not project), and the clip shown as the entity is,
    /// except for a quest target, whose visibility is the overlap pass's. An overlap-hidden target is back at the next pass
    /// here: UpdateNpc sets it shown and ProjectClip shows the clip.
    /// </summary>
    void RefreshNpc(Npc n)
    {
        if (n.Clip == null) return;
        string icon = n.Icon, state = n.State, layer = n.Layer, guide = n.GuideName;
        UpdateNpc(n);
        int changed = 0;
        if (!string.IsNullOrEmpty(n.Icon))
        {
            if (!string.Equals(icon, n.Icon, StringComparison.Ordinal)) changed |= 1;
            if (!string.Equals(state, n.State, StringComparison.Ordinal)) changed |= 2;
            if (!string.Equals(layer, n.Layer, StringComparison.Ordinal)) changed |= 8;
            if (!string.Equals(guide, n.GuideName, StringComparison.Ordinal)) changed |= 0x10;
        }
        if (changed == 2) Host.Invoke(n.Clip, "gotoAndPlay", n.State);
        else if (changed != 0)
        {
            var data = Host.Obj(("mcname", n.Icon), ("frame", n.State), ("layer", n.Layer), ("showguide", n.ShowGuide), ("guideName", n.GuideName ?? ""));
            if (Host.Invoke("changeEntityData", n.Clip, data) is AsObject clip) n.Clip = clip;
        }
        if (n.Visible && !ProjectClip(n.Position(), n.Clip)) n.Visible = false;
        if (!n.QuestTarget) Show(n.Clip, n.Visible);
    }

    /// <summary>
    /// UIMinimapEntityProxy::vfunc_5 (@0x141a33080, uiminimapentity.cpp:716-761) for an npc: a quest mark's text (the quest
    /// lines, <see cref="NpcQuestMarks.Minimap.Text"/>); else a boss (class 4 or 6) s_worldmap_tooltip_boss over the name
    /// with its title and the level ("??" with hide_hpbar_level), with npcID and bossFrameName ("event" with event_icon,
    /// else "boss"); else GetNpcTooltip(npc, "center", true).
    /// STILL-OPEN: the boss tooltip's BuildNpcRewardPreview (WorldMapManager, the reward items) is not ported.
    /// </summary>
    AsObject NpcTooltip(Npc n)
    {
        var tip = Host.Obj();
        string text = n.Name ?? "";
        if (n.Quest.HasQuest) text = n.Quest.Text ?? "";
        else if (n.Row is { } r)
        {
            if (r.Class is 4 or 6)
            {
                string name = Model.Tables.NpcNameWithTitle(r.Id) ?? "";
                text = r.HideHpBarLevel
                    ? ClientFormat.Format(Model.Tables.Common("s_worldmap_tooltip_boss"), name, "??")
                    : ClientFormat.Format(Model.Tables.Common("s_worldmap_tooltip_boss"), name, (ushort)r.Level);
                // the elite frame is for class 3 or 7, which this branch never has
                Host.Set(tip, "npcID", (uint)r.Id);
                Host.Set(tip, "bossFrameName", r.HasTag("event_icon") ? "event" : "boss");
            }
            else text = NpcFunctionTooltip(r, "center", true);
        }
        return BuildTooltip(tip, text);
    }

    /// <summary>
    /// GetNpcTooltip @0x141a319f0 (uiminimapentity.cpp:436-447): with a function name (string table 10), the common
    /// s_minimap_tooltip_function_npc (one row: _1row) over the align, the function name coloured (FUN_14183e930: a FONT tag
    /// with the colour in lower-case hex; the friendly colour for a friendly npc, else the class's) and the name with its
    /// title; else s_minimap_tooltip_normal over the name.
    /// </summary>
    public string NpcFunctionTooltip(NpcRow r, string align, bool multiRow)
    {
        var tables = Model.Tables;
        string name = tables.NpcNameWithTitle(r.Id) ?? "";
        if (tables.NpcFunctionName(r.Id) is { Length: > 0 } fn)
        {
            string key = r.Friendly > 0 ? "NameTagColorFriendly" : r.ClassColorKey;
            uint color = key == null ? 0 : ColorConstant(key);
            string colored = $"<FONT color='#{color:x}'>{fn}</FONT>";
            return ClientFormat.Format(tables.Common(multiRow ? "s_minimap_tooltip_function_npc" : "s_minimap_tooltip_function_npc_1row"),
                align, colored, name);
        }
        return ClientFormat.Format(tables.Common("s_minimap_tooltip_normal"), name);
    }

    /// <summary>BuildTooltip @0x141a32260: the text in the object's text when it is not empty, else no tooltip.</summary>
    AsObject BuildTooltip(AsObject tip, string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        Host.Set(tip, "text", text);
        return tip;
    }

    /// <summary>OnUserFunction @0x141a32120 (uiminimapentity.cpp:490-505): "tooltip" answers BuildTooltip's object, "click" is
    /// SendSetDebugMode (vfunc_3; the debug mode's packet is not sent: the debug mode is not ported).</summary>
    static object OnUserFunction(object[] a, Func<AsObject> tooltip) =>
        a.Length > 0 && Rt.ToString(a[0]) == "tooltip" ? tooltip() : null;

    // ---- named objects ----------------------------------------------------------------------------------------------

    int FindNamed(string name)
    {
        for (int i = 0; i < named.Count; i++)
            if (string.Equals(named[i].Source.Name, name, StringComparison.Ordinal)) return i;
        return -1;
    }

    /// <summary>
    /// AddNamedObject @0x141a3a8e0 (uiminimapentity.cpp:1839-1852): nothing in a house field, before the movie, for an
    /// object without a row, for one without a minimap icon that no active quest targets, or for a name it holds; else a
    /// UIMinimapEntity of kind 6, UpdateNamed and FUN_141a3e7d0; it is kept when that made a clip, and a quest target sets
    /// m_212.
    /// </summary>
    public void AddNamedObject(InteractObject o)
    {
        if (HouseField || Host?.Entity == null || !fieldLoaded || o == null || o.RowId <= 0) return;
        if (string.IsNullOrEmpty(o.Icon) && !QuestTargetObject(o.RowId)) return;
        if (FindNamed(o.Name) >= 0) return;
        var e = new Named { Source = o };
        UpdateNamed(e);
        var entity = e;
        e.UserFunction = a => OnUserFunction(a, () => BuildTooltip(Host.Obj(), entity.Text));     // GetText kind 6: m_text
        e.Clip = Create(e.Icon, e.State, e.Layer, e.Text, false, false, false, e.Pos, e.Visible, 6, e.UserFunction);
        if (e.Clip != null) named.Add(e);
        if (e.QuestTarget) overlapInvalid = true;
    }

    /// <summary>
    /// UpdateNamed @0x141a43ac0 (uiminimapentity.cpp:3347-3372): a reactable object an active quest targets takes
    /// quest_target_object and m_64, any other the row's minimap icon; layer interact, the point the object's, the text
    /// its interact name (string table 0x10). Not a target: shown when reactable with an icon. A watchtower (type 9) is on
    /// the watchtower layer, always shown, frame enabled at state 0 else disabled (UpdateNamedFrame).
    /// </summary>
    void UpdateNamed(Named e)
    {
        var o = e.Source;
        int state = o.State();
        bool target = QuestTargetObject(o.RowId) && state == 1;
        e.Icon = target ? "quest_target_object" : o.Icon ?? "";
        e.Layer = "interact";
        e.Pos = o.Position;
        e.Text = Model.Tables.InteractName(o.RowId) ?? "";
        e.QuestTarget = target;
        if (!target) e.Visible = state == 1 && !string.IsNullOrEmpty(e.Icon);
        if (o.Type == 9)
        {
            e.Layer = "watchtower";
            e.Visible = true;
            e.State = state == 0 ? "enabled" : "disabled";
            if (e.Clip != null) Host.Invoke("changeEntityFrame", e.Clip, e.State);
        }
    }

    /// <summary>
    /// RefreshNamedObject @0x141a3a490 (uiminimapentity.cpp:1794-1836), from a prop's state change: nothing in a house field;
    /// a held object: UpdateNamed, then on a new icon: none = hidden; else (when there was one) changeEntityData(clip,
    /// BuildData), whose returned object is the clip, shown as the entity is; a quest target then sets m_212. The same icon:
    /// shown as the entity is. An object not held goes to AddNamedObject.
    /// </summary>
    public void RefreshNamedObject(InteractObject o)
    {
        if (HouseField || o == null || Host?.Entity == null) return;
        int i = FindNamed(o.Name);
        if (i < 0) { AddNamedObject(o); return; }
        var e = named[i];
        e.Source = o;
        string icon = e.Icon;
        UpdateNamed(e);
        if (string.Equals(icon, e.Icon, StringComparison.Ordinal)) { Show(e.Clip, e.Visible); return; }
        if (string.IsNullOrEmpty(e.Icon)) { e.Visible = false; Show(e.Clip, false); return; }
        if (!string.IsNullOrEmpty(icon))
        {
            var data = Data(e.Icon, e.State, e.Layer, e.Text, false, false, false, e.Pos, e.Visible, 6, e.UserFunction);
            if (Host.Invoke("changeEntityData", e.Clip, data) is not AsObject clip) return;
            e.Clip = clip;
            Show(e.Clip, e.Visible);
        }
        if (e.QuestTarget) overlapInvalid = true;
    }

    /// <summary>RemoveNamedObject (SSG_INTERACT_REMOVE): removeEntity(clip, "") and the name leaves m_named.</summary>
    public void RemoveNamedObject(string name)
    {
        int i = FindNamed(name);
        if (i < 0) return;
        if (named[i].Clip != null && Host?.Entity != null) Host.Invoke("removeEntity", named[i].Clip, "");
        named.RemoveAt(i);
    }

    /// <summary>RefreshNamed @0x141a42550 (uiminimapentity.cpp:2289): RefreshNamedObject for each of the field's interact objects.</summary>
    void RefreshNamed()
    {
        var list = InteractObjects();
        for (int i = 0; i < list.Count; i++) RefreshNamedObject(list[i]);
    }

    /// <summary>BuildNamed @0x141a3eed0 (uiminimapentity.cpp:3610): AddNamedObject for each of the field's interact objects.</summary>
    void BuildNamed()
    {
        var list = InteractObjects();
        for (int i = 0; i < list.Count; i++) AddNamedObject(list[i]);
    }

    void RemoveNpcAndNamedEntities()
    {
        for (int i = 0; i < npcs.Count; i++)
        {
            var n = npcs[i];
            if (n.Clip != null) Host.Invoke("removeEntity", n.Clip, "");
            n.Clip = null;
            n.Kept = false;
        }
        for (int i = 0; i < named.Count; i++)
            if (named[i].Clip != null) Host.Invoke("removeEntity", named[i].Clip, "");
        named.Clear();
    }

    /// <summary>
    /// FUN_141a42bb0 (uiminimapentity.cpp:2321-2368), the tail of each refresh: once m_212 is set and the movie has its
    /// m_maxpanel, the quest targets (GatherOverlapEntities: the npcs with m_64, then the named entities with m_64 whose
    /// object is reactable) whose point projects are put in cells of m_minimapCell pixels by their place on the stage
    /// against the panel's; the first in a cell shows, the others hide. The cell key is the client's
    /// ((long)cx &lt;&lt; 32) | (long)cy, a negative cy filling the high half as it does there. The UI scale is 1 here.
    /// </summary>
    void Overlap()
    {
        if (Host?.Entity == null || !overlapInvalid || maxPanel is not Ms2.Avm2.Flash.AsDisplayObject panel) return;
        var (px, py) = panel.LocalToGlobal(0, 0);
        overlapInvalid = false;
        occupied.Clear();
        int cell = OverlapCell > 0 ? OverlapCell : 50;
        for (int i = 0; i < npcs.Count; i++)
        {
            var n = npcs[i];
            if (n.Kept && n.QuestTarget) Place(ref n.Visible, n.Pos, n.Clip, px, py, cell);
        }
        for (int i = 0; i < named.Count; i++)
        {
            var e = named[i];
            if (e.QuestTarget && e.Source.State() == 1) Place(ref e.Visible, e.Pos, e.Clip, px, py, cell);
        }

        void Place(ref bool visible, Vec3 pos, AsObject clip, double px, double py, int cell)
        {
            if (!WorldToMinimap(pos, out _, out _) || clip is not Ms2.Avm2.Flash.AsDisplayObject d) return;
            var (x, y) = d.LocalToGlobal(0, 0);
            int cx = (int)((x - px) / cell), cy = (int)((y - py) / cell);
            long key = ((long)cx << 32) | (long)cy;
            visible = occupied.Add(key);
            d.visible = visible;
        }
    }
}
