using System.Text.Json.Nodes;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using NUnit.Framework;

namespace Ms2.Avm2.Tests;

/// <summary>
/// A movie view reuses its last mouse-over shape test (Player.HitTestShapes) while the point, its runtime's DisplayMarks and
/// HitMarks and its place on screen are unchanged. So everything a point test reads must move one of the two counts: the
/// timeline's own frame changes (no script runs here), the hit-test disable flag and a bitmap's size.
/// </summary>
public class HitMarksTests
{
    // a 100 x 100 square shape at depth 1: on the stage from frame 0, moved right by 200 on frame 2 and gone after frame 3
    static (Runtime rt, Player p) Movie()
    {
        static JsonObject Square() => new()
        {
            ["kind"] = "shape", ["bounds"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["w"] = 100, ["h"] = 100 },
            ["paths"] = new JsonArray(new JsonObject { ["pts"] = new JsonArray(0.0, 0.0, 100.0, 0.0, 100.0, 100.0, 0.0, 100.0) }),
        };
        var layout = new JsonObject
        {
            ["movie"] = "t", ["stage"] = new JsonObject { ["width"] = 800, ["height"] = 600 },
            ["symbols"] = new JsonObject { ["1"] = Square() },
            ["root"] = new JsonObject
            {
                ["frames"] = 6,
                ["children"] = new JsonArray(new JsonObject
                {
                    ["id"] = 1, ["depth"] = 1, ["first"] = 0, ["last"] = 3, ["name"] = "square",
                    ["moves"] = new JsonArray(new JsonObject { ["frame"] = 2, ["matrix"] = new JsonObject { ["x"] = 200, ["y"] = 0 } }),
                }),
            },
        };
        var rt = new Runtime();
        var p = new Player(rt, layout);
        Assert.That(p.Start(), Is.True);
        return (rt, p);
    }

    static int Frame(Player p) => ((AsMovieClip)p.Root).Frame;

    [Test]
    public void TimelineFramesWithoutScriptsBumpDisplayMarks()
    {
        var (rt, p) = Movie();
        Assert.That(p.HitTestShapes(50, 50), Is.True);
        // every frame that changes what is on the stage marks it; the answer at a point changes only after a mark
        var seen = new List<(int frame, bool marked, bool at50, bool at250)>();
        while (Frame(p) < 5)
        {
            long before = rt.DisplayMarks;
            p.Tick();
            seen.Add((Frame(p), rt.DisplayMarks != before, p.HitTestShapes(50, 50), p.HitTestShapes(250, 50)));
        }
        Assert.That(seen.Single(s => s.frame == 2), Is.EqualTo((2, true, false, true)), "the move on frame 2 marks");
        Assert.That(seen.Single(s => s.frame == 4), Is.EqualTo((4, true, false, false)), "the removal after frame 3 marks");
        for (int i = 1; i < seen.Count; i++)
            if (!seen[i].marked) Assert.That((seen[i].at50, seen[i].at250), Is.EqualTo((seen[i - 1].at50, seen[i - 1].at250)), $"frame {seen[i].frame}");
        Assert.That(rt.Errors, Is.Empty);
    }

    [Test]
    public void HitTestDisableAndBitmapSizeBumpHitMarks()
    {
        var (rt, p) = Movie();
        var square = ((AsContainer)p.Root).Children.Single(c => c.InstanceName == "square");
        long marks = rt.DisplayMarks, hits = rt.HitMarks;
        square.HitTestDisabled = true;
        Assert.That(p.HitTestShapes(50, 50), Is.False);
        Assert.That(rt.HitMarks, Is.GreaterThan(hits));
        hits = rt.HitMarks;
        square.HitTestDisabled = true;
        Assert.That(rt.HitMarks, Is.EqualTo(hits), "the same value again is no change");
        square.HitTestDisabled = false;
        Assert.That(p.HitTestShapes(50, 50), Is.True);
        Assert.That(rt.HitMarks, Is.GreaterThan(hits));
        Assert.That(rt.DisplayMarks, Is.EqualTo(marks), "the flag draws nothing: no display mark, nothing redrawn");

        var bd = (AsBitmapData)rt.GetClass("flash.display.BitmapData").Construct(Rt.Box(10), Rt.Box(10));
        hits = rt.HitMarks;
        bd.width = 20;
        Assert.That(rt.HitMarks, Is.GreaterThan(hits));
        hits = rt.HitMarks;
        bd.height = 10;
        Assert.That(rt.HitMarks, Is.EqualTo(hits));
    }
}
