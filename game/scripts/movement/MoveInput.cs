using Godot;

namespace Ms2Godot.Movement;

/// <summary>
/// Keyboard state for the local player, by the client's key actions (guideentity: moveUp 0x30d41 ... jump 0x493e1,
/// walkPress 0x493ea). Default keys from table/default/defaultkey.xml: arrows move, C and Alt jump, comma holds walk.
/// WASD and Shift are kept as extra bindings.
/// </summary>
public static class MoveInput
{
    /// <summary>While a text field has focus (chat) movement and jump keys are ignored.</summary>
    public static Func<bool> Blocked = () => Ui.UiFocus.IsTyping;

    public struct State
    {
        public Vector2 Move;      // x = right, y = forward (camera relative), each -1 / 0 / 1
        public bool Jump;         // held
        public bool Walk;         // walkPress held
    }

    public static State Read()
    {
        var s = new State();
        if (Blocked()) return s;
        static bool K(Key k) => Input.IsPhysicalKeyPressed(k);
        // Unk_PCMove::GetMoveDirection: left wins over right, up over down
        if (K(Key.Left) || K(Key.A)) s.Move.X = -1;
        else if (K(Key.Right) || K(Key.D)) s.Move.X = 1;
        if (K(Key.Up) || K(Key.W)) s.Move.Y = 1;
        else if (K(Key.Down) || K(Key.S)) s.Move.Y = -1;
        s.Jump = K(Key.C) || K(Key.Alt);
        s.Walk = K(Key.Comma) || K(Key.Shift);
        return s;
    }
}
