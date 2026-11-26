using System;
using System.Collections.Generic;
using Godot;
using System.Linq;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2Godot.Net;
using Ms2Godot.World;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/camera_shake.tscn) of the vibrate parts' camera shake on a Godot camera
/// (<see cref="CameraShakes.Apply"/>, FUN_140af1de0):
/// - the shake moves the camera by the shake's direction times its wave, turned from client space (x, y, z) to Godot
///   (x, z, -y), and keeps its rotation; before the start it does not move it, and from the end on the shake is dropped and
///   the camera left;
/// - another PC's part (kind 3) is not made, and with no field the made part queues nothing;
/// - CreateEffect's view test (effectservice.cpp:316-330), where region skills and the combat layer make a monster's vibrate
///   part: the camera's actor past the vibrate row's distance from the target makes none, nearer makes it; the own player's
///   part is made at any distance;
/// - a frame allocates nothing.
/// Quits with 0 when it holds, else 1.
/// </summary>
public partial class CameraShakeCheck : Node
{
    readonly List<string> fails = new();

    public override void _Ready()
    {
        try { Run(); }
        catch (Exception e) { fails.Add(e.ToString()); }
        foreach (var f in fails) GD.PrintErr("camera shake: " + f);
        GD.Print(fails.Count == 0 ? "camera shake: PASS" : $"camera shake: FAIL ({fails.Count})");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        if (!ok) fails.Add(what);
    }

    void Run()
    {
        var camera = new Camera3D();
        AddChild(camera);
        var at = new Vector3(10, 20, 30);
        var turn = Basis.FromEuler(new Vector3(-0.4f, 0.7f, 0));
        camera.GlobalTransform = new Transform3D(turn, at);
        var effects = CameraShakes.Effects;
        effects.Clear();
        Check(!CameraShakes.Apply(camera, 0), "no shake: no move");

        // start 5, end 6, 2 Hz, amp 8, no fade: at 5.125 the wave is 8
        var v = new CameraVibrate(5, 6, 2, 8, 1f, 5, true);
        effects.Add(v, vibrateOption: true);
        Check(!CameraShakes.Apply(camera, 4.5) && camera.GlobalPosition == at, "before the start: not moved");
        Check(CameraShakes.Apply(camera, 5.125), "at 5.125 it moves");
        var d = camera.GlobalPosition - at;
        var client = new System.Numerics.Vector3(d.X, -d.Z, d.Y);
        var want = v.Direction * 8f;
        Check(System.Numerics.Vector3.Distance(client, want) < 1e-3f, $"the move {d} (client {client}) is the direction times the wave {want}");
        Check(v.Direction.LengthSquared() > 0.99f, "a direction was picked");
        Check(camera.GlobalTransform.Basis.IsEqualApprox(turn), "the rotation is kept");
        GD.Print($"camera shake: moved {d} for client {want}");

        camera.GlobalPosition = at;
        Check(!CameraShakes.Apply(camera, 6) && camera.GlobalPosition == at, "at the end: not moved");
        Check(effects.Count == 0, "the ended shake is dropped");

        // the part's gates
        var part = new EffectPart { Type = "vibrate", TypeId = EffectSkipTable.TypeVibrate, Duration = 0.5f, VibrateFrequency = 10, VibrateAmplitude = 5, VibrateDamping = 1 };
        var parts = new List<EffectPart> { part };
        Check(!CameraShakes.Makes(parts, part, EffectOwner.OfKind(3)), "another PC's part is not made");
        int played = CameraShakes.Played;
        bool made = CameraShakes.Play(parts, part, EffectOwner.OfKind(EffectOwner.KindMyPc));
        Check(made == GameOptions.Current.CameraOption.VibrateEffect, "my part is made when the vibrate option is on");
        Check(CameraShakes.Played == played + (made ? 1 : 0), "counted when made");
        Check(effects.Count == 0, "with no field nothing is queued");

        Sites();

        // a long shake: 0 B a frame
        effects.Add(new CameraVibrate(0, 1e9, 15, 10, 0.5f, 0, true), true);
        Frames(camera, 200);
        long least = long.MaxValue;
        for (int pass = 0; pass < 5 && least != 0; pass++) least = Math.Min(least, Frames(camera, 1000));
        Check(least == 0, $"{least} B over 1000 frames");
        GD.Print($"camera shake: {least} B over 1000 frames");
        effects.Clear();
        camera.QueueFree();
    }

    const int Monster = 5;

    /// <summary>A monster's vibrate part from a region skill and from the combat layer, the camera's actor near and far.</summary>
    void Sites()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var table = new EffectTable(packs);
        float row = FieldAudio.EffectSkip(packs).ViewDistance(EffectSkipTable.RowVibrate, Monster);
        Check(row > 0, $"the monster's vibrate row is read ({row})");
        var monster = EffectOwner.OfKind(Monster);
        string xml = null;
        var entries = new List<string>(packs.List("Xml"));
        entries.Sort(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!entry.StartsWith("effect/", StringComparison.OrdinalIgnoreCase) || !entry.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
            var file = entry.Substring("effect/".Length);
            var list = table.Get(file);
            if (list != null && list.Any(p => p.TypeId == EffectSkipTable.TypeVibrate && p.TopLevel && p.RandomParent < 0 && !p.OffSkip
                    && !p.SkippedFor(monster) && !p.SkippedFor(EffectOwner.OfKind(EffectOwner.KindMyPc)) && !p.SkippedFor(EffectOwner.OfKind(0)))
                && list.All(p => p.RandomParent < 0)) { xml = file; break; }
        }
        Check(xml != null, "an effect with a vibrate part a monster makes");
        if (xml == null) return;
        var option = GameOptions.Current.CameraOption;
        bool wasOn = option.VibrateEffect;
        option.VibrateEffect = true;
        var field = new Node3D();
        AddChild(field);
        var skills = new RegionSkills(packs, builder);
        AddChild(skills);
        var combat = new CombatLayer(packs, builder, table, field);
        System.Numerics.Vector3? eye = null;
        skills.ViewerOf = () => eye;
        combat.ViewerOf = () => eye;
        var at = new Ms2.Formats.Model.Vec3(100, 200, 0);   // client space; Godot (100, 0, -200)
        var gAt = new Vector3(100, 0, -200);
        var c = new System.Numerics.Vector3(100, 200, 0);
        int Region(int kind) { int n = CameraShakes.Played; skills.Insert(xml, new Xform(at, Xform.Identity.R, 1), false, 0, kind); return CameraShakes.Played - n; }
        int Combat(int kind) { int n = CameraShakes.Played; combat.PlayEffect(xml, gAt, 0f, kind); return CameraShakes.Played - n; }
        foreach (var (name, play) in new (string, Func<int, int>)[] { ("region skill", Region), ("combat layer", Combat) })
        {
            eye = c + new System.Numerics.Vector3(row - 50, 0, 0);
            Check(play(Monster) == 1, $"{name}: the camera's actor {row - 50} away, the shake is made");
            eye = c + new System.Numerics.Vector3(0, row + 50, 0);
            Check(play(Monster) == 0, $"{name}: the camera's actor {row + 50} away, no shake");
            Check(play(EffectOwner.KindMyPc) == 1, $"{name}: the own player's shake {row + 50} away is made (its row is 0)");
            Check(play(0) == 1, $"{name}: kind 0 is never out of view");
            eye = null;
            Check(play(Monster) == 1, $"{name}: no camera's actor, nothing is out of view");
        }
        GD.Print($"camera shake: {xml}, monster row {row}: near made, far refused, on both sites");
        CameraShakes.Effects.Clear();
        option.VibrateEffect = wasOn;
        skills.QueueFree();
        field.QueueFree();
    }

    static long Frames(Camera3D camera, int count)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < count; i++) CameraShakes.Apply(camera, i / 60.0);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
