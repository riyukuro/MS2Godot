using Godot;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Net;

/// <summary>The wheel in cube mode lifts or lowers the craft (<see cref="CubeCraftState.LiftStep"/>).</summary>
public partial class LiveSession
{
    /// <summary>ClientConstants CubeLiftHeightLimitUp / CubeLiftHeightLimitDown (2 each when the tables do not set them;
    /// unit_1402ab440_constantsblocks.cpp:901-902).</summary>
    (int Up, int Down)? liftLimits;
    (int Up, int Down) LiftLimits => liftLimits ??=
        (Constants()?.Int("CubeLiftHeightLimitUp", 2) ?? 2, Constants()?.Int("CubeLiftHeightLimitDown", 2) ?? 2);

    /// <summary>UGCKeyController::vfunc_6 (uiugccubepalettedialog.cpp:2207): the wheel, while the own pc is in cube mode and
    /// the event reached the field (no dialog took it, FUN_1422aeed0(2) == 0). The camera zoom still reads the wheel.</summary>
    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { Pressed: true } mb || !CubeCraft.InCubeMode) return;
        if (mb.ButtonIndex is not (MouseButton.WheelUp or MouseButton.WheelDown)) return;
        var (up, down) = LiftLimits;
        int refused = CubeCraft.LiftStep(mb.ButtonIndex == MouseButton.WheelUp, up, down);
        GD.Print($"live: cube lift {(mb.ButtonIndex == MouseButton.WheelUp ? "up" : "down")}: {(refused == 0 ? $"lift {CubeCraft.Current?.Lift}" : $"refused (notice 0x{refused:x})")}");
        if (refused == 0) return;
        string text = MessageBoxData.Common(refused);      // FUN_141275260(id, 1, 1)
        if (!string.IsNullOrEmpty(text)) SystemNoticeSink?.Invoke(text);
    }
}
