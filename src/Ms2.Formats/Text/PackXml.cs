using System.Text;
using Ms2.Formats.Pack;
using System.Xml.Linq;

namespace Ms2.Formats.Text;

/// <summary>
/// The one reader for an XML entry of a pack (a table in Xml.m2d, a model's companion .xml). The client reads these through
/// TinyXML (TiXmlDocument::Parse, TIXML_ENCODING_UNKNOWN): it skips a leading UTF-8 byte-order mark, takes the bytes as UTF-8
/// whatever an encoding declaration says, and has no UTF-16 reader. The same here, so a file the client loads loads here:
/// a UTF-8 mark (KMS2_DEV Character/male/male.xml and female/female.xml have one) is dropped, not left for XDocument.Parse to reject with "Data at
/// the root level is invalid. Line 1, position 1". A UTF-16 mark is decoded as UTF-16 (the client would fail that file; we
/// read it rather than stop a field).
/// </summary>
public static class PackXml
{
    /// <summary>The entry's text with any byte-order mark removed.</summary>
    public static string Text(byte[] data)
    {
        int n = data.Length;
        if (n >= 2 && data[0] == 0xFF && data[1] == 0xFE) return Encoding.Unicode.GetString(data, 2, n - 2).TrimStart('﻿');
        if (n >= 2 && data[0] == 0xFE && data[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(data, 2, n - 2).TrimStart('﻿');
        int start = 0;
        while (start + 2 < n && data[start] == 0xEF && data[start + 1] == 0xBB && data[start + 2] == 0xBF) start += 3;
        return Encoding.UTF8.GetString(data, start, n - start).TrimStart('﻿');
    }

    /// <summary>A reader over the entry's text as <see cref="Text"/> decodes it, without the whole text as one string (a
    /// forward-only reader of a large table): <paramref name="data"/> is read as the reader goes and disposed with it.</summary>
    public static TextReader Reader(Stream data)
    {
        // a UTF-8 mark decodes to U+FEFF, so dropping the leading marks and then Text's TrimStart is one TrimStart of the chars
        var head = new byte[2];
        int got = 0, n;
        while (got < 2 && (n = data.Read(head, got, 2 - got)) > 0) got += n;
        TextReader r;
        if (got == 2 && head[0] == 0xFF && head[1] == 0xFE) r = new StreamReader(data, new UnicodeEncoding(false, false), false, 4096);
        else if (got == 2 && head[0] == 0xFE && head[1] == 0xFF) r = new StreamReader(data, new UnicodeEncoding(true, false), false, 4096);
        else r = new StreamReader(new HeadStream(head, got, data), new UTF8Encoding(false), false, 4096);
        while (r.Peek() == 0xFEFF) r.Read();
        return r;
    }

    /// <summary>The bytes already taken from the front of a stream, then the rest of it; disposing it disposes the stream.</summary>
    sealed class HeadStream : Stream
    {
        readonly byte[] head;
        int at;
        readonly int count;
        readonly Stream rest;
        public HeadStream(byte[] head, int count, Stream rest) { this.head = head; this.count = count; this.rest = rest; }
        public override int Read(byte[] buffer, int offset, int length) => Read(buffer.AsSpan(offset, length));
        public override int Read(Span<byte> buffer)
        {
            if (at >= count || buffer.Length == 0) return rest.Read(buffer);
            int n = Math.Min(count - at, buffer.Length);
            head.AsSpan(at, n).CopyTo(buffer);
            at += n;
            return n;
        }
        protected override void Dispose(bool disposing) { if (disposing) rest.Dispose(); base.Dispose(disposing); }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int length) => throw new NotSupportedException();
    }

    public static XDocument Parse(byte[] data) => XDocument.Parse(Text(data));

    public static XDocument Read(PackSet packs, string archive, string entry) => Parse(packs.Read(archive, entry));
}
