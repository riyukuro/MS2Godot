using Godot;

namespace Ms2Godot.Movement;

/// <summary>
/// Keyboard state for the local player, by the client's key actions (guideentity: moveUp 0x30d41 ... jump 0x493e1,
/// walkPress 0x493ea). Default keys from table/default/defaultkey.xml: arrows move, C and Alt jump, comma holds walk.
/// Nothing else moves: in that table W, A, S and D are skill slot keys (500002, 500005, 500006, 500007) and Shift is
/// action 300038, so a skill on one of them is not also a move (a move held in the skill's input window would cancel it at
/// "combo", <see cref="Ms2.Ui.Skills.SkillSwingCancel"/>).
/// </summary>
public static class MoveInput
{
    /// <summary>
    /// Unk_PCMove reads the own player's keys (IsKeyDown @0x141682f80, IsActionKeyDown @0x141682fe0) through
    /// FUN_141683ef0 (@0x141683ef0), which gives nothing while CInputKeyHandler::FUN_14116d7a0(0xf) holds: while a text
    /// field has the focus (a movie's or an engine control's) or under a field move's loading screen
    /// (<see cref="Ms2.Ui.InputBlock"/>) the movement and jump keys are ignored.
    /// </summary>
    public static Func<bool> Blocked = () => Ui.UiFocus.IsTyping || Ms2.Ui.InputBlock.Active;

    public struct State
    {
        public Vector2 Move;      // x = right, y = forward (camera relative), each -1 / 0 / 1
        public bool Jump;         // held
        public bool Walk;         // walkPress held
    }

    /// <summary>gamepad/bGamePadUse (default off; the client also needs feature 0xb5).</summary>
    public static bool GamePad;
    static Vector2 lastStick;

    /// <summary>
    /// Unk_PCMove::GetMoveDirection (@0x141683290, unit_141682f80.cpp:313-317) gives no direction while the guide holds
    /// PlayerControl or PlayerMove (CGuideManager::IsRestricted 0x15 / 0x14): no key, pad or click move.
    /// </summary>
    public static bool GuideHoldsMove() => Ms2.Ui.Guide.GuideManager.Current is { } g
        && (g.IsRestricted(Ms2.Ui.Guide.GuideLimit.PlayerControl) || g.IsRestricted(Ms2.Ui.Guide.GuideLimit.PlayerMove));

    /// <summary>The jump action (0x493e1) reads through Unk_PCMove::FUN_1416840c0 (unit_141682f80.cpp:528-536), which
    /// CGuideManager::IsSkillRestricted (@0x14138c930, guideentity_1b.cpp:547-556) stops: PlayerControl holds it,
    /// PlayerMove holds only the move actions.</summary>
    static bool GuideHoldsJump() => Ms2.Ui.Guide.GuideManager.Current?.IsSkillRestricted(JumpAction) == true;

    const int JumpAction = 0x493e1;

    public static State Read()
    {
        var s = new State();
        if (Blocked()) return s;
        if (GamePad && ReadPad(ref s)) return Hold(s);
        static bool K(Key k) => Input.IsPhysicalKeyPressed(k);
        // Unk_PCMove::GetMoveDirection: left wins over right, up over down
        if (K(Key.Left)) s.Move.X = -1;
        else if (K(Key.Right)) s.Move.X = 1;
        if (K(Key.Up)) s.Move.Y = 1;
        else if (K(Key.Down)) s.Move.Y = -1;
        s.Jump = K(Key.C) || K(Key.Alt);
        s.Walk = K(Key.Comma);
        return Hold(s);
    }

    /// <summary>The guide's holds over a read (GuideHoldsMove, GuideHoldsJump).</summary>
    static State Hold(State s)
    {
        if (GuideHoldsMove()) s.Move = Vector2.Zero;
        if (s.Jump && GuideHoldsJump()) s.Jump = false;
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
