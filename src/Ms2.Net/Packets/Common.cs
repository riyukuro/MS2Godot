// Layouts adapted from the Maple2 server (AGPL-3.0, github.com/MS2Community/Maple2) and checked against the restored client and recorded KMS2 traffic.
using System.IO.Compression;
using System.Numerics;
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>A position in the sync records: three i16, scaled 1:1 (the server adds a 0.001 rounding offset when it widens them; here they stay raw).</summary>
public readonly record struct Vector3S(short X, short Y, short Z) {
    public Vector3 ToVector3() => new(X, Y, Z);
}

public readonly record struct Rgba(byte B, byte G, byte R, byte A);

/// <summary>The 20 byte equip color: three colors, an index (-1 for none) and a palette id.</summary>
public readonly record struct EquipColor(Rgba Primary, Rgba Secondary, Rgba Tertiary, int Index, int PaletteId);

/// <summary>Thrown when a packet does not parse to its last byte.</summary>
public sealed class PacketLayoutException : Exception {
    public PacketLayoutException(string msg) : base(msg) { }
}

public static class ReaderExt {
    public static Vector3 ReadVec3(this ByteReader r) => new(r.ReadFloat(), r.ReadFloat(), r.ReadFloat());
    public static Vector3S ReadVec3S(this ByteReader r) => new(r.ReadShort(), r.ReadShort(), r.ReadShort());
    public static Rgba ReadRgba(this ByteReader r) => new(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());
    public static EquipColor ReadEquipColor(this ByteReader r) => new(r.ReadRgba(), r.ReadRgba(), r.ReadRgba(), r.ReadInt(), r.ReadInt());
    public static ushort ReadUShort(this ByteReader r) => (ushort) r.ReadShort();
    public static uint ReadUInt(this ByteReader r) => (uint) r.ReadInt();
    public static ulong ReadULong(this ByteReader r) => (ulong) r.ReadLong();

    /// <summary>Ends a packet reader: every byte must be consumed.</summary>
    public static void ExpectEnd(this ByteReader r, string what) {
        if (r.Available != 0) throw new PacketLayoutException($"{what}: {r.Available} bytes left over");
    }

    /// <summary>WriteDeflated: bool deflated, i32 size, then raw bytes, or (BE i32 inflated length + zlib stream).</summary>
    public static ByteReader ReadDeflated(this ByteReader r) {
        bool deflated = r.ReadBool();
        int size = r.ReadInt();
        if (size < 0 || size > r.Available) throw new PacketLayoutException($"deflated block size {size}");
        byte[] data = r.ReadBytes(size);
        if (!deflated) return new ByteReader(data);
        int inflated = (data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3];
        using var zs = new ZLibStream(new MemoryStream(data, 4, data.Length - 4), CompressionMode.Decompress);
        var ms = new MemoryStream();
        zs.CopyTo(ms);
        if (ms.Length != inflated) throw new PacketLayoutException($"inflated {ms.Length} bytes, header said {inflated}");
        return new ByteReader(ms.ToArray());
    }
}
