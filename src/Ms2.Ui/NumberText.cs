using System.Globalization;
using System.Text;
using Ms2.Quest;

namespace Ms2.Ui;

/// <summary>
/// The client's number-text helpers behind the UI callbacks GetMesoText (uiservice.cpp:3147, FUN_140e70010) and
/// GetNumericText (uiservice.cpp:3166, FUN_14083ccf0). Paths are relative to the main repo src/ms2/.
///
/// The separators depend on the client language id DAT_143ab0240 (client/ms2: "client language", read by GetLanguage(),
/// commonfiles/localepath.cpp:388; set by SetLanguage(), localepath.cpp:578-580). Language ids: 0 KR, 1 JP, 2 EN,
/// 3 CN, 4 FR, 5 DE, 6 PT, 7 ES (SetLanguage switch localepath.cpp:581-589; GetLanguageId localepath.cpp:441-451).
/// SetLanguage is called with CGameOption's languageIndex (client/ms2/gameoption.cpp:1846); that index is read from
/// GameOption.xml language/languageIndex (gameoptionstruct.cpp:2496) and defaults to the service region
/// (Unk_LgState ctor, gameoptionstruct.cpp:2462-2487: region KR 0, JP 1, NA 2, CN 3). For the NA region on a fresh option
/// file it is derived from the user UI language (gameoption.cpp:1841-1844; GetLanguageFromLangId localepath.cpp:433-441:
/// 0x407 -> 5 DE, 0x416 -> 6 PT, anything else -> 2 EN). The restored English client therefore has language 2 (or 0
/// with the KR locale); for both the separators are "," thousands and "." decimal, so <see cref="DefaultLanguage"/> is 2.
/// </summary>
public static class NumberText
{
    /// <summary>Language id of the restored English client (see the class comment); 0 (KR) gives identical text.</summary>
    public const int DefaultLanguage = 2;

    /// <summary>GetThousandsSeparator (unit_14083bab0_localizedtexthelpers.cpp:434): 5,6 ".", 4 " ", else ",".</summary>
    public static string ThousandsSeparator(int lang) => lang switch { 5 or 6 => ".", 4 => " ", _ => "," };

    /// <summary>GetDecimalSeparator (unit_14083bab0_localizedtexthelpers.cpp:450): 5,6 ",", else ".".</summary>
    public static string DecimalSeparator(int lang) => lang is 5 or 6 ? "," : ".";

    /// <summary>
    /// FUN_140e70010 (client/ms2/uipaybackdialog.cpp:269) = FUN_14083cb20(FUN_140719900(str)); FUN_140719900 returns its
    /// argument unchanged (unit_1407186f0_stringconversionhelpers.cpp:190), so this is <see cref="Group"/>.
    /// </summary>
    public static string MesoText(string s, int lang = DefaultLanguage) => Group(s ?? "", lang);

    /// <summary>
    /// FUN_14083cb20 (unit_14083bab0_localizedtexthelpers.cpp:241-258): inserts the thousands separator into a number text.
    /// Quirk kept: a negative number whose digits are a multiple of 3 ("-123") gets a separator right after the sign.
    /// </summary>
    public static string Group(string str, int lang = DefaultLanguage)
    {
        int nLen = str.Length;                                        // :243
        int nInt = str.IndexOf('.');                                  // :244 str.Find(L".", 0)
        nInt = nInt >= 0 ? nInt : nLen;                               // :245
        if (nInt <= 3 && nLen == nInt) return str;                    // :246-247
        var sb = new StringBuilder(str);                              // :248
        if (nInt < nLen) sb[nInt] = DecimalSeparator(lang)[0];        // :249-250 SetAt(nInt, decimal separator[0])
        string sep = ThousandsSeparator(lang);                        // :252
        for (int nPos = nInt - 3; nPos > 0; nPos -= 3)                // :253 every 3 chars left of the point
            sb.Insert(nPos, sep);                                     // :254 CStringW::Insert(nPos, sep)
        return sb.ToString();
    }

    /// <summary>
    /// FUN_14083ccf0 (unit_14083bab0_localizedtexthelpers.cpp:260-292, via GetNumericText uiservice.cpp:3166-3181): keeps
    /// the digits, a leading '-' and the language's decimal separator of the text, then re-renders the number: an integer
    /// via FUN_140719a00 ("%I64d" of ParseInt64W), a fractional text via StringFormat("{0}", ParseDoubleW) (fmt default
    /// double = "%g"). No thousands grouping is applied.
    /// </summary>
    public static string NumericText(string str, int lang = DefaultLanguage)
    {
        str ??= "";
        var digits = new StringBuilder();                             // :262 strDigits
        string sep = DecimalSeparator(lang);                          // :263
        bool fraction = false;                                        // :264
        for (int i = 0; i < str.Length; i++)                          // :265
        {
            sbyte ch = unchecked((sbyte)(str[i] & 0xFF));             // :267 (char)GetAt(i): low byte, signed
            if (ch < '0' || '9' < ch)                                 // :268
            {
                if (ch == '-' && i == 0) digits.Append((char)ch);     // :270-271
                else if (sep.Length == 1 && sep[0] == (char)(ushort)(short)ch)   // :272 IsSingleChar(sep, ch), ch sign-extended
                {
                    digits.Append((char)ch);                          // :274
                    fraction = true;                                  // :275
                }
                continue;                                             // :277
            }
            digits.Append((char)ch);                                  // :279
        }
        if (!fraction) return ParseInt64W(digits.ToString()).ToString(CultureInfo.InvariantCulture);  // :284 FUN_140719a00 "%I64d"
        return ClientFormat.Format("{0}", ParseDoubleW(digits.ToString()));                           // :288-289
    }

    /// <summary>
    /// ParseInt64W (commonfiles/packetlogger.cpp:171) = _wtoi64: optional blanks, sign, digits up to the first other
    /// character; 64-bit wrap on overflow; 0 when no digits.
    /// </summary>
    public static long ParseInt64W(string s)
    {
        int i = 0;
        while (i < s.Length && (s[i] == ' ' || (s[i] >= '\t' && s[i] <= '\r'))) i++;
        bool neg = false;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) neg = s[i++] == '-';
        ulong v = 0;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9') v = unchecked(v * 10 + (ulong)(s[i++] - '0'));
        return unchecked(neg ? -(long)v : (long)v);
    }

    /// <summary>
    /// ParseDoubleW (commonfiles/packetlogger.cpp:195) = _wtof: longest "[-]digits[.digits]" prefix (the C locale decimal
    /// point is '.', so a ',' separator of languages 5/6 ends the number); 0 when none.
    /// </summary>
    public static double ParseDoubleW(string s)
    {
        int i = 0;
        while (i < s.Length && (s[i] == ' ' || (s[i] >= '\t' && s[i] <= '\r'))) i++;
        int start = i;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
        int d0 = i;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
        int nd = i - d0;
        if (i < s.Length && s[i] == '.')
        {
            int j = i + 1;
            while (j < s.Length && s[j] >= '0' && s[j] <= '9') j++;
            nd += j - i - 1;
            i = j;
        }
        if (nd == 0) return 0;
        return double.Parse(s.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
