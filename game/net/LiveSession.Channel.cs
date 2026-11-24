using Godot;
using Ms2.Net;
using Ms2.Net.Packets;

namespace Ms2Godot.Net;

/// <summary>
/// The channel: the number the server announced, the list it offers, the change request and the notices around a change. The
/// client's change is a request only: the server answers with GameToGame (<see cref="OnGameToGame"/>), which tears the
/// field down and loads the next server's field; the client has no field reload of its own for a channel change.
/// </summary>
public partial class LiveSession
{
    /// <summary>DAT_1439f4a7c: the channel of the last ServerEnter (starts at 1 as the client's initializer does, data_data_1439ed000.cpp:2072; 0 = the last ServerEnter announced none).</summary>
    int serverChannel = 1;
    /// <summary>DAT_1439f4a80: GetCurrentChannel's value while <see cref="serverChannel"/> is 0.</summary>
    int fallbackChannel = 1;
    ushort[] channelTabs = Array.Empty<ushort>();
    ushort[] classTable = new ushort[8];
    readonly Ms2.Ui.SavedServerAddress savedAddress = new();
    (string Host, ushort Port) activeGameEndpoint;

    internal void NoteGameEndpoint(string endpointHost, ushort endpointPort) => activeGameEndpoint = (endpointHost ?? "", endpointPort);

    /// <summary>UIServerListDialog::EnterGame @0x141ad2c50 (uiserverlistdialog.cpp:633-637): the channel to ask for is m_nChannel (the login's, <see cref="Ms2Godot.Flow.Session.Channel"/>),
    /// else GetCurrentChannel; it becomes the current channel (DAT_1439f4a7c) at once, ServerEnter then replaces it (<see cref="NoteServerEnter"/> shows the notice when they differ). Returns it for the request.</summary>
    short BeginEnterGame()
    {
        int n = Ms2Godot.Flow.Session.Channel;
        if (n == 0) n = CurrentChannel;
        serverChannel = n;
        return (short)n;
    }

    /// <summary>GetCurrentChannel @0x14092f640.</summary>
    public int CurrentChannel => serverChannel == 0 ? fallbackChannel : serverChannel;

    /// <summary>The channels of the current server (DynamicChannel kind 1, UIServerListDialog::SetChannels), in the order sent.</summary>
    public IReadOnlyList<ushort> ChannelTabs => channelTabs;

    /// <summary>DAT_143add400, DynamicChannel kind 0's eight values: the highest channel offered per channel group.</summary>
    public IReadOnlyList<ushort> ClassTable => classTable;

    /// <summary>WorldService::FUN_141bd9870 @0x141bd9870: DAT_143add400 at the field's channel group id (GetFieldChannelGroupId; 0 without field data).</summary>
    public int ChannelLimit(int channelGroupId) => classTable[(uint)channelGroupId < (uint)classTable.Length ? channelGroupId : 0];

    /// <summary>The ids UIServerListDialog::BuildChannelList lists for the current server (<see cref="Ms2.Ui.ChannelList"/>); empty before any DynamicChannel kind 1.</summary>
    public List<int> ChannelIds(int channelGroupId)
    {
        int gm = Me.User?.GmGrade ?? 0;
        return Ms2.Ui.ChannelList.Build(channelTabs.Select(t => (int)t), CurrentChannel, ChannelLimit(channelGroupId), gm);
    }

    /// <summary>Raised after each DynamicChannel (UIServerEventDispatcher::vfunc_1 @0x141347410: every listener's Notify; the system menu and the minimap rebuild their channel lists).
    /// UIServerListDialog's own set at EnterGame is <see cref="BeginEnterGame"/>.</summary>
    public event Action ChannelsChanged;

    /// <summary>A notice by common string id and its {0} argument (-1: none), shown by the HUD (FUN_141275260 /
    /// FUN_1412751b0 with a formatted string).</summary>
    public Action<int, int> ChannelNoticeSink;

    /// <summary>CSceneStateGame::OnServerEnter's channel part (scenestategame.cpp:2068-2082): with channel 0 the previous one
    /// is kept (at least 1) for GetCurrentChannel; with non-zero channel SaveServerAddress stores the current game address;
    /// a channel other than the current one shows string 0x2d7 as a chat line.</summary>
    void NoteServerEnter(short channel)
    {
        int n = (ushort)channel;
        if (n == 0 && serverChannel != 0) fallbackChannel = serverChannel < 1 ? 1 : serverChannel;
        if (n != 0 && CurrentChannel != 0 && CurrentChannel != n) ChannelNoticeSink?.Invoke(0x2d7, n);
        serverChannel = n;
        savedAddress.NoteServerEnter(channel, activeGameEndpoint.Host, activeGameEndpoint.Port);
        ChannelsChanged?.Invoke();
    }

    /// <summary>CNetworkService::OnDynamicChannel @0x14153c4b0 (networkservice.cpp:977).</summary>
    void OnDynamicChannel(DynamicChannelMessage m)
    {
        switch (m)
        {
            case DynamicChannelMessage.Channels c: channelTabs = c.Tabs; break;
            case DynamicChannelMessage.ClassTable t: classTable = t.Values; break;
            default: return;
        }
        GD.Print($"live: dynamic channel {m.GetType().Name} ({channelTabs.Length} channels)");
        ChannelsChanged?.Invoke();
    }

    /// <summary>
    /// WorldService::FUN_141bc80c0 @0x141bc80c0 (worldservice_1.cpp:4936): the change request. See <see cref="Ms2.Ui.ChannelChange"/>.
    /// MyPC::SendFunctionCube's part here is the guide's saved steps; STILL-OPEN: its furniture part (state 0x29, StateUseFurniture: FunctionCube 5 with the furniture key) waits for the furniture
    /// use state, which this client does not have yet (Home/UGC), so the player is never in it here.
    /// </summary>
    public bool RequestChannel(int channel)
    {
        if (game == null || Migrating) return false;   // Migrating has no client counterpart: there the old socket is gone, as is the sender's connection
        var s = new Ms2.Ui.ChannelChangeState
        {
            HasPlayer = Me.User != null && selfObject >= 0,
            Current = CurrentChannel,
            NoServerChannel = serverChannel == 0,
            ServiceTypeSet = (entry?.Field?.FieldType ?? 0) != 0,
            EmoteActive = selfEmotion == 1,
            Dead = Me.Dead,
        };
        return Ms2.Ui.ChannelChange.Request(channel, s, id => ChannelNoticeSink?.Invoke(id, -1), SendGuideStates, Send);
    }
}
