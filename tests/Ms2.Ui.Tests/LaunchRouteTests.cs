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
}
