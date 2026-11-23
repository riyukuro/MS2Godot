using System.Reflection;
using Godot;
using Ms2Godot.Flow;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/live_return_wiring.tscn): a live session attached by the launcher's Connect (after the flow
/// started, GameFlow.OnLauncherConnect) gets the way back to character select wired, as one attached at start does: its
/// ReturnedToCharacterSelect (GameToLogin's MoveResult) and CharacterListReturned have the flow's handlers. Without them the
/// system menu's character select sent RequestQuit and the game stayed on screen. Nothing connects: the server address is one
/// nobody listens on, and the session is freed before the end. Quits with 0 when both are wired, else 1.
/// </summary>
public partial class LiveReturnWiringCheck : Node
{
    public override void _Ready()
    {
        GetTree().Root.CallDeferred(Node.MethodName.AddChild, new LiveReturnWiringDriver());
        GameFlow.Start(GetTree());
    }
}

/// <summary>The check's driver, at the tree root so it outlives the flow's scene change.</summary>
public partial class LiveReturnWiringDriver : Node
{
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    int frames;

    public override void _Process(double delta)
    {
        if (++frames != 5) return;
        var flow = GetTree().Root.GetNodeOrNull<GameFlow>("GameFlow");
        string fail = null;
        if (flow == null) fail = "no flow";
        else if (Net.LiveSession.Instance != null) fail = "a live session before the launcher's Connect";
        else
        {
            // the launcher page is the state the Connect is taken in
            typeof(GameFlow).GetProperty(nameof(GameFlow.Current))!.SetValue(flow, GameFlow.State.Launcher);
            typeof(GameFlow).GetMethod("OnLauncherConnect", Any)!.Invoke(flow, new object[] { "127.0.0.1", 9 });
            var live = Net.LiveSession.Instance;
            if (live == null) fail = "the Connect attached no session";
            else
            {
                foreach (var name in new[] { nameof(Net.LiveSession.ReturnedToCharacterSelect), nameof(Net.LiveSession.CharacterListReturned) })
                    if (typeof(Net.LiveSession).GetField(name, Any)?.GetValue(live) == null) fail = (fail == null ? "" : fail + "; ") + name + " has no handler";
            }
        }
        GD.Print("live return wiring: " + (fail == null ? "ok" : "FAIL " + fail));
        if (Net.LiveSession.Instance is { } s)
        {
            Net.LiveSession.ClearServer();
            typeof(Net.LiveSession).GetProperty("Instance")!.SetValue(null, null);
            s.QueueFree();
        }
        GetTree().Quit(fail == null ? 0 : 1);
        SetProcess(false);
    }
}
