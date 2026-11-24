using System.Text;

namespace Ms2.Formats.Text;

/// <summary>
/// The bag search's text form (FUN_1413fa9b0, unit_1413f9810.cpp:522): the text to code page 1361 (Johab) bytes
/// (FUN_1413fb400: CW2A, WideCharToMultiByte with no flags), each double byte character split into its initial, medial and
/// final jamo codes through the Johab tables (FUN_1413fb0f0 with GetJohabJamoCode: the three 5 bit fields, a zero code
/// dropped), and back to text (FUN_1413fb330). The Johab codes are the standard's (KS X 1001 annex 3): a jamo code with
/// fill fields is the compatibility letter (0x8841 is U+3131). So a Hangul syllable becomes its compatibility jamo
/// (U+AC00 is U+3131 U+314F), a modern compatibility letter stays itself, and a KS X 1001 symbol or Hanja (its Johab code
/// from its row and cell) is split by the same bit fields. ASCII stays. The double byte test is IsDBCSLeadByte of the
/// system code page: the Korean client's (949, every Johab lead byte leads) is the one taken here.
/// STILL-OPEN: a character Johab lacks is '?' here; Windows' best-fit table for 1361 may give a near letter instead.
/// Then <see cref="Lower"/> is CStringW::MakeLower in the C locale the client never changes (no setlocale): only A-Z.
/// </summary>
public static class JamoSearchText
{
    // the compatibility letters of the Johab table codes below, in table order (initial 19, medial 21, final 27)
    const string Initials = "\u3131\u3132\u3134\u3137\u3138\u3139\u3141\u3142\u3143\u3145\u3146\u3147\u3148\u3149\u314a\u314b\u314c\u314d\u314e";
    const string Medials = "\u314f\u3150\u3151\u3152\u3153\u3154\u3155\u3156\u3157\u3158\u3159\u315a\u315b\u315c\u315d\u315e\u315f\u3160\u3161\u3162\u3163";
    const string Finals = "\u3131\u3132\u3133\u3134\u3135\u3136\u3137\u3139\u313a\u313b\u313c\u313d\u313e\u313f\u3140\u3141\u3142\u3144\u3145\u3146\u3147\u3148\u314a\u314b\u314c\u314d\u314e";

    static readonly ushort[] Initial = {
        0x0000, 0x0000, 0x8841, 0x8c41, 0x9041, 0x9441, 0x9841, 0x9c41, 0xa041, 0xa441, 0xa841, 0xac41, 0xb041, 0xb441, 0xb841, 0xbc41,
        0xc041, 0xc441, 0xc841, 0xcc41, 0xd041, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000 };
    static readonly ushort[] Medial = {
        0x0000, 0x0000, 0x0000, 0x8461, 0x8481, 0x84a1, 0x84c1, 0x84e1, 0x0000, 0x0000, 0x8541, 0x8561, 0x8581, 0x85a1, 0x85c1, 0x85e1,
        0x0000, 0x0000, 0x8641, 0x8661, 0x8681, 0x86a1, 0x86c1, 0x86e1, 0x0000, 0x0000, 0x8741, 0x8761, 0x8781, 0x87a1, 0x0000, 0x0000 };
    static readonly ushort[] Final = {
        0x0000, 0x0000, 0x8442, 0x8443, 0x8444, 0x8445, 0x8446, 0x8447, 0x8448, 0x8449, 0x844a, 0x844b, 0x844c, 0x844d, 0x844e, 0x844f,
        0x8450, 0x8451, 0x0000, 0x8453, 0x8454, 0x8455, 0x8456, 0x8457, 0x8458, 0x8459, 0x845a, 0x845b, 0x845c, 0x845d, 0x0000, 0x0000 };

    /// <summary>GetJohabJamoCode's tables as letters: '\0' for a zero code.</summary>
    static readonly char[] InitialLetter = Letters(Initial, Initials), MedialLetter = Letters(Medial, Medials), FinalLetter = Letters(Final, Finals);

    static char[] Letters(ushort[] table, string letters)
    {
        var a = new char[table.Length];
        for (int i = 0, n = 0; i < table.Length; i++) if (table[i] != 0) a[i] = letters[n++];
        return a;
    }

    static Encoding ksc;

    /// <summary>Code page 949, for a symbol's KS X 1001 row and cell.</summary>
    static Encoding Ksc
    {
        get
        {
            if (ksc != null) return ksc;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return ksc = Encoding.GetEncoding(949, new EncoderReplacementFallback("?"), new DecoderReplacementFallback("?"));
        }
    }

    /// <summary>FUN_1413fa9b0: the text's jamo form.</summary>
    public static string Of(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Length * 3);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c < 0x80 || (c >= 0x3131 && c <= 0x3163)) sb.Append(c);
            else if (c >= 0xac00 && c <= 0xd7a3)
            {
                int k = c - 0xac00;
                sb.Append(Initials[k / 588]).Append(Medials[k % 588 / 28]);
                if (k % 28 > 0) sb.Append(Finals[k % 28 - 1]);
            }
            else if (char.IsSurrogate(c))
            {
                sb.Append('?');      // a pair is one character to WideCharToMultiByte
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) i++;
            }
            else if (JohabSymbol(c) is int code)
            {
                Add(sb, InitialLetter[(code & 0x7c00) >> 10]);
                Add(sb, MedialLetter[(code & 0x3e0) >> 5]);
                Add(sb, FinalLetter[code & 0x1f]);
            }
            else sb.Append('?');
        }
        return sb.ToString();
    }

    static void Add(StringBuilder sb, char letter)
    {
        if (letter != '\0') sb.Append(letter);
    }

    /// <summary>The Johab code of a KS X 1001 symbol (rows 0xA1-0xAC) or Hanja (rows 0xCA-0xFD): two rows to a lead byte
    /// (0xD9-0xDE, 0xE0-0xF9), the first row's cells at 0x31-0x7E and 0x91-0xA0, the second's at 0xA1-0xFE. Null when the
    /// character is not in those rows.</summary>
    static int? JohabSymbol(char c)
    {
        Span<char> one = stackalloc char[1] { c };
        Span<byte> b = stackalloc byte[4];
        if (Ksc.GetBytes(one, b) != 2 || b[1] < 0xa1) return null;
        int row = b[0], cell = b[1], first, lead;
        if (row >= 0xa1 && row <= 0xac) { first = 0xa1; lead = 0xd9; }
        else if (row >= 0xca && row <= 0xfd) { first = 0xca; lead = 0xe0; }
        else return null;
        lead += (row - first) / 2;
        int trail = (row - first) % 2 != 0 ? cell : cell <= 0xee ? cell - 0x70 : cell - 0x5e;
        return (lead << 8) | trail;
    }

    /// <summary>CStringW::MakeLower in the C locale: A-Z only.</summary>
    public static string Lower(string s)
    {
        if (s == null) return "";
        int i = 0;
        while (i < s.Length && (s[i] < 'A' || s[i] > 'Z')) i++;
        if (i == s.Length) return s;
        var c = s.ToCharArray();
        for (; i < c.Length; i++) if (c[i] >= 'A' && c[i] <= 'Z') c[i] = (char)(c[i] + 32);
        return new string(c);
    }
}
