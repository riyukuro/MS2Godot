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
/// - a jump sequence stops the velocity.
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
        void Block(short x, short vx, short seq = -1, bool rotates = false, float yaw = 0) =>
            npcs.Move(new NpcMovedEvent(0, new NpcSync { ObjectId = 1, Flags = (byte)(rotates ? 1 : 0), Position = new Vector3S(x, 0, 0), Velocity = new Vector3S(vx, 0, 0), RotationZ = yaw, Sequence = seq }));
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
            Block(0, 0, (short)run);
            Check(Near(At(), 0), $"the run block did not place the npc at 0: {At()}");
            // ahead = 40 + 200 * 0.3 = 100, 100 from the npc and 60 from the block: ratio 1.67, clamped to 1.2
            Block(40, 200, (short)run);
            Check(Near(At(), 0), $"the catch-up block placed the npc: {At()}");
            npcs.Process(0.25f);
            Check(Near(At(), 60), $"the catch-up velocity is not 240: {At()} after 0.25 s");
        }

        Block(300, 100, -2);
        npcs.Process(0.1f);
        npcs.Process(1f);
        Check(Near(At(), 300), $"the jump block did not stop the velocity: {At()}");
        GD.Print($"npc move: npc {npcId}, run {run}, {(fails.Count == 0 ? "ok" : "FAIL " + string.Join("; ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
