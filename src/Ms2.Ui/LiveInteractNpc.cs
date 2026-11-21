using Ms2.Formats.Model;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Hud;

namespace Ms2.Ui;

/// <summary>What the npc interaction reads from the game and how it answers.</summary>
public interface IInteractNpcHost
{
    bool HasPlayer { get; }
    /// <summary>Client::Constants::InteractionDistance (actionkeyobject.cpp:1664, stateinteractdata.cpp:641).</summary>
    float InteractionDistance { get; }
    /// <summary>The local player's forward direction (the ridee's when riding), WorldService::FindNearestNpc's front
    /// plane (worldservice_1.cpp:6443-6452).</summary>
    Vec3 PlayerForward { get; }
    /// <summary>The PvP point of the player's PvPInfo when it has one (actionkeyobject.cpp:1667-1670), else null.</summary>
    int? PvPPoint { get; }
    /// <summary>MyPC::GetCurStateId as far as the host knows it (<see cref="InteractKey.StateId"/>: 0x19 / 0x1a while an
    /// interaction's cast runs). MyPC::FUN_141527160(0x1a) (actionkeyobject.cpp:1644) lets StateInteractionNpc start only
    /// from the states <see cref="LiveInteractObjects.StateAllowsInteraction"/> names (mypc.cpp:1873-1882).</summary>
    int PlayerStateId { get; }
    void Send(IOutPacket packet);
    /// <summary>UICastingBar 0x4e72 StartCasting with Unk_CastingParam m_nDuration / m_b5 (gauge drawn).</summary>
    void StartCasting(ICastingEventListener listener, int durationMs, bool showBar);
    /// <summary>UICastingBar 0x4e72 FUN_1418e1390 (cancel).</summary>
    void CancelCasting();
    void PlayPlayerAnim(string animName, bool loop);
    void StopPlayerInteractAnim();
}

/// <summary>A field npc as the npc interaction reads it (WorldService m_148 entry).</summary>
public sealed class InteractNpcEntry
{
    public int ObjectId;
    public NpcRow Row;
    /// <summary>The npc's talkable flag (+0xe50, Unk_WsC_GameObj::IsTalkable, actionkeyobject.cpp:349): set by
    /// FieldAddNpc (FUN_14174a560, scenestategame.cpp:3561) and by InteractNpc 0x59.</summary>
    public bool Talkable;
    public bool Dead;
    public Vec3 Position;
    /// <summary>CNpcData::HasPropertyTag(8) "dummy_npc" (npcdata.cpp:1345): exempt from the front test.</summary>
    public bool DummyNpc;
}

/// <summary>
/// InteractNpcAction (action key 0x11, actionkeyobject.cpp:1620-1700) and StateInteractionNpc (stateinteractdata.cpp:566-660):
/// the action key on a talkable npc whose row has an interact function casts for the row's casting time, then sends
/// InteractNpc (0x32). InteractNpc (0x59) sets the talkable flag. Poll and Tick allocate nothing.
/// </summary>
public sealed class LiveInteractNpc : ICastingEventListener
{
    public const string DefaultCastingAnimation = "Object_React_A";   // StateInteractionNpc::vfunc_4 (stateinteractdata.cpp:580)

    readonly IInteractNpcHost host;
    readonly List<InteractNpcEntry> npcs = new();    // ascending object id, the CRBTree's order (worldservice_1.cpp:6455)
    InteractNpcEntry polled;
    int castingNpc;
    bool castingActive, castingFinished;

    public LiveInteractNpc(IInteractNpcHost host) { this.host = host; }

    public IReadOnlyList<InteractNpcEntry> All => npcs;
    public InteractNpcEntry Current => polled;
    public bool IsCasting => castingActive;
    /// <summary>The local pc's held item (PC::GetHeldItem), shared with <see cref="LiveInteractObjects.Held"/>.</summary>
    public HeldItem Held { get; set; }
    public int CastingNpc => castingActive ? castingNpc : 0;

    public InteractNpcEntry Find(int objectId)
    {
        int i = IndexOf(objectId);
        return i >= 0 ? npcs[i] : null;
    }

    int IndexOf(int objectId)
    {
        int lo = 0, hi = npcs.Count - 1;
        uint key = (uint)objectId;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            uint k = (uint)npcs[mid].ObjectId;
            if (k == key) return mid;
            if (k < key) lo = mid + 1; else hi = mid - 1;
        }
        return ~lo;
    }

    /// <summary>FieldAddNpc (scenestategame.cpp:3561): the npc with its talkable byte.</summary>
    public InteractNpcEntry Add(int objectId, NpcRow row, bool talkable, Vec3 position)
    {
        var e = new InteractNpcEntry
        {
            ObjectId = objectId, Row = row, Talkable = talkable, Position = position,
            DummyNpc = row != null && row.HasTag("dummy_npc"),
        };
        int i = IndexOf(objectId);
        if (i >= 0) npcs[i] = e; else npcs.Insert(~i, e);
        return e;
    }

    public void Remove(int objectId)
    {
        int i = IndexOf(objectId);
        if (i < 0) return;
        if (ReferenceEquals(polled, npcs[i])) polled = null;
        npcs.RemoveAt(i);
    }

    public void Move(int objectId, Vec3 position)
    {
        int i = IndexOf(objectId);
        if (i >= 0) npcs[i].Position = position;
    }

    public void SetDead(int objectId, bool dead)
    {
        int i = IndexOf(objectId);
        if (i >= 0) npcs[i].Dead = dead;
    }

    /// <summary>CSceneStateGame::OnInteractNpc (@0x141719990, scenestategame.cpp:3634): FindNpcById, then the talkable
    /// byte (FUN_14174a560). A missing npc is ignored.</summary>
    public bool Apply(InteractNpcMessage m)
    {
        int i = IndexOf(m.ObjectId);
        if (i < 0 || !m.HasTalkable) return false;
        npcs[i].Talkable = m.Talkable;
        return true;
    }

    public void Clear()
    {
        CancelCast();
        npcs.Clear();
        polled = null;
    }

    /// <summary>
    /// WorldService::FindNearestNpc (@0x141bd2170, worldservice_1.cpp:6438-6478) with addBonus 0: the nearest npc of the
    /// given friendly type within range, ties to the lower object id; with useFront, npcs behind the player's forward
    /// plane are skipped unless tagged dummy_npc.
    /// </summary>
    public InteractNpcEntry FindNearest(Vec3 playerPos, int type, float range, bool useFront)
    {
        Vec3 n = host.PlayerForward;
        InteractNpcEntry best = null;
        float bestDist = float.MaxValue;
        for (int i = 0; i < npcs.Count; i++)
        {
            var e = npcs[i];
            if (e.Row == null || e.Row.Friendly != type) continue;
            float dist = Distance(playerPos, e.Position);
            if (dist > range) continue;
            if (useFront && !e.DummyNpc)
            {
                double side = n.X * (e.Position.X - playerPos.X) + n.Y * (e.Position.Y - playerPos.Y) + n.Z * (e.Position.Z - playerPos.Z);
                if (side < 0) continue;
            }
            if (dist < bestDist) { bestDist = dist; best = e; }
        }
        return best;
    }

    /// <summary>
    /// InteractNpcAction::FindInteractNpc (@0x140959330, actionkeyobject.cpp:1658-1680): the nearest friendly (2) npc in
    /// InteractionDistance, else the nearest of kind 1 (or the PvP point); the pick is then rejected when dead, without an
    /// interact function or not talkable. No farther npc is tried.
    /// </summary>
    public InteractNpcEntry FindInteractNpc(Vec3 playerPos)
    {
        if (!host.HasPlayer) return null;
        float range = host.InteractionDistance;
        var npc = FindNearest(playerPos, 2, range, useFront: true);
        if (npc == null)
        {
            int kind = host.PvPPoint ?? 1;
            npc = FindNearest(playerPos, kind, range, useFront: true);
        }
        if (npc == null || npc.Dead) return null;
        if (string.IsNullOrEmpty(npc.Row.InteractFunction)) return null;
        if (!npc.Talkable) return null;
        return npc;
    }

    /// <summary>InteractNpcAction::vfunc_1 (@0x140959020, actionkeyobject.cpp:1628): the action takes the key when
    /// FindInteractNpc finds one. Polled after GatheringAction (key 4) and before InteractAction (key 0x12).</summary>
    public bool Poll(Vec3 playerPos)
    {
        polled = FindInteractNpc(playerPos);
        return polled != null;
    }

    /// <summary>
    /// InteractNpcAction::vfunc_2 (@0x140959060, actionkeyobject.cpp:1637-1655): enter StateInteractionNpc (0x1a) with the
    /// npc's object id; its vfunc_4 (@0x1417d2b30, stateinteractdata.cpp:575-608) plays the row's casting animation (or
    /// Object_React_A) and starts the casting bar for the row's casting time.
    /// </summary>
    public bool Use()
    {
        var npc = polled;
        if (npc == null || !host.HasPlayer) return false;
        if (!LiveInteractObjects.StateAllowsInteraction(host.PlayerStateId)) return false;   // FUN_141527160(0x1a) (:1644)
        if (string.IsNullOrEmpty(npc.Row.InteractFunction)) return false;    // m_n20 = 1 (:1653), nothing else
        if (castingActive) CancelCast();
        castingNpc = npc.ObjectId;
        castingActive = true;
        castingFinished = false;
        string anim = string.IsNullOrEmpty(npc.Row.InteractCastingAnimation) ? DefaultCastingAnimation : npc.Row.InteractCastingAnimation;
        host.PlayPlayerAnim(anim, false);
        Held?.SendRequestCube(host.Send);     // GetHeldItem, then PC::SendRequestCube (stateinteractdata.cpp:604-605)
        host.StartCasting(this, npc.Row.InteractCastingTime, npc.Row.InteractShowCastingBar);
        return true;
    }

    /// <summary>StateInteractionNpc::OnCastingFinished: m_n48 = 1.</summary>
    public void OnCastingFinished() { if (castingActive) castingFinished = true; }

    public void OnCastingCancelled()
    {
        if (!castingActive) return;
        EndState();
    }

    /// <summary>
    /// StateInteractionNpc::vfunc_8 (@0x1417d2ed0, stateinteractdata.cpp:625-661): with the npc found, a finished cast
    /// sends InteractNpc (0x32), else leaving InteractionDistance ends the state (state 1); a finished cast ends it (state 2)
    /// whether the npc was found or not; moving ends it too. Zero allocations unless the cast finishes.
    /// </summary>
    public void Tick(Vec3 playerPos, bool playerMoved)
    {
        if (!castingActive) return;
        if (!host.HasPlayer) return;
        int i = IndexOf(castingNpc);
        if (i >= 0)
        {
            if (castingFinished)
                host.Send(new InteractNpcRequest(castingNpc));
            else if (host.InteractionDistance < Distance(playerPos, npcs[i].Position))
            {
                CancelCast();
                return;
            }
        }
        if (castingFinished)
        {
            castingActive = false;
            castingFinished = false;
            host.StopPlayerInteractAnim();
            return;
        }
        if (playerMoved) CancelCast();
    }

    /// <summary>StateInteractionNpc::vfunc_5 (@0x1417d2e70): leaving the state cancels the casting bar.</summary>
    public void CancelCast()
    {
        if (!castingActive) return;
        EndState();
        host.CancelCasting();
    }

    void EndState()
    {
        castingActive = false;
        castingFinished = false;
        host.StopPlayerInteractAnim();
    }

    static float Distance(Vec3 a, Vec3 b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
