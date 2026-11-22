using System;
using Ms2.Net.Wire;

namespace Ms2.Ui;

/// <summary>The own pc's ride-off request (MyPC::SendRequestRideMode1 @0x14152c280, mypc.cpp:3566).</summary>
public static class RideOff
{
    /// <summary>CRideeData m_basic.m_nType (RidingTable basic type, FUN_140785190 @0x140785190).</summary>
    public const int TypeDefault = 0, TypeBattle = 1, TypeObject = 2;
    /// <summary>The notice FUN_141275260(0x655, 1, 0) shows: s_action_cant_ride.</summary>
    public const int CantRideNoticeId = 0x655;
    /// <summary>RideOffAction m_nType 0xf: the action that needs the mount's enableRideOffUI.</summary>
    public const int ActionTypeUi = 0xf;

    public enum Outcome
    {
        /// <summary>Not riding, or no ridee data: returns 1 without a send.</summary>
        NotRiding,
        /// <summary>A battle or object mount refuses an action whose m_n20 is 0: the s_action_cant_ride notice, returns 0.</summary>
        CantRide,
        /// <summary>Action type 0xf on a mount without enableRideOffUI: returns 0.</summary>
        Refused,
        /// <summary>RequestRide mode 1 is sent: returns 1.</summary>
        Sent,
    }

    /// <summary>FUN_140785190 (@0x140785190, unit_140781d80_ridingtable.cpp:214): basic type "default" 0, "battle" 1,
    /// "object" 2 (case-insensitive), anything else 0.</summary>
    public static int TypeOf(string basicType)
    {
        if (string.Equals(basicType, "battle", StringComparison.OrdinalIgnoreCase)) return TypeBattle;
        if (string.Equals(basicType, "object", StringComparison.OrdinalIgnoreCase)) return TypeObject;
        return TypeDefault;
    }

    /// <summary>
    /// SendRequestRideMode1: no ride or no ridee data returns without a send; a battle or object mount (type 1 or 2) refuses an
    /// action whose m_n20 is 0 with the s_action_cant_ride notice; action type 0xf needs the mount's enableRideOffUI; else
    /// <paramref name="request"/> (u8 1 and the action) is sent.
    /// </summary>
    public static Outcome Request(bool riding, bool hasRideeData, int rideeType, bool enableRideOffUI, int actionType,
        bool allowsBattleMount, IOutPacket request, Action<IOutPacket> send)
    {
        if (!riding || !hasRideeData) return Outcome.NotRiding;
        if ((rideeType == TypeBattle || rideeType == TypeObject) && !allowsBattleMount) return Outcome.CantRide;
        if (actionType == ActionTypeUi && !enableRideOffUI) return Outcome.Refused;
        send(request);
        return Outcome.Sent;
    }
}
