using System.Reflection;

namespace Ms2.Avm2;

/// <summary>
/// The call of one [As] member of a flash class without <see cref="MethodBase.Invoke(object, object[])"/>. Reflection
/// compiles an invoke stub per method from its second call on: one JIT-compiled method for every native member a session
/// uses (114 by the end of the field load, 182 more over the perf gate's dialog opens). Here a member is called through a
/// typed open delegate, held by one of a few generic adapters picked by the member's shape (static or not, void or not,
/// 0 to 3 parameters). Their type arguments are the member's own types; for reference types the runtime shares one copy
/// of the adapter's code, which <see cref="Shapes"/> has precompiled for the shapes the flash classes use. A member of any
/// other shape, and a call whose argument is not of its parameter's exact type, goes through
/// <see cref="MethodBase.Invoke(object, object[])"/> as before.
/// </summary>
abstract class NativeCall
{
    MethodInfo method;

    /// <summary>Calls the member: <paramref name="self"/> is checked by the caller (ignored for a static member), and
    /// <paramref name="a"/> holds one argument per parameter, coerced to its type. Returns the result boxed (null for void).
    /// An exception the member throws comes out as itself, as the reflection path's callers unwrap it.</summary>
    public abstract object Call(object self, object[] a);

    protected abstract void Bind(MethodInfo mi);

    /// <summary>False when the member goes through <see cref="MethodBase.Invoke(object, object[])"/> (a shape without an adapter).</summary>
    internal bool Typed => this is not Reflected;

    /// <summary>The reflection call, for an argument the typed delegate can't take (the exception unwrapped).</summary>
    protected object Slow(object self, object[] a)
    {
        try { return method.Invoke(method.IsStatic ? null : self, a); }
        catch (TargetInvocationException e) when (e.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            throw;
        }
    }

    /// <summary>An argument as the parameter's type: false for a value of another type, or null for a value type.</summary>
    protected static bool Arg<T>(object o, out T v)
    {
        if (o is T t) { v = t; return true; }
        v = default;
        return o == null && !typeof(T).IsValueType;
    }

    static readonly Dictionary<MethodInfo, NativeCall> Made = new();

    /// <summary>The member's call, made once per process.</summary>
    public static NativeCall Of(MethodInfo mi)
    {
        lock (Made)
        {
            if (!Made.TryGetValue(mi, out var c)) Made[mi] = c = Make(mi);
            return c;
        }
    }

    static NativeCall Make(MethodInfo mi)
    {
        var ps = mi.GetParameters();
        bool isVoid = mi.ReturnType == typeof(void);
        Type def = null;
        if (ps.Length <= 3 && !mi.IsGenericMethodDefinition && !mi.DeclaringType.IsValueType && !mi.DeclaringType.ContainsGenericParameters
            && !mi.ReturnType.IsByRef && !mi.ReturnType.IsPointer && !mi.ReturnType.IsByRefLike
            && ps.All(p => !p.ParameterType.IsByRef && !p.ParameterType.IsPointer && !p.ParameterType.IsByRefLike))
        {
            def = (mi.IsStatic, isVoid, ps.Length) switch
            {
                (false, true, 0) => typeof(I0V<>), (false, false, 0) => typeof(I0<,>),
                (false, true, 1) => typeof(I1V<,>), (false, false, 1) => typeof(I1<,,>),
                (false, true, 2) => typeof(I2V<,,>), (false, false, 2) => typeof(I2<,,,>),
                (false, true, 3) => typeof(I3V<,,,>), (false, false, 3) => typeof(I3<,,,,>),
                (true, true, 0) => typeof(S0V), (true, false, 0) => typeof(S0<>),
                (true, true, 1) => typeof(S1V<>), (true, false, 1) => typeof(S1<,>),
                (true, true, 2) => typeof(S2V<,>), (true, false, 2) => typeof(S2<,,>),
                _ => null,
            };
        }
        NativeCall c = null;
        if (def != null)
        {
            var args = new List<Type>(5);
            if (!mi.IsStatic) args.Add(mi.DeclaringType);
            if (!isVoid) args.Add(mi.ReturnType);
            foreach (var p in ps) args.Add(p.ParameterType);
            try
            {
                c = (NativeCall)Activator.CreateInstance(def.IsGenericTypeDefinition ? def.MakeGenericType(args.ToArray()) : def);
                c.Bind(mi);
            }
            catch (ArgumentException) { c = null; }   // a type argument against a constraint, or a delegate the member can't bind to
        }
        c ??= new Reflected();
        c.method = mi;
        return c;
    }

    /// <summary>
    /// Every shape the flash classes' members have, with object for a reference type: crossgen2 compiles these
    /// instantiations' shared code (Bind and Call, reached through <see cref="Pre"/>) into this library's image, which every
    /// member of that shape then runs. NativeCallTests checks that it lists each member's shape.
    /// </summary>
    internal static NativeCall[] Shapes() => new NativeCall[]
    {
        Pre(new I0<object, bool>()),
        Pre(new I0<object, double>()),
        Pre(new I0<object, int>()),
        Pre(new I0<object, object>()),
        Pre(new I0<object, uint>()),
        Pre(new I0V<object>()),
        Pre(new I1<object, bool, object>()),
        Pre(new I1<object, double, object>()),
        Pre(new I1<object, int, int>()),
        Pre(new I1<object, int, object>()),
        Pre(new I1<object, object, int>()),
        Pre(new I1<object, object, object>()),
        Pre(new I1V<object, bool>()),
        Pre(new I1V<object, double>()),
        Pre(new I1V<object, int>()),
        Pre(new I1V<object, object>()),
        Pre(new I1V<object, uint>()),
        Pre(new I2<object, bool, double, double>()),
        Pre(new I2<object, bool, object, bool>()),
        Pre(new I2<object, bool, object, object>()),
        Pre(new I2<object, int, double, double>()),
        Pre(new I2<object, object, int, int>()),
        Pre(new I2<object, object, object, int>()),
        Pre(new I2<object, object, object, object>()),
        Pre(new I2<object, uint, int, int>()),
        Pre(new I2V<object, bool, object>()),
        Pre(new I2V<object, double, double>()),
        Pre(new I2V<object, int, int>()),
        Pre(new I2V<object, object, double>()),
        Pre(new I2V<object, object, int>()),
        Pre(new I2V<object, object, object>()),
        Pre(new I2V<object, uint, double>()),
        Pre(new I3<object, bool, double, double, bool>()),
        Pre(new I3<object, bool, object, double, bool>()),
        Pre(new I3<object, object, double, int, object>()),
        Pre(new I3V<object, double, double, double>()),
        Pre(new I3V<object, double, object, object>()),
        Pre(new I3V<object, int, bool, object>()),
        Pre(new I3V<object, int, int, object>()),
        Pre(new I3V<object, int, int, uint>()),
        Pre(new I3V<object, object, int, int>()),
        Pre(new I3V<object, object, object, bool>()),
        Pre(new I3V<object, object, object, object>()),
        Pre(new S0<bool>()),
        Pre(new S0<double>()),
        Pre(new S0<int>()),
        Pre(new S0<object>()),
        Pre(new S0<uint>()),
        Pre(new S0V()),
        Pre(new S1<object, bool>()),
        Pre(new S1V<bool>()),
        Pre(new S1V<double>()),
        Pre(new S1V<object>()),
        Pre(new S1V<uint>()),
        Pre(new S2<double, object, object>()),
        Pre(new S2<object, double, double>()),
        Pre(new S2<object, object, object>()),
        Pre(new S2V<int, int>()),
        Pre(new S2V<object, object>())
    };

    static T Pre<T>(T c, bool compile = false) where T : NativeCall
    {
        if (compile) { c.Bind(null); c.Call(null, null); }
        return c;
    }

    sealed class Reflected : NativeCall
    {
        protected override void Bind(MethodInfo mi) { }
        public override object Call(object self, object[] a) => Slow(self, a);
    }

    sealed class I0<TS, TR> : NativeCall where TS : class
    {
        Func<TS, TR> f;
        protected override void Bind(MethodInfo mi) => f = mi?.CreateDelegate<Func<TS, TR>>();
        public override object Call(object self, object[] a) => f((TS)self);
    }

    sealed class I0V<TS> : NativeCall where TS : class
    {
        Action<TS> f;
        protected override void Bind(MethodInfo mi) => f = mi?.CreateDelegate<Action<TS>>();
        public override object Call(object self, object[] a) { f((TS)self); return null; }
    }

    sealed class I1<TS, TR, T1> : NativeCall where TS : class
    {
        Func<TS, T1, TR> f;
        protected override void Bind(MethodInfo mi) => f = mi?.CreateDelegate<Func<TS, T1, TR>>();
        public override object Call(object self, object[] a) => Arg(a[0], out T1 x) ? f((TS)self, x) : Slow(self, a);
    }

    sealed class I1V<TS, T1> : NativeCall where TS : class
    {
        Action<TS, T1> f;
        protected override void Bind(MethodInfo mi) => f = mi?.CreateDelegate<Action<TS, T1>>();
        public override object Call(object self, object[] a)
        {
            if (!Arg(a[0], out T1 x)) return Slow(self, a);
            f((TS)self, x); return null;
        }
    }

    sealed class I2<TS, TR, T1, T2> : NativeCall where TS : class
    {
        Func<TS, T1, T2, TR> f;
        protected override void Bind(MethodInfo mi) => f = mi?.CreateDelegate<Func<TS, T1, T2, TR>>();
        public override object Call(object self, object[] a) =>
            Arg(a[0], out T1 x) && Arg(a[1], out T2 y) ? f((TS)self, x, y) : Slow(self, a);
    }

    sealed class I2V<TS, T1, T2> : NativeCall where TS : class
    {
        Action<TS, T1, T2> f;
        protected override void Bind(MethodInfo mi) => f = mi?.CreateDelegate<Action<TS, T1, T2>>();
        public override object Call(object self, object[] a)
        {
            if (!Arg(a[0], out T1 x) || !Arg(a[1], out T2 y)) return Slow(self, a);
            f((TS)self, x, y); return null;
        }
    }

    sealed class I3<TS, TR, T1, T2, T3> : NativeCall where TS : class
    {
        Func<TS, T1, T2, T3, TR> f;
        protected override void Bind(MethodInfo mi) => f = mi?.CreateDelegate<Func<TS, T1, T2, T3, TR>>();
        public override object Call(object self, object[] a) =>
            Arg(a[0], out T1 x) && Arg(a[1], out T2 y) && Arg(a[2], out T3 z) ? f((TS)self, x, y, z) : Slow(self, a);
    }

    sealed class I3V<TS, T1, T2, T3> : NativeCall where TS : class
    {
        Action<TS, T1, T2, T3> f;
        protected override void Bind(MethodInfo mi) => f = mi?.CreateDelegate<Action<TS, T1, T2, T3>>();
        public override object Call(object self, object[] a)
        {
            if (!Arg(a[0], out T1 x) || !Arg(a[1], out T2 y) || !Arg(a[2], out T3 z)) return Slow(self, a);
            f((TS)self, x, y, z); return null;
        }
    }

    sealed class S0<TR> : NativeCall
    {
        Func<TR> f;
        protected override void Bind(MethodInfo mi) => f = mi?.CreateDelegate<Func<TR>>();
        public override object Call(object self, object[] a) => f();
    }

    sealed class S0V : NativeCall
    {
        Action f;
        protected override void Bind(MethodInfo mi) => f = mi?.CreateDelegate<Action>();
        public override object Call(object self, object[] a) { f(); return null; }
    }

    sealed class S1<TR, T1> : NativeCall
    {
        Func<T1, TR> f;
        protected override void Bind(MethodInfo mi) => f = mi?.CreateDelegate<Func<T1, TR>>();
        public override object Call(object self, object[] a) => Arg(a[0], out T1 x) ? f(x) : Slow(self, a);
    }

    sealed class S1V<T1> : NativeCall
    {
        Action<T1> f;
        protected override void Bind(MethodInfo mi) => f = mi?.CreateDelegate<Action<T1>>();
        public override object Call(object self, object[] a)
        {
            if (!Arg(a[0], out T1 x)) return Slow(self, a);
            f(x); return null;
        }
    }

    sealed class S2<TR, T1, T2> : NativeCall
    {
        Func<T1, T2, TR> f;
        protected override void Bind(MethodInfo mi) => f = mi?.CreateDelegate<Func<T1, T2, TR>>();
        public override object Call(object self, object[] a) => Arg(a[0], out T1 x) && Arg(a[1], out T2 y) ? f(x, y) : Slow(self, a);
    }

    sealed class S2V<T1, T2> : NativeCall
    {
        Action<T1, T2> f;
        protected override void Bind(MethodInfo mi) => f = mi?.CreateDelegate<Action<T1, T2>>();
        public override object Call(object self, object[] a)
        {
            if (!Arg(a[0], out T1 x) || !Arg(a[1], out T2 y)) return Slow(self, a);
            f(x, y); return null;
        }
    }
}
