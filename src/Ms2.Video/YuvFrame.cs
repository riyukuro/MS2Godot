namespace Ms2.Video;

/// <summary>
/// A decoded 4:2:0 picture: a luma plane and two half-size chroma planes, each padded to whole 16x16 macroblocks
/// (stride = padded width). Only the top-left <see cref="Width"/> x <see cref="Height"/> pixels are picture.
/// </summary>
public sealed class YuvFrame
{
    public readonly int Width, Height;
    public readonly int LumaStride, LumaHeight, ChromaStride, ChromaHeight;
    public readonly byte[] Y, Cb, Cr;
    /// <summary>Presentation time in seconds (frame index / frame rate).</summary>
    public double Time;

    public YuvFrame(int width, int height, int lumaStride, int lumaHeight, int chromaStride, int chromaHeight)
    {
        Width = width; Height = height;
        LumaStride = lumaStride; LumaHeight = lumaHeight;
        ChromaStride = chromaStride; ChromaHeight = chromaHeight;
        Y = new byte[lumaStride * lumaHeight];
        Cb = new byte[chromaStride * chromaHeight];
        Cr = new byte[chromaStride * chromaHeight];
    }

    /// <summary>
    /// BT.601 studio-range YCbCr to RGBA (alpha 255), <see cref="Width"/> x <see cref="Height"/> pixels, with pl_mpeg's
    /// integer coefficients. The game converts in a shader; this is for tools and tests.
    /// </summary>
    public void ToRgba(byte[] dest, int stride)
    {
        for (int row = 0; row < Height; row++)
        {
            int c = (row >> 1) * ChromaStride, yi = row * LumaStride, d = row * stride;
            for (int col = 0; col < Width; col++)
            {
                int cr = Cr[c + (col >> 1)] - 128, cb = Cb[c + (col >> 1)] - 128;
                int r = (cr * 104597) >> 16;
                int g = (cb * 25674 + cr * 53278) >> 16;
                int b = (cb * 132201) >> 16;
                int y = ((Y[yi + col] - 16) * 76309) >> 16;
                dest[d++] = Clamp(y + r);
                dest[d++] = Clamp(y - g);
                dest[d++] = Clamp(y + b);
                dest[d++] = 255;
            }
        }
    }

    static byte Clamp(int n) => n > 255 ? (byte)255 : n < 0 ? (byte)0 : (byte)n;
}
