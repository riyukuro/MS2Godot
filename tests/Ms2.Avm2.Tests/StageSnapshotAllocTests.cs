using System.Text.Json.Nodes;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using NUnit.Framework;

namespace Ms2.Avm2.Tests;

/// <summary>
/// Adding a sprite to the stage and removing it walks its children (addedToStage / removedFromStage), and a change of what
/// the mouse is over walks the parents it rolls over. Each walk is over a copy, since a listener may change the list. These
/// count what those walks cost on their own, with no listeners, and check that a listener that changes the children
/// mid-walk still sees the children as they were when the walk began.
/// </summary>
public class StageSnapshotAllocTests
{
    static (Runtime rt, Player p) Empty()
    {
        var rt = new Runtime();
        var layout = new JsonObject { ["movie"] = "t", ["stage"] = new JsonObject { ["width"] = 800, ["height"] = 600 }, ["symbols"] = new JsonObject(), ["root"] = new JsonObject() };
        var p = new Player(rt, layout);
        Runtime.Current = rt;
        return (rt, p);
    }

    static AsSprite Sprite(Runtime rt) => (AsSprite)rt.GetClass("flash.display.Sprite").Construct();

    [Test]
    public void AddAndRemoveASpriteWithTwentyChildren()
    {
        var (rt, p) = Empty();
        var s = Sprite(rt);
        for (int i = 0; i < 20; i++) s.addChild(Sprite(rt));
        const int n = 100;
        void Cycle() { for (int i = 0; i < n; i++) { p.Stage.addChild(s); p.Stage.removeChild(s); } }
        Cycle();
        long bytes = Alloc.Least(Cycle) / n;
        TestContext.WriteLine($"add + remove of a sprite with 20 children: {bytes} B per cycle");
        Assert.That(rt.Errors, Is.Empty);
        Assert.That(bytes, Is.LessThan(MaxCycleBytes));
    }

    [Test]
    public void HoverChanges()
    {
        var (rt, p) = Empty();
        AsSprite Leaf(double x)
        {
            var leaf = Sprite(rt);
            leaf.graphics.beginFill(0xffffff); leaf.graphics.drawRect(0, 0, 40, 40);
            AsSprite outer = Sprite(rt), inner = Sprite(rt);
            inner.addChild(leaf); outer.addChild(inner); outer.x = x;
            p.Stage.addChild(outer);
            return leaf;
        }
        Leaf(0); Leaf(100);
        const int n = 100;
        void Moves() { for (int i = 0; i < n; i++) p.MouseMove((i & 1) == 0 ? 10 : 110, 10); }
        Moves();
        long bytes = Alloc.Least(Moves) / n;
        TestContext.WriteLine($"mouse move to the other sprite: {bytes} B per move");
        Assert.That(rt.Errors, Is.Empty);
        Assert.That(bytes, Is.LessThan(MaxHoverBytes));
    }

    [Test]
    public void AChildAddedOrRemovedByAListenerDoesNotChangeTheWalk()
    {
        var (rt, p) = Empty();
        var s = Sprite(rt);
        var kids = Enumerable.Range(0, 4).Select(_ => Sprite(rt)).ToArray();
        foreach (var k in kids) s.addChild(k);
        var late = Sprite(rt);
        var seen = new List<string>();
        void On(AsSprite o, string type, string tag, Action act) =>
            p.Call(o, "addEventListener", new object[] { type, new AsPlainFunction(rt, new NativeMethod(tag, (_, a) => { seen.Add(tag); act?.Invoke(); return Rt.Undef; })) });
        for (int i = 0; i < kids.Length; i++)
        {
            int j = i;
            On(kids[i], "addedToStage", "add" + i, j == 1 ? () => { s.removeChild(kids[2]); s.addChild(late); } : null);
            On(kids[i], "removedFromStage", "remove" + i, j == 1 ? () => { s.addChild(kids[2]); s.removeChild(late); } : null);
        }
        On(late, "addedToStage", "addLate", null);
        On(late, "removedFromStage", "removeLate", null);
        // child 1's listener takes child 2 off (its own removedFromStage) and adds late (its own addedToStage): the walk still
        // goes on to child 2 and child 3 and does not visit late again
        p.Stage.addChild(s);
        Assert.That(seen, Is.EqualTo(new[] { "add0", "add1", "remove2", "addLate", "add2", "add3" }));
        seen.Clear();
        // the children are now 0, 1, 3, late; child 1's listener puts child 2 back and takes late off: the walk is still 3, late
        p.Stage.removeChild(s);
        Assert.That(seen, Is.EqualTo(new[] { "remove0", "remove1", "add2", "removeLate", "remove3", "removeLate" }));
        Assert.That(rt.Errors, Is.Empty);
    }

    // Release: 0 B per cycle (1896 B when each level copied its children with ToList) and 720 B per move (808 B with a
    // new List for the rollOver chain)
    const long MaxCycleBytes = 100, MaxHoverBytes = 780;
}
