using System.Collections.Generic;
using System.Reflection;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2Godot.Movement;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/player_hold.tscn) of the own pc's StateHold (0x20, PlayerHold.cs) on another player's mount
/// seat, with a held actor given by <see cref="Player.HoldLookup"/>:
/// - ResponseRide mode 3 (HoldOn): the pc enters the hold, stands on the seat's position (feet, MS2 axes to Godot) with the
///   seat's Z rotation, and the move keys do not move it;
/// - the sync records in the hold carry state 0x20, skill 20000031 level 1 (Flag1, Flag3), the owner and node (Flag6) and the
///   ridee's feet z;
/// - the jump key does not leave the hold while the owner rides (MyPC::FUN_141527960);
/// - the owner riding off (the seat node gone) leaves for StateJump sub 0x22 and sends the mode 4 leave once;
/// - a second hold after modes 4 / 5 (HoldCameraBack) sends no mode 4 on leaving, and the jump key leaves it when the held
///   actor has no ridee, the entry direction back.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class PlayerHoldCheck : Node3D
{
    readonly NxController.FaceGrids faceGrids = new();
    readonly List<string> fails = new();
    readonly List<StateSync> sent = new();
    Player pc;
    MethodInfo step;
    int frames, leaves;
    HoldTarget target;
    const int Owner = 7001;
    const string Node = "Bip01 Spine";
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
        pc.HoldLookup = (id, node) => id == Owner && node == Node ? target : default;
        pc.HoldLeft += () => leaves++;
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
        try { Seat(); Plain(); }
        catch (System.Exception e) { fails.Add($"{e.InnerException ?? e}"); }
        SetProcess(false);
        GD.Print($"player hold: {(fails.Count == 0 ? "ok" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    void Seat()
    {
        Steps(30);
        target = new HoldTarget
        {
            Kind = HoldKind.Pc, HasNode = true, HasRidee = true, RideeZ = 40f, HasSeat = true,
            SeatPosition = new System.Numerics.Vector3(100f, 200f, 70f), SeatRow0 = new System.Numerics.Vector3(0f, 1f, 0f),
            RideeState = RemotePcStates.StateIdle, RideeAnimSpeed = 1f,
        };
        float yaw = RemotePcStates.EulerZ(target.SeatRow0, out float z) ? z * -57.2957764f : 0f;
        sent.Clear();
        pc.HoldOn(Owner, Node);
        pc.Input2 = new Vector2(0, 1);
        Steps(30);
        pc.Input2 = Vector2.Zero;
        var want = new Vector3(100f, 70f, -200f);
        Expect(pc.State == Player.MoveState.Hold && pc.HoldTargetId == Owner, $"hold: state {pc.State}, target {pc.HoldTargetId}");
        Expect(pc.StateTakesHits, "a held pc takes hits (StateHold m_n14 1)");
        Expect(pc.GlobalPosition.DistanceTo(want) < 0.01f, $"seat: at {pc.GlobalPosition}, want {want}");
        Expect(Mathf.Abs(Mathf.Wrap(pc.Visual.RotationDegrees.Y - yaw, -180f, 180f)) < 0.5f, $"seat rotation {pc.Visual.RotationDegrees.Y:0.#}, want {yaw:0.#}");
        float seatYaw = pc.Visual.RotationDegrees.Y;
        var at = pc.GlobalPosition;
        var holds = sent.FindAll(r => r.State == 0x20);
        Expect(holds.Count > 0, $"hold sync states: {string.Join(",", sent.ConvertAll(r => r.State))}");
        foreach (var r in holds)
        {
            bool ok = r.Flags.HasFlag(StateSync.Flag.Flag1) && r.Flag1Value == Player.HoldSkillId && r.Flags.HasFlag(StateSync.Flag.Flag3) && r.Flag3Value == 1
                && r.Flags.HasFlag(StateSync.Flag.Flag6) && r.Flag6Value == Owner && r.Flag6Text == Node && r.Position.Z == 40;
            if (!ok) { fails.Add($"hold record: flags {r.Flags} skill {r.Flag1Value} level {r.Flag3Value} target {r.Flag6Value} '{r.Flag6Text}' z {r.Position.Z}"); break; }
        }
        pc.JumpHeld = true;
        Steps(5);
        pc.JumpHeld = false;
        Steps(5);
        Expect(pc.State == Player.MoveState.Hold, $"jump left the hold while the owner rides: {pc.State}");
        Expect(leaves == 0, $"{leaves} mode 4 leaves while held");
        target.HasNode = false;                              // the owner rode off: the seat node is gone
        Steps(1);
        Expect(pc.State != Player.MoveState.Hold && pc.HoldTargetId == 0, $"lost seat: state {pc.State}");
        Expect(leaves == 1, $"lost seat: {leaves} mode 4 leaves, want 1");
        Steps(90);
        GD.Print($"player hold: seat at {at} yaw {seatYaw:0.#} (want {yaw:0.#}), exit yaw {pc.Visual.RotationDegrees.Y:0.#}, {holds.Count} hold records, after the seat {pc.State}");
    }

    void Plain()
    {
        float entry = pc.Visual.RotationDegrees.Y;
        target = new HoldTarget { Kind = HoldKind.Pc, HasNode = true, HasRidee = true, HasSeat = false };
        pc.HoldOn(Owner, Node);
        pc.HoldCameraBack(Owner);
        target.HasRidee = false;
        Steps(5);
        Expect(pc.State == Player.MoveState.Hold, $"seatless hold: {pc.State}");
        pc.JumpHeld = true;
        Steps(1);
        pc.JumpHeld = false;
        Expect(pc.State != Player.MoveState.Hold, $"jump in a hold without a ridee: {pc.State}");
        Expect(leaves == 1, $"leave after modes 4 / 5: {leaves} mode 4 leaves, want 1");
        Expect(Mathf.Abs(Mathf.Wrap(pc.Visual.RotationDegrees.Y - entry, -180f, 180f)) < 0.5f, $"exit direction {pc.Visual.RotationDegrees.Y:0.#}, entry {entry:0.#}");
        Steps(90);
    }
}
