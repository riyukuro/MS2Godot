using System.Text.Json.Nodes;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using NUnit.Framework;

namespace Ms2.Avm2.Tests;

/// <summary>
/// A TextField's vertical scroll: scrollV is clamped to 1 .. maxScrollV and sends the scroll event only when the line changes
/// (TextField::scrollVSet sfr_as3_13.cpp:1446, DocView::SetVScrollOffset sfr_render_03b.cpp:572, OnScroll sfr_as3_18.cpp:5131),
/// and the wheel over the field scrolls it when mouseWheelEnabled (TextField::OnMouseWheelEvent sfr_gfx_03e.cpp:915).
/// </summary>
public class TextScrollTests
{
    static (Player, AsTextField, List<string> events) Field(int lines)
    {
        var rt = new Runtime();
        var layout = new JsonObject { ["movie"] = "t", ["stage"] = new JsonObject { ["width"] = 800, ["height"] = 600 }, ["symbols"] = new JsonObject(), ["root"] = new JsonObject() };
        var p = new Player(rt, layout);
        Runtime.Current = rt;
        var tf = (AsTextField)rt.GetClass("flash.text.TextField").Construct();
        tf.multiline = true; tf.width = 400; tf.height = 60;
        tf.text = string.Join("\n", Enumerable.Range(0, lines).Select(i => "line " + i));
        p.Stage.addChild(tf);
        var events = new List<string>();
        tf.addEventListener("scroll", new AsPlainFunction(rt, new NativeMethod("f", (t, a) => { events.Add("scroll"); return Rt.Undef; })));
        return (p, tf, events);
    }

    [Test]
    public void ScrollVIsClampedAndSendsScrollOnlyOnAChange()
    {
        var (_, tf, events) = Field(30);
        Assert.That(tf.maxScrollV, Is.GreaterThan(1));
        Assert.That(tf.scrollV, Is.EqualTo(1));
        tf.scrollV = 0;
        Assert.That(tf.scrollV, Is.EqualTo(1), "below 1 is 1");
        Assert.That(events, Is.Empty, "no change, no event");
        tf.scrollV = 5;
        Assert.That(tf.scrollV, Is.EqualTo(5));
        tf.scrollV = 5;
        Assert.That(events.Count, Is.EqualTo(1), "the same line sends nothing");
        tf.scrollV = 9999;
        Assert.That(tf.scrollV, Is.EqualTo(tf.maxScrollV), "past the end is the last position");
        Assert.That(events.Count, Is.EqualTo(2));
    }

    [Test]
    public void WheelScrollsTheFieldUnderTheMouse()
    {
        var (p, tf, events) = Field(30);
        tf.scrollV = tf.maxScrollV;
        events.Clear();
        p.MouseMove(10, 10);
        p.Wheel(3);
        Assert.That(tf.scrollV, Is.EqualTo(tf.maxScrollV - 3), "a positive delta scrolls up");
        Assert.That(events.Count, Is.EqualTo(1));
        p.Wheel(-1);
        Assert.That(tf.scrollV, Is.EqualTo(tf.maxScrollV - 2));
        tf.mouseWheelEnabled = false;
        p.Wheel(5);
        Assert.That(tf.scrollV, Is.EqualTo(tf.maxScrollV - 2), "mouseWheelEnabled = false: the wheel is not taken");
    }

    [Test]
    public void TheFieldScrollsBeforeTheMouseWheelEventIsSent()
    {
        // MovieImpl::ProcessMouse (sfr_gfx_01i.cpp:3668): OnMouseWheelEvent first, GenerateMouseEvents (:3681) after
        var (p, tf, _) = Field(30);
        tf.scrollV = tf.maxScrollV;
        int seen = -1;
        var rt = Runtime.Current;
        tf.addEventListener("mouseWheel", new AsPlainFunction(rt, new NativeMethod("w", (t, a) => { seen = tf.scrollV; return Rt.Undef; })));
        p.MouseMove(10, 10);
        int before = tf.scrollV;
        p.Wheel(2);
        Assert.That(seen, Is.EqualTo(before - 2), "the handler sees the field already scrolled");
    }
}
