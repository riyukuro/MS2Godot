/*
PL_MPEG - MPEG1 Video decoder, MP2 Audio decoder, MPEG-PS demuxer
SPDX-License-Identifier: MIT

Dominic Szablewski - https://phoboslab.org
*/
// C# port of pl_mpeg's plm_buffer bit reader, over a complete in-memory elementary stream.

namespace Ms2.Video.Mpeg1;

/// <summary>A big-endian bit reader over the whole stream (plm_buffer with the source already ended).</summary>
internal sealed class BitBuffer
{
    readonly byte[] bytes;
    readonly long length;
    public long BitIndex;

    public byte[] Bytes => bytes;
    public long Length => length;

    public BitBuffer(byte[] bytes) : this(bytes, bytes.Length) { }
    public BitBuffer(byte[] bytes, int length) { this.bytes = bytes; this.length = length; }

    public bool Has(long count) => (length << 3) - BitIndex >= count;

    public int Read(int count)
    {
        if (!Has(count)) return 0;
        int value = 0;
        while (count > 0)
        {
            int current = bytes[BitIndex >> 3];
            int remaining = 8 - (int)(BitIndex & 7);
            int read = remaining < count ? remaining : count;
            int shift = remaining - read;
            int mask = 0xff >> (8 - read);
            value = (value << read) | ((current & (mask << shift)) >> shift);
            BitIndex += read;
            count -= read;
        }
        return value;
    }

    public void Align() => BitIndex = ((BitIndex + 7) >> 3) << 3;

    public void Skip(int count) { if (Has(count)) BitIndex += count; }

    /// <summary>The next start code's value (the byte after 00 00 01), positioned after it; -1 at the end.</summary>
    public int NextStartCode()
    {
        Align();
        while (Has(5 << 3))
        {
            long i = BitIndex >> 3;
            if (bytes[i] == 0 && bytes[i + 1] == 0 && bytes[i + 2] == 1)
            {
                BitIndex = (i + 4) << 3;
                return bytes[i + 3];
            }
            BitIndex += 8;
        }
        return -1;
    }

    public int FindStartCode(int code)
    {
        while (true)
        {
            int current = NextStartCode();
            if (current == code || current == -1) return current;
        }
    }

    public int HasStartCode(int code)
    {
        long previous = BitIndex;
        int current = FindStartCode(code);
        BitIndex = previous;
        return current;
    }

    public bool PeekNonZero(int count)
    {
        if (!Has(count)) return false;
        int value = Read(count);
        BitIndex -= count;
        return value != 0;
    }

    /// <summary>Walks a VLC table of (index, value) pairs one bit at a time.</summary>
    public int ReadVlc(int[] table)
    {
        int index = 0, value;
        do
        {
            int e = (index + Read(1)) << 1;
            index = table[e];
            value = table[e + 1];
        } while (index > 0);
        return value;
    }
}
