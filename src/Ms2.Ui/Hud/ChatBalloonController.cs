namespace Ms2.Ui.Hud;

/// <summary>
/// UIChatBalloon (ms2/client/ms2/uichatballoon.cpp, UI 0x4e2e, "PopupImageBalloon"), the C++ side of uipopupimageballoon:
/// the chat balloon over an actor. A UIHudEntity like <see cref="EmoticonController"/>: one movie for every actor, whose
/// root (HudEntityFactory) makes one "imageballoon_Ani" clip per actor (createEntity); a released actor's entity goes to
/// a pool and is handed to the next actor. Each clip's open(obj) shows the balloon of obj.skin ("imageballoon_balloon" or
/// "imageballoon_balloon_" + skin) with obj.text and the obj.imgPath picture, and plays "open"; close() plays "close".
/// <para>
/// The entity (UIChatBalloon::Entity, a UIHudEntity::TimerEntity): a balloon is started (vfunc_2) for ChatBalloonDuration
/// ms and shown while the timer runs and the actor is within ChatBalloonDistance; it stays active (m_4c) until it is
/// stopped (vfunc_3), which is what the emoticon's hide test reads (<see cref="EmoticonController.Actor.Balloon"/>).
/// </para>
/// <para>
/// The npc side is CNpc::vfunc_85 (@0x1410904c0, <see cref="NpcBalloon"/>): the npc's balloon, made on first use.
/// The npc monologue (UITalkManager::ShowNpcMonologue, called by CNpc::UpdateMonologue) shows through
/// <see cref="NpcMonologue"/>. STILL-OPEN: PCBase::ShowChatBalloon's (pcbase.cpp:1664) other callers, the chat's
/// (UIMessengerBrowser::ShowChatBalloon) and the talk and cut scene ones, are not ported; the PCs' and pets' balloons (pc.cpp:1589,
/// cubespawnobject.cpp:242) and the gem skins (itemgem.cpp:674/688) have no caller here.
/// </para>
/// </summary>
public sealed class ChatBalloonController : UiController
{
    public override string Movie => "uipopupimageballoon";
    protected override string EntityMethod => "createEntity";
    protected override bool IsDialog => false;          // a UIHudEntity (UIHeadUpDisplay)

    /// <summary>m_70: the movie class of each actor's clip.</summary>
    public const string EntityClass = "imageballoon_Ani";

    /// <summary>What the balloon reads from an actor (PCBase / CNpc / PC / CubeSpawnObject).</summary>
    public sealed class Actor
    {
        public int ObjectId;
        /// <summary>IsKindOf(0x6200028), the CNpc class: open's bNpc, and the npc data's portrait as the picture.</summary>
        public bool IsNpc;
        /// <summary>DynamicCast&lt;PC&gt;: a player; its profile picture (<see cref="ProfileImage"/>) and the gem test.</summary>
        public bool IsPc;
        /// <summary>DynamicCast&lt;MyPC&gt;: the local player (a PC too), whose balloon the own-balloon option shows.</summary>
        public bool IsMyPc;
        /// <summary>TypeCast 0x6200080 (CubeSpawnObject, FUN_1418fc490): a pet; its npc's portrait as the picture.</summary>
        public bool IsPet;
        /// <summary>CNpcData m_strPortrait of the npc, or of the pet's npc (GetNpc).</summary>
        public string Portrait = "";
        /// <summary>A PC's FUN_141028be0(FUN_141656a80(), FUN_1416612e0(), 1, ""): its profile picture path.</summary>
        public string ProfileImage = "";
        /// <summary>A PC's gem equip FUN_141270740(3): a chat balloon gem is equipped; without one the skin is dropped.</summary>
        public bool BalloonGem;
        /// <summary>PCBase IsDead: the "dead" skin, and the riding and FUN_141674bc0 tests skipped.</summary>
        public bool Dead;
        /// <summary>
        /// FUN_141670500 (PCBase m_f168), vfunc_9's y offset: OffsetNpcChatballoon for an npc (the CNpc constructor,
        /// npcactorandvibrate.cpp:492), OffsetPcChatballoon for a PC (pc.cpp:661).
        /// </summary>
        public float Offset;
        /// <summary>GameObject m_unk_c8 (FUN_14123dbd0): the actor's model was added (see <see cref="EmoticonController.Actor.ActorAdded"/>).</summary>
        public bool ActorAdded;
        /// <summary>GameObject m_unk_bc: the distance to the local player.</summary>
        public float Distance;
        /// <summary>The action state 0x3f test of Entity::vfunc_8 (uihudentity.cpp @0x140f490b0). STILL-OPEN: not reported.</summary>
        public bool StateHidden;
        /// <summary>
        /// The riding (FUN_14123c660 with no ridee or a ridee that rides) and FUN_141674bc0 tests of Entity::vfunc_8, which
        /// a dead actor skips. STILL-OPEN: not reported.
        /// </summary>
        public bool Hidden;
    }

    /// <summary>
    /// The scene tests of vfunc_8: GFxSceneService GetSubSceneId is 0x620001d (UISceneGamePlay, the HUD) or FUN_1422b9ea0
    /// (a cinematic sub scene) is 1; any other sub scene (the NPC talk's 0x6200031 among them) hides every balloon.
    /// </summary>
    public Func<bool> SceneShowsBalloons = () => true;
    /// <summary>CameraMgr FUN_140a18070 (m_1b0): vfunc_5 opens nothing while it is 0.</summary>
    public Func<bool> CameraReady = () => true;
    /// <summary>
    /// CGameOption FUN_14124ab20's community options: m_0 the own balloon (displayMyChatBalloon), m_1 the other players'
    /// (displayOtherChatBalloon). An npc's balloon has no option (FUN_1418fbbf0).
    /// </summary>
    public Func<bool> DisplayMyBalloon = () => true, DisplayOtherBalloon = () => true;
    /// <summary>
    /// COptimizer FUN_1409f7ea0, ProgressiveLoading: CNpc::vfunc_85 gives no balloon while the actor is not added.
    /// </summary>
    public bool ProgressiveLoading = true;
    /// <summary>IsUpperTargetId (@0x140930110): the object is the local player's.</summary>
    public Func<int, bool> IsUpperTarget = _ => false;
    /// <summary>The time in ms (GetTickCountMs2): the balloon's timer.</summary>
    public Func<long> TickMs = () => Environment.TickCount64;
    /// <summary>ChatBalloonDuration (constants +0x8a0), m_64 of every entity: the ms a started balloon shows.</summary>
    public int Duration = 5000;
    /// <summary>ChatBalloonDistance (constants +0x270), m_54 of every entity.</summary>
    public float VisibleDistance = 2000;
    /// <summary>CommonResource m_strDefaultProfileImagePath (DEFAULT_PROFILE_IMAGE_PATH): the picture of any other actor.</summary>
    public string DefaultImage = "";

    /// <summary>UIChatBalloon::Entity (uichatballoon.cpp @0x1418fafd0) over UIHudEntity::TimerEntity and Entity.</summary>
    public sealed class Entity
    {
        public Actor Actor;          // m_1c, the actor
        public object Clip;          // m_38
        /// <summary>m_a0 (FUN_1418fb6d0 sets it, FUN_1418fb6b0 reads it): the monologue leaves a set balloon alone.</summary>
        public bool Kept;
        /// <summary>m_a8 (FUN_1418fb6f0): the skin, a chat balloon gem's id.</summary>
        public string Skin = "";
        /// <summary>m_b0 (FUN_1418fb730): the text.</summary>
        public string Text = "";
        public bool Reopen;          // m_48: open on the next shown frame
        public bool Active;          // m_4c
        public bool Hidden;          // m_58
        public bool Held;            // m_60 (TimerEntity): shown without the timer
        public long TimerEnd = long.MinValue;   // m_68, started for m_64
        public bool ClipVisible;     // the clip's visibility (FUN_140f48dc0 / Entity::vfunc_5)
        /// <summary>The object the last open passed (text, imgPath, bNpc, skin).</summary>
        public (string Text, string ImgPath, bool Npc, string Skin)? Opened;
        public int Opens, Closes;
        public float X, Y;           // the clip's place, stage units
        public bool Placed;

        public bool TimerRunning(long now) => now < TimerEnd;
    }

    readonly Dictionary<int, Entity> entities = new();    // m_a8
    readonly LinkedList<Entity> pool = new();             // m_78

    /// <summary>The entities by actor id, for the shots and tests.</summary>
    public IReadOnlyDictionary<int, Entity> Entities => entities;
    /// <summary>The clips made by createEntity so far.</summary>
    public int ClipsMade { get; private set; }

    /// <summary>OnLoadComplete (@0x1418fbd80): CreateEntityCache(m_68), with a count of 0: no clip is made ahead.</summary>
    public override void OnLoadComplete() { }

    /// <summary>The root stays where the movie put it, shown.</summary>
    public override void Layout(double stageW, double stageH) => Host.SetVisible(Host.Entity, true);

    // ---- the entity's states (uichatballoon.cpp, uihudentity.cpp) -----------------------------------------------------

    /// <summary>vfunc_2 (@0x1418fb110): TimerEntity::vfunc_2, m_48 = m_4c = 1 and the timer started for m_64.</summary>
    public void Start(Entity e) { e.Reopen = true; e.Active = true; e.TimerEnd = TickMs() + Duration; }

    /// <summary>vfunc_3 (@0x1418fb130): TimerEntity::vfunc_3 (m_48 = m_4c = 0, m_60 = 0, the timer expired), then vfunc_7.</summary>
    public void Stop(Entity e) { e.Reopen = false; e.Active = false; e.Held = false; e.TimerEnd = long.MinValue; Close(e); }

    /// <summary>vfunc_7 (@0x1418fb580): the clip's close().</summary>
    void Close(Entity e)
    {
        if (e.Clip == null) return;
        e.Closes++;
        Host.Invoke(e.Clip, "close");
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
    /// FUN_1418fb7e0 (@0x1418fb7e0), the picture: an npc's portrait when set; a PC's profile picture; a pet's npc portrait
    /// when set; else the default profile image.
    /// </summary>
    public string ImagePath(Actor a)
    {
        if (a == null) return "";
        if (a.IsNpc)
        {
            if (!string.IsNullOrEmpty(a.Portrait)) return a.Portrait;
        }
        else if (a.IsPc) return a.ProfileImage ?? "";
        else if (a.IsPet && !string.IsNullOrEmpty(a.Portrait)) return a.Portrait;
        return DefaultImage;
    }

    /// <summary>FUN_1418fbbf0 (@0x1418fbbf0): the own PC's option m_0, another PC's m_1, any other actor 1.</summary>
    bool OptionShows(Actor a)
    {
        if (a.IsMyPc) return DisplayMyBalloon();
        if (a.IsPc) return DisplayOtherBalloon();
        return true;
    }

    /// <summary>
    /// vfunc_5 (@0x1418fb1c0): Entity::vfunc_5 (the clip shown, m_48 = 0); with the camera set and the actor there, a PC
    /// without a chat balloon gem loses its skin, then open({ text, imgPath, bNpc, skin: "dead" for a dead actor }).
    /// </summary>
    void Open(Entity e)
    {
        SetClipVisible(e, true);
        e.Reopen = false;
        if (!CameraReady()) return;
        var a = e.Actor;
        if (a == null || e.Clip == null) return;
        if (a.IsPc && !a.BalloonGem) e.Skin = "";
        var opened = (e.Text ?? "", ImagePath(a), a.IsNpc, a.Dead ? "dead" : e.Skin ?? "");
        e.Opened = opened;
        e.Opens++;
        Host.Invoke(e.Clip, "open", Host.Obj(("text", opened.Item1), ("imgPath", opened.Item2), ("bNpc", opened.Item3), ("skin", opened.Item4)));
    }

    /// <summary>FUN_1418fb730: the text the next open shows (PCBase::ShowChatBalloon sets it, then vfunc_2).</summary>
    public static void SetText(Entity e, string text) => e.Text = text ?? "";

    /// <summary>FUN_1418fb6f0: the skin (a chat balloon gem's id, ItemGem).</summary>
    public static void SetSkin(Entity e, string skin) => e.Skin = skin ?? "";

    /// <summary>FUN_1418fb6d0: m_a0 set.</summary>
    public static void Keep(Entity e) => e.Kept = true;

    /// <summary>
    /// GetEntity (@0x1418fc050, through GetChatBalloonEntity @0x1418fbdb0): the actor's entity is released first; a pooled
    /// one is handed over (its clip moved by updateEntity when the local player's is involved), else the root's
    /// createEntity(id, m_70, false) makes the clip and a new entity (its clip hidden). None for an actor that is gone.
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
        var e = new Entity { Actor = a, Clip = clip };
        entities[a.ObjectId] = e;
        SetClipVisible(e, false);                // the Entity ctor's FUN_140f48dc0(0)
        return e;
    }

    /// <summary>
    /// ReleaseEntity (uihudentity.cpp @0x140f49c00; FUN_1418fbe30 from PCBase's teardown): vfunc_4 (@0x1418fb160: the clip
    /// hidden; Entity::vfunc_1 clears m_1c, m_48, m_4c, m_50, m_58; then no skin, no text, m_a0 cleared) and the entity
    /// pooled. The timer and m_60 stay.
    /// </summary>
    public void Release(int actorId)
    {
        if (!entities.Remove(actorId, out var e)) return;
        SetClipVisible(e, false);
        e.Reopen = e.Active = e.Hidden = false;
        e.Skin = e.Text = "";
        e.Kept = false;
        e.Actor = null;              // vfunc_1: m_1c = 0
        pool.AddLast(e);
    }

    // ---- the npc side (unit_14108e1e0_npcactorandvibrate.cpp) -------------------------------------------------------

    readonly Dictionary<Actor, Entity> npcBalloons = new();     // CNpc m_spAA8

    /// <summary>
    /// CNpc::vfunc_85 (@0x1410904c0): none while <see cref="ProgressiveLoading"/> is on and the actor is not added; else
    /// the npc's balloon (m_spAA8), made by GetChatBalloonEntity when it has none. Unlike the emoticon's vfunc_83 there is
    /// no friendly test. The npc keeps its pointer (an MS2Ptr) after a release, as CNpc does until its teardown.
    /// </summary>
    public Entity NpcBalloon(Actor a)
    {
        if (a == null) return null;
        if (ProgressiveLoading && !a.ActorAdded) return null;
        if (npcBalloons.TryGetValue(a, out var e)) return e;
        e = GetEntity(a);
        if (e != null) npcBalloons[a] = e;
        return e;
    }

    /// <summary>
    /// The npc's teardown (PCBase::vfunc_16, pcbase.cpp:894): FUN_1418fbe30 releases its balloon, and the npc and its
    /// pointer go.
    /// </summary>
    public void RemoveNpc(Actor a)
    {
        if (a == null) return;
        Release(a.ObjectId);
        npcBalloons.Remove(a);
    }

    // ---- the frame (UIHudEntity::vfunc_15, uihudentity.cpp @0x140f49950) ----------------------------------------------

    /// <summary>
    /// Entity::vfunc_8 (@0x1418fb5e0) over TimerEntity's (@0x140f49540) and Entity's (@0x140f490b0), with n the actor's
    /// IsDead: 1 hides the balloon.
    /// </summary>
    int HideTest(Entity e)
    {
        if (!SceneShowsBalloons()) return 1;
        var a = e.Actor;
        if (a == null) return 1;
        if (!OptionShows(a)) return 1;
        if (!e.Held && !e.TimerRunning(TickMs())) return 1;
        if (a.Distance > VisibleDistance) return 1;
        if (a.StateHidden) return 1;
        if (a.Hidden && !a.Dead) return 1;
        return 0;
    }

    /// <summary>
    /// Every frame, per entity of an actor still there: an inactive one (m_4c 0) is skipped; one the camera cannot project
    /// (<paramref name="project"/> null) or that the tests hide is closed once (vfunc_7); else it is opened when asked
    /// (m_48) and placed (vfunc_6). <paramref name="project"/> gives the actor's anchor (vfunc_10, its "Eff_UI" dummy) on
    /// the stage (the screen point times GetMovieScale).
    /// </summary>
    public void Process(Func<Actor, (float X, float Y)?> project)
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

    /// <summary>vfunc_6 (@0x1418fb4a0): the actor's offset (vfunc_9, @0x1418fb670) on y, then SetPositionFloat(x - 110, y - 90).</summary>
    void Place(Entity e, (float X, float Y) pt)
    {
        if (e.Clip == null) return;
        float x = pt.X - 110f, y = pt.Y + e.Actor.Offset - 90f;
        if (e.Placed && x == e.X && y == e.Y) return;
        e.X = x; e.Y = y; e.Placed = true;
        Host.Set(e.Clip, "x", (double)x);
        Host.Set(e.Clip, "y", (double)y);
    }
}
