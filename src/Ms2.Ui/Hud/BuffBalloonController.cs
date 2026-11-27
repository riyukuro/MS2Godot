using Ms2.Ui.Skills;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIBuffBalloon (ms2/unnamed/unit_14128e7f0_uibuffballoon.cpp, UI 0x4ea4, "BuffBalloon"), the C++ side of uibuffballoon:
/// the icon of an additional effect over an actor's head. A UIHudEntity like <see cref="ChatBalloonController"/>: one movie
/// for every actor, whose root (HudEntityFactory) makes one "buff_ballon" clip per actor (createEntity); a released actor's
/// entity goes to a pool and is handed to the next actor. Each clip (BuffBalloon : PopupImageProfile) loads obj.imgPath into
/// its 40x40 picture on open(obj); its close() does nothing, and the C++ side never calls it: a stopped balloon is hidden.
/// <para>
/// Who shows one: AdditionalCliList::AddBuffUI (additionalcli_2.cpp:1243-1251) gives a level with &lt;UI
/// iconBalloonOnOff="1"&gt; to its owner's balloon (PC::vfunc_84 @0x1416572b0, CNpc::vfunc_84 @0x1410902f0) with the effect's
/// key, its icon and its remaining ticks (<see cref="Show"/>); RemoveBuffUI (:1275-1280) hides it by the same key
/// (<see cref="Hide"/>). One balloon per actor: the last balloon effect added replaces the icon, and only its key hides it.
/// </para>
/// </summary>
public sealed class BuffBalloonController : UiController
{
    public override string Movie => "uibuffballoon";
    protected override string EntityMethod => "createEntity";
    protected override bool IsDialog => false;          // a UIHudEntity (UIHeadUpDisplay)

    /// <summary>m_70 (the ctor @0x14128ee40): the movie class of each actor's clip.</summary>
    public const string EntityClass = "buff_ballon";

    /// <summary>What the balloon reads from an actor (PCBase: a PC or a CNpc).</summary>
    public sealed class Actor
    {
        public int ObjectId;
        /// <summary>PCBase IsDead: vfunc_8's n (@0x14128ec60), which skips the riding and FUN_141674bc0 tests.</summary>
        public bool Dead;
        /// <summary>
        /// FUN_141670500 (PCBase m_f168), vfunc_9's y offset (@0x14128ecb0): OffsetNpcChatballoon for an npc (the CNpc
        /// constructor, npcactorandvibrate.cpp:492), OffsetPcChatballoon for a PC (pc.cpp:661).
        /// </summary>
        public float Offset;
        /// <summary>GameObject m_unk_bc: the distance to the local player (Entity::FUN_140f48e20).</summary>
        public float Distance;
        /// <summary>The action state 0x3f test of Entity::vfunc_8 (uihudentity.cpp @0x140f490b0).</summary>
        public bool StateHidden;
        /// <summary>The riding and FUN_141674bc0 tests of Entity::vfunc_8, which a dead actor skips.</summary>
        public bool Hidden;
    }

    /// <summary>CameraMgr FUN_140a18070 (m_1b0): vfunc_2 starts nothing and vfunc_5 opens nothing while it is 0.</summary>
    public Func<bool> CameraReady = () => true;
    /// <summary>IsUpperTargetId (@0x140930110): the object is the local player's.</summary>
    public Func<int, bool> IsUpperTarget = _ => false;
    /// <summary>The time in ms (GetTickCountMs2): the balloon's timer.</summary>
    public Func<long> TickMs = () => Environment.TickCount64;
    /// <summary>BuffBallonDistance (constants +0x378, 2500 by default), m_54 of every entity (the Entity ctor @0x14128e7f0).</summary>
    public float VisibleDistance = 2500;

    /// <summary>UIBuffBalloon::Entity (@0x14128e7f0) over UIHudEntity::TimerEntity and Entity.</summary>
    public sealed class Entity : IBuffBalloon
    {
        internal BuffBalloonController Owner;
        public Actor Actor;          // m_1c, the actor
        public object Clip;          // m_38
        /// <summary>m_a0: the icon path open shows (FUN_14128edf0 reads it).</summary>
        public string Image = "";
        /// <summary>m_a8: the key of the effect the icon is of.</summary>
        public int Key;
        /// <summary>m_64 (TimerEntity): the ms the timer runs for, the effect's remaining ticks.</summary>
        public int Time;
        public bool Reopen;          // m_48: open on the next shown frame
        public bool Active;          // m_4c
        public bool Hidden;          // m_58
        public bool Held;            // m_60 (TimerEntity): shown without the timer
        public long TimerEnd = long.MinValue;   // m_68
        public bool ClipVisible;     // the clip's visibility (FUN_140f48dc0, vfunc_7)
        /// <summary>The imgPath the last open passed.</summary>
        public string Opened;
        public int Opens;
        public float X, Y;           // the clip's place, stage units
        public bool Placed;

        public bool TimerRunning(long now) => now < TimerEnd;

        /// <summary>
        /// FUN_14128ecf0 (@0x14128ecf0): m_a8 = the key, m_a0 = the icon, m_64 = the time; an active balloon is opened again
        /// (FUN_140f48d80) and its timer started for m_64; else it is held (FUN_140f49470: m_60 = 1) and started (vfunc_2).
        /// A held balloon shows until it is stopped, whatever the timer.
        /// </summary>
        public void Show(int key, string image, uint time)
        {
            Key = key;
            Image = image ?? "";
            Time = unchecked((int)time);
            if (Active)
            {
                Reopen = true;
                TimerEnd = Owner.TickMs() + Time;
            }
            else
            {
                Held = true;
                Owner.Start(this);
            }
        }

        /// <summary>FUN_14128edb0 (@0x14128edb0): stopped (vfunc_3) when the key is the shown one.</summary>
        public void Hide(int key)
        {
            if (Key == key) Owner.Stop(this);
        }
    }

    readonly Dictionary<int, Entity> entities = new();    // m_a8
    readonly LinkedList<Entity> pool = new();             // m_78

    /// <summary>The entities by actor id, for the shots and tests.</summary>
    public IReadOnlyDictionary<int, Entity> Entities => entities;
    /// <summary>The clips made by createEntity so far.</summary>
    public int ClipsMade { get; private set; }

    /// <summary>OnLoadComplete (@0x14128ef40): CreateEntityCache(m_68), with UIHudEntity's count of 0: no clip is made ahead.</summary>
    public override void OnLoadComplete() { }

    /// <summary>The root stays where the movie put it, shown.</summary>
    public override void Layout(double stageW, double stageH) => Host.SetVisible(Host.Entity, true);

    // ---- the entity's states (unit_14128e7f0_uibuffballoon.cpp, uihudentity.cpp) ---------------------------------------

    /// <summary>
    /// vfunc_2 (@0x14128e8e0): nothing while the camera is not ready; else TimerEntity::vfunc_2 (@0x140f49490): m_48 = m_4c = 1
    /// and the timer started for m_64.
    /// </summary>
    void Start(Entity e)
    {
        if (!CameraReady()) return;
        e.Reopen = true; e.Active = true; e.TimerEnd = TickMs() + e.Time;
    }

    /// <summary>vfunc_3 (@0x14128e920): TimerEntity::vfunc_3 (m_48 = m_4c = 0, m_60 = 0, the timer expired), then vfunc_7.</summary>
    void Stop(Entity e)
    {
        e.Reopen = false; e.Active = false; e.Held = false; e.TimerEnd = long.MinValue;
        Close(e);
    }

    /// <summary>vfunc_7 (@0x14128ec00): the clip hidden (m_38->vfunc_3(0)); the movie's close() is not called.</summary>
    void Close(Entity e)
    {
        if (e.Clip != null) SetClipVisible(e, false);
    }

    /// <summary>FUN_140f48dc0: the clip's visibility.</summary>
    void SetClipVisible(Entity e, bool visible)
    {
        e.ClipVisible = visible;
        if (e.Clip != null) Host.SetVisible(e.Clip, visible);
    }

    /// <summary>FUN_140f48e80: m_58 set; clearing it asks for a new open (FUN_140f48d80).</summary>
    static void SetHidden(Entity e, bool hidden)
    {
        if (e.Hidden == hidden) return;
        if (!hidden) e.Reopen = true;
        e.Hidden = hidden;
    }

    /// <summary>
    /// vfunc_5 (@0x14128e9a0): Entity::vfunc_5 (the clip shown, m_48 = 0); with the camera ready, open({ imgPath: m_a0 })
    /// (FUN_14016a730 only widens the string).
    /// </summary>
    void Open(Entity e)
    {
        SetClipVisible(e, true);
        e.Reopen = false;
        if (!CameraReady() || e.Clip == null) return;
        e.Opened = e.Image ?? "";
        e.Opens++;
        Host.Invoke(e.Clip, "open", Host.Obj(("imgPath", e.Opened)));
    }

    /// <summary>
    /// GetEntity (FUN_14128f210 @0x14128f210, through GetBuffBalloonEntity @0x14128ef70): the actor's entity is released
    /// first; a pooled one is handed over (its clip moved by updateEntity when the local player's is involved), else the
    /// root's createEntity(id, m_70, false) makes the clip and a new entity (its clip hidden). None for an actor that is gone.
    /// </summary>
    public Entity GetEntity(Actor a)
    {
        if (Host?.Entity == null || a == null) return null;       // IsActorGone
        Release(a.ObjectId);
        if (pool.First is { } node)
        {
            pool.RemoveFirst();
            var reused = node.Value;
            int old = reused.Actor?.ObjectId ?? 0;     // m_1c, cleared by the release (vfunc_1)
            if (a.ObjectId != old)
            {
                bool upper = IsUpperTarget(a.ObjectId);
                if ((IsUpperTarget(old) || upper) && reused.Clip != null) Host.Invoke(Host.Entity, "updateEntity", reused.Clip, upper);
            }
            reused.Actor = a;          // FUN_140f48ce0: the actor id, the anchor cache cleared
            entities[a.ObjectId] = reused;
            return reused;
        }
        // the flag IsUpperTargetId gives is overwritten with false before the call
        var clip = Host.Invoke(Host.Entity, "createEntity", (double)a.ObjectId, EntityClass, false);
        if (Ms2.Avm2.Rt.IsNullish(clip)) return null;
        ClipsMade++;
        var e = new Entity { Owner = this, Actor = a, Clip = clip };
        entities[a.ObjectId] = e;
        SetClipVisible(e, false);                // the Entity ctor's FUN_140f48dc0(0)
        return e;
    }

    /// <summary>
    /// ReleaseEntity (uihudentity.cpp @0x140f49c00; FUN_14128eff0 from PCBase's teardown, pcbase.cpp:893): vfunc_4
    /// (@0x14128e950: the clip hidden; Entity::vfunc_1 clears m_1c, m_48, m_4c, m_50, m_58; then m_a0 = "" and m_a8 = 0) and
    /// the entity pooled. The timer and m_60 stay.
    /// </summary>
    public void Release(int actorId)
    {
        if (!entities.Remove(actorId, out var e)) return;
        SetClipVisible(e, false);
        e.Reopen = e.Active = e.Hidden = false;
        e.Image = "";
        e.Key = 0;
        e.Actor = null;              // vfunc_1: m_1c = 0
        pool.AddLast(e);
    }

    // ---- the owners' pointers (PC::vfunc_84, CNpc::vfunc_84) ----------------------------------------------------------

    readonly Dictionary<Actor, Entity> balloons = new();      // PCBase m_spA98

    /// <summary>
    /// PC::vfunc_84 (@0x1416572b0) and CNpc::vfunc_84 (@0x1410902f0): the actor's balloon (m_spA98), made by
    /// GetBuffBalloonEntity(m_unk_70) on first use. The PC's gives none while ProgressiveLoading is on and its model is not
    /// added (<paramref name="pcNotAdded"/>); the npc's has no such test. The actor keeps its pointer (an MS2Ptr) after a
    /// release, until its teardown.
    /// </summary>
    public Entity BalloonOf(Actor a, bool pcNotAdded = false)
    {
        if (a == null || pcNotAdded) return null;
        if (balloons.TryGetValue(a, out var e)) return e;
        e = GetEntity(a);
        if (e != null) balloons[a] = e;
        return e;
    }

    /// <summary>The actor's teardown (PCBase::vfunc_16, pcbase.cpp:893): FUN_14128eff0 releases its balloon.</summary>
    public void Remove(Actor a)
    {
        if (a == null) return;
        Release(a.ObjectId);
        balloons.Remove(a);
    }

    // ---- the frame (UIHudEntity::vfunc_15, uihudentity.cpp @0x140f49950) ----------------------------------------------

    /// <summary>
    /// vfunc_8 (@0x14128ec60): 1 without an actor, else TimerEntity::vfunc_8 (@0x140f49540) with the actor's IsDead: not held
    /// and the timer over, then Entity::vfunc_8 (@0x140f490b0): past m_54, the 0x3f state, the riding tests.
    /// </summary>
    int HideTest(Entity e)
    {
        var a = e.Actor;
        if (a == null) return 1;
        if (!e.Held && !e.TimerRunning(TickMs())) return 1;
        if (a.Distance > VisibleDistance) return 1;
        if (a.StateHidden) return 1;
        if (a.Hidden && !a.Dead) return 1;
        return 0;
    }

    /// <summary>
    /// Every frame, per entity of an actor still there: an inactive one (m_4c 0) is skipped; one the camera cannot project
    /// (<paramref name="project"/> null) or that the tests hide is hidden once (vfunc_7); else it is opened when asked (m_48)
    /// and placed (vfunc_6). <paramref name="project"/> gives the actor's anchor (vfunc_10, its "Eff_UI" dummy) on the stage
    /// (the screen point times GetMovieScale) and FUN_1412762d0's camera-pitch shift there.
    /// </summary>
    public void Process(Func<Actor, (float X, float Y, float Shift)?> project)
    {
        if (Host?.Entity == null) return;
        foreach (var e in entities.Values)
        {
            if (e.Actor == null || !e.Active) continue;
            var pt = project(e.Actor);
            if (pt == null || HideTest(e) != 0)
            {
                if (!e.Hidden) { SetHidden(e, true); Close(e); }
                continue;
            }
            if (e.Hidden) SetHidden(e, false);
            if (e.Reopen) Open(e);
            Place(e, pt.Value);
        }
    }

    /// <summary>
    /// vfunc_6 (@0x14128eae0): the actor's offset (vfunc_9) and the pitch shift of its anchor (FUN_1412762d0(vfunc_10())) on
    /// y, then SetPositionFloat(x - 55, y - 110).
    /// </summary>
    void Place(Entity e, (float X, float Y, float Shift) pt)
    {
        if (e.Clip == null) return;
        float y = pt.Y + e.Actor.Offset;
        y += pt.Shift;
        float x = pt.X - 55f;
        y -= 110f;
        if (e.Placed && x == e.X && y == e.Y) return;
        e.X = x; e.Y = y; e.Placed = true;
        Host.Set(e.Clip, "x", (double)x);
        Host.Set(e.Clip, "y", (double)y);
    }
}
