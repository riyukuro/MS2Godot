using Godot;
using Ms2.Formats.Pack;

namespace Ms2Godot;

/// <summary>
/// The simple shadow of a character, npc or monster: the client's CObjectShadow. A square of side <c>radius</c> centred on the
/// ground point below the owner, textured with Shadow.dds, laid along the ground normal and lifted by a small offset (3 for
/// players, 1 for npcs), alpha-blended black with alpha = the field's simpleshadow (default 0.5) times a height fade: the
/// square and its alpha shrink to half at 400 units above the ground. The ground is found with a ray straight down from the
/// owner; with no hit the shadow is hidden. FieldShadows.Character adds it where the client shows it.
/// </summary>
public partial class BlobShadow : MeshInstance3D
{
    static readonly StringName StrengthParam = "strength";
    public const string Archive = "Resource/Image";
    public const string Entry = "effect/shadow.dds";
    public const float FadeHeight = 400f, FadeScale = 0.5f;

    /// <summary>&lt;shadow simpleshadow&gt; of the field.</summary>
    public static float Strength = 0.5f;
    static Texture2D texture;
    static Shader shader;

    float radius, lift;
    CollisionObject3D self;
    ShaderMaterial mat;

    /// <summary>Loads Shadow.dds from the client's image archive.</summary>
    public static void Load(PackSet packs)
    {
        if (texture != null) return;
        try
        {
            var img = new Image();
            if (img.LoadDdsFromBuffer(packs.Read(Archive, Entry)) == Error.Ok) { texture = ImageTexture.CreateFromImage(img); }
        }
        catch (Exception ex) { GD.PushWarning("shadow texture: " + ex.Message); }
    }

    public static BlobShadow Add(Node3D owner, float radius, float lift = 3f)
    {
        if (OS.GetEnvironment("MS2_NOSHADOW") == "1" || radius <= 0 || texture == null) return null;   // before shots
        shader ??= GD.Load<Shader>("res://shaders/blob_shadow.gdshader");
        var s = new BlobShadow
        {
            Name = "Shadow",
            radius = radius,
            lift = lift,
            TopLevel = true,
            CastShadow = ShadowCastingSetting.Off,
            // CObjectShadow vertices (0,0) (0,1) (1,1) (1,0), centred: a unit square, corners at +-0.5
            Mesh = new QuadMesh { Size = new Vector2(1, 1), Orientation = PlaneMesh.OrientationEnum.Y },
        };
        s.mat = new ShaderMaterial { Shader = shader };
        s.mat.SetShaderParameter(ShaderParam.ShadowTex, texture);
        s.MaterialOverride = s.mat;
        s.self = owner as CollisionObject3D;
        owner.AddChild(s);
        return s;
    }

    // One query and one exclude list per shadow, reused: a new RefCounted query, Array and result Dictionary every frame
    // for every character was most of the managed frame time. The ground under an owner that has not moved does not change
    // either, so a still owner re-casts only now and then (a moving owner every frame).
    PhysicsRayQueryParameters3D query;
    Vector3 lastPos = new(float.NaN, 0, 0);
    int idleFrames;
    const int RecastEvery = 90;
    const float MovedSq = 1f;

    public override void _Process(double delta)
    {
        var owner = GetParent<Node3D>();
        var pos = owner.GlobalPosition;
        if (pos.DistanceSquaredTo(lastPos) < MovedSq && ++idleFrames < RecastEvery) return;
        idleFrames = 0;
        lastPos = pos;
        var from = pos + Vector3.Up * 60;
        if (query == null)
        {
            query = PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 4000);
            if (self != null) query.Exclude = new Godot.Collections.Array<Rid> { self.GetRid() };
        }
        query.From = from;
        query.To = from + Vector3.Down * 4000;
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0) { Visible = false; return; }
        Visible = true;
        var p = (Vector3)hit[PhysicsKeys.Position];
        var n = ((Vector3)hit[PhysicsKeys.Normal]).Normalized();
        float dist = Mathf.Max(pos.Y - p.Y, 0);
        float fade = 1f - Mathf.Clamp(dist / FadeHeight, 0f, 1f) * (1f - FadeScale);
        // orient: local up along the ground normal, keep a fixed heading
        var x = (Vector3.Right - n * n.Dot(Vector3.Right)).Normalized();
        var basis = new Basis(x, n, x.Cross(n)).Scaled(Vector3.One * radius * fade);
        // + 10: the client lifts by 3 on a depth buffer with its bias; Godot z-fights at the camera distance without more
        GlobalTransform = new Transform3D(basis, p + n * (lift + 10f));
        mat.SetShaderParameter(StrengthParam, Strength * fade);
    }
}
