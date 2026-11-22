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
/// - a tagged npc with &lt;nodeCollisions&gt; rows, on a turned root, gets one center per row whose bone the skeleton has: the
///   bone's client-space position plus offset + (0, 0, vz/2) turned by the bone's world rotation (worked out here by vectors
///   in skeleton space), and the centers move with the npc's root;
/// - a tagged npc with rows but no skeleton in the tree, with none of the rows' bones, or a placeholder: false (the client
///   still has its boxes, so the packet goes on);
/// - a tagged npc without rows, a placeholder too: tagged with no anchors (true, empty), which ends a mode 5 packet.
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
        static System.Numerics.Vector3 Client(Godot.Vector3 v) => new(v.X, -v.Z, v.Y);

        var plain = table.Rows.First(r => !r.CollisionPosition && r.NodeCollisions.Length == 0);
        Spawn(plain.Id);
        Check(!npcs.CollisionAnchors(obj, anchors), $"untagged npc {plain.Id} has anchors");

        var withRowsList = table.Rows.Where(r => r.CollisionPosition && r.NodeCollisions.Length != 0).OrderBy(r => r.Id).ToList();
        var bareList = table.Rows.Where(r => r.CollisionPosition && r.NodeCollisions.Length == 0).OrderBy(r => r.Id).ToList();
        Check(withRowsList.Count >= 2 && bareList.Count >= 2, $"{withRowsList.Count} tagged rows with rows, {bareList.Count} without");

        // placeholders first, from the ends of the lists, so no later spawn reuses their (missing) model
        var phRows = withRowsList.Count >= 2 ? withRowsList[^1] : null;
        var phBare = bareList.Count >= 2 ? bareList[^1] : null;
        if (phRows != null)
        {
            phRows.Kfm = null;
            Check(Spawn(phRows.Id) == null, $"npc {phRows.Id} without a kfm is not a placeholder");
            Check(!npcs.CollisionAnchors(obj, anchors), $"placeholder tagged npc {phRows.Id} with rows reads as tagged");
        }
        if (phBare != null)
        {
            phBare.Kfm = null;
            Check(Spawn(phBare.Id) == null, $"npc {phBare.Id} without a kfm is not a placeholder");
            anchors.Add(default);
            Check(npcs.CollisionAnchors(obj, anchors) && anchors.Count == 0, $"placeholder tagged npc {phBare.Id} without rows: not tagged and empty");
        }

        int tagged = 0, withRows = 0;
        foreach (var row in withRowsList)
        {
            if (row == phRows) continue;
            var visual = Spawn(row.Id);
            var sk = visual?.GetNodeOrNull<Skeleton3D>(SkeletonPath);
            if (sk == null)
            {
                Check(!npcs.CollisionAnchors(obj, anchors), $"tagged npc {row.Id} with rows but no skeleton reads as tagged");
                continue;
            }
            tagged = row.Id;
            var root = visual.GetParent<Node3D>();
            root.Rotation = new Godot.Vector3(0, 0.7f, 0);
            Check(npcs.CollisionAnchors(obj, anchors), $"tagged npc {row.Id} reads untagged");
            var expect = row.NodeCollisions.Where(c => sk.FindBone(c.TargetNode) >= 0).ToList();
            withRows = expect.Count;
            Check(anchors.Count == expect.Count && expect.Count != 0, $"npc {row.Id}: {anchors.Count} anchors for {expect.Count} found rows");
            bool turns = false;
            for (int k = 0; k < anchors.Count && k < expect.Count; k++)
            {
                var pose = sk.GetBoneGlobalPose(sk.FindBone(expect[k].TargetNode));
                var skWorld = sk.GlobalTransform;
                var local = new Godot.Vector3(expect[k].Offset.X, expect[k].Offset.Y, expect[k].Offset.Z + expect[k].Volume.Z * 0.5f);
                var bone = Client(skWorld * pose.Origin);
                var turned = Client(skWorld.Basis.Orthonormalized() * (pose.Basis.Orthonormalized() * local));
                var want = bone + turned;
                var unturned = bone + new System.Numerics.Vector3(local.X, local.Y, local.Z);
                turns |= System.Numerics.Vector3.Distance(want, unturned) > 1f;
                Check(System.Numerics.Vector3.Distance(anchors[k], want) < 0.05f, $"npc {row.Id} anchor {k}: {anchors[k]}, not {want}");
            }
            Check(turns, $"npc {row.Id}: no anchor's offset is turned, so the rotation is not checked");
            var before = anchors.ToList();
            root.Position += new Godot.Vector3(100, 0, 0);
            npcs.CollisionAnchors(obj, anchors);
            Check(anchors.Count == before.Count && anchors.Zip(before).All(p => System.Numerics.Vector3.Distance(p.First, p.Second + new System.Numerics.Vector3(100, 0, 0)) < 0.01f),
                "the anchors did not move with the npc's root");

            sk.GetParent().RemoveChild(sk);
            anchors.Add(default);
            Check(!npcs.CollisionAnchors(obj, anchors), $"tagged npc {row.Id} without its skeleton in the tree reads as tagged");
            sk.Free();

            var kept = row.NodeCollisions;
            row.NodeCollisions = kept.Select(c => c with { TargetNode = "No Such Bone" }).ToArray();
            Check(Spawn(row.Id)?.GetNodeOrNull<Skeleton3D>(SkeletonPath) != null, $"npc {row.Id} respawned without a skeleton");
            Check(!npcs.CollisionAnchors(obj, anchors), $"tagged npc {row.Id} with none of its rows' bones reads as tagged");
            row.NodeCollisions = kept;
            break;
        }
        Check(tagged != 0, "no tagged npc with rows has a model");

        var bare = bareList.FirstOrDefault(r => r != phBare && Spawn(r.Id) != null);
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
