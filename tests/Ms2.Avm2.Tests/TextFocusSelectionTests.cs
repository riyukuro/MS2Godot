using System.Text.Json.Nodes;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using NUnit.Framework;

namespace Ms2.Avm2.Tests;

/// <summary>
/// A text field's selection as the focus moves (TextField::OnFocus @0x1425ba020, sfr_gfx_03e.cpp:1057): only a keyboard
/// move selects all of a selectable field's text, and the field losing the focus drops its selection unless
/// alwaysShowSelection.
/// </summary>
public class TextFocusSelectionTests
{
    static (Player, AsTextField, AsTextField) Fields()
    {
        var rt = new Runtime();
        var layout = new JsonObject { ["movie"] = "t", ["stage"] = new JsonObject { ["width"] = 800, ["height"] = 600 }, ["symbols"] = new JsonObject(), ["root"] = new JsonObject() };
        var p = new Player(rt, layout);
        Runtime.Current = rt;
        AsTextField Make(string text, double y)
        {
            var tf = (AsTextField)rt.GetClass("flash.text.TextField").Construct();
            tf.type = "input"; tf.width = 400; tf.height = 20; tf.y = y; tf.text = text;
            p.Stage.addChild(tf);
            return tf;
        }
        return (p, Make("hello", 0), Make("world", 40));
    }

    static (int Caret, int Begin, int End) Sel(AsTextField tf) => (tf.caretIndex, tf.selectionBeginIndex, tf.selectionEndIndex);

    [Test]
    public void APressOrStageFocusKeepsTheSelection()
    {
        var (p, a, b) = Fields();
        a.setSelection(2, 2);
        p.SetFocus(a, "mouse");
        Assert.That(Sel(a), Is.EqualTo((2, 2, 2)), "a press (GFx_FocusMovedByMouse) does not select all");
        b.setSelection(1, 3);
        p.Stage.focus = b;
        Assert.That(Sel(b), Is.EqualTo((3, 1, 3)), "stage.focus (GFx_FocusMovedByAS) does not select all");
    }

    [Test]
    public void TheKeyboardSelectsAllOfASelectableField()
    {
        var (p, a, b) = Fields();
        b.setSelection(2, 2);
        p.SetFocus(a, "mouse");
        p.MoveFocus("down");
        Assert.That(p.Focus, Is.SameAs(b));
        Assert.That(Sel(b), Is.EqualTo((5, 0, 5)), "Tab selects all");
        a.selectable = false;
        a.setSelection(1, 1);
        p.SetFocus(a, "key");
        Assert.That(Sel(a), Is.EqualTo((1, 1, 1)), "a field that is not selectable keeps its selection");
    }

    [Test]
    public void LosingTheFocusDropsTheSelectionUnlessAlwaysShown()
    {
        var (p, a, b) = Fields();
        p.SetFocus(a, "mouse");
        a.setSelection(1, 4);
        p.SetFocus(b, "mouse");
        Assert.That(Sel(a), Is.EqualTo((4, 4, 4)), "the selection goes, the caret stays");
        b.setSelection(3, 0);
        b.alwaysShowSelection = true;
        p.SetFocus(null, "mouse");
        Assert.That(Sel(b), Is.EqualTo((0, 0, 3)), "alwaysShowSelection keeps it");
    }
}
