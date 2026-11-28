namespace Ms2.Avm2;

/// <summary>A callable method body: an ABC method (with its captured scope) or a C# native.</summary>
public abstract class MethodImpl
{
    public string Name;
    public abstract object Invoke(object self, object[] args);
    /// <summary><see cref="Invoke"/> with one argument (a setter, a listener). A native member may take it without an
    /// array; otherwise the callee gets a fresh one, which it may keep.</summary>
    public virtual object Invoke1(object self, object arg) => Invoke(self, new[] { arg });
    public override string ToString() => Name;
}

public sealed class NativeMethod : MethodImpl
{
    readonly Func<object, object[], object> fn;
    /// <summary>The same call for exactly one argument, without an array (null: <see cref="Invoke1"/> makes one).</summary>
    readonly Func<object, object, object> fn1;
    public NativeMethod(string name, Func<object, object[], object> fn, Func<object, object, object> fn1 = null) { Name = name; this.fn = fn; this.fn1 = fn1; }
    /// <summary>Sees every native call on this thread before it runs (the equivalence tests trace the interpreter and the
    /// compiled code; per thread, so a test's observer never sees another thread's movies).</summary>
    [ThreadStatic] public static Action<NativeMethod, object, object[]> Observer;
    public override object Invoke(object self, object[] args)
    {
        Observer?.Invoke(this, self, args);
        return fn(self, args);
    }
    public override object Invoke1(object self, object arg) =>
        fn1 != null && Observer == null ? fn1(self, arg) : Invoke(self, new[] { arg });
}

/// <summary>Any AS3 object: traits of its class (slots, methods, accessors), dynamic properties and the prototype chain.</summary>
public class AsObject
{
    public AsClass Class;
    public TraitTable Traits;
    /// <summary>Set on prototype objects: the next prototype (null ends the chain).</summary>
    public AsObject ProtoParent;
    public bool IsPrototypeObject;           // the class's instance traits, or own traits (class objects, activations, scripts)
    public object[] Slots;
    Dictionary<string, object> dyn;
    List<string> dynOrder;
    Dictionary<MethodImpl, AsMethodClosure> closures;

    public AsObject() { }
    public AsObject(AsClass cls) { Init(cls); }

    /// <summary>Gives the object its class layout (called once, before the constructor runs).</summary>
    public void Init(AsClass cls)
    {
        Class = cls;
        if (cls != null) { Traits = cls.InstanceTraits; Slots = cls.InstanceTraits.NewSlots(); }
    }

    public Runtime Rt0 => Class?.Runtime;

    // ---- dynamic properties ---------------------------------------------------------------------------------

    public bool HasDynamic(string k) => dyn != null && dyn.ContainsKey(k);
    public IEnumerable<string> DynamicKeys => dynOrder ?? (IEnumerable<string>)Array.Empty<string>();
    public object GetDynamic(string k) => dyn != null && dyn.TryGetValue(k, out var v) ? v : Rt.Undef;

    public void SetDynamic(string k, object v)
    {
        dyn ??= new(); dynOrder ??= new();
        if (!dyn.ContainsKey(k)) dynOrder.Add(k);
        dyn[k] = v;
    }

    public bool DeleteDynamic(string k)
    {
        if (dyn == null || !dyn.Remove(k)) return false;
        dynOrder.Remove(k);
        return true;
    }

    public virtual bool IsDynamic => Class == null || Class.InstancesDynamic;

    // ---- keyed access (arrays, dictionaries, vectors override) ----------------------------------------------

    /// <summary>Property access with a runtime key that is not a plain name (an index or, for a Dictionary, any object).</summary>
    public virtual bool TryGetKey(object key, out object v) { v = null; return false; }
    public virtual bool TrySetKey(object key, object v) => false;
    public virtual bool TryHasKey(object key, out bool has) { has = false; return false; }
    public virtual bool TryDeleteKey(object key, out bool done) { done = false; return false; }

    // ---- named access -------------------------------------------------------------------------------------

    public object GetPublic(string name) => Rt0 != null ? Rt0.GetProperty(this, Name.Public(name)) : GetDynamic(name);
    public void SetPublic(string name, object v) { if (Rt0 != null) Rt0.SetProperty(this, Name.Public(name), v); else SetDynamic(name, v); }

    public AsMethodClosure Closure(MethodImpl m)
    {
        closures ??= new();
        if (!closures.TryGetValue(m, out var c)) closures[m] = c = new AsMethodClosure(Class?.Runtime, this, m);
        return c;
    }

    /// <summary>for-in / for-each enumeration: the dynamic properties (arrays and dictionaries add their keys).</summary>
    public virtual IList<object> EnumKeys() => dynOrder?.Select(k => (object)k).ToList() ?? new List<object>();

    public virtual object ToPrimitive(bool numberHint)
    {
        var rt = Rt0;
        if (rt != null)
        {
            foreach (var m in numberHint ? new[] { "valueOf", "toString" } : new[] { "toString", "valueOf" })
            {
                var f = rt.GetProperty(this, Name.Public(m));
                if (f is AsFunction fn)
                {
                    var r = fn.Call(this, Array.Empty<object>());
                    if (r is not AsObject) return r;
                }
            }
        }
        return DefaultString();
    }

    public virtual string DefaultString() => Class == null ? "[object Object]" : "[object " + Class.LocalName + "]";
    public override string ToString() => DefaultString();
}

/// <summary>A class object: static traits on itself, instance traits for its instances, a prototype, a constructor.</summary>
public sealed class AsClass : AsObject
{
    public Runtime Runtime;
    public string Name;                 // qualified (pkg.Name)
    public string LocalName;
    public Ns Ns;
    public AsClass Super;
    public TraitTable InstanceTraits;
    public AsObject Prototype;
    public List<AsClass> Interfaces = new();
    public bool InstancesDynamic = true, IsInterface, IsFinal;
    /// <summary>Creates the C# object for an instance (the nearest native base's type); inherited by subclasses.</summary>
    public Func<AsClass, AsObject> Allocator;
    /// <summary>The instance constructor: an ABC iinit or a native one.</summary>
    public MethodImpl Ctor;
    /// <summary>Called when the class is called as a function (conversions such as int(x), String(x), or a cast).</summary>
    public Func<object[], object> CallAsFunction;
    /// <summary>For a native class: the C# type behind it.</summary>
    public Type NativeType;
    public bool IsNative;
    public bool IsErrorClass;
    /// <summary>Vector.&lt;T&gt; element class for applied types.</summary>
    public AsClass TypeArg;
    /// <summary>The movie whose ABC defines this class (its traits' VMAbcFile, Traits::GetFilePtr); null for a builtin,
    /// whose traits come from no file (<see cref="AbcContext.MovieKey"/>).</summary>
    public string MovieKey;

    public override bool IsDynamic => true;

    public bool IsSubclassOf(AsClass other)
    {
        for (var c = this; c != null; c = c.Super)
        {
            if (c == other) return true;
            foreach (var i in c.Interfaces) if (i == other || i.IsSubclassOf(other)) return true;
        }
        return false;
    }

    public AsObject Construct(params object[] args)
    {
        if (IsInterface) throw Runtime.TypeError($"cannot construct interface {Name}");
        var alloc = Allocator ?? (c => new AsObject());
        var o = alloc(this);
        o.Init(this);
        Runtime.BeforeConstruct(o);
        Ctor?.Invoke(o, args);
        Runtime.AfterConstruct(o);
        return o;
    }

    public override string DefaultString() => "[class " + LocalName + "]";
}

/// <summary>A function value.</summary>
public abstract class AsFunction : AsObject
{
    public abstract object Call(object self, object[] args);
    /// <summary><see cref="Call"/> with one argument (<see cref="MethodImpl.Invoke1"/>).</summary>
    public virtual object Call1(object self, object arg) => Call(self, new[] { arg });
    public virtual AsObject Construct(object[] args)
    {
        var rt = Class.Runtime;
        var o = new AsObject(rt.ObjectClass);
        var proto = GetDynamic("prototype");
        if (proto is AsObject p) o.SetDynamic("__proto__", p);
        var r = Call(o, args);
        return r as AsObject ?? o;
    }
    public override string DefaultString() => "function Function() {}";
}

/// <summary>A method bound to its receiver (what reading a method property gives).</summary>
public sealed class AsMethodClosure : AsFunction
{
    public readonly object Receiver;
    public readonly MethodImpl Method;
    public AsMethodClosure(Runtime rt, object receiver, MethodImpl m) { Receiver = receiver; Method = m; if (rt != null) Init(rt.FunctionClass); }
    public override object Call(object self, object[] args) => Method.Invoke(Receiver, args);
    public override object Call1(object self, object arg) => Method.Invoke1(Receiver, arg);
    public bool SameAs(AsMethodClosure o) => ReferenceEquals(Method, o.Method) && Rt.StrictEquals(Receiver, o.Receiver);
    public override string ToString() => "closure " + Method.Name;
}

/// <summary>A function made by newfunction (or a native global function): called with the given receiver.</summary>
public sealed class AsPlainFunction : AsFunction
{
    public readonly MethodImpl Method;
    public AsPlainFunction(Runtime rt, MethodImpl m) { Method = m; Init(rt.FunctionClass); }
    public override object Call(object self, object[] args) => Method.Invoke(self, args);
    public override object Call1(object self, object arg) => Method.Invoke1(self, arg);
    public override string ToString() => "function " + Method.Name;
}

/// <summary>A scope chain link (captured by closures and class methods).</summary>
public sealed class Scope
{
    public readonly object Obj;
    public readonly bool With;
    public readonly Scope Parent;
    public Scope(object obj, bool with, Scope parent) { Obj = obj; With = with; Parent = parent; }
}
