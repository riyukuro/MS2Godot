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
/// - an npc sequence key plays the pc body's sequence of that key (a jump key Jump_Down_A), once per new key, with the kf's own
///   cycle: a looping kf keeps playing past its length even when another caller loaded the clip clamped before;
/// - on a mount (riding 20910003, whose kfm has the pc names Stun_A and Damg_B) the pc still goes where the npc is, the mount
///   plays the sequence of the npc's key by name, keeps its last one for a name its kfm lacks, and the rider keeps its riding
///   pose;
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
    PackSet packs;
    ModelBuilder builder;
    int frames;
    const float Dt = 1f / 60;
    const int NpcId = 4711;
    // the npc: its z-up position, rotation z and sync sequence key; null when it left
    (System.Numerics.Vector3 Pos, float RotZ, short AnimKey)? npc;

    public override void _Ready()
    {
        packs = PackSet.Shared(Settings.DataDir);
        builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
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
        try { Follow(); Leaves(); Mounted(); }
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

    /// <summary>A body sequence by key whose kf loops (cycle 0), other than the two given, loaded clamped first as another caller
    /// (an action list) may have loaded it.</summary>
    (short, string) LoopingSequence(string not1, string not2)
    {
        for (short id = 1; id < 2000; id++)
        {
            if (pc.Anims.SequenceName(id) is not { } name || name == not1 || name == not2) continue;
            var key = pc.Anims.Sequence(name, false);
            if (key != null && pc.Anims.Kf.TryGetValue(key, out var kf) && kf.Cycle == 0 && pc.Anim.GetAnimation(key).Length > 0.2f) return (id, name);
        }
        Expect(false, "the body has no looping sequence by key");
        return (0, "");
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
        // a looping kf loops: past its length it still plays, and the body's one clip keeps the kf's cycle
        var (kl, nl) = LoopingSequence(n1, n2);
        string keyL = nl.ToLowerInvariant();
        npc = (npc.Value.Pos, npc.Value.RotZ, kl);
        Steps(1);
        Expect(pc.CurrentAnim == keyL, $"key {kl}: playing {pc.CurrentAnim}, want {nl}");
        float len = pc.Anim.HasAnimation(keyL) ? (float)pc.Anim.GetAnimation(keyL).Length : 0f;
        Steps((int)(len / Dt) + 20);
        Expect(pc.Anim.IsPlaying() && pc.Anim.CurrentAnimation == keyL, $"looping {nl} ({len:F2} s) stopped after its length: playing {pc.Anim.IsPlaying()} '{pc.Anim.CurrentAnimation}'");
        Expect(pc.Anim.HasAnimation(keyL) && pc.Anim.GetAnimation(keyL).LoopMode == Animation.LoopModeEnum.Linear, $"looping {nl}: the clip's loop mode is {(pc.Anim.HasAnimation(keyL) ? pc.Anim.GetAnimation(keyL).LoopMode : "none")}");
        pc.Input2 = Vector2.Zero;
        Steps(30);
        Expect(sent.Exists(r => r.State == 0x46 && r.Flag1Value == NpcId), $"sync: {string.Join(",", sent.ConvertAll(r => $"{r.State:x}/{r.Flag1Value}"))}");
        // the same npc again keeps the running state (vfunc_25)
        held.Clear();
        pc.EnterTriggerFollowNpc(NpcId);
        Expect(held.Count == 0 && pc.State == Player.MoveState.TriggerFollowNpc, $"same npc entered the state again ({held.Count})");
        GD.Print($"player follow npc: followed through keys {k1} {n1}, {k2} {n2}, -2 and the looping {kl} {nl}; {sent.Count} sync records");
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

    /// <summary>The pc body's sequence key of a name (the rider's actor manager, NiActorManager::GetSequenceData), 0 for none.</summary>
    short KeyOf(string name)
    {
        for (short k = 1; k < 4096; k++)
            if (string.Equals(pc.Anims.SequenceName(k), name, System.StringComparison.OrdinalIgnoreCase)) return k;
        return 0;
    }

    /// <summary>The loop mode a kf's own cycle gives (NiSequenceData Cycle Type: 0 loop, 1 reverse, 2 clamp), read from the
    /// mount's kf apart from Mount's loader.</summary>
    Animation.LoopModeEnum MountCycle(Mount m, string seq)
    {
        var (a, kfmEntry) = Mount.FindModel(packs, m.Data.KfmFor(1).ToLowerInvariant() + ".kfm");
        var kfm = Ms2.Formats.Model.KfmFile.Load(packs.Read(a, kfmEntry));
        string kf = kfmEntry[..(kfmEntry.LastIndexOf('/') + 1)] + System.IO.Path.GetFileName(kfm.ByName(seq).KfPath.Replace('\\', '/')).ToLowerInvariant();
        int c = Ms2.Formats.Model.KfAnimation.Load(Ms2.Formats.Nif.NifFile.Load(ModelBuilder.SharedSpec(Settings.NifXml), packs.Read(a, kf))).Cycle;
        return c == 0 ? Animation.LoopModeEnum.Linear : c == 1 ? Animation.LoopModeEnum.Pingpong : Animation.LoopModeEnum.None;
    }

    void Mounted()
    {
        var (k1, n1, _, _) = TwoSequences();
        short stun = KeyOf("Stun_A"), damg = KeyOf("Damg_B");
        Expect(stun != 0 && damg != 0, $"mounted: the pc body has no Stun_A ({stun}) or Damg_B ({damg})");
        var m = Mount.Load(packs, builder, 20910003);
        string stunKey = m.SequenceKey("Stun_A", false), damgKey = m.SequenceKey("Damg_B", false);
        Expect(stunKey != null && damgKey != null, "mounted: riding 20910003 has no Stun_A or Damg_B");
        Expect(m.SequenceKey(n1, false) == null, $"mounted: riding 20910003 has {n1}, the name it should lack");
        pc.RideOn(m);
        Steps(5);
        string idle = m.Current;
        npc = (new System.Numerics.Vector3(-200, 80, 30), 120f, stun);
        pc.EnterTriggerFollowNpc(NpcId);
        Expect(pc.State == Player.MoveState.TriggerFollowNpc, $"mounted: state {pc.State}");
        Steps(30);      // past the riding pose's blend in (0.1 s), so it runs
        Expect(pc.GlobalPosition.DistanceTo(Godot(npc.Value.Pos)) < 0.01f, $"mounted: pc at {pc.GlobalPosition}, npc at {Godot(npc.Value.Pos)}");
        Expect(Mathf.Abs(pc.FacingRotZ - 120f) < 0.5f, $"mounted: turned to {pc.FacingRotZ}, want 120");
        Expect(m.Current == stunKey && m.Anim.CurrentAnimation == stunKey, $"mounted: the mount plays {m.Anim.CurrentAnimation} ({m.Current}), want Stun_A (was {idle})");
        var stunMode = MountCycle(m, "Stun_A");
        Expect(m.Anim.GetAnimation(stunKey).LoopMode == stunMode, $"mounted: Stun_A loop mode {m.Anim.GetAnimation(stunKey).LoopMode}, its kf's cycle gives {stunMode}");
        Expect(pc.CurrentAnim != "stun_a", "mounted: the rider plays the npc's Stun_A in place of its riding pose");
        // a new npc key: the mount plays it, the riding pose runs on (not played over and started again)
        string ride = pc.CurrentAnim;
        double at = pc.Anim.CurrentAnimationPosition;
        npc = (npc.Value.Pos, npc.Value.RotZ, damg);
        Steps(1);
        Expect(m.Current == damgKey && m.Anim.CurrentAnimation == damgKey, $"mounted: key {damg} (Damg_B): the mount plays {m.Anim.CurrentAnimation}");
        Expect(pc.CurrentAnim == ride && pc.Anim.CurrentAnimationPosition > at,
            $"mounted: key {damg} (Damg_B) broke the riding pose {ride}: now {pc.CurrentAnim} at {pc.Anim.CurrentAnimationPosition:F3} s, was {at:F3} s");
        var damgMode = MountCycle(m, "Damg_B");
        Expect(m.Anim.GetAnimation(damgKey).LoopMode == damgMode, $"mounted: Damg_B loop mode {m.Anim.GetAnimation(damgKey).LoopMode}, its kf's cycle gives {damgMode}");
        // a key whose name the mount's kfm lacks plays nothing on it
        npc = (npc.Value.Pos, npc.Value.RotZ, k1);
        Steps(1);
        Expect(m.Current == damgKey, $"mounted: key {k1} ({n1}), not in the mount's kfm, changed the mount to {m.Current}");
        npc = null;
        Steps(1);
        pc.RideOff();
        GD.Print($"player follow npc: mounted, the pc went to the npc, the mount played Stun_A then Damg_B and kept it for {n1}, the rider kept its riding pose ({ride}); loop modes {stunMode} / {damgMode}");
    }
}
