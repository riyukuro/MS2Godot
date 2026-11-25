namespace Ms2Godot.Net;

public partial class LiveSession
{
    /// <summary>
    /// Unk_PCMove::FUN_141683ef0 for the own pc (<see cref="Ms2.Ui.MoveGate"/>): living, no held input (a text field, the
    /// loading screen), no state flag, not stunned (the own buff stun), not in cube mode 1. The action key's taxi, hold and
    /// hold pc polls ask it. False without the own pc in the field.
    /// STILL-OPEN: the move and jump keys still read only the input part (<see cref="Movement.MoveInput.Blocked"/>); the
    /// cube craft drives the move in cube mode, where the full gate is closed.
    /// </summary>
    public bool MoveGatePasses() =>
        player != null && IsInstanceValid(player)
        && Ms2.Ui.MoveGate.Passes(player.DeadFlag || Me.Dead, Movement.MoveInput.Blocked(), player.StateFlags,
                                  OwnBuffStun() != null, CubeCraft.CubeMode);
}
