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

    abstract class Node
    {
        public abstract double Eval(Func<string, double> get);
        public abstract double Eval(in NifScope s);
    }
    sealed class Const : Node
    {
        public double V;
        public override double Eval(Func<string, double> get) => V;
        public override double Eval(in NifScope s) => V;
    }
    sealed class Ident : Node
    {
        public readonly string Name;
        // how the reader resolves the name, decided once here in place of on every evaluation (NifReader.Lookup's order)
        readonly NifScope.Kind kind;
        readonly string key;          // #ARG# name without the '#'s
        readonly string[] path;       // a\b\c
        public Ident(string name)
        {
            Name = name;
            kind = NifScope.Classify(name);
            if (kind == NifScope.Kind.Arg) key = name.Trim('#');
            else if (kind == NifScope.Kind.Path) path = name.Split('\\');
        }
        public override double Eval(Func<string, double> get) => get(Name);
        public override double Eval(in NifScope s) => s.Get(kind, Name, key, path);
    }
    enum Op : byte { Neg, Not, Inv, Add, Sub, Mul, Div, Shl, Shr, And, Or, Eq, Ne, Lt, Gt, Le, Ge, LAnd, LOr }
    static Op OpOf(string op, bool unary) => op switch
    {
        "-" => unary ? Op.Neg : Op.Sub, "~" => Op.Inv, "!" => Op.Not,
        "+" => Op.Add, "*" => Op.Mul, "/" => Op.Div, "<<" => Op.Shl, ">>" => Op.Shr, "&" => Op.And, "|" => Op.Or,
        "==" => Op.Eq, "!=" => Op.Ne, "<" => Op.Lt, ">" => Op.Gt, "<=" => Op.Le, ">=" => Op.Ge, "&&" => Op.LAnd, "||" => Op.LOr,
        _ => throw new InvalidOperationException(op),
    };
    sealed class Unary : Node
    {
        public readonly Op O; public readonly Node A;
        public Unary(string op, Node a) { O = OpOf(op, true); A = a; }
        double Apply(double a) => O switch { Op.Neg => -a, Op.Inv => ~(long)a, _ => a == 0 ? 1 : 0 };
        public override double Eval(Func<string, double> get) => Apply(A.Eval(get));
        public override double Eval(in NifScope s) => Apply(A.Eval(s));
    }
    sealed class Binary : Node
    {
        public readonly Op O; public readonly Node A, B;
        public Binary(string op, Node a, Node b) { O = OpOf(op, false); A = a; B = b; }
        static double Apply(Op o, double a, double b) => o switch
        {
            Op.Add => a + b, Op.Sub => a - b, Op.Mul => a * b, Op.Div => a / b,
            Op.Shl => (long)a << (int)b, Op.Shr => (long)a >> (int)b,
            Op.And => (long)a & (long)b, Op.Or => (long)a | (long)b,
            Op.Eq => a == b ? 1 : 0, Op.Ne => a != b ? 1 : 0,
            Op.Lt => a < b ? 1 : 0, Op.Gt => a > b ? 1 : 0, Op.Le => a <= b ? 1 : 0, _ => a >= b ? 1 : 0,
        };
        public override double Eval(Func<string, double> get)
        {
            if (O == Op.LAnd) return A.Eval(get) != 0 && B.Eval(get) != 0 ? 1 : 0;
            if (O == Op.LOr) return A.Eval(get) != 0 || B.Eval(get) != 0 ? 1 : 0;
            double a = A.Eval(get);
            return Apply(O, a, B.Eval(get));
        }
        public override double Eval(in NifScope s)
        {
            if (O == Op.LAnd) return A.Eval(s) != 0 && B.Eval(s) != 0 ? 1 : 0;
            if (O == Op.LOr) return A.Eval(s) != 0 || B.Eval(s) != 0 ? 1 : 0;
            double a = A.Eval(s);
            return Apply(O, a, B.Eval(s));
        }
    }

    readonly Node root;
    public string Source { get; }

    NifExpr(string src, Node root) { Source = src; this.root = root; }

    public double Eval(Func<string, double> get) => root.Eval(get);
    /// <summary>The reader's evaluation: the same values as Eval with NifReader's lookup, with no delegate per struct.</summary>
    internal double Eval(in NifScope s) => root.Eval(s);

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
        while (At("||")) { pos++; a = new Binary("||", a, ParseAnd()); }
        return a;
    }
    Node ParseAnd()
    {
        var a = ParseNot();
        while (At("&&")) { pos++; a = new Binary("&&", a, ParseNot()); }
        return a;
    }
    Node ParseNot()
    {
        if (At("!")) { pos++; return new Unary("!", ParseNot()); }
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
            var c = new Binary(op, a, b);
            result = result == null ? c : new Binary("&&", result, c);   // Python chained compare
            a = b;
        }
        return result ?? a;
    }
    Node ParseBor()
    {
        var a = ParseBand();
        while (At("|")) { pos++; a = new Binary("|", a, ParseBand()); }
        return a;
    }
    Node ParseBand()
    {
        var a = ParseShift();
        while (At("&")) { pos++; a = new Binary("&", a, ParseShift()); }
        return a;
    }
    Node ParseShift()
    {
        var a = ParseArith();
        string op;
        while ((op = AtAny("<<", ">>")) != null) { pos++; a = new Binary(op, a, ParseArith()); }
        return a;
    }
    Node ParseArith()
    {
        var a = ParseTerm();
        string op;
        while ((op = AtAny("+", "-")) != null) { pos++; a = new Binary(op, a, ParseTerm()); }
        return a;
    }
    Node ParseTerm()
    {
        var a = ParseUnary();
        string op;
        while ((op = AtAny("*", "/")) != null) { pos++; a = new Binary(op, a, ParseUnary()); }
        return a;
    }
    Node ParseUnary()
    {
        string op;
        if ((op = AtAny("-", "~")) != null) { pos++; return new Unary(op, ParseUnary()); }
        if (At("!")) { pos++; return new Unary("!", ParseUnary()); }
        return ParseAtom();
    }
    Node ParseAtom()
    {
        if (pos >= toks.Count) throw new FormatException("unexpected end of expression");
        var t = toks[pos++];
        if (t.k == 'n') return new Const { V = t.v };
        if (t.k == 'i') return new Ident(t.t);
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
