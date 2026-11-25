using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>UIServerListDialog's server and channel choice from the type 1 login's ServerList (ReceiveServerList, BuildServerList, OnSelectServer).</summary>
public class ServerListPickTests
{
    static ServerListChannelEntry Row(int id, string name, params ushort[] channels) => new(id, name, 0, new[] { ("127.0.0.1", (ushort)20001) }, 0, channels);

    [Test]
    public void AListWithoutServersPicksNothing() =>
        Assert.That(ServerListPick.Pick(new ServerList("live", new()), null, 0), Is.Null);

    [Test]
    public void WithoutASavedServerTheFirstByIdIsPickedOnChannelOne()
    {
        var c = ServerListPick.Pick(new ServerList("live", new() { Row(3, "C", 1), Row(2, "B", 4, 2, 2, 1) }), null, 5);
        Assert.That((c.Server.Name, c.ServerId, c.Channel), Is.EqualTo(("B", 2, 1)));
        Assert.That(c.Channels, Is.EqualTo(new ushort[] { 1, 2, 4 }), "m_channels: ascending, without repeats");
        var gone = ServerListPick.Pick(new ServerList("live", new() { Row(3, "C", 1), Row(2, "B", 4, 2, 2, 1) }), "Gone", 5);
        Assert.That((gone.Server.Name, gone.Channel), Is.EqualTo(("B", 1)), "a saved server the list no longer has");
    }

    [Test]
    public void APickedServerIdIsAtLeastOne() =>
        Assert.That(ServerListPick.Pick(new ServerList("live", new() { Row(0, "A", 1) }), null, 0).ServerId, Is.EqualTo(1));

    [Test]
    public void TheSavedServerKeepsItsIdAndTheSavedChannelWithinItsChannelCount()
    {
        var list = new ServerList("live", new() { Row(1, "A", 1, 2, 3), Row(0, "B", 1, 2) });
        var kept = ServerListPick.Pick(list, "A", 2);
        Assert.That((kept.Server.Name, kept.ServerId, kept.Channel), Is.EqualTo(("A", 1, 2)));
        Assert.That(ServerListPick.Pick(list, "A", 9).Channel, Is.EqualTo(3), "clamped to the channel count");
        Assert.That(ServerListPick.Pick(list, "A", 0).Channel, Is.EqualTo(1));
        Assert.That(ServerListPick.Pick(list, "B", 1).ServerId, Is.EqualTo(0), "BuildServerList keeps the id as sent");
    }

    [Test]
    public void ARepeatedNameKeepsTheLastRow()
    {
        var c = ServerListPick.Pick(new ServerList("live", new() { Row(1, "A", 1), Row(5, "A", 7, 8) }), null, 0);
        Assert.That((c.ServerId, c.Channels), Is.EqualTo((5, (IReadOnlyList<ushort>)new ushort[] { 7, 8 })));
    }

    [Test]
    public void TheEnteredServersChannelsFollowDynamicChannel()
    {
        // SetChannels (uiserverlistdialog.cpp:681-697) replaces only the entered server's channels; the server list's next
        // connect then clamps the saved channel to them (BuildServerList :473)
        var list = new ServerList("live", new() { Row(1, "A", 1, 2), Row(2, "B", 1, 2) });
        var later = ServerListPick.WithChannels(list, "A", new ushort[] { 1, 2, 3, 4 });
        Assert.That(ServerListPick.Pick(later, "A", 4).Channel, Is.EqualTo(4), "channel 4 is there now");
        Assert.That(ServerListPick.Pick(list, "A", 4).Channel, Is.EqualTo(2), "the first list had 2");
        Assert.That(later.Servers[1].Channels, Is.EqualTo(new ushort[] { 1, 2 }), "the other server keeps its own");
        Assert.That(ServerListPick.WithChannels(list, "Gone", new ushort[] { 9 }), Is.SameAs(list), "no such server");
        Assert.That(ServerListPick.WithChannels(list, "A", System.Array.Empty<ushort>()), Is.SameAs(list), "no channels yet");
    }
}
