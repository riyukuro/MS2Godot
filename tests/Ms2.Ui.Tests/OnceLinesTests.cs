using Ms2.Ui;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

public class OnceLinesTests
{
    [Test]
    public void EachDistinctLinePrintsOnce()
    {
        var printed = new List<string>();
        var once = new OnceLines(printed.Add);
        for (int i = 0; i < 5; i++)
        {
            once.Print("side popup: system sound A is not played");
            once.Print("timer: effect B is not played");
        }
        Assert.That(once.Print("side popup: system sound C is not played"), Is.True, "another key prints");
        Assert.That(once.Print("timer: effect B is not played"), Is.False);
        Assert.That(printed, Is.EqualTo(new[]
        {
            "side popup: system sound A is not played", "timer: effect B is not played", "side popup: system sound C is not played",
        }));
    }
}
