using System.Text.Json.Nodes;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using NUnit.Framework;

namespace Ms2.Avm2.Tests;

/// <summary>
/// The editing keys of a focused input TextField as the client's EditorKit runs them (EditorKit's key handler,
/// sfr_gfx_07.cpp:780-830, and EditorKit::OnChar @0x142618120), reached through <see cref="Player.Key"/>.
/// </summary>
public class TextEditKeyTests
{
    const uint Delete = 46;

    static (Player, AsTextField) Field(string text, string type = "input")
    {
        var rt = new Runtime();
        var layout = new JsonObject { ["movie"] = "t", ["stage"] = new JsonObject { ["width"] = 800, ["height"] = 600 }, ["symbols"] = new JsonObject(), ["root"] = new JsonObject() };
        var p = new Player(rt, layout);
        Runtime.Current = rt;
        var tf = (AsTextField)rt.GetClass("flash.text.TextField").Construct();
        tf.type = type; tf.width = 200; tf.height = 20; tf.text = text;
        p.Stage.addChild(tf);
        p.Focus = tf;
        return (p, tf);
    }

    static Func<int> CountChanges(Player p, AsTextField tf)
    {
        int n = 0;
        tf.addEventListener("change", new AsPlainFunction(p.RT, new NativeMethod("f", (t, a) => { n++; return Rt.Undef; })));
        return () => n;
    }

    static void Press(Player p, uint key, uint ch = 0, bool shift = false)
    {
        p.Key(key, ch, true, shift);
        p.Key(key, ch, false, shift);
    }

    [Test]
    public void DeleteRemovesTheCharAfterTheCaret()
    {
        var (p, tf) = Field("hello");
        var changes = CountChanges(p, tf);
        tf.setSelection(1, 1);
        Press(p, Delete);
        Assert.That((tf.text, tf.caretIndex, changes()), Is.EqualTo(("hllo", 1, 1)));
        Press(p, Delete);
        Assert.That((tf.text, tf.caretIndex, changes()), Is.EqualTo(("hlo", 1, 2)));
    }

    [Test]
    public void DeleteRemovesTheSelection()
    {
        var (p, tf) = Field("hello world");
        var changes = CountChanges(p, tf);
        tf.setSelection(5, 11);
        Press(p, Delete);
        Assert.That((tf.text, tf.selectionBeginIndex, tf.selectionEndIndex, changes()), Is.EqualTo(("hello", 5, 5, 1)));
    }

    [Test]
    public void DeleteAtTheEndChangesNothingAndSendsNoChange()
    {
        var (p, tf) = Field("hello");
        var changes = CountChanges(p, tf);
        tf.setSelection(5, 5);
        Press(p, Delete);
        Assert.That((tf.text, tf.caretIndex, changes()), Is.EqualTo(("hello", 5, 0)));
    }

    [Test]
    public void DeleteLeavesAReadOnlyFieldAlone()
    {
        var (p, tf) = Field("hello", "dynamic");
        tf.selectable = true;
        tf.setSelection(0, 2);
        Press(p, Delete);
        Assert.That(tf.text, Is.EqualTo("hello"));
    }
}
