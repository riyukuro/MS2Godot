using Maple2.PacketLib.Tools;

namespace Ms2.Net;

/// <summary>
/// One opcode's handler list of PacketHandlerManager (@0x140da50e0, unit_140da50e0_observermanagers.cpp): RegisterHandler
/// (@0x140da5180) appends, UnregisterHandler (@0x140da51e0) removes the first equal handler, and Dispatch (@0x140da52f0) calls
/// the handlers in list order, each on the packet rewound to its body (CInPacket::GetOpcode seeks to 6 before every call). So
/// two handlers of one opcode see the state the earlier ones left: the order is the order of registration.
/// </summary>
public sealed class PacketHandlerList
{
    readonly List<Action<ByteReader>> handlers = new();

    public int Count => handlers.Count;

    /// <summary>RegisterHandler: the handler goes last.</summary>
    public void Register(Action<ByteReader> handler) => handlers.Add(handler);

    /// <summary>UnregisterHandler: the first equal handler leaves the list; no match does nothing.</summary>
    public void Unregister(Action<ByteReader> handler)
    {
        int i = handlers.IndexOf(handler);
        if (i >= 0) handlers.RemoveAt(i);
    }

    /// <summary>Dispatch: each handler in list order, by index as the client does (a handler may change the list), on a
    /// fresh reader of the body.</summary>
    public void Dispatch(Func<ByteReader> body)
    {
        for (int i = 0; i < handlers.Count; i++) handlers[i](body());
    }
}
