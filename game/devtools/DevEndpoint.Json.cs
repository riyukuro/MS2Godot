#if MS2_DEVTOOLS
using System;
using System.Text.Json.Nodes;
using Godot;

namespace Ms2Godot;

public partial class DevEndpoint
{
    const int MaxVariantItems = 32;

    static JsonArray Arr(params double[] v)
    {
        var a = new JsonArray();
        foreach (var d in v) a.Add(double.IsFinite(d) ? d : 0);
        return a;
    }

    /// <summary>A short description of an engine object (never recursed into).</summary>
    internal static JsonNode ObjectBrief(GodotObject o)
    {
        if (o == null || !GodotObject.IsInstanceValid(o)) return null;
        var j = new JsonObject { ["$obj"] = o.GetClass(), ["id"] = (long)o.GetInstanceId() };
        if (o.GetType().FullName is { } cs && cs != "Godot." + o.GetClass()) j["cs"] = cs;
        if (o is Node n && n.IsInsideTree()) j["path"] = n.GetPath().ToString();
        else if (o is Node n2) j["name"] = n2.Name.ToString();
        if (o is Resource r)
        {
            if (!string.IsNullOrEmpty(r.ResourcePath)) j["res"] = r.ResourcePath;
            else if (!string.IsNullOrEmpty(r.ResourceName)) j["name"] = r.ResourceName;
        }
        return j;
    }

    /// <summary>Godot value types and engine objects as JSON (used by the reflection dump and the property readers).</summary>
    internal static JsonNode Struct(object o)
    {
        switch (o)
        {
            case GodotObject g: return ObjectBrief(g);
            case Variant v: return FromVariant(v, 0);
            case Vector2 v: return Arr(v.X, v.Y);
            case Vector2I v: return Arr(v.X, v.Y);
            case Vector3 v: return Arr(v.X, v.Y, v.Z);
            case Vector3I v: return Arr(v.X, v.Y, v.Z);
            case Vector4 v: return Arr(v.X, v.Y, v.Z, v.W);
            case Quaternion q: return Arr(q.X, q.Y, q.Z, q.W);
            case Color c: return new JsonObject { ["rgba"] = Arr(c.R, c.G, c.B, c.A) };
            case Rect2 r: return new JsonObject { ["pos"] = Arr(r.Position.X, r.Position.Y), ["size"] = Arr(r.Size.X, r.Size.Y) };
            case Rect2I r: return new JsonObject { ["pos"] = Arr(r.Position.X, r.Position.Y), ["size"] = Arr(r.Size.X, r.Size.Y) };
            case Aabb a: return new JsonObject { ["pos"] = Arr(a.Position.X, a.Position.Y, a.Position.Z), ["size"] = Arr(a.Size.X, a.Size.Y, a.Size.Z) };
            case Plane p: return Arr(p.Normal.X, p.Normal.Y, p.Normal.Z, p.D);
            case Basis b: return Arr(b.X.X, b.X.Y, b.X.Z, b.Y.X, b.Y.Y, b.Y.Z, b.Z.X, b.Z.Y, b.Z.Z);
            case Transform3D t: return new JsonObject { ["origin"] = Arr(t.Origin.X, t.Origin.Y, t.Origin.Z), ["basis"] = Struct(t.Basis) };
            case Transform2D t: return new JsonObject { ["x"] = Arr(t.X.X, t.X.Y), ["y"] = Arr(t.Y.X, t.Y.Y), ["origin"] = Arr(t.Origin.X, t.Origin.Y) };
            case StringName s: return s.ToString();
            case NodePath p: return p.ToString();
            case Rid rid: return "rid:" + rid.Id;
            case Callable c: return "<callable " + c.Method + ">";
            case Signal s: return "<signal " + s.Name + ">";
        }
        return null;
    }

    internal static JsonNode FromVariant(Variant v, int depth)
    {
        switch (v.VariantType)
        {
            case Variant.Type.Nil: return null;
            case Variant.Type.Bool: return v.AsBool();
            case Variant.Type.Int: return v.AsInt64();
            case Variant.Type.Float: { double d = v.AsDouble(); return double.IsFinite(d) ? d : d.ToString(); }
            case Variant.Type.String: case Variant.Type.StringName: case Variant.Type.NodePath: return v.AsString();
            case Variant.Type.Object: return ObjectBrief(v.AsGodotObject());
            case Variant.Type.Vector2: return Struct(v.AsVector2());
            case Variant.Type.Vector2I: return Struct(v.AsVector2I());
            case Variant.Type.Vector3: return Struct(v.AsVector3());
            case Variant.Type.Vector3I: return Struct(v.AsVector3I());
            case Variant.Type.Vector4: return Struct(v.AsVector4());
            case Variant.Type.Quaternion: return Struct(v.AsQuaternion());
            case Variant.Type.Color: return Struct(v.AsColor());
            case Variant.Type.Rect2: return Struct(v.AsRect2());
            case Variant.Type.Rect2I: return Struct(v.AsRect2I());
            case Variant.Type.Aabb: return Struct(v.AsAabb());
            case Variant.Type.Plane: return Struct(v.AsPlane());
            case Variant.Type.Basis: return Struct(v.AsBasis());
            case Variant.Type.Transform3D: return Struct(v.AsTransform3D());
            case Variant.Type.Transform2D: return Struct(v.AsTransform2D());
            case Variant.Type.Rid: return Struct(v.AsRid());
            case Variant.Type.Array:
            {
                if (depth > 3) return "<array>";
                var src = v.AsGodotArray();
                var a = new JsonArray();
                for (int i = 0; i < src.Count && i < MaxVariantItems; i++) a.Add(FromVariant(src[i], depth + 1));
                if (src.Count > MaxVariantItems) a.Add($"…{src.Count - MaxVariantItems} more (count {src.Count})");
                return a;
            }
            case Variant.Type.Dictionary:
            {
                if (depth > 3) return "<dictionary>";
                var src = v.AsGodotDictionary();
                var o = new JsonObject();
                int n = 0;
                foreach (var kv in src)
                {
                    if (n++ >= MaxVariantItems) { o["…"] = $"{src.Count - MaxVariantItems} more"; break; }
                    string key = kv.Key.AsString();
                    o[o.ContainsKey(key) ? key + "#" + n : key] = FromVariant(kv.Value, depth + 1);
                }
                return o;
            }
            default:
                // packed arrays, callables, signals: type and size only
                return "<" + v.VariantType + ">" + (v.VariantType.ToString().StartsWith("Packed") ? " " + SafeSize(v) : "");
        }
    }

    static string SafeSize(Variant v)
    {
        try
        {
            return v.VariantType switch
            {
                Variant.Type.PackedByteArray => v.AsByteArray().Length.ToString(),
                Variant.Type.PackedInt32Array => v.AsInt32Array().Length.ToString(),
                Variant.Type.PackedInt64Array => v.AsInt64Array().Length.ToString(),
                Variant.Type.PackedFloat32Array => v.AsFloat32Array().Length.ToString(),
                Variant.Type.PackedFloat64Array => v.AsFloat64Array().Length.ToString(),
                Variant.Type.PackedStringArray => v.AsStringArray().Length.ToString(),
                Variant.Type.PackedVector2Array => v.AsVector2Array().Length.ToString(),
                Variant.Type.PackedVector3Array => v.AsVector3Array().Length.ToString(),
                Variant.Type.PackedColorArray => v.AsColorArray().Length.ToString(),
                _ => "",
            };
        }
        catch { return ""; }
    }
}
#endif
