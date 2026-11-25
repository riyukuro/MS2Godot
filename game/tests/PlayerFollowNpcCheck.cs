using System.Collections.Generic;
using System.Reflection;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2Godot.Movement;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/player_follow_npc.tscn) of the own pc's StateTriggerFollowNpc (0x46, PlayerTriggerFollowNpc.cs),
/// entered as the FollowNpc packet enters it:
/// - the pc stands where the npc is and is turned as it is, frame after frame as the npc moves; the move keys do nothing;
/// - an npc sequence key plays the pc body's sequence of that key (a jump key Jump_Down_A), once per new key;
/// - its sync records carry state 0x46 with the npc id as the state data; the held item is let go on entry;
/// - the same npc again keeps the state, the npc's leaving goes back to idle, an npc not here never holds it, a dead pc does
///   not enter it.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class PlayerFollowNpcCheck : Node3D
{
    readonly NxController.FaceGrids faceGrids = new();
    readonly List<string> fails = new();
    readonly List<StateSync> sent = new();
    readonly List<Ms2.Ui.HeldItemState> held = new();
    Player pc;
    MethodInfo step;
    int frames;
    const float Dt = 1f / 60;
    const int NpcId = 4711;
    // the npc: its z-up position, rotation z and sync sequence key; null when it left
    (System.Numerics.Vector3 Pos, float RotZ, short AnimKey)? npc;

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
        pc.HeldStateEntered += s => held.Add(s);
        pc.FindNpc = id => id == NpcId ? npc : null;
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
        try { Follow(); Leaves(); }
        catch (System.Exception e) { fails.Add($"{e.InnerException ?? e}"); }
        SetProcess(false);
        GD.Print($"player follow npc: {(fails.Count == 0 ? "ok" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    /// <summary>Two body sequences of the pc's kfm that load, by key.</summary>
    (short, string, short, string) TwoSequences()
    {
        var found = new List<(short, string)>();
        for (short id = 1; id < 2000 && found.Count < 2; id++)
            if (pc.Anims.SequenceName(id) is { } name && pc.Anims.Sequence(name, false) != null && !found.Exists(f => f.Item2 == name)) found.Add((id, name));
        Expect(found.Count == 2, $"the body has {found.Count} sequences by key");
        return found.Count == 2 ? (found[0].Item1, found[0].Item2, found[1].Item1, found[1].Item2) : ((short)0, "", (short)0, "");
    }

    static Vector3 Godot(System.Numerics.Vector3 p) => new(p.X, p.Z, -p.Y);

    void Follow()
    {
        Steps(30);
        var (k1, n1, k2, n2) = TwoSequences();
        npc = (new System.Numerics.Vector3(300, -120, 40), 75f, 0);
        sent.Clear(); held.Clear();
        pc.EnterTriggerFollowNpc(NpcId);
        Expect(pc.State == Player.MoveState.TriggerFollowNpc && pc.FollowedNpc == NpcId, $"enter: state {pc.State}, npc {pc.FollowedNpc}");
        Expect(held.Contains(Ms2.Ui.HeldItemState.TriggerFollowNpc), $"enter: held states {string.Join(",", held)}");
        string before = pc.CurrentAnim;
        Steps(1);
        Expect(pc.GlobalPosition.DistanceTo(Godot(npc.Value.Pos)) < 0.01f, $"placed at {pc.GlobalPosition}, npc at {Godot(npc.Value.Pos)}");
        Expect(Mathf.Abs(pc.FacingRotZ - 75f) < 0.5f && Mathf.Abs(pc.TargetRotZ - 75f) < 0.5f, $"turned to {pc.FacingRotZ} / {pc.TargetRotZ}, want 75");
        Expect(pc.CurrentAnim == before, $"key 0 played {pc.CurrentAnim}");
        // the npc moves on and plays a sequence: the pc copies both; the move keys do nothing
        pc.Input2 = new Vector2(0, 1);
        npc = (new System.Numerics.Vector3(340, -100, 42), -30f, k1);
        Steps(10);
        Expect(pc.GlobalPosition.DistanceTo(Godot(npc.Value.Pos)) < 0.01f, $"moved npc: pc at {pc.GlobalPosition}, npc at {Godot(npc.Value.Pos)}");
        Expect(Mathf.Abs(pc.FacingRotZ + 30f) < 0.5f, $"moved npc: turned to {pc.FacingRotZ}, want -30");
        Expect(pc.CurrentAnim == n1.ToLowerInvariant(), $"key {k1}: playing {pc.CurrentAnim}, want {n1}");
        // the same key again does not restart it
        double pos = pc.Anim.CurrentAnimationPosition;
        Steps(5);
        Expect(pc.Anim.CurrentAnimationPosition > pos, $"key {k1} restarted ({pos} -> {pc.Anim.CurrentAnimationPosition})");
        npc = (npc.Value.Pos, npc.Value.RotZ, k2);
        Steps(1);
        Expect(pc.CurrentAnim == n2.ToLowerInvariant(), $"key {k2}: playing {pc.CurrentAnim}, want {n2}");
        npc = (npc.Value.Pos, npc.Value.RotZ, -2);
        Steps(1);
        Expect(pc.CurrentAnim == "jump_down_a", $"key -2: playing {pc.CurrentAnim}, want jump_down_a");
        pc.Input2 = Vector2.Zero;
        Steps(30);
        Expect(sent.Exists(r => r.State == 0x46 && r.Flag1Value == NpcId), $"sync: {string.Join(",", sent.ConvertAll(r => $"{r.State:x}/{r.Flag1Value}"))}");
        // the same npc again keeps the running state (vfunc_25)
        held.Clear();
        pc.EnterTriggerFollowNpc(NpcId);
        Expect(held.Count == 0 && pc.State == Player.MoveState.TriggerFollowNpc, $"same npc entered the state again ({held.Count})");
        GD.Print($"player follow npc: followed through keys {k1} {n1}, {k2} {n2} and -2; {sent.Count} sync records");
    }

    void Leaves()
    {
        npc = null;
        Steps(1);
        Expect(pc.State == Player.MoveState.Idle && pc.FollowedNpc == 0, $"npc gone: state {pc.State}");
        pc.EnterTriggerFollowNpc(NpcId);
        Expect(pc.State == Player.MoveState.Idle, $"an npc not here: state {pc.State}");
        npc = (new System.Numerics.Vector3(0, 0, 40), 0, 0);
        pc.DeadFlag = true;
        pc.EnterTriggerFollowNpc(NpcId);
        Expect(pc.State != Player.MoveState.TriggerFollowNpc, "a dead pc entered the state");
        pc.DeadFlag = false;
        GD.Print("player follow npc: idle when the npc left; an npc not here and a dead pc do not enter");
    }
}
