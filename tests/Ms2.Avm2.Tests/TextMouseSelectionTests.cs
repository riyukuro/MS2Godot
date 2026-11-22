using System.Text.Json.Nodes;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using NUnit.Framework;

namespace Ms2.Avm2.Tests;

/// <summary>
/// The mouse in a text field, as Scaleform's EditorKit handles it (sfr_gfx_07.cpp): a left press puts the caret at the
/// char boundary nearest the press and takes the mouse capture (OnMouseDown @0x142618bf0), a move while captured moves the
/// selection's end (OnMouseMove @0x142618f60), the release ends the capture (OnMouseUp @0x142619040). A second press at
/// the same point within 300 ms selects the word, a third the paragraph; with Shift held a press extends the selection.
/// The test font's lowercase chars are 6.24 px wide and its space 3.6 px, after the 2 px gutter, so in "hello world" the
/// boundaries are at x 2, 8.24, 14.48, 20.72, 26.96, 33.2, 36.8, 43.04, 49.28 ...
/// </summary>
public class TextMouseSelectionTests
{
    double now = 1000;

    (Player, AsTextField, AsTextField) Fields(string text = "hello world")
    {
        var rt = new Runtime();
        var layout = new JsonObject { ["movie"] = "t", ["stage"] = new JsonObject { ["width"] = 800, ["height"] = 600 }, ["symbols"] = new JsonObject(), ["root"] = new JsonObject() };
        var p = new Player(rt, layout);
        Runtime.Current = rt;
        p.Clock = () => now;
        AsTextField Make(string s, double y)
        {
            var tf = (AsTextField)rt.GetClass("flash.text.TextField").Construct();
            tf.type = "input"; tf.width = 400; tf.height = 20; tf.y = y; tf.text = s;
            p.Stage.addChild(tf);
            return tf;
        }
        return (p, Make(text, 0), Make("other", 40));
    }

    static (int Caret, int Begin, int End) Sel(AsTextField tf) => (tf.caretIndex, tf.selectionBeginIndex, tf.selectionEndIndex);

    void Press(Player p, double x, double y) { now += 1000; p.MouseMove(x, y); p.MouseButton(true); }

    [Test]
    public void APressPutsTheCaretAtTheNearestBoundary()
    {
        var (p, a, _) = Fields();
        a.setSelection(0, 0);
        Press(p, 15, 10);
        Assert.That(Sel(a), Is.EqualTo((2, 2, 2)), "x 15 is before the middle of the 'l' at 14.48..20.72");
        Assert.That(p.Focus, Is.SameAs(a));
        p.MouseButton(false);
        Press(p, 19, 10);
        Assert.That(Sel(a), Is.EqualTo((3, 3, 3)), "x 19 is past its middle");
        p.MouseButton(false);
        Press(p, 300, 10);
        Assert.That(Sel(a), Is.EqualTo((11, 11, 11)), "past the line's end, the end");
        p.MouseButton(false);
    }

    [Test]
    public void ADragMovesTheSelectionsEndUntilTheRelease()
    {
        var (p, a, _) = Fields();
        Press(p, 19, 10);
        Assert.That(a.MouseCaptured, Is.True);
        p.MouseMove(44, 10);
        Assert.That(Sel(a), Is.EqualTo((7, 3, 7)), "drag right");
        p.MouseMove(3, 10);
        Assert.That(Sel(a), Is.EqualTo((0, 0, 3)), "drag left of the anchor");
        p.MouseMove(500, 300);
        Assert.That(Sel(a), Is.EqualTo((11, 3, 11)), "the capture follows the mouse off the field: below the lines, the last line");
        p.MouseButton(false);
        Assert.That(a.MouseCaptured, Is.False);
        Assert.That(p.TextMouseCaptured, Is.False);
        p.MouseMove(3, 10);
        Assert.That(Sel(a), Is.EqualTo((11, 3, 11)), "a move after the release leaves the selection");
    }

    [Test]
    public void APressWithShiftExtendsTheSelection()
    {
        var (p, a, _) = Fields();
        Press(p, 19, 10);
        p.MouseButton(false);
        p.Key(16, 0, true, shift: true);
        Assert.That(a.ShiftPressed, Is.True);
        Press(p, 44, 10);
        Assert.That(Sel(a), Is.EqualTo((7, 3, 7)), "shift-press");
        p.MouseButton(false);
        p.Key(16, 0, false);
        Assert.That(a.ShiftPressed, Is.False);
        Press(p, 3, 10);
        Assert.That(Sel(a), Is.EqualTo((0, 0, 0)), "a press without Shift collapses the selection");
        p.MouseButton(false);
    }

    [Test]
    public void DoubleAndTriplePressesSelectTheWordAndTheParagraph()
    {
        var (p, a, _) = Fields("hello world\rnext");
        Press(p, 19, 10);
        p.MouseButton(false);
        now += 100; p.MouseButton(true); p.MouseButton(false);
        Assert.That(Sel(a), Is.EqualTo((5, 0, 5)), "double: the word");
        now += 100; p.MouseButton(true); p.MouseButton(false);
        Assert.That(Sel(a), Is.EqualTo((12, 0, 12)), "triple: the paragraph with its new line");
        now += 400; p.MouseButton(true); p.MouseButton(false);
        Assert.That(Sel(a), Is.EqualTo((3, 3, 3)), "past the 300 ms window a press is a single one");
        p.MouseMove(46, 10); now += 100; p.MouseButton(true); p.MouseButton(false);
        Assert.That(Sel(a), Is.EqualTo((7, 7, 7)), "another point is a single press");
    }

    [Test]
    public void AFieldThatIsNotSelectableTakesTheCaretButNoCapture()
    {
        var (p, a, _) = Fields();
        a.selectable = false;
        a.setSelection(0, 0);
        Press(p, 19, 10);
        Assert.That(Sel(a), Is.EqualTo((3, 3, 3)));
        Assert.That(a.MouseCaptured, Is.False);
        p.MouseMove(44, 10);
        Assert.That(Sel(a), Is.EqualTo((3, 3, 3)));
        p.MouseButton(false);
    }

    [Test]
    public void LosingTheFocusEndsTheCaptureAndTheShiftState()
    {
        var (p, a, b) = Fields();
        Press(p, 19, 10);
        p.Key(16, 0, true, shift: true);
        Assert.That(a.MouseCaptured && a.ShiftPressed, Is.True);
        p.SetFocus(b, "key");
        Assert.That(a.MouseCaptured || a.ShiftPressed, Is.False, "EditorKit::OnKillFocus");
        p.MouseMove(44, 10);
        Assert.That(Sel(b), Is.EqualTo((5, 0, 5)), "the move goes nowhere");
        p.MouseButton(false);
    }
}
