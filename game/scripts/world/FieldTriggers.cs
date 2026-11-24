using Godot;
using Maple2.PacketLib.Tools;
using Ms2.Formats.Model;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2Godot.Movement;

namespace Ms2Godot.World;

/// <summary>The field's sound, which the sound trigger objects play their music and ambient sounds through (CSoundEventMgr).</summary>
public interface ITriggerSounds
{
    /// <summary>CSoundEventMgr::PlayMusic(cue, 0, 0x80, 0); cue 0 goes back to the previous cue.</summary>
    bool PlayTriggerMusic(int cue);
    /// <summary>CSoundEventMgr::FUN_14177ce30(entity, on): the entity's ambient sound on or off.</summary>
    void SetTriggerAmbient(FieldEntity e, bool on);
}

/// <summary>
/// The field's trigger objects as CTriggerService holds them: every entity with the MS2TriggerObject mixin, keyed by its
/// TriggerObjectID (property 0x6c37a861; CTriggerService::FUN_141861620, triggerservice.cpp:324), with the kind
/// TriggerObject::Create gives it. The Trigger packet's object events (<see cref="TriggerObjects"/>) are applied here;
/// the server sends the field entry's states with type 2 (the map's trigger scripts' first states, e.g. a SetMesh that
/// hides a field's lava blocks) and later changes with type 3.
/// A mesh event (TriggerObjectMesh::vfunc_2, triggeractioncli.cpp:643) shows or hides the entity
/// (WorldService::FadeEntity); hidden, its drawn and shadow instances get a zero basis, as FieldFade hides a faded copy.
/// The same event moves the entity's colliders (FUN_140b60e40, unit_140b602f0_gamerankingitemtail.cpp:109-121, called
/// at triggeractioncli.cpp:679 whatever the fade gave): an entity with PhysX props (CPhysXHelper::HasPhysicsProps,
/// physxhelper.cpp:281: a GeneratePhysX box or NIF PhysX meshes) goes to its PropCollisionGroup (0xada5c5d2) when shown and
/// to group 5 when hidden (CPhysXHelper::SetEntityCollisionGroup @0x141689ec0, physxhelper.cpp:368: the cube actor, else
/// every actor of its props). A PC moves with groups 0x399 (pc.cpp:804-805) and its ground rays use 0x389, neither with
/// bit 5, so a hidden mesh no longer stops the player. An entity without PhysX props keeps its floor (FUN_140b60e40 returns
/// first). FieldBuilder gives a trigger mesh with colliders a body of its own (<see cref="FieldBuilder.TriggerCollider"/>);
/// the switch sets that body's layer, on the packet only.
/// A rope / ladder event (TriggerObjectRope::vfunc_2 @0x140de7670, triggeractioncli.cpp:738-763) fades the entity in the
/// same way (FadeEntity, :744), sets its Climbable flag (0x4df0c0de, :745) to the event's second bool, and switches its
/// colliders by the show bool (FUN_140b60e40, :746). The flag is what PC::FUN_141659b80 (pc.cpp:3209), StateLadder::UpdateClimb
/// (stateladder.cpp:374) and StateRope::FUN_1417e1e60 (statemanager.cpp:1440) read: the field's <see cref="Climbables"/>
/// entry of the entity, bound with <see cref="Bind(Climbables)"/>. A hidden rope with the flag set stays climbable, as in the client.
/// SetEntityCollisionGroup then runs WorldService::FUN_141bc8e00 (worldservice_1.cpp:4995-5009): each PC's
/// controller gets ReportSceneChanged; NxController keeps no touched-shape state across moves for it to drop.
/// An actor event (TriggerObjectActor::vfunc_2 @0x140de74b0, triggeractioncli.cpp:716-727) sets the entity's IsVisible
/// flag (0xa0b6b179, FUN_140b60e10 at :720) to the event's bool and plays the named sequence on it (Unk_AnimSvc::PlayAnimation,
/// :723), which an unknown name leaves as it was; bound with <see cref="Bind(IEnumerable{IActor})"/>.
/// A cube event (TriggerObjectCube::vfunc_2 @0x140de7cd0, triggeractioncli.cpp:818-822) sets the entity's IsVisible flag
/// (FUN_140b60e10, :821) to the event's bool and nothing else: the cube's draws show or hide at once (the engine's scene graph
/// culls a Renderable entity whose IsVisible is cleared) and its colliders stay where they are.
/// A sound event plays the BGM object's music or turns the AMB object's ambient sound on or off (<see cref="ApplySound"/>),
/// through the field's sound, bound with <see cref="Bind(ITriggerSounds)"/>.
/// A camera event queues the object's camera step (<see cref="QueueCameraStep"/>), and a type 5 packet a list of them
/// (<see cref="QueueCameraSteps"/>); the steps drive the field's camera manager, bound with <see cref="Bind(TriggerCamera)"/>.
/// An effect event starts or removes the entity's effect through the field's effect service (<see cref="ApplyEffect"/>),
/// bound with <see cref="Bind(IRegionSkillEffects)"/>; the effects go with the field, as the object's dtor removes its handle.
/// The fade (FadeEntity, Fade tenths of a second) runs in <see cref="TriggerFade"/>; a mesh without drawn instances to
/// fade switches at once. The collision switch does not wait for it. A mesh or rope / ladder event whose fade starts plays
/// the entity's show or hide effect (<see cref="InsertShowHideEffect"/>).
/// STILL-OPEN: the model swap a shown mesh with a model
/// name gets (triggeractioncli.cpp:643-705: the urn "urn:gamebryo-scenegraph:" + the name, property 0x770e5fc6 and skin
/// scale 0x2d7cad2d); both servers send an empty name, so no live packet reaches it; whether the server's npc ground drops a hidden mesh
/// (NpcGroundSpace keeps it; the client casts no npc ground: OnFieldAddNpc @0x141718cf0, scenestategame.cpp:3533-3584,
/// and CNpc::ApplySyncBlock, npcactorandvibrate.cpp:913,965, place an npc at the packet's position + 4 z, so the
/// server decides); the minimap mark of a mesh event (UIMinimapDialog::SetTriggerVisible, :702-704) and an actor event's
/// (:724-726), which needs the dialog's trigger marks first (AddTrigger @0x141a3e4e0, uiminimapentity.cpp:2818-2843: an
/// entity with an icon property 0x6814a50e gets a "triggerobject" mark keyed by its id; our minimap has none yet); the actor sequence's blend (PlayAnimation's 1.0f and false go to the client's own
/// NiActorManager::SetTargetAnimation, whose body is not in the recomp); a hidden cube's object weapon (IsObjectWeapon; PullObjectWeaponAction::FUN_140959eb0 takes only a cube whose IsVisible is set,
/// actionkeyobject.cpp:1873-1875; not ported here); the sound Effect kind (<see cref="ApplySound"/>); the camera parts listed at <see cref="TriggerCamera"/>; a trigger mesh drawn as an animated prop (FieldProps); a hidden
/// mesh that BGTransparencyMgr fades back in (FieldFade restores its transform).
/// </summary>
public sealed class FieldTriggers
{
    readonly Dictionary<int, TriggerObjectKind> kinds = new();
    readonly Dictionary<int, TriggerFade.Target> meshes = new();
    readonly Dictionary<int, FieldBuilder.TriggerCollider> colliders = new();
    readonly Dictionary<int, FieldEntity> entities = new();
    readonly Dictionary<int, Climbables.Climb> climbs = new();
    readonly Dictionary<int, int> effectHandles = new();
    readonly Dictionary<int, IActor> actors = new();
    IRegionSkillEffects effects;
    readonly Dictionary<int, int> soundHandles = new();
    ITriggerSounds sounds;
    TriggerCamera camera;
    readonly LinkedList<(int Id, bool Start)> cameraSteps = new();   // m_listCamera

    /// <summary>A drawn trigger actor: its entity, whether it is drawn (IsVisible) and the sequence it plays.</summary>
    public interface IActor
    {
        FieldEntity Entity { get; }
        bool Visible { get; set; }
        /// <summary>Plays the sequence of that name; false (nothing changes) when the actor has none.</summary>
        bool PlayAnimation(string sequence);
    }

    /// <summary>The node that runs the fades, in the tree with the field (null when built without fade draws).</summary>
    public readonly TriggerFade Fader;

    /// <summary>The group FUN_140b60e40 puts a hidden trigger mesh's colliders in.</summary>
    public const int HiddenGroup = 5;

    public FieldTriggers(IEnumerable<FieldEntity> entities, IReadOnlyDictionary<FieldEntity, List<(MultiMesh Mm, int Index)>> meshDraws,
                         IReadOnlyDictionary<FieldEntity, FieldBuilder.TriggerCollider> meshColliders = null,
                         IReadOnlyDictionary<FieldEntity, List<FieldBuilder.FadeDraw>> fadeDraws = null)
    {
        if (fadeDraws != null) Fader = new TriggerFade();
        foreach (var e in entities)
        {
            if (e.HasMixin("MS2TriggerBox")) boxes[(int)e.Number("TriggerObjectID")] = e.Position;
            if (TriggerObjects.KindOf(e.HasMixin) is not { } kind) continue;
            int id = (int)e.Number("TriggerObjectID");
            kinds[id] = kind;     // CAtlMap::SetAt: a later entity with the same id replaces the earlier
            this.entities[id] = e;
            bool fades = kind is TriggerObjectKind.Mesh or TriggerObjectKind.Rope;
            if ((fades || kind == TriggerObjectKind.Cube) && meshDraws.TryGetValue(e, out var draws) && draws.Count > 0)
            {
                var t = meshes[id] = new TriggerFade.Target();
                foreach (var (mm, i) in draws) t.All.Add((mm, i, mm.GetInstanceTransform(i)));
                if (fadeDraws != null && fadeDraws.TryGetValue(e, out var drawn)) t.Draws.AddRange(drawn);
            }
            else meshes.Remove(id);
            if (fades && meshColliders != null && meshColliders.TryGetValue(e, out var tc)) colliders[id] = tc;
            else colliders.Remove(id);
        }
    }

    readonly Dictionary<int, Vec3> boxes = new();

    /// <summary>The centre of the field's trigger box of that id (the server's box conditions; scripted runs stand in it).</summary>
    public Vec3? BoxCenter(int id) => boxes.TryGetValue(id, out var c) ? c : null;

    /// <summary>Binds the rope / ladder objects to the field's climbables: the entry of each object's entity.</summary>
    public void Bind(Climbables field)
    {
        climbs.Clear();
        foreach (var c in field.All)
            if (c.Entity != null && TriggerObjects.KindOf(c.Entity.HasMixin) == TriggerObjectKind.Rope
                && entities.TryGetValue((int)c.Entity.Number("TriggerObjectID"), out var e) && e == c.Entity)
                climbs[(int)e.Number("TriggerObjectID")] = c;
    }

    /// <summary>Binds the field's effect service (CEffectService), which the effect objects start and remove their effects with.</summary>
    public void Bind(IRegionSkillEffects service) => effects = service;

    /// <summary>Binds the field's sound (CSoundEventMgr), which the sound objects play their music and ambient sounds through.</summary>
    public void Bind(ITriggerSounds service) => sounds = service;

    /// <summary>Binds the field's camera manager (CameraMgr), which the camera objects' steps drive; its pending mode runs
    /// the next step.</summary>
    public void Bind(TriggerCamera c)
    {
        camera = c;
        c.RunNextCameraStep = RunNextCameraStep;
    }

    /// <summary>The camera steps still queued: object id and whether it starts (vfunc_3) or ends (vfunc_4).</summary>
    public IReadOnlyCollection<(int Id, bool Start)> CameraSteps => cameraSteps;

    /// <summary>The music cue the BGM sound object last played (m_nHandle; 0: none).</summary>
    public int SoundHandle(int id) => soundHandles.TryGetValue(id, out int h) ? h : 0;

    /// <summary>Binds the drawn trigger actors: each to the actor object of its entity.</summary>
    public void Bind(IEnumerable<IActor> drawn)
    {
        actors.Clear();
        foreach (var a in drawn)
            if (a.Entity != null && entities.TryGetValue((int)a.Entity.Number("TriggerObjectID"), out var e) && e == a.Entity
                && kinds[(int)e.Number("TriggerObjectID")] == TriggerObjectKind.Actor)
                actors[(int)e.Number("TriggerObjectID")] = a;
    }

    /// <summary>The drawn actor of the actor object with this id, or null.</summary>
    public IActor Actor(int id) => actors.TryGetValue(id, out var a) ? a : null;

    /// <summary>The handle of the effect the effect object last started (m_nHandle; 0: none).</summary>
    public int EffectHandle(int id) => effectHandles.TryGetValue(id, out int h) ? h : 0;

    /// <summary>The kind of the object with this id, or null when the field holds none.</summary>
    public TriggerObjectKind? KindOf(int id) => kinds.TryGetValue(id, out var k) ? k : null;

    /// <summary>The number of trigger objects the field holds (CTriggerService::m_mapObjects' count).</summary>
    public int Count => kinds.Count;

    /// <summary>The field's name (its xblock), for the log lines.</summary>
    public string FieldName { get; init; } = "";

    /// <summary>Whether the mesh object is shown (null: no such drawn mesh); a fading one changes when its fade ends.</summary>
    public bool? MeshShown(int id) => meshes.TryGetValue(id, out var t) ? t.Shown : null;

    /// <summary>Reads one Trigger packet body and applies its object events; the count applied.</summary>
    public int Apply(ByteReader r)
    {
        int n = 0;
        var events = TriggerObjects.Read(r, KindOf, out byte type);
        if (type == TriggerObjects.Load) GD.Print($"trigger load: {events.Count} event(s) on {FieldName}, which holds {kinds.Count} trigger object(s)");
        if (type == TriggerObjects.CameraFadeOff)
        {
            // CTriggerService::OnCameraFadeOff (@0x141861f30, triggerservice.cpp:485): FUN_141863460(0), the reset at once
            ResetCamera(0f);
            return 1;
        }
        if (type == TriggerObjects.CameraObjects)
        {
            // CTriggerService::OnCameraObjects, triggerservice.cpp:439-448
            cameraSteps.Clear();
            var (ids, end) = TriggerObjects.ReadCameraObjects(r);
            QueueCameraSteps(ids, end);
            return ids.Count;
        }
        foreach (var ev in events)
        {
            // the client's line (FUN_141863a90, triggerservice.cpp:951-958) and, for the reader of the log, the packet's type,
            // its event count and the field's table
            if (!ev.Known) { GD.PushWarning($"Trigger Object Sync Error. ObjectID:{ev.Id} (type {type}, {events.Count} event(s); {FieldName} holds {kinds.Count} trigger object(s))"); continue; }
            if (ev.Kind == TriggerObjectKind.Camera)
            {
                // TriggerObjectCamera::vfunc_2 (@0x140de7d80, triggeractioncli.cpp:848-856): an off event of a type 2
                // packet is skipped; otherwise the queue is cleared and the object's step queued
                bool on = ev.Flag != 0;
                if (!on && ev.SkipFx) continue;
                ClearCameraSteps();
                QueueCameraStep(ev.Id, on);
                n++;
                continue;
            }
            if (ev.Kind == TriggerObjectKind.Effect) { if (ApplyEffect(ev)) n++; continue; }
            if (ev.Kind == TriggerObjectKind.Sound) { if (ApplySound(ev)) n++; continue; }
            if (ev.Kind == TriggerObjectKind.Actor)
            {
                if (!actors.TryGetValue(ev.Id, out var a)) continue;
                a.Visible = ev.Show;            // FUN_140b60e10(entity, bOn), triggeractioncli.cpp:720
                a.PlayAnimation(ev.Text);       // PlayAnimation(id, strAnim, 1.0f, false), :721-723
                n++;
                continue;
            }
            if (ev.Kind == TriggerObjectKind.Cube)
            {
                // FUN_140b60e10(entity, b): IsVisible, triggeractioncli.cpp:820-821; no fade and no collider switch
                if (!meshes.TryGetValue(ev.Id, out var cube)) continue;
                TriggerFade.Switch(cube, ev.Show);
                n++;
                continue;
            }
            if (ev.Kind is not (TriggerObjectKind.Mesh or TriggerObjectKind.Rope)) continue;
            bool drawn = meshes.TryGetValue(ev.Id, out var t);
            // WorldService::FadeEntity(id, bShow, nFade * 0.1f), triggeractioncli.cpp:650-651 (rope: :743-744)
            // it is true when the entity's show state starts to change (FUN_141bd8860 / FUN_141bd8ae0)
            bool faded = false;
            if (drawn && Fader != null && t.Draws.Count > 0) faded = Fader.FadeEntity(ev.Id, t, ev.Show, ev.Fade * 0.1f);
            else if (drawn) { faded = t.Shown != ev.Show; TriggerFade.Switch(t, ev.Show); }
            // SetCachedBoolProperty(entity, 0x4df0c0de, bFlag), triggeractioncli.cpp:745
            bool climbable = false;
            if (ev.Kind == TriggerObjectKind.Rope && climbs.TryGetValue(ev.Id, out var c)) { c.Usable = ev.Flag2; climbable = true; }
            // FUN_140b60e40(entity, bShow), triggeractioncli.cpp:679 (rope: :746): after the fade and model swap
            bool collides = colliders.TryGetValue(ev.Id, out var tc);
            if (collides) tc.SetGroup(ev.Show ? tc.Group : HiddenGroup);
            if (faded && !ev.SkipFx) InsertShowHideEffect(ev);
            if (drawn || collides || climbable) n++;
        }
        return n;
    }

    /// <summary>
    /// The effect a mesh or rope / ladder event plays once its fade starts, unless the event is a type 2 one
    /// (TriggerObjectMesh::vfunc_2, triggeractioncli.cpp:687-701; TriggerObjectRope::vfunc_2, :747-761): the entity's
    /// showEffectXml (0x6dd85c78) or hideEffectXml (0x6454a3bb), picked by the mesh's show bool or the rope's second
    /// bool, inserted once (CEffectService::InsertEffect, not kept) on the entity's scene node, here at its placement
    /// (<see cref="FieldEntity.Transform"/>). An empty name plays nothing.
    /// </summary>
    void InsertShowHideEffect(TriggerObjectEvent ev)
    {
        if (effects == null || !entities.TryGetValue(ev.Id, out var e)) return;
        bool show = ev.Kind == TriggerObjectKind.Rope ? ev.Flag2 : ev.Show;
        string fx = e.Get(show ? "showEffectXml" : "hideEffectXml") ?? "";
        if (fx.Length != 0) effects.Insert(fx, e.Transform, false, 0);
    }

    /// <summary>
    /// TriggerObjectEffect::vfunc_2 (@0x140de7a00, triggeractioncli.cpp:785-808): removes the effect the object started
    /// before (CEffectService::FUN_1411a6060, :790-791), then, when on, starts the entity's XmlFilePath (0xdd2ac78a,
    /// GetEffectName) through FUN_1411a49c0 at <see cref="EffectAt"/> and keeps its handle (:792-807). An off event leaves
    /// the old handle, whose next removal finds nothing. The u8 and i32 the reader takes are not used.
    /// Not applied (false) when the field has no effect service bound.
    /// </summary>
    bool ApplyEffect(TriggerObjectEvent ev)
    {
        if (effects == null || !entities.TryGetValue(ev.Id, out var e)) return false;
        if (effectHandles.TryGetValue(ev.Id, out int h) && h != 0) effects.FadeOut(h, 0);
        if (ev.Show) effectHandles[ev.Id] = effects.Spawn(e.Get("XmlFilePath") ?? "", EffectAt(e), false, 0);
        return true;
    }

    /// <summary>
    /// TriggerObjectSound::vfunc_2 (@0x140de68f0, triggeractioncli.cpp:555-613), by the kind vfunc_1 read from the entity's
    /// SoundType (0xf83bad53, :541-552: "BGM" 1, "AMB" 2, "Effect" 3, else 0), on when the event's byte is not 0:
    /// BGM on plays atoi(soundEventID) (0xed530c5f) when it is above 0 and keeps it as the handle (:562-573); BGM off with a
    /// handle plays cue 0, back to the previous music, and clears it (:575-581; the soundEndXml it reads is not used);
    /// AMB turns the entity's ambient sound on or off when its soundEventID is not empty (:583-589).
    /// STILL-OPEN: the Effect kind (:590-611, soundStartXml / soundEndXml through CEffectService with no target object);
    /// no field's sound object has it.
    /// Not applied (false) when the field has no sound bound or the object's kind does nothing.
    /// </summary>
    bool ApplySound(TriggerObjectEvent ev)
    {
        if (sounds == null || !entities.TryGetValue(ev.Id, out var e)) return false;
        bool on = ev.Flag != 0;
        string kind = e.Get("SoundType") ?? "";
        string id = e.Get("soundEventID") ?? "";
        if (kind == "BGM")
        {
            if (on)
            {
                int cue = id.Length == 0 ? 0 : Atoi(id);
                if (cue <= 0) return true;
                sounds.PlayTriggerMusic(cue);
                soundHandles[ev.Id] = cue;
            }
            else if (SoundHandle(ev.Id) != 0)
            {
                sounds.PlayTriggerMusic(0);
                soundHandles[ev.Id] = 0;
            }
            return true;
        }
        if (kind == "AMB")
        {
            if (id.Length != 0) sounds.SetTriggerAmbient(e, on);
            return true;
        }
        return false;
    }

    /// <summary>CTriggerService::ClearCameraSteps (@0x1418631c0, triggerservice.cpp:801-804).</summary>
    public void ClearCameraSteps() => cameraSteps.Clear();

    /// <summary>CTriggerService::QueueCameraStep (@0x1418631f0, triggerservice.cpp:807-815): the step goes to the tail and,
    /// unless a fly-by runs, the next step runs at once.</summary>
    public void QueueCameraStep(int id, bool start)
    {
        cameraSteps.AddLast((id, start));
        if (camera?.ModeType != TriggerCamera.FlyBy) RunNextCameraStep();
    }

    /// <summary>CTriggerService::RunNextCameraStep (@0x141863260, triggerservice.cpp:819-831): takes the head step and runs
    /// its object's start (vfunc_3) or end (vfunc_4); an id the field does not hold does nothing. Only the camera object
    /// overrides the two (TriggerObject's base ones do nothing).</summary>
    public void RunNextCameraStep()
    {
        if (cameraSteps.Count == 0) return;
        var (id, start) = cameraSteps.First.Value;
        cameraSteps.RemoveFirst();
        if (camera == null || !entities.TryGetValue(id, out var e) || kinds[id] != TriggerObjectKind.Camera) return;
        if (start) CameraStart(e);
        else CameraEnd(e);
    }

    /// <summary>CTriggerService::QueueCameraSteps (@0x141863340, triggerservice.cpp:834-855): the queue is cleared; the
    /// single id 0 puts the camera back to the follow mode at once; else each id's start is queued, and with
    /// <paramref name="end"/> the last id's end after them.</summary>
    public void QueueCameraSteps(IReadOnlyList<int> ids, bool end)
    {
        cameraSteps.Clear();
        if (ids.Count == 0) return;
        if (ids.Count == 1 && ids[0] == 0)
        {
            camera?.SetMode(TriggerCamera.Target, false);
            return;
        }
        foreach (int id in ids) QueueCameraStep(id, true);
        if (end) QueueCameraStep(ids[^1], false);
    }

    /// <summary>The camera object's setting (triggeractioncli.cpp:862-872), from Unk_CameraSetting's defaults (fov 0,
    /// near 250, far 18000, zero position and rotation, not orthographic; cameramode.h:56-64) where the entity has none.</summary>
    public static CameraSetting CameraSettingOf(FieldEntity e)
    {
        var p = e.Vector("Position");
        var q = e.Vector("Rotation");
        return new CameraSetting
        {
            Fov = (float)e.Number("FOV", 0),
            DofNear = (float)e.Number("DOFNear", 250),
            DofFar = (float)e.Number("DOFFar", 18000),
            Position = new Vector3((float)p.X, (float)p.Y, (float)p.Z),
            Rotation = new Vector3((float)q.X, (float)q.Y, (float)q.Z),
            Ortho = e.Bool("IsOrthographic"),
        };
    }

    /// <summary>
    /// TriggerObjectCamera::vfunc_3 (@0x140de7e00, triggeractioncli.cpp:858-953), the step's start. With a
    /// StartInterpolationTime the camera flies from the current setting to the object's, eased by StartInterpolator
    /// (MakeFlyBy, then SetFlyBy with that kind); without one m_setting takes the object's setting, each rotation axis above
    /// 180 less 360. Then the lock mode is set (SetMode(4, 0)); without a fly-by it starts at once and the next step runs.
    /// STILL-OPEN: an object with an XmlFilePath (mode 5, the action camera: its camera path row and NIF camera; none is
    /// ported, so it does nothing); the depth of field (FUN_140af0b70), FUN_140af1a90 and FUN_140af7be0(EnableCameraEffect),
    /// RememberSetting with FixedLocationAfterEffect, the camera effect state's EndInterpolationTime and SendNpcTalkMode0.
    /// </summary>
    void CameraStart(FieldEntity e)
    {
        // the client sets the action camera (mode 5) here and does not run the next step itself (triggeractioncli.cpp:
        // 949-951); with mode 5 not ported the steps queued after such an object wait until the next camera packet
        // clears the queue
        if (!string.IsNullOrEmpty(e.Get("XmlFilePath"))) return;
        var set = CameraSettingOf(e);
        int kind = (int)(uint)e.Number("StartInterpolator", 1);
        float speed = (float)e.Number("StartInterpolationTime", 0);
        if (speed != 0f)
        {
            var fb = camera.MakeFlyBy(speed, set);
            fb.Kind = kind;
            camera.SetFlyBy(fb, kind, 1f);
        }
        else
        {
            if (set.Rotation.X > 180f) set.Rotation.X -= 360f;
            if (set.Rotation.Y > 180f) set.Rotation.Y -= 360f;
            if (set.Rotation.Z > 180f) set.Rotation.Z -= 360f;
            camera.SetSetting(set);
        }
        camera.SetMode(TriggerCamera.Lock, false);
        if (Mathf.IsZeroApprox(speed)) RunNextCameraStep();
    }

    /// <summary>
    /// TriggerObjectCamera::vfunc_4 (@0x140de86b0, triggeractioncli.cpp:955-998), the step's end: unless the follow mode is
    /// current, with an EndInterpolationTime the camera flies from the current setting (not orthographic) to the follow
    /// mode's fov, near, far and rotation, eased by EndInterpolator, ending at the follow mode's position (SetMode(2, 1));
    /// without one the follow mode starts at once.
    /// STILL-OPEN: FUN_140af7be0(1), FUN_140aed1c0 and the depth of field (FUN_140af0b70(0, ...)); the action camera path
    /// row's interpolation time.
    /// </summary>
    void CameraEnd(FieldEntity e)
    {
        if (camera.ModeType == TriggerCamera.Target) return;
        FlyToFollow((float)e.Number("EndInterpolationTime", 0), (int)(uint)e.Number("EndInterpolator", 1));
    }

    /// <summary>
    /// CTriggerService::FUN_141863460 (@0x141863460, triggerservice.cpp:859-883), the camera reset (OnCameraInterpolation,
    /// :508-512): the camera step queue is cleared, then the camera goes back to the follow mode, flying (kind 1) for
    /// <paramref name="seconds"/> when it is not 0, from whatever mode is current. A trigger's camera paths sent without
    /// their end (returnView 0) leave the lock mode on until this comes.
    /// STILL-OPEN: FUN_140aed1c0 (m_248, the remembered setting).
    /// </summary>
    public void ResetCamera(float seconds)
    {
        cameraSteps.Clear();
        if (camera == null) return;
        FlyToFollow(seconds, 1);
    }

    /// <summary>The follow mode, flown to for <paramref name="dur"/> seconds by <paramref name="kind"/> when dur is not 0:
    /// from the current setting (not orthographic) to the follow mode's fov, near, far and rotation, ending at the follow
    /// mode's position (SetMode(2, 1)); else at once (SetMode(2, 0)).</summary>
    void FlyToFollow(float dur, int kind)
    {
        if (dur != 0f)
        {
            var from = camera.Current;
            from.Ortho = false;
            var t = camera.TargetSetting;
            var to = from;
            to.Fov = t.Fov;
            to.DofNear = t.DofNear;
            to.DofFar = t.DofFar;
            to.Rotation = t.Rotation;
            camera.SetFlyBy(new CameraFlyBy { Duration = dur, Kind = kind, From = from, To = to }, kind, 1f);
            camera.SetMode(TriggerCamera.Target, true);
        }
        else camera.SetMode(TriggerCamera.Target, false);
    }

    /// <summary>The CRT's atoi: leading white space, an optional sign, then decimal digits up to the first other character.</summary>
    public static int Atoi(string s)
    {
        int i = 0;
        while (i < s.Length && (s[i] == ' ' || (s[i] >= '\t' && s[i] <= '\r'))) i++;
        bool neg = false;
        if (i < s.Length && (s[i] == '+' || s[i] == '-')) neg = s[i++] == '-';
        long v = 0;
        for (; i < s.Length && s[i] >= '0' && s[i] <= '9'; i++) v = Math.Min(v * 10 + (s[i] - '0'), (long)int.MaxValue + 1);
        v = neg ? -v : v;
        return (int)Math.Clamp(v, int.MinValue, int.MaxValue);
    }

    /// <summary>
    /// The transform an effect object starts its effect at (triggeractioncli.cpp:794-801): GetPosition, GetScale (1 when
    /// unset) and NiMatrix3::MakeZRotation of GetAngle(entity).z. The client passes the Rotation property's z, which is in
    /// degrees, to MakeZRotation unconverted (0x140de7b1d-0x140de7b2b load it straight into the call), so it is taken as
    /// radians here too. MakeZRotation(t) is Gamebryo's clockwise matrix: rows (cos t, sin t, 0), (-sin t, cos t, 0), (0, 0, 1).
    /// </summary>
    public static Xform EffectAt(FieldEntity e)
    {
        float t = (float)e.RotationDeg.Z;
        double c = MathF.Cos(t), s = MathF.Sin(t);
        return new Xform(e.Position, new[] { c, s, 0, -s, c, 0, 0, 0, 1 }, (float)e.Number("Scale", 1));
    }
}
