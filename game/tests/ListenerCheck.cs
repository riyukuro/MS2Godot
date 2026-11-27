using System;
using System.Collections.Generic;
using Godot;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/listener.tscn) of where FieldAudio says the 3D sounds are heard from
/// (<see cref="FieldAudio.ListenerAt"/>, ListenerRight, ListenerUp), which the curves' gains and pan strengths are worked
/// out from:
/// - with no viewer and a current listener away from the camera (the field's own, at the camera target: Field.cs), the
///   listener's place and basis, the ones Godot pans by, not the camera's;
/// - with a viewer, the viewer's place with the camera's right made level;
/// - the viewer gone again, the field's listener once more.
/// Needs no data. Quits with 0 when it holds, else 1.
/// </summary>
public partial class ListenerCheck : Node3D
{
    readonly List<string> fails = new();
    FieldAudio audio;
    AudioListener3D fieldListener;
    Camera3D camera;
    Vector3? viewer;
    int frames;

    public override void _Ready()
    {
        camera = new Camera3D { Position = new Vector3(0, 800, 900) };
        AddChild(camera);
        camera.LookAt(Vector3.Zero, Vector3.Up);
        camera.MakeCurrent();
        // the field's listener: at the camera target, turned as the camera (Field.cs)
        fieldListener = new AudioListener3D();
        AddChild(fieldListener);
        fieldListener.GlobalTransform = new Transform3D(camera.GlobalBasis, new Vector3(30, 0, -20));
        fieldListener.MakeCurrent();
        audio = new FieldAudio { Viewer = () => viewer };
        AddChild(audio);
    }

    void Expect(string what, Vector3 at, Vector3 right, Vector3 up)
    {
        if (!audio.HasListener) { fails.Add($"{what}: no listener"); return; }
        GD.Print($"listener: {what}: at {audio.ListenerAt} right {audio.ListenerRight} up {audio.ListenerUp}");
        if (audio.ListenerAt.DistanceTo(at) > 1e-3f) fails.Add($"{what}: at {audio.ListenerAt}, expected {at}");
        if (audio.ListenerRight.DistanceTo(right) > 1e-4f) fails.Add($"{what}: right {audio.ListenerRight}, expected {right}");
        if (audio.ListenerUp.DistanceTo(up) > 1e-4f) fails.Add($"{what}: up {audio.ListenerUp}, expected {up}");
    }

    public override void _Process(double delta)
    {
        // FieldAudio's _Process runs after this node's (a child), so each step reads the frame before
        var f = fieldListener.GlobalTransform;
        switch (++frames)
        {
            case 3:
                if (GetViewport().GetAudioListener3D() != fieldListener) fails.Add("the field's listener is not current");
                Expect("no viewer", f.Origin, f.Basis.X.Normalized(), f.Basis.Y.Normalized());
                viewer = new Vector3(-50, 10, 40);
                break;
            case 6:
                Expect("viewer", viewer!.Value, camera.GlobalBasis.X.Normalized(), Vector3.Up);
                viewer = null;
                break;
            case 9:
                Expect("viewer gone", f.Origin, f.Basis.X.Normalized(), f.Basis.Y.Normalized());
                foreach (var e in fails) GD.PrintErr("FAIL " + e);
                GD.Print(fails.Count == 0 ? "listener: PASS" : $"listener: FAIL ({fails.Count})");
                GetTree().Quit(fails.Count == 0 ? 0 : 1);
                break;
        }
    }
}
