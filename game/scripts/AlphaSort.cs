using System.Collections.Generic;
using Godot;

namespace Ms2Godot;

/// <summary>
/// Gives the field's blended draws the client's alpha key. The client sorts its alpha list with CompareAlpha
/// (MS2RenderListProcessor, GetAlpha() larger first) on one scalar per object:
/// <list type="bullet">
/// <item>a plain alpha object: ComputeDepth = WorldBound.Center . dir - WorldBound.Radius, because the processor's constructor
/// calls SetSortByClosestPoint(true);</item>
/// <item>a shell composite: AlphaShellComposite::Update = the mean of its shells' WorldBound.Center . dir, no radius.</item>
/// </list>
/// Godot 4 sorts perspective transparents by |camera - AABB centre| - sorting_offset, larger first within a priority. So once per
/// frame, after the camera has moved (FramePreDraw), each entry's SortingOffset is set to that distance minus the client key, and
/// Godot's depth becomes the key. The constant camera . dir term of the client's dot product drops out of every comparison.
/// </summary>
public partial class AlphaSort : Node
{
    public sealed class Entry
    {
        public GeometryInstance3D Node;
        /// <summary>The client's world bound in the field's local (Z-up) space; Radius 0 for a composite.</summary>
        public Vector3 Center;
        public float Radius;
        /// <summary>A composite's key point is its draw box centre (Godot's own sort point), so Center is ignored.</summary>
        public bool Composite;
        /// <summary>Kept on top of the key: an own-group shell twin sorts this far behind its colour.</summary>
        public float Extra;
        /// <summary>A plain entry's model bound (shared with the mesh), so its world bound can be rebuilt in another member order.</summary>
        internal float[] Bound;
        internal Vector3 GodotCenter, KeyPoint;
        internal bool Ready;
    }

    public readonly List<Entry> Entries;
    readonly Node3D space;
    Callable hook;
    Transform3D last;
    bool any;

    public AlphaSort(List<Entry> entries, Node3D space)
    {
        Entries = entries;
        this.space = space;
        Name = "AlphaSort";
    }

    public override void _EnterTree()
    {
        hook = Callable.From(Update);
        RenderingServer.Singleton.Connect(RenderingServer.SignalName.FramePreDraw, hook);
    }

    public override void _ExitTree() => RenderingServer.Singleton.Disconnect(RenderingServer.SignalName.FramePreDraw, hook);

    void Update()
    {
        var camera = GetViewport()?.GetCamera3D();
        if (camera == null || !space.IsInsideTree()) return;
        var xf = camera.GlobalTransform;
        if (any && xf.IsEqualApprox(last)) return;
        last = xf; any = true;
        var cam = xf.Origin;
        var fwd = -xf.Basis.Z.Normalized();
        foreach (var e in Entries)
        {
            if (!e.Ready)
            {
                // Godot measures to the centre of the instance's transformed AABB (render_forward_clustered.cpp, _fill_render_list)
                // (the custom box when one is set: an own composite's shared box)
                var box = e.Node.CustomAabb.Size != Vector3.Zero ? e.Node.CustomAabb : e.Node.GetAabb();
                e.GodotCenter = (e.Node.GlobalTransform * box).GetCenter();
                e.KeyPoint = e.Composite ? e.GodotCenter : space.GlobalTransform * e.Center;
                e.Ready = true;
            }
            float key = (e.KeyPoint - cam).Dot(fwd) - e.Radius;
            e.Node.SortingOffset = cam.DistanceTo(e.GodotCenter) - key + e.Extra;
        }
    }

    /// <summary>
    /// The client's world bound of one alpha object: the mesh's model bound (NiMesh "Bounding Sphere") moved by each member's
    /// world transform (NiBound::Update: c = s R c0 + T, r = s r0), then merged in member order with NiBound::Merge, skipping
    /// members of radius 0 after the first (NiInstancingMeshModifier::GatherInstanceNodeTransforms). One member is a plain
    /// entity's own NiMesh world bound.
    /// </summary>
    public static (Vector3 center, float radius) WorldBound(float[] bound, IEnumerable<Transform3D> members)
    {
        var c0 = new Vector3(bound[0], bound[1], bound[2]);
        bool first = true;
        Vector3 c = default; float r = 0;
        foreach (var w in members)
        {
            var bc = w * c0;
            float br = w.Basis.Scale.X * bound[3];
            if (first) { c = bc; r = br; first = false; continue; }
            if (br != 0) (c, r) = Merge(c, r, bc, br);
        }
        return (c, r);
    }

    /// <summary>NiBound::Merge (NiBound_Win32.cpp): this = (c, r), pBound = (bc, br).</summary>
    static (Vector3, float) Merge(Vector3 c, float r, Vector3 bc, float br)
    {
        var diff = c - bc;
        float lengthSqr = diff.Dot(diff);
        float deltaRad = br - r;
        float deltaRadSqr = deltaRad * deltaRad;
        if (deltaRad >= 0f)
        {
            if (deltaRadSqr >= lengthSqr) return (bc, br);
        }
        else if (deltaRadSqr >= lengthSqr) return (c, r);
        float length = Mathf.Sqrt(lengthSqr);
        if (length > 1e-6f) c = bc + (length - deltaRad) / (2f * length) * diff;
        return (c, 0.5f * (br + length + r));
    }
}
