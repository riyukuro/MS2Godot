using System.Text.Json.Nodes;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using NUnit.Framework;

namespace Ms2.Avm2.Tests;

/// <summary>
/// The text fields' clipboard keys as the client's EditorKit handles them (sfr_gfx_07.cpp:821-856), reached through
/// <see cref="Player.Key"/> with the Windows key codes, and a clipboard of the test's own.
/// </summary>
public class TextClipboardTests
{
    sealed class FakeClipboard : ITextClipboard
    {
        public string Text = "";
        public readonly List<string> Stored = new();
        public string GetText() => Text;
        public void SetText(string text) { Stored.Add(text); Text = text; }
    }

    const uint A = 65, C = 67, V = 86, X = 88, Insert = 45, Delete = 46;

    static (Player, AsTextField, FakeClipboard) Field(string text, string type = "input")
    {
        var rt = new Runtime();
        var layout = new JsonObject { ["movie"] = "t", ["stage"] = new JsonObject { ["width"] = 800, ["height"] = 600 }, ["symbols"] = new JsonObject(), ["root"] = new JsonObject() };
        var p = new Player(rt, layout);
        Runtime.Current = rt;
        var tf = (AsTextField)rt.GetClass("flash.text.TextField").Construct();
        tf.type = type; tf.width = 200; tf.height = 20; tf.text = text;
        p.Stage.addChild(tf);
        p.Focus = tf;
        var cb = new FakeClipboard();
        p.Clipboard = cb;
        return (p, tf, cb);
    }

    static void Press(Player p, uint key, bool shift = false, bool ctrl = false)
    {
        p.Key(key, 0, true, shift, ctrl);
        p.Key(key, 0, false, shift, ctrl);
    }

    [Test]
    public void CtrlCAndCtrlInsertCopyTheSelection()
    {
        var (p, tf, cb) = Field("hello world");
        tf.setSelection(0, 5);
        Press(p, C, ctrl: true);
        Assert.That(cb.Text, Is.EqualTo("hello"));
        tf.setSelection(6, 11);
        Press(p, Insert, ctrl: true);
        Assert.That(cb.Text, Is.EqualTo("world"));
        tf.setSelection(3, 3);
        Press(p, C, ctrl: true);
        Assert.That(cb.Stored, Is.EqualTo(new[] { "hello", "world", "" }), "no selection stores an empty text (CopyToClipboard)");
        Assert.That(tf.text, Is.EqualTo("hello world"));
    }

    [Test]
    public void CtrlXAndShiftDeleteCutTheSelection()
    {
        var (p, tf, cb) = Field("hello world");
        int changes = 0;
        tf.addEventListener("change", new AsPlainFunction(p.RT, new NativeMethod("f", (t, a) => { changes++; return Rt.Undef; })));
        tf.setSelection(5, 11);
        Press(p, X, ctrl: true);
        Assert.That((tf.text, cb.Text, tf.caretIndex, changes), Is.EqualTo(("hello", " world", 5, 1)));
        tf.setSelection(0, 2);
        Press(p, Delete, shift: true);
        Assert.That((tf.text, cb.Text, tf.caretIndex, changes), Is.EqualTo(("llo", "he", 0, 2)));
    }

    [Test]
    public void CtrlVAndShiftInsertPasteOverTheSelection()
    {
        var (p, tf, cb) = Field("hello world");
        cb.Text = "there";
        tf.setSelection(6, 11);
        Press(p, V, ctrl: true);
        Assert.That((tf.text, tf.caretIndex), Is.EqualTo(("hello there", 11)));
        tf.setSelection(0, 0);
        cb.Text = ">";
        Press(p, Insert, shift: true);
        Assert.That((tf.text, tf.caretIndex), Is.EqualTo((">hello there", 1)));
        Press(p, Insert, shift: true, ctrl: true);
        Assert.That(tf.text, Is.EqualTo(">>hello there"), "Ctrl+Shift+Insert: the later Shift+Insert entry wins (TextKeyMap::Find)");
    }

    [Test]
    public void CtrlASelectsAll()
    {
        var (p, tf, _) = Field("hello");
        tf.setSelection(2, 2);
        Press(p, A, ctrl: true);
        Assert.That((tf.selectionBeginIndex, tf.selectionEndIndex), Is.EqualTo((0, 5)));
    }

    [Test]
    public void APasswordFieldNeverReachesTheClipboard()
    {
        var (p, tf, cb) = Field("secret");
        tf.displayAsPassword = true;
        cb.Text = "keep";
        tf.setSelection(0, 6);
        Press(p, C, ctrl: true);
        Press(p, Insert, ctrl: true);
        Assert.That(cb.Stored, Is.Empty, "copy from a password field stores nothing (CopyToClipboard)");
        Press(p, X, ctrl: true);
        Assert.That(cb.Stored, Is.Empty, "neither does cut");
        Assert.That(tf.text, Is.EqualTo(""), "cut still removes the selection (CutToClipboard)");
        Press(p, V, ctrl: true);
        Assert.That(tf.text, Is.EqualTo("keep"), "paste into a password field works");
    }

    [Test]
    public void PasteIsCutToMaxChars()
    {
        var (p, tf, cb) = Field("abc");
        tf.maxChars = 8;
        tf.setSelection(3, 3);
        cb.Text = "defghijk";
        Press(p, V, ctrl: true);
        Assert.That((tf.text, tf.caretIndex), Is.EqualTo(("abcdefgh", 8)));
        tf.setSelection(2, 4);
        cb.Text = "WXYZ";
        Press(p, V, ctrl: true);
        Assert.That((tf.text, tf.caretIndex), Is.EqualTo(("abWXefgh", 4)), "a replaced selection's room counts (Cmd_ReplaceTextByPlainText)");
        tf.setSelection(8, 8);
        Press(p, V, ctrl: true);
        Assert.That(tf.text, Is.EqualTo("abWXefgh"), "full: nothing goes in");
    }

    [Test]
    public void PasteIsFilteredByRestrictWithTheOtherCaseTried()
    {
        var (p, tf, cb) = Field("");
        tf.restrict = "A-Z";
        cb.Text = "ab1C";
        Press(p, V, ctrl: true);
        Assert.That(tf.text, Is.EqualTo("ABC"), "lower case is retried upper; the digit goes");
        Assert.That(tf.caretIndex, Is.EqualTo(3), "the caret, at the start plus the 4 pasted, is kept inside the text");
        tf.restrict = "0-9";
        tf.text = "12";
        tf.setSelection(1, 1);
        cb.Text = "a3b";
        Press(p, V, ctrl: true);
        Assert.That(tf.text, Is.EqualTo("132"));
    }

    [Test]
    public void AMultiLinePasteKeepsItsLinesAndANulEndsIt()
    {
        var (p, tf, cb) = Field("");
        Assert.That(tf.multiline, Is.False);
        cb.Text = "one\r\ntwo\nthree\0four";
        Press(p, V, ctrl: true);
        Assert.That(tf.text, Is.EqualTo("one\rtwo\rthree"), "CR LF is one new line, LF a new line, also in a single-line field");
    }

    [Test]
    public void ASelectableDynamicFieldCopiesButTakesNoEdit()
    {
        var (p, tf, cb) = Field("read only", "dynamic");
        cb.Text = "x";
        tf.setSelection(0, 4);
        Press(p, X, ctrl: true);
        Assert.That((tf.text, cb.Text), Is.EqualTo(("read only", "read")), "cut in a read-only field copies");
        Press(p, V, ctrl: true);
        Assert.That(tf.text, Is.EqualTo("read only"));
        tf.selectable = false;
        cb.Text = "";
        Press(p, C, ctrl: true);
        Assert.That(cb.Stored, Has.Count.EqualTo(1), "a field that is not selectable copies nothing");
    }
}
