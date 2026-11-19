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

    /// <summary>GetDigitGroupSize (commonfiles/localepath.cpp:406): 4 digits a group for languages 0, 1 and 3, else 3.</summary>
    public static int DigitGroupSize(int lang) => lang is 0 or 1 or 3 ? 4 : 3;

    /// <summary>
    /// FUN_140b8f7a0 (unnamed/unit_140b8e760_clientdatetimehelpers.cpp:226): the digit text split from the right into the
    /// language's digit groups (at least 4 groups; the last takes the rest), each grouped (FUN_1409404f0), the common string
    /// "s_number_string_" + the mask of the non-zero groups as 4 binary digits formatted with the first three groups
    /// (FUN_14028dfa0, <paramref name="common"/> by key). A text no longer than a group is <see cref="Group"/>.
    /// </summary>
    public static string NumberString(string number, Func<string, string> common, int lang = DefaultLanguage)
    {
        number ??= "";
        int group = DigitGroupSize(lang);
        if (number.Length - group <= 0) return Group(number, lang);
        int count = Math.Max(4, number.Length / group);
        var groups = new string[count];
        string rest = number;
        int mask = 0;
        for (int i = 0; i < count; i++)
        {
            // CStringW::Right / Left clamp: Right(n) of a shorter text is the text, Left(n <= 0) is empty.
            long value = ParseInt64W(i == count - 1 ? rest : rest.Length <= group ? rest : rest[^group..]);
            if (value != 0) mask |= 1 << i;
            groups[i] = Group(value.ToString(CultureInfo.InvariantCulture), lang);
            rest = rest.Length - group <= 0 ? "" : rest[..(rest.Length - group)];
        }
        string key = "s_number_string_" + Convert.ToString(mask, 2).PadLeft(4, '0');   // StringFormat("{0:04b}")
        return ClientFormat.Format(common(key) ?? "", groups[0], groups[1], groups[2]);
    }

    /// <summary>GetNumberText (unnamed/unit_141274d10_uihelpers.cpp:447): below 10000 grouped (FUN_1409404f0), else
    /// <see cref="NumberString"/> of the "%I64d" text (FUN_141275790).</summary>
    public static string GetNumberText(long n, Func<string, string> common, int lang = DefaultLanguage) =>
        n < 10000 ? Group(n.ToString(CultureInfo.InvariantCulture), lang) : NumberString(n.ToString(CultureInfo.InvariantCulture), common, lang);

    /// <summary>FUN_141275860 (uihelpers.cpp:456): "s_number_string_meso" formatted with <see cref="GetNumberText"/>.</summary>
    public static string MesoNumberText(long n, Func<string, string> common, int lang = DefaultLanguage) =>
        ClientFormat.Format(common("s_number_string_meso") ?? "", GetNumberText(n, common, lang));

    /// <summary>
    /// ParseInt64W (commonfiles/packetlogger.cpp:171) = _wtoi64: optional blanks, sign, digits up to the first other
    /// character; 64-bit wrap on overflow; 0 when no digits.
    /// </summary>
    // The CRT's _wtoi64 saturates at the 64-bit limits on overflow (errno ERANGE); this wraps instead, so the two differ
    // only for inputs past the 64-bit range.
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
