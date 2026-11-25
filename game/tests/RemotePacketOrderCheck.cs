using Godot;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/remote_packet_order.tscn) that a packet naming an object an earlier packet of the same frame made
/// finds it, as the client handles each packet in order (RemoteLayer.CatchUp): the add events wait in the tracker's queue, which
/// the layer empties only on its next frame.
/// - An npc added and then named by NpcNotice mode 1 (RemoteLayer.SetOwnerEffect) is found.
/// - An npc added before FollowNpc is not in the layer until CatchUp, and is after it (the own pc's follow lookup).
/// - Insignia on a player added just before (RemoteLayer.SetSymbol) applies the add first.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class RemotePacketOrderCheck : Node3D
{
    public override void _Ready()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var field = new FieldStub(packs);
        AddChild(field);
        var layer = new RemoteLayer(field, builder, 1);
        var table = Ms2.Formats.World.NpcTable.For(packs);
        int npcId = 0;
        foreach (int id in new[] { 21000001, 21000002, 21000003, 11000001 })
            if (table.Find(id) != null) { npcId = id; break; }
        var fails = new System.Collections.Generic.List<string>();
        void Check(bool ok, string what) { if (!ok) fails.Add(what); }
        Check(npcId != 0, "no npc row");
        void AddNpc(int obj) => layer.Tracker.Events.Enqueue(new NpcAddedEvent(0, new FieldAddNpc { ObjectId = obj, NpcId = npcId }));

        AddNpc(41);
        Check(layer.SetOwnerEffect(41, "Eff_Test"), "NpcNotice mode 1 did not find the npc added before it in the same frame");

        AddNpc(42);
        Check(layer.FollowPoseOf(42) == null, "the npc's add was applied before the layer's frame or a catch-up (the check sees no queue)");
        layer.CatchUp();
        Check(layer.FollowPoseOf(42) != null, "after CatchUp the npc added before FollowNpc is not found");

        layer.Tracker.Events.Enqueue(new UserAddedEvent(0, new FieldAddUser { ObjectId = 43, User = new UserBlock { Name = "r43", CharacterId = 143, Gender = 1 } }, false));
        layer.SetSymbol(43, 2, true);
        Check(layer.Tracker.Events.IsEmpty, "Insignia did not apply the player's add before it");

        GD.Print($"remote packet order: {(fails.Count == 0 ? "ok" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
