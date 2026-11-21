using Godot;

namespace Ms2Godot;

/// <summary>
/// CMainSystem::SetTimeScale / ResetTimeScale (mainsystem.cpp:218) scale the game clock alone (GameTimeClock); the UI and the
/// network run on the real one. Here the scale drives Engine.TimeScale, which scales every node's delta, so the UI and the
/// network timers that run in it take their real delta back (<see cref="Real"/>, SceneTreeTimer's ignore_time_scale).
/// Engine.TimeScale never reaches 0 (<see cref="MinEngineScale"/>): a scale of 0 from the server stops the world for the
/// eye while the UI, which divides by it, keeps running.
/// STILL-OPEN: the world is not a clock of its own, so the scale also reaches the timers that may run on the real clock in
/// the client: FieldAudio's sound delay, EquipApplier's effect delays, RegionSkills' fade timers, PlayerHpBar's timer,
/// DamageText and DamageEffect.
/// </summary>
public static class GameClock
{
    /// <summary>The smallest Engine.TimeScale a scale sets.</summary>
    public const float MinEngineScale = 0.001f;

    /// <summary>The game clock's scale, as Engine.TimeScale (at least <see cref="MinEngineScale"/>).</summary>
    public static void SetScale(float scale) => Engine.TimeScale = scale > MinEngineScale ? scale : MinEngineScale;

    /// <summary>The real time behind a node's scaled delta.</summary>
    public static double Real(double delta) => delta / Engine.TimeScale;
}
