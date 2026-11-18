using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Ms2.Avm2;

/// <summary>The ECMAScript/AS3 top level: Object, Class, Function, Array, String, Number, int, uint, Boolean, Math, errors,
/// RegExp, Date, Vector, Dictionary and the global functions.</summary>
static class Builtins
{
    static object A(object[] a, int i) => i < a.Length ? a[i] : Rt.Undef;

    public static void Install(Runtime rt)
    {
        // bootstrap Object and Class by hand
        var obj = new AsClass { Runtime = rt, Name = "Object", LocalName = "Object", Ns = Ns.Public, InstancesDynamic = true, IsNative = true };
        obj.InstanceTraits = new TraitTable(); obj.Traits = new TraitTable(); obj.Slots = Array.Empty<object>();
        obj.Prototype = new AsObject { IsPrototypeObject = true };
        obj.Allocator = c => new AsObject();
        rt.ObjectClass = obj;
        obj.Prototype.Class = obj;
        var cls = new AsClass { Runtime = rt, Name = "Class", LocalName = "Class", Ns = Ns.Public, Super = obj, IsNative = true, InstancesDynamic = true };
        cls.InstanceTraits = new TraitTable(obj.InstanceTraits); cls.Traits = new TraitTable(); cls.Slots = Array.Empty<object>();
        cls.Prototype = new AsObject { Class = obj, IsPrototypeObject = true, ProtoParent = obj.Prototype };
        rt.ClassClass = cls;
        obj.Class = cls; cls.Class = cls;
        rt.DefineGlobal("Object", obj); rt.DefineGlobal("Class", cls);
        obj.CallAsFunction = a => Rt.IsNullish(A(a, 0)) ? new AsObject(obj) : A(a, 0);

        var ot = obj.InstanceTraits;
        Runtime.Method(ot, "hasOwnProperty", (s, a) => Rt.Box(rt.HasProperty(s, Name.Public(Rt.ToString(A(a, 0))), A(a, 0), true)));
        Runtime.Method(ot, "propertyIsEnumerable", (s, a) => Rt.Box(s is AsObject o && (o.HasDynamic(Rt.ToString(A(a, 0))) || (o.TryHasKey(A(a, 0), out var h) && h))));
        Runtime.Method(ot, "setPropertyIsEnumerable", (s, a) => Rt.Undef);
        Runtime.Method(ot, "isPrototypeOf", (s, a) => Rt.Box(false));
        Runtime.Method(ot, "toString", (s, a) => s is AsObject o ? o.DefaultString() : Rt.ToString(s));
        Runtime.Method(ot, "toLocaleString", (s, a) => s is AsObject o ? o.DefaultString() : Rt.ToString(s));
        Runtime.Method(ot, "valueOf", (s, a) => s);
        // Class instances (class objects) expose prototype
        Runtime.Accessor(cls.InstanceTraits, "prototype", s => (s as AsClass)?.Prototype);

        var fn = rt.FunctionClass = rt.NewNativeClass("Function", obj, null, true);
        Runtime.Method(fn.InstanceTraits, "call", (s, a) => ((AsFunction)s).Call(A(a, 0), a.Length > 1 ? a[1..] : Array.Empty<object>()));
        Runtime.Method(fn.InstanceTraits, "apply", (s, a) => ((AsFunction)s).Call(A(a, 0), A(a, 1) is AsArray arr ? arr.Items.ToArray() : Array.Empty<object>()));
        Runtime.Accessor(fn.InstanceTraits, "length", s => s is AsMethodClosure mc && mc.Method is AbcMethod am ? Rt.Box(am.Info.ParamCount) : s is AsPlainFunction pf && pf.Method is AbcMethod pm ? Rt.Box(pm.Info.ParamCount) : Rt.Box(0));
        Runtime.Accessor(fn.InstanceTraits, "prototype", s => ((AsObject)s).GetDynamic("prototype"), (s, v) => ((AsObject)s).SetDynamic("prototype", v));
        // fix up the classes made before Function existed
        foreach (var c in new[] { obj, cls, fn }) c.Class = cls;

        rt.NamespaceClass = rt.NewNativeClass("Namespace", obj);
        Runtime.Accessor(rt.NamespaceClass.InstanceTraits, "uri", s => ((AsNamespace)s).Ns.Uri);

        InstallArray(rt);
        InstallString(rt);
        InstallNumbers(rt);
        InstallMath(rt);
        InstallErrors(rt);
        InstallRegExp(rt);
        InstallDate(rt);
        InstallGlobals(rt);
    }

    // ---- Array / Vector ------------------------------------------------------------------------------------

    static void InstallArray(Runtime rt)
    {
        var arr = rt.ArrayClass = rt.NewNativeClass("Array", rt.ObjectClass, c => new AsArray(), true);
        arr.Ctor = new NativeMethod("Array", (s, a) =>
        {
            var self = (AsArray)s;
            if (a.Length == 1 && Rt.IsNumber(a[0])) self.Length = Rt.ToInt32(a[0]);
            else self.Items.AddRange(a);
            return Rt.Undef;
        });
        arr.CallAsFunction = a => arr.Construct(a);
        foreach (var (k, v) in new[] { ("CASEINSENSITIVE", 1), ("DESCENDING", 2), ("UNIQUESORT", 4), ("RETURNINDEXEDARRAY", 8), ("NUMERIC", 16) }) Runtime.Const(arr, k, Rt.Box(v));
        DefineListMethods(rt, arr.InstanceTraits, () => arr);

        var vec = rt.VectorClass = rt.NewNativeClass("__AS3__.vec.Vector", rt.ObjectClass, c => new AsArray(), true);
        rt.DefineGlobal("Vector", vec);
        foreach (var n in new[] { "__AS3__.vec.Vector$object", "__AS3__.vec.Vector$int", "__AS3__.vec.Vector$uint", "__AS3__.vec.Vector$double" }) rt.DefineGlobal(n, vec);
        vec.Ctor = new NativeMethod("Vector", (s, a) =>
        {
            var self = (AsArray)s;
            int len = Rt.ToInt32(A(a, 0));
            object def = self.Class?.TypeArg == rt.IntClass || self.Class?.TypeArg == rt.UintClass ? Rt.Box(0) : self.Class?.TypeArg == rt.NumberClass ? 0.0 : null;
            for (int i = 0; i < len; i++) self.Items.Add(def);
            return Rt.Undef;
        });
        vec.CallAsFunction = a => A(a, 0) is AsArray src ? new AsArray(rt, src.Items, vec) : new AsArray(rt, null, vec);
        Runtime.Accessor(vec.InstanceTraits, "fixed", s => Rt.False, (s, v) => { });
        DefineListMethods(rt, vec.InstanceTraits, () => vec);
    }

    static void DefineListMethods(Runtime rt, TraitTable t, Func<AsClass> cls)
    {
        AsArray Me(object s) => (AsArray)s;
        AsArray New(object s, IEnumerable<object> items) => new AsArray(rt, items, (s as AsObject)?.Class ?? cls());
        Runtime.Accessor(t, "length", s => Rt.Box(Me(s).Length), (s, v) => Me(s).Length = Rt.ToInt32(v));
        Runtime.Method(t, "push", (s, a) => { Me(s).Items.AddRange(a); return Rt.Box(Me(s).Length); });
        Runtime.Method(t, "pop", (s, a) => { var l = Me(s).Items; if (l.Count == 0) return Rt.Undef; var v = l[^1]; l.RemoveAt(l.Count - 1); return v; });
        Runtime.Method(t, "shift", (s, a) => { var l = Me(s).Items; if (l.Count == 0) return Rt.Undef; var v = l[0]; l.RemoveAt(0); return v; });
        Runtime.Method(t, "unshift", (s, a) => { Me(s).Items.InsertRange(0, a); return Rt.Box(Me(s).Length); });
        Runtime.Method(t, "splice", (s, a) =>
        {
            var l = Me(s).Items; int n = l.Count;
            if (a.Length == 0) return New(s, null);
            int st = Rel(Rt.ToNumber(A(a, 0)), n);
            int dc = a.Length < 2 ? n - st : Math.Clamp(Rt.ToInt32(a[1]), 0, n - st);
            var removed = l.GetRange(st, dc);
            l.RemoveRange(st, dc);
            if (a.Length > 2) l.InsertRange(st, a[2..]);
            return New(s, removed);
        });
        Runtime.Method(t, "slice", (s, a) =>
        {
            var l = Me(s).Items; int n = l.Count;
            int st = Rel(a.Length > 0 ? Rt.ToNumber(a[0]) : 0, n), en = Rel(a.Length > 1 && !Rt.IsUndef(a[1]) ? Rt.ToNumber(a[1]) : n, n);
            return New(s, en > st ? l.GetRange(st, en - st) : null);
        });
        Runtime.Method(t, "concat", (s, a) =>
        {
            var r = New(s, Me(s).Items);
            foreach (var x in a) if (x is AsArray xa) r.Items.AddRange(xa.Items); else r.Items.Add(x);
            return r;
        });
        Runtime.Method(t, "join", (s, a) => string.Join(Rt.IsUndef(A(a, 0)) ? "," : Rt.ToString(a[0]), Me(s).Items.Select(x => Rt.IsNullish(x) ? "" : Rt.ToString(x))));
        Runtime.Method(t, "toString", (s, a) => string.Join(",", Me(s).Items.Select(x => Rt.IsNullish(x) ? "" : Rt.ToString(x))));
        Runtime.Method(t, "reverse", (s, a) => { Me(s).Items.Reverse(); return s; });
        Runtime.Method(t, "indexOf", (s, a) => { var l = Me(s).Items; for (int i = Math.Max(0, Rt.ToInt32(A(a, 1))); i < l.Count; i++) if (Rt.StrictEquals(l[i], A(a, 0))) return Rt.Box(i); return Rt.Box(-1); });
        Runtime.Method(t, "lastIndexOf", (s, a) => { var l = Me(s).Items; for (int i = l.Count - 1; i >= 0; i--) if (Rt.StrictEquals(l[i], A(a, 0))) return Rt.Box(i); return Rt.Box(-1); });
        Runtime.Method(t, "forEach", (s, a) => { var f = A(a, 0) as AsFunction; var l = Me(s).Items.ToList(); for (int i = 0; i < l.Count; i++) f?.Call(A(a, 1), new[] { l[i], Rt.Box(i), s }); return Rt.Undef; });
        Runtime.Method(t, "map", (s, a) => { var f = (AsFunction)A(a, 0); var l = Me(s).Items.ToList(); return New(s, l.Select((x, i) => f.Call(A(a, 1), new[] { x, Rt.Box(i), s }))); });
        Runtime.Method(t, "filter", (s, a) => { var f = (AsFunction)A(a, 0); var l = Me(s).Items.ToList(); return New(s, l.Where((x, i) => Rt.ToBoolean(f.Call(A(a, 1), new[] { x, Rt.Box(i), s })))); });
        Runtime.Method(t, "some", (s, a) => { var f = (AsFunction)A(a, 0); var l = Me(s).Items.ToList(); for (int i = 0; i < l.Count; i++) if (Rt.ToBoolean(f.Call(A(a, 1), new[] { l[i], Rt.Box(i), s }))) return Rt.True; return Rt.False; });
        Runtime.Method(t, "every", (s, a) => { var f = (AsFunction)A(a, 0); var l = Me(s).Items.ToList(); for (int i = 0; i < l.Count; i++) if (!Rt.ToBoolean(f.Call(A(a, 1), new[] { l[i], Rt.Box(i), s }))) return Rt.False; return Rt.True; });
        Runtime.Method(t, "sort", (s, a) =>
        {
            var l = Me(s).Items;
            AsFunction f = A(a, 0) as AsFunction;
            int opts = f == null ? Rt.ToInt32(A(a, 0)) : Rt.ToInt32(A(a, 1));
            Comparison<object> cmp = f != null ? (x, y) => Math.Sign(Rt.ToNumber(f.Call(null, new[] { x, y })))
                : Cmp(opts);
            var sorted = StableSort(l, cmp);
            if ((opts & 8) != 0) return New(s, sorted.Select(x => Rt.Box(l.IndexOf(x))));
            l.Clear(); l.AddRange(sorted);
            return s;
        });
        Runtime.Method(t, "sortOn", (s, a) =>
        {
            var l = Me(s).Items;
            var fields = A(a, 0) is AsArray fa ? fa.Items.Select(Rt.ToString).ToArray() : new[] { Rt.ToString(A(a, 0)) };
            var optsV = A(a, 1);
            int[] opts = fields.Select((_, i) => optsV is AsArray oa ? Rt.ToInt32(i < oa.Items.Count ? oa.Items[i] : 0) : Rt.ToInt32(optsV)).ToArray();
            Comparison<object> cmp = (x, y) =>
            {
                for (int i = 0; i < fields.Length; i++)
                {
                    var vx = x is AsObject ox ? rt.GetProperty(ox, Name.Public(fields[i])) : Rt.Undef;
                    var vy = y is AsObject oy ? rt.GetProperty(oy, Name.Public(fields[i])) : Rt.Undef;
                    int r = Cmp(opts[i])(vx, vy);
                    if (r != 0) return r;
                }
                return 0;
            };
            var sorted = StableSort(l, cmp);
            if (opts.Length > 0 && (opts[0] & 8) != 0) return New(s, sorted.Select(x => Rt.Box(l.IndexOf(x))));
            l.Clear(); l.AddRange(sorted);
            return s;
        });
    }

    static List<object> StableSort(List<object> l, Comparison<object> cmp) => l.Select((x, i) => (x, i)).OrderBy(p => p, Comparer<(object x, int i)>.Create((p, q) => { int r = cmp(p.x, q.x); return r != 0 ? r : p.i.CompareTo(q.i); })).Select(p => p.x).ToList();

    static Comparison<object> Cmp(int opts)
    {
        bool num = (opts & 16) != 0, ci = (opts & 1) != 0, desc = (opts & 2) != 0;
        return (x, y) =>
        {
            int r;
            if (Rt.IsUndef(x) || Rt.IsUndef(y)) r = Rt.IsUndef(x) ? (Rt.IsUndef(y) ? 0 : 1) : -1;
            else if (num) r = Rt.ToNumber(x).CompareTo(Rt.ToNumber(y));
            else r = ci ? string.Compare(Rt.ToString(x), Rt.ToString(y), StringComparison.OrdinalIgnoreCase) : string.CompareOrdinal(Rt.ToString(x), Rt.ToString(y));
            return desc ? -r : r;
        };
    }

    static int Rel(double v, int n) => double.IsNaN(v) ? 0 : v < 0 ? (int)Math.Max(0, n + v) : (int)Math.Min(v, n);

    // ---- String ---------------------------------------------------------------------------------------------

    static void InstallString(Runtime rt)
    {
        var str = rt.StringClass = rt.NewNativeClass("String", rt.ObjectClass);
        str.IsFinal = true;
        str.CallAsFunction = a => a.Length == 0 ? "" : Rt.ToString(a[0]);
        var t = str.InstanceTraits;
        string S(object s) => s is string x ? x : Rt.ToString(s);
        Runtime.Accessor(t, "length", s => Rt.Box(S(s).Length));
        Runtime.Method(t, "charAt", (s, a) => { var x = S(s); int i = Rt.ToInt32(A(a, 0)); return i >= 0 && i < x.Length ? x[i].ToString() : ""; });
        Runtime.Method(t, "charCodeAt", (s, a) => { var x = S(s); int i = Rt.ToInt32(A(a, 0)); return i >= 0 && i < x.Length ? Rt.Box(x[i]) : double.NaN; });
        Runtime.Method(t, "indexOf", (s, a) => { var x = S(s); int st = Math.Clamp(Rt.ToInt32(A(a, 1)), 0, x.Length); return Rt.Box(x.IndexOf(Rt.ToString(A(a, 0)), st, StringComparison.Ordinal)); });
        Runtime.Method(t, "lastIndexOf", (s, a) => { var x = S(s); var sub = Rt.ToString(A(a, 0)); int st = a.Length > 1 ? Math.Clamp(Rt.ToInt32(a[1]), 0, x.Length) : x.Length; int from = Math.Min(st + sub.Length - 1, x.Length - 1); return Rt.Box(from < 0 ? (sub.Length == 0 ? 0 : -1) : x.LastIndexOf(sub, from, StringComparison.Ordinal)); });
        Runtime.Method(t, "substr", (s, a) =>
        {
            var x = S(s); int st = Rel(Rt.ToNumber(A(a, 0)), x.Length);
            int len = Rt.IsUndef(A(a, 1)) ? x.Length - st : Math.Clamp(Rt.ToInt32(a[1]), 0, x.Length - st);
            return x.Substring(st, len);
        });
        Runtime.Method(t, "substring", (s, a) =>
        {
            var x = S(s);
            int st = Math.Clamp(Rt.ToInt32(A(a, 0)), 0, x.Length), en = Rt.IsUndef(A(a, 1)) ? x.Length : Math.Clamp(Rt.ToInt32(a[1]), 0, x.Length);
            if (st > en) (st, en) = (en, st);
            return x[st..en];
        });
        Runtime.Method(t, "slice", (s, a) =>
        {
            var x = S(s); int st = Rel(Rt.ToNumber(A(a, 0)), x.Length), en = Rt.IsUndef(A(a, 1)) ? x.Length : Rel(Rt.ToNumber(a[1]), x.Length);
            return en > st ? x[st..en] : "";
        });
        Runtime.Method(t, "toUpperCase", (s, a) => S(s).ToUpperInvariant());
        Runtime.Method(t, "toLowerCase", (s, a) => S(s).ToLowerInvariant());
        Runtime.Method(t, "toLocaleUpperCase", (s, a) => S(s).ToUpperInvariant());
        Runtime.Method(t, "toLocaleLowerCase", (s, a) => S(s).ToLowerInvariant());
        Runtime.Method(t, "toString", (s, a) => S(s));
        Runtime.Method(t, "valueOf", (s, a) => S(s));
        Runtime.Method(t, "concat", (s, a) => S(s) + string.Concat(a.Select(Rt.ToString)));
        Runtime.Method(t, "localeCompare", (s, a) => Rt.Box(Math.Sign(string.Compare(S(s), Rt.ToString(A(a, 0)), StringComparison.CurrentCulture))));
        Runtime.Method(t, "split", (s, a) =>
        {
            var x = S(s); var sep = A(a, 0);
            int lim = Rt.IsUndef(A(a, 1)) ? int.MaxValue : Rt.ToInt32(a[1]);
            IEnumerable<string> parts;
            if (Rt.IsUndef(sep)) parts = new[] { x };
            else if (sep is AsRegExp re) parts = re.Regex.Split(x);
            else { var ss = Rt.ToString(sep); parts = ss.Length == 0 ? x.Select(ch => ch.ToString()) : x.Split(ss); }
            return new AsArray(rt, parts.Take(lim).Select(p => (object)p));
        });
        Runtime.Method(t, "replace", (s, a) =>
        {
            var x = S(s); var pat = A(a, 0); var rep = A(a, 1);
            string Repl(Match m)
            {
                if (rep is AsFunction f)
                {
                    var args = new List<object> { m.Value };
                    for (int g = 1; g < m.Groups.Count; g++) args.Add(m.Groups[g].Value);
                    args.Add(Rt.Box(m.Index)); args.Add(x);
                    return Rt.ToString(f.Call(null, args.ToArray()));
                }
                var r = Rt.ToString(rep);
                return Regex.Replace(r, @"\$(\$|&|\d{1,2})", mm => mm.Groups[1].Value == "$" ? "$" : mm.Groups[1].Value == "&" ? m.Value : int.TryParse(mm.Groups[1].Value, out var gi) && gi < m.Groups.Count ? m.Groups[gi].Value : mm.Value);
            }
            if (pat is AsRegExp re) return re.Global ? re.Regex.Replace(x, Repl) : re.Regex.Replace(x, Repl, 1);
            var ps = Rt.ToString(pat); int idx = x.IndexOf(ps, StringComparison.Ordinal);
            if (idx < 0) return x;
            var mm2 = Regex.Match(x[idx..], Regex.Escape(ps));
            return x[..idx] + Repl(mm2) + x[(idx + ps.Length)..];
        });
        Runtime.Method(t, "match", (s, a) =>
        {
            var x = S(s); var re = A(a, 0) as AsRegExp ?? new AsRegExp(rt, Regex.Escape(Rt.ToString(A(a, 0))), "");
            if (re.Global) { var ms = re.Regex.Matches(x); return ms.Count == 0 ? null : new AsArray(rt, ms.Select(m => (object)m.Value)); }
            var m1 = re.Regex.Match(x); return m1.Success ? AsRegExp.MatchArray(rt, m1, x) : null;
        });
        Runtime.Method(t, "search", (s, a) => { var x = S(s); var re = A(a, 0) as AsRegExp ?? new AsRegExp(rt, Regex.Escape(Rt.ToString(A(a, 0))), ""); var m = re.Regex.Match(x); return Rt.Box(m.Success ? m.Index : -1); });
        Runtime.Method(str.Traits, "fromCharCode", (s, a) => new string(a.Select(c => (char)Rt.ToInt32(c)).ToArray()));
    }

    // ---- numbers ----------------------------------------------------------------------------------------------

    static void InstallNumbers(Runtime rt)
    {
        var num = rt.NumberClass = rt.NewNativeClass("Number", rt.ObjectClass);
        var i32 = rt.IntClass = rt.NewNativeClass("int", rt.ObjectClass);
        var u32 = rt.UintClass = rt.NewNativeClass("uint", rt.ObjectClass);
        var bo = rt.BooleanClass = rt.NewNativeClass("Boolean", rt.ObjectClass);
        num.CallAsFunction = a => a.Length == 0 ? Rt.Box(0) : Rt.Num(Rt.ToNumber(a[0]));
        i32.CallAsFunction = a => Rt.Box(a.Length == 0 ? 0 : Rt.ToInt32(a[0]));
        u32.CallAsFunction = a => Rt.Uint(a.Length == 0 ? 0 : Rt.ToUint32(a[0]));
        bo.CallAsFunction = a => Rt.Box(a.Length > 0 && Rt.ToBoolean(a[0]));
        Runtime.Const(num, "MAX_VALUE", double.MaxValue); Runtime.Const(num, "MIN_VALUE", double.Epsilon); Runtime.Const(num, "NaN", double.NaN);
        Runtime.Const(num, "POSITIVE_INFINITY", double.PositiveInfinity); Runtime.Const(num, "NEGATIVE_INFINITY", double.NegativeInfinity);
        Runtime.Const(i32, "MAX_VALUE", Rt.Box(int.MaxValue)); Runtime.Const(i32, "MIN_VALUE", Rt.Box(int.MinValue));
        Runtime.Const(u32, "MAX_VALUE", (double)uint.MaxValue); Runtime.Const(u32, "MIN_VALUE", Rt.Box(0));
        foreach (var c in new[] { num, i32, u32 })
        {
            var t = c.InstanceTraits;
            Runtime.Method(t, "toString", (s, a) =>
            {
                int radix = Rt.IsUndef(A(a, 0)) ? 10 : Rt.ToInt32(a[0]);
                double d = Rt.ToNumber(s);
                if (radix == 10 || double.IsNaN(d) || double.IsInfinity(d)) return Rt.NumberToString(d);
                long l = (long)d; bool neg = l < 0; l = Math.Abs(l);
                var sb = new StringBuilder(); do { sb.Insert(0, "0123456789abcdefghijklmnopqrstuvwxyz"[(int)(l % radix)]); l /= radix; } while (l > 0);
                return (neg ? "-" : "") + sb;
            });
            Runtime.Method(t, "toFixed", (s, a) => Rt.ToNumber(s).ToString("F" + Math.Clamp(Rt.ToInt32(A(a, 0)), 0, 20), CultureInfo.InvariantCulture));
            Runtime.Method(t, "toPrecision", (s, a) => Rt.IsUndef(A(a, 0)) ? Rt.NumberToString(Rt.ToNumber(s)) : Rt.ToNumber(s).ToString("G" + Math.Clamp(Rt.ToInt32(a[0]), 1, 21), CultureInfo.InvariantCulture));
            Runtime.Method(t, "toExponential", (s, a) => Rt.ToNumber(s).ToString("e" + Math.Clamp(Rt.ToInt32(A(a, 0)), 0, 20), CultureInfo.InvariantCulture));
            Runtime.Method(t, "valueOf", (s, a) => s);
        }
        Runtime.Method(bo.InstanceTraits, "toString", (s, a) => Rt.ToBoolean(s) ? "true" : "false");
        Runtime.Method(bo.InstanceTraits, "valueOf", (s, a) => Rt.Box(Rt.ToBoolean(s)));
    }

    static void InstallMath(Runtime rt)
    {
        var m = rt.NewNativeClass("Math", rt.ObjectClass);
        foreach (var (k, v) in new[] { ("PI", Math.PI), ("E", Math.E), ("LN10", Math.Log(10)), ("LN2", Math.Log(2)), ("LOG10E", Math.Log10(Math.E)), ("LOG2E", Math.Log2(Math.E)), ("SQRT1_2", Math.Sqrt(0.5)), ("SQRT2", Math.Sqrt(2)) })
            Runtime.Const(m, k, v);
        void F(string n, Func<double, double> f) => Runtime.Method(m.Traits, n, (s, a) => Rt.Num(f(Rt.ToNumber(A(a, 0)))));
        F("abs", Math.Abs); F("ceil", Math.Ceiling); F("floor", Math.Floor); F("round", x => Math.Floor(x + 0.5)); F("sqrt", Math.Sqrt);
        F("sin", Math.Sin); F("cos", Math.Cos); F("tan", Math.Tan); F("asin", Math.Asin); F("acos", Math.Acos); F("atan", Math.Atan);
        F("exp", Math.Exp); F("log", Math.Log);
        Runtime.Method(m.Traits, "atan2", (s, a) => Rt.Num(Math.Atan2(Rt.ToNumber(A(a, 0)), Rt.ToNumber(A(a, 1)))));
        Runtime.Method(m.Traits, "pow", (s, a) => Rt.Num(Math.Pow(Rt.ToNumber(A(a, 0)), Rt.ToNumber(A(a, 1)))));
        Runtime.Method(m.Traits, "max", (s, a) => Rt.Num(a.Length == 0 ? double.NegativeInfinity : a.Select(Rt.ToNumber).Aggregate((x, y) => double.IsNaN(x) || double.IsNaN(y) ? double.NaN : Math.Max(x, y))));
        Runtime.Method(m.Traits, "min", (s, a) => Rt.Num(a.Length == 0 ? double.PositiveInfinity : a.Select(Rt.ToNumber).Aggregate((x, y) => double.IsNaN(x) || double.IsNaN(y) ? double.NaN : Math.Min(x, y))));
        var rnd = new Random(12345);
        Runtime.Method(m.Traits, "random", (s, a) => rnd.NextDouble());
    }

    // ---- errors -------------------------------------------------------------------------------------------------

    static void InstallErrors(Runtime rt)
    {
        var err = rt.ErrorClass = rt.NewNativeClass("Error", rt.ObjectClass, null, true);
        err.IsErrorClass = true;
        err.InstanceTraits.AddSlot(Ns.Public, "message", 0, "", "String", false);
        err.InstanceTraits.AddSlot(Ns.Public, "name", 0, "Error", "String", false);
        var idB = err.InstanceTraits.AddSlot(Ns.Public, "__errorID", 0, Rt.Box(0), "int", false);
        err.Ctor = new NativeMethod("Error", (s, a) =>
        {
            var o = (AsObject)s;
            o.SetPublic("message", Rt.IsUndef(A(a, 0)) ? "" : Rt.ToString(a[0]));
            o.SetPublic("name", o.Class?.LocalName ?? "Error");
            o.Slots[idB.Slot] = Rt.Box(Rt.ToInt32(A(a, 1)));
            return Rt.Undef;
        });
        Runtime.Accessor(err.InstanceTraits, "errorID", s => ((AsObject)s).Slots[idB.Slot]);
        Runtime.Method(err.InstanceTraits, "getStackTrace", (s, a) => null);
        Runtime.Method(err.InstanceTraits, "toString", (s, a) => { var o = (AsObject)s; var msg = Rt.ToString(o.GetPublic("message")); return Rt.ToString(o.GetPublic("name")) + (msg.Length > 0 ? ": " + msg : ""); });
        foreach (var n in new[] { "TypeError", "ReferenceError", "ArgumentError", "RangeError", "SecurityError", "EvalError", "SyntaxError", "URIError", "VerifyError", "DefinitionError", "UninitializedError" })
            rt.NewNativeClass(n, err, null, true);
        foreach (var n in new[] { "flash.errors.IOError", "flash.errors.IllegalOperationError", "flash.errors.EOFError", "flash.errors.MemoryError", "flash.errors.ScriptTimeoutError", "flash.errors.StackOverflowError", "flash.errors.InvalidSWFError" })
            rt.NewNativeClass(n, err, null, true);
    }

    // ---- RegExp ----------------------------------------------------------------------------------------------

    static void InstallRegExp(Runtime rt)
    {
        var re = rt.NewNativeClass("RegExp", rt.ObjectClass, c => new AsRegExp(), true);
        re.Ctor = new NativeMethod("RegExp", (s, a) => { ((AsRegExp)s).Set(A(a, 0) is AsRegExp r ? r.Source : Rt.IsUndef(A(a, 0)) ? "" : Rt.ToString(a[0]), Rt.IsUndef(A(a, 1)) ? "" : Rt.ToString(a[1])); return Rt.Undef; });
        var t = re.InstanceTraits;
        Runtime.Method(t, "test", (s, a) => Rt.Box(((AsRegExp)s).Regex.IsMatch(Rt.ToString(A(a, 0)))));
        Runtime.Method(t, "exec", (s, a) =>
        {
            var r = (AsRegExp)s; var x = Rt.ToString(A(a, 0));
            int st = r.Global ? r.LastIndex : 0;
            if (st > x.Length) { r.LastIndex = 0; return null; }
            var m = r.Regex.Match(x, st);
            if (!m.Success) { r.LastIndex = 0; return null; }
            if (r.Global) r.LastIndex = m.Index + Math.Max(1, m.Length);
            return AsRegExp.MatchArray(rt, m, x);
        });
        Runtime.Accessor(t, "source", s => ((AsRegExp)s).Source);
        Runtime.Accessor(t, "global", s => Rt.Box(((AsRegExp)s).Global));
        Runtime.Accessor(t, "lastIndex", s => Rt.Box(((AsRegExp)s).LastIndex), (s, v) => ((AsRegExp)s).LastIndex = Rt.ToInt32(v));
    }

    // ---- Date --------------------------------------------------------------------------------------------------

    static void InstallDate(Runtime rt)
    {
        var dc = rt.NewNativeClass("Date", rt.ObjectClass, c => new AsDate(), true);
        dc.Ctor = new NativeMethod("Date", (s, a) =>
        {
            var d = (AsDate)s;
            if (a.Length == 0) d.Ms = rt.WallClockMs();
            else if (a.Length == 1) d.Ms = a[0] is string str && DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? AsDate.FromLocal(dt) : Rt.ToNumber(a[0]);
            else
            {
                int Y = Rt.ToInt32(a[0]), M = Rt.ToInt32(A(a, 1)), D = a.Length > 2 ? Rt.ToInt32(a[2]) : 1, h = Rt.ToInt32(A(a, 3)), mi = Rt.ToInt32(A(a, 4)), se = Rt.ToInt32(A(a, 5)), ms = Rt.ToInt32(A(a, 6));
                d.Ms = AsDate.FromLocal(new DateTime(Y, 1, 1).AddMonths(M).AddDays(D - 1).AddHours(h).AddMinutes(mi).AddSeconds(se).AddMilliseconds(ms));
            }
            return Rt.Undef;
        });
        dc.CallAsFunction = a => new AsDate { Ms = rt.WallClockMs() }.Local.ToString("ddd MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture);
        var t = dc.InstanceTraits;
        AsDate D0(object s) => (AsDate)s;
        void G(string n, Func<DateTime, double> f, bool utc = false)
        {
            Runtime.Method(t, "get" + n, (s, a) => Rt.Num(f(utc ? D0(s).Utc : D0(s).Local)));
            var prop = char.ToLowerInvariant(n[0]) + n[1..];
            Runtime.Accessor(t, prop, s => Rt.Num(f(utc ? D0(s).Utc : D0(s).Local)));
        }
        G("FullYear", d => d.Year); G("Month", d => d.Month - 1); G("Date", d => d.Day); G("Day", d => (int)d.DayOfWeek);
        G("Hours", d => d.Hour); G("Minutes", d => d.Minute); G("Seconds", d => d.Second); G("Milliseconds", d => d.Millisecond);
        G("UTCFullYear", d => d.Year, true); G("UTCMonth", d => d.Month - 1, true); G("UTCDate", d => d.Day, true); G("UTCDay", d => (int)d.DayOfWeek, true);
        G("UTCHours", d => d.Hour, true); G("UTCMinutes", d => d.Minute, true); G("UTCSeconds", d => d.Second, true);
        Runtime.Method(t, "getTime", (s, a) => Rt.Num(D0(s).Ms));
        Runtime.Method(t, "valueOf", (s, a) => Rt.Num(D0(s).Ms));
        Runtime.Accessor(t, "time", s => Rt.Num(D0(s).Ms), (s, v) => D0(s).Ms = Rt.ToNumber(v));
        Runtime.Method(t, "setTime", (s, a) => { D0(s).Ms = Rt.ToNumber(A(a, 0)); return Rt.Num(D0(s).Ms); });
        Runtime.Method(t, "getTimezoneOffset", (s, a) => Rt.Num(-TimeZoneInfo.Local.GetUtcOffset(D0(s).Utc).TotalMinutes));
        void S(string n, Func<DateTime, object[], DateTime> f) => Runtime.Method(t, "set" + n, (s, a) => { var d = D0(s); d.Ms = AsDate.FromLocal(f(d.Local, a)); return Rt.Num(d.Ms); });
        S("FullYear", (d, a) => d.AddYears(Rt.ToInt32(A(a, 0)) - d.Year)); S("Month", (d, a) => d.AddMonths(Rt.ToInt32(A(a, 0)) - (d.Month - 1)));
        S("Date", (d, a) => d.AddDays(Rt.ToInt32(A(a, 0)) - d.Day)); S("Hours", (d, a) => d.AddHours(Rt.ToInt32(A(a, 0)) - d.Hour));
        S("Minutes", (d, a) => d.AddMinutes(Rt.ToInt32(A(a, 0)) - d.Minute)); S("Seconds", (d, a) => d.AddSeconds(Rt.ToInt32(A(a, 0)) - d.Second));
        S("Milliseconds", (d, a) => d.AddMilliseconds(Rt.ToInt32(A(a, 0)) - d.Millisecond));
        Runtime.Method(t, "toString", (s, a) => D0(s).Local.ToString("ddd MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture));
        Runtime.Method(dc.Traits, "UTC", (s, a) => Rt.Num((new DateTime(Rt.ToInt32(A(a, 0)), 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(Rt.ToInt32(A(a, 1))).AddDays(a.Length > 2 ? Rt.ToInt32(a[2]) - 1 : 0) - DateTime.UnixEpoch).TotalMilliseconds));
    }

    // ---- globals -------------------------------------------------------------------------------------------------

    static void InstallGlobals(Runtime rt)
    {
        rt.DefineGlobal("NaN", double.NaN); rt.DefineGlobal("Infinity", double.PositiveInfinity); rt.DefineGlobal("undefined", Rt.Undef);
        rt.Function("trace", a => { rt.Trace(string.Join(" ", a.Select(Rt.ToString))); return Rt.Undef; });
        rt.Function("isNaN", a => Rt.Box(double.IsNaN(Rt.ToNumber(A(a, 0)))));
        rt.Function("isFinite", a => Rt.Box(double.IsFinite(Rt.ToNumber(A(a, 0)))));
        rt.Function("parseFloat", a =>
        {
            var s = Rt.ToString(A(a, 0)).TrimStart();
            var m = Regex.Match(s, @"^[+-]?(Infinity|\d+\.?\d*([eE][+-]?\d+)?|\.\d+([eE][+-]?\d+)?)");
            return m.Success ? Rt.Num(m.Value.EndsWith("Infinity") ? (m.Value.StartsWith("-") ? double.NegativeInfinity : double.PositiveInfinity) : double.Parse(m.Value, CultureInfo.InvariantCulture)) : double.NaN;
        });
        rt.Function("parseInt", a =>
        {
            var s = Rt.ToString(A(a, 0)).Trim(); int radix = Rt.ToInt32(A(a, 1));
            bool neg = s.StartsWith("-"); if (s.StartsWith("-") || s.StartsWith("+")) s = s[1..];
            if ((radix == 0 || radix == 16) && s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) { s = s[2..]; radix = 16; }
            if (radix == 0) radix = 10;
            double v = 0; int n = 0;
            foreach (var ch in s)
            {
                int d = ch >= '0' && ch <= '9' ? ch - '0' : ch >= 'a' && ch <= 'z' ? ch - 'a' + 10 : ch >= 'A' && ch <= 'Z' ? ch - 'A' + 10 : 99;
                if (d >= radix) break;
                v = v * radix + d; n++;
            }
            return n == 0 ? double.NaN : Rt.Num(neg ? -v : v);
        });
        rt.Function("escape", a => Uri.EscapeDataString(Rt.ToString(A(a, 0))));
        rt.Function("unescape", a => Uri.UnescapeDataString(Rt.ToString(A(a, 0))));
        rt.Function("encodeURIComponent", a => Uri.EscapeDataString(Rt.ToString(A(a, 0))));
        rt.Function("decodeURIComponent", a => Uri.UnescapeDataString(Rt.ToString(A(a, 0))));
        rt.Function("encodeURI", a => Uri.EscapeUriString(Rt.ToString(A(a, 0))));
        rt.Function("decodeURI", a => Uri.UnescapeDataString(Rt.ToString(A(a, 0))));
        rt.Function("isXMLName", a => Rt.False);

        var dict = rt.NewNativeClass("flash.utils.Dictionary", rt.ObjectClass, c => new AsDictionary(), true);
        dict.Ctor = new NativeMethod("Dictionary", (s, a) => Rt.Undef);
        rt.Function("flash.utils.getQualifiedClassName", a => QualifiedName(rt, A(a, 0)));
        rt.Function("flash.utils.getQualifiedSuperclassName", a => { var c = A(a, 0) as AsClass ?? rt.ClassOf(A(a, 0)); return c?.Super == null ? null : QName(c.Super); });
        rt.Function("flash.utils.getDefinitionByName", a =>
        {
            var n = Rt.ToString(A(a, 0)).Replace("::", ".");
            var v = rt.GetGlobal(n);
            // Scaleform's getDefinitionByName gives undefined for an unknown class (no #1065 as in Flash); the movies
            // rely on it (MinimapDialogMax asks for a symbol no movie exports). Noted as a data gap, not a missing API.
            if (Rt.IsUndef(v)) rt.NoteMissing("data: definition " + n);
            return v;
        });
        rt.Function("flash.utils.getTimer", a => Rt.Box((int)rt.TimerMs));
        rt.Function("flash.utils.describeType", a => { rt.NoteMissing("flash.utils.describeType"); return null; });
        rt.Function("flash.utils.setTimeout", a => Rt.Box(rt.AddTimeout(A(a, 0) as AsFunction, Rt.ToNumber(A(a, 1)), a.Length > 2 ? a[2..] : Array.Empty<object>(), false)));
        rt.Function("flash.utils.setInterval", a => Rt.Box(rt.AddTimeout(A(a, 0) as AsFunction, Rt.ToNumber(A(a, 1)), a.Length > 2 ? a[2..] : Array.Empty<object>(), true)));
        rt.Function("flash.utils.clearTimeout", a => { rt.ClearTimeout(Rt.ToInt32(A(a, 0))); return Rt.Undef; });
        rt.Function("flash.utils.clearInterval", a => { rt.ClearTimeout(Rt.ToInt32(A(a, 0))); return Rt.Undef; });
    }

    static string QName(AsClass c)
    {
        int dot = c.Name.LastIndexOf('.');
        return dot < 0 ? c.Name : c.Name[..dot] + "::" + c.Name[(dot + 1)..];
    }

    static string QualifiedName(Runtime rt, object v)
    {
        if (v == null) return "null";
        if (Rt.IsUndef(v)) return "void";
        if (v is int) return "int";
        if (v is double d) return d == Math.Truncate(d) && d >= int.MinValue && d <= int.MaxValue ? "int" : "Number";
        var c = v as AsClass ?? rt.ClassOf(v);
        return c == null ? "Object" : QName(c);
    }
}

public sealed class AsRegExp : AsObject
{
    public Regex Regex; public string Source; public bool Global; public int LastIndex;
    public AsRegExp() { }
    public AsRegExp(Runtime rt, string src, string flags) { Init(rt.GetClass("RegExp")); Set(src, flags); }
    public void Set(string src, string flags)
    {
        Source = src; Global = flags.Contains('g');
        var o = RegexOptions.ECMAScript;
        if (flags.Contains('i')) o |= RegexOptions.IgnoreCase;
        if (flags.Contains('m')) o |= RegexOptions.Multiline;
        if (flags.Contains('s') || flags.Contains('x')) o &= ~RegexOptions.ECMAScript;
        if (flags.Contains('s')) o |= RegexOptions.Singleline;
        if (flags.Contains('x')) o |= RegexOptions.IgnorePatternWhitespace;
        try { Regex = new Regex(src, o); } catch { Regex = new Regex(src, o & ~RegexOptions.ECMAScript); }
    }
    public static AsArray MatchArray(Runtime rt, Match m, string input)
    {
        var a = new AsArray(rt, m.Groups.Cast<Group>().Select(g => g.Success ? (object)g.Value : Rt.Undef));
        a.SetDynamic("index", Rt.Box(m.Index)); a.SetDynamic("input", input);
        return a;
    }
}

public sealed class AsDate : AsObject
{
    public double Ms;
    public DateTime Utc => DateTime.UnixEpoch.AddMilliseconds(double.IsNaN(Ms) ? 0 : Ms);
    public DateTime Local => Utc.ToLocalTime();
    public static double FromLocal(DateTime local) => (DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime() - DateTime.UnixEpoch).TotalMilliseconds;
    public override object ToPrimitive(bool numberHint) => numberHint ? Rt.Num(Ms) : Local.ToString("ddd MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture);
}
