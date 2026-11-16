using System.Globalization;
using System.Xml.Linq;
using Godot;
using Ms2.Net.Packets;

namespace Ms2Godot.Ui.Create;

/// <summary>
/// What an item model lets the player customize (itemmodel &lt;customize scale rotation translation&gt; and the first
/// &lt;slot&gt;'s &lt;scale&gt; and &lt;custom&gt; lists), as the client's controllers read it:
/// hair (slot HR) uses CItemMorphController when its last asset replaces the hair (one size slider per &lt;scale&gt;),
/// else CPonyTailController (one slider; positions are the second asset's &lt;custom&gt; list); a face decal (slot FD) uses
/// CMakeupController (one slider, an angle, positions from &lt;decal&gt;/&lt;custom&gt;). Caps are not offered at creation.
/// </summary>
public sealed class ItemCustomize
{
    public enum Kind { None, Hair, Decal, Cap }

    /// <summary>A &lt;scale&gt; element (ModelFloatList): range, inverted display, the values a new item picks from.</summary>
    public sealed record Range(float Min, float Max, bool Reverse, float[] Values)
    {
        /// <summary>ModelFloatList::GetRandomValue: one of the values, 1 when there are none.</summary>
        public float Random(Random rng) => Values.Length == 0 ? 1f : Values[rng.Next(Values.Length)];
        /// <summary>The slider shows reversed ranges flipped: max - value + min.</summary>
        public float Display(float v) => Reverse ? Max - v + Min : v;
    }

    /// <summary>A &lt;custom&gt; preset: its icon and the position and rotation it applies.</summary>
    public sealed record Preset(string Icon, Vector3 Position, Vector3 Rotation);

    public Kind Type;
    public bool Scale, Rotation, Translation, Morph;
    public readonly List<Range> Ranges = new();
    public readonly List<Preset> Positions = new();
    /// <summary>The &lt;custom&gt; lists of the pony assets (hair), in asset order.</summary>
    public readonly List<List<Preset>> PonyCustoms = new();
    public string Movie = "";

    /// <summary>ItemModelHead::FUN_1405471d0: the item list marks the tile editable (the option panel opens on it).</summary>
    public bool Editable => Type != Kind.None && (Scale || Rotation || Translation);

    /// <summary>EditItems::GetFlags: 1 size, 2 size of a morph, 4 angle, 8 position.</summary>
    public int EditFlags => (Scale ? (Morph ? 2 : 1) : 0) | (Rotation ? 4 : 0) | (Translation ? 8 : 0);

    /// <summary>The controller's size slider count (vfunc_27): one per &lt;scale&gt; for a morph, one otherwise.</summary>
    public int SliderCount => Morph ? Ranges.Count : 1;

    public Range RangeAt(int i) => i < Ranges.Count ? Ranges[i] : new Range(0, 1, false, Array.Empty<float>());

    public static ItemCustomize Read(XElement model, string movie)
    {
        var c = new ItemCustomize { Movie = movie ?? "" };
        var cust = model?.Element("customize");
        var slot = model?.Element("slots")?.Element("slot");
        if (cust == null || slot == null) return c;
        c.Type = (string)slot.Attribute("name") switch { "HR" => Kind.Hair, "FD" => Kind.Decal, "CP" => Kind.Cap, _ => Kind.None };
        c.Scale = (string)cust.Attribute("scale") == "1";
        c.Rotation = (string)cust.Attribute("rotation") == "1";
        c.Translation = (string)cust.Attribute("translation") == "1";
        foreach (var s in slot.Elements("scale"))
            c.Ranges.Add(new Range(F(s, "min", 0), F(s, "max", 1), (string)s.Attribute("reverse") == "1",
                ((string)s.Attribute("value") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray()));
        var assets = slot.Elements("asset").ToList();
        if (c.Type == Kind.Hair)
        {
            c.Morph = assets.Count > 0 && ((string)assets[^1].Attribute("replace") ?? "0") != "0";
            foreach (var a in assets.Where(a => (string)a.Attribute("pony") == "1"))
                c.PonyCustoms.Add(Presets(a));
        }
        if (c.Type == Kind.Decal) c.Positions.AddRange(Presets(slot.Element("decal")));
        else if (assets.Count > 1) c.Positions.AddRange(Presets(assets[1]));
        return c;
    }

    static List<Preset> Presets(XElement parent) => parent == null ? new() : parent.Elements("custom")
        .Select(e => new Preset((string)e.Attribute("icon") ?? "", V((string)e.Attribute("position")), V((string)e.Attribute("rotation")))).ToList();

    static float F(XElement e, string attr, float def) =>
        float.TryParse((string)e.Attribute(attr), NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : def;

    static Vector3 V(string s)
    {
        var p = (s ?? "").Split(',');
        float At(int i) => i < p.Length && float.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : 0;
        return new Vector3(At(0), At(1), At(2));
    }
}

/// <summary>
/// An item's customize values (CHairExtraData / CDecalExtraData), filled as a new item fills them (vfunc_4: random
/// preset and random &lt;scale&gt; value) and written after its colour in the create request (vfunc_5).
/// </summary>
public sealed class CustomValues
{
    public readonly float[] Scale = { 0, 0 };
    public readonly Vector3[] Position = new Vector3[2], Rotation = new Vector3[2];   // hair: per pony part
    public float X, Y, DecalScale, Angle;                                             // decal: uv offset, scale, radians
    public int Pick = -1;

    public static CustomValues New(ItemCustomize c, Random rng)
    {
        var v = new CustomValues();
        if (c.Type == ItemCustomize.Kind.Hair)
        {
            // CHairExtraData::vfunc_4: one preset index for every pony part that has presets, then a value per <scale>
            int slot = 0;
            foreach (var list in c.PonyCustoms)
            {
                if (slot >= 2) break;
                if (list.Count == 0) continue;
                if (v.Pick < 0) v.Pick = rng.Next(list.Count);
                var p = list[Math.Min(v.Pick, list.Count - 1)];
                v.Position[slot] = p.Position; v.Rotation[slot] = p.Rotation;
                slot++;
            }
            for (int i = 0; i < 2; i++) v.Scale[i] = Math.Max(0, c.RangeAt(i).Random(rng));
        }
        else if (c.Type == ItemCustomize.Kind.Decal)
        {
            // CDecalExtraData::vfunc_4: a random preset gives x, y and the angle (rotation z), then a random scale
            if (c.Positions.Count > 0)
            {
                v.Pick = rng.Next(c.Positions.Count);
                var p = c.Positions[v.Pick];
                v.X = p.Position.X; v.Y = p.Position.Y; v.Angle = p.Rotation.Z;
            }
            v.DecalScale = c.RangeAt(0).Random(rng);
        }
        return v;
    }

    /// <summary>OnChangeSize(index, value): the value as the controller stores it (clamped to the range).</summary>
    public void SetSize(ItemCustomize c, int index, float value)
    {
        var r = c.RangeAt(index);
        value = Math.Clamp(value, Math.Min(r.Min, r.Max), Math.Max(r.Min, r.Max));
        if (c.Type == ItemCustomize.Kind.Decal) DecalScale = value;
        else if (index < 2) Scale[index] = value;
    }

    public float GetSize(ItemCustomize c, int index) => c.Type == ItemCustomize.Kind.Decal ? DecalScale : index < 2 ? Scale[index] : 0;

    /// <summary>OnChangeDegree: the makeup angle in degrees, kept in radians.</summary>
    public void SetDegree(float degrees) => Angle = Mathf.DegToRad(degrees);

    /// <summary>The angle the slider shows (vfunc_18): degrees, negative ones wrapped into 0..360.</summary>
    public float Degree => Mathf.RadToDeg(Angle) is var d && d < 0 ? d + 360 : d;

    /// <summary>OnChangePosition(n): the preset's position (decal: x and y; hair: every pony part's position and rotation).</summary>
    public void SetPosition(ItemCustomize c, int n)
    {
        if (c.Type == ItemCustomize.Kind.Decal)
        {
            if (n < 0 || n >= c.Positions.Count) return;
            X = c.Positions[n].Position.X; Y = c.Positions[n].Position.Y;
        }
        else
        {
            int slot = 0;
            foreach (var list in c.PonyCustoms)
            {
                if (slot >= 2) break;
                if (n < 0 || n >= list.Count) continue;
                Position[slot] = list[n].Position; Rotation[slot] = list[n].Rotation;
                slot++;
            }
        }
        Pick = n;
    }

    /// <summary>The floats after the colour in the item's appearance block (HR 14, FD 4; other slots none, a cap 13 zeros).</summary>
    public float[] Appearance(EquipSlot slot)
    {
        switch (slot)
        {
            case EquipSlot.HR:
                var f = new List<float>(14);
                for (int i = 0; i < 2; i++)
                    f.AddRange(new[] { Scale[i], Position[i].X, Position[i].Y, Position[i].Z, Rotation[i].X, Rotation[i].Y, Rotation[i].Z });
                return f.ToArray();
            case EquipSlot.FD:
                return new[] { X, Y, DecalScale, Angle };
            default:
                return CreateItem.DefaultAppearance(slot);
        }
    }
}
