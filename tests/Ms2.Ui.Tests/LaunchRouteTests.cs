using Ms2.Net;
using Ms2.Ui.Launch;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The launcher's start routing, with no Godot: the choice between the launcher, the offline flow and the live mode.
/// The addresses in these tests are from the reserved documentation ranges, so nothing here can reach a real server.
/// </summary>
public class LaunchRouteTests
{
    static readonly ServerEntry Reserved = new("Reserved", "192.0.2.1", 6553);

    [Test]
    public void SinglePlayerGoesToTheOfflineFlowAndAnEntryToTheLiveMode()
    {
        // the flow scene with no live address and no choice: the launcher is the first page
        Assert.That(LaunchRoute.Decide(LaunchRoute.FlowScene, null, false, null).Mode, Is.EqualTo(LaunchMode.Launcher));

        // Single player: the client's own login screen and the offline flow, exactly as before the launcher
        var single = LaunchRoute.Decide(LaunchRoute.FlowScene, null, true, null);
        Assert.That(single.Mode, Is.EqualTo(LaunchMode.Offline));
        Assert.That(single.Server, Is.Null);

        // an entry's Connect: the live mode with that entry's address
        var live = LaunchRoute.Decide(LaunchRoute.FlowScene, null, false, Reserved);
        Assert.That(live.Mode, Is.EqualTo(LaunchMode.Live));
        Assert.That(live.Server, Is.EqualTo(Reserved));
        Assert.That(live.Server.Host, Is.EqualTo("192.0.2.1"));
        Assert.That(live.Server.Port, Is.EqualTo(6553));
    }

    [Test]
    public void LiveLoginInTheEnvironmentSkipsTheLauncher()
    {
        var env = LaunchRoute.Decide(LaunchRoute.FlowScene, "192.0.2.1:6553", false, null);
        Assert.That(env.Mode, Is.EqualTo(LaunchMode.Live));
        Assert.That(env.Server, Is.Null, "the session reads the address from the variable itself");

        // it overrides a stored entry too
        Assert.That(LaunchRoute.Decide(LaunchRoute.FlowScene, "192.0.2.1:6553", false, Reserved).Mode, Is.EqualTo(LaunchMode.Live));

        // the scenes that run alone (setup, the HUD, the field, the tests) never see the launcher
        Assert.That(LaunchRoute.Decide("res://ui/hud.tscn", null, false, null).Mode, Is.EqualTo(LaunchMode.Offline));
        Assert.That(LaunchRoute.Decide("res://ui/field.tscn", null, false, Reserved).Mode, Is.EqualTo(LaunchMode.Offline));
        Assert.That(LaunchRoute.Decide("res://setup.tscn", null, false, null).Mode, Is.EqualTo(LaunchMode.Offline));
        Assert.That(LaunchRoute.Decide("res://game/tests/login_typing.tscn", null, false, null).Mode, Is.EqualTo(LaunchMode.Offline));
    }

    [Test]
    public void TheCommandLineAddressNeedsATicketOrTheAccount()
    {
        var withTicket = LauncherArgs.Parse(new[] { "1234", "--ticket=t", "--ip=192.0.2.1", "--port=6553" });
        var alone = LauncherArgs.Parse(new[] { "--ip=192.0.2.1", "--port=6553" });
        Assert.That(LaunchRoute.LiveAddress(withTicket, null), Is.EqualTo("192.0.2.1:6553"));
        Assert.That(LaunchRoute.LiveAddress(alone, "id:pw"), Is.EqualTo("192.0.2.1:6553"));
        Assert.That(LaunchRoute.LiveAddress(alone, null), Is.Null);
        Assert.That(LaunchRoute.LiveAddress(LauncherArgs.Parse(new[] { "--ticket=t", "--ip=h" }), "id:pw"), Is.Null);   // no port
        Assert.That(LaunchRoute.LiveAddress(LauncherArgs.Parse(null), "id:pw"), Is.Null);
        // an address alone leaves the launcher in place; with the ticket the page is skipped
        Assert.That(LaunchRoute.Decide(LaunchRoute.FlowScene, LaunchRoute.LiveAddress(alone, null), false, null).Mode, Is.EqualTo(LaunchMode.Launcher));
        Assert.That(LaunchRoute.Decide(LaunchRoute.FlowScene, LaunchRoute.LiveAddress(withTicket, null), false, null).Mode, Is.EqualTo(LaunchMode.Live));
    }

    /// <summary>
    /// Every command line input, through the whole route: the live address, the first page, and how the login scene logs in.
    /// Only a ticket with its address is the passport start (no login dialog); the account variable and the launcher's
    /// entries keep the dialog; an address alone, no arguments and arguments with a problem keep the launcher page.
    /// </summary>
    [TestCase(new string[0], null, null, LaunchMode.Launcher, LoginStart.Dialog, TestName = "NoArguments")]
    [TestCase(new[] { "--ip=192.0.2.1", "--port=6553" }, null, null, LaunchMode.Launcher, LoginStart.Dialog, TestName = "AddressAlone")]
    [TestCase(new[] { "1234", "--ticket=t", "--ip=192.0.2.1", "--port=6553" }, null, "192.0.2.1:6553", LaunchMode.Live, LoginStart.Passport, TestName = "AddressAndTicket")]
    [TestCase(new[] { "--ip=192.0.2.1", "--port=6553" }, "id:pw", "192.0.2.1:6553", LaunchMode.Live, LoginStart.Dialog, TestName = "AddressAndAccount")]
    [TestCase(new[] { "--ticket=t", "--ip=192.0.2.1", "--port=6553" }, "id:pw", "192.0.2.1:6553", LaunchMode.Live, LoginStart.Passport, TestName = "AddressTicketAndAccount")]
    [TestCase(new[] { "1234", "--ticket=t", "--ip=192.0.2.1", "--port=x" }, null, null, LaunchMode.Launcher, LoginStart.Dialog, TestName = "BadPortWithABarePort")]
    [TestCase(new[] { "--ticket=t" }, null, null, LaunchMode.Launcher, LoginStart.Dialog, TestName = "TicketWithoutAddress")]
    [TestCase(new[] { "--ticket=t", "--ip=192.0.2.1" }, "id:pw", null, LaunchMode.Launcher, LoginStart.Dialog, TestName = "IpWithoutPort")]
    [TestCase(new[] { "--ticket=t", "--port=6553" }, "id:pw", null, LaunchMode.Launcher, LoginStart.Dialog, TestName = "PortWithoutIp")]
    public void EveryCommandLineInputTakesItsRoute(string[] argv, string account, string address, LaunchMode mode, LoginStart login)
    {
        var args = LauncherArgs.Parse(argv);
        string live = LaunchRoute.LiveAddress(args, account);
        Assert.That(live, Is.EqualTo(address));
        Assert.That(LaunchRoute.Decide(LaunchRoute.FlowScene, live, false, null).Mode, Is.EqualTo(mode));
        Assert.That(LaunchRoute.LoginStartFor(args), Is.EqualTo(login));
    }

    [Test]
    public void TheLaunchersOwnStartsKeepTheLoginDialog()
    {
        // single player and an entry's Connect come with no command line ticket: the id and password dialog
        Assert.That(LaunchRoute.LoginStartFor(LauncherArgs.Parse(null)), Is.EqualTo(LoginStart.Dialog));
        Assert.That(LaunchRoute.LoginStartFor(null), Is.EqualTo(LoginStart.Dialog));
    }
}
