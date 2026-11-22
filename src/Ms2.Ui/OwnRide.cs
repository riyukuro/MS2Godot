using Ms2.Net.Packets;

namespace Ms2.Ui;

/// <summary>
/// CSceneStateGame::OnResponseRide (@0x14172a8f0, scenestategame.cpp:12026) for the own pc's object: modes 0, 1 and 2.
/// Mode 0 is PCBase::vfunc_120 through MyPC::vfunc_120 (mypc.cpp:3295); modes 1 and 2 are PCBase::vfunc_121 through
/// MyPC::vfunc_121 (mypc.cpp:3348). The passenger modes 3-5 and mode 6 are not the own pc's ride.
/// </summary>
public static class OwnRide
{
    public enum Step
    {
        /// <summary>Not the own pc, not modes 0-2, or a ride-on the client drops.</summary>
        None,
        /// <summary>PCBase::vfunc_120: the ridee is made from the action's riding id.</summary>
        RideOn,
        /// <summary>PCBase::vfunc_121: the ridee is released (a no-op body when the pc has none).</summary>
        RideOff,
    }

    /// <summary>
    /// The step the own pc takes. PCBase::vfunc_120 (pcbase.cpp:3289) returns at once for a null action (a RideOnAction type
    /// above 3, ReadRideOnAction) and when a ridee is already set (m_spB10). Mode 1 passes the RideOffAction as read: type 20
    /// has no case in ReadRideOffAction (rideonaction.cpp:159), so a react ride-off comes back null and is a plain ride off
    /// (exe bug). Mode 2 passes a null action, then the summon state (see <see cref="SummonsAfterRideOff"/>).
    /// </summary>
    public static Step For(ResponseRide r, int selfObjectId, bool mounted)
    {
        if (r == null || selfObjectId <= 0 || r.ObjectId != selfObjectId) return Step.None;
        return r.Mode switch
        {
            0 => r.RideOn == null || mounted ? Step.None : Step.RideOn,
            1 or 2 => Step.RideOff,
            _ => Step.None,
        };
    }

    /// <summary>Mode 2 of the own pc (class 0x6200004): after the ride off, vfunc_59(0x26, StateSummonData(itemUid, 0)) when
    /// GetRideeData(rideeId) has a row.</summary>
    public static bool SummonsAfterRideOff(ResponseRide r, int selfObjectId) =>
        r != null && r.Mode == 2 && selfObjectId > 0 && r.ObjectId == selfObjectId;
}
