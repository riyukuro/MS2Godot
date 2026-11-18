using System.Globalization;
using System.Text;

namespace Ms2.Formats.Text;

/// <summary>
/// The client's argument formatter for server-sent string ids (Unk_TextFormat, unit_14083f3d0_stringutilities.cpp:186):
/// StringIdInterface::FUN_141024e60 (stringidinterface.cpp) feeds it the packet's arguments in order. Each argument n
/// (FUN_14083ff40 @0x14083ff40) replaces every "{n}" by the argument and every "{n:fmt}" by the argument printed with "%fmt",
/// as a double (atof) when fmt ends in 'f', else as an __int64 (_atoi64).
/// <list type="bullet">
/// <item>The search goes on from the last start plus the inserted length, not from after the insertion (as the client).</item>
/// <item>A "{n" followed by anything but "}" or ":" (say "{10}" for argument 1) keeps the client's search start, so the
/// client finds it again forever; here the search steps past it instead.</item>
/// </list>
/// </summary>
public static class ArgumentFormat
{
    public static string Format(string format, IEnumerable<string> args)
    {
        string s = format ?? "";
        int n = 0;
        foreach (var a in args) s = Apply(s, n++, a ?? "");
        return s;
    }

    /// <summary>FUN_14083ff40: argument <paramref name="index"/> into <paramref name="s"/>.</summary>
    public static string Apply(string s, int index, string arg)
    {
        string tag = "{" + index.ToString(CultureInfo.InvariantCulture);
        int start = 0;
        while (true)
        {
            int find = s.IndexOf(tag, Math.Min(start, s.Length), StringComparison.Ordinal);
            if (find == -1) break;
            int end = s.IndexOf('}', find + tag.Length);
            if (end == -1) break;
            if (find + tag.Length == end)
            {
                s = s[..find] + arg + s[(end + 1)..];
                start += arg.Length;
            }
            else if (s[find + tag.Length] == ':')
            {
                string fmt = s.Substring(find + tag.Length + 1, end - (find + tag.Length + 1));
                string text = fmt.EndsWith('f') ? Printf(fmt, Atof(arg)) : Printf(fmt, Atoi64(arg));
                s = s[..find] + text + s[(end + 1)..];
                start += text.Length;
            }
            else start = find + tag.Length;     // the client loops here (see the summary)
        }
        return s;
    }

    /// <summary>_atoi64: leading white space, a sign and the digits; 0 when none.</summary>
    public static long Atoi64(string s)
    {
        int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        bool neg = false;
        if (i < s.Length && (s[i] == '+' || s[i] == '-')) neg = s[i++] == '-';
        long v = 0;
        while (i < s.Length && char.IsAsciiDigit(s[i])) v = unchecked(v * 10 + (s[i++] - '0'));
        return neg ? -v : v;
    }

    /// <summary>atof: the longest leading decimal number (sign, digits, '.', exponent); 0 when none.</summary>
    public static double Atof(string s)
    {
        int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        int b = i;
        if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
        while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
        if (i < s.Length && s[i] == '.') { i++; while (i < s.Length && char.IsAsciiDigit(s[i])) i++; }
        if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
        {
            int j = i + 1;
            if (j < s.Length && (s[j] == '+' || s[j] == '-')) j++;
            if (j < s.Length && char.IsAsciiDigit(s[j])) { i = j; while (i < s.Length && char.IsAsciiDigit(s[i])) i++; }
        }
        return double.TryParse(s.AsSpan(b, i - b), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
    }

    /// <summary>CStringW::Format("%" + fmt, value): flags "-+ 0#", width, precision and the types d i u x X o c (integer)
    /// and f F e E g G (double). Length prefixes are skipped (the value is already 64-bit). Another type gives the spec as
    /// written (STILL-OPEN: the CRT's output for such specs is not reproduced).</summary>
    public static string Printf(string fmt, object value)
    {
        int p = 0;
        bool left = false, plus = false, space = false, zero = false, alt = false;
        for (; p < fmt.Length && "-+ 0#".IndexOf(fmt[p]) >= 0; p++)
            switch (fmt[p]) { case '-': left = true; break; case '+': plus = true; break; case ' ': space = true; break; case '0': zero = true; break; default: alt = true; break; }
        int width = 0;
        while (p < fmt.Length && char.IsAsciiDigit(fmt[p])) width = width * 10 + (fmt[p++] - '0');
        int prec = -1;
        if (p < fmt.Length && fmt[p] == '.')
        {
            p++; prec = 0;
            while (p < fmt.Length && char.IsAsciiDigit(fmt[p])) prec = prec * 10 + (fmt[p++] - '0');
        }
        while (p < fmt.Length && "hlLIjzt0123456789".IndexOf(fmt[p]) >= 0) p++;
        if (p != fmt.Length - 1) return "%" + fmt;
        char type = fmt[p];
        string sign = "", body, prefix = "";
        bool numeric = true;
        switch (type)
        {
            case 'd' or 'i' or 'u' or 'x' or 'X' or 'o':
            {
                long v = value is double dv ? (long)dv : Convert.ToInt64(value, CultureInfo.InvariantCulture);
                if (type is 'd' or 'i')
                {
                    if (v < 0) sign = "-"; else if (plus) sign = "+"; else if (space) sign = " ";
                    body = (v < 0 ? (ulong)(-(v + 1)) + 1 : (ulong)v).ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    ulong u = (ulong)v;
                    body = type == 'u' ? u.ToString(CultureInfo.InvariantCulture) : type == 'o' ? Convert.ToString((long)u, 8)
                        : u.ToString(type == 'x' ? "x" : "X", CultureInfo.InvariantCulture);
                    if (alt && u != 0) prefix = type == 'o' ? "0" : type == 'x' ? "0x" : type == 'X' ? "0X" : "";
                }
                if (prec >= 0) { zero = false; if (prec == 0 && body == "0") body = ""; body = body.PadLeft(prec, '0'); }
                break;
            }
            case 'c':
                numeric = false;
                body = ((char)Convert.ToInt64(value is double cd ? (long)cd : value, CultureInfo.InvariantCulture)).ToString();
                break;
            case 'f' or 'F' or 'e' or 'E' or 'g' or 'G':
            {
                double d = value is double dd ? dd : Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (d < 0 || (d == 0 && double.IsNegative(d))) sign = "-"; else if (plus) sign = "+"; else if (space) sign = " ";
                body = Fixed(Math.Abs(d), type, prec < 0 ? 6 : prec, alt);
                break;
            }
            default:
                return "%" + fmt;
        }
        int pad = width - sign.Length - prefix.Length - body.Length;
        if (pad <= 0) return sign + prefix + body;
        if (left) return sign + prefix + body + new string(' ', pad);
        if (zero && numeric) return sign + prefix + new string('0', pad) + body;
        return new string(' ', pad) + sign + prefix + body;
    }

    static string Fixed(double d, char type, int prec, bool alt)
    {
        if (double.IsNaN(d)) return char.IsUpper(type) ? "NAN" : "nan";
        if (double.IsInfinity(d)) return char.IsUpper(type) ? "INF" : "inf";
        switch (type)
        {
            case 'f' or 'F':
            {
                string s = d.ToString("F" + prec, CultureInfo.InvariantCulture);
                return alt && prec == 0 ? s + "." : s;
            }
            case 'e' or 'E':
                return Exp(d, prec, type == 'E');
            default:
            {
                int P = prec == 0 ? 1 : prec;
                int X = d == 0 ? 0 : int.Parse(d.ToString("E" + (P - 1), CultureInfo.InvariantCulture).Split('E')[1], CultureInfo.InvariantCulture);
                string s = X >= -4 && X < P ? d.ToString("F" + (P - 1 - X), CultureInfo.InvariantCulture) : Exp(d, P - 1, type == 'G');
                if (!alt && s.Contains('.'))
                {
                    int e = s.IndexOfAny(new[] { 'e', 'E' });
                    string mant = e < 0 ? s : s[..e], tail = e < 0 ? "" : s[e..];
                    s = mant.TrimEnd('0').TrimEnd('.') + tail;
                }
                return s;
            }
        }
    }

    // the CRT prints at least two exponent digits ("1.5e+03")
    static string Exp(double d, int prec, bool upper)
    {
        string s = d.ToString((upper ? "E" : "e") + prec, CultureInfo.InvariantCulture);
        int e = s.IndexOfAny(new[] { 'e', 'E' });
        int x = int.Parse(s[(e + 1)..], CultureInfo.InvariantCulture);
        return s[..(e + 1)] + (x < 0 ? "-" : "+") + Math.Abs(x).ToString("00", CultureInfo.InvariantCulture);
    }
}
