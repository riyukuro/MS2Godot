using Ms2.Net.Packets;

namespace Ms2.Ui;

/// <summary>
/// UIServerListDialog's choice of server and channel from the ServerList the type 1 login brought back.
/// ReceiveServerList @0x141ad1150 (uiserverlistdialog.cpp:310-343) keys the servers by name (m_servers: a repeated name keeps the
/// last row) and each server's channels by id (m_channels: ascending, without repeats). OnLoadComplete @0x141ad16b0 (:350-352)
/// starts the channel at the saved option's, at least 1. BuildServerList @0x141ad1d20 (:451-499) walks the servers by id
/// (Unk_SlServerOrder, :193-199) and selects the one named like the saved server option, clamping the channel to 1..its channel
/// count (:473); m_nServerId is that server's id (:497). Without a saved match the page waits for the player's pick,
/// OnSelectServer @0x141ad25a0 (:529-538): a server other than the current one resets the channel to 1, and m_nServerId is its
/// id, at least 1. ConnectToServer @0x141ad2380 (:502-517) then connects to one of the picked server's addresses.
/// </summary>
public static class ServerListPick
{
    /// <summary>The picked server, m_nServerId, m_nChannel and the server's channel ids (m_channels' order).</summary>
    public sealed record Choice(ServerListChannelEntry Server, int ServerId, int Channel, IReadOnlyList<ushort> Channels);

    /// <summary>
    /// The choice for <paramref name="list"/>; null when it has no server (CSceneStateLogin::OnPacket shows box 0x20e then,
    /// scenestatelogin.cpp:121-129). <paramref name="savedServer"/> and <paramref name="savedChannel"/> are the options'
    /// m_strServer and m_strChannel; with no saved server in the list the first by id stands in for the player's pick.
    /// </summary>
    public static Choice Pick(ServerList list, string savedServer, int savedChannel)
    {
        var byName = new Dictionary<string, ServerListChannelEntry>();
        foreach (var s in list.Servers) byName[s.Name] = s;
        if (byName.Count == 0) return null;
        var servers = byName.Values.OrderBy(s => s.ServerId).ToList();
        int channel = Math.Max(1, savedChannel);
        var saved = servers.FirstOrDefault(s => s.Name == savedServer);
        if (saved != null)
        {
            var ids = ChannelIds(saved);
            return new Choice(saved, saved.ServerId, Math.Max(1, Math.Min(channel, ids.Count)), ids);
        }
        var first = servers[0];
        if (first.Name != savedServer) channel = 1;
        return new Choice(first, Math.Max(first.ServerId, 1), channel, ChannelIds(first));
    }

    static List<ushort> ChannelIds(ServerListChannelEntry s) => new SortedSet<ushort>(s.Channels).ToList();

    /// <summary>
    /// The dialog's list as it stands later: SetChannels @0x141ad3030 (uiserverlistdialog.cpp:681-697), from DynamicChannel,
    /// replaces the channels of the server named <paramref name="server"/> (GetServer, the one entered) with
    /// <paramref name="channels"/>; the other servers keep the ServerList's. No such server, or no channels yet, leaves the
    /// list as it is.
    /// </summary>
    public static ServerList WithChannels(ServerList list, string server, IReadOnlyList<ushort> channels)
    {
        if (list == null || string.IsNullOrEmpty(server) || channels == null || channels.Count == 0) return list;
        if (!list.Servers.Any(s => s.Name == server)) return list;
        var ids = channels.ToArray();
        return list with { Servers = list.Servers.Select(s => s.Name == server ? s with { Channels = ids } : s).ToList() };
    }
}
