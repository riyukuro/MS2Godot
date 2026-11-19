using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Ms2.Formats.Cache;

/// <summary>
/// A binary copy of a table's object graph as the parse leaves it, so a later session loads the table instead of parsing
/// the client's files again. Every instance field is written: primitives, enums, strings, arrays, List, HashSet,
/// Dictionary, SortedDictionary, structs (value tuples) and classes, with shared references kept shared. Delegate fields
/// are not written; a class that has any is created with its parameterless constructor (its own defaults), and the owner
/// sets the rest after the read. <see cref="Diff"/> compares two graphs the same way, delegates by their method and the
/// state their closure holds, and is the cache's equivalence check against a fresh parse.
/// </summary>
public static class Snapshot
{
    /// <summary>The bytes of <paramref name="root"/>'s graph.</summary>
    public static byte[] Write<T>(T root) where T : class
    {
        var w = new Writer();
        CodecOf<T>().Write(w, root);
        return w.ToArray();
    }

    /// <summary>The graph <see cref="Write{T}"/> wrote. Throws on bytes that do not read back to the end.</summary>
    public static T Read<T>(ReadOnlyMemory<byte> body) where T : class
    {
        var r = new Reader(body);
        var v = CodecOf<T>().Read(r);
        if (r.Pos != body.Length) throw new InvalidDataException($"snapshot: {body.Length - r.Pos} bytes left after the root");
        return v;
    }

    /// <summary>The first difference between two graphs (a path and both values), or null when they are the same.</summary>
    public static string Diff<T>(T a, T b) where T : class => CodecOf<T>().Diff(a, b, typeof(T).Name, new DiffState());

    // ---- codecs ------------------------------------------------------------------------------------------------------

    static readonly ConcurrentDictionary<Type, Codec> codecs = new();

    static Codec<T> CodecOf<T>() => (Codec<T>)For(typeof(T));

    internal static Codec For(Type t)
    {
        if (codecs.TryGetValue(t, out var c)) return c;
        lock (codecs)
        {
            if (codecs.TryGetValue(t, out c) || building.TryGetValue(t, out c)) return c;
            c = Create(t);
            building[t] = c;    // before Init, so a type that holds itself finds its codec
            bool outer = building.Count == 1;
            try { c.Init(); }
            catch
            {
                // the codecs made with it may hold it: none is kept, a later call fails the same way
                building.Remove(t);
                if (outer) building.Clear();
                throw;
            }
            if (outer)
            {
                // other threads see the codecs only once every one made with them is ready
                foreach (var kv in building) codecs[kv.Key] = kv.Value;
                building.Clear();
            }
            return c;
        }
    }

    static readonly Dictionary<Type, Codec> building = new();

    static Codec Create(Type t)
    {
        if (t == typeof(bool)) return new BoolCodec();
        if (t == typeof(byte)) return new ByteCodec();
        if (t == typeof(sbyte)) return new SByteCodec();
        if (t == typeof(short)) return new ShortCodec();
        if (t == typeof(ushort)) return new UShortCodec();
        if (t == typeof(int)) return new IntCodec();
        if (t == typeof(uint)) return new UIntCodec();
        if (t == typeof(long)) return new LongCodec();
        if (t == typeof(ulong)) return new ULongCodec();
        if (t == typeof(char)) return new CharCodec();
        if (t == typeof(float)) return new FloatCodec();
        if (t == typeof(double)) return new DoubleCodec();
        if (t == typeof(string)) return new StringCodec();
        if (t.IsEnum) return Make(typeof(EnumCodec<>), t);
        if (t.IsPointer || t.IsByRef || t == typeof(IntPtr) || t == typeof(UIntPtr) || typeof(Delegate).IsAssignableFrom(t)
            || typeof(Type).IsAssignableFrom(t) || typeof(MemberInfo).IsAssignableFrom(t))
            throw new NotSupportedException($"snapshot: {t} cannot be written");
        if (t.IsArray)
        {
            var e = t.GetElementType();
            if (t.GetArrayRank() > 1) return new MultiArrayCodec(t);
            return IsBlittable(e) ? Make(typeof(BlitArrayCodec<>), e) : Make(typeof(ArrayCodec<>), e);
        }
        if (t.IsGenericType)
        {
            var g = t.GetGenericTypeDefinition();
            var a = t.GetGenericArguments();
            if (g == typeof(Nullable<>)) return Make(typeof(NullableCodec<>), a[0]);
            if (g == typeof(List<>)) return Make(typeof(ListCodec<>), a[0]);
            if (g == typeof(HashSet<>)) return Make(typeof(HashSetCodec<>), a[0]);
            if (g == typeof(Dictionary<,>)) return Make(typeof(DictionaryCodec<,>), a[0], a[1]);
            if (g == typeof(SortedDictionary<,>)) return Make(typeof(SortedDictionaryCodec<,>), a[0], a[1]);
            if (t.Namespace is "System.Collections.Generic" or "System.Collections" or "System.Collections.Concurrent"
                    or "System.Collections.Immutable" or "System.Collections.Frozen")
                throw new NotSupportedException($"snapshot: collection {t} is not supported");
        }
        if (t.Namespace is "System.Collections.Generic" or "System.Collections")
            throw new NotSupportedException($"snapshot: collection {t} is not supported");
        if (t.IsValueType) return Make(typeof(StructCodec<>), t);
        if (t.IsInterface || t.IsAbstract || t == typeof(object)) return Make(typeof(PolyCodec<>), t);
        return Make(typeof(ClassCodec<>), t);
    }

    static Codec Make(Type generic, params Type[] args) => (Codec)Activator.CreateInstance(generic.MakeGenericType(args), true);

    static bool IsBlittable(Type t) => t.IsPrimitive && t != typeof(bool) && t != typeof(char) && t != typeof(IntPtr) && t != typeof(UIntPtr);

    /// <summary>A sealed class or a struct: the field's value is always of the declared type.</summary>
    static bool Exact(Type t) => t.IsValueType || t.IsSealed || t.IsArray;

    internal abstract class Codec
    {
        public virtual void Init() { }
        public abstract void WriteObj(Writer w, object v);
        public abstract object ReadObj(Reader r);
        public abstract string DiffObj(object a, object b, string path, DiffState s);
    }

    internal abstract class Codec<T> : Codec
    {
        public abstract void Write(Writer w, T v);
        public abstract T Read(Reader r);
        public abstract string Diff(T a, T b, string path, DiffState s);
        public override void WriteObj(Writer w, object v) => Write(w, (T)v);
        public override object ReadObj(Reader r) => Read(r);
        public override string DiffObj(object a, object b, string path, DiffState s) => Diff((T)a, (T)b, path, s);
    }

    static string Show(object v) => v switch
    {
        null => "null",
        string s => s.Length > 80 ? $"\"{s[..80]}...\" ({s.Length})" : $"\"{s}\"",
        _ => v.ToString(),
    };

    static string Differ(string path, object a, object b) => $"{path}: {Show(a)} != {Show(b)}";

    abstract class ValueCodec<T> : Codec<T> where T : IEquatable<T>
    {
        public override string Diff(T a, T b, string path, DiffState s) => a.Equals(b) ? null : Differ(path, a, b);
    }

    sealed class BoolCodec : ValueCodec<bool> { public override void Write(Writer w, bool v) => w.Byte(v ? (byte)1 : (byte)0); public override bool Read(Reader r) => r.Byte() != 0; }
    sealed class ByteCodec : ValueCodec<byte> { public override void Write(Writer w, byte v) => w.Byte(v); public override byte Read(Reader r) => r.Byte(); }
    sealed class SByteCodec : ValueCodec<sbyte> { public override void Write(Writer w, sbyte v) => w.Byte((byte)v); public override sbyte Read(Reader r) => (sbyte)r.Byte(); }
    sealed class ShortCodec : ValueCodec<short> { public override void Write(Writer w, short v) => w.Signed(v); public override short Read(Reader r) => (short)r.Signed(); }
    sealed class UShortCodec : ValueCodec<ushort> { public override void Write(Writer w, ushort v) => w.Unsigned(v); public override ushort Read(Reader r) => (ushort)r.Unsigned(); }
    sealed class IntCodec : ValueCodec<int> { public override void Write(Writer w, int v) => w.Signed(v); public override int Read(Reader r) => (int)r.Signed(); }
    sealed class UIntCodec : ValueCodec<uint> { public override void Write(Writer w, uint v) => w.Unsigned(v); public override uint Read(Reader r) => (uint)r.Unsigned(); }
    sealed class LongCodec : ValueCodec<long> { public override void Write(Writer w, long v) => w.Signed(v); public override long Read(Reader r) => r.Signed(); }
    sealed class ULongCodec : ValueCodec<ulong> { public override void Write(Writer w, ulong v) => w.Unsigned(v); public override ulong Read(Reader r) => r.Unsigned(); }
    sealed class CharCodec : ValueCodec<char> { public override void Write(Writer w, char v) => w.Unsigned(v); public override char Read(Reader r) => (char)r.Unsigned(); }

    // floats compare by their bits: NaN equals itself, 0 and -0 differ, as a byte copy keeps them
    sealed class FloatCodec : Codec<float>
    {
        public override void Write(Writer w, float v) => w.Fixed(BitConverter.SingleToUInt32Bits(v), 4);
        public override float Read(Reader r) => BitConverter.UInt32BitsToSingle((uint)r.Fixed(4));
        public override string Diff(float a, float b, string path, DiffState s) =>
            BitConverter.SingleToUInt32Bits(a) == BitConverter.SingleToUInt32Bits(b) ? null : Differ(path, a, b);
    }

    sealed class DoubleCodec : Codec<double>
    {
        public override void Write(Writer w, double v) => w.Fixed(BitConverter.DoubleToUInt64Bits(v), 8);
        public override double Read(Reader r) => BitConverter.UInt64BitsToDouble(r.Fixed(8));
        public override string Diff(double a, double b, string path, DiffState s) =>
            BitConverter.DoubleToUInt64Bits(a) == BitConverter.DoubleToUInt64Bits(b) ? null : Differ(path, a, b);
    }

    sealed class EnumCodec<T> : Codec<T> where T : struct, Enum
    {
        static readonly int size = Unsafe.SizeOf<T>();
        public override void Write(Writer w, T v)
        {
            ulong bits = size switch { 1 => Unsafe.As<T, byte>(ref v), 2 => Unsafe.As<T, ushort>(ref v), 4 => Unsafe.As<T, uint>(ref v), _ => Unsafe.As<T, ulong>(ref v) };
            w.Unsigned(bits);
        }
        public override T Read(Reader r)
        {
            ulong bits = r.Unsigned();
            T v = default;
            switch (size)
            {
                case 1: Unsafe.As<T, byte>(ref v) = (byte)bits; break;
                case 2: Unsafe.As<T, ushort>(ref v) = (ushort)bits; break;
                case 4: Unsafe.As<T, uint>(ref v) = (uint)bits; break;
                default: Unsafe.As<T, ulong>(ref v) = bits; break;
            }
            return v;
        }
        public override string Diff(T a, T b, string path, DiffState s) => EqualityComparer<T>.Default.Equals(a, b) ? null : Differ(path, a, b);
    }

    sealed class NullableCodec<T> : Codec<T?> where T : struct
    {
        Codec<T> inner;
        public override void Init() => inner = (Codec<T>)For(typeof(T));
        public override void Write(Writer w, T? v)
        {
            w.Byte(v.HasValue ? (byte)1 : (byte)0);
            if (v.HasValue) inner.Write(w, v.Value);
        }
        public override T? Read(Reader r) => r.Byte() != 0 ? inner.Read(r) : null;
        public override string Diff(T? a, T? b, string path, DiffState s) =>
            a.HasValue != b.HasValue ? Differ(path, a, b) : a.HasValue ? inner.Diff(a.Value, b.Value, path, s) : null;
    }

    // ---- references: null, a new object, or one written before (shared references stay shared) ---------------------

    /// <summary>A reference type: written once, then by its index; strings by value.</summary>
    internal abstract class RefCodec<T> : Codec<T> where T : class
    {
        public sealed override void Write(Writer w, T v)
        {
            if (v == null) { w.Unsigned(0); return; }
            if (w.Seen(v, out int id)) { w.Unsigned((ulong)id + 2); return; }
            w.Unsigned(1);
            WriteBody(w, v);
        }

        public sealed override T Read(Reader r)
        {
            ulong tag = r.Unsigned();
            if (tag == 0) return null;
            if (tag >= 2) return (T)r.Ref((int)(tag - 2));
            int slot = r.Reserve();
            var v = ReadBody(r, slot);
            r.Set(slot, v);
            return v;
        }

        protected abstract void WriteBody(Writer w, T v);
        /// <summary>Reads the body; a body that may hold itself sets <paramref name="slot"/> as soon as it is created.</summary>
        protected abstract T ReadBody(Reader r, int slot);

        public sealed override string Diff(T a, T b, string path, DiffState s)
        {
            if (a == null || b == null) return a == b ? null : Differ(path, a == null ? "null" : "object", b == null ? "null" : "object");
            if (a is string) return DiffBody(a, b, path, s);     // strings are values: shared or not, equal text is equal
            if (!s.Pair(a, b, out bool consistent)) return consistent ? null : $"{path}: shared differently";
            return DiffBody(a, b, path, s);
        }

        protected abstract string DiffBody(T a, T b, string path, DiffState s);
    }

    sealed class StringCodec : RefCodec<string>
    {
        protected override void WriteBody(Writer w, string v) => w.Utf8(v);
        protected override string ReadBody(Reader r, int slot) => r.Utf8();
        protected override string DiffBody(string a, string b, string path, DiffState s) => a == b ? null : Differ(path, a, b);
    }

    /// <summary>A field of an interface, abstract or object type: the value's own type is written with it.</summary>
    sealed class PolyCodec<T> : Codec<T> where T : class
    {
        public override void Write(Writer w, T v)
        {
            if (v == null) { w.Unsigned(0); return; }
            w.Unsigned(1);
            var t = v.GetType();
            For(typeof(string)).WriteObj(w, t.AssemblyQualifiedName);
            For(t).WriteObj(w, v);
        }

        public override T Read(Reader r)
        {
            if (r.Unsigned() == 0) return null;
            var name = (string)For(typeof(string)).ReadObj(r);
            var t = r.TypeOf(name);
            if (!typeof(T).IsAssignableFrom(t)) throw new InvalidDataException($"snapshot: {t} is not a {typeof(T)}");
            return (T)For(t).ReadObj(r);
        }

        public override string Diff(T a, T b, string path, DiffState s)
        {
            if (a == null || b == null) return a == b ? null : Differ(path, a == null ? "null" : a.GetType().Name, b == null ? "null" : b.GetType().Name);
            if (a.GetType() != b.GetType()) return Differ(path, a.GetType().Name, b.GetType().Name);
            return For(a.GetType()).DiffObj(a, b, path, s);
        }
    }

    /// <summary>A field of a non-sealed class type: the value's type is checked on write (a subclass is not written).</summary>
    static void CheckExact(Type declared, object v)
    {
        if (v != null && v.GetType() != declared) throw new NotSupportedException($"snapshot: a {v.GetType()} in a field of type {declared}");
    }

    // ---- arrays and collections --------------------------------------------------------------------------------------

    sealed class BlitArrayCodec<T> : RefCodec<T[]> where T : unmanaged
    {
        protected override void WriteBody(Writer w, T[] v) { w.Unsigned((ulong)v.Length); w.Bytes(MemoryMarshal.AsBytes(v.AsSpan())); }
        protected override T[] ReadBody(Reader r, int slot)
        {
            int n = r.Count();
            if (n == 0) return Array.Empty<T>();
            var v = new T[n];
            r.Bytes(MemoryMarshal.AsBytes(v.AsSpan()));
            return v;
        }
        protected override string DiffBody(T[] a, T[] b, string path, DiffState s)
        {
            if (a.Length != b.Length) return $"{path}: length {a.Length} != {b.Length}";
            var ea = MemoryMarshal.AsBytes(a.AsSpan());
            var eb = MemoryMarshal.AsBytes(b.AsSpan());
            if (ea.SequenceEqual(eb)) return null;
            for (int i = 0; i < a.Length; i++)
                if (!MemoryMarshal.AsBytes(a.AsSpan(i, 1)).SequenceEqual(MemoryMarshal.AsBytes(b.AsSpan(i, 1)))) return Differ($"{path}[{i}]", a[i], b[i]);
            return null;
        }
    }

    sealed class ArrayCodec<T> : RefCodec<T[]>
    {
        Codec<T> elem;
        public override void Init() => elem = (Codec<T>)For(typeof(T));
        protected override void WriteBody(Writer w, T[] v)
        {
            w.Unsigned((ulong)v.Length);
            foreach (var e in v) { if (!Exact(typeof(T))) CheckExact(typeof(T), e); elem.Write(w, e); }
        }
        protected override T[] ReadBody(Reader r, int slot)
        {
            int n = r.Count();
            var v = n == 0 ? Array.Empty<T>() : new T[n];
            r.Set(slot, v);
            for (int i = 0; i < n; i++) v[i] = elem.Read(r);
            return v;
        }
        protected override string DiffBody(T[] a, T[] b, string path, DiffState s)
        {
            if (a.Length != b.Length) return $"{path}: length {a.Length} != {b.Length}";
            for (int i = 0; i < a.Length; i++)
                if (elem.Diff(a[i], b[i], $"{path}[{i}]", s) is { } d) return d;
            return null;
        }
    }

    /// <summary>A multi-dimensional array: its lengths, then its elements in row-major order.</summary>
    sealed class MultiArrayCodec : Codec<Array>
    {
        readonly Type type;
        Codec elem;
        public MultiArrayCodec(Type t) => type = t;
        public override void Init() => elem = For(type.GetElementType());
        public override void Write(Writer w, Array v)
        {
            if (v == null) { w.Unsigned(0); return; }
            if (w.Seen(v, out int id)) { w.Unsigned((ulong)id + 2); return; }
            w.Unsigned(1);
            for (int d = 0; d < v.Rank; d++) w.Unsigned((ulong)v.GetLength(d));
            foreach (var e in v) elem.WriteObj(w, e);
        }
        public override Array Read(Reader r)
        {
            ulong tag = r.Unsigned();
            if (tag == 0) return null;
            if (tag >= 2) return (Array)r.Ref((int)(tag - 2));
            int slot = r.Reserve();
            var lengths = new int[type.GetArrayRank()];
            for (int d = 0; d < lengths.Length; d++) lengths[d] = r.Count();
            var v = Array.CreateInstance(type.GetElementType(), lengths);
            r.Set(slot, v);
            var index = new int[lengths.Length];
            long total = v.LongLength;
            for (long k = 0; k < total; k++)
            {
                v.SetValue(elem.ReadObj(r), index);
                for (int d = index.Length - 1; d >= 0 && ++index[d] == lengths[d]; d--) index[d] = 0;
            }
            return v;
        }
        public override string Diff(Array a, Array b, string path, DiffState s)
        {
            if (a == null || b == null) return a == b ? null : Differ(path, a, b);
            if (!s.Pair(a, b, out bool consistent)) return consistent ? null : $"{path}: shared differently";
            for (int d = 0; d < a.Rank; d++) if (a.GetLength(d) != b.GetLength(d)) return $"{path}: length {d} {a.GetLength(d)} != {b.GetLength(d)}";
            var ea = a.GetEnumerator();
            var eb = b.GetEnumerator();
            for (int k = 0; ea.MoveNext() && eb.MoveNext(); k++)
                if (elem.DiffObj(ea.Current, eb.Current, $"{path}[#{k}]", s) is { } d) return d;
            return null;
        }
        public override void WriteObj(Writer w, object v) => Write(w, (Array)v);
    }

    sealed class ListCodec<T> : RefCodec<List<T>>
    {
        Codec<T> elem;
        public override void Init() => elem = (Codec<T>)For(typeof(T));
        protected override void WriteBody(Writer w, List<T> v)
        {
            w.Unsigned((ulong)v.Count);
            foreach (var e in v) { if (!Exact(typeof(T))) CheckExact(typeof(T), e); elem.Write(w, e); }
        }
        protected override List<T> ReadBody(Reader r, int slot)
        {
            int n = r.Count();
            var v = new List<T>(n);
            r.Set(slot, v);
            for (int i = 0; i < n; i++) v.Add(elem.Read(r));
            return v;
        }
        protected override string DiffBody(List<T> a, List<T> b, string path, DiffState s)
        {
            if (a.Count != b.Count) return $"{path}: count {a.Count} != {b.Count}";
            for (int i = 0; i < a.Count; i++)
                if (elem.Diff(a[i], b[i], $"{path}[{i}]", s) is { } d) return d;
            return null;
        }
    }

    // The comparers a keyed collection may have: its key type's default, or one of the framework's string comparers.
    static readonly object[] Comparers =
    {
        null, StringComparer.Ordinal, StringComparer.OrdinalIgnoreCase, StringComparer.InvariantCulture, StringComparer.InvariantCultureIgnoreCase,
    };

    static int ComparerIndex<T>(object c, bool sorted)
    {
        if (c == null || (sorted ? ReferenceEquals(c, Comparer<T>.Default) : ReferenceEquals(c, EqualityComparer<T>.Default))) return 0;
        for (int i = 1; i < Comparers.Length; i++) if (Comparers[i].Equals(c)) return i;
        throw new NotSupportedException($"snapshot: comparer {c.GetType()} is not supported");
    }

    static IEqualityComparer<T> EqualityComparerAt<T>(int i) => i == 0 ? null : (IEqualityComparer<T>)Comparers[i];
    static IComparer<T> ComparerAt<T>(int i) => i == 0 ? null : (IComparer<T>)Comparers[i];

    sealed class HashSetCodec<T> : RefCodec<HashSet<T>>
    {
        Codec<T> elem;
        public override void Init() => elem = (Codec<T>)For(typeof(T));
        protected override void WriteBody(Writer w, HashSet<T> v)
        {
            w.Unsigned((ulong)ComparerIndex<T>(v.Comparer, false));
            w.Unsigned((ulong)v.Count);
            foreach (var e in v) { if (!Exact(typeof(T))) CheckExact(typeof(T), e); elem.Write(w, e); }
        }
        protected override HashSet<T> ReadBody(Reader r, int slot)
        {
            var cmp = EqualityComparerAt<T>(r.Count());
            int n = r.Count();
            var v = new HashSet<T>(n, cmp);
            r.Set(slot, v);
            for (int i = 0; i < n; i++) v.Add(elem.Read(r));
            return v;
        }
        protected override string DiffBody(HashSet<T> a, HashSet<T> b, string path, DiffState s)
        {
            if (!Equals(a.Comparer, b.Comparer)) return $"{path}: comparer {a.Comparer} != {b.Comparer}";
            if (a.Count != b.Count) return $"{path}: count {a.Count} != {b.Count}";
            int i = 0;
            using var eb = b.GetEnumerator();
            foreach (var x in a)          // the same order too: a reader may walk the set
            {
                eb.MoveNext();
                if (elem.Diff(x, eb.Current, $"{path}[#{i++}]", s) is { } d) return d;
            }
            return null;
        }
    }

    sealed class DictionaryCodec<K, V> : RefCodec<Dictionary<K, V>>
    {
        Codec<K> key;
        Codec<V> value;
        public override void Init() { key = (Codec<K>)For(typeof(K)); value = (Codec<V>)For(typeof(V)); }
        protected override void WriteBody(Writer w, Dictionary<K, V> v)
        {
            w.Unsigned((ulong)ComparerIndex<K>(v.Comparer, false));
            w.Unsigned((ulong)v.Count);
            foreach (var (k, x) in v)
            {
                if (!Exact(typeof(K))) CheckExact(typeof(K), k);
                if (!Exact(typeof(V))) CheckExact(typeof(V), x);
                key.Write(w, k);
                value.Write(w, x);
            }
        }
        protected override Dictionary<K, V> ReadBody(Reader r, int slot)
        {
            var cmp = EqualityComparerAt<K>(r.Count());
            int n = r.Count();
            var v = new Dictionary<K, V>(n, cmp);
            r.Set(slot, v);
            for (int i = 0; i < n; i++)
            {
                var k = key.Read(r);
                v.Add(k, value.Read(r));
            }
            return v;
        }
        protected override string DiffBody(Dictionary<K, V> a, Dictionary<K, V> b, string path, DiffState s)
        {
            if (!Equals(a.Comparer, b.Comparer)) return $"{path}: comparer {a.Comparer} != {b.Comparer}";
            if (a.Count != b.Count) return $"{path}: count {a.Count} != {b.Count}";
            int i = 0;
            using var eb = b.GetEnumerator();
            foreach (var (k, x) in a)     // in the same order: a reader may walk the dictionary
            {
                eb.MoveNext();
                if (key.Diff(k, eb.Current.Key, $"{path}.key#{i}", s) is { } dk) return dk;
                if (value.Diff(x, eb.Current.Value, $"{path}[{Show(k)}]", s) is { } dv) return dv;
                i++;
            }
            return null;
        }
    }

    sealed class SortedDictionaryCodec<K, V> : RefCodec<SortedDictionary<K, V>>
    {
        Codec<K> key;
        Codec<V> value;
        public override void Init() { key = (Codec<K>)For(typeof(K)); value = (Codec<V>)For(typeof(V)); }
        protected override void WriteBody(Writer w, SortedDictionary<K, V> v)
        {
            w.Unsigned((ulong)ComparerIndex<K>(v.Comparer, true));
            w.Unsigned((ulong)v.Count);
            foreach (var (k, x) in v)
            {
                if (!Exact(typeof(K))) CheckExact(typeof(K), k);
                if (!Exact(typeof(V))) CheckExact(typeof(V), x);
                key.Write(w, k);
                value.Write(w, x);
            }
        }
        protected override SortedDictionary<K, V> ReadBody(Reader r, int slot)
        {
            var v = new SortedDictionary<K, V>(ComparerAt<K>(r.Count()));
            r.Set(slot, v);
            int n = r.Count();
            for (int i = 0; i < n; i++)
            {
                var k = key.Read(r);
                v.Add(k, value.Read(r));
            }
            return v;
        }
        protected override string DiffBody(SortedDictionary<K, V> a, SortedDictionary<K, V> b, string path, DiffState s)
        {
            if (!Equals(a.Comparer, b.Comparer)) return $"{path}: comparer {a.Comparer} != {b.Comparer}";
            if (a.Count != b.Count) return $"{path}: count {a.Count} != {b.Count}";
            using var eb = b.GetEnumerator();
            foreach (var (k, x) in a)
            {
                eb.MoveNext();
                if (key.Diff(k, eb.Current.Key, $"{path}.key", s) is { } dk) return dk;
                if (value.Diff(x, eb.Current.Value, $"{path}[{Show(k)}]", s) is { } dv) return dv;
            }
            return null;
        }
    }

    // ---- classes and structs: every instance field ---------------------------------------------------------------------

    sealed class Field
    {
        public FieldInfo Info;
        public Codec Codec;
        public bool Exact;
        public bool IsDelegate;
        public string Name;
    }

    static List<Field> Fields(Type t)
    {
        var list = new List<Field>();
        var chain = new List<Type>();
        for (var b = t; b != null && b != typeof(object) && b != typeof(ValueType); b = b.BaseType) chain.Add(b);
        chain.Reverse();
        foreach (var b in chain)
            foreach (var f in b.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                               .OrderBy(f => f.MetadataToken))
            {
                bool del = typeof(Delegate).IsAssignableFrom(f.FieldType);
                list.Add(new Field
                {
                    Info = f,
                    Name = (chain.Count > 1 ? b.Name + "." : "") + f.Name,
                    IsDelegate = del,
                    Exact = Exact(f.FieldType) || f.FieldType.IsInterface || f.FieldType.IsAbstract || f.FieldType == typeof(object),
                });
            }
        return list;
    }

    static string DiffDelegate(Delegate a, Delegate b, string path, DiffState s)
    {
        if (a == null || b == null) return a == b ? null : Differ(path, a == null ? "null" : "delegate", b == null ? "null" : "delegate");
        if (a.Method != b.Method) return Differ(path, a.Method, b.Method);
        var ta = a.Target;
        var tb = b.Target;
        if (ta == null || tb == null) return ta == tb ? null : $"{path}: target null on one side";
        if (ta.GetType() != tb.GetType()) return Differ(path + ".Target", ta.GetType(), tb.GetType());
        // a closure's captured state is the delegate's data: compared field by field (a `this` target is compared by its owner)
        var tt = ta.GetType();
        if (!tt.IsDefined(typeof(CompilerGeneratedAttribute)) || tt.Name == "<>c") return null;
        foreach (var f in tt.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            var va = f.GetValue(ta);
            var vb = f.GetValue(tb);
            if (va is Delegate da) { if (DiffDelegate(da, vb as Delegate, $"{path}.{f.Name}", s) is { } d) return d; continue; }
            if (va == null || vb == null) { if (va != vb) return Differ($"{path}.{f.Name}", va, vb); continue; }
            if (va.GetType() != vb.GetType()) return Differ($"{path}.{f.Name}", va.GetType(), vb.GetType());
            Codec c;
            try { c = For(va.GetType()); } catch (NotSupportedException) { if (!Equals(va, vb)) return Differ($"{path}.{f.Name}", va, vb); continue; }
            if (c.DiffObj(va, vb, $"{path}.{f.Name}", s) is { } dd) return dd;
        }
        return null;
    }

    /// <summary>The codec's value type: a field of that type is read and written by its typed methods, else as an object.</summary>
    static Type ValueTypeOf(Codec c)
    {
        for (var t = c.GetType(); t != null; t = t.BaseType)
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Codec<>)) return t.GetGenericArguments()[0];
        return typeof(object);
    }

    /// <summary>
    /// The field-by-field reader and writer of a class or struct, emitted once per type (a reflection call per field
    /// is most of a load otherwise). The readers store into read-only fields too, as a constructor does.
    /// </summary>
    static class Emit
    {
        static void LoadCodec(ILGenerator il, int i, Codec c, out MethodInfo read, out MethodInfo write, out bool typed, Type fieldType)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, i);
            il.Emit(OpCodes.Ldelem_Ref);
            var vt = ValueTypeOf(c);
            typed = vt == fieldType;
            var ct = typed ? typeof(Codec<>).MakeGenericType(vt) : typeof(Codec);
            il.Emit(OpCodes.Castclass, ct);
            read = ct.GetMethod(typed ? "Read" : "ReadObj");
            write = ct.GetMethod(typed ? "Write" : "WriteObj");
        }

        /// <summary>(codecs, writer, value): writes each field of the value.</summary>
        public static Delegate Writer(Type t, Field[] fields, Type delegateType)
        {
            var dm = new DynamicMethod("SnapshotWrite_" + t.Name, null, new[] { typeof(Codec[]), typeof(Writer), t }, typeof(Snapshot).Module, true);
            var il = dm.GetILGenerator();
            var check = typeof(Snapshot).GetMethod(nameof(CheckExact), BindingFlags.NonPublic | BindingFlags.Static);
            var typeOf = typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle));
            for (int i = 0; i < fields.Length; i++)
            {
                var f = fields[i];
                if (f.IsDelegate) continue;
                var ft = f.Info.FieldType;
                if (!f.Exact)
                {
                    il.Emit(OpCodes.Ldtoken, ft);
                    il.Emit(OpCodes.Call, typeOf);
                    LoadField(il, t, f.Info);
                    il.Emit(OpCodes.Call, check);
                }
                LoadCodec(il, i, f.Codec, out _, out var write, out bool typed, ft);
                il.Emit(OpCodes.Ldarg_1);
                LoadField(il, t, f.Info);
                if (!typed && ft.IsValueType) il.Emit(OpCodes.Box, ft);
                il.Emit(OpCodes.Callvirt, write);
            }
            il.Emit(OpCodes.Ret);
            return dm.CreateDelegate(delegateType);
        }

        static void LoadField(ILGenerator il, Type t, FieldInfo f)
        {
            if (t.IsValueType) il.Emit(OpCodes.Ldarga_S, (byte)2); else il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldfld, f);
        }

        /// <summary>(codecs, reader, object): reads each field into a class object.</summary>
        public static Action<Codec[], Reader, T> ClassReader<T>(Field[] fields)
        {
            var t = typeof(T);
            var dm = new DynamicMethod("SnapshotRead_" + t.Name, null, new[] { typeof(Codec[]), typeof(Reader), t }, typeof(Snapshot).Module, true);
            var il = dm.GetILGenerator();
            for (int i = 0; i < fields.Length; i++)
            {
                var f = fields[i];
                if (f.IsDelegate) continue;
                il.Emit(OpCodes.Ldarg_2);
                ReadValue(il, i, f);
                il.Emit(OpCodes.Stfld, f.Info);
            }
            il.Emit(OpCodes.Ret);
            return (Action<Codec[], Reader, T>)dm.CreateDelegate(typeof(Action<Codec[], Reader, T>));
        }

        /// <summary>(codecs, reader): a struct of the fields read.</summary>
        public static Func<Codec[], Reader, T> StructReader<T>(Field[] fields)
        {
            var t = typeof(T);
            var dm = new DynamicMethod("SnapshotRead_" + t.Name, t, new[] { typeof(Codec[]), typeof(Reader) }, typeof(Snapshot).Module, true);
            var il = dm.GetILGenerator();
            var v = il.DeclareLocal(t);
            il.Emit(OpCodes.Ldloca_S, (byte)v.LocalIndex);
            il.Emit(OpCodes.Initobj, t);
            for (int i = 0; i < fields.Length; i++)
            {
                il.Emit(OpCodes.Ldloca_S, (byte)v.LocalIndex);
                ReadValue(il, i, fields[i]);
                il.Emit(OpCodes.Stfld, fields[i].Info);
            }
            il.Emit(OpCodes.Ldloc, v);
            il.Emit(OpCodes.Ret);
            return (Func<Codec[], Reader, T>)dm.CreateDelegate(typeof(Func<Codec[], Reader, T>));
        }

        static void ReadValue(ILGenerator il, int i, Field f)
        {
            var ft = f.Info.FieldType;
            LoadCodec(il, i, f.Codec, out var read, out _, out bool typed, ft);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, read);
            if (!typed) il.Emit(ft.IsValueType ? OpCodes.Unbox_Any : OpCodes.Castclass, ft);
        }
    }

    sealed class ClassCodec<T> : RefCodec<T> where T : class
    {
        Field[] fields;
        Codec[] codecs;
        Func<T> create;
        Action<Codec[], Writer, T> write;
        Action<Codec[], Reader, T> read;

        public override void Init()
        {
            fields = Fields(typeof(T)).ToArray();
            foreach (var f in fields) if (!f.IsDelegate) f.Codec = For(f.Info.FieldType);
            codecs = fields.Select(f => f.Codec).ToArray();
            // a class with delegate fields gets its own defaults for them from its parameterless constructor
            var ctor = fields.Any(f => f.IsDelegate)
                ? typeof(T).GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes) : null;
            if (ctor != null)
            {
                var dm = new DynamicMethod("SnapshotNew_" + typeof(T).Name, typeof(T), Type.EmptyTypes, typeof(Snapshot).Module, true);
                var il = dm.GetILGenerator();
                il.Emit(OpCodes.Newobj, ctor);
                il.Emit(OpCodes.Ret);
                create = (Func<T>)dm.CreateDelegate(typeof(Func<T>));
            }
            else create = () => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            write = (Action<Codec[], Writer, T>)Emit.Writer(typeof(T), fields, typeof(Action<Codec[], Writer, T>));
            read = Emit.ClassReader<T>(fields);
        }

        protected override void WriteBody(Writer w, T v)
        {
            if (v.GetType() != typeof(T)) throw new NotSupportedException($"snapshot: a {v.GetType()} where a {typeof(T)} is declared");
            write(codecs, w, v);
        }

        protected override T ReadBody(Reader r, int slot)
        {
            var v = create();
            r.Set(slot, v);
            read(codecs, r, v);
            return v;
        }

        protected override string DiffBody(T a, T b, string path, DiffState s)
        {
            foreach (var f in fields)
            {
                var va = f.Info.GetValue(a);
                var vb = f.Info.GetValue(b);
                string p = $"{path}.{f.Name}";
                var d = f.IsDelegate ? DiffDelegate((Delegate)va, (Delegate)vb, p, s) : f.Codec.DiffObj(va, vb, p, s);
                if (d != null) return d;
            }
            return null;
        }
    }

    sealed class StructCodec<T> : Codec<T> where T : struct
    {
        Field[] fields;
        Codec[] codecs;
        Action<Codec[], Writer, T> write;
        Func<Codec[], Reader, T> read;

        public override void Init()
        {
            fields = Fields(typeof(T)).ToArray();
            foreach (var f in fields)
            {
                if (f.IsDelegate) throw new NotSupportedException($"snapshot: delegate field {f.Name} in struct {typeof(T)}");
                f.Codec = For(f.Info.FieldType);
            }
            codecs = fields.Select(f => f.Codec).ToArray();
            write = (Action<Codec[], Writer, T>)Emit.Writer(typeof(T), fields, typeof(Action<Codec[], Writer, T>));
            read = Emit.StructReader<T>(fields);
        }

        public override void Write(Writer w, T v) => write(codecs, w, v);

        public override T Read(Reader r) => read(codecs, r);

        public override string Diff(T a, T b, string path, DiffState s)
        {
            object ba = a, bb = b;
            foreach (var f in fields)
                if (f.Codec.DiffObj(f.Info.GetValue(ba), f.Info.GetValue(bb), $"{path}.{f.Name}", s) is { } d) return d;
            return null;
        }
    }

    // ---- the byte stream ---------------------------------------------------------------------------------------------

    internal sealed class Writer
    {
        byte[] buf = new byte[1 << 16];
        int len;
        readonly Dictionary<object, int> objects = new(ReferenceEqualityComparer.Instance);
        readonly Dictionary<string, int> strings = new(StringComparer.Ordinal);
        int next;

        /// <summary>The index of an object (a string by value) written before; else it gets the next index.</summary>
        public bool Seen(object v, out int id)
        {
            if (v is string s)
            {
                if (strings.TryGetValue(s, out id)) return true;
                strings[s] = next++;
                return false;
            }
            if (objects.TryGetValue(v, out id)) return true;
            objects[v] = next++;
            return false;
        }

        Span<byte> Grow(int n)
        {
            if (len + n > buf.Length) Array.Resize(ref buf, Math.Max(buf.Length * 2, len + n));
            var s = buf.AsSpan(len, n);
            len += n;
            return s;
        }

        public void Byte(byte b) => Grow(1)[0] = b;

        public void Unsigned(ulong v)
        {
            while (v >= 0x80) { Byte((byte)(v | 0x80)); v >>= 7; }
            Byte((byte)v);
        }

        public void Signed(long v) => Unsigned((ulong)((v << 1) ^ (v >> 63)));

        public void Fixed(ulong v, int n)
        {
            var s = Grow(n);
            for (int i = 0; i < n; i++) s[i] = (byte)(v >> (8 * i));
        }

        public void Bytes(ReadOnlySpan<byte> b)
        {
            if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("snapshot: big-endian host");
            b.CopyTo(Grow(b.Length));
        }

        public void Utf8(string s)
        {
            int n = Encoding.UTF8.GetByteCount(s);
            Unsigned((ulong)n);
            Encoding.UTF8.GetBytes(s, Grow(n));
        }

        public byte[] ToArray() => buf.AsSpan(0, len).ToArray();
    }

    internal sealed class Reader
    {
        readonly ReadOnlyMemory<byte> mem;
        public int Pos;
        readonly List<object> refs = new();
        readonly Dictionary<string, Type> types = new();
        static readonly object Pending = new();

        public Reader(ReadOnlyMemory<byte> m) => mem = m;

        ReadOnlySpan<byte> Take(int n)
        {
            if ((uint)n > (uint)(mem.Length - Pos)) throw new InvalidDataException("snapshot: truncated");
            var s = mem.Span.Slice(Pos, n);
            Pos += n;
            return s;
        }

        public byte Byte() => Take(1)[0];

        public ulong Unsigned()
        {
            ulong v = 0;
            for (int shift = 0; shift < 64; shift += 7)
            {
                byte b = Byte();
                v |= (ulong)(b & 0x7f) << shift;
                if (b < 0x80) return v;
            }
            throw new InvalidDataException("snapshot: bad number");
        }

        public long Signed()
        {
            ulong u = Unsigned();
            return (long)(u >> 1) ^ -(long)(u & 1);
        }

        public int Count()
        {
            ulong n = Unsigned();
            if (n > int.MaxValue || (long)n > mem.Length) throw new InvalidDataException("snapshot: bad count");
            return (int)n;
        }

        public ulong Fixed(int n)
        {
            var s = Take(n);
            ulong v = 0;
            for (int i = 0; i < n; i++) v |= (ulong)s[i] << (8 * i);
            return v;
        }

        public void Bytes(Span<byte> into) => Take(into.Length).CopyTo(into);

        public string Utf8() => Encoding.UTF8.GetString(Take(Count()));

        public int Reserve() { refs.Add(Pending); return refs.Count - 1; }
        public void Set(int slot, object v) => refs[slot] = v;

        public object Ref(int id)
        {
            if ((uint)id >= (uint)refs.Count || ReferenceEquals(refs[id], Pending)) throw new InvalidDataException("snapshot: bad reference");
            return refs[id];
        }

        public Type TypeOf(string name)
        {
            if (!types.TryGetValue(name, out var t)) types[name] = t = Type.GetType(name, true);
            return t;
        }
    }

    /// <summary>The pairs of objects already compared: a shared object must be shared the same way on both sides.</summary>
    internal sealed class DiffState
    {
        readonly Dictionary<object, object> ab = new(ReferenceEqualityComparer.Instance), ba = new(ReferenceEqualityComparer.Instance);

        /// <summary>True when the pair is new (compare it); false when seen, <paramref name="consistent"/> when as the same pair.</summary>
        public bool Pair(object a, object b, out bool consistent)
        {
            bool sa = ab.TryGetValue(a, out var pb), sb = ba.TryGetValue(b, out var pa);
            if (!sa && !sb) { ab[a] = b; ba[b] = a; consistent = true; return true; }
            consistent = sa && ReferenceEquals(pb, b) && sb && ReferenceEquals(pa, a);
            return false;
        }
    }
}
