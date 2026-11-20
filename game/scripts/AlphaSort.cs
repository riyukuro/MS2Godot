using System.Collections.Generic;
using Godot;
using Ms2.Formats.Model;

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
    internal static (Vector3, float) Merge(Vector3 c, float r, Vector3 bc, float br)
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

/// <summary>
/// The client's alpha key for blended character meshes (NPCs, the player, worn items), as AlphaSort gives the field's: the
/// render list processor sorts every alpha object on ComputeDepth = WorldBound.Center . dir - WorldBound.Radius
/// (MS2RenderListProcessor, SetSortByClosestPoint), characters and field alike. A character's world bound moves with it, so it
/// is rebuilt every frame before the draw (FramePreDraw), against the camera of the mesh's own viewport:
/// <list type="bullet">
/// <item>skinned with RECOMPUTE_BOUNDS (ModelMesh.BoneBounds): each skin bone's bound moved by the bone's pose and merged in
/// skin bone order (NiSkinningMeshModifier::UpdateModelBounds, NiSkinningMeshModifier.cpp:1036-1056; NiBound::Merge),
/// then placed by the mesh's transform (ModelMesh.BoneBoundsToRoot);</item>
/// <item>skinned without it: the mesh's model bound at the mesh node (ModelMesh.BoundToRoot) under the skeleton;</item>
/// <item>rigid: the model bound at the mesh's own world transform (NiBound::Update).</item>
/// </list>
/// Godot sorts by |camera - AABB centre| - sorting_offset, so the offset is that distance minus the key (AlphaSort).
/// STILL-OPEN: Godot's sort point for a skinned mesh is its skeleton-updated box; the rest box under the mesh's transform
/// is used here. A worn item's mesh node transform is its own file's, under the body's skeleton.
/// </summary>
public static class CharacterAlphaSort
{
    sealed class Item
    {
        public MeshInstance3D Node;
        public Skeleton3D Skel;           // skinned only
        public Transform3D ToSkel;        // skinned: the bound's frame relative to the skeleton
        public Vector3 Center;            // the model bound
        public float Radius;
        public int[] Bones;               // RECOMPUTE_BOUNDS: the skeleton bone of each skin bone, in skin bone order
        public Vector3[] BoneCenters;
        public float[] BoneRadii;
    }

    static readonly List<Item> items = new();
    static bool hooked;

    /// <summary>The meshes registered and still alive (tests).</summary>
    public static int Count => items.Count;

    /// <summary>Registers a blended character mesh (ModelMesh.Blend not Opaque) that has a model bound.</summary>
    public static void Register(MeshInstance3D mi, ModelMesh m, Skeleton3D skel, System.Func<int, int> skelBone)
    {
        if (m.Blend == BlendForm.Opaque || m.Bound == null) return;
        var it = new Item { Node = mi, Center = new Vector3(m.Bound[0], m.Bound[1], m.Bound[2]), Radius = m.Bound[3] };
        if (m.Skinned && skel != null)
        {
            it.Skel = skel;
            it.ToSkel = ModelBuilder.ToGodot(m.BoundToRoot);
            if (m.BoneBounds is { Count: > 0 } bb)
            {
                var bones = new int[bb.Count];
                bool all = true;
                for (int i = 0; i < bb.Count && all; i++) all = (bones[i] = skelBone(bb[i].Bone)) >= 0;
                if (all)
                {
                    it.Bones = bones;
                    it.BoneCenters = new Vector3[bb.Count];
                    it.BoneRadii = new float[bb.Count];
                    for (int i = 0; i < bb.Count; i++)
                    {
                        it.BoneCenters[i] = new Vector3(bb[i].Bound[0], bb[i].Bound[1], bb[i].Bound[2]);
                        it.BoneRadii[i] = bb[i].Bound[3];
                    }
                    it.ToSkel = ModelBuilder.ToGodot(m.BoneBoundsToRoot);
                }
            }
        }
        items.Add(it);
        if (!hooked) { RenderingServer.FramePreDraw += Update; hooked = true; }
    }

    /// <summary>The world bound (Godot space) of a registered mesh now.</summary>
    static (Vector3 c, float r) WorldBound(Item it)
    {
        if (it.Bones != null)
        {
            Vector3 c = default; float r = 0;
            for (int i = 0; i < it.Bones.Length; i++)
            {
                var pose = it.Skel.GetBoneGlobalPose(it.Bones[i]);
                var bc = pose * it.BoneCenters[i];
                float br = pose.Basis.Scale.X * it.BoneRadii[i];
                if (i == 0) { c = bc; r = br; }
                else (c, r) = AlphaSort.Merge(c, r, bc, br);
            }
            var w = it.Skel.GlobalTransform * it.ToSkel;
            return (w * c, w.Basis.Scale.X * r);
        }
        var x = it.Skel != null ? it.Skel.GlobalTransform * it.ToSkel : it.Node.GlobalTransform;
        return (x * it.Center, x.Basis.Scale.X * it.Radius);
    }

    /// <summary>Sets each live mesh's SortingOffset so Godot's depth is the client's key; drops freed meshes.</summary>
    public static void Update()
    {
        for (int i = items.Count - 1; i >= 0; i--)
        {
            var it = items[i];
            if (!GodotObject.IsInstanceValid(it.Node) || it.Skel != null && !GodotObject.IsInstanceValid(it.Skel))
            {
                items[i] = items[^1];
                items.RemoveAt(items.Count - 1);
                continue;
            }
            if (!it.Node.IsInsideTree()) continue;
            var camera = it.Node.GetViewport()?.GetCamera3D();
            if (camera == null) continue;
            var xf = camera.GlobalTransform;
            var cam = xf.Origin;
            var fwd = -xf.Basis.Z.Normalized();
            var (c, r) = WorldBound(it);
            var godotCenter = (it.Node.GlobalTransform * it.Node.GetAabb()).GetCenter();
            it.Node.SortingOffset = cam.DistanceTo(godotCenter) - ((c - cam).Dot(fwd) - r);
        }
    }
}
