using System;
using Godot;
using Ms2.Formats.World;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/field_camera.tscn) of the field camera's frustum: the client's field camera is the
/// MS2GameplayCamera entity, whose flat sets the frustum to near 10 and far 100000 (CameraService.cpp:273-350,
/// NiCamera::SetViewFrustum's clamp, <see cref="CameraFrustum"/>); the camera modes' 250 / 18000 are its depth of field.
/// The near plane of 250 cut off what a close-up cut scene camera looks at (in the Ellinel lecture hall, Kaitlyn's face
/// and front, and the near floor). Also the near plane the blocked check casts from (CTargetCameraMode::vfunc_38,
/// targetcameramode.cpp:732-736): half height near·tan(FOV) with the whole FOV of the mode's setting, half width that
/// times the aspect.
///   godot-mono --headless --path . res://tests/field_camera.tscn -- --field 52000100_qd --no-npcs
/// Quits with 0 when it holds, 1 when not, 2 when the field does not load in time.
/// </summary>
public partial class FieldCameraCheck : Node
{
    const int LoadFrames = 60 * 300;
    int frames;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        CallDeferred(MethodName.Move);   // stay on the root across the scene change
    }

    void Move()
    {
        var root = GetTree().Root;
        GetParent().RemoveChild(this);
        root.AddChild(this);
        GetTree().ChangeSceneToFile("res://field.tscn");
    }

    public override void _Process(double delta)
    {
        if (!(Field.LoadComplete && GetTree().CurrentScene is Field field))
        {
            if (++frames > LoadFrames) { GD.Print("field camera: FAIL the field did not load"); GetTree().Quit(2); }
            return;
        }
        if (GetWindow().Size != new Vector2I(1280, 720))
        {
            GetWindow().Size = new Vector2I(1280, 720);   // the fixed values below are for a 16:9 frame
            return;
        }
        var cam = GetViewport().GetCamera3D();
        int fails = 0;
        void Check(bool ok, string what) { GD.Print($"field camera: {(ok ? "ok" : "FAIL")} {what}"); if (!ok) fails++; }
        Check(cam != null, "a current camera");
        if (cam != null)
        {
            Check(Mathf.IsEqualApprox(cam.Near, 10) && Mathf.IsEqualApprox(cam.Far, 100000), $"near {cam.Near} far {cam.Far}, the client's 10 / 100000");
            // fixed values for field 52000100 (its camera FOV is 40) in a 1280 x 720 window: half height 10·tan(40°),
            // half width that times 16/9. The live camera's FOV is set to a cut scene's 20 first: the blocked check takes
            // the mode's FOV, so the points must not move.
            float fov = cam.Fov;
            cam.Fov = 20;
            var half = field.BlockedCheckHalfExtents();
            cam.Fov = fov;
            Check(Mathf.Abs(half.Y - 8.391f) < 0.001f && Mathf.Abs(half.X - 14.917f) < 0.001f,
                  $"blocked check near plane half {half.X:F3} x {half.Y:F3} with the live fov at 20, the client's 14.917 x 8.391");
        }
        GD.Print(fails == 0 ? "field camera: PASS" : $"field camera: FAIL ({fails})");
        GetTree().Quit(fails == 0 ? 0 : 1);
    }
}
