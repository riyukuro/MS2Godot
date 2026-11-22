using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIQuestNavigation (ms2/client/ms2/uiquestnavigation.cpp, "QuestNavigation", LoadSwf 0x1f), the C++ side of
/// uiquestnavigation: the quest markers over the local player (its distance text), the quest npcs, interact objects,
/// liftables and the portal the guide points at. A UISwfEntity, not a dialog. The movie's root (QuestNavigationFactory)
/// makes one "UIQuestNavigation_{class}_hud" clip per marker (create) and the C++ side adds it to the root
/// (addChild) while the marker is on, removes it (removeChild) while it is off.
/// <para>
/// The markers are kept like the client's m_entities, a CAtlMap&lt;CStringA, MS2Ptr&lt;NavigationEntity&gt;&gt;
/// (17, 0.75, 0.25, 2.25, 10) (ctor @0x140e79f20): the frame walks them in the map's own order (<see cref="Markers"/>).
/// </para>
/// <para>
/// The HUD hosts it with CQuestNavigator (Ms2.Quest.QuestNavigator), whose trace and context feed <see cref="Tick"/>, and
/// sends the field's npcs, interact objects and portals at field load, then each live npc's add and remove. STILL-OPEN: no
/// liftable is sent (AddLiftable has no caller), and CanShowBeam's ride and hide tests are the host's (<see cref="CanShowBeam"/>).
/// </para>
/// </summary>
public sealed class QuestNavigationController : UiController
{
    public override string Movie => "uiquestnavigation";
    /// <summary>QuestNavigationFactory.removeAll: only the movie's root has it.</summary>
    protected override string EntityMethod => "removeAll";
    protected override bool IsDialog => false;
    public override string TypeName => "QuestNavigation";

    /// <summary>The marker kinds: the NavigationEntity subclasses.</summary>
    public enum Kind { Pc, Npc, Interact, Liftable, Portal }

    /// <summary>A point in the world (MS2 units, Z up).</summary>
    public readonly record struct Point(float X, float Y, float Z);

    /// <summary>NavigationEntity and its subclass's members.</summary>
    public sealed class Marker
    {
        public Kind Kind { get; internal set; }
        /// <summary>vfunc_1: the map key ("PcHud", "npc_{0}", "interact_{0}", "liftable_{0}", "portal_{0}").</summary>
        public string Key { get; internal set; }
        /// <summary>vfunc_2: the movie class ("pc", "target", "portal").</summary>
        public string Class => Kind == Kind.Pc ? "pc" : Kind == Kind.Portal ? "portal" : "target";
        /// <summary>Npc m_30: the object id.</summary>
        public int ObjectId { get; internal set; }
        /// <summary>Npc m_34 (GetNpcId), interact m_30 (GetRowId), liftable m_30 (FUN_141447cc0), portal m_30 (m_nId).</summary>
        public int Id { get; internal set; }
        /// <summary>Interact / liftable m_38: the object's name.</summary>
        public string Name { get; internal set; }
        /// <summary>m_20: the clip create made, null when the movie was not up.</summary>
        public object Clip { get; internal set; }
        /// <summary>m_18: added to the root (addChild).</summary>
        public bool Shown { get; internal set; }
        /// <summary>m_1c: the alpha last set (100 at first).</summary>
        public float Alpha { get; internal set; } = 100f;

        internal uint Hash;
        internal Marker Next;
        // the last values sent, so a frame with nothing new sends nothing
        internal bool? Visible;
        internal bool Placed;
        internal float X, Y;
        internal object DistanceField;
        internal bool DistanceLookedUp;
        internal string DistanceText;
    }

    /// <summary>WorldService::GetFieldId.</summary>
    public Func<int> FieldId = () => 0;
    /// <summary>
    /// CQuestNavigator::CanShowBeam (questnavigator.cpp): the sub scene 0x620001d and a local PC; riding, the ridee's
    /// m_nUseRidingNavi; else not hidden (FUN_14123c660() == 0).
    /// </summary>
    public Func<bool> CanShowBeam = () => false;
    /// <summary>WorldService::GetLocalPlayer()->vfunc_18, null without a local player.</summary>
    public Func<Point?> PlayerPosition = () => null;
    /// <summary>FindNpcById(object id): null = not found, else IsDead.</summary>
    public Func<int, bool?> NpcDead = _ => null;
    /// <summary>The npc's anchor (vfunc_7): its "Eff_UI" node's world translate, else its vfunc_18; null = not found.</summary>
    public Func<int, Point?> NpcAnchor = _ => null;
    /// <summary>FindProp(name): null = not found, else CInteractObject vfunc_7 (m_nState == 1).</summary>
    public Func<string, bool?> PropActive = _ => null;
    /// <summary>FindProp(name)->GetPos, null = not found.</summary>
    public Func<string, Point?> PropPosition = _ => null;
    /// <summary>FindLiftByName(name): null = not found, else GetState().</summary>
    public Func<string, int?> LiftState = _ => null;
    /// <summary>FindLiftByName(name)->GetPosition, null = not found.</summary>
    public Func<string, Point?> LiftPosition = _ => null;
    /// <summary>WorldService FUN_141bc2800(id)->GetPosition, null = no such portal.</summary>
    public Func<int, Point?> PortalPosition = _ => null;
    /// <summary>CPortal::IsShown.</summary>
    public Func<int, bool> PortalShown = _ => false;
    /// <summary>
    /// The camera's projection (CameraMgr FUN_140af0700) of a world point: the screen point times GetMovieScale, and the
    /// camera-pitch shift there (FUN_1412762d0, PlayerNameTag.PitchShift); null when it does not project.
    /// </summary>
    public Func<Point, (float X, float Y, float Shift)?> Project = _ => null;
    /// <summary>Client::Constants OffsetNpcNametag (+0x5e0).</summary>
    public float NpcNametagOffset;

    // m_entities: CAtlMap bins, each a chain from its head
    const float OptimalLoad = 0.75f, LoThreshold = 0.25f, HiThreshold = 2.25f;
    static readonly uint[] Primes =
    {
        17, 23, 29, 37, 41, 53, 67, 83, 103, 131, 163, 211, 257, 331, 409, 521, 647, 821, 1031, 1291, 1627, 2053, 2591,
        3251, 4099, 5167, 6521, 8209, 10331, 13007, 16411, 20663, 26017, 32771, 41299, 52021, 65537,
    };
    Marker[] bins = new Marker[17];
    int count;

    /// <summary>The markers in the map's order (GetStartPosition / GetNext): the bins in order, each from its head.</summary>
    public IEnumerable<Marker> Markers
    {
        get
        {
            foreach (var head in bins)
                for (var m = head; m != null; m = m.Next) yield return m;
        }
    }

    /// <summary>The marker of <paramref name="key"/>, null for none (Lookup).</summary>
    public Marker Find(string key)
    {
        uint h = Hash(key);
        for (var m = bins[h % (uint)bins.Length]; m != null; m = m.Next)
            if (m.Hash == h && m.Key == key) return m;
        return null;
    }

    /// <summary>
    /// CStringElementTraits&lt;CStringA&gt;::Hash: nHash = nHash * 33 + ch, ch a signed char of the key's CStringA bytes.
    /// Each UTF-16 unit stands for one byte here, so keys are assumed ASCII: npc_{id}, portal_{id}, PcHud and
    /// interact_{id} always are; liftable_{name} is when the map's entity name is (the client hashes the name's
    /// code-page bytes, which a non-ASCII name would change, and with them the bin, the walk order and so the
    /// addChild stacking order of markers turned on in one frame).
    /// </summary>
    static uint Hash(string key)
    {
        uint h = 0;
        foreach (char ch in key) h = (h << 5) + h + (uint)(int)(sbyte)(byte)ch;
        return h;
    }

    /// <summary>PickSize: the smallest listed prime not below elements / 0.75.</summary>
    static uint PickSize(int elements)
    {
        uint estimate = (uint)(elements / OptimalLoad);
        foreach (uint p in Primes) if (estimate <= p) return p;
        return estimate;
    }

    /// <summary>Rehash: the old bins walked in order, each node pushed to the head of its new bin.</summary>
    void Rehash(uint n)
    {
        if (n == bins.Length) return;
        var nb = new Marker[n];
        foreach (var head in bins)
            for (Marker m = head, next; m != null; m = next)
            {
                next = m.Next;
                uint i = m.Hash % n;
                m.Next = nb[i];
                nb[i] = m;
            }
        bins = nb;
    }

    /// <summary>
    /// SetAt: a key already there keeps its place and takes the new marker; a new one goes to the head of its bin, then
    /// past bins * 2.25 elements the map grows (NewNode).
    /// </summary>
    void SetAt(Marker marker)
    {
        marker.Hash = Hash(marker.Key);
        uint i = marker.Hash % (uint)bins.Length;
        for (Marker m = bins[i], prev = null; m != null; prev = m, m = m.Next)
        {
            if (m.Hash != marker.Hash || m.Key != marker.Key) continue;
            marker.Next = m.Next;
            if (prev == null) bins[i] = marker; else prev.Next = marker;
            m.Next = null;
            return;
        }
        marker.Next = bins[i];
        bins[i] = marker;
        count++;
        if (count > (int)(HiThreshold * bins.Length)) Rehash(PickSize(count));
    }

    /// <summary>RemoveKey: the node unlinked; below bins * 0.25 elements (0 under 17) the map shrinks (FreeNode).</summary>
    void RemoveKey(string key)
    {
        uint h = Hash(key);
        uint i = h % (uint)bins.Length;
        for (Marker m = bins[i], prev = null; m != null; prev = m, m = m.Next)
        {
            if (m.Hash != h || m.Key != key) continue;
            if (prev == null) bins[i] = m.Next; else prev.Next = m.Next;
            m.Next = null;
            count--;
            int lo = (int)(LoThreshold * bins.Length);
            if (lo < 17) lo = 0;
            if (count < lo) Rehash(PickSize(count));
            return;
        }
    }

    // ---- the notifies -------------------------------------------------------------------------------------------------

    /// <summary>OnAddNpc (@0x140e7af90): an npc marker made (create) and set at "npc_{object id}".</summary>
    public Marker AddNpc(int objectId, int npcId) =>
        Add(new Marker { Kind = Kind.Npc, Key = "npc_" + objectId, ObjectId = objectId, Id = npcId });

    /// <summary>
    /// OnRemoveNpc (@0x140e7b130): with the movie up, the npc's marker taken off the root (RemoveChild) and out of the map.
    /// </summary>
    public void RemoveNpc(int objectId)
    {
        if (Host?.Entity == null) return;
        string key = "npc_" + objectId;
        if (Find(key) is not { } m) return;
        RemoveChild(m);
        RemoveKey(key);
    }

    /// <summary>
    /// OnAddInteract (@0x140e7b290): only an interact object of kind 1 (CInteractObject vfunc_5, the mesh kind) gets a
    /// marker, set at "interact_{name}".
    /// </summary>
    public Marker AddInteract(string name, int rowId, int kind)
    {
        if (kind != 1) return null;
        return Add(new Marker { Kind = Kind.Interact, Key = "interact_" + name, Name = name, Id = rowId });
    }

    /// <summary>OnAddLiftable (@0x140e7b430): a lift whose "liftable_{name}" is already in the map is skipped.</summary>
    public Marker AddLiftable(string name, int liftId)
    {
        if (Find("liftable_" + name) != null) return null;
        return Add(new Marker { Kind = Kind.Liftable, Key = "liftable_" + name, Name = name, Id = liftId });
    }

    /// <summary>NotifyCompleteMyPC (FUN_140e7b640): the local player's marker set at "PcHud".</summary>
    public Marker CompleteMyPc() => Add(new Marker { Kind = Kind.Pc, Key = "PcHud" });

    /// <summary>OnAddPortal (@0x140e7b720): a portal marker set at "portal_{id}".</summary>
    public Marker AddPortal(int portalId) =>
        Add(new Marker { Kind = Kind.Portal, Key = "portal_" + portalId, Id = portalId });

    Marker Add(Marker m)
    {
        Create(m);
        SetAt(m);
        return m;
    }

    /// <summary>
    /// NavigationEntity::vfunc_3 (@0x140e786a0): with the movie up, create(class, {id: key}); a defined result is the clip.
    /// </summary>
    bool Create(Marker m)
    {
        if (Host?.Entity == null) return false;
        var v = Host.Invoke(Host.Entity, "create", m.Class, Host.Obj(("id", m.Key)));
        if (Ms2.Avm2.Rt.IsNullish(v)) return false;  // undefined (null here: no movie)
        m.Clip = v;
        m.DistanceField = null; m.DistanceLookedUp = false;
        return true;
    }

    /// <summary>OnLoadComplete (@0x140e7ab40): every marker made before the movie was up gets its clip.</summary>
    public override void OnLoadComplete()
    {
        foreach (var head in bins)
            for (var m = head; m != null; m = m.Next)
                if (m.Clip == null) Create(m);
    }

    /// <summary>vfunc_43 is UISwfEntity's, which does nothing.</summary>
    public override void Layout(double stageW, double stageH) { }

    /// <summary>vfunc_3 (@0x140e7aac0), the unload: removeAll on the root and the map cleared (back to 17 bins).</summary>
    public void Unload()
    {
        if (Host?.Entity != null) Host.Invoke(Host.Entity, "removeAll");
        bins = new Marker[PickSize(0)];
        count = 0;
    }

    /// <summary>AddChild (@0x140e7b890): 0 without the movie or a clip; already added = 1; else addChild(clip).</summary>
    bool AddChild(Marker m)
    {
        if (Host?.Entity == null || m.Clip == null) return false;
        if (m.Shown) return true;
        Host.Invoke(Host.Entity, "addChild", m.Clip);
        return true;
    }

    /// <summary>RemoveChild (@0x140e7ba00): 0 without the movie or a clip; not added = 1; else removeChild(clip).</summary>
    bool RemoveChild(Marker m)
    {
        if (Host?.Entity == null || m.Clip == null) return false;
        if (!m.Shown) return true;
        Host.Invoke(Host.Entity, "removeChild", m.Clip);
        return true;
    }

    // ---- the frame ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// vfunc_15 (@0x140e7ac40), each frame with the movie up, per marker in the map's order: on at alpha 100 when it is
    /// the result's (vfunc_5), else on at 50 when the context holds it (vfunc_6), else off; a change adds or removes the
    /// clip (m_18 set only when that worked); an on marker with a clip whose alpha changed gets SetAlpha; then it is placed
    /// (vfunc_4). <paramref name="result"/> is CQuestNavigator::Result(), <paramref name="context"/> Context().
    /// </summary>
    public void Tick(TraceResult result, TraceContext context)
    {
        if (Host?.Entity == null) return;
        int field = FieldId();
        foreach (var head in bins)
            for (var m = head; m != null; m = m.Next)
            {
                bool on;
                float alpha = 0f;
                if (IsTarget(m, result, field)) { on = true; alpha = 100f; }
                else if (InContext(m, context)) { on = true; alpha = 50f; }
                else on = false;
                if (m.Shown != on)
                {
                    if (on) { if (AddChild(m)) m.Shown = true; }
                    else if (RemoveChild(m)) m.Shown = false;
                }
                if (alpha != m.Alpha && on && m.Clip != null)
                {
                    // ASDisplayObject::SetAlpha: DisplayInfo alpha in percent, the AS alpha 0 to 1
                    Host.Set(m.Clip, "alpha", alpha / 100.0);
                    m.Alpha = alpha;
                }
                Place(m, result);
            }
    }

    /// <summary>vfunc_5 of each kind: the marker is the trace result's.</summary>
    bool IsTarget(Marker m, TraceResult r, int field)
    {
        if (r == null) return false;
        switch (m.Kind)
        {
            case Kind.Pc:          // @0x140e78af0
                return r.Kind != TraceKind.None && CanShowBeam();
            case Kind.Npc:         // @0x140e78e00
            {
                var c = r.Context;
                if (c == null || c.MapId != field || !c.NpcIds.Contains(m.Id)) return false;
                if (r.Kind == TraceKind.NpcDone) return false;
                return NpcDead(m.ObjectId) == false;
            }
            case Kind.Interact:    // @0x140e79280
            {
                var c = r.Context;
                if (c == null || c.MapId != field || !c.InteractIds.Contains(m.Id)) return false;
                return PropActive(m.Name) == true;
            }
            case Kind.Liftable:    // @0x140e797b0: !GetState() == false -> 0, so only state 0 is shown
            {
                var c = r.Context;
                if (c == null || c.MapId != field || !c.LiftableIds.Contains(m.Id)) return false;
                return LiftState(m.Name) == 0;
            }
            case Kind.Portal:      // @0x140e79d00: efd::Point3 != against m_20, then IsShown
            {
                if (r.Context == null || r.Kind != TraceKind.Portal) return false;
                if (PortalPosition(m.Id) is not { } p) return false;
                if (p.X != (float)r.X || p.Y != (float)r.Y || p.Z != (float)r.Z) return false;
                return PortalShown(m.Id);
            }
        }
        return false;
    }

    /// <summary>vfunc_6 of each kind (the base and the pc and portal ones are 0): the context holds the marker.</summary>
    bool InContext(Marker m, TraceContext c)
    {
        if (c == null) return false;
        switch (m.Kind)
        {
            case Kind.Npc:         // @0x140e78f50
                return c.NpcIds.Contains(m.Id) && NpcDead(m.ObjectId) == false;
            case Kind.Interact:    // @0x140e79450
                return c.InteractIds.Contains(m.Id) && PropActive(m.Name) == true;
            case Kind.Liftable:    // @0x140e799b0
                return c.LiftableIds.Contains(m.Id) && LiftState(m.Name) == 0;
        }
        return false;
    }

    /// <summary>vfunc_7 of each kind: the world point the marker sits on (the zero point when its object is gone).</summary>
    Point Anchor(Marker m)
    {
        Point? p = m.Kind switch
        {
            Kind.Pc => PlayerPosition(),                         // @0x140e78c40
            Kind.Npc => NpcAnchor(m.ObjectId),                   // @0x140e79010
            Kind.Interact => Up(PropPosition(m.Name)),           // @0x140e79590
            Kind.Liftable => Up(LiftPosition(m.Name)),           // @0x140e79b20
            Kind.Portal => Up(PortalPosition(m.Id)),             // @0x140e79e50
            _ => null,
        };
        return p ?? default;
    }

    static Point? Up(Point? p) => p is { } q ? new Point(q.X, q.Y, q.Z + 200f) : null;

    /// <summary>
    /// vfunc_8 of each kind from the camera-pitch shift (the base's, @0x140e78a40): the pc 40 - shift (@0x140e78cc0), an
    /// npc shift - (OffsetNpcNametag + 30) (@0x140e790f0), the others the shift.
    /// </summary>
    float Offset(Marker m, float shift) => m.Kind switch
    {
        Kind.Pc => 40f - shift,
        Kind.Npc => shift - (NpcNametagOffset + 30f),
        _ => shift,
    };

    /// <summary>
    /// NavigationEntity::vfunc_4 (@0x140e78870): a marker with a clip that is on is projected; projected, it goes to the
    /// stage point plus vfunc_8 (SetPositionFloat) and is shown, else hidden. The pc's (@0x140e78b40) then sets its
    /// m_distance text to the result's distance text, on or off.
    /// </summary>
    void Place(Marker m, TraceResult r)
    {
        if (m.Clip != null && m.Shown)
        {
            if (Project(Anchor(m)) is { } pt)
            {
                float x = pt.X, y = pt.Y + Offset(m, pt.Shift);
                if (!m.Placed || x != m.X || y != m.Y)
                {
                    m.X = x; m.Y = y; m.Placed = true;
                    Host.Set(m.Clip, "x", (double)x);
                    Host.Set(m.Clip, "y", (double)y);
                }
                SetVisible(m, true);
            }
            else SetVisible(m, false);
        }
        if (m.Kind != Kind.Pc || r == null || m.Clip == null) return;
        // the client asks the clip for m_distance every frame (uiquestnavigation.cpp:218); the clip does not change, so once
        if (!m.DistanceLookedUp) { m.DistanceField = Host.Child("m_distance", m.Clip); m.DistanceLookedUp = true; }
        if (m.DistanceField == null || string.Equals(m.DistanceText, r.DistanceText, StringComparison.Ordinal)) return;
        m.DistanceText = r.DistanceText;
        Host.Set(m.DistanceField, "text", r.DistanceText ?? "");
    }

    /// <summary>ASUIComponent::vfunc_3 (@0x1422c7260, SetDisplayInfo visible), sent when it changes.</summary>
    void SetVisible(Marker m, bool visible)
    {
        if (m.Visible == visible) return;
        m.Visible = visible;
        Host.SetVisible(m.Clip, visible);
    }
}
