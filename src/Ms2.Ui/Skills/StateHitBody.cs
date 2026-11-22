using System.Numerics;

namespace Ms2.Ui.Skills;

/// <summary>
/// GameObjectController's lift (m_bLift, m_bLiftFalling, m_fLiftTime, m_fLiftHeight, m_fLiftDuration, m_fLiftPeak,
/// m_fLiftBase): the arc StateHit starts when a hit lifts the body. Heights are field units (the owner's z).
/// </summary>
public sealed class LiftArc
{
    /// <summary>m_bLift / m_bLiftFalling.</summary>
    public bool Active, Falling;
    /// <summary>m_fLiftTime, m_fLiftHeight (the last height), m_fLiftDuration, m_fLiftPeak, m_fLiftBase.</summary>
    public float Time, Height, Duration, Peak, Base;

    /// <summary>
    /// StartLift @0x141240cb0 (unit_14123f5b0_gameobjectcontrollerphysxhelpers.cpp:409): nothing while a lift runs, else
    /// the arc starts at <paramref name="baseZ"/>. Returns whether it started; the body's jump then ends (m_bJumping = 0).
    /// </summary>
    public bool Start(float duration, float peak, float baseZ)
    {
        if (Active) return false;
        Active = true;
        Time = 0f;
        Height = baseZ;
        Duration = duration;
        Peak = peak;
        Base = baseZ;
        Falling = false;
        return true;
    }

    /// <summary>EndLift @0x141240d70 (:425): the lift ends; the body resets its fall timer.</summary>
    public void End() => Active = false;

    /// <summary>
    /// FUN_1412407f0's lift branch (@0x1412407f0, :365-381): the time moves on, the height is
    /// base - 4 peak f (f - 1) with f = 1 - (duration - time) / duration, the lift turns to falling the first time the
    /// height drops (<paramref name="startedFalling"/>: MyPC then marks the fall start, FUN_141676060), and the step is
    /// the height's change. The step is in field units; the client's FromPercent turns it into the controller's metres.
    /// </summary>
    public float Step(float dt, out bool startedFalling)
    {
        Time += dt;
        float h = Base;
        float f = 1f - (Duration - Time) / Duration;
        h -= 4f * Peak * f * (f - 1f);
        startedFalling = false;
        if (!Falling && h < Height)
        {
            Falling = true;
            startedFalling = true;
        }
        float step = h - Height;
        Height = h;
        return step;
    }
}

/// <summary>What StateHit asks the body to do after an update or a sequence end.</summary>
public enum HitStep
{
    /// <summary>Nothing.</summary>
    None,
    /// <summary>vfunc_59(1): on to StateIdle.</summary>
    Idle,
    /// <summary>PlayAnimation("Damg_C", 1, false): the down sequence, without restarting it.</summary>
    Down,
    /// <summary>PlayAnimation("Jump_FlyDamg_A", 1, true): the fly sequence again.</summary>
    ReplayFly,
}

/// <summary>
/// StateHit's timeline (statehit.cpp): the data it copied on entering (<see cref="StateHitData"/>), the time the hit
/// arrives (m_fA0), the owner's z (m_fAc), the end time (m_fEndTime) and the sequence it plays (m_strAnim).
/// Times are the game time (CMainSystem::GetGameTime) in seconds.
/// </summary>
public sealed class StateHitTimeline
{
    /// <summary>The fly and down sequences StateHit names (statehit.cpp:90, 125, 175-176).</summary>
    public const string FlySequence = "Jump_FlyDamg_A", DownSequence = "Damg_C";

    /// <summary>m_f9c, m_fA4, m_fA8, m_fB0, m_nB4, m_nBc and m_strC0, copied from the entering data.</summary>
    public readonly StateHitData Data = new();
    /// <summary>m_fA0: the data's duration after the game time at entering; 0 without data.</summary>
    public float ArriveTime;
    /// <summary>m_fAc: the owner's z at entering, the lift's base.</summary>
    public float OwnerZ;
    /// <summary>m_fEndTime: set by a sequence end to <see cref="ArriveTime"/> + the pause; 0 until then.</summary>
    public float EndTime;
    /// <summary>The sequence's speed (the data's m_f18, 1 without data).</summary>
    public float Speed = 1f;
    /// <summary>m_strAnim: the sequence playing; empty when the body has none.</summary>
    public string Sequence = "";

    /// <summary>The hit lifts the body (m_fB0 is not about 0, :88).</summary>
    public bool Lifts => !HitReaction.NearlyZero(Data.UpDistance);

    /// <summary>
    /// vfunc_4 @0x1417ca370 (statehit.cpp:65-85): FUN_1417cb1d0 clears the fields, then with data the speed, pause,
    /// duration, distance, arrive time ((float)(duration + game time)), lift, down, owner z and priority and the
    /// animation are copied. The sequence and the lift are picked by the body (<see cref="PickSequence"/>).
    /// </summary>
    public void Enter(StateHitData data, double now, float ownerZ)
    {
        Clear();
        Speed = 1f;
        if (data == null) return;
        Speed = data.SpeedRate;
        Data.PauseTime = data.PauseTime;
        Data.Duration = data.Duration;
        Data.Distance = data.Distance;
        ArriveTime = (float)(Data.Duration + now);
        Data.UpDistance = data.UpDistance;
        Data.Down = data.Down;
        OwnerZ = ownerZ;
        Data.Priority = data.Priority;
        Data.Animation = data.Animation;
        Data.SpeedRate = data.SpeedRate;
    }

    /// <summary>FUN_1417cb1d0 @0x1417cb1d0 (statehit.cpp:212).</summary>
    public void Clear()
    {
        EndTime = 0f;
        ArriveTime = 0f;
        OwnerZ = 0f;
        Data.Clear();
        Sequence = "";
    }

    /// <summary>
    /// vfunc_4's sequence (statehit.cpp:88-110): the fly sequence when the hit lifts, else the body's hit action
    /// (<paramref name="hitAction"/>, vfunc_123(0x35)); the data's animation replaces it when the body has that one; a
    /// sequence the body does not have leaves none (empty).
    /// </summary>
    public string PickSequence(string hitAction, System.Predicate<string> has)
    {
        string seq = Lifts ? FlySequence : hitAction ?? "";
        if (!string.IsNullOrEmpty(Data.Animation) && has(Data.Animation)) seq = Data.Animation;
        Sequence = seq.Length > 0 && has(seq) ? seq : "";
        return Sequence;
    }

    /// <summary>
    /// vfunc_3 @0x1417ca8a0 (statehit.cpp:116-130), after the state's move: once an end time is set and the game time
    /// reaches it, the down sequence plays when the data has down, else the state goes on to StateIdle.
    /// </summary>
    public HitStep Update(double now)
    {
        if (0f < EndTime && now >= EndTime) return Data.Down != 0 ? HitStep.Down : HitStep.Idle;
        return HitStep.None;
    }

    /// <summary>
    /// EndOfSequence @0x1417caaf0 (statehit.cpp:155-175), for the target sequence ending: with no duration and no pause,
    /// or the down sequence ending, on to StateIdle; else the fly sequence plays again if it was the one, and the end time
    /// becomes the arrive time + the pause.
    /// </summary>
    public HitStep EndOfSequence(string ended)
    {
        if ((HitReaction.NearlyZero(Data.Duration) && HitReaction.NearlyZero(Data.PauseTime)) || ended == DownSequence)
            return HitStep.Idle;
        EndTime = ArriveTime + Data.PauseTime;
        return ended == FlySequence ? HitStep.ReplayFly : HitStep.None;
    }

    /// <summary>FUN_1417cb160 @0x1417cb160 (statehit.cpp:203): false (the fall damage waits, PCBase::SendFallDamage
    /// pcbase.cpp:4360) while the game time is before the arrive time and the hit lifts.</summary>
    public bool FallDamageAllowed(double now) => !((float)now < ArriveTime && Lifts);
}

/// <summary>What StateHit's escape (vfunc_8) leads to.</summary>
public enum HitEscape
{
    /// <summary>The state stays.</summary>
    None,
    /// <summary>vfunc_59(2): StateWalk.</summary>
    Walk,
    /// <summary>vfunc_59(6): StateJump.</summary>
    Jump,
    /// <summary>State::FUN_1417b9460: the next queued skill.</summary>
    Skill,
}

/// <summary>Client::Constants EscapeHitMethodSkill / Jump / Move and EscapeHitMoveKeyIsDown (constantsblocks.cpp:803-806,
/// all defined 0), read once by StateHit::vfunc_8 (statehit.cpp:184-187).</summary>
public sealed class EscapeHitMethods
{
    public int Skill, Jump, Move, MoveKeyIsDown;

    /// <summary>Reads the four constants (key / value rows of table/constants.xml), 0 when absent.</summary>
    public static EscapeHitMethods From(Ms2.Formats.World.ClientConstants c) => new()
    {
        Skill = c.Int("EscapeHitMethodSkill", 0),
        Jump = c.Int("EscapeHitMethodJump", 0),
        Move = c.Int("EscapeHitMethodMove", 0),
        MoveKeyIsDown = c.Int("EscapeHitMoveKeyIsDown", 0),
    };

    /// <summary>
    /// StateHit::vfunc_8 @0x1417cad10 (statehit.cpp:179-198): nothing while a lift rises; nothing while the body cannot act
    /// (vfunc_63: m_unk_158 is set); then with the move method and the controller moving by the input (FUN_1416749c0)
    /// StateWalk, else with the jump method, on the ground (vfunc_99(0) 0) and the jump key pressed StateJump, else with
    /// the skill method the next skill (FUN_1417b9460). The body's move direction is set to zero afterwards (:199).
    /// </summary>
    /// <param name="moving">FUN_1416749c0 after vfunc_22 took the input's move direction (KeyIsDown: the held keys'
    /// GetMoveDirection, else the pressed ones').</param>
    public HitEscape Escape(bool liftActive, bool liftFalling, bool canAct, bool moving, bool inAir, bool jumpPressed)
    {
        if (liftActive && !liftFalling) return HitEscape.None;
        if (!canAct) return HitEscape.None;
        if (Move != 0 && moving) return HitEscape.Walk;
        if (Jump != 0 && !inAir && jumpPressed) return HitEscape.Jump;
        if (Skill != 0) return HitEscape.Skill;
        return HitEscape.None;
    }
}

/// <summary>What the push tick (<see cref="PushControl.Tick"/>) reads of and does to the body, in field coordinates.</summary>
public interface IPushBody
{
    /// <summary>vfunc_18: the feet, z up.</summary>
    Vector3 FieldPosition { get; }
    /// <summary>GetPrevState: the client id of the state before the current one.</summary>
    int PrevStateId { get; }
    /// <summary>CPhysXHelper::RayCastFromPoint (physxhelper.cpp:433) on groups 0x389: a hit within <paramref name="len"/>.</summary>
    bool RayHits(Vector3 from, Vector3 dir, float len);
    /// <summary>A ray straight down on groups 0x389 within <paramref name="len"/> (0 = unlimited): RayCastFixedDirection
    /// (:451) / GetGroundDistance (:475). <paramref name="hitZ"/> is the hit's z.</summary>
    bool GroundBelow(Vector3 from, float len, out float hitZ);
    /// <summary>PCBase::vfunc_106 (@0x141674750, pcbase.cpp:4188): the controller's move by <paramref name="move"/>
    /// (field units) leaving out the npcs (flags 0x10), then the body faces against the move (heading -atan2(x, y)).</summary>
    void PushMove(Vector3 move, double dt);
}

public static class PushTick
{
    /// <summary>The ledge probe's height (vfunc_1's 150, hitcontrol.cpp:82).</summary>
    public const float LedgeHeight = 150f;

    /// <summary>
    /// PCBase::FUN_1416725c0 @0x1416725c0 (pcbase.cpp:4006): whether the move ahead leaves the ground. From the feet
    /// raised by <paramref name="up"/>, a ray along the direction and one along its flat part (a hit is a wall, not a
    /// ledge: 0), then the ground within 10 + height + 1 under the point 10 above the move's end, and again 0.1 / 0.05
    /// aside (ground: 0). No ground ahead is 1.
    /// </summary>
    public static bool LedgeAhead(IPushBody body, Vector3 dir, float dist, float height, float up)
    {
        dir = HitReaction.Unitize(dir);
        if (dir == Vector3.Zero) return false;
        var pos = body.FieldPosition;
        pos.Z += up;
        if (dist < 0f)
        {
            dir = -dir;
            dist = -dist;
        }
        var flat = new Vector3(dir.X, dir.Y, 0f);
        if (body.RayHits(pos, dir, dist)) return false;
        if (body.RayHits(pos, flat, dist)) return false;
        pos = pos + dir * dist + new Vector3(0f, 0f, 10f);
        float len = 10f + height + 1f;
        if (body.GroundBelow(pos, len, out _)) return false;
        pos.X += 0.1f;
        pos.Y += 0.05f;
        if (body.GroundBelow(pos, len, out _)) return false;
        return true;
    }
}
