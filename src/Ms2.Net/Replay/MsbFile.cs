namespace Ms2.Net.Replay;

/// <summary>One recorded packet (MapleShark2 .msb): plaintext body after the opcode, the sender, and the recorder's timestamp.</summary>
public sealed class MsbPacket {
    public ushort Opcode;
    public bool ServerToClient;
    public byte[] Data;
    /// <summary>.NET ticks (100 ns) of the recorder's clock; only differences mean anything.</summary>
    public long Ticks;
}

/// <summary>Reader for MapleShark2 .msb recordings. Recordings are the player's own local files and are never part of the repo.</summary>
public sealed class MsbFile {
    public string Path;
    public byte Locale;
    public uint Build;
    public string Error;
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
                long ticks = r.ReadInt64();
                uint size = r.ReadUInt32();
                var p = new MsbPacket { Ticks = ticks, Opcode = r.ReadUInt16() };
                // the flag is the sender as the recorder sees it: 1 = client (ResponseVersion, op 1, 8 bytes, has flag 1)
                p.ServerToClient = r.ReadByte() == 0;
                if (size > r.BaseStream.Length - r.BaseStream.Position) throw new InvalidDataException("size");
                p.Data = r.ReadBytes((int) size);
                if (ver < 0x2030) r.ReadBytes(8);
                f.Packets.Add(p);
            }
        } catch (Exception e) when (e is EndOfStreamException or InvalidDataException) { f.Error = e.Message; }
        return f;
    }
}
