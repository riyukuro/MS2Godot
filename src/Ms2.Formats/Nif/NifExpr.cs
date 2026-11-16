using System.Globalization;
using System.Text.RegularExpressions;

namespace Ms2.Formats.Nif;

/// <summary>
/// Compiled nif.xml condition / length expression. Operator precedence follows the reference Python tool
/// (tools/nif/nifxml.py in the decomp tree), which evaluates the expressions with Python semantics:
/// || &lt; &amp;&amp; &lt; ! &lt; comparisons &lt; | &lt; &amp; &lt; shifts &lt; + - &lt; * / &lt; unary - ~.
/// All values are doubles; bitwise operators work on the integer part.
/// </summary>
public sealed class NifExpr
{
    static readonly Regex Ops = new(@"(\|\||&&|==|!=|<=|>=|<<|>>|[-+*/()<>!&|~])", RegexOptions.Compiled);
    static readonly Regex VerLit = new(@"^\d+\.\d+\.\d+\.\d+$", RegexOptions.Compiled);
    static readonly Regex NumLit = new(@"^(0x[0-9a-fA-F]+|\d+(\.\d*)?([eE][-+]?\d+)?)$", RegexOptions.Compiled);

    abstract class Node { public abstract double Eval(Func<string, double> get); }
    sealed class Const : Node { public double V; public override double Eval(Func<string, double> get) => V; }
    sealed class Ident : Node { public string Name; public override double Eval(Func<string, double> get) => get(Name); }
    sealed class Unary : Node
    {
        public string Op; public Node A;
        public override double Eval(Func<string, double> get)
        {
            double a = A.Eval(get);
            return Op switch { "-" => -a, "~" => ~(long)a, "!" => a == 0 ? 1 : 0, _ => throw new InvalidOperationException(Op) };
        }
    }
    sealed class Binary : Node
    {
        public string Op; public Node A, B;
        public override double Eval(Func<string, double> get)
        {
            if (Op == "&&") return A.Eval(get) != 0 && B.Eval(get) != 0 ? 1 : 0;
            if (Op == "||") return A.Eval(get) != 0 || B.Eval(get) != 0 ? 1 : 0;
            double a = A.Eval(get), b = B.Eval(get);
            return Op switch
            {
                "+" => a + b, "-" => a - b, "*" => a * b, "/" => a / b,
                "<<" => (long)a << (int)b, ">>" => (long)a >> (int)b,
                "&" => (long)a & (long)b, "|" => (long)a | (long)b,
                "==" => a == b ? 1 : 0, "!=" => a != b ? 1 : 0,
                "<" => a < b ? 1 : 0, ">" => a > b ? 1 : 0, "<=" => a <= b ? 1 : 0, ">=" => a >= b ? 1 : 0,
                _ => throw new InvalidOperationException(Op),
            };
        }
    }

    readonly Node root;
    public string Source { get; }

    NifExpr(string src, Node root) { Source = src; this.root = root; }

    public double Eval(Func<string, double> get) => root.Eval(get);

    public static uint ParseVersion(string s)
    {
        s = s.Trim();
        if (s.StartsWith('V')) s = s[1..].Split("__")[0].Replace('_', '.');
        var p = s.Split('.').Select(x => uint.Parse(x, CultureInfo.InvariantCulture)).Concat(new uint[] { 0, 0, 0 }).ToArray();
        return (p[0] << 24) | (p[1] << 16) | (p[2] << 8) | p[3];
    }

    // ---- parser ----
    // token: (kind, text) with kind 'o' operator, 'n' number, 'i' identifier
    List<(char k, string t, double v)> toks;
    int pos;

    public static NifExpr Compile(string expanded)
    {
        var toks = new List<(char, string, double)>();
        foreach (var part in Ops.Split(expanded))
        {
            var q = part.Trim();
            if (q.Length == 0) continue;
            if (Ops.IsMatch(q) && Ops.Match(q).Length == q.Length) toks.Add(('o', q, 0));
            else if (VerLit.IsMatch(q)) toks.Add(('n', q, ParseVersion(q)));
            else if (NumLit.IsMatch(q))
                toks.Add(('n', q, q.StartsWith("0x") ? Convert.ToInt64(q[2..], 16) : double.Parse(q, CultureInfo.InvariantCulture)));
            else if (q == "INFINITY") toks.Add(('n', q, double.PositiveInfinity));
            else toks.Add(('i', q, 0));
        }
        var e = new NifExpr(expanded, null);
        if (toks.Count == 0) return new NifExpr(expanded, new Const { V = 1 });
        e.toks = toks;
        var n = e.ParseOr();
        if (e.pos != toks.Count) throw new FormatException("trailing tokens in expression: " + expanded);
        return new NifExpr(expanded, n);
    }

    bool At(string op) => pos < toks.Count && toks[pos].k == 'o' && toks[pos].t == op;
    string AtAny(params string[] ops)
    {
        if (pos >= toks.Count || toks[pos].k != 'o') return null;
        foreach (var o in ops) if (toks[pos].t == o) return o;
        return null;
    }

    Node ParseOr()
    {
        var a = ParseAnd();
        while (At("||")) { pos++; a = new Binary { Op = "||", A = a, B = ParseAnd() }; }
        return a;
    }
    Node ParseAnd()
    {
        var a = ParseNot();
        while (At("&&")) { pos++; a = new Binary { Op = "&&", A = a, B = ParseNot() }; }
        return a;
    }
    Node ParseNot()
    {
        if (At("!")) { pos++; return new Unary { Op = "!", A = ParseNot() }; }
        return ParseCmp();
    }
    Node ParseCmp()
    {
        var a = ParseBor();
        Node result = null;
        string op;
        while ((op = AtAny("==", "!=", "<=", ">=", "<", ">")) != null)
        {
            pos++;
            var b = ParseBor();
            var c = new Binary { Op = op, A = a, B = b };
            result = result == null ? c : new Binary { Op = "&&", A = result, B = c };   // Python chained compare
            a = b;
        }
        return result ?? a;
    }
    Node ParseBor()
    {
        var a = ParseBand();
        while (At("|")) { pos++; a = new Binary { Op = "|", A = a, B = ParseBand() }; }
        return a;
    }
    Node ParseBand()
    {
        var a = ParseShift();
        while (At("&")) { pos++; a = new Binary { Op = "&", A = a, B = ParseShift() }; }
        return a;
    }
    Node ParseShift()
    {
        var a = ParseArith();
        string op;
        while ((op = AtAny("<<", ">>")) != null) { pos++; a = new Binary { Op = op, A = a, B = ParseArith() }; }
        return a;
    }
    Node ParseArith()
    {
        var a = ParseTerm();
        string op;
        while ((op = AtAny("+", "-")) != null) { pos++; a = new Binary { Op = op, A = a, B = ParseTerm() }; }
        return a;
    }
    Node ParseTerm()
    {
        var a = ParseUnary();
        string op;
        while ((op = AtAny("*", "/")) != null) { pos++; a = new Binary { Op = op, A = a, B = ParseUnary() }; }
        return a;
    }
    Node ParseUnary()
    {
        string op;
        if ((op = AtAny("-", "~")) != null) { pos++; return new Unary { Op = op, A = ParseUnary() }; }
        if (At("!")) { pos++; return new Unary { Op = "!", A = ParseUnary() }; }
        return ParseAtom();
    }
    Node ParseAtom()
    {
        if (pos >= toks.Count) throw new FormatException("unexpected end of expression");
        var t = toks[pos++];
        if (t.k == 'n') return new Const { V = t.v };
        if (t.k == 'i') return new Ident { Name = t.t };
        if (t.t == "(")
        {
            var n = ParseOr();
            if (!At(")")) throw new FormatException("missing )");
            pos++;
            return n;
        }
        throw new FormatException("unexpected token " + t.t);
    }
}
