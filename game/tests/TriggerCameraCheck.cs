using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2Godot.World;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/trigger_camera.tscn) of the camera trigger objects (TriggerObjectCamera, triggeractioncli.cpp:
/// 848-998) driving the camera manager (<see cref="TriggerCamera"/>) through <see cref="FieldTriggers"/>, with a fixed
/// follow camera:
/// - 63000041: object 8001 (no interpolation times, rotation z 315) locks the camera on its setting at once (the z as -45) and its end goes back to the
///   follow mode at once; an off event of a type 2 packet is skipped while one of type 3 ends the step. Object 8014
///   (StartInterpolationTime 0.1, EndInterpolationTime 0.5) flies linearly into the lock mode, which starts on the next
///   frame from the fly-by's previous frame (FUN_14121f9f0 keeps the mode's last applied setting), and its end flies back
///   to the follow mode's fov and position. A type 5 packet queues the steps: they wait while a fly-by runs and the next
///   one runs when its pending mode is set; the single id 0 goes back to the follow mode.
/// - 52000067: objects 8010 (StartInterpolator 2) and 8006 (StartInterpolator 3) ease by the log and exp curves
///   (FUN_141b6fdb0), while the rotation goes by the linear ratio (FUN_140afdec0).
/// Also the setting's Godot transform for rotation (90, 0, 0) (looking along MS2 +x with +z up) and the rotation
/// blend's ends. Needs MS2_DATA. Quits with 0 when it holds, else 1.
/// </summary>
public partial class TriggerCameraCheck : Node
{
    const int FieldA = 63000041, FieldB = 52000067;

    sealed class Follow : ITargetCamera
    {
        public CameraSetting Setting => new()
        {
            Fov = 40, DofNear = 250, DofFar = 18000, Position = new Vector3(0, 0, 1000), Rotation = new Vector3(90, 45, 0),
        };
        public Vector3 FlyToPosition => new(100, 200, 1500);
    }

    PackSet packs;
    readonly Follow follow = new();
    readonly List<string> fails = new();
    readonly List<string> notes = new();
    bool done;

    public override void _Ready()
    {
        try { Run(); }
        catch (Exception e) { fails.Add(e.ToString()); }
    }

    public override void _Process(double delta) => Done();

    (FieldTriggers Triggers, TriggerCamera Camera, Dictionary<int, FieldEntity> Objects) Field(int field)
    {
        var fd = FieldData.FindById(packs, field) ?? throw new InvalidOperationException($"no field {field}");
        var map = FieldMap.Load(packs, new FlatLibrary(packs), fd.XBlock);
        var t = new FieldTriggers(map.Entities, new Dictionary<FieldEntity, List<(MultiMesh, int)>>());
        var c = new TriggerCamera(follow);
        t.Bind(c);
        var objects = new Dictionary<int, FieldEntity>();
        foreach (var e in map.Entities)
            if (TriggerObjects.KindOf(e.HasMixin) == TriggerObjectKind.Camera) objects[(int)e.Number("TriggerObjectID")] = e;
        return (t, c, objects);
    }

    /// <summary>A Trigger packet of type 2 or 3 with one camera event (id, on byte).</summary>
    static Maple2.PacketLib.Tools.ByteReader Event(byte type, int id, bool on)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(type);
        if (type == TriggerObjects.Load) w.Write(1);
        w.Write(id);
        w.Write((byte)(on ? 1 : 0));
        return new Maple2.PacketLib.Tools.ByteReader(ms.ToArray());
    }

    /// <summary>A Trigger packet of type 5: u8 count, the ids, u8 end.</summary>
    static Maple2.PacketLib.Tools.ByteReader Steps(bool end, params int[] ids)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(TriggerObjects.CameraObjects);
        w.Write((byte)ids.Length);
        foreach (int id in ids) w.Write(id);
        w.Write((byte)(end ? 1 : 0));
        return new Maple2.PacketLib.Tools.ByteReader(ms.ToArray());
    }

    void Near(string what, float got, float want, float eps = 1e-3f)
    {
        if (!(Mathf.Abs(got - want) <= eps)) fails.Add($"{what}: {got} (want {want})");
    }

    void Near(string what, Vector3 got, Vector3 want, float eps = 1e-2f)
    {
        if (!(got.DistanceTo(want) <= eps)) fails.Add($"{what}: {got} (want {want})");
    }

    void Mode(string what, TriggerCamera c, int want)
    {
        if (c.ModeType != want) fails.Add($"{what}: mode {c.ModeType} (want {want})");
    }

    void Run()
    {
        packs = PackSet.Shared(Settings.DataDir);
        Transforms();
        var (t, c, objs) = Field(FieldA);
        foreach (int id in new[] { 8001, 8014 })
            if (!objs.ContainsKey(id)) { fails.Add($"no camera object {id} on {FieldA}"); return; }
        var a = FieldTriggers.CameraSettingOf(objs[8001]);
        var b = FieldTriggers.CameraSettingOf(objs[8014]);
        notes.Add($"8001 fov {a.Fov} pos {a.Position} rot {a.Rotation}; 8014 fov {b.Fov}");

        // 8001: no interpolation times
        t.Apply(Event(TriggerObjects.Update, 8001, true));
        Mode("8001 on", c, TriggerCamera.Lock);
        if (c.Shown is { } s1) { Near("8001 on: position", s1.Position, a.Position); Near("8001 on: fov", s1.Fov, a.Fov);
            Near("8001 on: rotation, each axis above 180 less 360", s1.Rotation, new Vector3(a.Rotation.X, a.Rotation.Y, a.Rotation.Z - 360)); }
        else fails.Add("8001 on: nothing shown");
        t.Apply(Event(TriggerObjects.Load, 8001, false));
        Mode("8001 off at load", c, TriggerCamera.Lock);
        t.Apply(Event(TriggerObjects.Update, 8001, false));
        Mode("8001 off", c, TriggerCamera.Target);
        if (c.Shown != null) fails.Add("8001 off: still shown");

        // 8014: a 0.1 s linear fly-by into the lock mode, then a 0.5 s one back
        t.Apply(Event(TriggerObjects.Update, 8014, true));
        Mode("8014 on", c, TriggerCamera.FlyBy);
        if (c.FlyByMode != TriggerCamera.Lock) fails.Add($"8014 on: flies to mode {c.FlyByMode}");
        c.Update(0.05f);
        float half = (follow.Setting.Fov + b.Fov) / 2;
        Near("8014 fly at 0.05 s: fov", c.Shown?.Fov ?? -1, half);
        Near("8014 fly at 0.05 s: position", c.Shown?.Position ?? default, (follow.Setting.Position + b.Position) / 2, 0.1f);
        c.Update(0.06f);
        if (c.PendingMode != TriggerCamera.Lock) fails.Add($"8014 fly past its end: pending {c.PendingMode}");
        Near("8014 fly past its end: fov", c.Shown?.Fov ?? -1, b.Fov);
        c.Update(0.01f);
        Mode("8014 landed", c, TriggerCamera.Lock);
        Near("8014 landed: fov (the previous frame's)", c.Shown?.Fov ?? -1, half);
        var landed = c.Shown ?? default;
        t.Apply(Event(TriggerObjects.Update, 8014, false));
        Mode("8014 off", c, TriggerCamera.FlyBy);
        c.Update(0.25f);
        Near("8014 back at 0.25 s: fov", c.Shown?.Fov ?? -1, (landed.Fov + follow.Setting.Fov) / 2);
        Near("8014 back at 0.25 s: position", c.Shown?.Position ?? default, (landed.Position + follow.FlyToPosition) / 2, 0.1f);
        c.Update(0.3f);
        c.Update(0.01f);
        Mode("8014 back", c, TriggerCamera.Target);

        // type 5: 8014 flies, 8001's start and end wait for it
        t.Apply(Steps(true, 8014, 8001));
        Mode("steps: first", c, TriggerCamera.FlyBy);
        var queued = string.Join(",", t.CameraSteps.Select(x => $"{x.Id}{(x.Start ? "+" : "-")}"));
        if (queued != "8001+,8001-") fails.Add($"steps: queued {queued} (want 8001+,8001-)");
        c.Update(0.2f);
        if (t.CameraSteps.Count != 2) fails.Add($"steps: {t.CameraSteps.Count} queued before the pending mode (want 2)");
        c.Update(0.01f);
        Mode("steps: after the fly-by", c, TriggerCamera.Target);
        if (t.CameraSteps.Count != 0) fails.Add($"steps: {t.CameraSteps.Count} left after the fly-by (want 0)");
        t.Apply(Event(TriggerObjects.Update, 8001, true));
        Mode("8001 on again", c, TriggerCamera.Lock);
        t.Apply(Steps(false, 0));
        Mode("steps: id 0", c, TriggerCamera.Target);
        notes.Add($"8014 half fov {half}, landed {landed.Fov}");

        Eased();
    }

    void Eased()
    {
        var (t, c, objs) = Field(FieldB);
        foreach (int id in new[] { 8010, 8006 })
            if (!objs.ContainsKey(id)) { fails.Add($"no camera object {id} on {FieldB}"); return; }
        var from = follow.Setting;
        // 8010: log, 0.5 s
        var s = FieldTriggers.CameraSettingOf(objs[8010]);
        if ((int)objs[8010].Number("StartInterpolator") != 2) fails.Add("8010 is not log");
        t.Apply(Event(TriggerObjects.Update, 8010, true));
        c.Update(0.1f);
        float k = MathF.Log(0.2f * 10f + 1f) / 2.397895f;
        Near("8010 log at 0.1 s: fov", c.Shown?.Fov ?? -1, from.Fov + (s.Fov - from.Fov) * k);
        var rot = Ms2.Net.CameraMath.RotationLerp(0.2f, (from.Rotation.X, from.Rotation.Y, from.Rotation.Z), (s.Rotation.X, s.Rotation.Y, s.Rotation.Z));
        Near("8010 log at 0.1 s: rotation (linear)", c.Shown?.Rotation ?? default, new Vector3(rot.X, rot.Y, rot.Z));
        t.Apply(Steps(false, 0));
        // 8006: exp, 3 s
        s = FieldTriggers.CameraSettingOf(objs[8006]);
        if ((int)objs[8006].Number("StartInterpolator") != 3) fails.Add("8006 is not exp");
        t.Apply(Event(TriggerObjects.Update, 8006, true));
        c.Update(1f);
        k = (MathF.Exp(5f / 3f) - 1f) / 147.41316f;
        Near("8006 exp at 1 s: position", c.Shown?.Position ?? default, from.Position + (s.Position - from.Position) * k, 0.1f);
        notes.Add($"exp k {k:F4}");
    }

    void Transforms()
    {
        var x = new CameraSetting { Rotation = new Vector3(90, 0, 0), Position = new Vector3(1, 2, 3) }.Transform();
        Near("rotation (90,0,0): view", -x.Basis.Z, new Vector3(1, 0, 0), 1e-4f);
        Near("rotation (90,0,0): up", x.Basis.Y, new Vector3(0, 1, 0), 1e-4f);
        Near("position (1,2,3)", x.Origin, new Vector3(1, 3, -2), 1e-4f);
        (float, float, float) a = (90, 45, 315), b = (270, 45, 180);
        foreach (var (r, want) in new[] { (0f, a), (1f, b) })
        {
            var got = Ms2.Net.CameraMath.RotationLerp(r, a, b);
            var m = Ms2.Net.CameraMath.FromEuler(got.Item1, got.Item2, got.Item3);
            var w = Ms2.Net.CameraMath.FromEuler(want.Item1, want.Item2, want.Item3);
            for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++)
                if (Math.Abs(m[i, j] - w[i, j]) > 1e-3) { fails.Add($"rotation blend at {r}: [{i},{j}] {m[i, j]} (want {w[i, j]})"); i = 3; break; }
        }
    }

    void Done()
    {
        if (done) return;
        done = true;
        GD.Print($"trigger camera check: {string.Join("; ", notes)}");
        GD.Print($"trigger camera check: {(fails.Count == 0 ? "OK" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
