using System.Collections.Generic;
using System.Linq;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/collision_anchors.tscn) of <see cref="NpcLayer.CollisionAnchors"/>, the anchors of a splash hit
/// on an npc with property tag 0x1d "collision_position" (npcdata.cpp:1387, CNpc::BuildCollision's m_arrE68):
/// - an npc without the tag has none (false);
/// - a tagged npc with &lt;nodeCollisions&gt; rows gets one center per row whose bone the skeleton has. Each lies
///   |offset + (0, 0, vz/2)| from its bone's world position in client space (rotation only), and moves with the npc's root;
/// - a tagged npc without rows is tagged with no anchors (true, empty), which ends a mode 5 packet.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class CollisionAnchorsCheck : Node3D
{
    static readonly NodePath SkeletonPath = "Model/" + ModelBuilder.SkeletonName;

    public override void _Ready()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var table = new Ms2.Formats.World.NpcTable(packs);
        var npcs = new NpcLayer(packs, builder, table, this);
        var fails = new List<string>();
        void Check(bool ok, string what) { if (!ok) fails.Add(what); }
        var anchors = new List<System.Numerics.Vector3>();
        int obj = 0;
        Node3D Spawn(int npcId)
        {
            npcs.Add(new NpcAddedEvent(0, new FieldAddNpc { ObjectId = ++obj, NpcId = npcId }));
            return npcs.HoldInfo(obj, out _, out _, out var v) ? v : null;
        }

        var plain = table.Rows.First(r => !r.CollisionPosition && r.NodeCollisions.Length == 0);
        Spawn(plain.Id);
        Check(!npcs.CollisionAnchors(obj, anchors), $"untagged npc {plain.Id} has anchors");

        int tagged = 0, withRows = 0;
        foreach (var row in table.Rows.Where(r => r.CollisionPosition && r.NodeCollisions.Length != 0).OrderBy(r => r.Id))
        {
            var visual = Spawn(row.Id);
            var sk = visual?.GetNodeOrNull<Skeleton3D>(SkeletonPath);
            if (sk == null) continue;
            tagged = row.Id;
            Check(npcs.CollisionAnchors(obj, anchors), $"tagged npc {row.Id} reads untagged");
            var expect = row.NodeCollisions.Where(c => sk.FindBone(c.TargetNode) >= 0).ToList();
            withRows = expect.Count;
            Check(anchors.Count == expect.Count && expect.Count != 0, $"npc {row.Id}: {anchors.Count} anchors for {expect.Count} found rows");
            for (int k = 0; k < anchors.Count && k < expect.Count; k++)
            {
                var g = (sk.GlobalTransform * sk.GetBoneGlobalPose(sk.FindBone(expect[k].TargetNode))).Origin;
                var bone = new System.Numerics.Vector3(g.X, -g.Z, g.Y);
                float want = (expect[k].Offset + new System.Numerics.Vector3(0, 0, expect[k].Volume.Z * 0.5f)).Length();
                float got = System.Numerics.Vector3.Distance(anchors[k], bone);
                Check(Mathf.Abs(got - want) < 0.01f, $"npc {row.Id} anchor {k}: {got:0.###} from its bone, not {want:0.###}");
            }
            var before = anchors.ToList();
            visual.GetParent<Node3D>().Position += new Godot.Vector3(100, 0, 0);
            npcs.CollisionAnchors(obj, anchors);
            Check(anchors.Count == before.Count && anchors.Zip(before).All(p => System.Numerics.Vector3.Distance(p.First, p.Second + new System.Numerics.Vector3(100, 0, 0)) < 0.01f),
                "the anchors did not move with the npc's root");
            break;
        }
        Check(tagged != 0, "no tagged npc with rows has a model");

        var bare = table.Rows.FirstOrDefault(r => r.CollisionPosition && r.NodeCollisions.Length == 0 && Spawn(r.Id) != null);
        if (bare != null)
        {
            anchors.Add(default);
            Check(npcs.CollisionAnchors(obj, anchors) && anchors.Count == 0, $"tagged npc {bare.Id} without rows: not tagged and empty");
        }
        else Check(false, "no tagged npc without rows has a model");

        GD.Print($"collision anchors: npc {tagged} ({withRows} anchors), {(fails.Count == 0 ? "ok" : "FAIL " + string.Join("; ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
