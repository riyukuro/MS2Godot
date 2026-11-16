using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2Godot.Movement;

/// <summary>
/// Local player movement numbers, as the restored client sets them. Lengths are game units (1 unit = 1 cm); the client
/// keeps PhysX in metres (FromPercent = x0.01, ToPercent = x100), so a 3.5 speed there is 350 units/s here.
/// The README's movement table names the client source of each value.
/// </summary>
public static class MoveConfig
{
    // GameObjectController::CreateController (capsule controller) and PCBase (m_unk_620 = radius 25, height 105)
    public const float Radius = 25f;
    public const float Height = 105f;            // full height; the PhysX capsule part is Height - 2 * Radius
    public const float SlopeLimitDeg = 50f;      // slopeLimit = cos(0.87266463 rad)
    public const float StepOffset = 30f;         // stepOffset = 0.3 m
    public const float SkinWidth = 1f;           // skinWidth = 0.01 m

    // PC ctor: m_unk_180 / m_unk_190 = (turn 1800, walk 1.2, run 3.5, swim 3.5) m/s
    public const float WalkSpeed = 120f;
    public const float RunSpeed = 350f;
    public const float SwimSpeed = 350f;
    public const float TurnSpeedDeg = 1800f;     // PCBase::TurnTowardTargetYaw, degrees per second

    // MS2PhysXScene Gravity default (0, 0, -40) m/s^2; a field's MS2PhysXScene entity can override it
    public const float SceneGravity = -4000f;
    /// <summary>The field's MS2PhysXScene Gravity z (m/s^2, flat default -40) in units/s^2.</summary>
    public static float FieldGravity(FieldMap map)
    {
        var scene = map.OfModel("MS2PhysXScene").FirstOrDefault();
        double z = scene?.Vector("Gravity").Z ?? 0;
        return z < 0 ? (float)(z * 100) : SceneGravity;
    }

    // GameObjectController s_fDefaultGravity (m/s^2), used only for the state delays below
    public const float DefaultGravityM = -34.602074f;

    // StartJump: v0 = sqrt(2 * (2 * h) * -g), so the apex is 2 h; h = m_f930 (1.0) * JumpHeight stat / 100 (m)
    public const float JumpHeightStat = 100f;
    public const float JumpParam = 100f * JumpHeightStat / 100f;   // h in units (1.0 m)
    // Client::Constants (table/constants.xml, defaults from the client's Define calls), read by LoadConstants
    public static float FallMoveSpeed = 1.0f;     // air speed scale in StateJump / StateFall
    public static float ClimbVelocityV = 3.0f;    // StateClimb up / down, m/s
    public static float ClimbVelocityH = 1.0f;    // StateClimb sideways, m/s
    public static int GamePadStickMoveValue = 50; // stick percent at or below which the pad walks

    /// <summary>Reads the movement values of table/constants.xml (key / value rows).</summary>
    public static void LoadConstants(PackSet packs)
    {
        try
        {
            var x = XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", "table/constants.xml")));
            foreach (var v in x.Root?.Elements("v") ?? Enumerable.Empty<XElement>())
            {
                string k = (string)v.Attribute("key"), val = (string)v.Attribute("value");
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                switch (k)
                {
                    case "FallMoveSpeed": float.TryParse(val, System.Globalization.NumberStyles.Float, ci, out FallMoveSpeed); break;
                    case "ClimbVelocityV": float.TryParse(val, System.Globalization.NumberStyles.Float, ci, out ClimbVelocityV); break;
                    case "ClimbVelocityH": float.TryParse(val, System.Globalization.NumberStyles.Float, ci, out ClimbVelocityH); break;
                    case "GamePadStickMoveValue": int.TryParse(val, out GamePadStickMoveValue); break;
                }
            }
        }
        catch (Exception ex) { Godot.GD.PushWarning("constants: " + ex.Message); }
    }
    // StateJump: a standing jump steered in the air moves at base speed 1.0 m/s
    public const float AirSteerSpeed = 100f;

    // StateWalk / StateIdle: in the air for longer than sqrt(3 / |g|) s (Idle: 100 ms) and more than 0.3 m above
    // the ground (PCBase::vfunc_99) -> StateFall
    public static readonly float WalkFallDelay = MathF.Round(MathF.Sqrt(3f / -DefaultGravityM) * 1000f) / 1000f;
    public const float IdleFallDelay = 0.1f;
    public const float FallMinHeight = 30f;
    // StateJump::vfunc_9: the fall animation after sqrt(12 / |g|) s + the apex time
    public static readonly float JumpFallAnimDelay = MathF.Round(MathF.Sqrt(12f / -DefaultGravityM) * 1000f) / 1000f;

    // PC::FUN_141659b80: ladder / rope search
    public const float LadderReach = 50f, LadderReachDown = 70f, LadderProbeUp = 90f, LadderProbeDown = 75f, LadderSpan = 150f;
    // StateLadder
    public const float LadderStep = 75f;         // per climb cycle
    public const float LadderStepTime = 0.2f;    // s / animation rate, from the "begin" text key
    public const float LadderTopProbe = 75f;     // FUN_1417d99e0: ray down from 75 ahead, 500 up
    public const float LadderTopReach = 150f;
    public const float LadderTakeUpTime = 0.15f, LadderTakeDownTime = 0.2f, LadderTakeAirTime = 0.1f, LadderLandTime = 0.2f;

    // StateRope: hands 50 above the rope base, 20 from its axis; 150 per climb cycle over 400 ms / rate from "begin";
    // left / right turns about the rope at 2 rad/s once the key is held 500 ms; up / down keys after 100 ms
    public const float RopeGrip = 50f, RopeRadius = 20f, RopeStep = 150f, RopeStepTime = 0.4f, RopeTurnRate = 2f;
    public const float RopeTakeTime = 0.2f, RopeKeyDelay = 0.1f, RopeTurnDelay = 0.5f, RopeNextUp = 150f, RopeNextDown = 90f;

    // StateDash: 20 m/s along the facing, 600 before it only waits for the ground, slowed to 0 by the landing's "stop" key
    public const float DashSpeed = 2000f, DashDistance = 600f, DashMinStep = 2f;

    // StateClimb (cube walls): rays 50 long at feet + 54 / 90 / 99; the over-the-top move is (150 + 20) up and 105
    // forward per second; the side probe is 150 * 0.2 to the side; the jump-in snap reaches 25 + 3 + 30
    public const float ClimbRay = 50f, ClimbLow = 54f, ClimbHigh = 90f, ClimbTopRay = 99f, ClimbOverUp = 170f, ClimbOverFwd = 105f;
    public const float ClimbSide = 30f, ClimbSnapReach = 58f, ClimbSnapGap = 28f, ClimbHangCheck = 0.1f, ClimbInputDelay = 0.1f;
    public const float CubeSize = 150f;

    // CMouseController: arrival when the distance grows and is under 75, give up after 15 of growth; the obstacle
    // ray (75 up, 50 ahead) every 100 ms
    public const float MouseArrive = 75f, MouseGiveUp = 15f, MouseProbeUp = 75f, MouseProbeLen = 50f, MouseProbeEvery = 0.1f;

    // PCBase::FUN_141675ed0: standing on an entity whose PropCollisionGroup is 8 = water (swim state)
    public const int WaterGroup = 8;
    // Unk_PCMove::UpdateMoveDirection: a diagonal is kept for 50 ms after one of its keys is released
    public const float DiagonalHold = 0.05f;
}
