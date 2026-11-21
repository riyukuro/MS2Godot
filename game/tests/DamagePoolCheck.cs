using System;
using Godot;
using Ms2.Formats.Pack;
using Ms2Godot.Ui;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/damage_pool.tscn): the damage text's pools leave nothing behind when the HUD goes. The
/// pooled nodes are outside the tree, so the tree's free does not reach them. It counts the engine's objects, makes a
/// DamageText (its load fills the pools with one clip of each common look), warms it twice more, frees it with its
/// parent as hud.tscn's change of scene does, and counts again after a full GC. Fails when the pools were empty after
/// the warm-up or when the objects did not go back to the first count. Quits with 0 when all hold, else 1.
/// </summary>
public partial class DamagePoolCheck : Node
{
    int frame, failures;
    long before;
    Node holder;

    static long Objects()
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        return (long)Performance.GetMonitor(Performance.Monitor.ObjectCount);
    }

    void Fail(string what) { failures++; GD.Print("damage pool: FAIL " + what); }

    public override void _Process(double delta)
    {
        frame++;
        if (frame == 2)
        {
            var assets = new UiAssets(PackSet.Shared(Settings.DataDir));
            var constants = Ms2.Ui.FeatureElements.Constants(assets.Packs);
            // a first DamageText, freed: the movies' shared caches are made once, outside the count
            var warm = new DamageText(assets, constants, new Vector2(1280, 720));
            warm.Free();
            before = Objects();
            holder = new Node();
            AddChild(holder);
            var d = new DamageText(assets, constants, new Vector2(1280, 720));
            holder.AddChild(d);
            d.Warm();
            d.Warm();
            GD.Print($"damage pool: {d.PooledNodes} pooled nodes");
            if (d.PooledNodes == 0) Fail("nothing was pooled");
        }
        else if (frame == 3)
        {
            holder.QueueFree();
        }
        else if (frame == 5)
        {
            long after = Objects();
            GD.Print($"damage pool: objects {before} before, {after} after the free");
            if (after > before) Fail($"{after - before} objects left after the HUD went");
            GD.Print($"damage pool: {(failures == 0 ? "ok" : "FAIL")}, {failures} failures");
            GetTree().Quit(failures == 0 ? 0 : 1);
        }
    }
}
