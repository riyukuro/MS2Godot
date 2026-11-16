using System.Globalization;
using System.Xml;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;

namespace Ms2.Formats.World;

/// <summary>One entity of a field's entity set: its flat model name and the properties it overrides.</summary>
public sealed class XEntity
{
    public string Id, ModelName, Name;
    public Dictionary<string, string> Props = new(StringComparer.Ordinal);
    /// <summary>Every &lt;set index=.. value=..&gt; of a property, in file order (NpcList: npc id -> count).</summary>
    public Dictionary<string, List<(string index, string value)>> Sets = new(StringComparer.Ordinal);
}

/// <summary>
/// A field entity set (Resource/Exported.m2d : xblock/&lt;id&gt;_&lt;name&gt;.xblock, Gamebryo WorldBuilder XML).
/// Only overrides are stored per entity; defaults come from the flat model (<see cref="FlatLibrary"/>).
/// </summary>
public sealed class XBlock
{
    public const string Archive = "Resource/Exported";
    public List<XEntity> Entities = new();

    public static XBlock Load(PackSet packs, string xblockName)
    {
        var entry = $"xblock/{xblockName.ToLowerInvariant()}.xblock";
        return Parse(packs.Read(Archive, entry));
    }

    public static XBlock Parse(byte[] data)
    {
        var xb = new XBlock();
        using var ms = new MemoryStream(data);
        using var r = XmlReader.Create(ms, new XmlReaderSettings { IgnoreComments = true, IgnoreWhitespace = true, DtdProcessing = DtdProcessing.Ignore });
        XEntity cur = null;
        string prop = null;
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element) continue;
            switch (r.Name)
            {
                case "entity":
                    cur = new XEntity { Id = r.GetAttribute("id"), ModelName = r.GetAttribute("modelName"), Name = r.GetAttribute("name") };
                    xb.Entities.Add(cur);
                    break;
                case "property":
                    prop = r.GetAttribute("name");
                    break;
                case "set":
                    // list properties carry several <set index=..> elements; the first one is kept
                    if (cur != null && prop != null)
                    {
                        cur.Props.TryAdd(prop, r.GetAttribute("value") ?? "");
                        if (!cur.Sets.TryGetValue(prop, out var sets)) cur.Sets[prop] = sets = new();
                        sets.Add((r.GetAttribute("index"), r.GetAttribute("value") ?? ""));
                    }
                    break;
            }
        }
        return xb;
    }
}

/// <summary>Flat model defaults (Resource/Exported.m2d : flat/**/&lt;model&gt;.flat), found by model name.</summary>
public sealed class FlatModel
{
    public string Name;
    public HashSet<string> Mixins = new(StringComparer.Ordinal);
    public Dictionary<string, string> Props = new(StringComparer.Ordinal);
}

public sealed class FlatLibrary
{
    readonly PackSet packs;
    readonly Dictionary<string, string> entryByName = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, FlatModel> cache = new(StringComparer.OrdinalIgnoreCase);

    public FlatLibrary(PackSet packs)
    {
        this.packs = packs;
        foreach (var e in packs.List(XBlock.Archive))
            if (e.StartsWith("flat/", StringComparison.OrdinalIgnoreCase) && e.EndsWith(".flat", StringComparison.OrdinalIgnoreCase))
                entryByName.TryAdd(Path.GetFileNameWithoutExtension(e), e);
    }

    /// <summary>The flat model, or null when the data has none of that name.</summary>
    public FlatModel Get(string modelName)
    {
        if (modelName == null) return null;
        if (cache.TryGetValue(modelName, out var f)) return f;
        f = null;
        if (entryByName.TryGetValue(modelName, out var entry))
        {
            f = new FlatModel { Name = modelName };
            using var ms = new MemoryStream(packs.Read(XBlock.Archive, entry));
            using var r = XmlReader.Create(ms, new XmlReaderSettings { IgnoreComments = true, IgnoreWhitespace = true, DtdProcessing = DtdProcessing.Ignore });
            string prop = null;
            while (r.Read())
            {
                if (r.NodeType != XmlNodeType.Element) continue;
                if (r.Name == "mixin") f.Mixins.Add(r.GetAttribute("name"));
                else if (r.Name == "property") prop = r.GetAttribute("name");
                else if (r.Name == "set" && prop != null) f.Props.TryAdd(prop, r.GetAttribute("value") ?? "");
            }
        }
        cache[modelName] = f;
        return f;
    }
}

/// <summary>An entity with its flat defaults merged under its overrides, plus typed accessors.</summary>
public sealed class FieldEntity
{
    public XEntity Raw;
    public FlatModel Flat;

    public string ModelName => Raw.ModelName;
    public string Name => Raw.Name;

    public string Get(string key) =>
        Raw.Props.TryGetValue(key, out var v) ? v : Flat != null && Flat.Props.TryGetValue(key, out var d) ? d : null;

    public bool Has(string key) => Get(key) != null;
    public bool HasMixin(string m) => Flat != null && Flat.Mixins.Contains(m);

    public bool Bool(string key, bool def = false)
    {
        var v = Get(key);
        return v == null ? def : v.Equals("True", StringComparison.OrdinalIgnoreCase) || v == "1";
    }

    public double Number(string key, double def = 0)
    {
        var v = Get(key);
        return v != null && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : def;
    }

    public double[] Numbers(string key)
    {
        var v = Get(key);
        if (v == null) return null;
        return v.Split(',').Select(s => double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0).ToArray();
    }

    public Vec3 Vector(string key, Vec3 def = default)
    {
        var a = Numbers(key);
        return a != null && a.Length >= 3 ? new Vec3(a[0], a[1], a[2]) : def;
    }

    public Vec3 Position => Vector("Position");
    /// <summary>Euler degrees (x, y, z), Z-up.</summary>
    public Vec3 RotationDeg => Vector("Rotation");
    public double Scale => Number("Scale", 1);

    /// <summary>
    /// Placement transform: R = Rx * Ry * Rz (degrees, counter-clockwise about each axis), uniform scale; the
    /// order the Gamebryo scene graph service uses for an entity's Rotation property.
    /// </summary>
    public Xform Transform
    {
        get
        {
            var r = RotationDeg;
            const double d2r = Math.PI / 180;
            var q = Quat.FromAxis(0, r.X * d2r) * Quat.FromAxis(1, r.Y * d2r) * Quat.FromAxis(2, r.Z * d2r);
            return new Xform(Position, q.ToMatrix(), Scale);
        }
    }
}

/// <summary>A field: its entity set resolved against the flat library.</summary>
public sealed class FieldMap
{
    public string XBlockName;
    public List<FieldEntity> Entities = new();

    public static FieldMap Load(PackSet packs, FlatLibrary flats, string xblockName)
    {
        var xb = XBlock.Load(packs, xblockName);
        var map = new FieldMap { XBlockName = xblockName };
        foreach (var e in xb.Entities)
            map.Entities.Add(new FieldEntity { Raw = e, Flat = flats.Get(e.ModelName) });
        return map;
    }

    public IEnumerable<FieldEntity> OfModel(string modelName) =>
        Entities.Where(e => string.Equals(e.ModelName, modelName, StringComparison.OrdinalIgnoreCase));
}
