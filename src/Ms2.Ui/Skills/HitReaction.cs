using System.Numerics;

namespace Ms2.Ui.Skills;

/// <summary>
/// The push a hit gives an actor: CPushControl (hitcontrol.cpp, PCBase::m_pushControl). <see cref="Start"/> sets it from a
/// hit (PC::vfunc_107) and <see cref="Update"/> gives the distance of a tick along <see cref="Dir"/>; the move itself, with its
/// ledge test, is the actor's (CPushControl::vfunc_1 @0x1413fcb30, run by PCBase::vfunc_53 pcbase.cpp:3737-3739).
/// The curve is held as its kind instead of CreateDistanceCalculator's object (@0x14028e960, commonutil.cpp:1006), so a
/// hit allocates nothing. m_b38 (vfunc_1's change to state 1 when the push ends) is only ever cleared (Reset :129), so it is
/// not kept.
/// </summary>
public sealed class PushControl
{
    /// <summary>m_kDir: the push's unit direction, in field coordinates; zero when no push runs.</summary>
    public Vector3 Dir;
    /// <summary>m_fSpeed: the push's whole distance, in field units / 100.</summary>
    public float Speed;
    /// <summary>m_f28 / m_f2c: the push's duration and the time it has run, in seconds.</summary>
    public float Duration, Time;
    /// <summary>m_b30: the hit lifts (pushUpDistance is not 0); the move then measures from the ground under the actor.</summary>
    public bool Lift;
    /// <summary>m_n34: pushFall; the push goes on over a ledge.</summary>
    public int Fall;
    /// <summary>m_nPriority: pushPriority of the hit that started it.</summary>
    public int Priority;
    /// <summary>CreateDistanceCalculator's kind: 0 linear, 1 ease out, 2 ease in; -1 none (m_pCalc null).</summary>
    public int Curve = -1;

    /// <summary>m_kDir is not zero (vfunc_1's first test, :61).</summary>
    public bool Running => Dir != Vector3.Zero;

    /// <summary>Reset @0x1413fcf90 (hitcontrol.cpp:123).</summary>
    public void Reset()
    {
        Dir = Vector3.Zero;
        Speed = Duration = Time = 0f;
        Lift = false;
        Fall = 0;
        Priority = 0;
        Curve = -1;
    }

    /// <summary>SetCurve @0x1413fd050 (:140): CreateDistanceCalculator gives 0 linear, 1 ease out, 2 ease in, else none.</summary>
    public void SetCurve(int curve) => Curve = curve is >= 0 and <= 2 ? curve : -1;

    /// <summary>
    /// Update @0x1413fce30 (:102): with a curve and time left, the tick's delta is clamped to the time left, the run time
    /// moves by it, and the curve gives the distance of that delta; 0 otherwise.
    /// </summary>
    public float Update(double dt)
    {
        if (Curve < 0) return 0f;
        if (!(0f < Duration && Time < Duration)) return 0f;
        float delta = (float)dt;
        if (Duration < Time + delta) delta = Duration - Time;
        Time += delta;
        if (Time > Duration) Time = Duration;
        if (Time < delta) delta = Time;
        return Distance(Curve, delta, Time, Duration, Speed);
    }

    /// <summary>
    /// The calculators' vfunc_0 (commonutil.cpp:1625-1660): linear distance * delta / duration; ease out and ease in
    /// Calc(t) - Calc(t - delta), with ease out distance * sqrt(|1 - ((t - d) / d)^2|) and ease in
    /// distance * (1 - sqrt(|1 - (t / d)^2|)).
    /// </summary>
    public static float Distance(int curve, float delta, float time, float duration, float distance) => curve switch
    {
        0 => distance * (delta / duration),
        1 => EaseOut(time, duration, distance) - EaseOut(time - delta, duration, distance),
        2 => EaseIn(time, duration, distance) - EaseIn(time - delta, duration, distance),
        _ => 0f,
    };

    static float EaseOut(float t, float d, float dist) => dist * MathF.Sqrt(MathF.Abs(1f - MathF.Pow((t - d) / d, 2f)));
    static float EaseIn(float t, float d, float dist) => dist * (1f - MathF.Sqrt(MathF.Abs(1f - MathF.Pow(t / d, 2f))));

    /// <summary>
    /// PC::vfunc_107 (pc.cpp:4272), the own PC's push from a hit, after its gates (<see cref="HitReaction.PushGate"/>):
    /// the hit's push kind picks the direction (2 toward the record's position, 1 away from it, 5 the record's direction or
    /// the attacker's forward when that is zero, 6 back along the record's hit facing (FUN_140a01250's forward)), the
    /// distance is pushdistance / 100 less the equipment's resistance (<see cref="HitReaction.PushResist"/>) times the scale;
    /// kind 2 stops 150 short of the attacker and kind 4 pulls to the record's position on the ground plane. A distance of
    /// about 0 starts nothing. Returns whether the push started; the record's direction becomes the push's.
    /// STILL-OPEN: kind 3 and the kinds past 6 leave the direction unset (an uninitialised efd::Point3) in the client;
    /// they start nothing here.
    /// </summary>
    /// <param name="me">The actor's position (vfunc_18).</param>
    /// <param name="recordPos">The record's m_ptPos (the packet's position).</param>
    /// <param name="recordDir">The record's m_ptDir: the packet's direction for attackMethodType 1, else zero (the local aim,
    /// FUN_14145e1a0, is STILL-OPEN); set to the push's direction.</param>
    /// <param name="hitRotZ">The record's m_ptHit z: the packet's direction short, a rotation in degrees.</param>
    /// <param name="attacker">The owner's position and forward, when the owner is in the field.</param>
    public bool Start(SkillAttackDamage attack, int kind, Vector3 me, Vector3 recordPos, ref Vector3 recordDir, float hitRotZ,
        (Vector3 Pos, Vector3 Forward)? attacker, int resistLevel, float scale = 1f)
    {
        float speed = HitReaction.FromPercent(attack.PushDistance);
        Vector3 dir;
        switch (kind)
        {
            case 2: dir = recordPos - me; break;
            case 1: dir = me - recordPos; break;
            case 5:
                if (recordDir == Vector3.Zero) dir = attacker?.Forward ?? Vector3.Zero;
                else dir = recordDir;
                break;
            case 6:
                SkillHit.Axes(hitRotZ, out var forward, out _);
                dir = -forward;
                break;
            case 4: dir = Vector3.Zero; break;
            default: return false;
        }
        dir = HitReaction.Unitize(dir);
        recordDir = dir;
        speed = HitReaction.PushResist(speed, resistLevel);
        speed *= scale;
        if (kind == 2)
        {
            if (attacker is { } a) speed = MathF.Min((Vector3.Distance(a.Pos, me) - 150f) * 0.01f, speed);
        }
        else if (kind == 4)
        {
            var d = recordPos - me;
            d.Z = 0f;
            speed = HitReaction.FromPercent(d.Length());
            recordDir = HitReaction.Unitize(d);
        }
        if (HitReaction.NearlyZero(speed)) return false;
        Reset();
        Dir = recordDir;
        Speed = speed;
        Duration = attack.PushDuration;
        Time = 0f;
        Lift = !HitReaction.NearlyZero(attack.PushUpDistance);
        Fall = attack.PushFall;
        Priority = attack.PushPriority;
        SetCurve(attack.PushEaseType);
        return true;
    }

    /// <summary>
    /// CPushControl::vfunc_1 @0x1413fcb30 (hitcontrol.cpp:58-96), run every frame by PCBase::vfunc_53 (pcbase.cpp:3737):
    /// nothing without a direction; a tick distance of about 0 ends the push. A lifting push measures from 1 above the
    /// ground under the feet (RayCastFixedDirection, unlimited). The ledge probe (<see cref="PushTick.LedgeAhead"/>) looks
    /// the tick's distance ahead; with no ledge, a push out of the hold state or pushFall the body moves by the direction
    /// times the distance (the client's metres, field units here), else the push ends. Returns whether a push ran.
    /// </summary>
    public bool Tick(double dt, IPushBody body)
    {
        if (Dir == Vector3.Zero) return false;
        float dist = Update(dt);
        if (HitReaction.NearlyZero(dist))
        {
            Reset();
            return false;
        }
        float up = 0f;
        if (Lift)
        {
            var pos = body.FieldPosition;
            if (body.GroundBelow(pos, 0f, out float hitZ))
            {
                hitZ += 1f;
                if (pos.Z > hitZ) up = -(pos.Z - hitZ);
            }
        }
        bool ledge = PushTick.LedgeAhead(body, Dir, dist * 100f, PushTick.LedgeHeight, up);
        if (!ledge || body.PrevStateId == HitReaction.StateHold || Fall != 0) body.PushMove(Dir * (dist * 100f), dt);
        else Reset();
        return true;
    }
}

/// <summary>StateHitData (pc.cpp:934-942): what PC::vfunc_33 hands StateHit (0xd) on entering it.</summary>
public sealed class StateHitData
{
    /// <summary>m_f18 hitSpeedRate, m_f1c hitPauseTime, m_f20 the push distance after the equipment's resistance, m_f24
    /// pushduration, m_f28 pushUpDistance.</summary>
    public float SpeedRate, PauseTime, Distance, Duration, UpDistance;
    /// <summary>m_n2c pushDown, m_n30 pushPriority.</summary>
    public int Down, Priority;
    /// <summary>m_str: pushAnimation.</summary>
    public string Animation = "";

    public void Clear()
    {
        SpeedRate = PauseTime = Distance = Duration = UpDistance = 0f;
        Down = Priority = 0;
        Animation = "";
    }
}

/// <summary>
/// The own PC's reaction to a hit: PC::vfunc_33 (@0x141652080, pc.cpp:885) for a skill hit, vfunc_35 (:1010) for a splash
/// hit and vfunc_34 (@0x1416529f0, :968) for a dot, as far as the state they change to and the push go.
/// STILL-OPEN, not here: the buffs' hit invoke effects (PlayInvokeEffectsByEvent), the auto taxi cancel
/// (CAutoFindTaxiStation::FUN_1409f1a00), the state's vfunc_6, the color boost, the hit sound, the death hand-off
/// (FUN_141653740) and the DPS meter.
/// </summary>
public static class HitReaction
{
    public const int StateIdle = 1, StateDead = 0xc, StateHit = 0xd, StateSkill = 0x10, StateHold = 0x20, StateGrabTarget = 0x2b,
        State2f = 0x2f;

    /// <summary>IsNearlyEqual(f, 0) (@0x14028d470): |f| &lt; 1e-6.</summary>
    public static bool NearlyZero(float f) => MathF.Abs(f) < 1e-6f;

    /// <summary>FromPercent: f / 100.</summary>
    public static float FromPercent(float f) => f / 100f;

    /// <summary>NiPoint3::Unitize: zero below a length of 1e-6.</summary>
    public static Vector3 Unitize(Vector3 v)
    {
        float len = v.Length();
        return len > 1e-6f ? v * (1f / len) : Vector3.Zero;
    }

    /// <summary>
    /// CEquipment::FUN_1411e0f90 @0x1411e0f90 (equipment.cpp:705): the push resistance of the worn special ability 0x27:
    /// level 1, 2, 3 take 0.5, 1, 1.5 off the distance, a level above 4 takes 2, level 4 nothing; never below 0.
    /// </summary>
    public static float PushResist(float f, int level)
    {
        float l = level;
        float reduce = 0f;
        if (l == 1f) reduce = FromPercent(50f);
        else if (l == 2f) reduce = FromPercent(100f);
        else if (l == 3f) reduce = FromPercent(150f);
        else if (l > 4f) reduce = FromPercent(200f);
        return MathF.Max(0f, f - reduce);
    }

    /// <summary>
    /// AdditionalCliList::GetSuperArmor @0x140970b30 (additionalcli_2.cpp:599): in StateSkill (0x10) the larger of the cast
    /// skill's super armor (CSkillRecord::GetSuperArmor, skillrecord.cpp:412) and the buffs' (<see cref="AdditionalBuffList.SuperArmor"/>);
    /// the buffs' alone in any other state.
    /// </summary>
    public static uint SuperArmor(int state, uint castSuperArmor, uint buffSuperArmor)
    {
        uint v = state == StateSkill ? castSuperArmor : 0;
        return v > buffSuperArmor ? v : buffSuperArmor;
    }

    /// <summary>
    /// AdditionalCliList::FUN_140970c10 @0x140970c10 (additionalcli_2.cpp:622): the super armor holds against the hit (no
    /// reaction). True for a PC whose invincibility timer runs (PC::FUN_140976c10); false for no attack or a
    /// superArmorBreak hit; else by the armor: 1 holds unless the hit pushes or grabs (grabTargetType 1), 2 holds unless it
    /// pushes with a lift or grabs, 3 always holds, 4 holds unless <paramref name="forPush"/> or a grab; no armor never holds.
    /// </summary>
    /// <param name="ground">The field's map type is 9 or 3 (<see cref="SkillAttackDamage.GetPush"/>).</param>
    /// <param name="forPush">nPush: 1 from the push (PC::vfunc_107), 0 from the reaction.</param>
    /// <param name="invincible">PC::FUN_140976c10 (m_n1058, set by PC::FUN_141661370 pc.cpp:3416).</param>
    public static bool SuperArmorHolds(SkillAttackDamage attack, uint armor, bool ground, bool forPush, bool invincible)
    {
        if (invincible) return true;
        if (attack == null) return false;
        if (attack.SuperArmorBreak != 0) return false;
        int push = attack.GetPush(ground);
        switch (armor)
        {
            case 1:
                if (push != 0) return false;
                return attack.GrabTargetType != 1;
            case 2:
                if (push != 0 && !NearlyZero(attack.PushUpDistance)) return false;
                return attack.GrabTargetType != 1;
            case 3:
                return true;
            case 4:
                if (forPush) return false;
                return attack.GrabTargetType != 1;
        }
        return false;
    }

    /// <summary>
    /// PC::vfunc_107's gates (pc.cpp:4274-4291): no push with a scale of about 0, when the super armor holds against a push,
    /// in StateDead (0xc), for a hit of no damage, while held (FUN_141660120), without an attack, when the hit's push kind
    /// is 0 in the field, or while a push of the same or a higher priority runs. Returns the push kind, 0 for none.
    /// </summary>
    public static int PushGate(SkillAttackDamage attack, long totalDamage, int state, bool held, bool armorHolds,
        PushControl running, bool ground, float scale = 1f)
    {
        if (NearlyZero(scale)) return 0;
        if (armorHolds) return 0;
        if (state == StateDead) return 0;
        if (totalDamage == 0) return 0;
        if (held) return 0;
        if (attack == null) return 0;
        int kind = attack.GetPush(ground);
        if (kind == 0) return 0;
        if (running.Running && running.Priority >= attack.PushPriority) return 0;
        return kind;
    }

    /// <summary>What a hit changes the own PC's state to.</summary>
    public enum Change { None, Hit, Idle }

    /// <summary>
    /// PC::vfunc_33 (pc.cpp:897-952) and vfunc_35 (:1020-1064), the state change: the reaction holds unless the super armor
    /// does (<paramref name="armorHolds"/>, nPush 0). In StateGrabTarget (0x2b) the hit pushes when it has a push kind in the
    /// field and does not grab (grabTargetType 1), or when grabTargetType is 2. Elsewhere, a StateHit with a duration
    /// ignores a pushPriorityHitImmune hit of no higher push priority than its own, a grab (FUN_1416538d0) or being held
    /// (FUN_141660120) stops it. A hit of damage (a total below 0) or a push then enters StateHit (0xd) with
    /// <paramref name="data"/> when it reacts and either the state takes hits (State::vfunc_14) with hitImmuneBreak above
    /// 0 (PC::vfunc_64) or it has a push kind in the field; otherwise a push, or state 0x2f for a skill hit, goes to
    /// StateIdle (1).
    /// </summary>
    /// <param name="hitDuration">The current StateHit's HasHitDuration (statehit.cpp:239: its duration is not about 0).</param>
    /// <param name="hitPriority">The current StateHit's GetPushPriority (:245).</param>
    /// <param name="grabbed">PC::FUN_1416538d0 (pc.cpp:2791) started a grab: only for grabTargetType 1 and an attacker with
    /// grab nodes. STILL-OPEN: no actor has grab nodes here, so the callers pass false.</param>
    /// <param name="splash">vfunc_35: state 0x2f does not go to StateIdle.</param>
    public static Change Decide(SkillAttackDamage attack, long totalDamage, int state, bool stateTakesHits, bool hitDuration,
        int hitPriority, bool armorHolds, bool grabbed, bool held, bool ground, int resistLevel, bool splash, StateHitData data)
    {
        bool react = !armorHolds;
        bool push = false;
        if (state == StateGrabTarget)
        {
            if (attack.GetPush(ground) != 0 && attack.GrabTargetType != 1) push = true;
            else if (attack.GrabTargetType == 2) push = true;
        }
        else
        {
            if (state == StateHit && hitDuration && attack.PushPriorityHitImmune != 0 && hitPriority >= attack.PushPriority)
                react = false;
            if (react) react = !grabbed;
            if (react) react = !held;
        }
        if (!(totalDamage < 0 || push)) return Change.None;
        if (react)
        {
            bool pushHit = attack.GetPush(ground) != 0;
            if ((stateTakesHits && attack.HitImmuneBreak > 0) || pushHit)
            {
                data.SpeedRate = attack.HitSpeedRate;
                data.PauseTime = attack.HitPauseTime;
                data.Duration = attack.PushDuration;
                data.Distance = PushResist(attack.PushDistance, resistLevel);
                data.UpDistance = attack.PushUpDistance;
                data.Down = attack.PushDown;
                data.Priority = attack.PushPriority;
                data.Animation = attack.PushAnimation;
                return Change.Hit;
            }
        }
        if (push || (!splash && state == State2f)) return Change.Idle;
        return Change.None;
    }

    /// <summary>
    /// PC::vfunc_33 (pc.cpp:885) for one damage record on the own PC, in its order: the push first (PC::vfunc_107 with a
    /// scale of 1, <see cref="PushGate"/> then <see cref="PushControl.Start"/>), so that StateSkill's vfunc_14 sees it, then
    /// the state change (<see cref="Decide"/>) with the super armor's test for the reaction (nPush 0). Returns the change
    /// the caller makes (StateHit with <paramref name="data"/>, or StateIdle).
    /// STILL-OPEN: the grab (FUN_1416538d0) and being held by a ridee (FUN_141660120) pass false: no actor here has grab
    /// nodes and state 0x20 is not ported.
    /// </summary>
    /// <param name="armor">GetSuperArmor of the own buffs and, in StateSkill, the cast (<see cref="SuperArmor"/>).</param>
    /// <param name="recordPos">The record's m_ptPos (mode 1: the packet's position).</param>
    /// <param name="recordDir">The record's m_ptDir (mode 1: the packet's direction for attackMethodType 1, else zero).</param>
    /// <param name="hitRotZ">The record's m_ptHit z (mode 1: the packet's direction short).</param>
    public static Change OnSkillHit(IHitBody body, SkillAttackDamage attack, long totalDamage, uint armor, bool invincible,
        bool ground, int resistLevel, Vector3 recordPos, Vector3 recordDir, float hitRotZ,
        (Vector3 Pos, Vector3 Forward)? attacker, StateHitData data)
    {
        if (attack == null) return Change.None;   // CDamageRecord::Init failed: no record reaches the PC
        int kind = PushGate(attack, totalDamage, body.ClientStateId, false,
            SuperArmorHolds(attack, armor, ground, true, invincible), body.Push, ground);
        if (kind != 0)
            body.Push.Start(attack, kind, body.FieldPosition, recordPos, ref recordDir, hitRotZ, attacker, resistLevel);
        return Decide(attack, totalDamage, body.ClientStateId, body.StateTakesHits, body.HitHasDuration, body.HitPushPriority,
            SuperArmorHolds(attack, armor, ground, false, invincible), false, false, ground, resistLevel, false, data);
    }

    /// <summary>
    /// PC::vfunc_34 (pc.cpp:972-984), a dot's state change: a dot of damage (a total below 0) enters StateHit (0xd) with no
    /// data when no super armor is on (GetSuperArmor 0), the state takes hits and the record's vfunc_1 is above 0.
    /// </summary>
    /// <param name="dotHitImmuneBreak">CDotDamageRecord::vfunc_1 (skillrecord.cpp:650): from the caster's buff of the record's
    /// effect. STILL-OPEN: the caster's buff list is not read, so the callers pass 0.</param>
    public static bool DotEntersHit(long totalDamage, uint armor, bool stateTakesHits, int dotHitImmuneBreak) =>
        totalDamage < 0 && armor == 0 && stateTakesHits && dotHitImmuneBreak > 0;
}

/// <summary>What a hit reaction reads of the body it lands on (the own PC).</summary>
public interface IHitBody
{
    /// <summary>GetCurStateId: the client state id (StateSkill 0x10 while the cast holds the body).</summary>
    int ClientStateId { get; }
    /// <summary>State::vfunc_14 of the current state, read after the push started.</summary>
    bool StateTakesHits { get; }
    /// <summary>StateHit::HasHitDuration; false outside StateHit.</summary>
    bool HitHasDuration { get; }
    /// <summary>StateHit::GetPushPriority; 0 outside StateHit.</summary>
    int HitPushPriority { get; }
    /// <summary>PCBase m_pushControl.</summary>
    PushControl Push { get; }
    /// <summary>vfunc_18: the position in the field's z-up coordinates.</summary>
    Vector3 FieldPosition { get; }
}
