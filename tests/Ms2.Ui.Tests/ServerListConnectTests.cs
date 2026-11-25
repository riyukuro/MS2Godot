using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ms2.Ui.Launch;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The lobby's server list connect (<see cref="ServerListConnect"/>) with a fake session connect: one connect at a time, as the
/// server list dialog's m_nConnecting keeps it (uiserverlistdialog.cpp:502-517, :665-671), and the flow read again when the
/// connect ends, since a lost login connection may have moved it to the login scene meanwhile.
/// </summary>
public class ServerListConnectTests
{
    /// <summary>A session connect that ends when the test says so, counting how often it was started.</summary>
    sealed class FakeConnect
    {
        public readonly List<TaskCompletionSource<ServerListConnectResult>> Started = new();
        public Task<ServerListConnectResult> Connect()
        {
            var t = new TaskCompletionSource<ServerListConnectResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Started.Add(t);
            return t.Task;
        }
    }

    /// <summary>
    /// The popdown's end raised twice, or the AlreadyLogin box's OK while the popdown's connect still runs: the second start is
    /// ignored at once, so one RequestServerList and one login go out, and the first login is not dropped by a second one.
    /// </summary>
    [Test]
    public async Task ASecondStartDuringAConnectIsIgnored()
    {
        var g = new ServerListConnect();
        var serverList = new FakeConnect();
        var configured = new FakeConnect();
        var first = g.RunAsync(() => true, serverList.Connect);
        Assert.That(g.Running, Is.True, "the guard is set before the connect's first await");
        var again = await g.RunAsync(() => true, serverList.Connect);
        var ok = await g.RunAsync(() => true, configured.Connect);
        Assert.That(again, Is.EqualTo(ServerListConnectEnd.Ignored));
        Assert.That(ok, Is.EqualTo(ServerListConnectEnd.Ignored));
        Assert.That(serverList.Started, Has.Count.EqualTo(1));
        Assert.That(configured.Started, Is.Empty);
        serverList.Started[0].SetResult(new ServerListConnectResult(true));
        Assert.That(await first, Is.EqualTo(ServerListConnectEnd.Entered));
        Assert.That(g.Running, Is.False);
    }

    /// <summary>Each end of a connect frees the guard, a thrown one too: the AlreadyLogin box's OK then connects again.</summary>
    [Test]
    public async Task TheNextStartConnectsOnceTheLastEnded()
    {
        var g = new ServerListConnect();
        var c = new FakeConnect();
        var first = g.RunAsync(() => true, c.Connect);
        c.Started[0].SetResult(new ServerListConnectResult(false, 4));
        Assert.That(await first, Is.EqualTo(ServerListConnectEnd.AlreadyLogin));
        var second = g.RunAsync(() => true, c.Connect);
        Assert.That(c.Started, Has.Count.EqualTo(2));
        c.Started[1].SetException(new InvalidOperationException("socket"));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await second);
        Assert.That(g.Running, Is.False);
        var third = g.RunAsync(() => true, c.Connect);
        Assert.That(c.Started, Has.Count.EqualTo(3));
        c.Started[2].SetResult(new ServerListConnectResult(true));
        Assert.That(await third, Is.EqualTo(ServerListConnectEnd.Entered));
    }

    /// <summary>Off character select nothing starts.</summary>
    [Test]
    public async Task NoStartOffTheLobby()
    {
        var g = new ServerListConnect();
        var c = new FakeConnect();
        Assert.That(await g.RunAsync(() => false, c.Connect), Is.EqualTo(ServerListConnectEnd.Ignored));
        Assert.That(c.Started, Is.Empty);
    }

    /// <summary>
    /// The whole list of results with the flow moved to the login scene during the connect (the login connection lost, whose
    /// box is up): a failure and AlreadyLogin show nothing more, and a good login is dropped rather than left logged in behind
    /// the login scene.
    /// </summary>
    [TestCase(true, null, ServerListConnectEnd.LeftLoggedIn)]
    [TestCase(false, 4, ServerListConnectEnd.Left)]
    [TestCase(false, 5, ServerListConnectEnd.Left)]
    [TestCase(false, 3, ServerListConnectEnd.Left)]
    [TestCase(false, null, ServerListConnectEnd.Left)]
    public async Task TheFlowMovedDuringTheConnect(bool loggedIn, int? refusal, ServerListConnectEnd want)
    {
        var g = new ServerListConnect();
        var c = new FakeConnect();
        bool lobby = true;
        var run = g.RunAsync(() => lobby, c.Connect);
        lobby = false;
        c.Started[0].SetResult(new ServerListConnectResult(loggedIn, refusal));
        Assert.That(await run, Is.EqualTo(want));
    }

    /// <summary>The same results with the flow still on character select.</summary>
    [TestCase(true, null, ServerListConnectEnd.Entered)]
    [TestCase(false, 4, ServerListConnectEnd.AlreadyLogin)]
    [TestCase(false, 5, ServerListConnectEnd.AlreadyLogin)]
    [TestCase(false, 3, ServerListConnectEnd.Failed)]
    [TestCase(false, null, ServerListConnectEnd.Failed)]
    public async Task TheFlowStayedOnTheLobby(bool loggedIn, int? refusal, ServerListConnectEnd want)
    {
        var g = new ServerListConnect();
        var c = new FakeConnect();
        var run = g.RunAsync(() => true, c.Connect);
        c.Started[0].SetResult(new ServerListConnectResult(loggedIn, refusal));
        Assert.That(await run, Is.EqualTo(want));
    }
}
