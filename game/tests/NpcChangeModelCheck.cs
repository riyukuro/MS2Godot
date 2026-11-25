using System.Linq;
using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/npc_change_model.tscn) of NpcNotice mode 2 in NpcLayer (CNpc::FUN_1410951d0, then
/// GameObject::ChangeModel):
/// - a notice for an npc not here changes nothing;
/// - another row's kfm builds the npc's body from that kfm (the body named as it), in place of the old body, which leaves the
///   root; the body keeps its turn, the talk body follows it, its sequences come from the new kfm's folder, and it plays the
///   old sequence's name from the new kfm;
/// - an empty name and a kfm the packs lack keep the body (the name is still kept as m_strED8);
/// - the npc's replacement by a new add loads the row's kfm again.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class NpcChangeModelCheck : Node3D
{
    public override void _Ready()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var table = new Ms2.Formats.World.NpcTable(packs);
        var npcs = new NpcLayer(packs, builder, table, this);
        var fails = new System.Collections.Generic.List<string>();
        void Check(bool ok, string what) { if (!ok) fails.Add(what); }
        // two rows with different kfms whose models load, in both data sets
        var rows = new[] { 21000001, 21000002, 21000003, 21000010, 21000020, 11000001, 11000002 }
            .Select(table.Find).Where(r => r != null && !string.IsNullOrEmpty(r.Kfm)).ToList();
        var a = rows.FirstOrDefault();
        var b = rows.FirstOrDefault(r => a != null && !string.Equals(r.Kfm, a.Kfm, System.StringComparison.OrdinalIgnoreCase));
        Check(a != null && b != null, "no two npc rows with different kfms");
        if (a == null || b == null) { Done(fails); return; }
        World.FieldNpcs.Placed talk = null;
        npcs.Added += p => talk = p;
        npcs.Add(new NpcAddedEvent(0, new FieldAddNpc { ObjectId = 1, NpcId = a.Id, Rotation = new System.Numerics.Vector3(0, 0, 90) }));
        var before = npcs.ModelOf(1).Value;
        Check(before.Kfm == a.Kfm && !before.Placeholder && before.Body.Name == a.Kfm, $"the row's body: {before.Kfm} {before.Body?.Name} placeholder {before.Placeholder}");
        var turn = before.Body.Rotation;
        Check(!npcs.ChangeModel(99, b.Kfm), "a notice for an npc not here changed one");
        Check(npcs.ChangeModel(1, b.Kfm), "ChangeModel found no npc");
        var after = npcs.ModelOf(1).Value;
        Check(after.Kfm == b.Kfm && after.Body != before.Body && after.Body.Name == b.Kfm && !after.Placeholder,
            $"the changed body: kfm {after.Kfm}, body {after.Body?.Name} (was {before.Body.Name})");
        Check(before.Body.GetParent() == null && before.Body.IsQueuedForDeletion(), "the old body stayed under the root");
        Check(after.Body.GetParent() == talk?.Root && talk.Root.GetChildren().OfType<Node3D>().Count(n => n.Name == a.Kfm || n.Name == b.Kfm) == 1,
            "the new body is not the root's one body");
        Check(after.Body.Rotation.IsEqualApprox(turn), $"the changed body turned {after.Body.Rotation}, was {turn}");
        Check(talk?.Body == after.Body, "the talk body is still the old one");
        string entryB = packs.List(NpcLayer.NpcArchive).First(e => e.EndsWith("/" + b.Kfm.ToLowerInvariant() + ".kfm"));
        var kfmB = KfmFile.Load(packs.Read(NpcLayer.NpcArchive, entryB));
        Check(after.Folder == entryB[..entryB.LastIndexOf('/')] && before.Folder != after.Folder,
            $"the npc's sequences come from {after.Folder}, want the new kfm's {entryB[..entryB.LastIndexOf('/')]} (was {before.Folder})");
        int idleB = kfmB.ByName("Idle_A")?.Id ?? -1;
        var player = after.Body.GetNode<AnimationPlayer>("Model/AnimationPlayer");
        Check(idleB < 0 || player.CurrentAnimation == $"s{idleB}", $"the changed body plays {player.CurrentAnimation}, want the new kfm's Idle_A s{idleB}");
        Check(npcs.SequenceId(1, "Idle_A") == idleB, $"the npc's sequences are not the new kfm's ({npcs.SequenceId(1, "Idle_A")} vs {idleB})");
        Check(npcs.ChangeModel(1, "") && npcs.ModelOf(1).Value.Body == after.Body && npcs.ModelOf(1).Value.Kfm == "", "an empty name changed the body");
        Check(npcs.ChangeModel(1, "NoSuchModel_Kfm") && npcs.ModelOf(1).Value.Body == after.Body && npcs.MissingModels.Contains("NoSuchModel_Kfm"),
            "a kfm the packs lack changed the body");
        npcs.Add(new NpcAddedEvent(0, new FieldAddNpc { ObjectId = 1, NpcId = a.Id }));
        Check(npcs.ModelOf(1).Value.Kfm == a.Kfm && npcs.ModelOf(1).Value.Body.Name == a.Kfm, "the replaced npc kept the changed kfm");
        GD.Print($"npc change model: {a.Id} {a.Kfm} -> {b.Kfm}");
        Done(fails);
    }

    void Done(System.Collections.Generic.List<string> fails)
    {
        GD.Print($"npc change model: {(fails.Count == 0 ? "ok" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
