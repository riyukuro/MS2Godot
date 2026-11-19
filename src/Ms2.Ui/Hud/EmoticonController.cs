using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIEmoticon (ms2/client/ms2/uiemoticon.cpp, UI 0x4e2f, "Emoticon"), the C++ side of uiemoticon: the emoticon over an actor.
/// A UIHudEntity: one movie for every actor, whose root (HudEntityFactory) makes one "emoticon_entity_body" clip per actor
/// (createEntity); a released actor's entity goes to a pool and is handed to the next actor. Each clip's open(name) shows
/// the movie class of that name (Emoticon.loadMovieClip), centred over the clip's point.
/// <para>
/// What it shows (Entity::FUN_14192fa50): kind 1 the npc's quest mark (GetQuestMovieName), else the npc's iconName (m_b8)
/// when set, else for kind 0 the movie of the npc's kind (GetNpcMovieName). The npc side (CNpc, the spawn's emoticon setup
/// and RefreshQuestState, unit_14108e1e0_npcactorandvibrate.cpp) is here too: <see cref="SpawnNpc"/>, <see cref="NpcTick"/>.
/// </para>
/// <para>
/// STILL-OPEN: the PC emoticons (pc.cpp / mypc.cpp make them on demand; kind 3 is the microgame's, statemicrogame.cpp, with
/// the PC's FUN_141670500 offset) and the status kind 2 (set through UIStatusEvent; FUN_14192f9f0 drops it) have no caller
/// here.
/// </para>
/// <para>
/// The load gate (CNpc::vfunc_83 @0x1410903b0): with <see cref="ProgressiveLoading"/> an npc has no emoticon until its actor
/// is added (<see cref="Actor.ActorAdded"/>). The spawn asks once, before that (CNpc::vfunc_10 runs on the entity's add,
/// worldservice_1.cpp:10331; GameObject::vfunc_12 on the later egmAnimation ActorAddedMessage, worldservice_1.cpp:3606), and
/// CNpc::vfunc_12 does not ask again: of CNpc's own paths only <see cref="RefreshQuestState"/> does, when the quest mark
/// changes. (UITalkManager's SetNpcNameTag and talk page, uitalkcinematic.cpp:1102 and 1563-1565, also ask; STILL-OPEN:
/// not ported, with the talk scene's status kind.) So with
/// progressive loading the npc's iconName and kind movie are not set up, and an npc with quests gets its entity once its
/// mark moves off the reset one.
/// </para>
/// </summary>
public sealed class EmoticonController : UiController
{
    public override string Movie => "uiemoticon";
    protected override string EntityMethod => "createEntity";
    protected override bool IsDialog => false;          // a UIHudEntity (UIHeadUpDisplay)

    /// <summary>
    /// The quest marks' classes (emoticon_movie_quest_*) are defined only in commonresource, which uiemoticon does not
    /// import: the client finds them because uiemoticon is loaded into the HUD root, where the HUD movies that import
    /// commonresource (uiupperhuddialog, uimainplayerinfo, uiexpbar) put its classes in the root VM's class table.
    /// </summary>
    public override IEnumerable<string> Libraries => new[] { "commonresource" };

    /// <summary>m_70: the movie class of each actor's clip.</summary>
    public const string EntityClass = "emoticon_entity_body";

    /// <summary>What the emoticon reads from an actor (PCBase / CNpc).</summary>
    public sealed class Actor
    {
        public int ObjectId;
        /// <summary>DynamicCast&lt;CNpc&gt;: only npcs refresh and get the npc offset.</summary>
        public bool IsNpc;
        /// <summary>CNpcData GetKind (GetNpcId, @0x14192fe00): the npc kind GetNpcMovieName looks up.</summary>
        public int NpcKind;
        /// <summary>The npc id (GetNpcId of RefreshQuestState): the quest marks' key.</summary>
        public int NpcId;
        /// <summary>CNpc vfunc_116: the npc data's friendly; no emoticon at 0 (vfunc_83).</summary>
        public int Friendly;
        /// <summary>CNpcData m_strIcon (iconName): a movie name, "empty" or "".</summary>
        public string Icon = "";
        /// <summary>
        /// GameObject m_unk_c8 (FUN_14123dbd0): set by GameObject::vfunc_12 (gameobject.cpp:264), which WorldService::OnActorAdded
        /// calls when the actor's model has been added, after the spawn.
        /// </summary>
        public bool ActorAdded;
        /// <summary>CNpc m_cE40: HasNpcQuests, set at spawn.</summary>
        public bool HasQuests;
        /// <summary>CNpc m_questState: the mark GetNpcQuestState last gave.</summary>
        public NpcQuestMarks.Mark Quest = NpcQuestMarks.Mark.None;
        /// <summary>CNpc m_nE10 (set by the quest and status listeners, FUN_141093e90) and m_nE14 (the last 1000 ms tick).</summary>
        public bool QuestDirty;
        public long QuestTickAt;
        /// <summary>GameObject m_unk_bc: the distance to the local player.</summary>
        public float Distance;
        /// <summary>
        /// The actor tests of Entity::vfunc_8 (uihudentity.cpp @0x140f490b0): action state 0x3f, riding (FUN_14123c660, with
        /// no ridee or a ridee that rides too), FUN_141674bc0. STILL-OPEN: no field actor reports any of them yet.
        /// </summary>
        public bool Hidden;
        /// <summary>vfunc_85 (the chat balloon entity) active. STILL-OPEN: no chat balloon yet, always false.</summary>
        public bool BalloonActive;
    }

    /// <summary>
    /// GFxSceneService FUN_1422b9ea0 or the sub scene 0x6200031 (the NPC talk scene, <see cref="MovieLayers.InTalk"/> for the
    /// host): only the status kind is shown. STILL-OPEN: the scene FUN_1422b9ea0 tests is not known.
    /// </summary>
    public Func<bool> SceneHidesEmoticons = () => false;
    /// <summary>
    /// COptimizer FUN_1409f7ea0, ProgressiveLoading (DAT_143ae2b40): see <c>Ms2.Formats.Options.Optimization</c>. On (1)
    /// unless the option folder's Optimization.xml says otherwise.
    /// </summary>
    public bool ProgressiveLoading = true;
    /// <summary>The time in ms (GetTickCountMs2): the timer and the 1000 ms quest tick.</summary>
    public Func<long> TickMs = () => Environment.TickCount64;
    /// <summary>CFameTable IsOfficerNpc / GetSymbolByOfficerNpc: the officer npc's alliance symbol, null for none.</summary>
    public Func<int, string> OfficerSymbol = _ => null;
    /// <summary>The quest's data for the suffix (SelectStringByQuestId).</summary>
    public Func<int, QuestData> QuestOf = _ => null;
    /// <summary>EmoticonVisibleDistance (constants m_2600; 2500 in the client's constants): m_54 of every entity.</summary>
    public float VisibleDistance = 2500;
    /// <summary>OffsetNpcEmoticon (constants m_638; -30 in the client's constants): the npc's stage-unit y offset.</summary>
    public float NpcOffset = -30;

    /// <summary>UIEmoticon::Entity (uiemoticon.cpp @0x14192ef40) over UIHudEntity::TimerEntity and Entity (uihudentity.cpp).</summary>
    public sealed class Entity
    {
        public Actor Actor;          // m_1c, the actor
        public object Clip;          // m_38
        /// <summary>m_b0: 0 npc, 1 quest, 2 status, 3 microgame, 4 none.</summary>
        public int Kind = 4;
        /// <summary>m_b8: the movie name set from the npc's iconName.</summary>
        public string Icon = "";
        public bool Reopen;          // m_48: open on the next shown frame
        public bool Active;          // m_4c
        public bool Hidden;          // m_58
        public bool Held;            // m_60 (TimerEntity): shown without the timer
        public long TimerEnd = long.MinValue;   // m_68 with m_64 = 99999999 ms
        public bool ClipVisible;     // the clip's visibility as vfunc_5 / vfunc_7 left it
        public string Opened;        // the name the last open passed
        public float X, Y;           // the clip's place, stage units
        public bool Placed;

        public bool TimerRunning(long now) => now < TimerEnd;
    }

    const long TimerMs = 99999999;   // m_64

    readonly Dictionary<int, Entity> entities = new();    // m_a8
    readonly LinkedList<Entity> pool = new();             // m_78

    /// <summary>The entities by actor id, for the shots and tests.</summary>
    public IReadOnlyDictionary<int, Entity> Entities => entities;
    /// <summary>The clips made by createEntity so far.</summary>
    public int ClipsMade { get; private set; }

    /// <summary>InitNpcMovieNames (@0x14192e8f0): the movie of each npc kind; 500 has none.</summary>
    static readonly Dictionary<int, string> NpcMovies = new()
    {
        [2] = "emoticon_movie_npc_warehouse", [3] = "emoticon_movie_npc_mail",
        [0xb] = "emoticon_movie_npc_equip", [0xc] = "emoticon_movie_npc_equip",
        [0xd] = "emoticon_movie_npc_shopetc", [0xe] = "emoticon_movie_npc_ugc", [0xf] = "emoticon_movie_npc_shopetc",
        [0x51] = "emoticon_movie_npc_doctor", [0x52] = "emoticon_movie_npc_trans", [0x53] = "emoticon_movie_npc_container",
        [0x54] = "emoticon_movie_npc_landagent", [0x50] = "emoticon_movie_npc_headhunter", [500] = "",
    };

    /// <summary>OnLoadComplete (@0x14192fd10): CreateEntityCache(m_68), with a count of 0: no clip is made ahead.</summary>
    public override void OnLoadComplete() { }

    /// <summary>The root stays where the movie put it, shown.</summary>
    public override void Layout(double stageW, double stageH) => Host.SetVisible(Host.Entity, true);

    /// <summary>GetNpcMovieName (@0x14192ebf0): the kind's movie, else an officer npc's "emoticon_movie_npc_{symbol}", else "".</summary>
    public string NpcMovieName(int kind)
    {
        if (NpcMovies.TryGetValue(kind, out var name)) return name;
        return OfficerSymbol(kind) is { } s ? "emoticon_movie_npc_" + s : "";
    }

    /// <summary>GetQuestMovieName (@0x14192ed20): 0 start and 2 complete with the quest's kind suffix, 1 progress, else "".</summary>
    public string QuestMovieName(int questId, int state) => state switch
    {
        0 => "emoticon_movie_quest_start" + NpcQuestMarks.Suffix(QuestOf(questId)),
        2 => "emoticon_movie_quest_complete" + NpcQuestMarks.Suffix(QuestOf(questId)),
        1 => "emoticon_movie_quest_progress",
        _ => "",
    };

    /// <summary>FUN_14192fa50: the name open gets.</summary>
    public string MovieName(Entity e)
    {
        var a = e.Actor;
        if (e.Kind == 1 && a is { IsNpc: true }) return QuestMovieName(a.Quest.QuestId, a.Quest.State);
        if (!string.IsNullOrEmpty(e.Icon)) return e.Icon;
        if (e.Kind == 0 && a is { IsNpc: true }) return NpcMovieName(a.NpcKind);
        return "";
    }

    // ---- the entity's states (uiemoticon.cpp, uihudentity.cpp) --------------------------------------------------------

    /// <summary>vfunc_2: Entity::vfunc_2 (m_48 = m_4c = 1) and the timer started for m_64.</summary>
    void Start(Entity e) { e.Reopen = true; e.Active = true; e.TimerEnd = TickMs() + TimerMs; }

    /// <summary>vfunc_3: Entity::vfunc_3 (m_48 = m_4c = 0), m_60 = 0, the timer expired, then vfunc_7 (the clip hidden).</summary>
    void Stop(Entity e) { e.Reopen = false; e.Active = false; e.Held = false; e.TimerEnd = long.MinValue; Hide(e); }

    /// <summary>vfunc_7: the clip hidden.</summary>
    void Hide(Entity e) { e.ClipVisible = false; if (e.Clip != null) Host.SetVisible(e.Clip, false); }

    /// <summary>FUN_140f48e80: m_58 set; clearing it asks for a new open (FUN_140f48d80).</summary>
    static void SetHidden(Entity e, bool hidden)
    {
        if (e.Hidden == hidden) return;
        if (!hidden) e.Reopen = true;
        e.Hidden = hidden;
    }

    /// <summary>
    /// vfunc_5: Entity::vfunc_5 (the clip shown, m_48 = 0), open(FUN_14192fa50()) and validateNow (vfunc_24(true)); the
    /// status kind's name is used once.
    /// </summary>
    void Open(Entity e)
    {
        e.ClipVisible = true;
        e.Reopen = false;
        if (e.Clip != null)
        {
            Host.SetVisible(e.Clip, true);
            e.Opened = MovieName(e);
            Host.Invoke(e.Clip, "open", e.Opened);
            Host.Invoke(e.Clip, "validateNow");
        }
        if (e.Kind == 2) e.Icon = "";
    }

    /// <summary>
    /// FUN_14192f910: an npc's refresh. A quest mark (state not 4) is kind 1, started; else kind 0, started when it has a
    /// movie name, else kind 4 and stopped.
    /// </summary>
    public void Refresh(Entity e)
    {
        if (e.Actor is not { IsNpc: true } a) return;
        if (a.Quest.State != 4) { e.Kind = 1; Start(e); return; }
        e.Kind = 0;
        if (MovieName(e).Length != 0) Start(e);
        else { e.Kind = 4; Stop(e); }
    }

    /// <summary>
    /// GetOrCreateEntity (@0x141930000): the actor's entity is released first; a pooled one is handed over, else the root's
    /// createEntity(id, m_70, false) makes the clip and the new entity runs its constructor's refresh (@0x14192ef40).
    /// STILL-OPEN: IsActorGone and IsUpperTargetId (the pooled clip's updateEntity) are not ported; the flag is false.
    /// </summary>
    public Entity GetOrCreate(Actor a)
    {
        if (Host?.Entity == null || a == null) return null;
        Release(a.ObjectId);
        if (pool.First is { } node)
        {
            pool.RemoveFirst();
            var reused = node.Value;
            reused.Actor = a;          // FUN_140f48ce0: the actor id, the anchor cache cleared
            entities[a.ObjectId] = reused;
            return reused;
        }
        var clip = Host.Invoke(Host.Entity, "createEntity", (double)a.ObjectId, EntityClass, false);
        if (Ms2.Avm2.Rt.IsNullish(clip)) return null;
        ClipsMade++;
        var e = new Entity { Actor = a, Clip = clip };
        entities[a.ObjectId] = e;
        Hide(e);                                 // the Entity ctor's FUN_140f48dc0(0)
        Refresh(e);                              // the UIEmoticon::Entity ctor
        return e;
    }

    /// <summary>
    /// ReleaseEntity (uihudentity.cpp @0x140f49c00, RemoveEmoticonEntity from PCBase's teardown): vfunc_4 (the clip hidden;
    /// m_48, m_4c, m_50, m_58 cleared; kind 4, no icon) and the entity pooled. The timer and m_60 stay.
    /// </summary>
    public void Release(int actorId)
    {
        if (!entities.Remove(actorId, out var e)) return;
        Hide(e);
        e.Reopen = e.Active = e.Hidden = false;
        e.Kind = 4;
        e.Icon = "";
        e.Actor = null;
        pool.AddLast(e);
    }

    // ---- the npc side (unit_14108e1e0_npcactorandvibrate.cpp) -------------------------------------------------------

    /// <summary>
    /// CNpc::vfunc_83 (@0x1410903b0): none while <see cref="ProgressiveLoading"/> is on and the actor is not added; else the
    /// npc's entity (m_spA88), made for a friendly npc (vfunc_116 above 0) when it has none.
    /// </summary>
    public Entity NpcEntity(Actor a)
    {
        if (ProgressiveLoading && !a.ActorAdded) return null;
        if (entities.TryGetValue(a.ObjectId, out var e)) return e;
        return a.Friendly > 0 ? GetOrCreate(a) : null;
    }

    /// <summary>GameObject::vfunc_12 (@0x14123b9d0): the actor was added (m_unk_c8 = 1). CNpc::vfunc_12 asks for no emoticon.</summary>
    public static void OnActorAdded(Actor a) => a.ActorAdded = true;

    /// <summary>
    /// The CNpc spawn's emoticon (CNpc::vfunc_10, npcactorandvibrate.cpp:561-577): when vfunc_83 gives one, an iconName other
    /// than "empty" holds it (m_60), kind 0 with that name, started; no iconName and a kind above 0 holds it as kind 0,
    /// started; then RefreshQuestState. None (and no RefreshQuestState) when vfunc_83 gives none.
    /// </summary>
    public Entity SpawnNpc(Actor a, Func<NpcQuestMarks.Mark> questState)
    {
        var e = NpcEntity(a);
        if (e == null) return null;
        if (!string.IsNullOrEmpty(a.Icon))
        {
            if (a.Icon != "empty") { e.Held = true; e.Kind = 0; e.Icon = a.Icon; Start(e); }
        }
        else if (a.NpcKind > 0) { e.Held = true; e.Kind = 0; Start(e); }
        RefreshQuestState(a, questState);
        return e;
    }

    /// <summary>
    /// CNpc::vfunc_53, the npc's update (@0x1410952f0): with quests, every 1000 ms the mark is asked for again (m_nE10); then
    /// RefreshQuestState when asked.
    /// </summary>
    public void NpcTick(Actor a, Func<NpcQuestMarks.Mark> questState)
    {
        long now = TickMs();
        if (a.HasQuests && now - a.QuestTickAt > 1000) { a.QuestTickAt = now; a.QuestDirty = true; }
        if (a.QuestDirty) RefreshQuestState(a, questState);
    }

    /// <summary>
    /// CNpc::RefreshQuestState (@0x141093c80): a friendly npc with quests gets GetNpcQuestState(npc, all = 0) again; when the
    /// icon or the state changed, vfunc_83's emoticon (made now if the npc has none) refreshes (FUN_14192f910).
    /// </summary>
    public void RefreshQuestState(Actor a, Func<NpcQuestMarks.Mark> questState)
    {
        a.QuestDirty = false;
        if (a.Friendly == 0 || !a.HasQuests) return;
        var was = a.Quest;
        a.Quest = questState();
        if (was.Icon != a.Quest.Icon || was.State != a.Quest.State)
            if (NpcEntity(a) is { } e) Refresh(e);
    }

    // ---- the frame (UIHudEntity::vfunc_15, uihudentity.cpp @0x140f49950) ----------------------------------------------

    /// <summary>
    /// Entity::vfunc_8 of UIEmoticon (@0x14192f5a0), TimerEntity (@0x140f49540) and Entity (@0x140f490b0): 1 hides the clip.
    /// </summary>
    int HideTest(Entity e)
    {
        var a = e.Actor;
        if (a == null) return 1;
        if (a.BalloonActive && e.Kind != 1 && e.Kind != 3) return 1;
        if (SceneHidesEmoticons())
        {
            if (e.Kind != 2) return 1;
        }
        else if (e.Kind == 2)
        {
            e.Kind = 4;
            Refresh(e);
            return 1;
        }
        if (!e.Held && !e.TimerRunning(TickMs())) return 1;
        if (a.Distance > VisibleDistance) return 1;
        if (a.Hidden) return 1;
        return 0;
    }

    /// <summary>
    /// Every frame, per entity of an actor still there: an inactive one (m_4c 0) is skipped; one the camera cannot project
    /// (<paramref name="project"/> null) or that the tests hide is hidden once; else it is opened when asked (m_48) and
    /// placed (vfunc_6). <paramref name="project"/> gives the actor's anchor (vfunc_10, its "Eff_UI" dummy) on the stage
    /// (the screen point times GetMovieScale) and the camera-pitch shift there (FUN_1412762d0).
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
                if (!e.Hidden) { SetHidden(e, true); Hide(e); }
                continue;
            }
            if (e.Hidden) SetHidden(e, false);
            if (e.Reopen) Open(e);
            Place(e, pt.Value);
        }
    }

    /// <summary>
    /// vfunc_6 (@0x14192f3a0): an npc's point moves by OffsetNpcEmoticon; kind 3 by -90, kind 2 by 30; then the camera-pitch
    /// shift; SetPositionFloat. STILL-OPEN: a PC's offset (FUN_141670500) is not ported.
    /// </summary>
    void Place(Entity e, (float X, float Y, float Shift) pt)
    {
        float x = pt.X, y = pt.Y;
        if (e.Actor.IsNpc) y += NpcOffset;
        if (e.Kind == 3) y += -90f;
        else if (e.Kind == 2) y += 30f;
        y += pt.Shift;
        if (e.Clip == null || (e.Placed && x == e.X && y == e.Y)) return;
        e.X = x; e.Y = y; e.Placed = true;
        Host.Set(e.Clip, "x", (double)x);
        Host.Set(e.Clip, "y", (double)y);
    }
}
