using System.Reflection;
using System.Runtime.CompilerServices;

namespace Ms2.Avm2;

/// <summary>Marks a C# class as a native AS3 class (instances are created as this C# type, also for AS subclasses).</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AsClassAttribute : Attribute
{
    public readonly string Name, Super;
    public bool Dynamic, Final;
    public AsClassAttribute(string name, string super = "Object") { Name = name; Super = super; }
}

/// <summary>Exposes a member to AS3 (under its C# name, or the given one).</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field)]
public sealed class AsAttribute : Attribute
{
    public readonly string Name;
    public AsAttribute(string name = null) { Name = name; }
}

/// <summary>The native constructor: <c>void Ctor(object[] args)</c>, run by <c>new</c> and by a subclass's super().</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AsCtorAttribute : Attribute { }

public sealed partial class Runtime
{
    /// <summary>Implementations of AS3 methods declared native in the movies (Scaleform extensions), by "pkg.Class/name".</summary>
    public readonly Dictionary<string, MethodImpl> Natives = new();

    public AsClass NewNativeClass(string qname, AsClass super, Func<AsClass, AsObject> alloc = null, bool dynamic = false)
    {
        int dot = qname.LastIndexOf('.');
        var c = new AsClass
        {
            Runtime = this, Name = qname, LocalName = dot < 0 ? qname : qname[(dot + 1)..], Ns = Ns.Get(NsKind.Public, dot < 0 ? "" : qname[..dot]),
            Super = super, InstancesDynamic = dynamic, IsNative = true,
        };
        c.Class = ClassClass;
        c.InstanceTraits = new TraitTable(super?.InstanceTraits);
        c.Traits = new TraitTable();
        c.Slots = Array.Empty<object>();
        c.Allocator = alloc ?? super?.Allocator;
        c.NativeType = super?.NativeType;
        c.IsErrorClass = super?.IsErrorClass ?? false;
        c.Prototype = new AsObject { Class = ObjectClass, IsPrototypeObject = true, ProtoParent = super?.Prototype };
        c.Prototype.SetDynamic("constructor", c);
        c.Ctor = super?.Ctor;
        DefineGlobal(qname, c);
        return c;
    }

    public static void Method(TraitTable t, string name, Func<object, object[], object> f) =>
        t.Set(new Binding { Ns = Ns.Public, Local = name, Kind = BindingKind.Method, Method = new NativeMethod(name, f) });

    public static void Accessor(TraitTable t, string name, Func<object, object> get, Action<object, object> set = null) =>
        t.Set(new Binding
        {
            Ns = Ns.Public, Local = name, Kind = BindingKind.Accessor,
            Getter = get == null ? null : new NativeMethod("get " + name, (s, a) => get(s)),
            Setter = set == null ? null : new NativeMethod("set " + name, (s, a) => { set(s, a.Length > 0 ? a[0] : Rt.Undef); return Rt.Undef; },
                (s, v) => { set(s, v); return Rt.Undef; }),
        });

    public static void Const(AsClass c, string name, object v)
    {
        var b = c.Traits.AddSlot(Ns.Public, name, 0, v, null, true);
        c.Slots = TraitTable.GrowSlots(c.Slots, c.Traits, b.Slot, v);
    }

    public void Function(string qname, Func<object[], object> f) => DefineGlobal(qname, new AsPlainFunction(this, new NativeMethod(qname, (s, a) => f(a))));

    // ---- reflection binder for [AsClass] types -----------------------------------------------------------------

    /// <summary>Defines a native class from a C# type marked [AsClass]; its base must be defined already.</summary>
    public AsClass Bind(Type t)
    {
        var refl = Reflected.GetValue(t, tt => new Members(tt));
        var at = refl.Class ?? throw new ArgumentException(t.Name + " has no [AsClass]");
        var super = at.Super == null ? null : GetClass(at.Super) ?? throw new InvalidOperationException($"native base {at.Super} of {at.Name} not defined");
        var c = NewNativeClass(at.Name, super, cls => (AsObject)Activator.CreateInstance(t), at.Dynamic);
        c.NativeType = t;
        c.IsFinal = at.Final;
        foreach (var (mi, isCtor, asAt) in refl.Methods)
        {
            if (isCtor)
            {
                var m = mi;
                var baseCtor = super?.Ctor;
                NativeCall ctor = null;
                // as MethodBase.Invoke did: an exception the constructor throws comes out wrapped
                c.Ctor = new NativeMethod(at.Name + "/ctor", (self, a) =>
                {
                    CheckSelf(t, self, at.Name);
                    try { (ctor ??= NativeCall.Of(m)).Call(self, new object[] { a }); }
                    catch (Exception e) when (e is not TargetInvocationException) { throw new TargetInvocationException(e); }
                    return Rt.Undef;
                });
                continue;
            }
            if (asAt == null || mi.IsSpecialName) continue;
            var name = asAt.Name ?? mi.Name;
            var impl = new NativeMethod(at.Name + "/" + name, Caller(t, mi, at.Name, out var one), one);
            var table = mi.IsStatic ? c.Traits : c.InstanceTraits;
            table.Set(new Binding { Ns = Ns.Public, Local = name, Kind = BindingKind.Method, Method = impl });
        }
        foreach (var (pi, asAt) in refl.Props)
        {
            var name = asAt.Name ?? pi.Name;
            var g = pi.GetGetMethod(true); var s = pi.GetSetMethod(true);
            bool isStatic = (g ?? s).IsStatic;
            var b = new Binding { Ns = Ns.Public, Local = name, Kind = BindingKind.Accessor };
            if (g != null) b.Getter = new NativeMethod(at.Name + "/get " + name, Caller(t, g, at.Name, out var gOne), gOne);
            if (s != null) b.Setter = new NativeMethod(at.Name + "/set " + name, Caller(t, s, at.Name, out var sOne), sOne);
            (isStatic ? c.Traits : c.InstanceTraits).Set(b);
        }
        foreach (var (fi, asAt) in refl.Fields)
        {
            Const(c, asAt.Name ?? fi.Name, ToAs(fi.GetValue(null)));
        }
        return c;
    }

    /// <summary>A native type's [AsClass] and [As] members, read once per process (each open binds the flash classes into a new
    /// runtime; reading an attribute again compiles reflection invoke stubs for AttributeUsageAttribute's setters).</summary>
    sealed class Members
    {
        public readonly AsClassAttribute Class;
        public readonly List<(MethodInfo Method, bool IsCtor, AsAttribute As)> Methods = new();
        public readonly List<(PropertyInfo Property, AsAttribute As)> Props = new();
        public readonly List<(FieldInfo Field, AsAttribute As)> Fields = new();
        public Members(Type t)
        {
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            Class = t.GetCustomAttribute<AsClassAttribute>();
            foreach (var mi in t.GetMethods(all)) Methods.Add((mi, mi.GetCustomAttribute<AsCtorAttribute>() != null, mi.GetCustomAttribute<AsAttribute>()));
            foreach (var pi in t.GetProperties(all)) if (pi.GetCustomAttribute<AsAttribute>() is { } a) Props.Add((pi, a));
            foreach (var fi in t.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)) if (fi.GetCustomAttribute<AsAttribute>() is { } a) Fields.Add((fi, a));
        }
    }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Type, Members> Reflected = new();

    /// <summary>A native type's [AsClass], read once per process.</summary>
    internal static AsClassAttribute AsClassOf(Type t) => Reflected.GetValue(t, tt => new Members(tt)).Class;

    void CheckSelf(Type t, object self, string cls)
    {
        if (!t.IsInstanceOfType(self)) throw TypeError($"Error #1034: {cls} method called on {Rt.Describe(self)}.");
    }

    static readonly Dictionary<MethodInfo, bool> RestParams = new();

    /// <summary>The call of a reflected member from AS3 (the arguments coerced to its parameters' types, the result converted).
    /// <paramref name="one"/> is the same call for exactly one value without an argument array (a setter, a listener),
    /// or null for a member <see cref="NativeCall.CallAs1"/> can't take.</summary>
    Func<object, object[], object> Caller(Type t, MethodInfo mi, string cls, out Func<object, object, object> one)
    {
        var ps = mi.GetParameters();
        bool rest;
        // once per process: reading the attribute again (its inherit walk reads AttributeUsageAttribute) compiles reflection stubs
        lock (RestParams)
            if (!RestParams.TryGetValue(mi, out rest))
                RestParams[mi] = rest = ps.Length > 0 && ps[^1].ParameterType == typeof(object[]) && ps[^1].GetCustomAttribute<ParamArrayAttribute>() != null;
        bool isStatic = mi.IsStatic;
        // the typed call (NativeCall.CallAs): every parameter a type FromAs coerces without a box of its own
        bool direct = !rest && ps.Length <= 3 && Array.TrueForAll(ps, p => Direct(p.ParameterType));
        NativeCall native = null;
        Func<object, object[], object> full = (self, a) =>
        {
            if (!isStatic) CheckSelf(t, self, cls);
            native ??= NativeCall.Of(mi);
            if (direct && a.Length >= ps.Length && native.Typed) return native.CallAs(this, self, a);
            var call = ps.Length == 0 ? Array.Empty<object>() : new object[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                if (rest && i == ps.Length - 1) { call[i] = a.Length > i ? a[i..] : Array.Empty<object>(); break; }
                var pt = ps[i].ParameterType;
                if (i < a.Length) call[i] = FromAs(a[i], pt, ps[i].Name);
                else call[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : pt == typeof(object) ? Rt.Undef : pt.IsValueType ? Activator.CreateInstance(pt) : null;
            }
            // the member's exception comes out as itself (NativeCall unwraps the reflection path's)
            var r = native.Call(self, call);
            return mi.ReturnType == typeof(void) ? Rt.Undef : ToAs(r);
        };
        one = null;
        if (direct && ps.Length <= 1)
            one = (self, v) =>
            {
                if (!isStatic) CheckSelf(t, self, cls);
                native ??= NativeCall.Of(mi);
                return native.Typed ? native.CallAs1(this, self, v) : full(self, new[] { v });
            };
        return full;
    }

    /// <summary>A parameter type <see cref="From{T}"/> coerces exactly as <see cref="FromAs"/> does.</summary>
    static bool Direct(Type pt) => !pt.IsValueType || pt == typeof(double) || pt == typeof(int) || pt == typeof(uint) || pt == typeof(bool) || pt == typeof(float);

    /// <summary><see cref="FromAs"/> for a parameter of type <typeparamref name="T"/>, without boxing a value type
    /// (only for a type <see cref="Direct"/> takes).</summary>
    internal T From<T>(object v, string pname)
    {
        if (typeof(T) == typeof(double)) { double d = Rt.ToNumber(v); return Unsafe.As<double, T>(ref d); }
        if (typeof(T) == typeof(int)) { int i = Rt.ToInt32(v); return Unsafe.As<int, T>(ref i); }
        if (typeof(T) == typeof(uint)) { uint u = Rt.ToUint32(v); return Unsafe.As<uint, T>(ref u); }
        if (typeof(T) == typeof(bool)) { bool b = Rt.ToBoolean(v); return Unsafe.As<bool, T>(ref b); }
        if (typeof(T) == typeof(float)) { float f = (float)Rt.ToNumber(v); return Unsafe.As<float, T>(ref f); }
        if (typeof(T) == typeof(object)) return (T)v;
        if (typeof(T) == typeof(string)) return Rt.IsNullish(v) ? default : (T)(object)Rt.ToString(v);
        if (Rt.IsNullish(v)) return default;
        if (v is T x) return x;
        throw TypeError($"Error #1034: Type Coercion failed: cannot convert {Rt.Describe(v)} to {typeof(T).Name} ({pname}).");
    }

    object FromAs(object v, Type pt, string pname)
    {
        if (pt == typeof(object)) return v;
        if (pt == typeof(string)) return Rt.IsNullish(v) ? null : Rt.ToString(v);
        if (pt == typeof(double)) return Rt.ToNumber(v);
        if (pt == typeof(int)) return Rt.ToInt32(v);
        if (pt == typeof(uint)) return Rt.ToUint32(v);
        if (pt == typeof(bool)) return Rt.ToBoolean(v);
        if (pt == typeof(float)) return (float)Rt.ToNumber(v);
        if (Rt.IsNullish(v)) return null;
        if (pt.IsInstanceOfType(v)) return v;
        throw TypeError($"Error #1034: Type Coercion failed: cannot convert {Rt.Describe(v)} to {pt.Name} ({pname}).");
    }

    public static object ToAs(object r) => r switch
    {
        null => null,
        bool b => Rt.Box(b),
        int i => Rt.Box(i),
        double d => Rt.Num(d),
        float f => Rt.Num(f),
        uint u => Rt.Uint(u),
        _ => r,
    };
}
