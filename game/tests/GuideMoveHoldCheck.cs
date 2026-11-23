using Godot;
using Ms2.Ui.Guide;
using Ms2Godot.Movement;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/guide_move_hold.tscn): the guide's move limits hold the own player's keys as
/// Unk_PCMove does. GetMoveDirection (@0x141683290, unit_141682f80.cpp:313-317) gives no direction while PlayerControl
/// or PlayerMove holds, and the jump key reads through FUN_1416840c0 (:528-536), which IsSkillRestricted
/// (@0x14138c930, guideentity_1b.cpp:547-556) stops under PlayerControl only. With Up and C held it reads the keys
/// with no guide, under PlayerMove and under PlayerControl, then with both limits removed again.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class GuideMoveHoldCheck : Node
{
    int frames, stage;
    GuideManager guide;
    readonly System.Collections.Generic.List<string> failures = new();

    public override void _Ready()
    {
        guide = new GuideManager(new GuideWorld());
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Up, PhysicalKeycode = Key.Up, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.C, PhysicalKeycode = Key.C, Pressed = true });
    }

    void Expect(string when, bool move, bool jump)
    {
        var s = MoveInput.Read();
        bool gotMove = s.Move != Vector2.Zero;
        if (gotMove != move || s.Jump != jump || MoveInput.GuideHoldsMove() == move)
            failures.Add($"{when}: move {s.Move}, jump {s.Jump}, held {MoveInput.GuideHoldsMove()} (want move {move}, jump {jump})");
    }

    public override void _Process(double delta)
    {
        if (++frames < 5) return;
        var keep = GuideManager.Current;
        try
        {
            GuideManager.Current = null;
            Expect("no guide", true, true);
            GuideManager.Current = guide;
            Expect("no limit", true, true);
            guide.AddLimit(GuideLimit.PlayerMove);
            Expect("PlayerMove", false, true);
            guide.AddLimit(GuideLimit.PlayerControl);
            Expect("PlayerMove and PlayerControl", false, false);
            guide.RemoveLimit(GuideLimit.PlayerMove);
            Expect("PlayerControl", false, false);
            guide.RemoveLimit(GuideLimit.PlayerControl);
            Expect("limits removed", true, true);
        }
        finally { GuideManager.Current = keep; }
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Up, PhysicalKeycode = Key.Up, Pressed = false });
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.C, PhysicalKeycode = Key.C, Pressed = false });
        SetProcess(false);
        bool ok = failures.Count == 0;
        GD.Print($"guide move hold: {(ok ? "ok" : "FAIL")}{(ok ? "" : ": " + string.Join("; ", failures))}");
        GetTree().Quit(ok ? 0 : 1);
    }
}
