using System;
using System.Collections.Generic;
using System.Numerics;
using Ms2.Formats.Model;
using Ms2.Formats.World;
using Ms2.Net.Packets;

namespace Ms2.Ui;

/// <summary>
/// The field's vibrate objects (MS2Vibrate: a door, a crate, a tree that a skill shakes or breaks) and their running vibrators.
/// <para>The registry is WorldService m_5e8 (worldservice_1.cpp:6819-6866, the field load): each entity of model 0x2a95a20
/// (net hash of "MS2Vibrate") whose Enabled (0x2683e9ec) reads true and whose ShapeDimensions (0x9dc22aea) is not zero,
/// keyed by its DataFileID lowercased (SetAt: a later entity of the same key replaces the earlier). Each also gets its tag
/// box in m_420: the half size (x, y * 0.5, z) at (0, y * 0.5, 0) from the position, turned by the rotation, its notify flag
/// SkillPauseControllFeedback (0xa34897b1). The NameTag alias map (m_618, 0x64ea534e) is another lookup that nothing here
/// reads.</para>
/// <para>The vibrators are WorldService m_468 by entity: <see cref="Vibrator"/> (CEntityVibrator). A Vibrate packet
/// (<see cref="Apply"/>, OnVibrate scenestategame.cpp:5981) starts one, HideVibrate (OnHideVibrate, :6018) drops the object
/// and ShowVibrate (OnShowVibrate, :6026) lets it come back; the world tick (FUN_141bc3560, worldservice_1.cpp:9243-9250)
/// updates each and drops the finished ones (<see cref="Update"/>).</para>
/// <para>The host does what the client does to the entity: <see cref="CollisionSet"/> (CPhysXHelper::SetEntityCollisionGroup:
/// group 5, which the player does not meet, or group 0; the host skips an entity without PhysX props, as HasPhysicsProps does),
/// <see cref="Faded"/> (WorldService::FadeEntity), <see cref="VisibleSet"/> (IsVisible, 0xa0b6b179), <see cref="Moved"/> (the
/// Position property, as an offset from the rest position) and <see cref="EffectPlayed"/> (CEffectService::InsertEffect, type 7,
/// at the entity).</para>
/// STILL-OPEN: the PhysX debris actor a break makes (MakePhysxActor, entityvibrator.cpp:333: the object flies from the hit
/// with the attack's brokenForce and the entity's brokenForceScale, then sleeps or is released after 3 s); until it is ported a
/// broken object stands where it was, without its collision, until its fade. The cube height index cell a break clears and the
/// return sets (FUN_1410b25a0 / FUN_1410b2510), the GeneratePhysX box the return makes again (FUN_1410c5570), the Transparency
/// flag (0x9004569a) a break clears, the motion's material effect (the motion's zone map keyed by materialTag 0x82e054a5), the
/// model swap of the cast's model name (FUN_1411cddc0 and FUN_141bc9560: NifAsset 0x770e5fc6) and the object weapon's
/// respawn tick the tag pass checks (FindCubeByEntityRef's minTick).
/// </summary>
public sealed class FieldVibrates
{
    /// <summary>The model every vibrate object has: net hash 0x2a95a20 (worldservice_1.cpp:6822).</summary>
    public const string Model = "MS2Vibrate";

    /// <summary>One registered object: the m_5e8 key and entity, and its m_420 tag box.</summary>
    public sealed class Entry
    {
        public string Name { get; internal set; }
        public FieldEntity Entity { get; internal set; }
        /// <summary>The entity's id (GetEntityIDValue): a tag area keeps the ids it entered.</summary>
        public ulong EntityId { get; } = Hud.Tombstone.NextEntityId();
        public Vec3 Size { get; internal set; }
        public Vec3 Position { get; internal set; }
        public Vec3 Rotation { get; internal set; }
        /// <summary>The box's notify flag: SkillPauseControllFeedback (worldservice_1.cpp:6863).</summary>
        public bool SkillPauseFeedback { get; internal set; }
        /// <summary>SoundMaterial (0x8843d885), the hit sound the tag pass plays.</summary>
        public int HitSound { get; internal set; }
        /// <summary>The offset of the entity's Position from its rest position (the vibrator's SetPosition).</summary>
        public Vector3 Offset { get; internal set; }

        Skills.HitBox? box;

        /// <summary>The m_420 box (worldservice_1.cpp:6855-6862): the field load makes it once and the tag pass reads it, so it
        /// is posed on the first read and kept.</summary>
        public Skills.HitBox Box() => box ??= Skills.HitBox.Pose(
            new((float)Size.X, (float)(Size.Y * 0.5), (float)Size.Z), new(0, (float)(Size.Y * 0.5), 0),
            new((float)Position.X, (float)Position.Y, (float)Position.Z), new((float)Rotation.X, (float)Rotation.Y, (float)Rotation.Z));
    }

    /// <summary>What a Vibrate, HideVibrate or ShowVibrate did.</summary>
    public enum Outcome { NotFound, NoSkill, StillBroken, Broke, Shook, Visibility, Hidden, Shown, NotRunning, Held }

    /// <summary>
    /// CEntityVibrator (entityvibrator.cpp): one object's shake, or its break and return.
    /// FUN_1411cd570 (@0x1411cd570, :500) reads the entity: frequency (0x4e98cfb7), amp (0xd993099), damping (0xbd4a4f13, kept
    /// in 0..1), dampingstarttime (0x68bb3f0f) and duration (0x597dcc01) in seconds, ShapeDimensions, DisableCollision
    /// (0x7e5f4195), resetTimer (0x4764b338, the time hidden) and brokenTick (0x292206c1, the time broken before the hide), both
    /// in ms, and Transparency (0x9004569a); the start is the server tick and the phase a random turn.
    /// </summary>
    public sealed class Vibrator
    {
        readonly FieldVibrates owner;
        public Entry Entry { get; }
        uint start, end, dampStart;        // m_2c, m_34, m_38
        readonly float frequency, amp, damping, phase;
        Vector3 dir;                       // m_3c
        bool negative = true;              // m_60
        readonly float sizeZ;              // m_98.z
        readonly bool disableCollision;    // m_a8
        readonly bool transparency;        // m_ab
        readonly int resetTimer;           // m_94
        int brokenTick;                    // m_90
        bool returned, hiddenDone;         // m_80, m_88
        uint dropStart;                    // m_8c
        bool held, showAgain;              // m_a9, m_aa

        /// <summary>m_7c (FUN_1411cd550): the object is broken.</summary>
        public bool Broken { get; private set; }
        /// <summary>m_d0: the cast's visibility mode (1 show, 2 hide).</summary>
        public int Mode { get; private set; }

        internal Vibrator(FieldVibrates owner, Entry entry, uint now)
        {
            this.owner = owner;
            Entry = entry;
            var e = entry.Entity;
            start = now;
            frequency = (float)e.Number("frequency");
            amp = (float)e.Number("amp");
            damping = Math.Clamp((float)e.Number("damping"), 0f, 1f);
            float dampingStart = (float)e.Number("dampingstarttime");
            float duration = (float)e.Number("duration");
            end = (uint)(int)Ms(duration) + start;
            dampStart = (uint)(int)Ms(dampingStart) + start;
            phase = owner.Random01() * 6.2831855f;
            sizeZ = (float)entry.Size.Z;
            disableCollision = e.Bool("DisableCollision");
            resetTimer = (int)e.Number("resetTimer");
            brokenTick = (int)e.Number("brokenTick");
            transparency = e.Bool("Transparency");
        }

        /// <summary>FUN_14028d260: seconds to milliseconds, truncated.</summary>
        static long Ms(float seconds) => (long)(seconds * 1000.0f);

        /// <summary>
        /// The cast ctor (@0x1411cb390, entityvibrator.cpp:153-224) after FUN_1411cd570: the start is the cast's tick (the
        /// record's m_nTick, the packet's serial). The object breaks when brokenTick is above 0 and the attack's brokenOffence is
        /// above the entity's brokenDefence (0xf5d61d2b); otherwise it shakes from now. A break runs MakePhysxActor: the entity's
        /// collision goes to group 5 (unless DisableCollision). Then the entity's XmlFilePath (0xdd2ac78a) effect plays at it, and
        /// the cast's model and visibility mode apply (FUN_1411cddc0, :605-630).
        /// </summary>
        internal void Cast(uint serial, int brokenOffence, int mode, uint now)
        {
            start = serial;
            int limit = (int)Entry.Entity.Number("brokenDefence");
            if (brokenTick > 0 && brokenOffence > limit) Broken = true;
            else start = now;
            if (Broken && !disableCollision) owner.CollisionSet?.Invoke(Entry, false);   // MakePhysxActor, :338-342
            owner.EffectPlayed?.Invoke(Entry, Entry.Entity.Get("XmlFilePath") ?? "");
            Mode = mode;
            if (mode == 1) owner.VisibleSet?.Invoke(Entry, true);
            if (mode == 2) owner.VisibleSet?.Invoke(Entry, false);
        }

        /// <summary>StartFall (@0x1411cdba0, :584): HideVibrate's drop. The collision goes to group 5, the entity fades out in
        /// 0.1 s, and the object counts as broken and held (m_a9) until a ShowVibrate.</summary>
        internal void StartFall()
        {
            if (!disableCollision) owner.CollisionSet?.Invoke(Entry, false);
            owner.Faded?.Invoke(Entry, false, 0.1f);
            Broken = true;
            held = true;
        }

        /// <summary>FUN_1411cdd90 (@0x1411cdd90, :601): ShowVibrate, the hold ends and the return shows the entity.</summary>
        internal void Release()
        {
            held = false;
            showAgain = true;
        }

        /// <summary>RestoreRestPose (@0x1411cd3b0, :474): the rest position again; a broken object gets its collision back
        /// (group 0).</summary>
        internal void RestoreRestPose()
        {
            SetOffset(Vector3.Zero);
            if (Broken && !disableCollision) owner.CollisionSet?.Invoke(Entry, true);
        }

        void SetOffset(Vector3 off)
        {
            if (Entry.Offset == off) return;
            Entry.Offset = off;
            owner.Moved?.Invoke(Entry);
        }

        /// <summary>
        /// Update (@0x1411cba60, entityvibrator.cpp:231-326); false when it is done (the world tick drops it).
        /// Broken: brokenTick is at least 3000 ms. After the start + brokenTick the entity fades out in 0.1 s when resetTimer is
        /// set; after start + brokenTick + resetTimer (not while held) it fades back in at once (when resetTimer is set or a
        /// ShowVibrate asked) and drops into place from three times its height over 500 ms, then its collision is back (group 0).
        /// Shaking: up to the end (start + duration) the entity moves along a random flat direction by
        /// sin(t * frequency * 2 pi + phase) * scale, t the part of the duration gone, scale = (fade * (1 - damping) + damping) *
        /// amp, the fade 1 - (now - dampStart) / (end - dampStart) in whole numbers (so 1 until the end); a new direction is drawn
        /// each time the offset changes sign. At the end it is back at rest.
        /// </summary>
        internal bool Update(uint now)
        {
            if (Broken)
            {
                if (brokenTick < 3000) brokenTick = 3000;
                if (!hiddenDone)
                {
                    hiddenDone = now > (uint)(start + brokenTick);
                    if (hiddenDone && resetTimer > 0) owner.Faded?.Invoke(Entry, false, 0.1f);
                }
                if (!returned)
                {
                    returned = now > (uint)(start + brokenTick + resetTimer);
                    if (held) returned = false;
                    if (returned)
                    {
                        if (resetTimer > 0 || showAgain)
                        {
                            owner.Faded?.Invoke(Entry, true, 0f);
                            showAgain = false;
                        }
                        dropStart = now;
                    }
                }
                if (returned)
                {
                    if (dropStart > now) dropStart = now;
                    float drop = sizeZ * 3.0f;
                    float k = Math.Min(1.0f, (float)(now - dropStart) / 500);
                    SetOffset(new Vector3(0, 0, -drop + drop * k));
                    if (1.0f <= k)
                    {
                        SetOffset(Vector3.Zero);
                        if (!disableCollision) owner.CollisionSet?.Invoke(Entry, true);
                        return false;
                    }
                }
                return true;
            }
            if (now < start) return true;
            if (now < end)
            {
                float fade = 1.0f;
                if (now >= dampStart) fade = 1.0f - (float)((now - dampStart) / (end - dampStart));
                float scale = (fade * (1.0f - damping) + damping) * amp;
                float t = (float)(now - start) / (float)(end - start);
                float off = MathF.Sin(t * frequency * 2.0f * 3.1415927f + phase) * scale;
                if (negative != (off < 0.0f))
                {
                    float a = (owner.RandomBit() - 0.5f) * 2.0f;
                    float b = (owner.RandomBit() - 0.5f) * 2.0f;
                    dir = new Vector3(owner.Random01() * a, owner.Random01() * b, 0);
                    float len = dir.Length();
                    if (len > 0) dir /= len;
                    negative = off < 0.0f;
                }
                SetOffset(dir * off);
                return true;
            }
            SetOffset(Vector3.Zero);
            return false;
        }
    }

    readonly SortedDictionary<string, Entry> byName = new(StringComparer.Ordinal);
    readonly Dictionary<Entry, Vibrator> running = new();
    readonly List<Entry> done = new();
    readonly List<Entry> sorted = new();
    bool sortedDirty;

    public Action<Entry, bool> CollisionSet;
    public Action<Entry, bool, float> Faded;
    public Action<Entry, bool> VisibleSet;
    public Action<Entry> Moved;
    public Action<Entry, string> EffectPlayed;

    /// <summary>FUN_140768090: a random float in [0, 1). Tests set it.</summary>
    public Func<float> Random01 = () => Random.Shared.NextSingle();
    /// <summary>GetRandomRange(2, 0): 0 or 1. Tests set it.</summary>
    public Func<int> RandomBit = () => Random.Shared.Next(2);

    public int Count => byName.Count;
    public int Running => running.Count;

    /// <summary>The m_5e8 entry of the key (the map's Lookup: the name as sent, against the lowercase keys).</summary>
    public Entry Find(string name) => name != null && byName.TryGetValue(name, out var e) ? e : null;

    public Vibrator VibratorOf(Entry e) => e != null && running.TryGetValue(e, out var v) ? v : null;

    /// <summary>The entries in key order, as the tag pass walks m_5e8 (a CRBMap keyed by the lowercase name: ordinal order).</summary>
    public IReadOnlyList<Entry> Sorted
    {
        get
        {
            if (sortedDirty)
            {
                sorted.Clear();
                sorted.AddRange(byName.Values);
                sortedDirty = false;
            }
            return sorted;
        }
    }

    /// <summary>The registry's test of an entity (worldservice_1.cpp:6826-6829): the model, Enabled read true and a size
    /// other than zero.</summary>
    public static bool Registers(FieldEntity e) =>
        e.HasMixin(Model) && e.Get("Enabled") != null && e.Bool("Enabled") && e.Get("ShapeDimensions") != null
        && e.Vector("ShapeDimensions") is var s && !(s.X == 0 && s.Y == 0 && s.Z == 0);

    /// <summary>The field load's loop (worldservice_1.cpp:6819-6866). Returns the entries; the packets held since
    /// <see cref="Clear"/> are applied after.</summary>
    public int AttachField(IReadOnlyList<FieldEntity> entities, uint now)
    {
        for (int i = 0; entities != null && i < entities.Count; i++)
        {
            var e = entities[i];
            if (!Registers(e)) continue;
            string id = e.Raw?.Id;
            if (id == null) continue;
            var entry = new Entry
            {
                Name = id.ToLowerInvariant(), Entity = e, Size = e.Vector("ShapeDimensions"), Position = e.Position,
                Rotation = e.Vector("Rotation"), SkillPauseFeedback = e.Bool("SkillPauseControllFeedback"),
                HitSound = (int)e.Number("SoundMaterial"),
            };
            byName[entry.Name] = entry;
        }
        sortedDirty = true;
        Holding = false;
        foreach (var h in held)
        {
            if (h.Packet != null) Apply(h.Packet, h.Offence, now);
            else if (h.Hide) Hide(h.Name, now);
            else Show(h.Name);
        }
        held.Clear();
        return byName.Count;
    }

    readonly List<(Vibrate Packet, int? Offence, string Name, bool Hide)> held = new();

    /// <summary>Whether packets wait for <see cref="AttachField"/>: from a field enter until its entities are attached.</summary>
    public bool Holding { get; private set; }

    /// <summary>The field goes (worldservice_1.cpp:10066-10076: m_420, m_468 and m_5e8 RemoveAll). The client loads the new
    /// field before it reads the next packet; here the field loads later, so the packets that come first are held until
    /// <see cref="AttachField"/> and applied in order.</summary>
    public void Clear()
    {
        byName.Clear();
        running.Clear();
        sortedDirty = true;
        held.Clear();
        Holding = true;
    }

    /// <summary>
    /// OnVibrate (@0x14172a1c0, scenestategame.cpp:5981-6012). Modes 0 and 1: the cast record is built from the skill and
    /// level (CSkillRecord::Init, which fails without them: <paramref name="brokenOffence"/> is null then, and nothing
    /// happens), its attack the packet's motion and attack index (GetBrokenOffence, skillrecord.cpp:382); then
    /// WorldService::FUN_141bc9020 (worldservice_1.cpp:5048): the named object, unless its vibrator is broken, is put back at
    /// rest and gets a new vibrator. Mode 2: FUN_141bc9560 (:5100), the object's IsVisible set by the flag (1 show, 2 hide).
    /// </summary>
    public Outcome Apply(Vibrate v, int? brokenOffence, uint now)
    {
        if (v == null) return Outcome.NotFound;
        if (Holding) { held.Add((v, brokenOffence, null, false)); return Outcome.Held; }
        if (v.Mode is 0 or 1)
        {
            if (brokenOffence is not int offence) return Outcome.NoSkill;
            var entry = Find(v.Entity);
            if (entry == null) return Outcome.NotFound;
            if (running.TryGetValue(entry, out var cur))
            {
                if (cur.Broken) return Outcome.StillBroken;
                cur.RestoreRestPose();
            }
            var vib = new Vibrator(this, entry, now);
            vib.Cast(unchecked((uint)v.Serial), offence, v.Flag, now);
            running[entry] = vib;
            return vib.Broken ? Outcome.Broke : Outcome.Shook;
        }
        if (v.Mode == 2)
        {
            var entry = Find(v.Entity);
            if (entry == null) return Outcome.NotFound;
            if (v.Flag == 1) VisibleSet?.Invoke(entry, true);
            if (v.Flag == 2) VisibleSet?.Invoke(entry, false);
            return Outcome.Visibility;
        }
        return Outcome.NotFound;
    }

    /// <summary>OnHideVibrate (@0x14172a7b0) -> FUN_141bc92e0 (worldservice_1.cpp:5067): the running vibrator back at rest, a
    /// new one (FUN_1411cd570 alone) and its StartFall.</summary>
    public Outcome Hide(string name, uint now)
    {
        if (Holding) { held.Add((null, null, name, true)); return Outcome.Held; }
        var entry = Find(name);
        if (entry == null) return Outcome.NotFound;
        if (running.TryGetValue(entry, out var cur)) cur.RestoreRestPose();
        var vib = new Vibrator(this, entry, now);
        vib.StartFall();
        running[entry] = vib;
        return Outcome.Hidden;
    }

    /// <summary>OnShowVibrate (@0x14172a850) -> FUN_141bc9480 (worldservice_1.cpp:5083): the running vibrator's hold ends.</summary>
    public Outcome Show(string name)
    {
        if (Holding) { held.Add((null, null, name, false)); return Outcome.Held; }
        var entry = Find(name);
        if (entry == null) return Outcome.NotFound;
        if (!running.TryGetValue(entry, out var cur)) return Outcome.NotRunning;
        cur.Release();
        return Outcome.Shown;
    }

    /// <summary>The world tick's m_468 loop (worldservice_1.cpp:9243-9250): each vibrator's Update, the finished ones dropped.
    /// Nothing runs with no vibrator.</summary>
    public void Update(uint now)
    {
        if (running.Count == 0) return;
        foreach (var (e, v) in running)
            if (!v.Update(now)) done.Add(e);
        foreach (var e in done) running.Remove(e);
        done.Clear();
    }
}
