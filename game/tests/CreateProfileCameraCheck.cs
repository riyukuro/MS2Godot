using Godot;
using Ms2Godot.Ui.Create;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/create_profile_camera.tscn): the camera of the create page's profile shot converts through
/// Ms2Bloom.GammaOut() like every other 3D view (the character shader writes gamma values), and keeps the page camera's
/// projection. Quits with 0 when it does, else 1.
/// </summary>
public partial class CreateProfileCameraCheck : Node
{
    public override void _Ready()
    {
        var page = new Camera3D { Fov = 33f, Near = 0.5f, Far = 900f, CullMask = 7u };
        var cam = CharacterCreateScene.ProfileCamera(page);
        bool convert = false;
        if (cam.Compositor?.CompositorEffects is { } effects)
            foreach (var e in effects) convert |= e is Ms2Bloom { ConvertOnly: true };
        bool same = cam.Fov == 33f && cam.Near == 0.5f && cam.Far == 900f && cam.CullMask == 7u;
        if (!convert) GD.PrintErr("FAIL the profile camera has no Ms2Bloom conversion");
        if (!same) GD.PrintErr("FAIL the profile camera lost the page camera's projection");
        GD.Print(convert && same ? "create_profile_camera: ALL OK" : "create_profile_camera: FAILED");
        GetTree().Quit(convert && same ? 0 : 1);
    }
}
