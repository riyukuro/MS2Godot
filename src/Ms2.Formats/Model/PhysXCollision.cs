using Maple2.File.IO.Nif;
using Ms2.Formats.Nif;

namespace Ms2.Formats.Model;

/// <summary>One collision triangle mesh of a NIF's NiPhysXProp, in model space and file units.</summary>
public sealed class CollisionMesh
{
    public float[] Vertices;      // xyz
    public int[] Triangles;       // 3 indices per triangle
    public bool Convex;
}

/// <summary>
/// Reads the PhysX collision stored in a NIF (NiPhysXProp -> NiPhysXPropDesc -> actors -> shape descs -> cooked NXS
/// mesh). Vertex = PhysXToWorldScale * (ActorPose * (ShapeLocalPose * v)), the same chain the server ingest uses.
/// Only mesh shapes (convex / triangle) are read; MS2 map data uses no other shape type.
/// </summary>
public static class PhysXCollision
{
    /// <summary>NxMat34 from nif.xml Matrix34: 9 rotation floats in file order (row-major for column vectors) + translation.</summary>
    public static Xform Matrix34(NifStruct m)
    {
        var v = m.Values.Select(x => (double)(float)x).ToArray();
        return new Xform(new Vec3(v[9], v[10], v[11]), v[..9], 1.0);
    }

    public static List<CollisionMesh> Load(NifFile nif)
    {
        var B = nif.Blocks;
        var o = new List<CollisionMesh>();
        foreach (var prop in B.Where(b => b.Type == "NiPhysXProp"))
        {
            double scale = prop.Float("PhysX to World Scale");
            long snap = prop.Int("Snapshot");
            if (snap < 0) continue;
            foreach (var ai in B[(int)snap].Ints("Actors"))
            {
                if (ai < 0) continue;
                var actor = B[(int)ai];
                var poses = actor.Structs("Poses");
                var pose = poses.Length > 0 ? Matrix34(poses[0]) : Xform.Identity;
                foreach (var si in actor.Ints("Shape Descriptions"))
                {
                    if (si < 0) continue;
                    var sh = B[(int)si];
                    long type = sh.Int("Shape Type");
                    if (type != 5 && type != 6) continue;
                    long mi = sh.Int("Mesh");
                    if (mi < 0) continue;
                    var md = B[(int)mi];
                    var data = md.Struct("Mesh Data").Bytes("Data");
                    PhysXMesh px;
                    try { px = new PhysXMesh(data); }
                    catch { continue; }
                    var local = pose.Compose(Matrix34(sh.Struct("Local Pose")));
                    var world = new Xform(local.T * scale, local.R, local.S * scale);
                    var cm = new CollisionMesh
                    {
                        Vertices = new float[px.Vertices.Count * 3],
                        Triangles = new int[px.Faces.Count * 3],
                        Convex = type == 5,
                    };
                    for (int i = 0; i < px.Vertices.Count; i++)
                    {
                        var p = world.Apply(new Vec3(px.Vertices[i].X, px.Vertices[i].Y, px.Vertices[i].Z));
                        cm.Vertices[i * 3] = (float)p.X; cm.Vertices[i * 3 + 1] = (float)p.Y; cm.Vertices[i * 3 + 2] = (float)p.Z;
                    }
                    for (int i = 0; i < px.Faces.Count; i++)
                    {
                        cm.Triangles[i * 3] = (int)px.Faces[i].Vert0;
                        cm.Triangles[i * 3 + 1] = (int)px.Faces[i].Vert1;
                        cm.Triangles[i * 3 + 2] = (int)px.Faces[i].Vert2;
                    }
                    o.Add(cm);
                }
            }
        }
        return o;
    }
}
