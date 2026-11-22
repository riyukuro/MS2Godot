using System.Text.Json.Nodes;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using NUnit.Framework;

namespace Ms2.Avm2.Tests;

/// <summary>
/// The caret keys of a focused TextField as the client's text key map binds them (TextKeyMap::InitWindowsKeyMap
/// @0x14258e0c0) and EditorKit's key handler moves the caret (sfr_gfx_07.cpp:646-776), reached through
/// <see cref="Player.Key"/>.
/// </summary>
public class TextCaretKeyTests
{
    const uint PageUp = 0x21, PageDown = 0x22, End = 0x23, Home = 0x24, Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28;

    static (Player, AsTextField) Field(string text, string type = "input", bool multiline = false)
    {
        var rt = new Runtime();
        var layout = new JsonObject { ["movie"] = "t", ["stage"] = new JsonObject { ["width"] = 800, ["height"] = 600 }, ["symbols"] = new JsonObject(), ["root"] = new JsonObject() };
        var p = new Player(rt, layout);
        Runtime.Current = rt;
        var tf = (AsTextField)rt.GetClass("flash.text.TextField").Construct();
        tf.type = type; tf.multiline = multiline; tf.width = 400; tf.height = multiline ? 200 : 20; tf.text = text;
        p.Stage.addChild(tf);
        p.Focus = tf;
        return (p, tf);
    }

    static void Press(Player p, uint key, bool shift = false, bool ctrl = false)
    {
        p.Key(key, 0, true, shift, ctrl);
        p.Key(key, 0, false, shift, ctrl);
    }

    static (int Caret, int Begin, int End) Sel(AsTextField tf) => (tf.caretIndex, tf.selectionBeginIndex, tf.selectionEndIndex);

    [Test]
    public void LeftAndRightMoveTheCaretOneCharAndStopAtTheEnds()
    {
        var (p, tf) = Field("hello");
        tf.setSelection(5, 5);
        Press(p, Left); Press(p, Left);
        Assert.That(Sel(tf), Is.EqualTo((3, 3, 3)));
        Press(p, Right);
        Assert.That(Sel(tf), Is.EqualTo((4, 4, 4)));
        Press(p, Right); Press(p, Right);
        Assert.That(Sel(tf), Is.EqualTo((5, 5, 5)), "Right at the end stays");
        tf.setSelection(0, 0);
        Press(p, Left);
        Assert.That(Sel(tf), Is.EqualTo((0, 0, 0)), "Left at the start stays");
        Press(p, Right, ctrl: true);
        Assert.That(Sel(tf), Is.EqualTo((1, 1, 1)), "Ctrl+Right is Right: the key map has no Ctrl entry for it");
    }

    [Test]
    public void LeftOrRightOnASelectionCollapsesItToThatSide()
    {
        var (p, tf) = Field("hello");
        tf.setSelection(1, 4);
        Press(p, Left);
        Assert.That(Sel(tf), Is.EqualTo((1, 1, 1)));
        tf.setSelection(1, 4);
        Press(p, Right);
        Assert.That(Sel(tf), Is.EqualTo((4, 4, 4)));
        tf.setSelection(0, 5);
        Press(p, Right);
        Assert.That(Sel(tf), Is.EqualTo((5, 5, 5)), "the caret is already at the end: the selection still collapses");
    }

    [Test]
    public void HomeAndEndGoToTheLineEndsCtrlToTheTextEnds()
    {
        var (p, tf) = Field("hello");
        tf.setSelection(2, 2);
        Press(p, Home);
        Assert.That(Sel(tf), Is.EqualTo((0, 0, 0)));
        Press(p, End);
        Assert.That(Sel(tf), Is.EqualTo((5, 5, 5)));

        (p, tf) = Field("ab\rcde\rf", multiline: true);
        tf.setSelection(4, 4);
        Press(p, Home);
        Assert.That(Sel(tf), Is.EqualTo((3, 3, 3)), "the start of the line \"cde\"");
        Press(p, End);
        Assert.That(Sel(tf), Is.EqualTo((6, 6, 6)), "before the line's new line");
        Press(p, Home, ctrl: true);
        Assert.That(Sel(tf), Is.EqualTo((0, 0, 0)));
        Press(p, End, ctrl: true);
        Assert.That(Sel(tf), Is.EqualTo((8, 8, 8)));
    }

    [Test]
    public void ShiftExtendsTheSelectionFromItsAnchor()
    {
        var (p, tf) = Field("hello");
        tf.setSelection(5, 5);
        Press(p, Left, shift: true); Press(p, Left, shift: true);
        Assert.That(Sel(tf), Is.EqualTo((3, 3, 5)));
        Press(p, Right, shift: true);
        Assert.That(Sel(tf), Is.EqualTo((4, 4, 5)));
        Press(p, Home, shift: true);
        Assert.That(Sel(tf), Is.EqualTo((0, 0, 5)));
        Press(p, End, shift: true);
        Assert.That(Sel(tf), Is.EqualTo((5, 5, 5)), "back on the anchor: nothing selected");

        tf.setSelection(2, 2);
        Press(p, Right, shift: true); Press(p, Right, shift: true);
        Assert.That(Sel(tf), Is.EqualTo((4, 2, 4)));
        Press(p, Home, shift: true);
        Assert.That(Sel(tf), Is.EqualTo((0, 0, 2)), "across the anchor");
        Press(p, Right);
        Assert.That(Sel(tf), Is.EqualTo((2, 2, 2)), "Right without Shift goes to the selection end");
    }

    [Test]
    public void SelectAllThenShiftLeftShrinksFromTheEnd()
    {
        var (p, tf) = Field("hello");
        Press(p, 'A', ctrl: true);
        Press(p, Left, shift: true);
        Assert.That(Sel(tf), Is.EqualTo((4, 0, 4)));
    }

    [Test]
    public void SetSelectionKeepsTheCaretAtItsSecondIndex()
    {
        var (p, tf) = Field("hello");
        tf.setSelection(5, 2);
        Assert.That(Sel(tf), Is.EqualTo((2, 2, 5)), "TextField::SetSelection @0x1425bcfc0 keeps the order; the caret goes to the end index");
        Press(p, Left, shift: true);
        Assert.That(Sel(tf), Is.EqualTo((1, 1, 5)));
    }

    [Test]
    public void ShiftDoesNotSelectInAnUnselectableField()
    {
        var (p, tf) = Field("hello");
        tf.selectable = false;
        tf.setSelection(5, 5);
        Press(p, Left, shift: true);
        Assert.That(Sel(tf), Is.EqualTo((4, 4, 4)));
    }

    [Test]
    public void ArrowsMoveTheCaretInASelectableDynamicField()
    {
        var (p, tf) = Field("hello", type: "dynamic");
        tf.setSelection(3, 3);
        Press(p, Left);
        Assert.That(Sel(tf), Is.EqualTo((2, 2, 2)));
        Press(p, End, shift: true);
        Assert.That(Sel(tf), Is.EqualTo((5, 2, 5)));
        Assert.That(tf.text, Is.EqualTo("hello"));
    }

    [Test]
    public void UpAndDownInASingleLineGoToTheEnds()
    {
        var (p, tf) = Field("hello");
        tf.setSelection(2, 2);
        Press(p, Up);
        Assert.That(Sel(tf), Is.EqualTo((0, 0, 0)));
        Press(p, Down);
        Assert.That(Sel(tf), Is.EqualTo((5, 5, 5)));
        tf.setSelection(2, 2);
        Press(p, PageUp);
        Assert.That(Sel(tf), Is.EqualTo((0, 0, 0)));
        Press(p, PageDown);
        Assert.That(Sel(tf), Is.EqualTo((5, 5, 5)));
    }

    [Test]
    public void UpAndDownKeepTheColumnAcrossAShortLine()
    {
        // all lower case: each char has the same width, so the column is the char count
        var (p, tf) = Field("abcdef\rx\rabcdef", multiline: true);
        tf.setSelection(5, 5);
        Press(p, Down);
        Assert.That(Sel(tf), Is.EqualTo((8, 8, 8)), "the end of \"x\"");
        Press(p, Down);
        Assert.That(Sel(tf), Is.EqualTo((14, 14, 14)), "back at column 5: EditorKit keeps LastHorizCursorPos");
        Press(p, Up); Press(p, Up);
        Assert.That(Sel(tf), Is.EqualTo((5, 5, 5)));
        Press(p, Up);
        Assert.That(Sel(tf), Is.EqualTo((0, 0, 0)), "Up on the first line goes to the start");
        tf.setSelection(10, 10);
        Press(p, Down);
        Assert.That(Sel(tf), Is.EqualTo((15, 15, 15)), "Down on the last line goes to the end");
    }

    [Test]
    public void UpOrDownOnASelectionWithoutShiftCollapsesIt()
    {
        var (p, tf) = Field("abc\rdef\rghi", multiline: true);
        tf.setSelection(5, 9);
        Press(p, Up);
        Assert.That(Sel(tf), Is.EqualTo((5, 5, 5)));
        tf.setSelection(5, 9);
        Press(p, Down);
        Assert.That(Sel(tf), Is.EqualTo((9, 9, 9)));
        tf.setSelection(5, 5);
        Press(p, Down, shift: true);
        Assert.That(Sel(tf), Is.EqualTo((9, 5, 9)), "Shift+Down extends to the same column a line down");
    }
}
