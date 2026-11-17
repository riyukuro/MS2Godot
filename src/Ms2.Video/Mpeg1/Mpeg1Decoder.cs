/*
PL_MPEG - MPEG1 Video decoder, MP2 Audio decoder, MPEG-PS demuxer
SPDX-License-Identifier: MIT

Dominic Szablewski - https://phoboslab.org
*/
// C# port of pl_mpeg's plm_video (MPEG-1 video elementary stream decoder). The structure, names and arithmetic follow
// the C source; the buffer is the whole stream in memory.

namespace Ms2.Video.Mpeg1;

/// <summary>
/// Decodes an MPEG-1 video elementary stream (sequence header 00 00 01 B3) into 4:2:0 YUV frames, in display order.
/// The returned frame is owned by the decoder and is overwritten by later calls: copy or upload it before the next one.
/// </summary>
public sealed class Mpeg1Decoder
{
    const int PictureTypeIntra = 1, PictureTypePredictive = 2, PictureTypeB = 3;
    const int StartSequence = 0xB3, StartSliceFirst = 0x01, StartSliceLast = 0xAF, StartPicture = 0x00;
    const int StartExtension = 0xB5, StartUserData = 0xB2;

    struct Motion
    {
        public bool FullPx, IsSet;
        public int RSize, H, V;
    }

    readonly BitBuffer buffer;
    int startCode = -1, pictureType;
    bool hasSequenceHeader, hasReferenceFrame;
    int framesDecoded;
    double time;

    int mbWidth, mbHeight, mbSize, lumaWidth, lumaHeight, chromaWidth, chromaHeight;
    Motion motionForward, motionBackward;
    int quantizerScale, macroblockAddress, mbRow, mbCol, macroblockType;
    bool sliceBegin, macroblockIntra;
    readonly int[] dcPredictor = new int[3];
    readonly int[] blockData = new int[64];
    readonly byte[] intraQuantMatrix = new byte[64], nonIntraQuantMatrix = new byte[64];
    YuvFrame frameCurrent, frameForward, frameBackward;
    int dcPrecision;

    void ResetDcPredictors() => dcPredictor[0] = dcPredictor[1] = dcPredictor[2] = 128 << dcPrecision;

    /// <param name="stream">the elementary stream</param>
    /// <param name="length">bytes of <paramref name="stream"/> in use (default: all)</param>
    public Mpeg1Decoder(byte[] stream, int length = -1)
    {
        buffer = new BitBuffer(stream, length < 0 ? stream.Length : length);
    }

    public int Width { get; private set; }
    public int Height { get; private set; }
    /// <summary>Frames per second from the sequence header (0 for an unknown rate code).</summary>
    public double FrameRate { get; private set; }
    public double PixelAspectRatio { get; private set; } = 1;
    /// <summary>Frames returned so far.</summary>
    public int FramesDecoded => framesDecoded;
    /// <summary>The presentation time of the next frame, in seconds.</summary>
    public double Time => time;
    /// <summary>Decode returned null: no more frames until <see cref="Rewind"/>.</summary>
    public bool HasEnded { get; private set; }

    /// <summary>
    /// Intra DC precision in bits (8-11). MPEG-1 has 8; TMPGEnc streams (CRI Sofdec movies) code more and say so in
    /// their user data ("IDCPREC", read automatically) and in the USM header (mpeg_dcprec).
    /// </summary>
    public int IntraDcPrecision
    {
        get => 8 + dcPrecision;
        set => dcPrecision = Math.Clamp(value - 8, 0, 3);
    }

    /// <summary>Reads the sequence header if it was not read yet; false if the stream has none.</summary>
    public bool HasHeader()
    {
        if (hasSequenceHeader) return true;
        if (startCode != StartSequence) startCode = buffer.FindStartCode(StartSequence);
        if (startCode == -1) return false;
        return DecodeSequenceHeader();
    }

    /// <summary>Back to the first frame.</summary>
    public void Rewind()
    {
        buffer.BitIndex = 0;
        startCode = -1;
        hasSequenceHeader = false;
        hasReferenceFrame = false;
        framesDecoded = 0;
        time = 0;
        pictureType = 0;
        HasEnded = false;
    }

    /// <summary>The next frame in display order, or null at the end of the stream.</summary>
    public YuvFrame Decode()
    {
        if (!HasHeader()) { HasEnded = true; return null; }
        YuvFrame frame = null;
        do
        {
            if (startCode != StartPicture)
            {
                startCode = FindPicture();
                if (startCode == -1)
                {
                    // the last reference picture is still held back for B-frame reordering (pl_mpeg only returns it
                    // when the stream ends on a reference picture; after trailing B pictures it is still unshown)
                    if (hasReferenceFrame)
                    {
                        hasReferenceFrame = false;
                        frame = frameBackward;
                        break;
                    }
                    HasEnded = true;
                    return null;
                }
            }
            DecodePicture();
            if (pictureType == PictureTypeB) frame = frameCurrent;
            else if (hasReferenceFrame) frame = frameForward;
            else hasReferenceFrame = true;
        } while (frame == null);

        frame.Time = time;
        framesDecoded++;
        time = FrameRate > 0 ? framesDecoded / FrameRate : 0;
        return frame;
    }

    /// <summary>The next picture start code (-1 at the end), reading user data on the way.</summary>
    int FindPicture()
    {
        while (true)
        {
            int code = buffer.NextStartCode();
            if (code == StartUserData) ReadUserData();
            if (code == StartPicture || code == -1) return code;
        }
    }

    /// <summary>TMPGEnc's user data: "TMPGEXS", then tagged values ("IDCPREC" + 8 hex digits length + value).</summary>
    void ReadUserData()
    {
        var data = buffer.Bytes;
        int start = (int)(buffer.BitIndex >> 3), end = start;
        while (end + 3 <= buffer.Length && !(data[end] == 0 && data[end + 1] == 0 && data[end + 2] == 1)) end++;
        var tag = "IDCPREC\0"u8;
        int at = data.AsSpan(start, end - start).IndexOf(tag);
        if (at < 0) return;
        int p = start + at + tag.Length + 8;   // skip the length field
        if (p + 8 > end) return;
        if (int.TryParse(System.Text.Encoding.ASCII.GetString(data, p, 8), System.Globalization.NumberStyles.HexNumber, null, out int prec)
            && prec >= 0 && prec <= 3)
            dcPrecision = prec;
    }

    bool DecodeSequenceHeader()
    {
        const int maxHeaderSize = 64 + 2 * 64 * 8;
        if (!buffer.Has(maxHeaderSize)) return false;

        Width = buffer.Read(12);
        Height = buffer.Read(12);
        if (Width <= 0 || Height <= 0) return false;

        int par = Math.Clamp(buffer.Read(4) - 1, 0, Tables.PixelAspectRatio.Length - 1);
        PixelAspectRatio = Tables.PixelAspectRatio[par];
        FrameRate = Tables.PictureRate[buffer.Read(4)];

        // bit_rate, marker, buffer_size and constrained bit
        buffer.Skip(18 + 1 + 10 + 1);

        if (buffer.Read(1) != 0)
            for (int i = 0; i < 64; i++) intraQuantMatrix[Tables.ZigZag[i]] = (byte)buffer.Read(8);
        else
            Array.Copy(Tables.IntraQuantMatrix, intraQuantMatrix, 64);
        if (buffer.Read(1) != 0)
            for (int i = 0; i < 64; i++) nonIntraQuantMatrix[Tables.ZigZag[i]] = (byte)buffer.Read(8);
        else
            Array.Fill(nonIntraQuantMatrix, (byte)16);

        mbWidth = (Width + 15) >> 4;
        mbHeight = (Height + 15) >> 4;
        mbSize = mbWidth * mbHeight;
        lumaWidth = mbWidth << 4;
        lumaHeight = mbHeight << 4;
        chromaWidth = mbWidth << 3;
        chromaHeight = mbHeight << 3;

        if (frameCurrent == null || frameCurrent.LumaStride != lumaWidth || frameCurrent.Y.Length != lumaWidth * lumaHeight
            || frameCurrent.Width != Width || frameCurrent.Height != Height)
        {
            frameCurrent = NewFrame();
            frameForward = NewFrame();
            frameBackward = NewFrame();
        }
        hasSequenceHeader = true;
        return true;
    }

    YuvFrame NewFrame() => new(Width, Height, lumaWidth, lumaHeight, chromaWidth, chromaHeight);

    void DecodePicture()
    {
        buffer.Skip(10);                     // temporal_reference
        pictureType = buffer.Read(3);
        buffer.Skip(16);                     // vbv_delay

        // D frames or unknown coding type
        if (pictureType <= 0 || pictureType > PictureTypeB) return;

        if (pictureType == PictureTypePredictive || pictureType == PictureTypeB)
        {
            motionForward.FullPx = buffer.Read(1) != 0;
            int fCode = buffer.Read(3);
            if (fCode == 0) return;          // ignore pictures with a zero f_code
            motionForward.RSize = fCode - 1;
        }
        if (pictureType == PictureTypeB)
        {
            motionBackward.FullPx = buffer.Read(1) != 0;
            int fCode = buffer.Read(3);
            if (fCode == 0) return;
            motionBackward.RSize = fCode - 1;
        }

        var frameTemp = frameForward;
        if (pictureType == PictureTypeIntra || pictureType == PictureTypePredictive)
            frameForward = frameBackward;

        // first slice start code; skip extension and user data
        do startCode = buffer.NextStartCode();
        while (startCode == StartExtension || startCode == StartUserData);

        while (startCode >= StartSliceFirst && startCode <= StartSliceLast)
        {
            DecodeSlice(startCode & 0xff);
            if (macroblockAddress >= mbSize - 1) break;
            startCode = buffer.NextStartCode();
        }

        // a reference picture rotates the prediction frames
        if (pictureType == PictureTypeIntra || pictureType == PictureTypePredictive)
        {
            frameBackward = frameCurrent;
            frameCurrent = frameTemp;
        }
    }

    void DecodeSlice(int slice)
    {
        sliceBegin = true;
        macroblockAddress = (slice - 1) * mbWidth - 1;

        motionBackward.H = motionForward.H = 0;
        motionBackward.V = motionForward.V = 0;
        ResetDcPredictors();

        quantizerScale = buffer.Read(5);
        while (buffer.Read(1) != 0) buffer.Skip(8);   // extra slice information

        do DecodeMacroblock();
        while (macroblockAddress < mbSize - 1 && buffer.PeekNonZero(23));
    }

    void DecodeMacroblock()
    {
        int increment = 0;
        int t = buffer.ReadVlc(Tables.MacroblockAddressIncrement);
        while (t == 34) t = buffer.ReadVlc(Tables.MacroblockAddressIncrement);   // macroblock_stuffing
        while (t == 35)
        {
            increment += 33;                                                     // macroblock_escape
            t = buffer.ReadVlc(Tables.MacroblockAddressIncrement);
        }
        increment += t;

        if (sliceBegin)
        {
            // the first increment of a slice is relative to the start of the previous row
            sliceBegin = false;
            macroblockAddress += increment;
        }
        else
        {
            if (macroblockAddress + increment >= mbSize) return;   // invalid
            if (increment > 1)
            {
                // skipped macroblocks reset the DC predictors, and the motion vectors in P pictures
                ResetDcPredictors();
                if (pictureType == PictureTypePredictive) { motionForward.H = 0; motionForward.V = 0; }
            }
            while (increment > 1)
            {
                macroblockAddress++;
                mbRow = macroblockAddress / mbWidth;
                mbCol = macroblockAddress % mbWidth;
                PredictMacroblock();
                increment--;
            }
            macroblockAddress++;
        }

        mbRow = macroblockAddress / mbWidth;
        mbCol = macroblockAddress % mbWidth;
        if (mbCol >= mbWidth || mbRow >= mbHeight) return;   // corrupt stream

        macroblockType = buffer.ReadVlc(Tables.MacroblockType[pictureType]);
        macroblockIntra = (macroblockType & 0x01) != 0;
        motionForward.IsSet = (macroblockType & 0x08) != 0;
        motionBackward.IsSet = (macroblockType & 0x04) != 0;

        if ((macroblockType & 0x10) != 0) quantizerScale = buffer.Read(5);

        if (macroblockIntra)
        {
            motionBackward.H = motionForward.H = 0;
            motionBackward.V = motionForward.V = 0;
        }
        else
        {
            ResetDcPredictors();
            DecodeMotionVectors();
            PredictMacroblock();
        }

        int cbp = (macroblockType & 0x02) != 0
            ? buffer.ReadVlc(Tables.CodeBlockPattern)
            : (macroblockIntra ? 0x3f : 0);
        for (int block = 0, mask = 0x20; block < 6; block++, mask >>= 1)
            if ((cbp & mask) != 0) DecodeBlock(block);
    }

    void DecodeMotionVectors()
    {
        if (motionForward.IsSet)
        {
            int r = motionForward.RSize;
            motionForward.H = DecodeMotionVector(r, motionForward.H);
            motionForward.V = DecodeMotionVector(r, motionForward.V);
        }
        else if (pictureType == PictureTypePredictive)
        {
            motionForward.H = 0;
            motionForward.V = 0;
        }
        if (motionBackward.IsSet)
        {
            int r = motionBackward.RSize;
            motionBackward.H = DecodeMotionVector(r, motionBackward.H);
            motionBackward.V = DecodeMotionVector(r, motionBackward.V);
        }
    }

    int DecodeMotionVector(int rSize, int motion)
    {
        int fscale = 1 << rSize;
        int mCode = buffer.ReadVlc(Tables.Motion);
        int d;
        if (mCode != 0 && fscale != 1)
        {
            int r = buffer.Read(rSize);
            d = ((Math.Abs(mCode) - 1) << rSize) + r + 1;
            if (mCode < 0) d = -d;
        }
        else d = mCode;

        motion += d;
        if (motion > (fscale << 4) - 1) motion -= fscale << 5;
        else if (motion < (int)((uint)(-fscale) << 4)) motion += fscale << 5;
        return motion;
    }

    void PredictMacroblock()
    {
        int fwH = motionForward.H, fwV = motionForward.V;
        if (motionForward.FullPx) { fwH <<= 1; fwV <<= 1; }

        if (pictureType == PictureTypeB)
        {
            int bwH = motionBackward.H, bwV = motionBackward.V;
            if (motionBackward.FullPx) { bwH <<= 1; bwV <<= 1; }
            if (motionForward.IsSet)
            {
                CopyMacroblock(frameForward, fwH, fwV, false);
                if (motionBackward.IsSet) CopyMacroblock(frameBackward, bwH, bwV, true);
            }
            else CopyMacroblock(frameBackward, bwH, bwV, false);
        }
        else CopyMacroblock(frameForward, fwH, fwV, false);
    }

    /// <summary>plm_video_copy_macroblock / plm_video_interpolate_macroblock.</summary>
    void CopyMacroblock(YuvFrame s, int motionH, int motionV, bool interpolate)
    {
        var d = frameCurrent;
        ProcessMacroblock(s.Y, d.Y, motionH, motionV, 16, interpolate);
        ProcessMacroblock(s.Cr, d.Cr, motionH / 2, motionV / 2, 8, interpolate);
        ProcessMacroblock(s.Cb, d.Cb, motionH / 2, motionV / 2, 8, interpolate);
    }

    void ProcessMacroblock(byte[] s, byte[] d, int motionH, int motionV, int blockSize, bool interpolate)
    {
        int dw = mbWidth * blockSize;
        int hp = motionH >> 1, vp = motionV >> 1;
        bool oddH = (motionH & 1) == 1, oddV = (motionV & 1) == 1;

        uint si = (uint)(((mbRow * blockSize) + vp) * dw + (mbCol * blockSize) + hp);
        uint di = (uint)((mbRow * dw + mbCol) * blockSize);
        uint maxAddress = (uint)(dw * (mbHeight * blockSize - blockSize + 1) - blockSize);
        if (si > maxAddress || di > maxAddress) return;   // corrupt video

        int scan = dw - blockSize;
        int sIdx = (int)si, dIdx = (int)di;
        int mode = (interpolate ? 4 : 0) | (oddH ? 2 : 0) | (oddV ? 1 : 0);
        // half-pel reads on the last row can pass the plane's end (the C source reads into the next plane there);
        // such a block reads the plane's last byte instead
        int last = s.Length - 1;
        bool edge = sIdx + (blockSize - 1 + (oddV ? 1 : 0)) * dw + blockSize - 1 + (oddH ? 1 : 0) > last;
        int dn = dw, rt = 1;
        for (int y = 0; y < blockSize; y++)
        {
            if (edge)
            {
                dn = sIdx + blockSize + dw <= last ? dw : 0;   // the row below, or this row again past the end
                rt = 1;
            }
            for (int x = 0; x < blockSize; x++)
            {
                if (edge) rt = sIdx + dn + 1 <= last ? 1 : 0;
                int v = mode switch
                {
                    0 => s[sIdx],
                    1 => (s[sIdx] + s[sIdx + dn] + 1) >> 1,
                    2 => (s[sIdx] + s[sIdx + rt] + 1) >> 1,
                    3 => (s[sIdx] + s[sIdx + rt] + s[sIdx + dn] + s[sIdx + dn + rt] + 2) >> 2,
                    4 => (d[dIdx] + s[sIdx] + 1) >> 1,
                    5 => (d[dIdx] + ((s[sIdx] + s[sIdx + dn] + 1) >> 1) + 1) >> 1,
                    6 => (d[dIdx] + ((s[sIdx] + s[sIdx + rt] + 1) >> 1) + 1) >> 1,
                    _ => (d[dIdx] + ((s[sIdx] + s[sIdx + rt] + s[sIdx + dn] + s[sIdx + dn + rt] + 2) >> 2) + 1) >> 1,
                };
                d[dIdx] = (byte)v;
                sIdx++; dIdx++;
            }
            sIdx += scan;
            dIdx += scan;
        }
    }

    void DecodeBlock(int block)
    {
        int n = 0;
        byte[] quantMatrix;

        if (macroblockIntra)
        {
            // DC prediction
            int planeIndex = block > 3 ? block - 3 : 0;
            int predictor = dcPredictor[planeIndex];
            int dctSize = buffer.ReadVlc(Tables.DctSize[planeIndex]);
            if (dctSize > 0)
            {
                int differential = buffer.Read(dctSize);
                blockData[0] = (differential & (1 << (dctSize - 1))) != 0
                    ? predictor + differential
                    : predictor + (-(1 << dctSize) | (differential + 1));
            }
            else blockData[0] = predictor;
            dcPredictor[planeIndex] = blockData[0];

            // dequantize + premultiply
            blockData[0] <<= 3 - dcPrecision + 5;
            quantMatrix = intraQuantMatrix;
            n = 1;
        }
        else quantMatrix = nonIntraQuantMatrix;

        // AC coefficients (and DC for non-intra blocks)
        while (true)
        {
            int run, level;
            int coeff = buffer.ReadVlc(Tables.DctCoeff) & 0xffff;
            if (coeff == 0x0001 && n > 0 && buffer.Read(1) == 0) break;   // end_of_block
            if (coeff == 0xffff)
            {
                // escape
                run = buffer.Read(6);
                level = buffer.Read(8);
                if (level == 0) level = buffer.Read(8);
                else if (level == 128) level = buffer.Read(8) - 256;
                else if (level > 128) level -= 256;
            }
            else
            {
                run = coeff >> 8;
                level = coeff & 0xff;
                if (buffer.Read(1) != 0) level = -level;
            }

            n += run;
            if (n < 0 || n >= 64) return;   // invalid

            int deZigZagged = Tables.ZigZag[n];
            n++;

            // dequantize, oddify, clip
            level = (int)((uint)level << 1);
            if (!macroblockIntra) level += level < 0 ? -1 : 1;
            level = (level * quantizerScale * quantMatrix[deZigZagged]) >> 4;
            if ((level & 1) == 0) level -= level > 0 ? 1 : -1;
            if (level > 2047) level = 2047;
            else if (level < -2048) level = -2048;

            blockData[deZigZagged] = level * Tables.PremultiplierMatrix[deZigZagged];
        }

        // move the block to its place
        byte[] d;
        int dw, di;
        if (block < 4)
        {
            d = frameCurrent.Y;
            dw = lumaWidth;
            di = (mbRow * lumaWidth + mbCol) << 4;
            if ((block & 1) != 0) di += 8;
            if ((block & 2) != 0) di += lumaWidth << 3;
        }
        else
        {
            d = block == 4 ? frameCurrent.Cb : frameCurrent.Cr;
            dw = chromaWidth;
            di = ((mbRow * lumaWidth) << 2) + (mbCol << 3);
        }

        var s = blockData;
        int scan = dw - 8, si = 0;
        if (n == 1)
        {
            // DC only
            int value = (s[0] + 128) >> 8;
            for (int y = 0; y < 8; y++, di += scan)
                for (int x = 0; x < 8; x++, di++)
                    d[di] = Clamp(macroblockIntra ? value : d[di] + value);
            s[0] = 0;
        }
        else
        {
            Idct(s);
            for (int y = 0; y < 8; y++, di += scan)
                for (int x = 0; x < 8; x++, di++, si++)
                    d[di] = Clamp(macroblockIntra ? s[si] : d[di] + s[si]);
            Array.Clear(blockData);
        }
    }

    static byte Clamp(int n) => n > 255 ? (byte)255 : n < 0 ? (byte)0 : (byte)n;

    static void Idct(int[] block)
    {
        int b1, b3, b4, b6, b7, tmp1, tmp2, m0, x0, x1, x2, x3, x4, y3, y4, y5, y6, y7;

        // columns
        for (int i = 0; i < 8; ++i)
        {
            b1 = block[4 * 8 + i];
            b3 = block[2 * 8 + i] + block[6 * 8 + i];
            b4 = block[5 * 8 + i] - block[3 * 8 + i];
            tmp1 = block[1 * 8 + i] + block[7 * 8 + i];
            tmp2 = block[3 * 8 + i] + block[5 * 8 + i];
            b6 = block[1 * 8 + i] - block[7 * 8 + i];
            b7 = tmp1 + tmp2;
            m0 = block[0 * 8 + i];
            x4 = ((b6 * 473 - b4 * 196 + 128) >> 8) - b7;
            x0 = x4 - (((tmp1 - tmp2) * 362 + 128) >> 8);
            x1 = m0 - b1;
            x2 = (((block[2 * 8 + i] - block[6 * 8 + i]) * 362 + 128) >> 8) - b3;
            x3 = m0 + b1;
            y3 = x1 + x2;
            y4 = x3 + b3;
            y5 = x1 - x2;
            y6 = x3 - b3;
            y7 = -x0 - ((b4 * 473 + b6 * 196 + 128) >> 8);
            block[0 * 8 + i] = b7 + y4;
            block[1 * 8 + i] = x4 + y3;
            block[2 * 8 + i] = y5 - x0;
            block[3 * 8 + i] = y6 - y7;
            block[4 * 8 + i] = y6 + y7;
            block[5 * 8 + i] = x0 + y5;
            block[6 * 8 + i] = y3 - x4;
            block[7 * 8 + i] = y4 - b7;
        }

        // rows
        for (int i = 0; i < 64; i += 8)
        {
            b1 = block[4 + i];
            b3 = block[2 + i] + block[6 + i];
            b4 = block[5 + i] - block[3 + i];
            tmp1 = block[1 + i] + block[7 + i];
            tmp2 = block[3 + i] + block[5 + i];
            b6 = block[1 + i] - block[7 + i];
            b7 = tmp1 + tmp2;
            m0 = block[0 + i];
            x4 = ((b6 * 473 - b4 * 196 + 128) >> 8) - b7;
            x0 = x4 - (((tmp1 - tmp2) * 362 + 128) >> 8);
            x1 = m0 - b1;
            x2 = (((block[2 + i] - block[6 + i]) * 362 + 128) >> 8) - b3;
            x3 = m0 + b1;
            y3 = x1 + x2;
            y4 = x3 + b3;
            y5 = x1 - x2;
            y6 = x3 - b3;
            y7 = -x0 - ((b4 * 473 + b6 * 196 + 128) >> 8);
            block[0 + i] = (b7 + y4 + 128) >> 8;
            block[1 + i] = (x4 + y3 + 128) >> 8;
            block[2 + i] = (y5 - x0 + 128) >> 8;
            block[3 + i] = (y6 - y7 + 128) >> 8;
            block[4 + i] = (y6 + y7 + 128) >> 8;
            block[5 + i] = (x0 + y5 + 128) >> 8;
            block[6 + i] = (y3 - x4 + 128) >> 8;
            block[7 + i] = (y4 - b7 + 128) >> 8;
        }
    }
}
