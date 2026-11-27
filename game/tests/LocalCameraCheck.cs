using System;
using System.Collections.Generic;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2Godot.World;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/local_camera.tscn) of the local camera mode (0x10) through <see cref="TriggerCamera"/> over
/// a field's local camera setups and its trigger boxes (<see cref="FieldTriggers.Contains"/>):
/// - 52000066: the LocalCamera packet of id 700 enters its Operate 2 setup from the follow mode and ends at the setup's
///   view; an unknown id or id 0 does nothing; from the lock mode the enter does nothing (STILL-OPEN in TriggerCamera);
///   the leave goes back to the follow mode, which takes the local mode's shown camera (vfunc_7) and is placed again at
///   its own kept distance (FUN_14183cc80(1) then SetTarget(player, 1)). Turning the local cameras off moves the camera
///   back and sets the follow mode pending, which the next Update sets.
/// - 02100004: the auto check enters the Operate 3 setup of trigger box 101 while the player stands in it, not with the
///   local cameras off, and the camera moves back once the player walks out.
/// - 02000064: the auto check enters the Operate 1 setup 101 within its Scope 450 of (-3600, 7200, 2100).
/// Needs MS2_DATA. Quits with 0 when it holds, else 1.
/// </summary>
public partial class LocalCameraCheck : Node
{
    sealed class Follow : ITargetCamera, ILocalCameraFollow
    {
        public CameraSetting Setting => new()
        {
            Fov = 34, DofNear = 250, DofFar = 18000, Position = new Vector3(0, 0, 1000), Rotation = new Vector3(90, 45, 0),
        };
        public System.Numerics.Vector3 Center;
        public Ms2.Ui.CameraPose? Taken;
        public float? RetargetDistance = -1;
        public int Retargets;
        public Ms2.Ui.LocalCameraPrev LocalPrev
        {
            get
            {
                const float yaw = 0.6f, pitch = 1.0f, d = 800f;
                var eye = Center + new System.Numerics.Vector3(d * MathF.Sin(yaw), d * MathF.Cos(yaw), d * MathF.Cos(pitch));
                var (rx, ry, rz) = Ms2.Net.CameraMath.Euler(eye.X, eye.Y, eye.Z, Center.X, Center.Y, Center.Z);
                var rot = new System.Numerics.Vector3(rx, ry, rz);
                return new Ms2.Ui.LocalCameraPrev
                {
                    Type = TriggerCamera.Target, Yaw = yaw, Pitch = pitch, Distance = d,
                    Setting = new Ms2.Ui.LocalCameraSetting(34, 250, 18000, eye, rot), Center = Center,
                    Shown = new Ms2.Ui.CameraPose(eye, rot), Placed = new Ms2.Ui.CameraPose(eye, rot),
                    LooseFinal = 2, LooseNow = 2, LooseUpdate = 1, Nearest = 150, Farthest = 1000,
                };
            }
        }
        public float FollowDistance => 800f;
        public void TakeShown(Ms2.Ui.CameraPose shown) => Taken = shown;
        public void Retarget(float? distance) { RetargetDistance = distance; Retargets++; }
    }

    sealed class Player : Ms2.Ui.ILocalCameraTarget
    {
        public System.Numerics.Vector3? Position { get; set; }
        public bool IsPc => true;
        public System.Numerics.Vector3 Rotation { get; set; }
        public TriggerCamera Camera;
        public FieldTriggers Triggers;
        public bool LocalCamerasOn => Camera.LocalCamerasOn;
        public bool BoxContains(int boxId, System.Numerics.Vector3 p) => Triggers.Contains(boxId, p);
    }

    PackSet packs;
    readonly List<string> fails = new();
    readonly List<string> notes = new();
    bool done;

    public override void _Ready()
    {
        try { Run(); }
        catch (Exception e) { fails.Add(e.ToString()); }
    }

    public override void _Process(double delta) => Done();

    (TriggerCamera Camera, Follow Follow, Player Player) Field(int field, System.Numerics.Vector3 at)
    {
        var fd = FieldData.FindById(packs, field) ?? throw new InvalidOperationException($"no field {field}");
        var map = FieldMap.Load(packs, new FlatLibrary(packs), fd.XBlock);
        var t = new FieldTriggers(map.Entities, new Dictionary<FieldEntity, List<(MultiMesh, int)>>());
        var f = new Follow { Center = at };
        var c = new TriggerCamera(f);
        t.Bind(c);
        c.Setups.AddRange(Ms2.Ui.LocalCameraSetup.All(map.Entities));
        var p = new Player { Position = at, Rotation = new System.Numerics.Vector3(0, 0, 45), Camera = c, Triggers = t };
        c.Bind(f, p);
        notes.Add($"{field}: {c.Setups.Count} local cameras");
        return (c, f, p);
    }

    void Mode(string what, TriggerCamera c, int want)
    {
        if (c.ModeType != want) fails.Add($"{what}: mode {c.ModeType} (want {want})");
    }

    static void Frames(TriggerCamera c, float seconds)
    {
        for (float s = 0; s < seconds; s += 1 / 60f) c.Update(1 / 60f);
    }

    void Run()
    {
        packs = PackSet.Shared(Settings.DataDir);
        Trigger();
        Box();
        Scope();
    }

    void Trigger()
    {
        var (c, f, p) = Field(52000066, new System.Numerics.Vector3(0, 0, 1500));
        if (Ms2.Ui.LocalCameraSetup.FindTrigger(c.Setups, 700) is not { } s) { fails.Add("52000066: no Operate 2 setup 700"); return; }
        p.Position = s.Setting.Position;
        f.Center = s.Setting.Position;
        if (c.EnterLocal(999999, 0, true) || c.EnterLocal(0, 0, true)) fails.Add("an unknown id or id 0 found a setup");
        Mode("unknown id", c, TriggerCamera.Target);
        c.Update(1 / 60f);
        Mode("the trigger setup does not start by itself", c, TriggerCamera.Target);

        if (!c.EnterLocal(700, 0, true)) fails.Add("700: no setup");
        Mode("700 enter", c, TriggerCamera.LocalTarget);
        Frames(c, s.InterpolationTime + 0.1f);
        var l = c.Local;
        if (l == null) { fails.Add("700: no local mode"); return; }
        if (!l.Steady) fails.Add("700: not at the setup's view after its InterpolationTime");
        var view = l.Placed.Position - (s.Setting.Position + s.OffsetPosition);
        if (MathF.Abs(view.Length() - s.TargetDistance) > 0.5f) fails.Add($"700: the eye is {view.Length()} from the look point (want {s.TargetDistance})");
        if (c.Current.Fov != s.Setting.Fov) fails.Add($"700: fov {c.Current.Fov} (want {s.Setting.Fov})");
        notes.Add($"700: distance {s.TargetDistance}, time {s.InterpolationTime}, eye {l.Placed.Position}");

        c.EnterLocal(700, 0, false);
        Mode("700 leave", c, TriggerCamera.Target);
        if (f.Taken is not { } taken || taken.Position != l.Shown.Position) fails.Add("700 leave: the follow mode did not take the shown camera");
        if (f.RetargetDistance != 800f) fails.Add($"700 leave: placed again at {f.RetargetDistance} (want the kept 800)");
        if (c.Local != null) fails.Add("700 leave: the local mode is still current");

        f.Retargets = 0;
        c.EnterLocal(700, 0, false);
        // g_143aef240 keeps the distance of the last enter: a leave in the follow mode places it there again
        if (f.Retargets != 1 || f.RetargetDistance != 800f) fails.Add($"a leave in the follow mode: {f.Retargets} retargets at {f.RetargetDistance} (want 1 at the kept 800)");

        c.SetSetting(new CameraSetting { Fov = 40, Position = new Vector3(1, 2, 3) });
        c.SetMode(TriggerCamera.Lock, false);
        c.EnterLocal(700, 0, true);
        Mode("700 enter from the lock mode", c, TriggerCamera.Lock);
        c.SetMode(TriggerCamera.Target, false);

        f.Taken = null;
        c.EnterLocal(700, 0, true);
        Frames(c, 0.5f);
        c.LocalCamerasOn = false;
        c.Update(1 / 60f);
        if (c.Local?.Returning != true) fails.Add("cameras off: not moving back");
        for (int i = 0; i < 600 && c.ModeType == TriggerCamera.LocalTarget; i++) c.Update(1 / 60f);
        Mode("cameras off: back", c, TriggerCamera.Target);
        if (f.Taken == null) fails.Add("cameras off: the follow mode did not take the shown camera");
        c.LocalCamerasOn = true;
    }

    void Box()
    {
        var box = new System.Numerics.Vector3(-2400, -2100, 2400);
        var (c, _, p) = Field(2100004, box);
        c.LocalCamerasOn = false;
        c.Update(1 / 60f);
        Mode("02100004 box 101 with the cameras off", c, TriggerCamera.Target);
        c.LocalCamerasOn = true;
        c.Update(1 / 60f);
        Mode("02100004 box 101", c, TriggerCamera.LocalTarget);
        if (c.Local is { } l && (l.Setup.Operate, l.Setup.BoxId) != (3, 101)) fails.Add($"02100004: setup {l.Setup.Operate}/{l.Setup.BoxId} (want 3/101)");
        Frames(c, 1.1f);
        p.Position = box + new System.Numerics.Vector3(300, 0, 0);
        c.Update(1 / 60f);
        if (c.Local?.Returning != true) fails.Add("02100004: walking out of box 101 does not move back");
        for (int i = 0; i < 600 && c.ModeType == TriggerCamera.LocalTarget; i++) c.Update(1 / 60f);
        Mode("02100004 out of the box", c, TriggerCamera.Target);
    }

    void Scope()
    {
        var at = new System.Numerics.Vector3(-3600, 7200 - 440, 2100);
        var (c, _, p) = Field(2000064, at);
        c.Update(1 / 60f);
        Mode("02000064 within 450 of setup 101", c, TriggerCamera.LocalTarget);
        if (c.Local is { } l && (l.Setup.Operate, l.Setup.LocalCameraId) != (1, 101)) fails.Add($"02000064: setup {l.Setup.Operate}/{l.Setup.LocalCameraId} (want 1/101)");
        var (c2, _, _) = Field(2000064, at - new System.Numerics.Vector3(0, 20, 0));
        c2.Update(1 / 60f);
        Mode("02000064 460 from setup 101", c2, TriggerCamera.Target);
    }

    void Done()
    {
        if (done) return;
        done = true;
        GD.Print($"local camera check: {string.Join("; ", notes)}");
        GD.Print($"local camera check: {(fails.Count == 0 ? "OK" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
