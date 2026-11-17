using Godot;

namespace Ms2Godot;

/// <summary>
/// The simple shadow of a character, npc or monster (the client's CObjectShadow, shadow levels 1 and 2 of table/graphicOption
/// for myPC, PC and monster at every quality but the lowest): a soft dark disc on the ground below the actor, found by a ray
/// down from it, shrinking and fading as the actor leaves the ground. The strength is the field's simpleshadow value (default 0.5).
/// </summary>
public partial class BlobShadow : MeshInstance3D
{
    public static float Strength = 0.5f;

    float radius;
    CollisionObject3D self;
    static Shader shader;
    ShaderMaterial mat;

    public static BlobShadow Add(Node3D owner, float radius)
    {
        if (OS.GetEnvironment("MS2_NOSHADOW") == "1") return null;   // before shots
        shader ??= GD.Load<Shader>("res://shaders/blob_shadow.gdshader");
        var s = new BlobShadow
        {
            Name = "Shadow",
            radius = radius,
            TopLevel = true,
            CastShadow = ShadowCastingSetting.Off,
            Mesh = new QuadMesh { Size = new Vector2(2, 2), Orientation = PlaneMesh.OrientationEnum.Y },
        };
        s.mat = new ShaderMaterial { Shader = shader };
        s.MaterialOverride = s.mat;
        s.self = owner as CollisionObject3D;
        owner.AddChild(s);
        return s;
    }

    public override void _Process(double delta)
    {
        var owner = GetParent<Node3D>();
        var from = owner.GlobalPosition + Vector3.Up * 60;
        var q = PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 4000);
        if (self != null) q.Exclude = new Godot.Collections.Array<Rid> { self.GetRid() };
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(q);
        if (hit.Count == 0) { Visible = false; return; }
        Visible = true;
        var p = (Vector3)hit["position"];
        float air = Mathf.Max(owner.GlobalPosition.Y - p.Y, 0);
        float k = 1f / (1f + air / 400f);
        GlobalTransform = new Transform3D(Basis.Identity.Scaled(Vector3.One * radius * k), p + Vector3.Up * 12);
        mat.SetShaderParameter("strength", Strength * k);
    }
}
