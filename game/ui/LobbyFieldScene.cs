using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.World;

namespace Ms2Godot.Ui;

/// <summary>
/// A lobby field (CSceneStateMulti: the character select and character create maps of table/lobbyfield.xml) with one
/// preview character in it. The camera is the field's Camera0 entity; the preview stands where
/// TCreateCharInfo::ApplyToPC / FUN_14107b7e0 put it: WorldService::FUN_141bd0dd0, the position and rotation of the first
/// spawn point entity of the field (SpawnPointPC).
/// </summary>
public partial class LobbyFieldScene : Field
{
    protected Vector3 spawnPos;
    protected Vector3 spawnFace = Vector3.Back;
    protected Node3D preview;

    protected override bool SpawnsLocalPlayer => false;

    /// <summary>The camera and the spawn point of a built lobby field.</summary>
    protected void PlaceLobby(FieldMap map)
    {
        PlaceCamera(map);
        var sp = map.Entities.FirstOrDefault(e => e.ModelName == "SpawnPointPC");
        if (sp != null)
        {
            spawnPos = FieldBuilder.ToGodotPoint(sp.Position);
            double yaw = sp.RotationDeg.Z * Math.PI / 180;
            spawnFace = FieldBuilder.ToGodotPoint(new Vec3(Math.Sin(yaw), -Math.Cos(yaw), 0));
        }
    }

    /// <summary>The field's Camera0 entity: a Gamebryo camera looks along its local +X with +Y up; the FOV is vertical.</summary>
    void PlaceCamera(FieldMap map)
    {
        var e = map.Entities.FirstOrDefault(x => x.ModelName == "Camera" && x.Name == "Camera0") ?? map.Entities.FirstOrDefault(x => x.ModelName == "Camera");
        if (e == null || camera == null) return;
        var t = e.Transform;
        var origin = t.Apply(new Vec3(0, 0, 0));
        var eye = FieldBuilder.ToGodotPoint(origin);
        var dir = FieldBuilder.ToGodotPoint(t.Apply(new Vec3(1, 0, 0))) - eye;
        var up = FieldBuilder.ToGodotPoint(t.Apply(new Vec3(0, 1, 0))) - eye;
        camera.Fov = (float)e.Number("FOV", 40);
        camera.Near = 10;
        RasterOrigin();   // the frustum window follows the new near plane and FOV
        camera.LookAtFromPosition(eye, eye + dir.Normalized(), up.LengthSquared() > 1e-6f ? up.Normalized() : Vector3.Up);
        GD.Print($"camera {e.Name}: eye {eye} dir {dir.Normalized()} fov {camera.Fov}");
    }

    /// <summary>Replaces the preview character (ReleasePreviewPC, then the new one at the spawn point, idle).</summary>
    protected void ShowPreview(Node3D visual, AnimationPlayer anim)
    {
        ClearPreview();
        if (visual == null) return;
        preview = new Node3D { Name = "Preview" };
        preview.AddChild(visual);
        AddChild(preview);
        preview.GlobalPosition = spawnPos;
        visual.Rotation = new Vector3(0, Mathf.Atan2(spawnFace.X, spawnFace.Z), 0);
        anim?.Play("idle");
        snapPending = true;
    }

    bool snapPending;
    const uint GroundGroups = 0x389;   // CPhysXHelper::RayCastFixedDirection with no mask: groups 0x389

    /// <summary>
    /// FUN_14107b7e0: RayCastFixedDirection from the spawn point straight down with no length limit (m_maxDist 0 is
    /// FLT_MAX in CastRay); on a hit the character goes to the hit point, 1 unit up. Run once the physics space is in.
    /// </summary>
    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);
        if (!snapPending || preview == null) return;
        snapPending = false;
        var q = PhysicsRayQueryParameters3D.Create(spawnPos, spawnPos + Vector3.Down * 1000000f);
        var exclude = new Godot.Collections.Array<Rid>();
        var space = GetWorld3D().DirectSpaceState;
        for (int i = 0; i < 16; i++)
        {
            var hit = space.IntersectRay(q);
            if (hit.Count == 0) return;
            var col = hit["collider"].AsGodotObject();
            int group = col is Node n && n.HasMeta("ms2_group") ? (int)n.GetMeta("ms2_group") : 0;
            if ((GroundGroups & (1u << group)) != 0)
            {
                preview.GlobalPosition = (Vector3)hit["position"] + Vector3.Up;
                return;
            }
            exclude.Add((Rid)hit["rid"]);
            q.Exclude = exclude;
        }
    }

    protected void ClearPreview()
    {
        preview?.QueueFree();
        preview = null;
    }
}
