using Ms2.Net.Packets;

namespace Ms2.Ui;

/// <summary>
/// CSceneStateGame::OnResponseRide (@0x14172a8f0, scenestategame.cpp:12026) for the own pc's object (<see cref="For"/>) and
/// any other actor (<see cref="ForActor"/>): modes 0, 1 and 2.
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
        return ForActor(r, mounted);
    }

    /// <summary>
    /// The step any actor takes: modes 0 and 1 find any actor (FUN_141bd1200) and call its vfunc_120 / vfunc_121, mode 2
    /// calls vfunc_121(null) on any actor too (the summon after it is the own pc's only). <paramref name="mounted"/> is the
    /// actor's ridee (m_spB10).
    /// </summary>
    public static Step ForActor(ResponseRide r, bool mounted)
    {
        if (r == null) return Step.None;
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

/// <summary>
/// CSceneStateGame::OnResponseRide modes 3-6 (scenestategame.cpp:12065-12123) on the owner PC found by FindPCById: its
/// passenger map PCBase m_mapB28 (rider object id to seat) and PC m_b1224, the no-boarding flag.
/// </summary>
public static class RidePassengers
{
    /// <summary>The owner's map after <paramref name="r"/>: mode 3 FUN_141676670 (SetAt rider -> seat), mode 4
    /// RemovePassenger, mode 5 ClearPassengers. False for any other mode.</summary>
    public static bool Apply(ResponseRide r, IDictionary<int, int> passengers)
    {
        if (r == null || passengers == null) return false;
        switch (r.Mode)
        {
            case 3: passengers[r.RiderId] = r.Seat; return true;
            case 4: passengers.Remove(r.RiderId); return true;
            case 5: passengers.Clear(); return true;
            default: return false;
        }
    }

    /// <summary>Mode 3 with the own pc (GetLocalPlayer's object id) as the rider: it holds on to the owner, vfunc_59(0x20,
    /// StateHoldData{the owner, the seat's rideBone}).</summary>
    public static bool BoardsSelf(ResponseRide r, int selfObjectId) =>
        r != null && r.Mode == 3 && selfObjectId > 0 && r.RiderId == selfObjectId;

    /// <summary>FieldAddUser's ride block (scenestategame.cpp:11201-11212): ClearPassengers, then FUN_141676670 per entry.</summary>
    public static void Load(IReadOnlyList<(int Key, byte Value)> entries, IDictionary<int, int> passengers)
    {
        passengers.Clear();
        foreach (var (k, v) in entries) passengers[k] = v;
    }
}
