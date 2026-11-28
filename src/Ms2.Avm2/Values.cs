using System.Globalization;

namespace Ms2.Avm2;

/// <summary>The AS3 undefined value (null is C# null).</summary>
public sealed class Undefined
{
    public static readonly Undefined Value = new();
    Undefined() { }
    public override string ToString() => "undefined";
}

/// <summary>An ActionScript exception in flight: <see cref="Value"/> is the thrown AS value.</summary>
public sealed class AsThrow : Exception
{
    public readonly object Value;
    /// <summary>The AS3 methods active when it was thrown (innermost first).</summary>
    public readonly string AsStack;
    public AsThrow(object value) : base(Rt.Describe(value)) { Value = value; AsStack = Interp.StackString(8); }
}

/// <summary>
/// Value rules of AVM2 (ECMA-262 3rd edition conversions plus the AS3 int/uint/Number split). Values are C# objects:
/// <c>null</c>, <see cref="Undefined.Value"/>, bool, int, double (Number and uint outside the int range), string,
/// <see cref="AsObject"/>.
/// </summary>
public static class Rt
{
    public static readonly object Undef = Undefined.Value;
    public static readonly object True = true, False = false;
    static readonly object[] smallInts = Enumerable.Range(-128, 1280).Select(i => (object)i).ToArray();

    public static object Box(int i) => i >= -128 && i < 1152 ? smallInts[i + 128] : i;
    public static object Box(bool b) => b ? True : False;

    static readonly string[] intStrings = new string[4096];
    /// <summary>The string of an int (Rt.ToString's), shared for 0..4095: array index keys and Dictionary number keys.</summary>
    public static string IntString(int i) =>
        (uint)i < (uint)intStrings.Length ? intStrings[i] ??= i.ToString(CultureInfo.InvariantCulture) : i.ToString(CultureInfo.InvariantCulture);

    /// <summary>A Number as stored: int when it is an integer in the int range (not -0), else double.</summary>
    public static object Num(double d)
    {
        if (d >= int.MinValue && d <= int.MaxValue)
        {
            int i = (int)d;
            if (i == d && !(i == 0 && double.IsNegative(d))) return Box(i);
        }
        return d;
    }

    public static object Uint(uint u) => u <= int.MaxValue ? Box((int)u) : (double)u;

    public static bool IsUndef(object v) => ReferenceEquals(v, Undef);
    public static bool IsNullish(object v) => v == null || ReferenceEquals(v, Undef);
    public static bool IsNumber(object v) => v is int or double or uint;

    public static double ToNumber(object v) => v switch
    {
        int i => i,
        double d => d,
        uint u => u,
        bool b => b ? 1 : 0,
        null => 0,
        string s => StringToNumber(s),
        Undefined => double.NaN,
        AsObject o => ToNumber(o.ToPrimitive(true)),
        _ => double.NaN,
    };

    public static double StringToNumber(string s)
    {
        s = s.Trim();
        if (s.Length == 0) return 0;
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && long.TryParse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var h)) return h;
        if (s.StartsWith("-0x", StringComparison.OrdinalIgnoreCase) && long.TryParse(s.AsSpan(3), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var nh)) return -nh;
        if (s == "Infinity" || s == "+Infinity") return double.PositiveInfinity;
        if (s == "-Infinity") return double.NegativeInfinity;
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN;
    }

    public static int ToInt32(object v) => v is int i ? i : ToInt32(ToNumber(v));

    public static int ToInt32(double d)
    {
        if (double.IsNaN(d) || double.IsInfinity(d)) return 0;
        if (d >= int.MinValue && d <= int.MaxValue) return (int)d;
        double m = Math.Truncate(d) % 4294967296.0;
        if (m < 0) m += 4294967296.0;
        return (int)(uint)m;
    }

    public static uint ToUint32(object v) => (uint)ToInt32(v);

    public static bool ToBoolean(object v) => v switch
    {
        bool b => b,
        int i => i != 0,
        double d => !(d == 0 || double.IsNaN(d)),
        uint u => u != 0,
        string s => s.Length > 0,
        null => false,
        Undefined => false,
        _ => true,
    };

    public static string ToString(object v) => v switch
    {
        string s => s,
        int i => i.ToString(CultureInfo.InvariantCulture),
        double d => NumberToString(d),
        uint u => u.ToString(CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        null => "null",
        Undefined => "undefined",
        AsObject o => ToString(o.ToPrimitive(false)),
        _ => v.ToString(),
    };

    public static string NumberToString(double d)
    {
        if (double.IsNaN(d)) return "NaN";
        if (double.IsPositiveInfinity(d)) return "Infinity";
        if (double.IsNegativeInfinity(d)) return "-Infinity";
        if (d == 0) return "0";
        if (Math.Abs(d) < 1e21 && d == Math.Truncate(d)) return d.ToString("0", CultureInfo.InvariantCulture);
        var r = d.ToString("R", CultureInfo.InvariantCulture);
        if (r.Contains('E')) r = r.Replace("E+", "e+").Replace("E-", "e-");
        return r;
    }

    /// <summary>String conversion for coerce_s / String-typed slots: null and undefined stay null.</summary>
    public static object CoerceString(object v) => IsNullish(v) ? null : ToString(v);

    public static object ToPrimitive(object v, bool numberHint = true) => v is AsObject o ? o.ToPrimitive(numberHint) : v;

    public static string TypeOf(object v) => v switch
    {
        null => "object",
        Undefined => "undefined",
        bool => "boolean",
        int or double or uint => "number",
        string => "string",
        AsFunction => "function",
        AsXmlLike => "xml",
        _ => "object",
    };

    // ---- operators --------------------------------------------------------------------------------------

    public static object Add(object a, object b)
    {
        if (a is int x && b is int y) { long r = (long)x + y; return r >= int.MinValue && r <= int.MaxValue ? Box((int)r) : (double)r; }
        if (a is string sa) return sa + ToString(ToPrimitive(b, false));
        if (b is string sb) return ToString(ToPrimitive(a, false)) + sb;
        var pa = ToPrimitive(a, true); var pb = ToPrimitive(b, true);
        if (pa is string || pb is string) return ToString(pa) + ToString(pb);
        return Num(ToNumber(pa) + ToNumber(pb));
    }

    public static bool StrictEquals(object a, object b)
    {
        if (IsNumber(a) && IsNumber(b)) return ToNumber(a) == ToNumber(b);
        if (a is string sa && b is string sb) return sa == sb;
        if (a is bool ba && b is bool bb) return ba == bb;
        if (a == null || b == null) return a == null && b == null;
        if (a is Undefined || b is Undefined) return a is Undefined && b is Undefined;
        if (a is AsMethodClosure ma && b is AsMethodClosure mb) return ma.SameAs(mb);
        return ReferenceEquals(a, b);
    }

    public static bool LooseEquals(object a, object b)
    {
        if (IsNullish(a) && IsNullish(b)) return true;
        if (IsNullish(a) || IsNullish(b)) return false;
        if (IsNumber(a) && IsNumber(b)) return ToNumber(a) == ToNumber(b);
        if (a is string sa && b is string sb) return sa == sb;
        if (a is bool ba && b is bool bb) return ba == bb;
        if (a is AsObject && b is AsObject) return StrictEquals(a, b);
        if (a is bool) return LooseEquals(ToNumber(a), b);
        if (b is bool) return LooseEquals(a, ToNumber(b));
        if (IsNumber(a) && b is string) return ToNumber(a) == ToNumber(b);
        if (a is string && IsNumber(b)) return ToNumber(a) == ToNumber(b);
        if (a is AsObject oa) return LooseEquals(oa.ToPrimitive(true), b);
        if (b is AsObject ob) return LooseEquals(a, ob.ToPrimitive(true));
        return false;
    }

    /// <summary>Abstract relational comparison: true, false, or null for undefined (NaN involved).</summary>
    public static bool? LessThan(object a, object b)
    {
        if (a is int x && b is int y) return x < y;
        var pa = ToPrimitive(a, true); var pb = ToPrimitive(b, true);
        if (pa is string sa && pb is string sb) return string.CompareOrdinal(sa, sb) < 0;
        double da = ToNumber(pa), db = ToNumber(pb);
        if (double.IsNaN(da) || double.IsNaN(db)) return null;
        return da < db;
    }

    public static string Describe(object v)
    {
        if (v is AsObject o && o.Class != null && o.Class.IsErrorClass) return o.Class.Name + ": " + ToString(o.GetPublic("message"));
        try { return ToString(v); } catch { return v?.ToString() ?? "null"; }
    }
}

/// <summary>Marker for XML values (E4X is not supported; the UI movies hardly use it).</summary>
public abstract class AsXmlLike : AsObject { }
