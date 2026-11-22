using Godot;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/npc_move.tscn) of an npc's sync movement in NpcLayer, as CNpc::ApplySyncBlock (@0x1410918e0) and
/// CNpc::vfunc_53 (@0x1410952f0) move it:
/// - a block puts the npc at its position (raised by the packet lift) and the block velocity moves it on every frame;
/// - the rotate flag snaps the yaw;
/// - on a Run_ sequence an npc 40 short of the block position stays put, and its velocity is scaled by the clamped catch-up ratio (1.2);
/// - the Run_ animation plays at the row's anispeed times PCBase::vfunc_97 times the unclamped catch-up ratio; an npc made with the row's
///   model scale at 1.5 (no scale buff) plays it at 2 - 1/1.5, and vfunc_97's four branches give the client's rates;
/// - a block speed of 0 on a one-shot sequence the model has is kept, not taken as 1 (a model without one fails);
/// - a jump sequence stops the velocity and keeps the yaw;
/// - the yaw turns at most the row's rotationSpeed per second (CNpc::TurnTowardTargetYaw @0x141099170);
/// - Process allocates nothing per frame.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class NpcMoveCheck : Node3D
{
    public override void _Ready()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var table = new Ms2.Formats.World.NpcTable(packs);
        var npcs = new NpcLayer(packs, builder, table, this);
        int npcId = 0;
        foreach (int id in new[] { 21000001, 21000002, 21000003, 11000001 })
            if (table.Find(id) != null) { npcId = id; break; }
        var fails = new System.Collections.Generic.List<string>();
        void Check(bool ok, string what) { if (!ok) fails.Add(what); }
        Check(npcId != 0, "no npc row");
        npcs.Add(new NpcAddedEvent(0, new FieldAddNpc { ObjectId = 1, NpcId = npcId }));
        float lift = World.FieldNpcs.PacketLift;
        var row = table.Find(npcId);
        void Block(short x, short vx, short seq = -1, bool rotates = false, float yaw = 0, int obj = 1, float speed = 1) =>
            npcs.Move(new NpcMovedEvent(0, new NpcSync { ObjectId = obj, Flags = (byte)(rotates ? 1 : 0), Position = new Vector3S(x, 0, 0), Velocity = new Vector3S(vx, 0, 0), RotationZ = yaw, Sequence = seq, AnimSpeed = speed }));
        float Yaw() => Mathf.RadToDeg(npcs.Info(1)?.yaw ?? float.NaN);
        Vector3 At() => npcs.Info(1)?.root.Position ?? new Vector3(float.NaN, 0, 0);
        bool Near(Vector3 a, float x) => (a - new Vector3(x, lift, 0)).Length() < 0.01f;

        Block(100, 50, rotates: true, yaw: 90);
        Check(Near(At(), 100), $"the block did not place the npc: {At()}");
        Check(Mathf.Abs((npcs.Info(1)?.yaw ?? 0) - Mathf.DegToRad(90)) < 0.001f, $"the rotate flag did not snap the yaw: {npcs.Info(1)?.yaw}");
        npcs.Process(0.5f);
        Check(Near(At(), 125), $"velocity 50 for 0.5 s did not move the npc to 125: {At()}");
        npcs.Process(0.5f);
        Check(Near(At(), 150), $"the velocity did not keep moving the npc to 150: {At()}");

        int run = npcs.SequenceId(1, "Run_A");
        if (run < 0) GD.Print("npc move: no Run_A sequence, the catch-up part is skipped");
        else
        {
            Block(0, 0);
            Check(Near(At(), 0), $"the block did not place the npc at 0: {At()}");
            // ahead = 40 + 200 * 0.3 = 100, 100 from the npc and 60 from the block: ratio 1.67, clamped to 1.2
            Block(40, 200, (short)run);
            Check(Near(At(), 0), $"the catch-up block placed the npc: {At()}");
            float anim = npcs.AnimSpeed(1);
            float rate = NpcLayer.ScaleRate(1f, row.Scale);
            Check(Mathf.Abs(anim - row.AniSpeed * rate * (100f / 60f)) < 0.001f, $"the catch-up animation speed {anim} is not anispeed {row.AniSpeed} x the scale rate {rate} x ratio 1.67");
            npcs.Process(0.25f);
            Check(Near(At(), 60), $"the catch-up velocity is not 240: {At()} after 0.25 s");

            // the same row with model scale 1.5 (m_fScaleAC) and no scale buff (m_fScaleA8 1): Run_ at anispeed x (2 - 1/1.5), on the block in place
            float scale0 = row.Scale;
            row.Scale = 1.5f;
            npcs.Add(new NpcAddedEvent(0, new FieldAddNpc { ObjectId = 2, NpcId = npcId }));
            row.Scale = scale0;
            Block(0, 0, obj: 2);
            Block(0, 100, (short)run, obj: 2);
            float scaled = npcs.AnimSpeed(2);
            Check(Mathf.Abs(scaled - row.AniSpeed * (2f - 1f / 1.5f)) < 0.001f, $"the scale 1.5 npc's Run_ speed {scaled} is not anispeed {row.AniSpeed} x 1.333");
        }
        // the scale rate's branches (buff scale against model scale)
        foreach (var (buff, model, rateWant) in new[] { (1f, 1f, 1f), (1f, 3f, 1.5f), (1f, 1.5f, 2f - 1f / 1.5f), (1f, 0.5f, 0.5f), (1f, 0.8f, 1f - 0.2f / 1.6f), (1f, 0.4f, 0.5f) })
            Check(Mathf.Abs(NpcLayer.ScaleRate(buff, model) - rateWant) < 1e-5f, $"the scale rate of buff {buff}, model {model} is {NpcLayer.ScaleRate(buff, model)}, want {rateWant}");

        // a block speed of 0 on a one-shot sequence: PlayAnimation keeps it (the sequence's frequency is 0)
        int oneShot = npcs.SequenceId(1, OneShot);
        Check(oneShot >= 0, $"npc {npcId} has no one-shot sequence for the speed 0 block");
        if (oneShot >= 0)
        {
            Block(0, 0, (short)oneShot, speed: 0);
            Check(npcs.AnimSpeed(1) == 0, $"a block speed of 0 played the sequence at {npcs.AnimSpeed(1)}");
        }

        // the turn: snapped to 0, then a target of 90 without the snap flag turns at the row's cap (or closes 10x the gap per second)
        Block(0, 0, rotates: true, yaw: 0);
        Block(0, 0, yaw: 90);
        npcs.Process(0.05f);
        float want = Mathf.Min(90f / 0.1f, row.RotationSpeed) * 0.05f;
        Check(Mathf.Abs(Yaw() - want) < 0.01f, $"the turn step is {Yaw()}, want {want} (rotationSpeed {row.RotationSpeed})");

        Block(0, 0, rotates: true, yaw: 0);
        Block(300, 100, -2, rotates: true, yaw: 45);
        Check(Mathf.Abs(Yaw()) < 0.01f, $"the jump block snapped the yaw to {Yaw()}");
        npcs.Process(0.1f);
        npcs.Process(1f);
        Check(Near(At(), 300), $"the jump block did not stop the velocity: {At()}");
        Check(Mathf.Abs(Yaw()) < 0.01f, $"the jump block set the yaw target: {Yaw()}");

        Block(300, 100);
        npcs.Process(0.016f);
        long a0 = System.GC.GetAllocatedBytesForCurrentThread();
        for (int k = 0; k < 100; k++) npcs.Process(0.016f);
        long alloc = System.GC.GetAllocatedBytesForCurrentThread() - a0;
        Check(alloc == 0, $"Process allocated {alloc} B in 100 frames");
        GD.Print($"npc move: npc {npcId}, run {run}, anispeed {row.AniSpeed}, scale {row.Scale}, rotationSpeed {row.RotationSpeed}, {alloc} B per 100 frames, {(fails.Count == 0 ? "ok" : "FAIL " + string.Join("; ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    static bool OneShot(string n) => !n.StartsWith("Idle", System.StringComparison.OrdinalIgnoreCase) && !n.StartsWith("Walk", System.StringComparison.OrdinalIgnoreCase)
        && !n.StartsWith("Run", System.StringComparison.OrdinalIgnoreCase) && !n.Contains("Idle", System.StringComparison.OrdinalIgnoreCase)
        && !n.StartsWith("Dead", System.StringComparison.OrdinalIgnoreCase);
}
