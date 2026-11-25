using Godot;
using Ms2Godot.Ui;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/hud_once.tscn): the HUD (res://ui/hud.tscn) opened with a session (a LiveSession without a
/// connection, as the instance) makes one HUD model, so each live event its player watch subscribes to (the own character's
/// Changed, field property 5's UserTagSymbolApplied, OwnRideChanged, the key table's KeysSentToServer) reaches the HUD once
/// per model it keeps, no handler holds a model the HUD dropped, and the dialogs built with the model use the HUD's
/// (<see cref="HudScene.LiveWatchScripted"/>). Quits with 0 when all of that holds, else 1.
/// </summary>
public partial class HudOnceCheck : Node
{
    public override void _Ready()
    {
        GetTree().Root.Size = new Vector2I(1280, 720);   // a headless window is 64x64
        typeof(Net.LiveSession).GetProperty("Instance")!.SetValue(null, new Net.LiveSession());
        GetTree().Root.CallDeferred(Node.MethodName.AddChild, new HudOnceDriver());
        GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, "res://ui/hud.tscn");
    }
}

/// <summary>The check's driver, at the tree root so it outlives the scene change to the HUD.</summary>
public partial class HudOnceDriver : Node
{
    int wait;
    readonly ulong startMs = Time.GetTicksMsec();

    void Finish(bool ok, string what)
    {
        if (Net.LiveSession.Instance is { } s && !s.IsInsideTree())
        {
            typeof(Net.LiveSession).GetProperty("Instance")!.SetValue(null, null);
            s.Free();
        }
        GD.Print("hud once: " + (ok ? "OK " : "FAIL ") + what);
        GetTree().Quit(ok ? 0 : 1);
        SetProcess(false);
    }

    public override void _Process(double delta)
    {
        if (Time.GetTicksMsec() - startMs > 300_000) { Finish(false, "timed out before the HUD loaded"); return; }
        if (GetTree().CurrentScene is not HudScene hud || !Field.LoadComplete) return;
        if (++wait < 30) return;
        var counts = hud.LiveWatchScripted(out bool once);
        Finish(once, counts);
    }
}
