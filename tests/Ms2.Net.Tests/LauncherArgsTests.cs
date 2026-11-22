using Ms2.Net;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class LauncherArgsTests
{
    static readonly string[] LauncherLine = { "1234", "--nxapp=nxsteam", "--stage=play.example", "--ticket=MST_abc", "--ip=play.example", "--port=1235", "--title=Lith", "--console" };

    [Test]
    public void TheLaunchersCommandLineGivesTheAddressAndTheTicket()
    {
        var a = LauncherArgs.Parse(LauncherLine);
        Assert.That((a.Host, a.Port, a.Ticket), Is.EqualTo(("play.example", 1235, "MST_abc")));   // --port wins over the bare port
        Assert.That(a.HasAddress, Is.True);
    }

    [Test]
    public void TheBarePortIsTheFallback()
    {
        var a = LauncherArgs.Parse(new[] { "1234", "--ticket=t", "--ip=h" });
        Assert.That((a.Host, a.Port, a.Ticket), Is.EqualTo(("h", 1234, "t")));
    }

    [Test]
    public void TheEnginesArgumentsBeforeTheSeparatorDoNotHideTheLine()
    {
        // OS.GetCmdlineArgs + OS.GetCmdlineUserArgs: the executable, engine switches, "--", then the launcher's line
        var all = new[] { "/opt/game/MapleStory2", "--path", "game", "--" }.Concat(LauncherLine).ToArray();
        var a = LauncherArgs.Parse(all);
        Assert.That((a.Host, a.Port, a.Ticket), Is.EqualTo(("play.example", 1235, "MST_abc")));
    }

    [Test]
    public void ANumberAfterASwitchIsItsValueNotThePort()
    {
        var a = LauncherArgs.Parse(new[] { "--shots", "5", "--ip=h", "--ticket=t" });
        Assert.That(a.HasAddress, Is.False);
    }

    [Test]
    public void OrderDoesNotMatterAndUnknownArgumentsAreIgnored()
    {
        var a = LauncherArgs.Parse(new[] { "--shots", "dir", "--ip=h", "--ticket=t", "--port=5", "--flow-shots", "--unknown=1" });
        Assert.That((a.Host, a.Port, a.Ticket), Is.EqualTo(("h", 5, "t")));
    }

    [Test]
    public void TheAddressAloneHasNoTicket()
    {
        var a = LauncherArgs.Parse(new[] { "--ip=h", "--port=5" });
        Assert.That(a.HasAddress, Is.True);
        Assert.That(a.Ticket, Is.Null);
    }

    [TestCase("")]
    [TestCase("--ip=h")]
    [TestCase("--port=5")]
    [TestCase("--ticket=t --port=5")]
    [TestCase("--ticket=t --ip=h")]
    [TestCase("--ip=h --port=0")]
    [TestCase("--ip=h --port=70000")]
    [TestCase("--ip=h --port=x")]
    [TestCase("--ip= --port=5")]
    public void AnIncompleteAddressIsNoAddress(string line) =>
        Assert.That(LauncherArgs.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)).HasAddress, Is.False);

    [Test]
    public void NullArgumentsAreEmpty() => Assert.That(LauncherArgs.Parse(null).HasAddress, Is.False);

    [Test]
    public void TheTicketValueMayContainEqualsAndIsNeverPrinted()
    {
        var a = LauncherArgs.Parse(new[] { "--ticket=MST_a=b", "--ip=h", "--port=1" });
        Assert.That(a.Ticket, Is.EqualTo("MST_a=b"));
        Assert.That(a.ToString(), Does.Not.Contain("MST_"));
    }

    [TestCase("--ip=h", "--ip without --port")]
    [TestCase("--ip=h --port=x", "--port is not a port number")]
    [TestCase("--ip=h --port=0", "--port is not a port number")]
    [TestCase("--ip=h --port=70000", "--port is not a port number")]
    [TestCase("--port=5", "--port without --ip")]
    [TestCase("--ticket=SECRETVALUE", "--ticket without")]
    [TestCase("--ticket=SECRETVALUE --ip=h", "--ip without --port")]
    public void ABrokenLineNamesItsProblemWithoutTheValues(string line, string expected)
    {
        var a = LauncherArgs.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        Assert.That(a.Problem, Does.StartWith(expected));
        Assert.That(a.Problem, Does.Not.Contain("SECRETVALUE"));
    }

    [TestCase("")]
    [TestCase("1234")]
    [TestCase("1234 --ticket=t --ip=h")]
    [TestCase("--ip=h --port=5")]
    [TestCase("--ip=h --port=5 --ticket=t")]
    public void AGoodLineHasNoProblem(string line) =>
        Assert.That(LauncherArgs.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Problem, Is.Null);
}
