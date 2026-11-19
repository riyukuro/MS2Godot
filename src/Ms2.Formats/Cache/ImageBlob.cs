using System.Buffers.Binary;
using K4os.Compression.LZ4;

namespace Ms2.Formats.Cache;

/// <summary>
/// The body of an image's cache file: what it holds (<see cref="Kind"/>), its size and its bytes as one LZ4 block (HC level
/// 6: on the game's 863 UI atlases, 2235 MB of pixels, 571 MB on disk against the archive's 482 MB, decoded in 674 ms
/// against 3175 ms for the archive read and the pixel conversion; the setup step encodes them). Layout: kind (1 byte),
/// width, height, the decoded length (32-bit each), then the block.
/// </summary>
public static class ImageBlob
{
    public enum Kind : byte
    {
        /// <summary>R, G, B, A bytes, width by height.</summary>
        Rgba = 0,
        /// <summary>The file as it is in the archive (a block-compressed DDS file, or one the pixel conversion does not take).</summary>
        File = 1,
    }

    const int Head = 1 + 4 + 4 + 4;

    public static byte[] Write(Kind kind, int w, int h, ReadOnlySpan<byte> bytes)
    {
        var body = new byte[Head + LZ4Codec.MaximumOutputSize(bytes.Length)];
        body[0] = (byte)kind;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(1), w);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(5), h);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(9), bytes.Length);
        int n = bytes.Length == 0 ? 0 : LZ4Codec.Encode(bytes, body.AsSpan(Head), LZ4Level.L06_HC);
        if (n < 0) throw new InvalidOperationException("LZ4 encode failed");
        Array.Resize(ref body, Head + n);
        return body;
    }

    /// <summary>The image of a body; throws InvalidDataException on a body that does not decode to its stated length.</summary>
    public static (Kind kind, int w, int h, byte[] bytes) Read(ReadOnlySpan<byte> body)
    {
        if (body.Length < Head) throw new InvalidDataException("short image body");
        var kind = (Kind)body[0];
        if (kind != Kind.Rgba && kind != Kind.File) throw new InvalidDataException($"image kind {body[0]}");
        int w = BinaryPrimitives.ReadInt32LittleEndian(body.Slice(1)), h = BinaryPrimitives.ReadInt32LittleEndian(body.Slice(5));
        int len = BinaryPrimitives.ReadInt32LittleEndian(body.Slice(9));
        if (len < 0 || w < 0 || h < 0 || kind == Kind.Rgba && (long)w * h * 4 != len) throw new InvalidDataException($"image {w}x{h} of {len} bytes");
        var bytes = new byte[len];
        if (len > 0 && LZ4Codec.Decode(body.Slice(Head), bytes) != len) throw new InvalidDataException("the LZ4 block does not decode to its length");
        return (kind, w, h, bytes);
    }
}
