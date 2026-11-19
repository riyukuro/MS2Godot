#if MS2_DEVTOOLS
using System.Collections;
using System.Reflection;
using System.Text.Json.Nodes;

namespace Ms2.Devtools;

/// <summary>
/// Read-only dump of a managed object's state by reflection: public and private instance fields (auto-property backing
/// fields show under the property's name), optionally static fields and computed property getters. Depth-limited, cycles cut
/// (an object that is its own ancestor shows as "$cycle"), large collections truncated, and the total work bounded. Only
/// collections that report a Count are enumerated; no other method of the target is called.
/// </summary>
public sealed class ReflectionDump
{
    public int MaxDepth = 3;
    public int MaxItems = 32;
    public int MaxStringLength = 256;
    public int MaxValues = 4000;
    public bool IncludeStatics;
    public bool CallGetters;

    /// <summary>Renders values the dump should not recurse into (engine objects, vectors, variants); null = not handled.</summary>
    public Func<object, JsonNode> Custom;

    /// <summary>Stops the base-type walk before a type (the engine's own base classes have no state worth dumping).</summary>
    public Func<Type, bool> StopAt;

    readonly List<object> ancestors = new();
    int values;

    public JsonNode Dump(object o) => Value(o, 0);

    /// <summary>Follows a path like "effect.nodes[2].name" through fields (and indexers of lists) from <paramref name="root"/>.</summary>
    public static object Navigate(object root, string path, out string error)
    {
        error = null;
        object cur = root;
        foreach (var raw in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            string name = raw; var indexes = new List<int>();
            int br = raw.IndexOf('[');
            if (br >= 0)
            {
                name = raw[..br];
                foreach (var part in raw[br..].Split(new[] { '[', ']' }, StringSplitOptions.RemoveEmptyEntries))
                    if (int.TryParse(part, out int ix)) indexes.Add(ix); else { error = "bad index in " + raw; return null; }
            }
            if (name.Length > 0)
            {
                if (cur == null) { error = "null before " + name; return null; }
                var m = FindMember(cur.GetType(), name, out var field);
                if (m == null) { error = "no member " + name + " on " + cur.GetType().Name; return null; }
                cur = field != null ? field.GetValue(cur) : null;
            }
            foreach (int ix in indexes)
            {
                if (cur is IList list && ix >= 0 && ix < list.Count) cur = list[ix];
                else { error = "index " + ix + " out of range or not a list"; return null; }
            }
        }
        return cur;
    }

    static MemberInfo FindMember(Type t, string name, out FieldInfo field)
    {
        const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (var c = t; c != null && c != typeof(object); c = c.BaseType)
        {
            field = c.GetField(name, F) ?? c.GetField("<" + name + ">k__BackingField", F);
            if (field != null) return field;
        }
        field = null;
        return null;
    }

    JsonNode Value(object o, int depth)
    {
        if (++values > MaxValues) return "<truncated: value budget>";
        if (o == null) return null;
        var t = o.GetType();
        if (t == typeof(string)) { var s = (string)o; return s.Length > MaxStringLength ? s[..MaxStringLength] + "…(" + s.Length + ")" : s; }
        if (t == typeof(bool)) return (bool)o;
        if (t.IsEnum) return o.ToString();
        if (t.IsPrimitive)
        {
            if (o is double d) return double.IsFinite(d) ? d : d.ToString();
            if (o is float f) return float.IsFinite(f) ? f : f.ToString();
            if (o is IntPtr or UIntPtr) return "<ptr>";
            if (o is char c) return c.ToString();
            return Num(o);
        }
        if (o is decimal dec) return dec;
        if (o is DateTime or DateTimeOffset or TimeSpan or Guid) return o.ToString();
        if (o is Delegate) return "<" + t.Name + ">";
        if (o is Type ty) return ty.FullName;
        if (Custom?.Invoke(o) is { } custom) return custom;
        if (o is Pointer) return "<ptr>";

        if (!t.IsValueType)
        {
            foreach (var a in ancestors) if (ReferenceEquals(a, o)) return new JsonObject { ["$cycle"] = t.Name };
        }
        if (depth >= MaxDepth) return Brief(o, t);

        if (!t.IsValueType) ancestors.Add(o);
        try
        {
            if (o is IDictionary dict) return Dictionary(dict, t, depth);
            if (o is Array arr) return Items(arr, arr.Length, depth);
            if (o is ICollection col) return Items(col, col.Count, depth);
            if (o is IEnumerable && CountOf(o, t) is int cnt) return Items((IEnumerable)o, cnt, depth);
            return Members(o, t, depth);
        }
        finally { if (!t.IsValueType) ancestors.RemoveAt(ancestors.Count - 1); }
    }

    static JsonNode Num(object o) => o switch
    {
        byte v => v, sbyte v => v, short v => v, ushort v => v, int v => v, uint v => v, long v => v, ulong v => v, _ => o.ToString(),
    };


    // generic collections (HashSet, Queue, Stack, ...) report Count without being non-generic ICollection implementers
    static int? CountOf(object o, Type t)
    {
        var p = t.GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);
        if (p != null && p.PropertyType == typeof(int) && p.GetIndexParameters().Length == 0 && IsBcl(t)) return (int)p.GetValue(o);
        return null;
    }

    static bool IsBcl(Type t) => t.Assembly == typeof(object).Assembly || t.Assembly == typeof(HashSet<>).Assembly || t.Assembly.GetName().Name is "System.Collections" or "System.Collections.Concurrent";

    static JsonNode Brief(object o, Type t)
    {
        var obj = new JsonObject { ["$type"] = t.Name };
        if (o is ICollection c) obj["count"] = c.Count;
        return obj;
    }

    JsonNode Items(IEnumerable seq, int count, int depth)
    {
        var arr = new JsonArray();
        int n = 0;
        foreach (var item in seq)
        {
            if (n++ >= MaxItems) break;
            arr.Add(Value(item, depth + 1));
        }
        if (count > MaxItems) arr.Add($"…{count - MaxItems} more (count {count})");
        return arr;
    }

    JsonNode Dictionary(IDictionary d, Type t, int depth)
    {
        var obj = new JsonObject { ["$type"] = t.Name, ["count"] = d.Count };
        var entries = new JsonObject();
        int n = 0;
        foreach (DictionaryEntry e in d)
        {
            if (n++ >= MaxItems) { entries["…"] = (d.Count - MaxItems) + " more"; break; }
            string key = e.Key?.ToString() ?? "null";
            if (entries.ContainsKey(key)) key += "#" + n;
            entries[key] = Value(e.Value, depth + 1);
        }
        obj["entries"] = entries;
        return obj;
    }

    JsonNode Members(object o, Type t, int depth)
    {
        var obj = new JsonObject { ["$type"] = t.Name };
        const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (var c = t; c != null && c != typeof(object) && c != typeof(ValueType) && !(StopAt?.Invoke(c) ?? false); c = c.BaseType)
        {
            foreach (var f in c.GetFields(Inst))
            {
                if (f.FieldType.IsPointer || f.FieldType == typeof(IntPtr)) continue;
                string name = BackingName(f.Name) ?? f.Name;
                Put(obj, name, c, () => f.GetValue(o), depth);
            }
            if (IncludeStatics)
                foreach (var f in c.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (!f.IsLiteral && !f.FieldType.IsPointer) Put(obj, "static " + f.Name, c, () => f.GetValue(null), depth);
            if (CallGetters)
                foreach (var p in c.GetProperties(Inst))
                    if (p.GetIndexParameters().Length == 0 && p.CanRead && !IsAutoProperty(c, p)) Put(obj, p.Name + "()", c, () => p.GetValue(o), depth);
        }
        return obj;
    }

    static bool IsAutoProperty(Type c, PropertyInfo p) =>
        c.GetField("<" + p.Name + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) != null;

    static string BackingName(string n) => n.StartsWith('<') && n.EndsWith(">k__BackingField") ? n[1..^16] : null;

    void Put(JsonObject obj, string name, Type declaring, Func<object> read, int depth)
    {
        string key = name;
        if (obj.ContainsKey(key)) key = declaring.Name + "." + name;
        try { obj[key] = Value(read(), depth + 1); }
        catch (Exception e) { obj[key] = "<error: " + (e.InnerException ?? e).GetType().Name + ">"; }
    }
}
#endif
