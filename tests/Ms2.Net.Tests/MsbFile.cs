namespace Ms2.Net.Tests;

/// <summary>Reader for MapleShark2 .msb recordings (local files, never part of the repo). Packet data is the plaintext body after the opcode.</summary>
public sealed class MsbPacket {
    public ushort Opcode; public bool ServerToClient; public byte[] Data;
}

public sealed class MsbFile {
    public string Path; public byte Locale; public uint Build; public string Error;
    public List<MsbPacket> Packets = new();

    public static MsbFile Read(string path) {
        var f = new MsbFile { Path = path };
        using var r = new BinaryReader(new MemoryStream(File.ReadAllBytes(path)));
        try {
            ushort ver = r.ReadUInt16();
            if (ver != 0x2027 && ver != 0x2030) throw new InvalidDataException($"format {ver:X4}");
            r.ReadBytes(r.ReadByte()); r.ReadUInt16();
            r.ReadBytes(r.ReadByte()); r.ReadUInt16();
            f.Locale = r.ReadByte(); f.Build = r.ReadUInt32();
            while (r.BaseStream.Position < r.BaseStream.Length) {
                r.ReadInt64();
                uint size = r.ReadUInt32();
                var p = new MsbPacket { Opcode = r.ReadUInt16() };
                p.ServerToClient = r.ReadByte() == 0; // the flag is the sender as MapleShark sees it: 1 = client (ResponseVersion, op 1, 8 bytes, has flag 1)
                if (size > r.BaseStream.Length - r.BaseStream.Position) throw new InvalidDataException("size");
                p.Data = r.ReadBytes((int) size);
                if (ver < 0x2030) r.ReadBytes(8);
                f.Packets.Add(p);
            }
        } catch (Exception e) when (e is EndOfStreamException or InvalidDataException) { f.Error = e.Message; }
        return f;
    }
}
