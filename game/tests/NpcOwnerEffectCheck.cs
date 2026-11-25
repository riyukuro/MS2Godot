using Godot;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/npc_owner_effect.tscn) of an npc's owner effect in NpcLayer (NpcNotice mode 1,
/// CNpc::SetOwnerEffect / SetOwnerEffectActor / AttachOwnerEffect / ReleaseOwnerEffect):
/// - the effect name alone keeps nothing: it waits for a pc the npc's sync names as its owner actor;
/// - a sync block naming a pc here keeps the effect on that pc with object kind 6; the same actor again keeps it as it is;
/// - another actor stops the kept effect and keeps a new one on the new pc; an actor that is not a pc stops it, none is kept;
/// - the owner pc leaving and coming back with the same object id (a new node) gets the effect again on the new node; while it
///   is away the same actor changes nothing;
/// - a new name stops the old effect and keeps the new one; the npc's removal and its replacement by a new add stop it;
/// - a notice for an npc not here changes nothing.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class NpcOwnerEffectCheck : Node3D
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
        var pcA = new Node3D { Name = "PcA" }; var pcB = new Node3D { Name = "PcB" };
        AddChild(pcA); AddChild(pcB);
        var kept = new System.Collections.Generic.Dictionary<int, (string Fx, Node3D On, int Kind)>();
        var stopped = new System.Collections.Generic.List<int>();
        int next = 100;
        Node3D pcA1 = pcA;
        npcs.FindPc = id => id == 11 ? pcA1 : id == 12 ? pcB : null;
        npcs.SpawnKept = (fx, on, kind) => { kept[++next] = (fx, on, kind); return next; };
        npcs.StopKept = stopped.Add;
        void Sync(int obj, int owner) =>
            npcs.Move(new NpcMovedEvent(0, new NpcSync { ObjectId = obj, Sequence = -1, OwnerActor = owner }));
        npcs.Add(new NpcAddedEvent(0, new FieldAddNpc { ObjectId = 1, NpcId = npcId }));
        Check(!npcs.SetOwnerEffect(99, "Etc/Eff_Battle_Target_Balrog.xml") && kept.Count == 0, "a notice for an npc not here kept an effect");
        Check(npcs.SetOwnerEffect(1, "Etc/Eff_Battle_Target_Balrog.xml"), "SetOwnerEffect found no npc");
        Check(kept.Count == 0 && npcs.OwnerEffectOf(1)?.Kept == 0, "the name alone kept an effect");
        Sync(1, 11);
        Check(kept.Count == 1 && kept[101] == ("Etc/Eff_Battle_Target_Balrog.xml", pcA, NpcLayer.OwnerEffectKind) && npcs.OwnerEffectOf(1)?.Kept == 101,
            $"the owner actor's effect: {string.Join(",", kept)}");
        Sync(1, 11);
        Check(kept.Count == 1 && stopped.Count == 0, "the same actor again moved the effect");
        Sync(1, 12);
        Check(stopped.SequenceEqual(new[] { 101 }) && kept.Count == 2 && kept[102].On == pcB, $"a new actor: stopped {string.Join(",", stopped)}, kept {kept.Count}");
        Sync(1, 77);
        Check(stopped.SequenceEqual(new[] { 101, 102 }) && kept.Count == 2 && npcs.OwnerEffectOf(1)?.Kept == 0, "an actor that is not a pc kept the effect");
        Sync(1, 11);
        Check(kept.Count == 3 && kept[103].On == pcA, "the pc again did not keep it");
        // the owner leaves (its node goes) and comes back with the same object id as a new node
        pcA1 = null;
        Sync(1, 11);
        Check(stopped.Count == 2 && kept.Count == 3, "the same actor while it is away changed the effect");
        var pcA2 = new Node3D { Name = "PcA2" };
        AddChild(pcA2);
        pcA1 = pcA2;
        Sync(1, 11);
        Check(stopped.Last() == 103 && kept.Count == 4 && kept[104].On == pcA2, $"the owner back as a new node: kept {kept.Count}, last on {(kept.Count > 3 ? kept[kept.Keys.Max()].On?.Name : "-")}");
        Sync(1, 11);
        Check(kept.Count == 4 && stopped.Count == 3, "the same actor on the same new node moved the effect again");
        npcs.SetOwnerEffect(1, "Etc/Other.xml");
        Check(stopped.Last() == 104 && kept.Count == 5 && kept[105].Fx == "Etc/Other.xml", "a new name did not replace the effect");
        npcs.Add(new NpcAddedEvent(0, new FieldAddNpc { ObjectId = 1, NpcId = npcId }));
        Check(stopped.Last() == 105, "the replaced npc kept its effect");
        npcs.SetOwnerEffect(1, "Etc/Other.xml");
        Sync(1, 12);
        npcs.Remove(1, false);
        Check(stopped.Last() == 106 && kept.Count == 6, $"the removed npc kept its effect ({stopped.Last()})");
        GD.Print($"npc owner effect: {kept.Count} kept, {stopped.Count} stopped");
        GD.Print($"npc owner effect: {(fails.Count == 0 ? "ok" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
