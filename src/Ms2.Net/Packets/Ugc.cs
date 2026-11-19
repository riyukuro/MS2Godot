// Layouts from the restored client's CUgcManager (ugcmanager_4.cpp), checked against the server fork's UgcPacket.
using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// Inbound Ugc (CUgcManager::OnUgc @0x1418897f0): u8 mode, then the mode's fields. The modes read here:
/// <list type="bullet">
/// <item>0x11 (<see cref="UgcEndpoint"/>): the web server's addresses, sent by the login server after the login result.</item>
/// <item>0x0b (<see cref="UgcProfileImage"/>): a character's profile picture changed (OnCharacterProfileImage @0x14188e200).</item>
/// <item>0x16 (<see cref="UgcSetMode"/>): SetMode(i32).</item>
/// </list>
/// Other modes are kept as their mode alone.
/// </summary>
public abstract record UgcMessage(byte Mode) {
    public static UgcMessage Read(ByteReader r) {
        byte mode = r.ReadByte();
        switch (mode) {
            case 0x11: return new UgcEndpoint(r.ReadUnicodeString(), r.ReadUnicodeString(), r.ReadUnicodeString(), r.ReadByte());
            case 0x0b: return new UgcProfileImage(r.ReadInt(), r.ReadLong(), r.ReadUnicodeString());
            case 0x16: return new UgcSetMode(r.ReadInt());
            default: r.ReadBytes(r.Available); return new UgcOther(mode);
        }
    }
}

/// <summary>
/// Mode 0x11: the SOAP url ("&lt;base&gt;ws.asmx?wsdl"), the web host the downloads come from, the region and the SOAP
/// mode. CSoapClient::FUN_14176fcf0 keeps the url less its last 12 characters as the upload base in mode 2 (no base in
/// any other mode); FUN_1410268a0 keeps the host, and a region of "cn" turns on the CN download layout.
/// </summary>
public sealed record UgcEndpoint(string SoapUrl, string Host, string Region, byte SoapMode) : UgcMessage(0x11) {
    /// <summary>m_strPath: the base "urq.aspx" is posted to, or "" outside mode 2.</summary>
    public string UploadBase => SoapMode == 2 && SoapUrl != null && SoapUrl.Length >= 12 ? SoapUrl[..^12] : "";
}

/// <summary>Mode 0x0b: i32 object id (0 from the login server), i64 character id, unicode the picture's key.</summary>
public sealed record UgcProfileImage(int ObjectId, long CharacterId, string Key) : UgcMessage(0x0b);

/// <summary>Mode 0x16: CUgcManager::SetMode. In mode 1 the client takes every profile picture key as "".</summary>
public sealed record UgcSetMode(int Value) : UgcMessage(0x16);

/// <summary>A mode not read here.</summary>
public sealed record UgcOther(byte OtherMode) : UgcMessage(OtherMode);

/// <summary>CUgcManager::SendUGCMode11 (@0x141883380): the uploaded profile picture's key (u8 0x0b, unicode key).</summary>
public sealed record UgcProfileImageRequest(string Key) : IOutPacket {
    public Outbound Op => Outbound.Ugc;
    public void Write(ByteWriter w) { w.WriteByte(0x0b); w.WriteUnicodeString(Key ?? ""); }
}

/// <summary>CUgcManager::SendUGCMode0 (@0x141883800): the upload failed (u8 0, nothing else); sent by FUN_141882d80 for a
/// nonzero upload answer.</summary>
public sealed record UgcUploadFailedRequest : IOutPacket {
    public Outbound Op => Outbound.Ugc;
    public void Write(ByteWriter w) { w.WriteByte(0); }
}
