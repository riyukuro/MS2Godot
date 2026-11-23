using System.Collections.Generic;
using System.Reflection;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui.Guide;
using Ms2Godot.Movement;
using Act = Ms2Godot.Movement.ActionAnims.Act;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/player_trigger_emotion.tscn) of the own pc's StateTriggerEmotion (0x35,
/// PlayerTriggerEmotion.cs), entered as the trigger packet's guide subs 7 and 8 enter it:
/// - a list (sub 7) plays its first name at once, the next when it ends, then goes back to idle; the move keys do not move
///   the pc meanwhile, and its sync records carry state 0x35;
/// - a list while the state runs is ignored (bForce 0); a timed one (sub 8, bForce 1) restarts it and replays its name until
///   the duration has passed;
/// - a dead pc does not enter it.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class PlayerTriggerEmotionCheck : Node3D
{
    readonly NxController.FaceGrids faceGrids = new();
    readonly List<string> fails = new();
    readonly List<StateSync> sent = new();
    Player pc;
    MethodInfo step;
    int frames;
    const float Dt = 1f / 60;

    public override void _Ready()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        builder.TextureArchives = builder.TextureArchives.Prepend(ModelBuilder.ModelArchive).ToArray();
        var sb = new StaticBody3D { Position = new Vector3(0, -50, 0) };
        sb.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(4000, 100, 4000) } });
        AddChild(sb);
        var (visual, anim, md, bodyDir) = Ms2Godot.Look.PlayerLook.Build(builder, packs);
        pc = Player.Create(visual, anim, faceGrids);
        pc.Anims = ActionAnims.Load(packs, builder, md, anim, bodyDir);
        AddChild(pc);
        pc.SetProcess(false);
        pc.SetPhysicsProcess(false);
        pc.ScriptDriven = true;
        pc.Live = true;
        pc.BoundsMin = new Vector3(-2000, -2000, -1000);
        pc.BoundsMax = new Vector3(2000, 2000, 1000);
        pc.StateSyncOut += list => { foreach (var (r, _) in list) sent.Add(r); };
        pc.GlobalPosition = new Vector3(0, 5, 0);
        step = typeof(Player).GetMethod("Step", BindingFlags.Instance | BindingFlags.NonPublic);
    }

    void Steps(int n)
    {
        for (int i = 0; i < n; i++) { step.Invoke(pc, new object[] { (double)Dt }); pc.Anim.Advance(Dt); }
    }

    void Expect(bool ok, string what) { if (!ok) fails.Add(what); }

    public override void _Process(double delta)
    {
        if (++frames != 5) return;
        try { List(); Timed(); }
        catch (System.Exception e) { fails.Add($"{e.InnerException ?? e}"); }
        SetProcess(false);
        GD.Print($"player trigger emotion: {(fails.Count == 0 ? "ok" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    /// <summary>Steps until the state is left or the frame cap is hit; returns the frames taken.</summary>
    int StepsWhileInState(int cap)
    {
        int n = 0;
        while (pc.State == Player.MoveState.TriggerEmotion && n < cap) { Steps(1); n++; }
        return n;
    }

    void List()
    {
        Steps(60);
        string a = pc.Anims.Name(Act.JumpLand, ""), b = pc.Anims.Name(Act.Idle, "");
        Expect(a != null && b != null, $"sequence names {a} / {b}");
        float lenA = (float)pc.Anim.GetAnimation(pc.Anims.Sequence(a, false)).Length;
        sent.Clear();
        var at = pc.GlobalPosition;
        pc.EnterTriggerEmotion(new TriggerEmotionData(new[] { a, b }, 0, 0, 0, false));
        Expect(pc.State == Player.MoveState.TriggerEmotion, $"list: state {pc.State}");
        Expect(pc.CurrentAnim.ToLowerInvariant() == a.ToLowerInvariant(), $"list: first sequence {pc.CurrentAnim}, want {a}");
        pc.Input2 = new Vector2(0, 1);
        Steps(Mathf.CeilToInt(lenA / 2 / Dt));
        pc.Input2 = Vector2.Zero;
        Expect(pc.GlobalPosition.DistanceTo(at) < 0.01f, $"list: moved from {at} to {pc.GlobalPosition}");
        Expect(sent.Exists(r => r.State == 0x35), $"list: sync states {string.Join(",", sent.ConvertAll(r => r.State))}");
        // a list while the state runs: bForce 0 keeps the running one
        pc.EnterTriggerEmotion(new TriggerEmotionData(new[] { b }, 0, 0, 0, false));
        Expect(pc.CurrentAnim.ToLowerInvariant() == a.ToLowerInvariant(), $"unforced list replaced the running one: {pc.CurrentAnim}");
        Steps(Mathf.CeilToInt(lenA / 2 / Dt) + 10);   // the first play's blend in (DEFAULT_NONSYNC 0.1 s) adds to its end
        Expect(pc.State == Player.MoveState.TriggerEmotion && pc.CurrentAnim.ToLowerInvariant() == b.ToLowerInvariant(), $"list: second {pc.State} {pc.CurrentAnim}, want {b}");
        int n = StepsWhileInState(600);
        Expect(pc.State == Player.MoveState.Idle, $"list end: state {pc.State} after {n} frames");
        GD.Print($"player trigger emotion: list {a} ({lenA:0.###} s), {b}, idle after {n} more frames");
    }

    void Timed()
    {
        string a = pc.Anims.Name(Act.JumpLand, "");
        float lenA = (float)pc.Anim.GetAnimation(pc.Anims.Sequence(a, false)).Length;
        int durationMs = Mathf.CeilToInt(lenA * 1000 * 2.5f);
        pc.EnterTriggerEmotion(new TriggerEmotionData(new[] { a }, 0, 0, 0, false));
        Steps(2);
        pc.EnterTriggerEmotion(new TriggerEmotionData(new[] { a }, 1, 1, durationMs, true));
        Expect(pc.State == Player.MoveState.TriggerEmotion && pc.TriggerEmotionWeapons, $"timed: state {pc.State}, weapons {pc.TriggerEmotionWeapons}");
        int n = StepsWhileInState(2000);
        float secs = n * Dt;
        // the third play ends past the duration (2.5 plays): back to idle after three plays
        Expect(pc.State == Player.MoveState.Idle && secs > lenA * 2.5f && secs < lenA * 3f + 0.25f, $"timed: {pc.State} after {secs:0.###} s, plays of {lenA:0.###} s, duration {durationMs} ms");
        Expect(!pc.TriggerEmotionWeapons, "timed: the flag outlived the state");
        pc.DeadFlag = true;
        pc.EnterTriggerEmotion(new TriggerEmotionData(new[] { a }, 1, 0, 1000, true));
        Expect(pc.State != Player.MoveState.TriggerEmotion, "a dead pc entered the state");
        pc.DeadFlag = false;
        GD.Print($"player trigger emotion: timed {durationMs} ms, idle after {secs:0.###} s");
    }
}
