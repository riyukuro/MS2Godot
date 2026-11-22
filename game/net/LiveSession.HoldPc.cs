using System;
using Godot;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;
using Ms2Godot.Movement;

namespace Ms2Godot.Net;

// HoldPCAction (action key 0x1b): the own pc boards another player's mount (RequestRide mode 3), riding off its own first.
public partial class LiveSession : IHoldPcWorld
{
    /// <summary>The hold pc action key's state (m_n18).</summary>
    public readonly HoldPcAction HoldPc = new();

    float? holdRange;
    /// <summary>ClientConstants FindHoldTargetRange (230 when the tables do not set it).</summary>
    float HoldRange => holdRange ??= Constants()?.Float("FindHoldTargetRange", HoldPcAction.DefaultRange) ?? HoldPcAction.DefaultRange;

    bool? friendsOnlyFeature;
    /// <summary>Feature 0xad: boarding only a friend's, a party member's or a guild member's mount.</summary>
    bool FriendsOnlyFeature()
    {
        if (friendsOnlyFeature is { } on) return on;
        try { on = LobbyFeatures.IsEnabled(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir), FeatureTable.Names[HoldPcAction.FriendsOnlyFeature - 1]); }
        catch (Exception e) { GD.Print($"live: feature table unavailable ({e.Message})"); on = false; }
        friendsOnlyFeature = on;
        return on;
    }

    // FUN_141683ef0 (the move gate) is not ported and taken as passing, as the taxi key does
    bool IHoldPcWorld.HoldPcUsable => selfObject > 0 && player != null && IsInstanceValid(player) && !player.DeadFlag;

    /// <summary>GetCurStateId as far as it is known here: StateHold 0x20, riding 0x21, a state the server set, else 1.</summary>
    int IHoldPcWorld.HoldPcState =>
        player.State == Player.MoveState.Hold ? HoldPcAction.StateHold
        : player.Mount != null ? 0x21
        : Me.State is not (0 or 1 or 2) ? Me.State : 1;

    int IHoldPcWorld.HoldPcSelfId => selfObject;
    bool IHoldPcWorld.HoldPcRiding => player.Mount != null;

    /// <summary>
    /// PC::FindHoldTarget (FindNearestActor): the own pc's feet against every PC's hold nodes, the own pc's (its body's
    /// Hold_PointNN and its own mount's seats) among them, so standing nearest to the own nodes refuses the key.
    /// The npcs are searched as well (CNpc keeps PCBase::vfunc_119); a nearer npc wins and fails FindPCById, so the key
    /// refuses. A ridee never matches: Ridee::vfunc_119 (ridee.cpp:638) fills its rider's map, its own stays empty.
    /// STILL-OPEN: the pets, guide objects and cube spawn objects (PCBase too, own vfunc_119) are not searched.
    /// </summary>
    public int FindHoldTarget(float range, out string node)
    {
        node = null;
        var from = player.GlobalPosition;
        float best = float.MaxValue;
        int id = 0;
        float own = RemoteLayer.NearestHoldNode(player.Visual?.GetNodeOrNull<Skeleton3D>(OwnSkeletonPath), player.Mount, from, out string ownNode);
        if (own <= range && own < best) { best = own; id = selfObject; node = ownNode; }
        if (remote != null && remote.NearestHold(from, range, ref best, out string n) is var r && r != 0) { id = r; node = n; }
        return id;
    }

    static readonly NodePath OwnSkeletonPath = "Model/" + ModelBuilder.SkeletonName;

    bool IHoldPcWorld.IsPc(int objectId) => remote != null && objectId != selfObject && remote.HoldPcInfo(objectId, out _, out _, out _, out _, out _, out _, out _);

    bool IHoldPcWorld.NoBoarding(int objectId) => remote != null && remote.HoldPcInfo(objectId, out _, out _, out _, out _, out _, out _, out bool nb) && nb;

    /// <summary>MyPC::FUN_1415279d0 for another player (<see cref="HoldPcAction.CanBoard"/>); the line of sight is cast only
    /// once the seat test passes.</summary>
    bool IHoldPcWorld.CanBoard(int objectId)
    {
        if (remote == null || !remote.HoldPcInfo(objectId, out bool ridee, out int passengers, out int seats, out long charId,
                out string guild, out Vector3 feet, out _))
            return false;
        if (!ridee || passengers >= seats) return false;
        bool friendsOnly = FriendsOnlyFeature();
        return HoldPcAction.CanBoard(ridee, passengers, seats, LineOfSightTo(feet), friendsOnly,
            friendsOnly && Buddies.IsFriend(charId), friendsOnly && Party.PartyId != 0 && Party.Find(charId) != null,
            Guilds.GuildName, guild);
    }

    RayQuery losRay;
    /// <summary>MyPC::HasLineOfSightTo (@0x141527b50, mypc.cpp:2042): RayCastFromPoint from the own position along the
    /// flat direction to the other's (its z set to 0) for the flat length, on the default groups 0x389; a hit blocks.</summary>
    bool LineOfSightTo(Vector3 to)
    {
        var from = player.GlobalPosition;
        var dir = new Vector3(to.X - from.X, 0, to.Z - from.Z);
        if (dir.LengthSquared() == 0) return true;
        losRay ??= RayQuery.CastRay();
        return losRay.Nearest(player.GetWorld3D().DirectSpaceState, from, from + dir, player.GetRid(), MoveConfig.DownRayGroups) == null;
    }

    /// <summary>HoldPCAction::vfunc_1 for the action key poll.</summary>
    public bool PollHoldPc() => player != null && IsInstanceValid(player) && HoldPc.Poll(this, HoldRange);

    /// <summary>HoldPCAction::vfunc_2: a rider sends RequestRide mode 1 with a RideOffActionHold (SendRequestRideMode1's
    /// mount tests; the action's m_n20 is 0, so a battle or object mount refuses it with s_action_cant_ride); else mode 3
    /// for the player the poll kept.</summary>
    public void UseHoldPc()
    {
        if (player == null || !IsInstanceValid(player)) return;
        switch (HoldPc.Use(this))
        {
            case HoldPcAction.Press.RideOff:
                var ride = player.Mount?.Data;
                if (RideOff.Request(true, ride != null, ride?.Type ?? 0, ride?.EnableRideOffUI ?? false, RideOffHoldRequest.ActionType,
                        false, new RideOffHoldRequest(), Send) == RideOff.Outcome.CantRide)
                    SystemNoticeSink?.Invoke(MessageBoxData.Common(RideOff.CantRideNoticeId));
                break;
            case HoldPcAction.Press.Board:
                SendRideBoard(HoldPc.Target);
                break;
        }
    }

    /// <summary>MyPC::SendRequestRideMode3 (@0x14152dee0). STILL-OPEN: content 0x57's shutdown (notice 0x2f7, no send).</summary>
    void SendRideBoard(int ownerId) => Send(new RideBoardRequest(ownerId));

    /// <summary>
    /// MyPC::vfunc_121's case 7 (mypc.cpp:3446-3466), after a ride off answered with a RideOffActionHold: the hold target
    /// within FindHoldTargetRange + 20; a player with a mount FUN_1415279d0 accepts gets mode 3, a player without one is
    /// held on (vfunc_59(0x20) with that node).
    /// </summary>
    void HoldAfterRideOff()
    {
        int id = FindHoldTarget(HoldRange + HoldPcAction.RideOffExtraRange, out string node);
        bool hasRidee = false;
        if (id != 0 && remote != null) remote.HoldPcInfo(id, out hasRidee, out _, out _, out _, out _, out _, out _);
        switch (HoldPcAction.AfterRideOff(this, hasRidee, id, out int board))
        {
            case HoldPcAction.RideOffOutcome.Board: SendRideBoard(board); break;
            case HoldPcAction.RideOffOutcome.Hold: player.HoldOn(id, node); break;
        }
    }
}
