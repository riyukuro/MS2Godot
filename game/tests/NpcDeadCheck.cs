using Godot;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/npc_dead.tscn) of an npc's dead flag in NpcLayer (PCBase::IsDead, set by CNpc::vfunc_125):
/// - an npc added without the add packet's dead flag is alive;
/// - one added with it is dead at once (OnFieldAddNpc's vfunc_125(0));
/// - FieldDeadNpc makes the first dead (vfunc_125(1)), the body stays, a second one changes nothing;
/// - FieldRemoveNpc drops it.
/// - FieldAddNpc for an object id already present raises the old npc's Removed before the new one replaces it.
/// RemoteLayer's PlayCast returns for a dead npc on this flag. Quits with 0 when all hold, else 1.
/// </summary>
public partial class NpcDeadCheck : Node3D
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
        FieldAddNpc Npc(int obj, bool hidden) => new FieldAddNpc { ObjectId = obj, NpcId = npcId, Hidden = hidden };
        npcs.Add(new NpcAddedEvent(0, Npc(1, false)));
        npcs.Add(new NpcAddedEvent(0, Npc(2, true)));
        Check(npcs.Count == 2, $"count {npcs.Count} after the adds");
        Check(!npcs.IsDead(1), "npc without the dead flag is dead");
        Check(npcs.IsDead(2), "npc with the dead flag is alive");
        Check(npcs.Deaths == 1, $"deaths {npcs.Deaths} after the adds");
        npcs.Remove(1, true);
        Check(npcs.IsDead(1) && npcs.Count == 2, "FieldDeadNpc did not kill and keep the npc");
        npcs.Remove(1, true);
        npcs.SetHp(2, 0, 100);
        Check(npcs.Deaths == 2, $"deaths {npcs.Deaths} after a second dead packet and hp 0 on a dead npc");
        npcs.Remove(1, false);
        Check(npcs.Count == 1 && !npcs.IsDead(1), "FieldRemoveNpc did not drop the npc");
        // FieldAddNpc for a present object id replaces the npc: the old one's NotifyRemoveNpc goes out first, as when it
        // is removed, so a marker keyed by the id goes with it
        var removed = new System.Collections.Generic.List<int>();
        npcs.Removed = removed.Add;
        npcs.Add(new NpcAddedEvent(0, Npc(2, false)));
        Check(removed.Count == 1 && removed[0] == 2, $"the replaced npc's removal was not raised ({removed.Count})");
        Check(npcs.Count == 1 && !npcs.IsDead(2), "the replacing add did not keep one live npc");
        GD.Print($"npc dead: npc {npcId}, deaths {npcs.Deaths}, {(fails.Count == 0 ? "ok" : "FAIL " + string.Join("; ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
