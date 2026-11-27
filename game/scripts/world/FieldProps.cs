using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.World;

namespace Ms2Godot.World;

/// <summary>
/// Field props whose NIF animates itself: NiTransformController on its nodes (flags, signs, wheels, the merchant cart, the portal
/// marks). FieldBuilder draws every other prop as a static MultiMesh; these are taken over (FieldBuilder.DrawsInstance) and built
/// per entity as a Skeleton3D (the NIF's nodes), skinned and rigid meshes with the field material, and an AnimationPlayer that
/// loops the controllers from the time the field is shown, as Gamebryo's time controllers run from the scene's update time.
/// </summary>
public sealed class FieldProps
{
    readonly ModelBuilder builder;
    readonly Dictionary<string, (ModelData md, KfAnimation anim)> byLlid = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<(ModelData, int, Color, bool), ArrayMesh> meshes = new();
    readonly Dictionary<ModelData, Godot.Animation> anims = new();
    public readonly List<FieldEntity> Entities = new();
    /// <summary>The entities that receive no sun shadow (FieldBuilder.NotReceivingElsewhere).</summary>
    public readonly HashSet<FieldEntity> NotReceiving = new();
    /// <summary>The field lights that do not reach an entity, by entity (FieldBuilder.LightOffElsewhere); absent: all reach it.</summary>
    public readonly Dictionary<FieldEntity, uint> LightOff = new();
    static readonly StringName LightOffParam = "ms2_light_off";
    static readonly StringName ReceiveParam = "ms2_receive";
    public int Built, Animated, Frozen;
    float freezeAt;
    readonly Dictionary<FieldEntity, Node3D> nodes = new();
    readonly Dictionary<FieldEntity, bool> shown = new();
    /// <summary>The players of the running (not frozen) nodes, and when each hidden one was hidden (<see cref="Clock"/>).</summary>
    readonly Dictionary<FieldEntity, AnimationPlayer> players = new();
    readonly Dictionary<FieldEntity, double> hiddenAt = new();
    readonly Dictionary<FieldEntity, List<PropMesh>> meshesOf = new();

    /// <summary>One mesh of a built prop: its node, its model mesh, and the skeleton it hangs under (whose bones are the
    /// model's, in <see cref="ModelData.Bones"/> order).</summary>
    public readonly record struct PropMesh(MeshInstance3D Node, ModelMesh Mesh, Skeleton3D Skel);

    /// <summary>The time in seconds a hidden node's controllers keep running by (a check sets its own).</summary>
    public Func<double> Clock { get; set; } = Seconds;
    static double Seconds() => Time.GetTicksUsec() / 1e6;

    const float DefaultMaterialColor = 0.7058824f;

    public FieldProps(ModelBuilder builder) { this.builder = builder; }

    /// <summary>FieldBuilder hook: true when this model animates itself, so the entity is drawn here instead of in a MultiMesh.</summary>
    public bool TakesModel(string llid, ModelData md, KfAnimation anim)
    {
        if (anim == null) return false;
        // only animations that move a node the model has (or a rigid mesh)
        if (!anim.Channels.Keys.Any(n => md.BoneIndex(n) >= 0 || md.Meshes.Any(m => !m.Skinned && m.Name == n))) return false;
        byLlid[llid] = (md, anim);
        return true;
    }

    /// <summary>Builds the taken-over entities under a Z-up parent (the field's World node convention).</summary>
    public Node3D Build()
    {
        var root = new Node3D { Name = "AnimatedProps", Transform = FieldBuilder.ZupToYup };
        var rng = new Random(1);
        freezeAt = ModelBuilder.FreezeTimeNow();
        foreach (var e in Entities)
        {
            var llid = e.Get("NifAsset");
            if (llid == null || !byLlid.TryGetValue(llid, out var m)) continue;
            var c = e.Numbers("MaterialColor");
            var col = c != null && c.Length >= 3 ? new Color((float)c[0], (float)c[1], (float)c[2])
                                                 : new Color(DefaultMaterialColor, DefaultMaterialColor, DefaultMaterialColor);
            bool frozen = FieldBuilder.IsStatic(e);
            var list = new List<PropMesh>();
            var node = BuildOne(m.md, m.anim, col, e.Name ?? e.ModelName, e.CastsShadow && FieldShadows.MapObjects, !NotReceiving.Contains(e), frozen,
                                LightOff.TryGetValue(e, out var off) ? off : 0, list);
            meshesOf[e] = list;
            node.Transform = ModelBuilder.ToGodot(e.Transform);
            nodes[e] = node;
            if (shown.TryGetValue(e, out bool show) && !show) { Show(node, false); hiddenAt[e] = Clock(); }
            root.AddChild(node);
            Built++;
            var player = node.GetNode<AnimationPlayer>("Model/AnimationPlayer");
            if (player.HasAnimation("ctrl"))
            {
                player.Play("ctrl");
                // RandomStartDelay: the entity starts its controllers up to that many seconds late
                double delay = e.Number("RandomStartDelay");
                if (delay > 0) player.Seek(rng.NextDouble() * Math.Min(delay, player.CurrentAnimationLength), true);
                // a static entity is updated once when it is added (FieldBuilder.IsStatic): its nodes hold that pose
                if (frozen) { player.Seek(player.CurrentAnimationPosition, true); player.Pause(); Frozen++; }
                else { Animated++; players[e] = player; }
            }
        }
        return root;
    }

    /// <summary>
    /// Shows or hides the entity's node (world/FieldInteractDraws: an interact mesh's own model or its reactable twin, by its
    /// NifAsset). Kept until <see cref="Build"/> makes the node when that comes later. A hidden node is not processed either
    /// (its player does not advance), and shown again its controllers are advanced by the time it was hidden, so they are
    /// where the scene's time puts them: the client's swap sets NifAsset (CInteractMeshObject::FUN_1413ff690,
    /// interactobject.cpp:909-916) and the scene graph service attaches a fresh clone of the asset, whose controllers have
    /// never been updated (NiTimeController::CopyMembers, NiTimeController.cpp:483-485). Their first update is then at the
    /// game time clock (ms2scenegraphservice.cpp:304-306): an APP_TIME controller takes that time whole and an APP_INIT one
    /// starts at 0 (ComputeScaledTime, NiTimeController.cpp:110-142). Every interact mesh model drawn here is APP_TIME on
    /// both data sets. One <see cref="AnimationPlayer.Advance"/> per swap, nothing per frame while hidden.
    /// </summary>
    public void SetShown(FieldEntity e, bool show)
    {
        if (e == null) return;
        bool was = Shown(e);
        shown[e] = show;
        if (!nodes.TryGetValue(e, out var node) || !GodotObject.IsInstanceValid(node)) return;
        Show(node, show);
        if (show == was) return;
        if (!show) hiddenAt[e] = Clock();
        else if (hiddenAt.Remove(e, out double at) && players.TryGetValue(e, out var player) && GodotObject.IsInstanceValid(player))
            player.Advance(Math.Max(0, Clock() - at));
    }

    /// <summary>Whether the entity's node is shown (as set, or as built: shown).</summary>
    public bool Shown(FieldEntity e) => e == null || !shown.TryGetValue(e, out bool s) || s;

    /// <summary>The entity's meshes once built, else none (world/FieldFade picks and fades them).</summary>
    public IReadOnlyList<PropMesh> MeshesOf(FieldEntity e) => e != null && meshesOf.TryGetValue(e, out var l) ? l : Array.Empty<PropMesh>();

    /// <summary>The entity's node once built, else null.</summary>
    public Node3D NodeOf(FieldEntity e) => e != null && nodes.TryGetValue(e, out var n) && GodotObject.IsInstanceValid(n) ? n : null;

    static void Show(Node3D node, bool show)
    {
        node.Visible = show;
        node.ProcessMode = show ? Node.ProcessModeEnum.Inherit : Node.ProcessModeEnum.Disabled;
    }

    /// <param name="casts">The entity casts the sun's shadow: FieldEntity.CastsShadow at the map-object shadow level. The client
    /// sends these entities through the same SceneGraphAdded path as every placed entity (FUN_141567000 then FUN_141bbd490,
    /// worldservice_1.cpp:3637-3638), whose SetShadowCaster also skips "NoShadow"-tagged subtrees (ModelMesh.NoShadow).</param>
    /// <param name="receives">The entity takes the sun's shadow (ShadowReceivers); else every mesh's ms2_receive is 0, as in
    /// FieldBuilder.</param>
    /// <param name="lightOff">The field lights that do not reach the entity (FieldLightSet.Unlit), as each mesh's ms2_light_off.</param>
    /// <param name="built">Each mesh made, in model order.</param>
    Node3D BuildOne(ModelData md, KfAnimation kf, Color col, string name, bool casts, bool receives, bool frozen, uint lightOff, List<PropMesh> built)
    {
        var root = new Node3D { Name = name.ValidateNodeName() };
        var model = new Node3D { Name = "Model", Transform = ModelBuilder.ToGodot(md.RootLocal) };
        root.AddChild(model);
        var skel = new Skeleton3D { Name = ModelBuilder.SkeletonName };
        model.AddChild(skel);
        foreach (var b in md.Bones) skel.AddBone(b.Name);
        ModelBuilder.SetRests(skel, md);
        var meshPaths = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int mi = 0; mi < md.Meshes.Count; mi++)
        {
            var m = md.Meshes[mi];
            var inst = new MeshInstance3D { Name = $"m{mi}", Mesh = Mesh(md, mi, col, frozen) };
            if (!casts || m.NoShadow) inst.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            if (!receives) inst.SetInstanceShaderParameter(ReceiveParam, 0f);
            if (lightOff != 0) inst.SetInstanceShaderParameter(LightOffParam, (float)lightOff);
            var skin = m.Skinned ? ModelBuilder.SkinOf(md, m) : null;
            if (m.Skinned)
            {
                skel.AddChild(inst);
                inst.Skin = skin;
                inst.Skeleton = "..";
            }
            else if (m.ParentBone >= 0)
            {
                var att = new BoneAttachment3D { Name = $"a{mi}", BoneName = md.Bones[m.ParentBone].Name };
                skel.AddChild(att);
                inst.Transform = ModelBuilder.ToGodot(m.Local);
                att.AddChild(inst);
                meshPaths.TryAdd(m.Name ?? "", $"{ModelBuilder.SkeletonName}/a{mi}/m{mi}");
            }
            else
            {
                inst.Transform = ModelBuilder.ToGodot(m.Local);
                skel.AddChild(inst);
                meshPaths.TryAdd(m.Name ?? "", $"{ModelBuilder.SkeletonName}/m{mi}");
            }
            builder.AddGlowTwin(inst, m, m.Skinned ? skin : null, frozen ? freezeAt : -1f);
            built.Add(new PropMesh(inst, m, skel));
        }
        var player = new AnimationPlayer { Name = "AnimationPlayer" };
        model.AddChild(player);
        player.RootNode = "..";
        var lib = new AnimationLibrary();
        var a = Anim(md, kf, meshPaths);
        if (a != null) lib.AddAnimation("ctrl", a);
        player.AddAnimationLibrary("", lib);
        return root;
    }

    Godot.Animation Anim(ModelData md, KfAnimation kf, Dictionary<string, string> meshPaths)
    {
        if (anims.TryGetValue(md, out var a)) return a;
        a = ModelBuilder.BuildAnimation(kf, md);
        a.LoopMode = kf.Cycle switch { 1 => Godot.Animation.LoopModeEnum.Pingpong, 2 => Godot.Animation.LoopModeEnum.None, _ => Godot.Animation.LoopModeEnum.Linear };
        // controllers on rigid meshes: node tracks on the mesh instance (its transform relative to the parent node)
        int nfr = Math.Max(2, (int)Math.Round(kf.Duration * 30) + 1);
        foreach (var (target, ch) in kf.Channels)
        {
            if (md.BoneIndex(target) >= 0 || !meshPaths.TryGetValue(target, out var path)) continue;
            var np = new NodePath(path);
            if (ch.T != null) { int tr = a.AddTrack(Godot.Animation.TrackType.Position3D); a.TrackSetPath(tr, np); for (int f = 0; f < nfr; f++) { double t = kf.Duration * f / (nfr - 1); var v = ch.T(t); a.PositionTrackInsertKey(tr, t, new Vector3((float)v.X, (float)v.Y, (float)v.Z)); } }
            if (ch.R != null)
            {
                int tr = a.AddTrack(Godot.Animation.TrackType.Rotation3D); a.TrackSetPath(tr, np);
                Quaternion? prev = null;
                for (int f = 0; f < nfr; f++)
                {
                    double t = kf.Duration * f / (nfr - 1); var r = ch.R(t);
                    var q = new Quaternion((float)r.X, (float)r.Y, (float)r.Z, (float)r.W).Normalized();
                    if (prev is Quaternion p && p.Dot(q) < 0) q = -q;
                    prev = q;
                    a.RotationTrackInsertKey(tr, t, q);
                }
            }
            if (ch.S != null) { int tr = a.AddTrack(Godot.Animation.TrackType.Scale3D); a.TrackSetPath(tr, np); for (int f = 0; f < nfr; f++) { double t = kf.Duration * f / (nfr - 1); float s = (float)ch.S(t); a.ScaleTrackInsertKey(tr, t, new Vector3(s, s, s)); } }
        }
        if (a.GetTrackCount() == 0) a = null;
        return anims[md] = a;
    }

    /// <summary>The field mesh with the entity's material colour as the vertex COLOR (field_lit reads the material from COLOR).</summary>
    ArrayMesh Mesh(ModelData md, int mi, Color col, bool frozen)
    {
        frozen &= md.Meshes[mi].AnimatesMaterial;
        if (meshes.TryGetValue((md, mi, col, frozen), out var mesh)) return mesh;
        var m = md.Meshes[mi];
        var src = builder.BuildMesh(m, skin: m.Skinned, instanceColor: true);
        var arrays = src.SurfaceGetArrays(0);
        int nv = ((Vector3[])arrays[(int)Godot.Mesh.ArrayType.Vertex]).Length;
        var colors = new Color[nv];
        Array.Fill(colors, col);
        arrays[(int)Godot.Mesh.ArrayType.Color] = colors;
        ulong format = ModelBuilder.FieldCustomFormat(m);
        mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays, null, null, (Godot.Mesh.ArrayFormat)format);
        mesh.SurfaceSetMaterial(0, src.SurfaceGetMaterial(0));
        mesh.CustomAabb = src.CustomAabb;   // a billboard's or morph's swept bounds (ModelBuilder.BuildMesh)
        if (frozen) ModelBuilder.Freeze(mesh, freezeAt);
        return meshes[(md, mi, col, frozen)] = mesh;
    }
}
