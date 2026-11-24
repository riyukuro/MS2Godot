using System.Text.Json.Nodes;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using NUnit.Framework;

namespace Ms2.Avm2.Tests;

/// <summary>
/// TextFieldEx.appendHtml parses only the appended html (DocView::AppendHtml, sfr_render_03b.cpp:277 / :287); the text, formats
/// and html a field ends with are those of setting the htmlText to everything appended.
/// </summary>
public class TextAppendHtmlTests
{
    static AsTextField Field()
    {
        var rt = new Runtime();
        var layout = new JsonObject { ["movie"] = "t", ["stage"] = new JsonObject { ["width"] = 800, ["height"] = 600 }, ["symbols"] = new JsonObject(), ["root"] = new JsonObject() };
        var p = new Player(rt, layout);
        Runtime.Current = rt;
        var tf = (AsTextField)rt.GetClass("flash.text.TextField").Construct();
        tf.multiline = true; tf.wordWrap = true; tf.width = 300; tf.height = 100;
        p.Stage.addChild(tf);
        return tf;
    }

    public static IEnumerable<string[]> Pieces() => new[]
    {
        new[] { "<p><font color='#ffd200'>Name</font> hello &amp; bye</p>", "<p>second <b>line</b></p>", "<p><i>third</i></p>" },
        new[] { "<b>one", "two</b> three", "<p>x", "y</p>", "tail" },
        new[] { "a &am", "p; b", " &lt;c&gt;" },
        new[] { "a <fo", "nt color='#ff0000'>b</font>", "<font size='20'>big</font>" },
        new[] { "<p>a</p>", "<p>b</p>\n", "<p>c</p>  " },
        new[] { "<b>bold</b>", "<br>x", "<br><br>y" },
        new[] { "<li>one", "<li>two</li>", "<li>three" },
        new[] { "plain", "<img src='x' width='8' height='8'>", "<a href='u'>link</a>", "after" },
        new[] { "x", "\\", "ny", "line\\nbreak" },
        new[] { "", "<p></p>", "z", "" },
    };

    [TestCaseSource(nameof(Pieces))]
    public void AppendedHtmlIsTheTextAndFormatsOfTheWholeHtml(string[] pieces)
    {
        var a = Field();
        a.htmlText = pieces[0];
        for (int i = 1; i < pieces.Length; i++) { a.AppendHtml(pieces[i]); }
        var whole = Field();
        whole.htmlText = string.Concat(pieces);
        Assert.That(a.text, Is.EqualTo(whole.text), "text");
        Assert.That(a.htmlText, Is.EqualTo(whole.htmlText), "text and formats as the html writes them");
        Assert.That(a.numLines, Is.EqualTo(whole.numLines));
        Assert.That(a.maxScrollV, Is.EqualTo(whole.maxScrollV));
    }

    [TestCaseSource(nameof(Pieces))]
    public void ParserFedInPiecesIsTheParseOfTheWholeWhereItCanGoOn(string[] pieces)
    {
        foreach (var (newLine, literal, perChar) in new[] { ('\n', true, false), ('\r', false, true) })
        {
            var p = new HtmlText.Parser(true, newLine, literal, perChar);
            string fed = "";
            foreach (var piece in pieces)
            {
                if (!p.Continuable) break;
                p.Feed(piece);
                fed += piece;
                var (text, formats) = HtmlText.Parse(fed, true, newLine, literal, perChar);
                Assert.That(p.Text(0), Is.EqualTo(text), $"text after {fed}");
                Assert.That(p.Formats.Take(p.Count), Is.EqualTo(formats), $"formats after {fed}");
                if (!p.Continuable) break;
            }
        }
    }

    [Test]
    public void ATagOrEntityOpenAtTheEndCannotGoOn()
    {
        var p = new HtmlText.Parser(true, '\n', false, false);
        p.Feed("a <fo");
        Assert.That(p.Continuable, Is.False);
        p = new HtmlText.Parser(true, '\n', false, false);
        p.Feed("a &am");
        Assert.That(p.Continuable, Is.False);
        p = new HtmlText.Parser(true, '\n', true, false);
        p.Feed("a\\");
        Assert.That(p.Continuable, Is.False, "a backslash a literal line break could follow");
        p = new HtmlText.Parser(true, '\n', false, false);
        p.Feed("<b>a</b> & bcdefghijklmnop");
        Assert.That(p.Continuable, Is.True, "a lone & far from the end is just text");
    }
}
