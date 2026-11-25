namespace Ms2.Ui;

/// <summary>What <see cref="HoldAction"/> reads of the own pc and the field, besides <see cref="IHoldPcWorld"/>'s
/// FindHoldTarget and IsPc.</summary>
public interface IHoldWorld
{
    /// <summary>A local player that is not dead and whose move gate (FUN_141683ef0) passes.</summary>
    bool HoldUsable { get; }
    /// <summary>The own pc's state id (GetCurStateId).</summary>
    int HoldState { get; }
    /// <summary>The own pc's object id.</summary>
    int HoldSelfId { get; }
    /// <summary>The own pc rides (GetRidee).</summary>
    bool HoldRiding { get; }
    /// <summary>The current state's sub id (PCBase::FUN_14166cc80, the state's m_n10).</summary>
    int HoldSubState { get; }
    /// <summary>PC::FindHoldTarget (WorldService::FindNearestActor): the actor with the nearest hold node within
    /// <paramref name="range"/> of the own feet and that node's name; 0 when none.</summary>
    int FindHoldTarget(float range, out string node);
    /// <summary>WorldService::FindPCById finds a player (the own pc included).</summary>
    bool IsPc(int objectId);
    /// <summary>WorldService::FindNpcById: the npc's id (CNpc::GetNpcId) when the object is an npc.</summary>
    bool FindNpc(int objectId, out int npcId);
}

/// <summary>
/// HoldAction (action key 6, actionkeyobject.cpp:2196-2295): hold on to an npc that has hold nodes (PCBase::vfunc_117,
/// the body's Hold_PointNN nodes), or let go of it. The poll (vfunc_1) applies while such an npc's node is within
/// FindHoldTargetRange; the press (vfunc_2) leaves the hold in StateHold, rides off with a RideOffActionHold when riding,
/// else enters StateHold (0x20) on the node found within the range + 20.
/// </summary>
public sealed class HoldAction
{
    /// <summary>vfunc_5: the action's key.</summary>
    public const int Key = 6;
    /// <summary>The ClientConstants FindHoldTargetRange default (GetClientConstants()->FindHoldTargetRange.Get(230)).</summary>
    public const float DefaultRange = 230f;
    /// <summary>vfunc_2 searches 20 further than the poll (m_f24 + 20).</summary>
    public const float UseExtraRange = 20f;
    /// <summary>The own state the key refuses (0x2b) and StateHold (0x20).</summary>
    public const int StateRefused = 0x2b, StateHold = 0x20;
    /// <summary>The skill state (0x10) and its sub id while the cast is attached to an actor (StateSkill::vfunc_4 sets
    /// m_n10 0x3b when the record's m_nB0, the attach id, is set; stateskill.cpp:491-494), which both vfuncs refuse.</summary>
    public const int StateSkill = 0x10, SubSkillAttached = 0x3b;

    static bool Refused(IHoldWorld w, int state) =>
        state == StateRefused || (state == StateSkill && w.HoldSubState == SubSkillAttached);
    /// <summary>vfunc_59(6): the state the press leaves a hold for (StateJump).</summary>
    public const int StateLeave = 6;
    /// <summary>The cart npc whose captions are their own (vfunc_6).</summary>
    public const int CartNpcId = 0xa7e02a;
    /// <summary>vfunc_6's captions: common 0x652 / 0x653, and the cart's by key.</summary>
    public const int CaptionHold = 0x652, CaptionHoldEnd = 0x653;
    public const string CaptionCartKey = "s_action_hold_cart", CaptionCartEndKey = "s_action_hold_end_cart";

    public enum Press { None, Leave, RideOff, Hold }

    /// <summary>m_nNpcId: the npc id of the last npc the poll found (0 for an actor that is not an npc).</summary>
    public int NpcId { get; private set; }
    /// <summary>m_n18 (vfunc_10): the caption must be refreshed (the npc changed, or the hold was entered or left since the
    /// caption was taken).</summary>
    public bool Refresh { get; private set; }
    /// <summary>m_f24: the range the last poll searched.</summary>
    public float Range { get; private set; } = DefaultRange;
    bool captionHolding;   // m_n1c: the caption was taken in StateHold

    /// <summary>
    /// vfunc_1 (@0x14095c060): 0 for no usable player, state 0x2b, or the skill state attached to an actor (0x10 with sub 0x3b); the hold target within <paramref name="range"/>; the own
    /// pc's id or a player refuses; m_n18 cleared, then an npc of another npc id than kept sets m_nNpcId and m_n18, any other
    /// actor clears m_nNpcId; the caption's hold state differing from the current one sets m_n18; then 1.
    /// </summary>
    public bool Poll(IHoldWorld w, float range = DefaultRange)
    {
        if (!w.HoldUsable) return false;
        int state = w.HoldState;
        if (Refused(w, state)) return false;
        Range = range;
        int id = w.FindHoldTarget(range, out _);
        if (id == 0) return false;
        if (id == w.HoldSelfId) return false;
        if (w.IsPc(id)) return false;
        Refresh = false;
        if (w.FindNpc(id, out int npcId))
        {
            if (NpcId != npcId) { NpcId = npcId; Refresh = true; }
        }
        else NpcId = 0;
        if (!captionHolding && state == StateHold) Refresh = true;
        if (captionHolding && state != StateHold) Refresh = true;
        return true;
    }

    /// <summary>
    /// vfunc_2 (@0x14095c2f0): nothing for no usable player, state 0x2b or 0x10 with sub 0x3b; in StateHold vfunc_59(6) (leave); riding,
    /// SendRequestRideMode1(RideOffActionHold(0)); else the hold target within m_f24 + 20 enters StateHold with its id and
    /// node (<paramref name="target"/>, <paramref name="node"/>), nothing when none is found.
    /// </summary>
    public Press Use(IHoldWorld w, out int target, out string node)
    {
        target = 0;
        node = null;
        if (!w.HoldUsable) return Press.None;
        int state = w.HoldState;
        if (Refused(w, state)) return Press.None;
        if (state == StateHold) return Press.Leave;
        if (w.HoldRiding) return Press.RideOff;
        target = w.FindHoldTarget(Range + UseExtraRange, out node);
        return target != 0 ? Press.Hold : Press.None;
    }

    /// <summary>vfunc_6 (@0x14095c560): in StateHold the let-go caption (the cart's own for npc 0xa7e02a), else the hold
    /// caption; it keeps which one it gave (m_n1c). Returns the common string id, or 0 with <paramref name="key"/> set for
    /// the cart's.</summary>
    public int Caption(int state, out string key)
    {
        captionHolding = state == StateHold;
        key = null;
        if (captionHolding)
        {
            if (NpcId == CartNpcId) { key = CaptionCartEndKey; return 0; }
            return CaptionHoldEnd;
        }
        if (NpcId == CartNpcId) { key = CaptionCartKey; return 0; }
        return CaptionHold;
    }
}
