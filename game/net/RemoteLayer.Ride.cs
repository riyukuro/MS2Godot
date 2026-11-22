using Godot;
using Ms2.Net.Packets;
using Ms2.Net.Replay;

namespace Ms2Godot.Net;

/// <summary>
/// The other players' mounts: PCBase::vfunc_120 / vfunc_121 (pcbase.cpp:3289-3420) of a remote PC. The mount is a PC body
/// of its own (<see cref="RemotePc.CreateRidee"/>) that replays the PC's RideSync records (CSceneStateGame::OnRideSync,
/// scenestategame.cpp:2472: the PC's ridee, nothing without one) through its own states; the rider sits in StateRide on its
/// seat and plays StateRide::vfunc_9's sequence by the mount's state (<see cref="StateRideAnim"/>).
/// STILL-OPEN: the ride-on and ride-off effects (CommonResource +0x2b0 / +0x2b8) and rideOffBySkillEffect, hideRider
/// (AddHideReason(2)), StateRide's shadow off, the rider's stats copied to the ridee, the breakable anchor of a ride-off,
/// the fall start the ridee had (StateFallData), the ridee's m_unk_b80 SP idle, the mount's step keys, the ride-on action's
/// m_b28 of mode 6.
/// </summary>
public sealed partial class RemoteLayer
{
    /// <summary>OnResponseRide (scenestategame.cpp:12026) for another player's PC: modes 0-2 (<see cref="Ms2.Ui.OwnRide.ForActor"/>),
    /// the owner's passenger map of modes 3-5 (<see cref="Ms2.Ui.RidePassengers"/>) and mode 6's PC::FUN_1416642c0 (m_b1224).</summary>
    public void OnRide(ResponseRide r)
    {
        if (r == null || !users.TryGetValue(r.ObjectId, out var u)) return;
        if (Ms2.Ui.RidePassengers.Apply(r, u.Passengers)) return;
        if (r.Mode == 6) { u.NoBoarding = r.Flag; return; }
        switch (Ms2.Ui.OwnRide.ForActor(r, u.Ridee != null))
        {
            case Ms2.Ui.OwnRide.Step.RideOn: RideOn(u, r.RideOn); break;
            case Ms2.Ui.OwnRide.Step.RideOff: RideOff(u); break;
        }
    }

    /// <summary>
    /// PCBase::vfunc_120 (@0x141676b90): nothing for a null action or with a ridee set; Ridee::FUN_1417043a0 loads the row
    /// (an unknown riding id makes no ridee); the ridee takes the rider's position and direction, Ridee::vfunc_10 raises it by
    /// 25 and puts the rider in StateRide (0x21).
    /// </summary>
    void RideOn(Remote r, RideOnAction a)
    {
        if (a == null || r.Ridee != null) return;
        Mount m;
        try { m = Mount.Load(packs, builder, a.RideId); }
        catch (Exception ex) { GD.PushWarning($"live: riding {a.RideId} of {r.Name}: {ex.Message}"); return; }
        var ridee = RemotePc.CreateRidee($"Ridee{r.ObjectId}", m, gravity, r.Pc.Body.Position + Vector3.Up * 25f, r.Pc.RotationZ, faceGrids);
        AddChild(ridee.Body);
        ridee.Spawn();
        r.Ridee = ridee;
        r.Pc.States.ChangeState(RemotePcStates.StateRide);
    }

    /// <summary>
    /// PCBase::vfunc_121 (@0x1416773f0): the rider takes the ridee's position and direction, the ridee is released, the
    /// jump stopped, then StateIdle when the ridee stood on the ground (FUN_141671a30) or StateFall.
    /// </summary>
    void RideOff(Remote r)
    {
        var ridee = r.Ridee;
        if (ridee == null) return;
        Dismount(r.Pc, r.Visual, ridee);
        FreeRidee(r);
    }

    /// <summary>The rider's side of <see cref="RideOff"/>, before the ridee is freed.</summary>
    public static void Dismount(RemotePc rider, Node3D riderVisual, RemotePc ridee)
    {
        bool ground = ridee.OnGround;
        rider.Body.Position = ridee.Body.Position;
        riderVisual.Transform = Transform3D.Identity;
        rider.RotationZ = ridee.RotationZ;
        rider.States.TargetYaw = ridee.RotationZ;
        rider.StopJump();
        rider.States.ChangeState(ground ? RemotePcStates.StateIdle : RemotePcStates.StateFall);
    }

    static void FreeRidee(Remote r)
    {
        if (r.Ridee == null) return;
        r.Ridee.Free();
        r.Ridee = null;
        r.Passengers.Clear();               // PCBase::vfunc_121's ClearPassengers (pcbase.cpp:3430)
    }

    /// <summary>
    /// Ridee::vfunc_53 (@0x1417079f0): the ridee's own frame, then the rider at its position and direction; RideeInterpolator
    /// puts the rider's model on the seat, and StateRide::vfunc_9 plays the rider's sequence at the ridee's AnimSpeed.
    /// </summary>
    static void TickRide(Remote r, float dt) => TickRide(r.Pc, r.Visual, r.Ridee, dt);

    public static void TickRide(RemotePc rider, Node3D riderVisual, RemotePc ridee, float dt)
    {
        ridee.Tick(dt);
        rider.Body.Position = ridee.Body.Position;
        rider.RotationZ = ridee.RotationZ;
        riderVisual.GlobalTransform = ridee.Mount.Seat();
        var st = ridee.States;
        rider.PlaySequence(StateRideAnim.For(st.Cur, st.Sub, ridee.Mount.Data.Rider), ridee.AnimSpeed);
    }
}
