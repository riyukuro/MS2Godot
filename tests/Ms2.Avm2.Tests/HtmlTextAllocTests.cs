using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using NUnit.Framework;

namespace Ms2.Avm2.Tests;

/// <summary>
/// What setting a 2 KB htmlText with the tags and attributes the item and quest tooltips use costs on its own (parse,
/// formats and layout), after a first set warms everything up.
/// </summary>
public class HtmlTextAllocTests
{
    static string Html(int salt)
    {
        var sb = new StringBuilder();
        for (int i = 0; sb.Length < 2048; i++)
            sb.Append("<textformat leading='2' indent='0' blockindent='4' leftmargin='1' rightmargin='1' tabstops='20,40'>")
              .Append("<p align='left'><font color='#ffd200' size='12'>Line ").Append(i + salt).Append("</font> ")
              .Append("<a href='event:item").Append(i).Append("'><b>link</b></a> <font color=\"#ff5ad35f\">grade</font> ")
              .Append("<img src='icon' width='12' height='12' vspace='1' hspace='2' id='i").Append(i).Append("'></p></textformat>");
        return sb.ToString();
    }

    [Test]
    public void SetHtmlText()
    {
        var rt = new Runtime();
        var layout = new JsonObject { ["movie"] = "t", ["stage"] = new JsonObject { ["width"] = 800, ["height"] = 600 }, ["symbols"] = new JsonObject(), ["root"] = new JsonObject() };
        var p = new Player(rt, layout);
        Runtime.Current = rt;
        var tf = (AsTextField)rt.GetClass("flash.text.TextField").Construct();
        tf.multiline = true; tf.wordWrap = true; tf.width = 300; tf.height = 400;
        p.Stage.addChild(tf);
        string a = Html(0), b = Html(1);
        const int n = 200;
        void Sets() { for (int i = 0; i < n; i++) tf.htmlText = (i & 1) == 0 ? a : b; }
        Sets();
        long bytes = Alloc.Least(Sets) / n;
        var sw = Stopwatch.StartNew();
        Sets();
        double ms = sw.Elapsed.TotalMilliseconds / n;
        TestContext.WriteLine($"htmlText set ({a.Length} chars): {bytes} B, {ms:F3} ms per set");
        Assert.That(rt.Errors, Is.Empty);
        Assert.That(bytes, Is.LessThan(MaxBytes));
    }

    const long MaxBytes = 250_000;   // 187040 B (Release); 889392 B when each attribute went through Regex.Match(string, pattern)
}
