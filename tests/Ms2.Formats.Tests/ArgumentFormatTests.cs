using Ms2.Formats.Text;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>ArgumentFormat, the server string id formatter (Unk_TextFormat FUN_14083ff40), and CommonStringIds.</summary>
public class ArgumentFormatTests
{
    [Test]
    public void PlainArguments() =>
        Assert.That(ArgumentFormat.Format("{0} met {1}, {0} again", new[] { "A", "B" }), Is.EqualTo("A met B, A again"));

    [Test]
    public void FormattedArguments()
    {
        Assert.That(ArgumentFormat.Format("[{0:02d}:{1:02d}]", new[] { "7", "5" }), Is.EqualTo("[07:05]"));
        Assert.That(ArgumentFormat.Format("{0:.2f}%", new[] { "3.14159" }), Is.EqualTo("3.14%"));
        Assert.That(ArgumentFormat.Format("{0:d}", new[] { "12abc" }), Is.EqualTo("12"));     // _atoi64 reads the leading digits
        Assert.That(ArgumentFormat.Format("{0:x}", new[] { "255" }), Is.EqualTo("ff"));
    }

    [Test]
    public void LongerTagIsLeft() =>
        Assert.That(ArgumentFormat.Apply("{10} {1}", 1, "x"), Is.EqualTo("{10} x"));

    [Test]
    public void MissingArgumentStays() =>
        Assert.That(ArgumentFormat.Format("{0} {1}", new[] { "a" }), Is.EqualTo("a {1}"));

    [Test]
    public void CommonIds()
    {
        Assert.That(CommonStringIds.Key(0xa9), Is.EqualTo("s_timeevent_notice_chat"));
        Assert.That(CommonStringIds.Key(-1), Is.Null);
        Assert.That(CommonStringIds.Key(100000), Is.Null);
    }
}
