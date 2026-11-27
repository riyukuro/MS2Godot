using System;
using System.Collections.Generic;
using Ms2.Formats.Model;
using Ms2.Formats.World;
using Ms2.Net.Packets;

namespace Ms2.Ui;

/// <summary>
/// The breakable objects' server states (CSceneStateGame::OnBreakable, @0x1417212d0, scenestategame.cpp:3667-3694).
/// An entry whose name is a field marker (WorldService::FindMarkerByName) sets its state (CBreakableObject::SetState(state,
/// 0)) and its sync values (FUN_140adeaa0); any other name keeps its state in the cube map's list
/// (CubeMapInformationService::FUN_1410caa60, cubemapinformationservice_3.cpp:1298-1303: erased, then set).
/// The lookup is FindMarkerByName (@0x141bc4e70, worldservice_1.cpp:4321-4327), which lowercases the name; the cube list
/// keeps the packet's name as sent.
/// <para>The field's markers (<see cref="AttachField"/>): WorldService's field load (worldservice_1.cpp:6757-6768) makes a
/// CBreakableObject for every entity of model 0xd3b04f05 (net hash of "MS2Breakable") and adds it when vfunc_1(entity, 2)
/// (@0x140ade120, breakableobject.cpp:244-266) returns 1, which it always does.</para>
/// <para>SetState (<see cref="Marker"/>): the host gets <see cref="StateSet"/>, the marker's show/hide, the sequence and the
/// dead effect to play; the tag pass (<see cref="Hud.TombTagPass.RunMarkers"/>) reads the hittable and idle rules.</para>
/// <para>The physics box (<see cref="Marker.HasBox"/>, <see cref="BoxChanged"/>): MakeNxBoxController in states 2 (so at the
/// attach) and 6, ReleasePhysicsActor in 3 and 4.</para>
/// <para>The move cycle (<see cref="Marker.Step"/>, <see cref="Moved"/>): a marker with a move time and distance runs
/// Update's wait, go, wait, back cycle on the server tick, offset and frozen by the sync values.</para>
/// STILL-OPEN: the shadow; the props' re-read on a dirty entity (Update's FUN_140adf9d0, which would also take the moved
/// position as the new base). The reader of the cube map's list (+0x1a0) is not found.
/// </summary>
public sealed class BreakableObjects
{
    /// <summary>
    /// Whether the entity's NIF PhysX meshes reach the scene: ecrPhysX PropService::HandleSceneGraphAddedMessage
    /// (PropService.cpp:455-458) makes the NIF's PhysX prop only for an entity whose model contains PhysXProp. A field
    /// breakable (MS2BreakableNIF: MS2Breakable, MS2MapProperties, Mesh ...) has none, so its only collision is
    /// CBreakableObject's NxCollision box (MakeNxBoxController, breakableobject.cpp:656-705), which follows the marker.
    /// Other entities are left to the field builder's own rules.
    /// </summary>
    public static bool NifPhysX(FieldEntity e) => !e.HasMixin("MS2Breakable") || e.HasMixin("PhysXProp");

    public sealed class Marker
    {
        public string Name { get; internal set; }
        public int State { get; internal set; }
        /// <summary>m_nSyncA/B/C (FUN_140adeaa0, breakableobject.cpp:349-354): the ctor sets 1, 0, 0 (:137-139), so a marker
        /// the server never names runs its cycle from the field load. A = 0 stops the cycle at the cycle time C once the
        /// time passes it; B is added to the cycle time.</summary>
        public int SyncA { get; internal set; } = 1;
        public int SyncB { get; internal set; }
        public int SyncC { get; internal set; }
        /// <summary>The field entity (m_spEntity); null for a marker added by name only.</summary>
        public FieldEntity Entity { get; internal set; }
        /// <summary>The entity's id (GetEntityIDValue): a tag area keeps the ids it entered.</summary>
        public ulong EntityId { get; } = Hud.Tombstone.NextEntityId();
        /// <summary>m_bHittable: property 0x2683e9ec ("Enabled"), false when the size is zero (FUN_140adf9d0,
        /// breakableobject.cpp:586-590).</summary>
        public bool Hittable { get; internal set; }
        /// <summary>m_ptSize: property 0x9dc22aea ("ShapeDimensions"). The hit box is (x, y * 0.5, z) at offset
        /// (0, y * 0.5, 0) from the position (:621-625).</summary>
        public Vec3 Size { get; internal set; }
        /// <summary>m_bC1: property 0x54adb2fd ("reactionArrow"); the tag pass sends only for such a marker
        /// (worldservice_1.cpp:9268). The flats set it True for MS2BreakableActor and False for MS2BreakableNIF, which
        /// the enter pass (reactionEnter, worldservice_1.cpp:10712) takes instead.</summary>
        public bool ReactionArrow { get; internal set; }
        /// <summary>The hit box's m_b54: property 0xa34897b1 ("SkillPauseControllFeedback") (:630): a hit runs the tag's
        /// FUN_1417591b0.</summary>
        public bool SkillPauseFeedback { get; internal set; }
        /// <summary>m_nHitSound: property 0x8843d885 ("SoundMaterial"), the material FUN_140ade030 plays.</summary>
        public int HitSound { get; internal set; }
        /// <summary>m_ptPos / m_ptRot: properties 0xfd95ca52 ("Position") and 0x68c7c29d ("Rotation") (:617-618).</summary>
        public Vec3 Position { get; internal set; }
        public Vec3 Rotation { get; internal set; }
        /// <summary>m_bPhysics: property 0xd3ba88ae (breakableobject.cpp:595), the flats' NxCollision (the breakable flats'
        /// one physics flag; the hash is not checked against the name). MakeNxBoxController makes no box without it (:658).</summary>
        public bool Physics { get; internal set; }
        /// <summary>m_nGroup: property 0xada5c5d2 ("PropCollisionGroup") (:588), the box's shapes' group (:701).</summary>
        public int Group { get; internal set; }
        /// <summary>m_pActor is set: the box MakeNxBoxController (@0x140ae0210, :656-705) made and ReleasePhysicsActor
        /// (@0x140ae0720, :736-752) removed. The box is <see cref="Size"/> (its half dimensions m_ptSize * 0.5, :679-680) at the
        /// position with its centre raised by half the height (:689), turned by the rotation's z (rotZ, :690): see
        /// <see cref="BoxCenter"/>. Static: the kinematic body (:671-677) is for a moving marker.</summary>
        public bool HasBox { get; private set; }
        /// <summary>The box's centre: the position, z + half of the size's z.</summary>
        public Vec3 BoxCenter => new(Position.X, Position.Y, Position.Z + Size.Z * 0.5);

        /// <summary>MakeNxBoxController: nothing without m_bPhysics or while the box is up (:658-661). Returns whether it made one.</summary>
        internal bool MakeBox()
        {
            if (!Physics || HasBox) return false;
            return HasBox = true;
        }

        /// <summary>ReleasePhysicsActor. Returns whether a box went.</summary>
        internal bool ReleaseBox()
        {
            if (!HasBox) return false;
            HasBox = false;
            return true;
        }

        /// <summary>m_bHasAnim: the entity's model contains 0x48ccc02d ("Actor").</summary>
        public bool HasAnim { get; internal set; }
        /// <summary>m_strIdleAnim: property 0x3ff293c8 ("InitialSequence").</summary>
        public string IdleAnim { get; internal set; }
        /// <summary>The revive list: property 0xc9385319 ("reactionSequenceName") split on ",".</summary>
        public string[] ReviveAnims { get; internal set; } = Array.Empty<string>();
        /// <summary>m_strDeadEffect: property 0xf509dc4a ("reactionEffectXml"), played by state 3.</summary>
        public string DeadEffect { get; internal set; }
        /// <summary>Whether SetState last showed the model (FUN_140ae07d0(1): states 2, 3, 5 and 6) or hid it
        /// (FUN_140ae07d0(0), state 4: a 500 ms alpha fade from 1 to 0, breakableobject.cpp:755-776); other states leave it.
        /// A marker without an entity changes nothing (:277).</summary>
        public bool Shown { get; internal set; } = true;
        /// <summary>The entity's IsVisible (0xa0b6b179): set by a show, cleared by Update once a hide's 500 ms fade ran out.</summary>
        public bool Visible { get; internal set; } = true;
        /// <summary>The sequence the last SetState asked for and PlayAnimation's force flag; null when it plays nothing.
        /// Read and cleared by the model (<see cref="Played"/>).</summary>
        public string Play { get; private set; }
        public bool PlayForce { get; private set; }
        /// <summary>The effect the last SetState asked for (state 3's <see cref="DeadEffect"/>); read and cleared by the host.</summary>
        public string Effect { get; private set; }

        long hitUntil = long.MinValue, fadeEnd = -1;

        /// <summary>FUN_140addea0 (breakableobject.cpp:205) for a field marker: hittable, the hit timer (m_kHitTimer, 1000 ms
        /// from a hit) not running, and idle (state 2).</summary>
        public bool CanBeHit(long nowMs) => Hittable && nowMs >= hitUntil && State == 2;

        /// <summary>GetBox: the CBoxCollision FUN_140adf9d0 builds (:621-631) at the marker's position and rotation.</summary>
        public Skills.HitBox Box() => Skills.HitBox.Pose(
            new((float)Size.X, (float)(Size.Y * 0.5), (float)Size.Z), new(0, (float)(Size.Y * 0.5), 0),
            new((float)Position.X, (float)Position.Y, (float)Position.Z), new((float)Rotation.X, (float)Rotation.Y, (float)Rotation.Z));

        /// <summary>FUN_140ade030(hitter, kind) (breakableobject.cpp:186): PlayHitSound (the host plays <see cref="HitSound"/>),
        /// then m_kHitTimer.Start(1000).</summary>
        internal void Hit(long nowMs) => hitUntil = nowMs + 1000;

        /// <summary>The model took <see cref="Play"/>.</summary>
        public void Played() => Play = null;
        /// <summary>The host took <see cref="Effect"/>.</summary>
        public void EffectPlayed() => Effect = null;

        /// <summary>
        /// CBreakableObject::SetState(state, 0) (@0x140ade420, breakableobject.cpp:270-343) for a field marker: its regen and
        /// damage lists and effects are empty (FUN_140ae0c20 runs only for a tomb), so 5 and 6 only show it; 2 plays the idle
        /// (force) when the model animates and shows it; 3 plays the dead effect and a random revive entry (not forced) and
        /// shows it; 4 hides it. No entity: the state only (:277). FUN_140adf6e0 acts only for a tomb, so a field marker's
        /// state changes only by the server's Breakable. The box: MakeNxBoxController in 2 (:301) and 6 (:309),
        /// ReleasePhysicsActor in 3 (:322) and 4 (:335); 5 leaves it.
        /// STILL-OPEN: the shadow.
        /// </summary>
        internal void SetState(int state, long nowMs = 0, Func<int, int> pick = null)
        {
            State = state;
            if (Entity == null) return;
            Play = null; PlayForce = false;
            if (state is 2 or 6) MakeBox();
            else if (state is 3 or 4) ReleaseBox();
            switch (state)
            {
                case 2:
                    // m_bHasAnim alone gates it (:302); an empty name finds no sequence there, so it plays nothing here
                    if (HasAnim && !string.IsNullOrEmpty(IdleAnim)) (Play, PlayForce) = (IdleAnim, true);
                    break;
                case 3:
                    if (!string.IsNullOrEmpty(DeadEffect)) Effect = DeadEffect;
                    if (HasAnim && ReviveAnims.Length > 0) Play = ReviveAnims[(pick ?? Random.Shared.Next)(ReviveAnims.Length)];
                    break;
            }
            if (state is 2 or 3 or 5 or 6) { Shown = true; Visible = true; fadeEnd = -1; }
            else if (state == 4) { Shown = false; fadeEnd = nowMs + 500; }
        }

        /// <summary>Update's fade (FUN_140ae07d0): a hide's timer ran out, IsVisible is cleared.</summary>
        internal void Update(long nowMs)
        {
            if (fadeEnd < 0 || nowMs < fadeEnd) return;
            fadeEnd = -1;
            Visible = false;
        }

        internal bool Fading => fadeEnd >= 0;

        /// <summary>m_ptMoveDir: property 0x0c10618b ("moveDirection"), unitized (breakableobject.cpp:596, :604).</summary>
        public Vec3 MoveDir { get; internal set; }
        /// <summary>m_fMoveDelay: property 0x1994cfe8 ("moveTimeOffset"), seconds added to the cycle time (:597).</summary>
        public float MoveDelay { get; internal set; }
        /// <summary>m_fMoveTime: property 0xf6068266 ("moveDuration"), the seconds one way takes (:600).</summary>
        public float MoveTime { get; internal set; }
        /// <summary>m_afWait[0] / m_afWait[1]: properties 0x000bd4a6 ("moveStopDurationS") and 0x000bd4bc
        /// ("moveStopDurationE"), the seconds it waits at the start and at the end (:601-602).</summary>
        public float WaitStart { get; internal set; }
        public float WaitEnd { get; internal set; }
        /// <summary>m_nMoveMode: property 0x76ba4184 ("moveRoundType") (:619). 1: the cycle ends at the end wait (it jumps
        /// back to the start); 2: it turns 180 degrees about z at the end and on the way back; other: back without the turn.</summary>
        public int MoveMode { get; internal set; }
        /// <summary>m_fSpeed: property 0x83d0f1ba ("moveDistance") over the move time, when that is above zero (:605-606).</summary>
        public float Speed { get; internal set; }
        /// <summary>m_ptPosBase / m_ptRotBase: the position and rotation the props gave (:620-621).</summary>
        public Vec3 PosBase { get; internal set; }
        public Vec3 RotBase { get; internal set; }
        /// <summary>m_nPhase: 0 until the first move frame, then 1 (start wait), 2 (going), 3 (end wait, and the way back
        /// entered from it) or 4 (the way back entered from any other phase: Update tests phase != 3 there).</summary>
        public int Phase { get; private set; }
        /// <summary>m_ptVelocity: the last frame's move direction (the unitized position change), zero on a frame it did
        /// not move.</summary>
        public Vec3 Velocity { get; private set; }
        int phaseTime, phaseStart;

        /// <summary>Update moves it: 0 &lt; m_fMoveTime and m_fSpeed != 0 (breakableobject.cpp:376).</summary>
        public bool Moves => 0f < MoveTime && Speed != 0f;

        /// <summary>FUN_14028d260: seconds to milliseconds, truncated (commonutil.cpp:610), read as int.</summary>
        static int Ms(float s) => (int)(long)(s * 1000f);
        /// <summary>FUN_14028d280: milliseconds to seconds (commonutil.cpp:616).</summary>
        static float Sec(uint ms) => (float)ms * 0.001f;

        /// <summary>SetPhase (@0x140ae06a0, breakableobject.cpp:718-723): the phase's server time and local start.</summary>
        void SetPhase(int phase, int now, int serverTick)
        {
            phaseTime = serverTick;
            phaseStart = now;
            Phase = phase;
        }

        /// <summary>
        /// CBreakableObject::Update's move (@0x140adeaf0, breakableobject.cpp:362-423), in the client's float and unsigned
        /// math. <paramref name="now"/> is the local tick (GetTickCountMs2) and <paramref name="serverTick"/> is
        /// GetServerTickAt(now). The cycle time is the server time of the phase's start plus the local time since, plus the
        /// delay and SyncB; with SyncA 0 the place is SyncC's once the time passes it. The cycle is: start wait, go, end wait,
        /// back (mode 1: no back). Only states 2, 5 and 6 move. Returns whether the position or rotation changed
        /// (SetPosition and SetRotation change only a different, finite value, :538-560).
        /// </summary>
        internal bool Step(int now, int serverTick)
        {
            Velocity = default;
            if (!Moves || !(State is 2 or 5 or 6)) return false;
            unchecked
            {
                if (Phase == 0) { phaseTime = serverTick; phaseStart = now; }
                uint time = (uint)(phaseTime + (now - phaseStart) + Ms(MoveDelay) + SyncB);
                uint wait0 = (uint)Ms(WaitStart);
                uint go = wait0 + (uint)Ms(MoveTime);
                uint wait1 = go + (uint)Ms(WaitEnd);
                uint back = wait1 + (uint)Ms(MoveTime);
                uint cycle = MoveMode == 1 ? wait1 : back;
                if (cycle == 0) return false;   // the client divides by it; no data has a cycle under 1 ms
                uint pos = time % cycle;
                if (SyncA == 0 && (uint)SyncC < time) pos = (uint)SyncC % cycle;
                Vec3 rot;
                if (pos <= wait0)
                {
                    pos = 0;
                    rot = RotBase;
                    if (Phase != 1) SetPhase(1, now, serverTick);
                }
                else if (pos <= go)
                {
                    pos -= wait0;
                    rot = RotBase;
                    if (Phase != 2) SetPhase(2, now, serverTick);
                }
                else if (pos <= wait1)
                {
                    pos = (uint)Ms(MoveTime);
                    rot = MoveMode == 2 ? Turned() : Rotation;
                    if (Phase != 3) SetPhase(3, now, serverTick);
                }
                else
                {
                    pos = cycle - pos;
                    rot = MoveMode == 2 ? Turned() : Rotation;
                    if (Phase != 3) SetPhase(4, now, serverTick);
                }
                bool changed = false;
                if (rot != Rotation && Finite(rot)) { Rotation = rot; changed = true; }
                var old = Position;
                // m_ptMoveDir * m_fSpeed * FUN_14028d280(nPos): the point times the speed, then times the seconds
                float sec = Sec(pos);
                var p = new Vec3((float)PosBase.X + (float)MoveDir.X * Speed * sec,
                    (float)PosBase.Y + (float)MoveDir.Y * Speed * sec, (float)PosBase.Z + (float)MoveDir.Z * Speed * sec);
                if (p != old && Finite(p)) { Position = p; changed = true; }
                Velocity = Unitize(Position - old);
                return changed;
            }
        }

        Vec3 Turned() => new((float)RotBase.X, (float)RotBase.Y, (float)RotBase.Z + 180f);

        static bool Finite(Vec3 v) => double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);

        /// <summary>efd::Point3::Unitize: divided by the length when it is above 1e-6, else zero.</summary>
        internal static Vec3 Unitize(Vec3 v)
        {
            float x = (float)v.X, y = (float)v.Y, z = (float)v.Z;
            float len = MathF.Sqrt(x * x + y * y + z * z);
            if (len > 1e-6f) return new(x / len, y / len, z / len);
            return default;
        }
    }

    /// <summary>The model every field breakable has: net hash 0xd3b04f05 (worldservice_1.cpp:6760).</summary>
    public const string Model = "MS2Breakable";

    readonly Dictionary<string, Marker> markers = new(StringComparer.Ordinal);
    readonly Dictionary<string, int> cubeStates = new(StringComparer.Ordinal);

    /// <summary>A marker's state was set: the name and the state.</summary>
    public Action<string, int> StateSet;

    /// <summary>A marker's box was made or released (<see cref="Marker.HasBox"/>): at the attach or by a state.</summary>
    public Action<Marker> BoxChanged;

    /// <summary>The cube map's states by name (CubeMapInformationService +0x1a0).</summary>
    public IReadOnlyDictionary<string, int> CubeStates => cubeStates;

    public Marker AddMarker(string name, int state = 0)
    {
        // a marker's name is lowercase (vfunc_1's ToLower), as FindMarkerByName looks it up
        var m = new Marker { Name = name.ToLowerInvariant(), State = state };
        if (markers.TryGetValue(m.Name, out var was)) moving.Remove(was);
        markers[m.Name] = m;
        sortedDirty = true;
        return m;
    }

    /// <summary>rand() % n for the revive entry; tests set it.</summary>
    public Func<int, int> Pick;

    /// <summary>The clock SetState and the fades read: the last <see cref="Update"/>'s time.</summary>
    public long NowMs { get; private set; }

    readonly List<Marker> sorted = new();
    readonly List<Marker> fading = new();
    readonly List<Marker> moving = new();
    bool sortedDirty;

    /// <summary>A marker's position or rotation changed by its move cycle (<see cref="Marker.Step"/>): the draw, the box and
    /// what stands on it follow.</summary>
    public Action<Marker> Moved;

    /// <summary>The markers whose props make them move (<see cref="Marker.Moves"/>), in attach order; the others cost
    /// nothing per frame.</summary>
    public IReadOnlyList<Marker> Moving => moving;

    /// <summary>
    /// The markers as WorldService m_4b0 iterates them: a CRBMap keyed by the lowercase name, so in ordinal name order.
    /// Rebuilt only after a marker is added or the field is left.
    /// </summary>
    public IReadOnlyList<Marker> Sorted
    {
        get
        {
            if (sortedDirty)
            {
                sorted.Clear();
                foreach (var m in markers.Values) sorted.Add(m);
                sorted.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
                sortedDirty = false;
            }
            return sorted;
        }
    }

    /// <summary>The frame's clock without a server tick: the fades only.</summary>
    public void Update(long nowMs) => Update(nowMs, null);

    /// <summary>
    /// WorldService's per-frame marker loop (worldservice_1.cpp:9650-9655): CBreakableObject::Update(GetTickCountMs2()) for
    /// each marker. Here only the moving ones run the move cycle (<see cref="Marker.Step"/>, with
    /// <paramref name="serverTick"/> as GetServerTickAt(now)), and the hides whose 500 ms fade ran out clear IsVisible
    /// (FUN_140ae07d0's timer). A marker that does not move or fade is not visited.
    /// </summary>
    public void Update(long nowMs, int serverTick) => Update(nowMs, (int?)serverTick);

    ulong steppedFrame = ulong.MaxValue;

    /// <summary>
    /// <see cref="Update(long, int)"/> once per drawn <paramref name="frame"/>: WorldService's tick moves the markers before
    /// the actors step (worldservice_1.cpp:9648-9655, then the actor loop at gameobject.cpp:307-312), so the first caller of a
    /// frame (the field's marker step, ahead of the player) runs it and a later one in the same frame does nothing.
    /// </summary>
    public void UpdateOnce(ulong frame, long nowMs, int serverTick)
    {
        if (frame == steppedFrame) return;
        steppedFrame = frame;
        Update(nowMs, (int?)serverTick);
    }

    void Update(long nowMs, int? serverTick)
    {
        NowMs = nowMs;
        if (serverTick is int tick)
        {
            int now = unchecked((int)nowMs);
            for (int i = 0; i < moving.Count; i++)
                if (moving[i].Step(now, tick)) Moved?.Invoke(moving[i]);
        }
        for (int i = fading.Count - 1; i >= 0; i--)
        {
            var m = fading[i];
            m.Update(nowMs);
            if (!m.Fading) fading.RemoveAt(i);
        }
    }

    void SetState(Marker m, int state)
    {
        bool box = m.HasBox;
        m.SetState(state, NowMs, Pick);
        if (m.HasBox != box) BoxChanged?.Invoke(m);
        if (m.Fading && !fading.Contains(m)) fading.Add(m);
    }

    /// <summary>The tag pass hit <paramref name="m"/>: FUN_140ade030(1, 1)'s hit timer.</summary>
    internal void Hit(Marker m) => m.Hit(NowMs);

    /// <summary>FindMarkerByName: the name lowercased.</summary>
    public Marker Find(string name) => name != null && markers.TryGetValue(name.ToLowerInvariant(), out var m) ? m : null;

    public int Count => markers.Count;

    /// <summary>
    /// The field load's breakables loop (worldservice_1.cpp:6757-6768): each entity of model "MS2Breakable" gets a marker
    /// through vfunc_1(entity, 2). Its name is the entity's DataFileID lowercased (breakableobject.cpp:250-262; a field
    /// marker is never a tomb stone: only FUN_140adf370 sets m_bTombStone, :470-485). AddMarker (@0x141bc4b10,
    /// worldservice_1.cpp:4278-4284) is SetAt, so a later entity of the same name replaces the earlier. Then SetState(2, 0).
    /// Returns the markers added; the updates held since <see cref="ClearMarkers"/> are applied after.
    /// </summary>
    public int AttachField(IReadOnlyList<FieldEntity> entities)
    {
        int n = 0;
        sortedDirty = true;
        for (int i = 0; entities != null && i < entities.Count; i++)
        {
            var e = entities[i];
            if (!e.HasMixin(Model)) continue;
            string id = e.Raw?.Id;
            if (string.IsNullOrEmpty(id)) continue;
            // FUN_140adf9d0 (breakableobject.cpp:584-635)
            var size = e.Vector("ShapeDimensions");
            var revive = e.Get("reactionSequenceName");
            var m = new Marker
            {
                Name = id.ToLowerInvariant(), Entity = e, Size = size,
                Hittable = e.Bool("Enabled") && !(size.X == 0 && size.Y == 0 && size.Z == 0),
                ReactionArrow = e.Bool("reactionArrow"), SkillPauseFeedback = e.Bool("SkillPauseControllFeedback"),
                HitSound = (int)e.Number("SoundMaterial"),
                Position = e.Position, Rotation = e.Vector("Rotation"),
                HasAnim = e.HasMixin("Actor"), IdleAnim = e.Get("InitialSequence"),
                ReviveAnims = string.IsNullOrEmpty(revive) ? Array.Empty<string>() : revive.Split(',', StringSplitOptions.RemoveEmptyEntries),
                DeadEffect = e.Get("reactionEffectXml"),
                Physics = e.Bool("NxCollision"), Group = (int)e.Number("PropCollisionGroup"),
                MoveDelay = (float)e.Number("moveTimeOffset"), MoveTime = (float)e.Number("moveDuration"),
                WaitStart = (float)e.Number("moveStopDurationS"), WaitEnd = (float)e.Number("moveStopDurationE"),
                MoveMode = (int)e.Number("moveRoundType"),
            };
            var dir = e.Vector("moveDirection");
            m.MoveDir = Marker.Unitize(new((float)dir.X, (float)dir.Y, (float)dir.Z));
            if (0f < m.MoveTime) m.Speed = (float)e.Number("moveDistance") / m.MoveTime;
            m.Position = m.PosBase = new((float)m.Position.X, (float)m.Position.Y, (float)m.Position.Z);
            m.Rotation = m.RotBase = new((float)m.Rotation.X, (float)m.Rotation.Y, (float)m.Rotation.Z);
            if (markers.TryGetValue(m.Name, out var was)) moving.Remove(was);
            markers[m.Name] = m;
            if (m.Moves) moving.Add(m);
            SetState(m, 2);   // its MakeNxBoxController makes the box
            n++;
        }
        Holding = false;
        for (int i = 0; i < held.Count; i++) Apply(held[i]);
        held.Clear();
        return n;
    }

    readonly List<BreakableUpdate> held = new();

    /// <summary>Whether updates wait for <see cref="AttachField"/>: from a field enter until its entities are attached.</summary>
    public bool Holding { get; private set; }

    /// <summary>The field is left: its markers go. The client loads the new field before it reads the next packet; here the
    /// field loads later, so the updates that come first are held until <see cref="AttachField"/> and applied in order.</summary>
    public void ClearMarkers()
    {
        markers.Clear();
        fading.Clear();
        moving.Clear();
        sortedDirty = true;
        held.Clear();
        Holding = true;
    }

    /// <summary>FUN_1410b76a0 (cubemapinformationservice_3.cpp:414-419) clears the cubes and FUN_1410d0310 the list (:5445).</summary>
    public void ClearCubeStates() => cubeStates.Clear();

    public void Apply(BreakableUpdate u)
    {
        if (u == null) return;
        if (Holding) { held.Add(u); return; }
        foreach (var e in u.Entries)
        {
            if (e.Name != null && markers.TryGetValue(e.Name.ToLowerInvariant(), out var m))
            {
                SetState(m, e.State);
                m.SyncA = e.SyncA;
                m.SyncB = e.SyncB;
                m.SyncC = e.SyncC;
                StateSet?.Invoke(m.Name, e.State);
            }
            else cubeStates[e.Name] = e.State;
        }
    }
}

/// <summary>
/// The liftable objects by name (WorldService m_510; FindLiftByName @0x141bc5a20, worldservice_1.cpp:4461-4466) and
/// CSceneStateGame::OnLiftable (@0x141729250, scenestategame.cpp:11941-12020).
/// <para>A field lift (WorldService, worldservice_1.cpp:6771-6783) is CLiftableObject::Init(entity, false, kind): lift
/// disabled, state 0. Its name is the entity's lowercased DataFileID (BindEntity, liftableobject.cpp:186-191).</para>
/// <para>A mode 3 lift (FUN_141bc5420, worldservice_1.cpp:4429-4447) has no entity: Init(0, true, kind), then its name,
/// quest masks and flag; SetAt replaces a lift of that name.</para>
/// <para>A lift goes from the list when its entity is removed (worldservice_1.cpp:3526-3533). DestroyLiftByName
/// (:4451-4456) removes the entity of the named lift.</para>
/// <para>The field's lifts (<see cref="AttachField"/>): the field load (worldservice_1.cpp:6771-6786) makes a CLiftableObject
/// for every entity of model 0x32767dd (net hash of "MS2Liftable") with kind = property 0x3773e9e0 ("ItemStackCount").
/// Updates that come before the field's lifts are attached wait for them (<see cref="Holding"/>).</para>
/// <para>DestroyLiftByName on a lift without an entity is CMainSystem::DestroyEntity(0), which does nothing
/// (mainsystem.cpp:432-439): such a lift stays in the list, as here. The client binds a mode 3 lift to its placed cube's
/// entity (cubemapinformationservice_3.cpp:5689-5694, name "objCode_cellKey"); the placed cubes have no entity here
/// (STILL-OPEN), so that lift keeps none.</para>
/// <para>The host draws <see cref="ActiveSet"/> and <see cref="Removed"/> (world/FieldLiftables).</para>
/// STILL-OPEN: the quest masks (ParseQuestMask, liftableobject.cpp:105), UpdateConditions (:356), the collision groups and
/// the cube cell clear of a removed field lift (CubeMapInformationService::FUN_1410c4b50(cell, 0)) have no port.
/// </summary>
public sealed class LiftableObjects
{
    public sealed class Lift
    {
        public string Name { get; internal set; }
        /// <summary>m_nKind (SetKind @0x141447a60).</summary>
        public int Kind { get; internal set; }
        /// <summary>m_bC0 (SetLiftEnabled @0x1414478b0).</summary>
        public bool Enabled { get; internal set; }
        /// <summary>m_nState (SetState @0x141447b00).</summary>
        public int State { get; internal set; }
        /// <summary>m_bC1, SetActive's flag (@0x141447ec0, liftableobject.cpp:371-396): Init sets it (:171); SetState's
        /// states 0 and 2 SetActive(1), state 1 SetActive(0), others leave it (:296-309; only with an entity).</summary>
        public bool Active { get; internal set; } = true;
        /// <summary>The quest mask strings: FUN_1414477f0(A, B) and FUN_141447850(C, D).</summary>
        public string QuestA { get; internal set; }
        public string QuestB { get; internal set; }
        public string QuestC { get; internal set; }
        public string QuestD { get; internal set; }
        /// <summary>m_bC2 (FUN_141447aa0); the ctor sets it (liftableobject.cpp:121).</summary>
        public bool Flag { get; internal set; } = true;
        /// <summary>A field lift has an entity; a mode 3 lift has none.</summary>
        public bool HasEntity { get; internal set; }
        /// <summary>The field entity (m_ent); null for a mode 3 lift and for one added by name.</summary>
        public FieldEntity Entity { get; internal set; }
        /// <summary>m_fRange (GetRange @0x141447ac0): 150 from the ctor (liftableobject.cpp:127), else property 0xf39fa2a1
        /// ("ActionDistance") when the entity has it (BindEntity, :192-194).</summary>
        public float Range { get; internal set; } = 150f;
        /// <summary>m_vC4 (GetPosition, liftableobject.cpp:324): property 0xfd95ca52 ("Position") (:196).</summary>
        public Vec3 Position { get; internal set; }
        /// <summary>m_nA8: property 0x2e687f77 ("ItemID") for a field lift (:198-201).</summary>
        public int ItemId { get; internal set; }
        /// <summary>The action key's highlight (SetHighlight @0x141447d50, liftableobject.cpp:348): set only on a lift with an
        /// entity. STILL-OPEN: the entity highlight draw (SetEntityHighlight).</summary>
        public bool Highlighted { get; internal set; }

        /// <summary>CanLift (@0x141447340, liftableobject.cpp:155-164): enabled, a kind above 0, state 0.</summary>
        public bool CanLift => Enabled && Kind > 0 && State == 0;

        /// <summary>SetState (@0x141447b00): true when it ran SetActive (an entity and state 0, 1 or 2).</summary>
        internal bool SetState(int state)
        {
            State = state;
            if (!HasEntity) return false;                    // liftableobject.cpp:295
            if (state is 0 or 2) Active = true;
            else if (state == 1) Active = false;
            else return false;
            return true;
        }
    }

    readonly Dictionary<string, Lift> lifts = new(StringComparer.Ordinal);
    readonly HashSet<string> listed = new(StringComparer.Ordinal);
    readonly List<string> gone = new();
    // GetLiftableObjects (worldservice_1.cpp:873) walks m_510, a CRBMap keyed by the name: ordinal name order. Rebuilt only
    // after a lift is added or removed, so the poll allocates nothing.
    readonly List<Lift> ordered = new();
    bool orderStale;
    static readonly Comparer<Lift> ByName = Comparer<Lift>.Create((a, b) => string.CompareOrdinal(a.Name, b.Name));

    /// <summary>A field lift's entity was removed: its name.</summary>
    public Action<string> Removed;

    /// <summary>A field lift's SetState ran SetActive (liftableobject.cpp:296-309): its name and the flag. SetActive(1) shows
    /// the entity at alpha 1 (:375-381); SetActive(0) fades its alpha 1 to 0 over 500 ms (FUN_140ae1e80, :386-390), then
    /// the 500 ms timer hides it (FUN_141447c10, :313-321). The host draws it (world/FieldLiftables).</summary>
    public Action<string, bool> ActiveSet;

    public int Count => lifts.Count;
    public const string Model = "MS2Liftable";

    /// <summary>True from <see cref="FieldLeft"/> (a field change) until <see cref="AttachField"/>: updates wait in arrival
    /// order.</summary>
    public bool Holding { get; private set; }
    readonly List<LiftableUpdate> held = new();
    public Lift Find(string name) => name != null && lifts.TryGetValue(name, out var l) ? l : null;

    /// <summary>A field lift: Init(entity, false, kind) with the entity's lowercased DataFileID as its name.</summary>
    public Lift AddFieldLift(string dataFileId, int kind)
    {
        // Init (liftableobject.cpp:167-175): m_bC1 set, then SetState(0) runs before BindEntity, so it finds no entity and
        // does not SetActive
        var l = new Lift { Name = dataFileId.ToLowerInvariant(), Kind = kind };
        l.SetState(0);
        l.HasEntity = true;
        lifts[l.Name] = l;
        orderStale = true;
        return l;
    }

    public void Clear() { lifts.Clear(); orderStale = true; }

    /// <summary>LiftCubeAction's m_str18: the name of the lift the last poll found, "" for none.</summary>
    public string Polled { get; private set; } = "";

    /// <summary>
    /// LiftCubeAction::FUN_140957890 (@0x140957890, actionkeyobject.cpp:1318-1342): the first lift in name order that
    /// CanLift and lies within its range (the 3D distance from <paramref name="at"/>, Vector3::Unitize) of the player's
    /// point. The player's point is PCBase::FUN_1416767d0 (pcbase.cpp:3235): the ridee's position when riding.
    /// </summary>
    public Lift FindInReach(Vec3 at)
    {
        if (orderStale)
        {
            ordered.Clear();
            foreach (var l in lifts.Values) ordered.Add(l);
            ordered.Sort(ByName);
            orderStale = false;
        }
        for (int i = 0; i < ordered.Count; i++)
        {
            var l = ordered[i];
            if (!l.CanLift) continue;
            // the client's float Vector3 (m_vC4 minus the NiPoint3 position)
            float dx = (float)l.Position.X - (float)at.X, dy = (float)l.Position.Y - (float)at.Y, dz = (float)l.Position.Z - (float)at.Z;
            float dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            if (dist > l.Range) continue;
            return l;
        }
        return null;
    }

    static void SetHighlight(Lift l, bool on)
    {
        if (!l.HasEntity) return;                            // liftableobject.cpp:350-351: no entity, nothing
        l.Highlighted = on;
    }

    /// <summary>
    /// LiftCubeAction::vfunc_1 (@0x1409572c0, actionkeyobject.cpp:1279-1294), the action key's poll: the lift the last poll
    /// found loses its highlight when another (or none) is found now, the found lift gets it, and its name is kept. True
    /// when a lift is in reach. 0 B per poll.
    /// STILL-OPEN: the poll returns false while the pending ride item cast is set (MyPC::FUN_141523c00, mypc.cpp:1635,
    /// m_sp1608). MyPC::vfunc_121's ride off sets it (FUN_14152a310, mypc.cpp:3369) and the ride-on request clears it
    /// (:4595, :4606). No ride-off or ride-on send is ported, so the cast is never pending here; it must be ported together
    /// with MyPC::SendRequestRideMode1 (mypc.cpp:3566).
    /// </summary>
    public bool Poll(Vec3 at)
    {
        var old = Find(Polled);
        var pick = FindInReach(at);
        if (old != null && old != pick) SetHighlight(old, false);
        if (pick != null) SetHighlight(pick, true);
        Polled = pick != null ? pick.Name : "";
        return pick != null;
    }

    /// <summary>
    /// LiftCubeAction::vfunc_2 (@0x140957550, actionkeyobject.cpp:1297-1315), the action key on the polled lift: nothing
    /// when the polled name finds no lift; when riding (GetRidee) the client first sends the ride off
    /// (MyPC::SendRequestRideMode1(0), mypc.cpp:3566, <see cref="RideOff.Request"/>), <paramref name="rideOff"/> here, null
    /// when not riding, and goes on only when it returns true. Then the lift loses its highlight and Liftable (0x3C) is sent.
    /// </summary>
    public LiftableRequest Use(Func<bool> rideOff)
    {
        var l = Find(Polled);
        if (l == null) return null;
        if (rideOff != null && !rideOff()) return null;
        SetHighlight(l, false);
        return new LiftableRequest(l.Name);
    }

    /// <summary>The field load's lifts (worldservice_1.cpp:6771-6786): each MS2Liftable entity with a DataFileID becomes
    /// Init(entity, false, kind) (liftableobject.cpp:167-210) and SetAt replaces a lift of its name; then the held updates
    /// run. Returns the field's lift count.
    /// STILL-OPEN: the shadow flag (0xd89e9b12), the rotation, the quest conditions (UpdateConditions) and NotifyAddLiftable
    /// have no port.</summary>
    public int AttachField(IReadOnlyList<FieldEntity> entities)
    {
        int n = 0;
        for (int i = 0; entities != null && i < entities.Count; i++)
        {
            var e = entities[i];
            if (!e.HasMixin(Model)) continue;
            string id = e.Raw?.Id;
            if (string.IsNullOrEmpty(id)) continue;
            var l = AddFieldLift(id, (int)e.Number("ItemStackCount"));
            l.Entity = e;
            l.Range = (float)e.Number("ActionDistance", 150);
            l.Position = e.Position;
            l.ItemId = (int)(uint)e.Number("ItemID");
            n++;
        }
        Holding = false;
        for (int i = 0; i < held.Count; i++) Apply(held[i]);
        held.Clear();
        return n;
    }

    /// <summary>The field is left: its entities go, and with each its lift (worldservice_1.cpp:3526-3533). A lift without an
    /// entity stays.</summary>
    public void FieldLeft()
    {
        Holding = true;
        held.Clear();
        gone.Clear();
        foreach (var (name, l) in lifts)
            if (l.HasEntity) gone.Add(name);
        foreach (var name in gone) lifts.Remove(name);
        orderStale = true;
    }

    public void Apply(LiftableUpdate u)
    {
        if (u == null) return;
        if (Holding) { held.Add(u); return; }
        switch (u.Mode)
        {
            case LiftableUpdate.ModeList:
                listed.Clear();
                foreach (var e in u.Entries)
                {
                    listed.Add(e.Name);
                    if (!lifts.TryGetValue(e.Name, out var l)) continue;
                    l.QuestA = e.QuestA; l.QuestB = e.QuestB;
                    l.QuestC = e.QuestC; l.QuestD = e.QuestD;
                    l.Kind = e.Kind;
                    l.Enabled = e.Enabled;
                    l.Flag = e.Flag;
                    if (l.SetState(e.State)) ActiveSet?.Invoke(l.Name, l.Active);
                }
                // :11973-11991: the field lift entities whose name the list doesn't hold are removed
                gone.Clear();
                foreach (var (name, l) in lifts)
                    if (l.HasEntity && !listed.Contains(name)) gone.Add(name);
                foreach (var name in gone) Destroy(name);
                break;
            case LiftableUpdate.ModeUpdate:
            {
                var e = u.Entries[0];
                if (!lifts.TryGetValue(e.Name, out var l)) break;
                l.Kind = e.Kind;
                l.Enabled = e.Enabled;
                if (l.SetState(e.State)) ActiveSet?.Invoke(l.Name, l.Active);
                break;
            }
            case LiftableUpdate.ModeAdd:
            {
                var e = u.Entries[0];
                var l = new Lift
                {
                    Name = e.Name, Kind = e.Kind, QuestA = e.QuestA, QuestB = e.QuestB, QuestC = e.QuestC, QuestD = e.QuestD,
                    Flag = e.Flag,
                };
                l.SetState(0);
                lifts[e.Name] = l;
                orderStale = true;
                break;
            }
            case LiftableUpdate.ModeRemove:
                Destroy(u.Name);
                break;
        }
    }

    void Destroy(string name)
    {
        if (name == null || !lifts.TryGetValue(name, out var l) || !l.HasEntity) return;
        lifts.Remove(name);
        orderStale = true;
        Removed?.Invoke(name);
    }
}

/// <summary>What a RequestFieldEnter did to the field.</summary>
public enum FieldEnterResult
{
    /// <summary>A non-zero error: only a message box; the current field and its objects stay (scenestategame.cpp:2335).</summary>
    Refused,
    /// <summary>The map whose load is still running was entered again: the load goes on, nothing is torn down.</summary>
    LoadingAlready,
    /// <summary>The entry is taken and a new field load starts (WorldService::LoadMap, worldservice_1.cpp:3319).</summary>
    NewField,
}

/// <summary>
/// CSceneStateGame::OnRequestFieldEnter (@0x14170f930, scenestategame.cpp:2335) as far as the field's breakables and lifts
/// go: only a taken entry starts a new field, and only a new field tears down the old field's markers and entity lifts
/// (LoadMap; worldservice_1.cpp:3526-3533) and its target boxes (the field teardown FUN_141bbc580 empties m_540,
/// :10062), and holds the updates until the new field's entities are attached.
/// </summary>
public static class FieldEnter
{
    /// <summary>Takes <paramref name="f"/> into <paramref name="entry"/>; the map still loading again
    /// (<see cref="Ms2.Net.FieldEntry.LoadingAlready"/>) keeps the field.</summary>
    public static FieldEnterResult Take(Ms2.Net.FieldEntry entry, RequestFieldEnter f, BreakableObjects breakables, LiftableObjects lifts, LiftableTargetBoxes targets,
        FieldVibrates vibrates = null)
    {
        if (!entry.Enter(f)) return FieldEnterResult.Refused;
        if (entry.LoadingAlready) return FieldEnterResult.LoadingAlready;
        breakables.ClearMarkers();
        lifts.FieldLeft();
        targets.Clear();
        vibrates?.Clear();   // the field reset's m_420, m_468 and m_5e8 RemoveAll (worldservice_1.cpp:10066-10076)
        // A box shown over a same-server field change stays: the UI side of the field load (GFxSceneService::FUN_1422ba150
        // -> UISceneEntity::vfunc_53, worldservice_1.cpp:3392) closes nothing; see SceneChange. The drop-the-queue call
        // FUN_14151f460(1) runs on the scene changes only (SceneChange.StateChanged, SceneChange.GameSceneLeft).
        return FieldEnterResult.NewField;
    }
}

/// <summary>
/// The field's liftable target boxes (WorldService m_540, a CRBMultiMap keyed by the box id), the places a lifted cube can
/// be put down. The field load (worldservice_1.cpp:6788-6801) makes a CLiftableTargetBox for every entity of model
/// 0x319a1ee4 (net hash of "MS2LiftableTargetBox") and inserts it under its id when Init succeeds. Init
/// (CLiftableTarget::vfunc_1 @0x1414482a0, liftabletarget.cpp:84-96) needs the Point3 0xfd95ca52 ("Position"), the Point3
/// 0x68c7c29d ("Rotation") and the int 0x79ec600a ("liftableTarget"); a box missing any of them is dropped.
/// <para>GetLiftableTargetBoxes (@0x141bc5980, worldservice_1.cpp:889-896) returns every box in the map's order: ascending
/// id, and among equal ids the last inserted first (CRBTree::InsertImpl sends an equal key left, atlcoll.h).</para>
/// <para>The field reset (FUN_141bbc580, worldservice_1.cpp:10062) empties the map with the lifts.</para>
/// <para>Each box's marker (<see cref="RefreshMarkers"/>): CLiftableTarget::FUN_1414483e0 for every box, run by
/// WorldService::FUN_141bd3920 (worldservice_1.cpp:7117-7131) at the end of every craft start or stop
/// (Unk_PCCubeCraft::FUN_1410a8af0, cubecraft_2.cpp:416), any pc's.</para>
/// STILL-OPEN: the target guides (model 0x14e880f6, m_570) have no port.
/// </summary>
public sealed class LiftableTargetBoxes
{
    public sealed class Box
    {
        /// <summary>m_nId: property 0x79ec600a ("liftableTarget").</summary>
        public int Id { get; internal set; }
        /// <summary>m_vPos: property 0xfd95ca52 ("Position").</summary>
        public Vec3 Position { get; internal set; }
        /// <summary>m_vRot: property 0x68c7c29d ("Rotation").</summary>
        public Vec3 Rotation { get; internal set; }
        public FieldEntity Entity { get; internal set; }
        /// <summary>m_nFx != 0: the box's marker effect runs.</summary>
        public bool Marked { get; internal set; }
    }

    /// <summary>CLiftableTargetBox::vfunc_2 (liftabletarget.cpp:162-165): CommonResource +0x348,
    /// EFFECT_LIFTABLE_CUBE_MARKER, the same in the KR and the restored data.</summary>
    public const string MarkerEffect = "BG/Common/Eff_co_targetBox.xml";

    /// <summary>CreateEffect (true) or DestroyEffect (false) of a box (liftabletarget.cpp:137-160); the host plays the
    /// effect at <see cref="MarkerAt"/>.</summary>
    public Action<Box, bool> MarkerSet;

    /// <summary>
    /// FUN_1414483e0 (@0x1414483e0, liftabletarget.cpp:99-134) for every box: outside the cube mode no marker; else a
    /// box is marked when a quest in progress (GetQuestIdsFiltered(1, 0xb, -1)) that is not complete (IsComplete,
    /// QuestRules.IsComplete) has an item_move condition (0x9c) whose target names one id, matches the box id, and whose
    /// code matches the active craft's item. A box changes only on a change (CreateEffect returns when the effect runs,
    /// DestroyEffect when none does). <paramref name="quest"/> is the quest data; it is read only in the cube mode.
    /// </summary>
    public void RefreshMarkers(bool inCubeMode, int craftItemId, IEnumerable<QuestRecord> quests, Func<int, Ms2.Quest.QuestData> quest)
    {
        foreach (var b in boxes)
        {
            bool on = inCubeMode && QuestWants(b.Id, craftItemId, quests, quest);
            if (on == b.Marked) continue;
            b.Marked = on;
            MarkerSet?.Invoke(b, on);
        }
    }

    /// <summary>The quest match of FUN_1414483e0 (liftabletarget.cpp:108-131).</summary>
    public static bool QuestWants(int boxId, int craftItemId, IEnumerable<QuestRecord> quests, Func<int, Ms2.Quest.QuestData> quest)
    {
        if (quests == null || quest == null) return false;
        foreach (var r in quests)
        {
            if (r.State != 1) continue;
            var data = quest(r.Id);
            if (data == null || Ms2.Quest.QuestRules.IsComplete(data, r.State, r.Progress)) continue;
            foreach (var c in data.Conditions)
                if (c.Type == ItemMoveCondition && c.Target.IdCount == 1 && c.Target.Matches(boxId) && c.Code.Matches((uint)craftItemId))
                    return true;
        }
        return false;
    }

    /// <summary>The condition type item_move (Ms2.Quest.ConditionTypes).</summary>
    public const int ItemMoveCondition = 0x9c;

    /// <summary>CreateEffect's transform (liftabletarget.cpp:140-144): the box position raised by 1, FromEulerAnglesXYZ of
    /// the box rotation's degrees, scale 1.</summary>
    public static Xform MarkerAt(Box b) =>
        new(b.Position + new Vec3(0, 0, 1), DummyWorld.EulerXyz(b.Rotation.X, b.Rotation.Y, b.Rotation.Z), 1);

    public const string Model = "MS2LiftableTargetBox";
    public const string IdProperty = "liftableTarget";

    readonly List<Box> boxes = new();

    /// <summary>GetLiftableTargetBoxes: the boxes in the map's order.</summary>
    public IReadOnlyList<Box> All => boxes;
    public int Count => boxes.Count;

    /// <summary>The field load's boxes (worldservice_1.cpp:6788-6801). Returns the box count.</summary>
    public int AttachField(IReadOnlyList<FieldEntity> entities)
    {
        boxes.Clear();
        for (int i = 0; entities != null && i < entities.Count; i++)
        {
            var e = entities[i];
            if (!e.HasMixin(Model)) continue;
            // Init: each GetPropertyValue that fails returns 0 (liftabletarget.cpp:89-94)
            if (!e.Has("Position") || !e.Has("Rotation") || !e.Has(IdProperty)) continue;
            Insert(new Box { Id = (int)e.Number(IdProperty), Position = e.Position, Rotation = e.Vector("Rotation"), Entity = e });
        }
        return boxes.Count;
    }

    /// <summary>CRBMultiMap::Insert: before the first box whose id is not below the new one.</summary>
    internal void Insert(Box b)
    {
        int lo = 0, hi = boxes.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (boxes[mid].Id < b.Id) lo = mid + 1; else hi = mid;
        }
        boxes.Insert(lo, b);
    }

    /// <summary>The field reset empties the map (worldservice_1.cpp:10062).</summary>
    public void Clear() => boxes.Clear();
}
