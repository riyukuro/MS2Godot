using System.Numerics;
using Ms2.Formats.World;
using Ms2.Net.Packets;

namespace Ms2.Ui;

/// <summary>
/// The game rule's reactor context (CGameRule m_be0, SetReactor @0x14047d1c0 / GetReactor @0x14047d230,
/// commonfiles/gamerule.cpp:2266-2278) as GameEventManagerCli (gameeventmanagercli.cpp) fills it from GameEventReactor:
/// OnStart sets the event's context, vfunc_6 sets null (gameevent.cpp:5480/5484) whichever reactor event held it.
/// <para>The manager keys its events by id across every type; this holder only sees the Reactor type (as
/// <see cref="ConstructionEvents"/> does its two).</para>
/// </summary>
public sealed class ReactorRule
{
    readonly Dictionary<int, ReactorEvent> events = new();

    /// <summary>m_be0: the running reactor event's context, or null.</summary>
    public ReactorEvent Current { get; private set; }

    /// <summary>GameEventManagerCli::Dispatch (:77-95) for one GameEvent message: kinds 0 and 1 add an id not held yet and
    /// start it, 2 stops and removes, 3 stops the previous event of the id, then sets and starts the new one.</summary>
    public void Apply(GameEventMessage m)
    {
        switch (m.Kind)
        {
            case 0:
            case 1:
                foreach (var e in m.ReactorEvents)
                {
                    if (events.ContainsKey(e.Id)) continue;
                    Current = e;
                    events[e.Id] = e;
                }
                break;
            case 2:
                foreach (int id in m.RemovedIds)
                {
                    if (!events.Remove(id)) continue;
                    Current = null;
                }
                break;
            case 3:
                foreach (var e in m.ReactorEvents)
                {
                    if (events.ContainsKey(e.Id)) Current = null;
                    events[e.Id] = e;
                    Current = e;
                }
                break;
        }
    }

    /// <summary>GameEventManagerCli::Clear (:98-107), when the game scene is left: every event's vfunc_6, then RemoveAll.</summary>
    public void Clear()
    {
        events.Clear();
        Current = null;
    }
}

/// <summary>What the own player brings to a reactor's checks (CReactor::CanUse @0x140b79c30, reactor.cpp).</summary>
public interface IReactorUser
{
    /// <summary>FUN_140b7a5e0 (@0x140b7a5e0) is 0: the player's state is 1 (idle), FUN_141675ed0 is 0 and the sub scene is
    /// 0x620001d (the field).</summary>
    bool CanReact { get; }
    /// <summary>PCBase::FUN_141656a80: the character id the reactor's player set holds.</summary>
    long CharacterId { get; }
    /// <summary>g_143ab2a90.FUN_140a5f180(code, rank): the held count of the item at that rank.</summary>
    int ItemCount(int code, int rank);
    /// <summary>GetItemRank(code): the item's own rank, for a condition of rank 0.</summary>
    int ItemRank(int code);
}

/// <summary>
/// The field's reactors: CReactor (client/ms2/reactor.cpp) and WorldService's reactor map m_7c8 (a CRBMap keyed by name,
/// worldservice_1.cpp:3163 / 3228 / 10083), with ReactorAction's checks (actionkeyobject.cpp:2860-2990).
/// </summary>
public sealed class FieldReactors
{
    /// <summary>One CReactor.</summary>
    public sealed class Reactor
    {
        public int ObjectId;
        public string Name = "";
        public ReactorTable.Reactor Data;
        public Vector3 Position, Rotation;
        /// <summary>m_nStateIdx, m_n150, m_setPlayers, m_n158.</summary>
        public int StateIndex;
        public long Value150;
        public uint Value158;
        public readonly List<long> Players = new();
        /// <summary>m_listAnims: the animations to play, the head first.</summary>
        public readonly List<string> Animations = new();

        /// <summary>GetCurrentStateData: the state at the index, or null.</summary>
        public ReactorTable.State State => Data?.StateAt(StateIndex);

        public bool HasPlayer(long id) => Players.Contains(id);

        /// <summary>AddAnimation @0x140b78ea0: an empty name is skipped.</summary>
        public void AddAnimation(string ani)
        {
            if (!string.IsNullOrEmpty(ani)) Animations.Add(ani);
        }

        /// <summary>FUN_140b791a0 @0x140b791a0: the head of the queue is played each tick (vfunc_53), or none.</summary>
        public string Head => Animations.Count > 0 ? Animations[0] : null;

        /// <summary>EndOfSequence @0x140b7a1b0: the head is popped once the playing animation is the head.</summary>
        public bool EndOfSequence(string current)
        {
            if (Animations.Count == 0 || Animations[0] != current) return false;
            Animations.RemoveAt(0);
            return true;
        }

        void SetState(ReactorState s)
        {
            StateIndex = s.StateIndex;
            Value150 = s.Value150;
            Players.Clear();
            Players.AddRange(s.Players);
            Value158 = s.Value158;
        }

        /// <summary>FUN_140b79490 @0x140b79490's fields, then the queue holds the state's idle animation.</summary>
        internal void Init(ReactorAdd a, ReactorTable.Reactor data)
        {
            ObjectId = a.ObjectId;
            Name = a.Name ?? "";
            Data = data;
            Position = a.Position;
            Rotation = a.Rotation;
            SetState(a.State);
            Animations.Clear();
            AddAnimation(State.IdleAni);
        }

        /// <summary>FUN_140b79a10 @0x140b79a10: the fields; with a state at the new index, the queue holds its start then idle
        /// animation (else the queue is left).</summary>
        public void Update(ReactorState s)
        {
            SetState(s);
            var st = State;
            if (st == null) return;
            Animations.Clear();
            AddAnimation(st.StartAni);
            AddAnimation(st.IdleAni);
        }

        /// <summary>FUN_140b79cc0 @0x140b79cc0 is 0: by the state's type, "connect" (1) refuses a player in the set, "reward"
        /// (3) one not in it, "sleep" (2) everyone; no state refuses too.</summary>
        public bool TypeAllows(long characterId)
        {
            var st = State;
            if (st == null) return false;
            return st.Type switch
            {
                1 => !HasPlayer(characterId),
                3 => HasPlayer(characterId),
                2 => false,
                _ => true,
            };
        }

        /// <summary>FUN_140b79ee0 @0x140b79ee0 is 0: each item condition needs max(checkCount, consume) of the item at its rank
        /// (0 = the item's own rank); a condition of code 0 or need 0 is skipped.</summary>
        public bool HasItems(IReactorUser user)
        {
            var st = State;
            if (st == null) return false;
            foreach (var c in st.Conditions)
            {
                int need = Math.Max(c.CheckCount, c.Consume);
                if (c.Code == 0 || need == 0) continue;
                int rank = c.Rank == 0 ? user.ItemRank(c.Code) : c.Rank;
                if (user.ItemCount(c.Code, rank) < need) return false;
            }
            return true;
        }

        /// <summary>CanUse @0x140b79c30.</summary>
        public bool CanUse(IReactorUser user) =>
            user != null && user.CanReact && TypeAllows(user.CharacterId) && HasItems(user);

        /// <summary>UpdateConnectedEffect @0x140b79330: the data's connected effect is on while the own character is in the
        /// player set (AddEffect at pos + (0, 0, 1), the rotation in degrees), else off.</summary>
        public bool ConnectedEffectOn(long characterId) => HasPlayer(characterId);
    }

    readonly ReactorTable table;
    readonly Dictionary<int, Reactor> byId = new();
    // m_7c8: by name, ordinal (CStringT::Compare); a List kept sorted so the poll walks it without an enumerator allocation
    readonly List<Reactor> byName = new();

    public FieldReactors(ReactorTable table) => this.table = table;

    public int Count => byId.Count;
    public Reactor Get(int objectId) => byId.GetValueOrDefault(objectId);
    /// <summary>The reactors in the name map's order.</summary>
    public IReadOnlyList<Reactor> ByName => byName;

    /// <summary>
    /// OnReactor mode 0 (scenestategame.cpp:6709-6721): FUN_140b79490 fails (no add) for an unknown reactor id or a state
    /// index past its list; else OnObjectCreated (worldservice_1.cpp:3148-3163) SetAts the object id and the name. A
    /// reactor of the same name stays an object (found by id, updated, removed) but leaves the name map, so the poll no
    /// longer sees it. An object of the same id is returned in <paramref name="replaced"/>: the client leaves its entity
    /// in the field unreachable by id; here it is dropped from the name map too (port choice: its model goes with it).
    /// </summary>
    public Reactor Add(ReactorAdd a, out Reactor replaced)
    {
        replaced = null;
        var data = table?.Get(a.ReactorId);
        if (data == null || data.StateAt(a.State.StateIndex) == null) return null;
        var r = new Reactor();
        r.Init(a, data);
        if (byId.TryGetValue(r.ObjectId, out replaced))
        {
            int j = Find(replaced.Name);
            if (j >= 0 && byName[j] == replaced) byName.RemoveAt(j);
        }
        byId[r.ObjectId] = r;
        int i = Find(r.Name);
        if (i >= 0) byName[i] = r;
        else byName.Insert(~i, r);
        return r;
    }

    /// <summary>OnReactor mode 3 (:6728-6738): FindObjectById, then the reactor's FUN_140b79a10; null when no reactor has
    /// the id.</summary>
    public Reactor Update(int objectId, ReactorState s)
    {
        if (!byId.TryGetValue(objectId, out var r)) return null;
        r.Update(s);
        return r;
    }

    /// <summary>OnReactor mode 1 (:6722-6727), FUN_141bbbcf0 → FUN_141bbb7f0 (worldservice_1.cpp:3215-3229): the object id
    /// leaves the object map and the reactor's name the name map (RemoveKey by name, so a reactor that replaced it there
    /// goes from the poll as well).</summary>
    public Reactor Remove(int objectId)
    {
        if (!byId.Remove(objectId, out var r)) return null;
        int i = Find(r.Name);
        if (i >= 0) byName.RemoveAt(i);
        return r;
    }

    /// <summary>The field is left: m_7c8.RemoveAll (worldservice_1.cpp:10083) with the objects.</summary>
    public void Clear()
    {
        byId.Clear();
        byName.Clear();
    }

    /// <summary>WorldService::FindSpawnByName as StateReact reads it (statereact.cpp:158, 223, 293): the name map's
    /// reactor, or null. No allocation.</summary>
    public Reactor FindByName(string name)
    {
        if (name == null) return null;
        int i = Find(name);
        return i >= 0 ? byName[i] : null;
    }

    int Find(string name)
    {
        int lo = 0, hi = byName.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            int c = string.CompareOrdinal(byName[mid].Name, name);
            if (c == 0) return mid;
            if (c < 0) lo = mid + 1; else hi = mid - 1;
        }
        return ~lo;
    }

    /// <summary>
    /// WorldService::FUN_141bc6c60(pos, 1) (@0x141bc6c60, worldservice_1.cpp), ReactorAction's poll (FUN_14095fcb0): the
    /// usable reactor nearest to <paramref name="pos"/> within its data's reactDistance, walking the name map; a later
    /// reactor at the same distance replaces the earlier one (only a longer distance is skipped).
    /// </summary>
    public Reactor Nearest(Vector3 pos, IReactorUser user)
    {
        float best = float.MaxValue;
        Reactor found = null;
        for (int i = 0; i < byName.Count; i++)
        {
            var r = byName[i];
            if (!r.CanUse(user)) continue;
            if (r.Data == null) continue;
            float dist = (pos - r.Position).Length();
            if (dist > r.Data.ReactDistance) continue;
            if (dist > best) continue;
            best = dist;
            found = r;
        }
        return found;
    }

    /// <summary>
    /// CheckReactorGameEvent @0x140b7a3d0 (reactor.cpp:558-582): 1 for an unknown reactor; 0 for gameEventId 0; 4 without
    /// the rule's reactor context or for another event's; 5 when the field is not among its field ids; 6 when the event's
    /// attend count (g_attendTable.GetValue(0x76d, event, 0)) reached its m_28; else 0.
    /// STILL-OPEN: g_attendTable (the per-event counters) has no port, so the count is 0 and 6 never comes.
    /// </summary>
    public static int CheckGameEvent(ReactorTable.Reactor data, ReactorEvent rule, uint fieldId, int attendCount = 0)
    {
        if (data == null) return 1;
        uint ev = data.GameEventId;
        if (ev == 0) return 0;
        if (rule == null || (uint)rule.EventId != ev) return 4;
        bool inField = false;
        foreach (uint f in rule.FieldIds)
            if (f == fieldId) { inField = true; break; }
        if (!inField) return 5;
        if (attendCount >= rule.MaxCount) return 6;
        return 0;
    }

    /// <summary>ReactorAction::vfunc_2's outcome (@0x14095f2f0, actionkeyobject.cpp:2887-2938).</summary>
    public enum UseOutcome
    {
        /// <summary>Nothing happens.</summary>
        None,
        /// <summary>ShowSystemNotice(s_reactor_action_failed_reward_count, 1, 1): check 6.</summary>
        FailedRewardCount,
        /// <summary>ShowSystemNotice(s_reactor_action_failed, 1, 1): any other nonzero check.</summary>
        Failed,
        /// <summary>vfunc_59(0x3d, StateReactData(reactorId, name, ""), 0x9f, 0): the react cast.</summary>
        Cast,
        /// <summary>SendRequestRideMode1(RideOffActionReact(reactorId, name)): off the ride first, the cast after.</summary>
        RideOff,
        /// <summary>FUN_14152e0c0, then SendStateReact: a reactor of reactTimeMSec 0.</summary>
        Send,
    }

    /// <summary>
    /// ReactorAction::vfunc_2: the game event check's notices; with a reactTimeMSec the cast (or the ride-off) when MyPC may
    /// enter state 0x3d (FUN_141527160: state 1, 0xb, 0xf, 0x21 or 0x3a), else nothing; without one, the StateReact send
    /// unless the player is in state 0x10.
    /// </summary>
    public static UseOutcome Use(ReactorTable.Reactor data, int check, int playerState, bool riding)
    {
        if (data == null) return UseOutcome.None;
        if (check == 6) return UseOutcome.FailedRewardCount;
        if (check != 0) return UseOutcome.Failed;
        if (data.ReactTimeMSec != 0)
        {
            if (!CanEnterReact(playerState)) return UseOutcome.None;
            return riding ? UseOutcome.RideOff : UseOutcome.Cast;
        }
        return playerState == 0x10 ? UseOutcome.None : UseOutcome.Send;
    }

    /// <summary>MyPC::FUN_141527160(0x3d) (@0x141527160, mypc.cpp:1872-1882).</summary>
    public static bool CanEnterReact(int playerState) =>
        playerState is 1 or 0xb or 0xf or 0x21 or 0x3a;
}
