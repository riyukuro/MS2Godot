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
/// DamageText and DamageEffect. Tweens: CharacterListScene's (the character select, where no game scale is in force).
/// </summary>
public static class GameClock
{
    /// <summary>The smallest Engine.TimeScale a scale sets.</summary>
    public const float MinEngineScale = 0.001f;

    static float scale = 1f;        // Engine.TimeScale as the last SetScale left it
    static float frameScale = 1f;   // the scale in force at the start of changeFrame: that frame's deltas were scaled by it
    static ulong changeFrame = ulong.MaxValue;

    /// <summary>The game clock's scale, as Engine.TimeScale (at least <see cref="MinEngineScale"/>). The engine scales a frame's
    /// delta at the frame's start, so the nodes that run later in the frame of a change still get the old scale's delta.</summary>
    public static void SetScale(float value)
    {
        ulong frame = Engine.GetProcessFrames();
        if (frame != changeFrame) { frameScale = scale; changeFrame = frame; }
        scale = value > MinEngineScale ? value : MinEngineScale;
        Engine.TimeScale = scale;
    }

    /// <summary>The real time behind a node's scaled delta: divided by the scale the frame's delta was scaled by.</summary>
    public static double Real(double delta) => delta / (Engine.GetProcessFrames() == changeFrame ? frameScale : scale);
}
