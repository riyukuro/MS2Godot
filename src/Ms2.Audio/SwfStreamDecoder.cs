namespace Ms2.Audio;

/// <summary>
/// Decodes a swf movie's stream sound (SoundStreamHead + the SoundStreamBlock payloads, one per frame) to PCM. The client's GFx
/// loader hands these to its sound renderer (Scaleform's GFx::Sound, FMOD behind GFxManager::SetMasterVolume @0x1422afdd0);
/// here the payloads are decoded the way the swf format says: codec 0 and 3 raw samples, 1 ADPCM (4096-sample packets, a 2-bit
/// code size, each packet opening with a 16-bit sample and a 6-bit step index per channel), 2 MP3 (a 4-byte sample count and seek
/// count in front of each block's MPEG frames).
/// </summary>
public static class SwfStreamDecoder
{
    static readonly int[] StepTable =
    {
        7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97, 107, 118, 130,
        143, 157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658, 724, 796, 876, 963, 1060, 1166, 1282,
        1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358, 5894, 6484, 7132, 7845, 8630,
        9493, 10442, 11487, 12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767,
    };

    static readonly int[][] IndexTables =
    {
        new[] { -1, 2 },
        new[] { -1, -1, 2, 4 },
        new[] { -1, -1, -1, -1, 2, 4, 6, 8 },
        new[] { -1, -1, -1, -1, -1, -1, -1, -1, 1, 2, 4, 6, 8, 10, 13, 16 },
    };

    /// <summary>The movie's stream sound as PCM laid out from frame 0, or null when it has none. A stream whose first block comes at a
    /// later frame (the MasterSkill movies start at frame 5) is preceded by that many frames of silence, so the PCM position is the
    /// movie's time.</summary>
    public static Pcm16 Decode(Ms2.Swf.SwfMovie movie)
    {
        if (movie.SoundStream is not { Blocks.Count: > 0 } s) return null;
        var pcm = Decode(s.Codec, s.Rate, s.Stereo, s.Bits16, s.SamplesPerFrame, s.Blocks.Select(b => b.Data).ToList());
        int lead = s.Blocks[0].Frame * s.SamplesPerFrame * pcm.Channels;
        if (lead <= 0) return pcm;
        var all = new short[lead + pcm.Samples.Length];
        Array.Copy(pcm.Samples, 0, all, lead, pcm.Samples.Length);
        return new Pcm16 { Rate = pcm.Rate, Channels = pcm.Channels, Samples = all };
    }

    /// <param name="codec">0 raw, 1 ADPCM, 2 MP3, 3 raw little endian.</param>
    /// <param name="samplesPerFrame">The head's average sample count per block (an ADPCM block's sample count).</param>
    public static Pcm16 Decode(int codec, int rate, bool stereo, bool bits16, int samplesPerFrame, IReadOnlyList<byte[]> blocks)
    {
        switch (codec)
        {
            case 1: return Adpcm(rate, stereo ? 2 : 1, samplesPerFrame, blocks);
            case 2: return Mp3(blocks);
            case 0: case 3:
            {
                // codec 0 is platform endian (little endian on every shipped movie); 8-bit samples are unsigned.
                int ch = stereo ? 2 : 1, total = blocks.Sum(b => b.Length);
                int n = bits16 ? total / 2 : total;
                var s = new short[n];
                int at = 0;
                foreach (var b in blocks)
                    if (bits16) for (int i = 0; i + 1 < b.Length; i += 2) s[at++] = (short)(b[i] | b[i + 1] << 8);
                    else foreach (var v in b) s[at++] = (short)((v - 128) << 8);
                return new Pcm16 { Rate = rate, Channels = ch, Samples = s };
            }
            default: throw new NotSupportedException($"stream sound codec {codec}");
        }
    }

    static Pcm16 Adpcm(int rate, int channels, int samplesPerBlock, IReadOnlyList<byte[]> blocks)
    {
        var all = new List<short>(blocks.Count * samplesPerBlock * channels);
        var predictor = new int[channels];
        var index = new int[channels];
        foreach (var b in blocks)
        {
            var bits = new BitReader(b);
            int size = bits.Read(2) + 2;
            var table = IndexTables[size - 2];
            int sign = 1 << (size - 1);
            int done = 0;
            while (done < samplesPerBlock && !bits.AtEnd)
            {
                // a packet is up to 4096 samples: the first is stored, the others are code deltas
                for (int c = 0; c < channels; c++) { predictor[c] = (short)bits.Read(16); index[c] = Math.Min(bits.Read(6), 88); }
                for (int c = 0; c < channels; c++) all.Add((short)predictor[c]);
                done++;
                for (int i = 1; i < 4096 && done < samplesPerBlock && !bits.AtEnd; i++, done++)
                    for (int c = 0; c < channels; c++)
                    {
                        int code = bits.Read(size), mag = code & (sign - 1);
                        int step = StepTable[index[c]];
                        int diff = ((2 * mag + 1) * step) >> (size - 1);
                        predictor[c] = Math.Clamp(predictor[c] + ((code & sign) != 0 ? -diff : diff), short.MinValue, short.MaxValue);
                        index[c] = Math.Clamp(index[c] + table[mag], 0, 88);
                        all.Add((short)predictor[c]);
                    }
            }
        }
        return new Pcm16 { Rate = rate, Channels = channels, Samples = all.ToArray() };
    }

    /// <summary>Each block: sample count (u16) and seek samples (s16), then the MPEG frames. The frames of all blocks run on as one stream.</summary>
    static Pcm16 Mp3(IReadOnlyList<byte[]> blocks)
    {
        var ms = new MemoryStream();
        foreach (var b in blocks) if (b.Length > 4) ms.Write(b, 4, b.Length - 4);
        return MpegDecoder.DecodeStream(ms.ToArray());
    }

    sealed class BitReader(byte[] data)
    {
        int bit;
        public bool AtEnd => bit >= data.Length * 8;
        public int Read(int n)
        {
            int v = 0;
            for (int i = 0; i < n; i++, bit++)
                v = v << 1 | (bit < data.Length * 8 ? data[bit >> 3] >> (7 - (bit & 7)) & 1 : 0);
            return v;
        }
    }
}
