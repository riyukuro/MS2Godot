using System.Globalization;
using System.Text;

namespace Ms2.Quest;

/// <summary>
/// The client's StringFormat (0x1401d02a0, ms2format.h) over the fmt library it links (ms2/fmt/format.h): "{n}" and
/// "{n:spec}" fields, spec = [[fill]align][sign]['#']['0'][width]['.' precision][type]. FUN_14028dfa0 / FUN_14028dee0
/// (commonutil.cpp:868-882) format a common string this way, and give "" when the string is empty. A field whose index
/// has no argument, and text outside fields, is kept as written; "{{" and "}}" are single braces.
/// </summary>
public static class ClientFormat
{
    public static string Format(string format, params object[] args)
    {
        if (string.IsNullOrEmpty(format)) return "";
        var sb = new StringBuilder();
        for (int i = 0; i < format.Length; i++)
        {
            char c = format[i];
            if (c == '{' && i + 1 < format.Length && format[i + 1] == '{') { sb.Append('{'); i++; continue; }
            if (c == '}' && i + 1 < format.Length && format[i + 1] == '}') { sb.Append('}'); i++; continue; }
            int close = c == '{' ? format.IndexOf('}', i + 1) : -1;
            if (close > i + 1)
            {
                var field = format.AsSpan(i + 1, close - i - 1);
                int colon = field.IndexOf(':');
                var index = colon < 0 ? field : field[..colon];
                if (int.TryParse(index, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n < args.Length)
                {
                    sb.Append(Field(args[n], colon < 0 ? "" : field[(colon + 1)..].ToString()));
                    i = close;
                    continue;
                }
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    static string Field(object arg, string spec)
    {
        int p = 0;
        char fill = ' ', align = '\0', sign = '-';
        bool alt = false;
        if (spec.Length >= 2 && "<>=^".IndexOf(spec[1]) >= 0) { fill = spec[0]; align = spec[1]; p = 2; }
        else if (spec.Length >= 1 && "<>=^".IndexOf(spec[0]) >= 0) { align = spec[0]; p = 1; }
        if (p < spec.Length && "+- ".IndexOf(spec[p]) >= 0) sign = spec[p++];
        if (p < spec.Length && spec[p] == '#') { alt = true; p++; }
        if (p < spec.Length && spec[p] == '0') { fill = '0'; align = '='; p++; }
        int width = 0;
        while (p < spec.Length && char.IsAsciiDigit(spec[p])) width = width * 10 + (spec[p++] - '0');
        int precision = -1;
        if (p < spec.Length && spec[p] == '.')
        {
            p++;
            precision = 0;
            while (p < spec.Length && char.IsAsciiDigit(spec[p])) precision = precision * 10 + (spec[p++] - '0');
        }
        char type = p < spec.Length ? spec[p] : '\0';

        string body, prefix = "";
        bool numeric = true, negative = false;
        switch (arg)
        {
            case string s:
                numeric = false;
                body = precision >= 0 && precision < s.Length ? s[..precision] : s;
                break;
            case float or double:
            {
                double d = Convert.ToDouble(arg, CultureInfo.InvariantCulture);
                negative = d < 0 || (d == 0 && double.IsNegative(d));
                body = Double(Math.Abs(d), type, precision, alt);
                break;
            }
            case bool b:
                numeric = false;
                body = b ? "true" : "false";
                break;
            case null:
                numeric = false;
                body = "";
                break;
            default:
            {
                long v = Convert.ToInt64(arg, CultureInfo.InvariantCulture);
                negative = v < 0;
                ulong u = negative ? (ulong)(-(v + 1)) + 1 : (ulong)v;
                switch (type)
                {
                    case 'x': body = u.ToString("x", CultureInfo.InvariantCulture); if (alt) prefix = "0x"; break;
                    case 'X': body = u.ToString("X", CultureInfo.InvariantCulture); if (alt) prefix = "0X"; break;
                    case 'o': body = Convert.ToString((long)u, 8); if (alt) prefix = "0"; break;
                    case 'b': body = Convert.ToString((long)u, 2); if (alt) prefix = "0b"; break;
                    case 'c': numeric = false; body = ((char)v).ToString(); break;
                    default: body = u.ToString(CultureInfo.InvariantCulture); break;
                }
                break;
            }
        }
        if (numeric)
        {
            string s = negative ? "-" : sign == '+' ? "+" : sign == ' ' ? " " : "";
            prefix = s + prefix;
        }
        int pad = width - prefix.Length - body.Length;
        if (pad <= 0) return prefix + body;
        if (align == '\0') align = numeric ? '>' : '<';
        string f = new(fill, pad);
        return align switch
        {
            '<' => prefix + body + f,
            '^' => new string(fill, pad / 2) + prefix + body + new string(fill, pad - pad / 2),
            '=' => prefix + f + body,
            _ => f + prefix + body,
        };
    }

    // fmt formats a double with snprintf: "%.{p}f" / "%e" / "%g" (no type: "%g"); precision -1 is printf's default 6
    static string Double(double d, char type, int precision, bool alt)
    {
        if (double.IsNaN(d)) return char.IsUpper(type) ? "NAN" : "nan";
        if (double.IsInfinity(d)) return char.IsUpper(type) ? "INF" : "inf";
        int prec = precision < 0 ? 6 : precision;
        switch (type)
        {
            case 'f':
            case 'F':
                return d.ToString("F" + prec, CultureInfo.InvariantCulture);
            case 'e':
            case 'E':
                return Exp(d, prec, type == 'E');
            default:
            {
                // %g: P = precision (0 -> 1); exponent X of %e; X >= -4 and X < P -> %f with P-1-X, else %e with P-1;
                // trailing zeros removed unless '#'
                int P = prec == 0 ? 1 : prec;
                int X = d == 0 ? 0 : int.Parse(d.ToString("E" + (P - 1), CultureInfo.InvariantCulture).Split('E')[1], CultureInfo.InvariantCulture);
                string s = X >= -4 && X < P ? d.ToString("F" + (P - 1 - X), CultureInfo.InvariantCulture) : Exp(d, P - 1, type == 'G');
                if (!alt && s.Contains('.'))
                {
                    int e = s.IndexOfAny(new[] { 'e', 'E' });
                    string mant = e < 0 ? s : s[..e], tail = e < 0 ? "" : s[e..];
                    mant = mant.TrimEnd('0').TrimEnd('.');
                    s = mant + tail;
                }
                return s;
            }
        }
    }

    static string Exp(double d, int prec, bool upper)
    {
        string s = d.ToString((upper ? "E" : "e") + prec, CultureInfo.InvariantCulture);   // "1.5e+003"
        int e = s.IndexOfAny(new[] { 'e', 'E' });
        int x = int.Parse(s[(e + 1)..], CultureInfo.InvariantCulture);
        return s[..(e + 1)] + (x < 0 ? "-" : "+") + Math.Abs(x).ToString("00", CultureInfo.InvariantCulture);
    }
}
