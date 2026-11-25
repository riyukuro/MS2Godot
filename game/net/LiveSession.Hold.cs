using System;
using Godot;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Net;

// HoldAction (action key 6): the own pc holds on to an npc with hold nodes (StateHold 0x20), or lets go of it.
public partial class LiveSession : IHoldWorld
{
    /// <summary>The hold action key's state (m_nNpcId, m_n18, m_f24).</summary>
    public readonly HoldAction Hold = new();

    bool holdPolled;
    Func<bool> holdPoll;
    /// <summary>The interact poll's last pick was HoldAction (key 6).</summary>
    public bool HoldPolled => holdPolled;

    // FUN_141683ef0 (the move gate) is not ported and taken as passing, as the hold pc and taxi keys do
    bool IHoldWorld.HoldUsable => selfObject > 0 && player != null && IsInstanceValid(player) && !player.DeadFlag;

    /// <summary>GetCurStateId (<see cref="OwnCurStateIdNow"/>): StateHold is the move state's 0x20.</summary>
    int IHoldWorld.HoldState => OwnCurStateIdNow;
    int IHoldWorld.HoldSelfId => selfObject;
    bool IHoldWorld.HoldRiding => player.Mount != null;

    /// <summary>FindPCById: another player here, or the own pc.</summary>
    bool IHoldWorld.IsPc(int objectId) =>
        objectId == selfObject || remote != null && remote.HoldPcInfo(objectId, out _, out _, out _, out _, out _, out _, out _);

    bool IHoldWorld.FindNpc(int objectId, out int npcId)
    {
        npcId = 0;
        return remote != null && remote.NpcHoldable(objectId, out npcId, out _);
    }

    /// <summary>HoldAction::vfunc_1 for the action key poll (inside the interact part, after gathering).</summary>
    bool PollHold() => player != null && IsInstanceValid(player) && Hold.Poll(this, HoldRange);

    /// <summary>PCBase::vfunc_117 for a field npc: its body has a Hold_PointNN node (m_unk_b88 not empty).</summary>
    public bool NpcHoldable(int objectId) => remote != null && remote.NpcHoldable(objectId, out _, out bool holdable) && holdable;

    /// <summary>HoldAction::vfunc_6's caption text for the own state (common 0x652 / 0x653, the cart's by key).</summary>
    public string HoldCaption(GameTables t)
    {
        int id = Hold.Caption(OwnCurStateIdNow, out string key);
        return key != null ? t.Common(key) ?? "" : t.Common(id) ?? "";
    }

    /// <summary>
    /// HoldAction::vfunc_2 (@0x14095c2f0): in StateHold vfunc_59(6) lets go (StateJump sub 0, no move); a rider sends
    /// RequestRide mode 1 with a RideOffActionHold (vfunc_121's case 7 then holds on only to a player without a mount,
    /// <see cref="HoldAfterRideOff"/>); else StateHold on the node found within the range + 20.
    /// </summary>
    public void UseHold()
    {
        if (player == null || !IsInstanceValid(player)) return;
        switch (Hold.Use(this, out int target, out string node))
        {
            case HoldAction.Press.Leave:
                player.HoldLetGo();
                break;
            case HoldAction.Press.RideOff:
                var ride = player.Mount?.Data;
                if (RideOff.Request(true, ride != null, ride?.Type ?? 0, ride?.EnableRideOffUI ?? false, RideOffHoldRequest.ActionType,
                        false, new RideOffHoldRequest(), Send) == RideOff.Outcome.CantRide)
                    SystemNoticeSink?.Invoke(MessageBoxData.Common(RideOff.CantRideNoticeId));
                break;
            case HoldAction.Press.Hold:
                player.HoldOn(target, node);
                break;
        }
    }
}
