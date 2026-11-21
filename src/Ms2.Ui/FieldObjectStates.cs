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
/// STILL-OPEN: SetState's effects, animations, physics actor and shadow per state (breakableobject.cpp:270-343) have no
/// port; the host gets <see cref="StateSet"/> and <see cref="Marker.Shown"/>, which the game does not draw yet. The reader of the cube map's list (+0x1a0)
/// is not found.
/// </summary>
public sealed class BreakableObjects
{
    public sealed class Marker
    {
        public string Name { get; internal set; }
        public int State { get; internal set; }
        public int SyncA { get; internal set; }
        public int SyncB { get; internal set; }
        public int SyncC { get; internal set; }
        /// <summary>The field entity (m_spEntity); null for a marker added by name only.</summary>
        public FieldEntity Entity { get; internal set; }
        /// <summary>m_bHittable: property 0x2683e9ec ("Enabled"), false when the size is zero (FUN_140adf9d0,
        /// breakableobject.cpp:586-590).</summary>
        public bool Hittable { get; internal set; }
        /// <summary>m_ptSize: property 0x9dc22aea ("ShapeDimensions"). The hit box is (x, y * 0.5, z) at offset
        /// (0, y * 0.5, 0) from the position (:621-625).</summary>
        public Vec3 Size { get; internal set; }
        /// <summary>m_bC1: property 0x54adb2fd ("reactionArrow"); the tag pass sends only for such a marker
        /// (worldservice_1.cpp:9169-9392).</summary>
        public bool ReactionArrow { get; internal set; }
        /// <summary>The hit box's m_b54: property 0xa34897b1 ("SkillPauseControllFeedback") (:630).</summary>
        public bool SkillPauseFeedback { get; internal set; }
        /// <summary>m_ptPos / m_ptRot: properties 0xfd95ca52 ("Position") and 0x68c7c29d ("Rotation") (:617-618).</summary>
        public Vec3 Position { get; internal set; }
        public Vec3 Rotation { get; internal set; }
        /// <summary>Whether SetState last showed the model (FUN_140ae07d0(1): states 2, 3, 5 and 6) or hid it
        /// (FUN_140ae07d0(0), state 4: a 500 ms alpha fade from 1 to 0, breakableobject.cpp:755-776); other states leave it.
        /// A marker without an entity changes nothing (:277).</summary>
        public bool Shown { get; internal set; } = true;

        /// <summary>CBreakableObject::SetState (@0x140ade420, breakableobject.cpp:270-343), the visibility part.</summary>
        internal void SetState(int state)
        {
            State = state;
            if (Entity == null) return;
            if (state is 2 or 3 or 5 or 6) Shown = true;
            else if (state == 4) Shown = false;
        }
    }

    /// <summary>The model every field breakable has: net hash 0xd3b04f05 (worldservice_1.cpp:6760).</summary>
    public const string Model = "MS2Breakable";

    readonly Dictionary<string, Marker> markers = new(StringComparer.Ordinal);
    readonly Dictionary<string, int> cubeStates = new(StringComparer.Ordinal);

    /// <summary>A marker's state was set: the name and the state.</summary>
    public Action<string, int> StateSet;

    /// <summary>The cube map's states by name (CubeMapInformationService +0x1a0).</summary>
    public IReadOnlyDictionary<string, int> CubeStates => cubeStates;

    public Marker AddMarker(string name, int state = 0)
    {
        // a marker's name is lowercase (vfunc_1's ToLower), as FindMarkerByName looks it up
        var m = new Marker { Name = name.ToLowerInvariant(), State = state };
        markers[m.Name] = m;
        return m;
    }

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
        for (int i = 0; entities != null && i < entities.Count; i++)
        {
            var e = entities[i];
            if (!e.HasMixin(Model)) continue;
            string id = e.Raw?.Id;
            if (string.IsNullOrEmpty(id)) continue;
            // FUN_140adf9d0 (breakableobject.cpp:584-635)
            var size = e.Vector("ShapeDimensions");
            var m = new Marker
            {
                Name = id.ToLowerInvariant(), Entity = e, Size = size,
                Hittable = e.Bool("Enabled") && !(size.X == 0 && size.Y == 0 && size.Z == 0),
                ReactionArrow = e.Bool("reactionArrow"), SkillPauseFeedback = e.Bool("SkillPauseControllFeedback"),
                Position = e.Position, Rotation = e.Vector("Rotation"),
            };
            m.SetState(2);
            markers[m.Name] = m;
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
                m.SetState(e.State);
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
/// STILL-OPEN: the field's lifts (model 0x32767dd, kind prop 0x3773e9e0) are not attached: the model's name is not proven;
/// the host adds them with <see cref="AddFieldLift"/>.
/// STILL-OPEN: DestroyLiftByName on a lift without an entity (CMainSystem::DestroyEntity(0)) is not followed: such a lift is
/// left in the list.
/// STILL-OPEN: the quest masks (ParseQuestMask, liftableobject.cpp:105), UpdateConditions (:356), the collision groups and
/// the cube cell clear of a removed field lift (CubeMapInformationService::FUN_1410c4b50(cell, 0)) have no port; the host
/// gets <see cref="Removed"/>.
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
        /// <summary>SetState: states 0 and 2 SetActive(1), state 1 SetActive(0), others leave it
        /// (liftableobject.cpp:296-309; only with an entity).</summary>
        public bool Active { get; internal set; }
        /// <summary>The quest mask strings: FUN_1414477f0(A, B) and FUN_141447850(C, D).</summary>
        public string QuestA { get; internal set; }
        public string QuestB { get; internal set; }
        public string QuestC { get; internal set; }
        public string QuestD { get; internal set; }
        /// <summary>m_bC2 (FUN_141447aa0).</summary>
        public bool Flag { get; internal set; }
        /// <summary>A field lift has an entity; a mode 3 lift has none.</summary>
        public bool HasEntity { get; internal set; }

        internal void SetState(int state)
        {
            State = state;
            if (!HasEntity) return;                          // liftableobject.cpp:295
            if (state is 0 or 2) Active = true;
            else if (state == 1) Active = false;
        }
    }

    readonly Dictionary<string, Lift> lifts = new(StringComparer.Ordinal);
    readonly HashSet<string> listed = new(StringComparer.Ordinal);
    readonly List<string> gone = new();

    /// <summary>A field lift's entity was removed: its name.</summary>
    public Action<string> Removed;

    public int Count => lifts.Count;
    public Lift Find(string name) => name != null && lifts.TryGetValue(name, out var l) ? l : null;

    /// <summary>A field lift: Init(entity, false, kind) with the entity's lowercased DataFileID as its name.</summary>
    public Lift AddFieldLift(string dataFileId, int kind)
    {
        // Init (liftableobject.cpp:167-175): SetState(0) runs before BindEntity, so it finds no entity and leaves Active
        var l = new Lift { Name = dataFileId.ToLowerInvariant(), Kind = kind };
        l.SetState(0);
        l.HasEntity = true;
        lifts[l.Name] = l;
        return l;
    }

    public void Clear() => lifts.Clear();

    /// <summary>The field is left: its entities go, and with each its lift (worldservice_1.cpp:3526-3533). A lift without an
    /// entity stays.</summary>
    public void FieldLeft()
    {
        gone.Clear();
        foreach (var (name, l) in lifts)
            if (l.HasEntity) gone.Add(name);
        foreach (var name in gone) lifts.Remove(name);
    }

    public void Apply(LiftableUpdate u)
    {
        if (u == null) return;
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
                    l.SetState(e.State);
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
                l.SetState(e.State);
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
        Removed?.Invoke(name);
    }
}
