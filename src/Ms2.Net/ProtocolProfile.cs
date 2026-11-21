namespace Ms2.Net;

/// <summary>
/// One protocol flavour: the version the handshake and cipher use, and the opcode numbers of both directions. The packet classes
/// name their opcode by the Inbound / Outbound enum member; the profile maps that name to the wire number, so a later profile
/// (another client version) changes the numbers here and no packet class. Per-packet layout differences stay in the packet classes.
/// KMS 2525 and GMS2 (version 12) are the profiles.
/// </summary>
public sealed class ProtocolProfile {
    public string Name { get; }
    /// <summary>The client version the server hello must carry (CNetworkService::OnRequestVersion checks it) and the cipher is keyed with.</summary>
    public uint Version { get; }
    readonly Dictionary<Inbound, ushort> inbound;
    readonly Dictionary<Outbound, ushort> outbound;
    readonly Dictionary<ushort, Inbound> inboundByNumber;

    public ProtocolProfile(string name, uint version, IReadOnlyDictionary<Inbound, ushort> inbound, IReadOnlyDictionary<Outbound, ushort> outbound) {
        Name = name; Version = version;
        this.inbound = new(inbound); this.outbound = new(outbound);
        inboundByNumber = this.inbound.ToDictionary(kv => kv.Value, kv => kv.Key);
    }

    /// <summary>The numbers the enums carry.</summary>
    public static readonly ProtocolProfile Kms2525 = new("KMS2 2525", 2525,
        Enum.GetValues<Inbound>().ToDictionary(o => o, o => (ushort) o),
        Enum.GetValues<Outbound>().ToDictionary(o => o, o => (ushort) o));

    /// <summary>GMS2 (the server hello carries version 12): the numbers of the Maple2 upstream server's SendOp / RecvOp tables, see <see cref="Gms2Opcodes"/>.</summary>
    public static readonly ProtocolProfile Gms2 = new("GMS2 12", 12, Gms2Opcodes.InboundNumbers, Gms2Opcodes.OutboundNumbers);

    /// <summary>The profile new channels use until the hello is read.</summary>
    public static ProtocolProfile Default => Kms2525;

    public ushort Number(Inbound op) => inbound.TryGetValue(op, out var n) ? n : throw new KeyNotFoundException($"{Name}: no inbound number for {op}");
    public ushort Number(Outbound op) => outbound.TryGetValue(op, out var n) ? n : throw new KeyNotFoundException($"{Name}: no outbound number for {op}");
    public bool TryInbound(ushort number, out Inbound op) => inboundByNumber.TryGetValue(number, out op);
    public bool Is(ushort number, Inbound op) => inbound.TryGetValue(op, out var n) && n == number;
}
