using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ms2.Formats.Pack;
using Ms2Godot.World;
using Ms2Godot.Ui;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/hpbar_clip.tscn, needs the game data): the HP bar clip's pooling and reuse. A freed clip goes
/// to the pool and the next owner takes that node with a rebuilt gauge (no stale label or gauge); a hide and a show at the
/// same rank keeps the gauge (no reopen); the gauge's width follows the hp in place. Quits with 0 when all hold, else 1.
/// </summary>
public partial class HpBarClipCheck : Node
{
    public override async void _Ready()
    {
        var fails = new List<string>();
        void Is(bool ok, string what) { if (!ok) fails.Add(what); }
        var packs = PackSet.Shared(Ms2Godot.Settings.DataDir);
        var movie = new UiMovie(FieldContents.Ui(packs), "uihpbar");
        var holder = new Node2D();
        AddChild(holder);
        var pool = new Stack<MovieClipNode>();
        bool dirty = true;
        var a = new HpBarClip(movie, 1, holder, pool);
        a.Draw(new Vector2(10, 10), 1, true, 1000, 1000, ref dirty);
        var node = (MovieClipNode)holder.GetChild(0);
        var gauge = node.GetChildren().OfType<MovieClipNode>().Last();
        // every shape of the gauge (the label's frame has them all) must carry the width
        bool Wide(MovieClipNode g, int w) { var sh = g.GetChildren().OfType<ShapeNode>().ToList(); return sh.Count >= 2 && sh.All(c => c.Nine is { } n && Math.Abs(n.Sx * n.Bounds.Size.X - w) < 1e-3); }
        Is(Wide(gauge, HpBarClip.Width(1000, 1000, a.FullWidth)), "full width on both shapes");
        Is(gauge.Frame >= 10, $"friendly gauge not on the friendly label (frame {gauge.Frame})");

        // the width follows the hp in place: the same gauge node, no rebuild
        dirty = true;
        a.Draw(new Vector2(10, 10), 1, true, 500, 1000, ref dirty);
        Is(node.GetChildren().OfType<MovieClipNode>().Last() == gauge, "gauge rebuilt on a width change");
        Is(Wide(gauge, HpBarClip.Width(500, 1000, a.FullWidth)), "half width on both shapes");

        // hide and show at the same rank: the open is not repeated, the gauge stays
        a.Hide();
        dirty = false;
        a.Draw(new Vector2(10, 10), 1, true, 500, 1000, ref dirty);
        Is(node.GetChildren().OfType<MovieClipNode>().Last() == gauge && node.Visible, "gauge lost or node hidden after a same-rank reopen");

        // free: the node goes back to the pool; the next owner takes it with a fresh gauge on the other label
        a.Free();
        Is(pool.Count == 1 && pool.Peek() == node, "freed clip not in the pool");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Is(!IsInstanceValid(gauge), "old gauge not freed");
        var b = new HpBarClip(movie, 1, holder, pool);
        dirty = true;
        b.Draw(new Vector2(20, 20), 1, false, 1000, 1000, ref dirty);
        Is(pool.Count == 0 && holder.GetChildCount() == 1 && holder.GetChild(0) == node, "pooled node not reused");
        var g2 = node.GetChildren().OfType<MovieClipNode>().Last();
        Is(g2 != gauge && node.GetChildren().OfType<MovieClipNode>().Count(x => IsInstanceValid(x) && !x.IsQueuedForDeletion()) >= 1, "gauge not rebuilt for the new owner");
        Is(g2.Frame is >= 0 and < 10, $"new owner's hostile label not set (frame {g2.Frame})");
        Is(Wide(g2, HpBarClip.Width(1000, 1000, b.FullWidth)), "new owner's gauge width");
        GD.Print($"hpbar clip: {(fails.Count == 0 ? "ok" : "FAIL " + string.Join("; ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
