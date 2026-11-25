using System.Numerics;
using Ms2.Formats.World;
using Ms2.Net.Packets;

namespace Ms2.Ui;

/// <summary>
/// CFunctionCubeService (functioncubeobject.cpp:2383-2720): the function cube objects of the field's placed cubes, by id
/// "objCode_cellKey", and what the FunctionCube packet (FUN_14122f270 @0x14122f270, :2411) does to them.
/// <para>A placed cube whose item's install object (objCode) has a &lt;FunctionCube&gt; is first a pending cube
/// (CreateCube, cubemapinformationservice_3.cpp:2647-2653: AddPendingCube @0x1412304e0 with the cell key, the object code
/// FUN_1408a0190 of the item's subtype, and the cube's kind). When its entity is made, FUN_1410d0000 (@0x1410d0000,
/// :3331-3358) makes the real object (vfunc_2 @0x141225610, functioncubeobject.cpp:507-585), names it and adds it
/// (AddCube @0x141230c90, :2660-2671), which takes over the pending twin's state (TransferPendingState @0x141230580,
/// :2638-2647). Our placed cubes have no entity (<see cref="CubeCellMap"/> adds them at once), so <see cref="Place"/>
/// runs both steps together, as if the entity was made right away. The cube leaves with its cell (FUN_1410c1910,
/// :5236-5241: RemoveCube @0x141230df0).</para>
/// <para>The states (vfunc_12 @0x141226a90, FUN_141226c20 @0x141226c20, vfunc_10 @0x141226730): 0, 1 and 2 are the cube's
/// still states; 3, 4 and 5 are the moves into 0, 1 and 2 (FUN_141239e40 @0x141239e40 maps a state record's state to its
/// move when the record's flag is set). A state plays its sequence (ZeroSeq ... SecondIngSeq) and its sound (ZeroEffect
/// ... SecondIngEffect, vfunc_11 @0x141226950); a move without a sequence goes straight to its still state
/// (FUN_14122edc0 @0x14122edc0), and a move's sequence ending does the same (EndOfSequence @0x141226d70).</para>
/// STILL-OPEN: the cube entities and their sequence playback (no placed cube has a model here; <see cref="Cube.Sequence"/>
/// and <see cref="Cube.Sound"/> say what would play), the camera modes of UpdateCameraEffect (@0x141227950), the collision
/// groups (FUN_141227cd0), the forbid flags' notices (FUN_141227dd0 @0x141227dd0), the home-made, nurturing and notice cube
/// subclasses' own fields beyond the nurturing record, and the furniture state 0x29 (StateUseFurnitureData).
/// </summary>
public sealed class FunctionCubes
{
    public sealed class Cube
    {
        /// <summary>m_strId: "objCode_cellKey" (SetId @0x141226b80, set by FUN_1410d0000 :3352).</summary>
        public string Id { get; internal set; }
        /// <summary>The object's function cube row (ObjectData m_cube), null for a pending cube.</summary>
        public ObjectData.FunctionCube Data { get; internal set; }
        /// <summary>m_spData->m_nKind (GetKind): the row's ControlType.</summary>
        public int Kind { get; internal set; }
        /// <summary>m_spData->m_nSubKind (GetSubKind): the row's UiFunction.</summary>
        public int SubKind => Data?.UiFunction ?? 0;
        /// <summary>m_nState (+0x38).</summary>
        public int State { get; internal set; }
        /// <summary>m_b21: a pending cube got a state record (FUN_1412271c0 @0x1412271c0); AddCube passes the state on only then.</summary>
        public bool StateSet { get; internal set; }
        /// <summary>m_spData->m_bBuff (FUN_14122e710): a Switch-kind (3) or PVP (9) cube changed by the action key
        /// (StateChangeAction 1; vfunc_2 :536-539).</summary>
        public bool ByActionKey { get; internal set; }
        /// <summary>m_spData->m_bLike (FUN_141236a60): a kind 3 cube changed by an attack (StateChangeAction 2, :540-541).</summary>
        public bool ByAttack { get; internal set; }
        /// <summary>m_spItem: the placed cube's item.</summary>
        public int ItemId { get; internal set; }
        /// <summary>m_nArg2: the cube's owner (GetOwnerId of the cube's plot, :3348).</summary>
        public long Owner { get; internal set; }
        /// <summary>The entity position (property 0xfd95ca52): the cube's cell times 150 (CreateCube's ptCell).</summary>
        public Vector3 Position { get; internal set; }
        /// <summary>The sequence the last state change played (vfunc_10's PlayAnimation), "" for none.</summary>
        public string Sequence { get; internal set; } = "";
        /// <summary>The sound the last state change started (vfunc_11), "" for none.</summary>
        public string Sound { get; internal set; } = "";
        /// <summary>m_spBooty: the object has a booty popup (firstAdditionalPopup; vfunc_2 :574-582).</summary>
        public bool HasBootyPopup => Data != null && Data.FirstEffect.Popup != 0;

        /// <summary>vfunc_15 (@0x141226f30): the action key uses the cube: an action key cube of kind 3 or 9.</summary>
        public bool Usable => ByActionKey && Kind is 3 or 9;
        /// <summary>FUN_14122e6d0 (@0x14122e6d0): state 2 or its move 5, the only states OpenSubKindDialog opens in.</summary>
        public bool InSecondState => State is 2 or 5;
        /// <summary>HasModel (@0x1412271f0): the row's first pc animation (row +0x110 / +0x118: firstPcAnimation's animation
        /// or its UsePcMove) is set: the furniture a pc takes into use.</summary>
        public bool HasModel => Data?.FirstAnimation is { } a && (a.Animation != "" || a.UsePcMove != 0);
        /// <summary>HasSkillUrl (@0x141227270): the row has a web link.</summary>
        public bool HasWebLink => Data != null && Data.WebUrl != "";
    }

    /// <summary>What a FunctionCube use reply (mode 6) asks the host to do.</summary>
    public enum UseAction
    {
        None,
        /// <summary>OpenSubKindDialog (@0x1412278b0): 1 trigger editor, 4 object store, 5 cube sub scene, 6 home password, 7 home summon.</summary>
        OpenDialog,
        /// <summary>vfunc_59(0x29, StateUseFurnitureData(id)): the own pc takes the furniture into use.</summary>
        UseFurniture,
        /// <summary>vfunc_59(1): the own pc, in state 0x29, leaves the furniture.</summary>
        LeaveFurniture,
        /// <summary>The cube's web link: open type 0 the system browser (FUN_1418fe220), 1 and 2 the in-game browser 0x4e6a.</summary>
        OpenWeb,
    }

    public readonly record struct UseResult(UseAction Action, Cube Cube, int Dialog = 0);

    readonly Dictionary<string, Cube> cubes = new(StringComparer.Ordinal);
    readonly Dictionary<string, Cube> pending = new(StringComparer.Ordinal);

    public int Count => cubes.Count;
    public int PendingCount => pending.Count;
    public IEnumerable<Cube> All => cubes.Values;

    /// <summary>A cube was added (AddCube) or removed (RemoveCube); the host follows it (the booty popups, the action key).</summary>
    public Action<Cube> Added, Removed;

    /// <summary>FUN_1408a0190 (@0x1408a0190, ugcmapdata_1.cpp:85-92): an item subtype's object code: 3 is 1, 4 is 2, 5 to 7
    /// are 4, else 0.</summary>
    public static int ObjCodeOf(int subtype) => subtype switch { 3 => 1, 4 => 2, 5 or 6 or 7 => 4, _ => 0 };

    /// <summary>The cube id: Format("%d_%d", objCode, cell key) (AddPendingCube :2627-2628, FUN_1410d0000 :3351).</summary>
    public static string Key(int objCode, int cellKey) => $"{objCode}_{cellKey}";

    public Cube Find(string id) => id != null && cubes.TryGetValue(id, out var c) ? c : null;
    public Cube FindPending(string id) => id != null && pending.TryGetValue(id, out var c) ? c : null;

    /// <summary>
    /// CreateCube's function cube part and FUN_1410d0000 for a placed cube at <paramref name="cellKey"/>: nothing unless the
    /// item's install object has a function cube; else the pending cube (AddPendingCube), then the object: vfunc_2 takes
    /// the row, plays the default state as at creation (vfunc_12(m_nState, true)), and AddCube passes on the pending
    /// cube's state when a record had set one (vfunc_14 → vfunc_12(state, false)). Null when no cube was made.
    /// </summary>
    public Cube Place(int cellKey, int itemSubtype, ObjectData obj, int itemId, long owner, Vector3 position)
    {
        if (obj == null || !obj.Cube.Enabled) return null;
        int objCode = ObjCodeOf(itemSubtype);
        string id = Key(objCode, cellKey);
        AddPending(id, obj.Cube.ControlType);
        var c = new Cube
        {
            Id = id, Data = obj.Cube, Kind = obj.Cube.ControlType, ItemId = itemId, Owner = owner, Position = position,
            ByActionKey = obj.Cube.StateChangeAction == 1 && obj.Cube.ControlType is 3 or 9,
            ByAttack = obj.Cube.ControlType == 3 && obj.Cube.StateChangeAction == 2,
        };
        SetState(c, obj.Cube.DefaultState, true);
        AddCube(c);
        return c;
    }

    /// <summary>AddPendingCube: a pending cube of the kind (CreateCubeObject) under the id; replaces one of that id.</summary>
    void AddPending(string id, int kind) => pending[id] = new Cube { Id = id, Kind = kind };

    /// <summary>AddCube (@0x141230c90): the pending twin's state when it had one (TransferPendingState), then the table.</summary>
    void AddCube(Cube c)
    {
        if (pending.Remove(c.Id, out var old) && old.StateSet) SetState(c, old.State, false);
        cubes[c.Id] = c;
        Added?.Invoke(c);
    }

    /// <summary>RemoveCube (@0x141230df0): the cube of the id leaves the table (with its entity). True when one was there.
    /// The pending cube of the id stays, as in the client.</summary>
    public bool Remove(int itemSubtype, int cellKey) => Remove(Key(ObjCodeOf(itemSubtype), cellKey));

    public bool Remove(string id)
    {
        if (!cubes.Remove(id, out var c)) return false;
        Removed?.Invoke(c);
        return true;
    }

    /// <summary>A new field: the client makes a new service per field (the pc's field objects go with it).</summary>
    public void Clear()
    {
        foreach (var c in cubes.Values.ToList()) Remove(c.Id);
        pending.Clear();
    }

    /// <summary>FUN_141239e40 (@0x141239e40): a flagged record's state is its move: 0 → 3, 1 → 4, 2 → 5.</summary>
    public static int MoveOf(int state) => state switch { 1 => 4, 0 => 3, 2 => 5, _ => state };

    /// <summary>FUN_14122edc0 (@0x14122edc0): a move's still state: 3 → 0, 4 → 1, 5 → 2.</summary>
    public static int StillOf(int state) => state switch { 4 => 1, 3 => 0, 5 => 2, _ => state };

    /// <summary>
    /// vfunc_12 (@0x141226a90): a state outside 0-5 is refused (assert); else m_nState, then FUN_141226c20: a still state
    /// plays as is, a move with <c>vfunc_10(state, true)</c>. vfunc_10: a state without a sequence goes on to its still state
    /// when it is a move (vfunc_12(still, false), and nothing more here); else its sequence plays when the row's +0x04
    /// (m_nCubeId, the object's UseKfm) is set (one of its list at random; the list holds one name); then its sound (vfunc_11),
    /// with or without a sequence.
    /// </summary>
    public static void SetState(Cube c, int state, bool init)
    {
        if (state < 0 || state >= 6) return;
        c.State = state;
        if (c.Data == null) return;                                  // m_ent == 0 (:679): a pending cube only keeps the state
        bool move = state >= 3;
        string seq = c.Data.Sequences[state];
        if (seq == "" && move)
        {
            SetState(c, StillOf(state), false);
            return;
        }
        c.Sequence = c.Data.UseKfm != 0 ? seq : "";
        c.Sound = c.Data.Effects[state];
    }

    /// <summary>EndOfSequence (@0x141226d70): a move's sequence ended; the cube goes to the move's still state.</summary>
    public static void EndOfSequence(Cube c)
    {
        if (c.State is 3 or 4 or 5) SetState(c, StillOf(c.State), false);
    }

    /// <summary>
    /// ReadCubeState (@0x141230310) applied: the cube of the id, else its pending twin, else nothing (the client reads no
    /// more of the record; <see cref="FunctionCubeReply"/> reads it whole). The flag turns the state into its move; a
    /// pending cube keeps it for AddCube (FUN_1412271c0), a cube plays it (vfunc_12(state, false)). Null when no cube.
    /// </summary>
    public Cube Apply(FunctionCubeState s)
    {
        if (s == null) return null;
        bool isPending = false;
        if (!cubes.TryGetValue(s.Id, out var c))
        {
            if (!pending.TryGetValue(s.Id, out c)) return null;
            isPending = true;
        }
        int state = s.Flag ? MoveOf(s.State) : s.State;
        if (isPending) { c.State = state; c.StateSet = true; }
        else SetState(c, state, false);
        return c;
    }

    /// <summary>The record reader's question: is the record of this id a nurturing cube's (CNurturingCubeObject::vfunc_1
    /// reads its pet fields)? The table's kind 0xd, the cube or its pending twin.</summary>
    public bool IsNurturing(int index, string id) => (Find(id) ?? FindPending(id))?.Kind == 0xd;

    /// <summary>
    /// FUN_14122f270 modes 2 and 3 (each record, ReadCubeState) and mode 6 (the use reply, :2426-2490): nothing without the
    /// own pc; else the cube of the id, kind 3 only: a cube with a sub-kind opens its dialog when the flag is set and the
    /// reply is the own pc's (OpenSubKindDialog, which opens only in state 2 or 5); else a furniture cube (HasModel), for the
    /// own pc only, is taken into use (flag set) or left (no flag, while the pc is in state 0x29); else a cube with a web link
    /// opens it, for anyone. <paramref name="inFurniture"/> is GetCurStateId() == 0x29.
    /// </summary>
    public UseResult Apply(FunctionCubeReply r, long ownCharacterId, bool hasOwnPc = true, bool inFurniture = false)
    {
        if (r == null) return default;
        switch (r.Kind)
        {
            case FunctionCubeKind.States:
            case FunctionCubeKind.State:
                foreach (var s in r.States) Apply(s);
                return default;
            case FunctionCubeKind.Use:
            {
                if (!hasOwnPc) return default;
                bool mine = ownCharacterId == r.Owner;
                var c = Find(r.CubeId);
                if (c == null || c.Kind != 3) return default;
                if (c.SubKind != 0)
                {
                    if (r.Flag && mine && c.InSecondState && c.SubKind is 1 or 4 or 5 or 6 or 7)
                        return new UseResult(UseAction.OpenDialog, c, c.SubKind);
                    return new UseResult(UseAction.None, c);
                }
                if (c.HasModel)
                {
                    if (!mine) return new UseResult(UseAction.None, c);
                    if (r.Flag) return new UseResult(UseAction.UseFurniture, c);
                    return inFurniture ? new UseResult(UseAction.LeaveFurniture, c) : new UseResult(UseAction.None, c);
                }
                if (c.HasWebLink) return new UseResult(UseAction.OpenWeb, c);
                return new UseResult(UseAction.None, c);
            }
        }
        return default;
    }

    /// <summary>
    /// FUN_141230f60 (@0x141230f60, functioncubeobject.cpp:2700-2720): the nearest usable cube (vfunc_15) within
    /// <paramref name="range"/> of <paramref name="point"/> (the 3D distance; a tie keeps the later one, `fDist &gt; fBest`).
    /// STILL-OPEN: the home-made cube owner skip (FUN_140e5cc00 / FUN_140e5cbf0) needs the home-made subclass.
    /// </summary>
    public Cube NearestUsable(Vector3 point, float range)
    {
        float best = float.MaxValue;
        Cube found = null;
        foreach (var c in cubes.Values)
        {
            if (!c.Usable) continue;
            float d = Vector3.Distance(point, c.Position);
            if (d > range || d > best) continue;
            found = c;
            best = d;
        }
        return found;
    }
}
