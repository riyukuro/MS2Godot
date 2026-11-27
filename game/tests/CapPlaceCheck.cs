using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Net.Packets;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/cap_place.tscn) of EquipApplier's cap placement, CEquipment::FUN_1411e5c10
/// (@0x1411e5c10, equipment.cpp:2794-2917): a cap whose model head has capAttach (customize@capAttach, GMS2 customize/CP@attach)
/// is put at the worn hair's capTransform and moved onto the hair by a pick along its own -z.
/// <list type="bullet">
/// <item>the crown (11300000, capAttach on both data sets) worn over the starter hair sits on the hair: its centre is inside
/// the hair's bounds grown by the crown's half size, and above the head bone;</item>
/// <item>the hair changed (Reapply of the HR slot alone) moves the crown with it onto the new hair;</item>
/// <item>the crown taken off and put on again lands where it was.</item>
/// </list>
/// Positions are the drawn vertices in the skeleton's space (skinned ones through the Godot skin and pose).
/// Needs MS2_DATA and MS2_NIFXML (or local.cfg). Quits with 0 when it holds, else 1.
/// </summary>
public partial class CapPlaceCheck : Node3D
{
    public override async void _Ready()
    {
        var fails = new List<string>();
        try { await Run(fails); }
        catch (Exception e) { fails.Add(e.ToString()); }
        GD.Print($"cap place: {(fails.Count == 0 ? "ok" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    const int Crown = 11300000;

    async System.Threading.Tasks.Task Run(List<string> fails)
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var (visual, _, md, bodyDir) = Look.PlayerLook.Build(builder, packs);
        int gender = bodyDir == "male" ? 0 : 1;
        visual.Free();
        var start = Look.PlayerLook.OfflineWorn.Where(i => i.Slot != EquipSlot.CP).ToList();
        var hair = start.FirstOrDefault(i => i.Slot == EquipSlot.HR);
        if (hair == null) { fails.Add("the starter look has no hair"); return; }

        // another hair of the same gender with a capTransform, for the hair change
        var hairs = packs.Exists("Xml", "itemmodel/102.xml") ? PackXml.Parse(packs.Read("Xml", "itemmodel/102.xml"))
            : Ms2.Formats.World.PerFileTable.ItemModels(packs, 102);
        var other = (hairs?.Descendants("ItemModel") ?? Enumerable.Empty<System.Xml.Linq.XElement>())
            .Where(e => (int)e.Attribute("id") != hair.Id && (int)e.Attribute("id") / 1000 == hair.Id / 1000)
            .Where(e => e.Element("customize") is { } c && (c.Element("capTransform") ?? c.Element("CP")?.Element("transform")) != null)
            .Select(e => (int)e.Attribute("id")).FirstOrDefault();

        var v = builder.Build(md, "Capped");
        AddChild(v);
        var equip = new EquipApplier(packs, builder);
        var cap = new EquipItem { Id = Crown, Uid = 9001, Slot = EquipSlot.CP };
        equip.Apply(v, md, gender, start.Append(cap));
        await Settle();
        var skel = v.GetNode<Skeleton3D>("Model/" + ModelBuilder.SkeletonName);
        var first = OnHair(fails, skel, hair.Id, "worn");

        if (other != 0)
        {
            var newHair = new EquipItem { Id = other, Uid = hair.Uid, Slot = EquipSlot.HR };
            equip.Reapply(v, start.Select(i => i.Slot == EquipSlot.HR ? newHair : i).Append(cap));
            await Settle();
            var moved = OnHair(fails, skel, other, $"hair {hair.Id} -> {other}");
            if (first is { } a && moved is { } b && a.DistanceTo(b) < 0.5f)
                fails.Add($"hair {hair.Id} -> {other}: the crown stayed at {a} (the hairs' cap points differ)");
            equip.Reapply(v, start.Append(cap));
            await Settle();
        }
        else GD.Print($"cap place: no other hair of {hair.Id / 1000}xxx with a capTransform; hair change not checked");

        equip.Reapply(v, start);
        await Settle();
        if (Meshes(skel, Crown).Any()) fails.Add("taken off: a crown mesh is still under the skeleton");
        equip.Reapply(v, start.Append(cap));
        await Settle();
        var again = OnHair(fails, skel, hair.Id, "put on again");
        if (first is { } f && again is { } g && f.DistanceTo(g) > 0.01f)
            fails.Add($"put on again: the crown is at {g}, first worn at {f}");
        v.Free();
    }

    /// <summary>The crown's centre when it sits on the hair <paramref name="hairId"/>, else a failure.</summary>
    static Vector3? OnHair(List<string> fails, Skeleton3D skel, int hairId, string what)
    {
        var crown = Bounds(skel, Meshes(skel, Crown));
        var hairBox = Bounds(skel, Meshes(skel, hairId));
        if (crown == null) { fails.Add($"{what}: no crown vertices under the skeleton"); return null; }
        if (hairBox == null) { fails.Add($"{what}: no hair {hairId} vertices under the skeleton"); return null; }
        var c = crown.Value.GetCenter();
        var grown = hairBox.Value.Grow(crown.Value.Size.Length() / 2);
        int head = skel.FindBone("Bip01 Head");
        var headAt = head >= 0 ? skel.GetBoneGlobalPose(head).Origin : Vector3.Zero;
        GD.Print($"cap place {what}: crown centre {c} size {crown.Value.Size}, hair {hairId} {hairBox.Value.Position}..{hairBox.Value.End}, head {headAt}");
        if (!grown.HasPoint(c)) fails.Add($"{what}: the crown centre {c} is off the hair {hairId} ({hairBox.Value.Position}..{hairBox.Value.End})");
        // the head bone's +x runs up the head (Biped); the cap point is above the bone
        if (head >= 0)
        {
            var up = skel.GetBoneGlobalPose(head).Basis.Orthonormalized() * Vector3.Right;
            if ((c - headAt).Dot(up) <= 0) fails.Add($"{what}: the crown centre {c} is not above the head bone {headAt}");
        }
        return c;
    }

    static List<MeshInstance3D> Meshes(Skeleton3D skel, int id) =>
        skel.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>()
            .Where(m => !m.IsQueuedForDeletion() && m.Visible && Under(m, skel, $"eq{id}_")).ToList();

    static bool Under(Node n, Node top, string prefix)
    {
        for (; n != null && n != top; n = n.GetParent())
            if (n.Name.ToString().StartsWith(prefix, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>The bounds of the meshes' drawn vertices in the skeleton's space.</summary>
    static Aabb? Bounds(Skeleton3D skel, List<MeshInstance3D> meshes)
    {
        Aabb? box = null;
        var toSkel = skel.GlobalTransform.AffineInverse();
        foreach (var mi in meshes)
        {
            if (mi.Mesh is not ArrayMesh am) continue;
            var s = mi.Skin != null ? mi.GetNodeOrNull<Skeleton3D>(mi.Skeleton) : null;
            for (int si = 0; si < am.GetSurfaceCount(); si++)
            {
                var arr = am.SurfaceGetArrays(si);
                var verts = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                int[] bones = s != null ? arr[(int)Mesh.ArrayType.Bones].AsInt32Array() : null;
                float[] weights = s != null ? arr[(int)Mesh.ArrayType.Weights].AsFloat32Array() : null;
                Transform3D[] binds = null;
                if (s != null)
                {
                    binds = new Transform3D[mi.Skin.GetBindCount()];
                    var sk = toSkel * s.GlobalTransform;
                    for (int j = 0; j < binds.Length; j++)
                    {
                        int b = mi.Skin.GetBindBone(j);
                        if (b < 0) b = s.FindBone(mi.Skin.GetBindName(j));
                        binds[j] = sk * (b >= 0 ? s.GetBoneGlobalPose(b) : Transform3D.Identity) * mi.Skin.GetBindPose(j);
                    }
                }
                var rigid = toSkel * mi.GlobalTransform;
                int per = bones != null && verts.Length > 0 ? bones.Length / verts.Length : 0;
                for (int i = 0; i < verts.Length; i++)
                {
                    Vector3 p;
                    if (binds != null && per > 0)
                    {
                        p = Vector3.Zero;
                        for (int k = 0; k < per; k++)
                        {
                            float w = weights[i * per + k];
                            int j = bones[i * per + k];
                            if (w != 0 && j >= 0 && j < binds.Length) p += binds[j] * verts[i] * w;
                        }
                    }
                    else p = rigid * verts[i];
                    box = box is { } bx ? bx.Expand(p) : new Aabb(p, Vector3.Zero);
                }
            }
        }
        return box;
    }

    async System.Threading.Tasks.Task Settle()
    {
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
