using Godot;
using Ms2.Ui.Hud;

namespace Ms2Godot;

/// <summary>
/// Ticks the process frame rate meter (<see cref="FrameRateMeter.Process"/>) every frame from start-up (autoload), as
/// CMainSystem::OnTick counts frames whatever the scene (mainsystem.cpp:540-561). It lives across scene changes, so the
/// frame rate dialog opened at game entry shows the rate already counted at login and through the load. It runs while
/// paused and first in the frame, before the scene reads the rate.
/// </summary>
public partial class FrameRateClock : Node
{
    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        ProcessPriority = int.MinValue;
    }

    public override void _Process(double delta) => FrameRateMeter.Process.Tick(Time.GetTicksUsec() / 1_000_000.0);
}
