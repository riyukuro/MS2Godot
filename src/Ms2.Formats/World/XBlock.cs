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
    /// <summary>egf FlatModel::ContainsModel: the entity's own model or one of its mixins. A flat model holds its own id in
    /// its super-model set (FlatModel::SetID adds it, FlatModel.cpp:72-74), so an entity of model X contains X. The flat is
    /// looked up by name ignoring case, and so is the own-model compare.</summary>
    public bool HasMixin(string m) => Flat != null && (string.Equals(Flat.Name, m, StringComparison.OrdinalIgnoreCase) || Flat.Mixins.Contains(m));

    /// <summary>The effect archive, where the effect models live.</summary>
    public const string EffectArchive = "Resource/Model/Effect";

    /// <summary>
    /// A visible EffectMesh entity whose model plays through the effect runtime (world/FieldEffects): its alpha and transform
    /// controllers and its particle systems run, looping. The EffectMesh flat model sets IsStatic False. Gamebryo's
    /// SceneGraphService makes an entity's scene graph dynamic when IsStatic is false (SetIsDynamic(!isStatic),
    /// scenegraphservice.cpp:2377-2380). It then updates every dynamic scene graph each tick on the game clock, with the
    /// controllers on (:102-106, UpdateDynamicEntities :208-240). Nothing there depends on the model's archive, so effect
    /// models from the map archive (the 02020120 crystal rings, the light and aurora effects) play too. A skinned model from
    /// another archive (the butterflies, the crow crowds) stays on the prop path, because the effect runtime has no skinning.
    /// Its particle systems run on the dynamic-model particle path (the game's FieldBuilder.ParticleEntities), at the entity's
    /// transform. STILL-OPEN: there they do not follow the skinned bones, and no such model has been checked against the client.
    /// </summary>
    public bool PlaysAsEffect(string archive, ModelData model) => Bool("IsVisible", true) && IsEffectModel(archive, model);

    /// <summary>An EffectMesh entity whose model plays through the effect runtime when drawn (<see cref="PlaysAsEffect"/>
    /// without the IsVisible check: a portal's entity is built hidden and shown by its CPortal).</summary>
    public bool IsEffectModel(string archive, ModelData model) =>
        HasMixin("EffectMesh")
        && (string.Equals(archive, EffectArchive, StringComparison.OrdinalIgnoreCase) || (archive != null && model != null && !model.Meshes.Any(m => m.Skinned)));

    /// <summary>
    /// The entity's meshes cast shadows. On SceneGraphAdded (WorldService::FUN_141bbfe30, worldservice_1.cpp:3637) the client
    /// runs FUN_141567000 (unit_141567000.cpp:112-124): for a Mesh (0xbd4f8e8e) or Actor (0x48ccc02d) entity it reads
    /// IsCastingShadow (0xd89e9b12, false when unset) and PrepareLoadedNif runs SetShadowCaster(root, it), which sets or
    /// clears the caster flag of every render object. ecr LightService::OnRenderedEntityAdded (LightService.cpp:1936-1950)
    /// also attaches such an entity to every shadow generator as an unaffected caster. Right after, WorldService::FUN_141bbd490
    /// (worldservice_1.cpp:3412-3432) keeps every entity with IsCastingShadow, whatever its model, in its caster list
    /// (spawned game objects excepted), and SetShadowEnabled (worldservice_1.cpp:6992-7004) runs SetShadowCaster(root, on) on
    /// that list. An entity of another model without IsCastingShadow gets no caster mark, and a loaded NIF carries none
    /// (MS2_IS_SHADOW_CASTER, ms2shadowclickgenerator.cpp:103-108), so it never casts. Either way the entity casts when
    /// IsCastingShadow is set; the shadow level is applied by the caller (FieldShadows.MapObjects).
    /// </summary>
    public bool CastsShadow => Bool("IsCastingShadow");

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

    // Parsed once per key: the same string always gives the same doubles, and Position / Rotation / Scale are read
    // several times per entity. The returned array is shared, so callers read it and never write it.
    Dictionary<string, double[]> numbers;

    public double[] Numbers(string key)
    {
        var v = Get(key);
        if (v == null) return null;
        numbers ??= new Dictionary<string, double[]>(4, StringComparer.Ordinal);
        if (numbers.TryGetValue(key, out var cached)) return cached;
        var a = new double[CountFields(v)];
        FillFields(v, a);
        numbers[key] = a;
        return a;
    }

    public Vec3 Vector(string key, Vec3 def = default)
    {
        var v = Get(key);
        if (v == null) return def;
        // the first three fields without allocating: the same values Numbers(key) holds
        Span<double> f = stackalloc double[3];
        return FillFields(v, f) >= 3 ? new Vec3(f[0], f[1], f[2]) : def;
    }

    static int CountFields(string v)
    {
        int n = 1;
        foreach (var c in v) if (c == ',') n++;
        return n;
    }

    /// <summary>Parses the comma-separated fields in order into dst (as many as fit); a field that does not parse is 0.</summary>
    static int FillFields(string v, Span<double> dst)
    {
        int n = 0, start = 0;
        while (start <= v.Length && n < dst.Length)
        {
            int end = v.IndexOf(',', start);
            if (end < 0) end = v.Length;
            dst[n++] = double.TryParse(v.AsSpan(start, end - start).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
            start = end + 1;
        }
        return n;
    }

    public Vec3 Position => Vector("Position");
    /// <summary>The file space a field effect entity's effect plays in (world/FieldEffects): the full placement rotation
    /// R = Rx * Ry * Rz and the scale of <see cref="Transform"/>, with no translation (the position goes on the node).</summary>
    public Xform EffectSpace { get { var t = Transform; return new Xform(new Vec3(0, 0, 0), t.R, t.S); } }
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
