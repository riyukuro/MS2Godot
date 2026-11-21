using System.Reflection;
using Godot;
using Ms2.Net.Packets;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/time_scale_exit.tscn) that a time scale change (OnTimeScale, StartTimeScale) ends with the game
/// scene state on every way out, as the client's WorldService goes with it:
/// - a ramp past its end with no game link left: the session's frame still ends it (scale 1);
/// - GameToLogin (back to character select), SendRequestQuitAndDisconnect (the quit) and the session leaving the tree
///   each reset the scale to 1 at once;
/// - a scale of 0 keeps the engine at <see cref="GameClock.MinEngineScale"/>, and <see cref="GameClock.Real"/> gives the real
///   delta back, so the UI does not freeze;
/// - in the frame of a change the real delta divides by the scale the frame's delta was scaled by (the one in force at the
///   frame's start), so 1 -> 0 gives no 16 s spike, and the next frame divides by the new one;
/// - a HUD tween (NativeUi.EaseOut, the NWindow open and close) runs on the real clock under scale 0;
/// - a one-time effect's remove drops the logged adds of its id and is not logged itself.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class TimeScaleExitCheck : Node
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    public override async void _Ready()
    {
        var fails = new System.Collections.Generic.List<string>();
        void Check(bool ok, string what) { if (!ok) fails.Add(what); }
        var ls = new LiveSession();
        AddChild(ls);
        ((System.Threading.CancellationTokenSource)typeof(LiveSession).GetField("exitCts", Private).GetValue(ls)).Cancel();

        void Slow(uint durationMs)
        {
            ls.TimeScaleRamp.Start(0.3f, 1f, durationMs, 0, (uint)Time.GetTicksMsec());
            GameClock.SetScale(ls.TimeScaleRamp.Scale);
        }

        Slow(5000);
        Check(Engine.TimeScale == 0.3f, $"scale {Engine.TimeScale} after the start");
        Slow(1);
        OS.DelayMsec(5);
        ls._Process(0.016);
        Check(ls.TimeScaleRamp.Running, "a second start while one runs replaced it");
        ls.TimeScaleRamp.Stop();
        GameClock.SetScale(1);
        Slow(1);
        OS.DelayMsec(5);
        ls._Process(0.016);
        Check(!ls.TimeScaleRamp.Running && Engine.TimeScale == 1.0, $"the frame without a game link left scale {Engine.TimeScale}");

        Slow(5000);
        typeof(LiveSession).GetMethod("OnGameToLogin", Private).Invoke(ls, new object[] { new GameToLogin(0, new byte[] { 127, 0, 0, 1 }, 0, 0, 0) });
        Check(!ls.TimeScaleRamp.Running && Engine.TimeScale == 1.0, $"GameToLogin left scale {Engine.TimeScale}");

        Slow(5000);
        ls.SendRequestQuitAndDisconnect();
        Check(!ls.TimeScaleRamp.Running && Engine.TimeScale == 1.0, $"the quit left scale {Engine.TimeScale}");

        var log = typeof(LiveSession).GetMethod("LogOneTime", Private);
        foreach (var m in new[] { new OneTimeEffect(1, true, "a"), new OneTimeEffect(2, true, "b"), new OneTimeEffect(1, true, "c"), new OneTimeEffect(1, false, "") })
            log.Invoke(ls, new object[] { m });
        Check(ls.OneTimeEffectLog.Count == 1 && ls.OneTimeEffectLog[0].Id == 2, $"one-time log {ls.OneTimeEffectLog.Count} after a remove of id 1");

        Slow(5000);
        RemoveChild(ls);
        Check(!ls.TimeScaleRamp.Running && Engine.TimeScale == 1.0, $"leaving the tree left scale {Engine.TimeScale}");
        ls.Free();

        bool Near(double got) => System.Math.Abs(got - 0.016) < 1e-6;
        var tree = GetTree();
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        GameClock.SetScale(1);
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        GameClock.SetScale(0);
        Check(Engine.TimeScale == GameClock.MinEngineScale, $"scale 0 set the engine to {Engine.TimeScale}");
        Check(Near(GameClock.Real(0.016)), $"the frame of 1 -> 0 gives a real delta of {GameClock.Real(0.016)} s for a 0.016 s frame");
        GameClock.SetScale(0.5f);
        GameClock.SetScale(0);
        Check(Near(GameClock.Real(0.016)), "a second change in the frame moved the frame's scale");
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        Check(Near(GameClock.Real(0.016 * Engine.TimeScale)), "the real delta under scale 0 is not the frame's");
        GameClock.SetScale(1);
        Check(Near(GameClock.Real(0.016 * GameClock.MinEngineScale)), $"the frame of 0 -> 1 gives a real delta of {GameClock.Real(0.016 * GameClock.MinEngineScale)} s");
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        Check(Near(GameClock.Real(0.016)), "the frame after 0 -> 1 is not divided by 1");
        GameClock.SetScale(-2);
        Check(Engine.TimeScale == GameClock.MinEngineScale, $"scale -2 set the engine to {Engine.TimeScale}");

        // a HUD tween under the floor scale: 0.05 s of real time, not 50 s
        var box = new Godot.Control();
        AddChild(box);
        Ms2Godot.Ui.Native.NativeUi.EaseOut(box).TweenProperty(box, "modulate:a", 0f, 0.05);
        ulong t0 = Time.GetTicksMsec();
        while (Time.GetTicksMsec() - t0 < 300) await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        Check(box.Modulate.A == 0f, $"the eased tween under scale 0 reached alpha {box.Modulate.A} after 0.3 s");
        GameClock.SetScale(1);

        GD.Print($"time scale exit: {(fails.Count == 0 ? "ok" : "FAIL " + string.Join("; ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
