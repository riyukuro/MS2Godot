using Godot;

namespace Ms2Godot.Movement;

/// <summary>
/// Keyboard state for the local player, by the client's key actions (guideentity: moveUp 0x30d41 ... jump 0x493e1,
/// walkPress 0x493ea). Default keys from table/default/defaultkey.xml: arrows move, C and Alt jump, comma holds walk.
/// WASD and Shift are kept as extra bindings.
/// </summary>
public static class MoveInput
{
    /// <summary>While a text field has focus (chat), or under a field move's loading screen (Ms2.Ui.FieldMoveCover),
    /// movement and jump keys are ignored.</summary>
    public static Func<bool> Blocked = () => Ui.UiFocus.IsTyping || Ms2.Ui.FieldMoveCover.Up;

    public struct State
    {
        public Vector2 Move;      // x = right, y = forward (camera relative), each -1 / 0 / 1
        public bool Jump;         // held
        public bool Walk;         // walkPress held
    }

    /// <summary>gamepad/bGamePadUse (default off; the client also needs feature 0xb5).</summary>
    public static bool GamePad;
    static Vector2 lastStick;

    public static State Read()
    {
        var s = new State();
        if (Blocked()) return s;
        if (GamePad && ReadPad(ref s)) return s;
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

    /// <summary>
    /// The pad (device 0) when its stick is off centre: the left stick's direction, analog and unit length (the client
    /// reads it with no dead zone; Godot's 0.2 keeps a resting stick still); a stick whose sign flips gives no direction
    /// for that frame. At GamePadStickMoveValue percent or less it walks (feature 0xd6). The bottom face button jumps
    /// (defaultxboxkey 300001 -> NIGP_RDOWN). The stick replaces the keyboard.
    /// </summary>
    static bool ReadPad(ref State s)
    {
        var stick = new Vector2(Input.GetJoyAxis(0, JoyAxis.LeftX), -Input.GetJoyAxis(0, JoyAxis.LeftY));
        bool jump = Input.IsJoyButtonPressed(0, JoyButton.A);
        if (stick.Length() < 0.2f) { lastStick = Vector2.Zero; s.Jump |= jump; return jump; }
        bool flipped = (lastStick.X * stick.X < 0) || (lastStick.Y * stick.Y < 0);
        lastStick = stick;
        float pct = Mathf.Max(Mathf.Abs(stick.X), Mathf.Abs(stick.Y)) * 100f;
        s.Move = flipped ? Vector2.Zero : stick.Normalized();
        s.Walk = pct <= MoveConfig.GamePadStickMoveValue;
        s.Jump = jump || Input.IsPhysicalKeyPressed(Key.C) || Input.IsPhysicalKeyPressed(Key.Alt);
        return true;
    }
}
