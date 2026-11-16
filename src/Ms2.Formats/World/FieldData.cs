using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;

namespace Ms2.Formats.World;

/// <summary>
/// The camera and bloom parts of a field's entry in Xml.m2d : table/fielddata.xml (&lt;environment&gt; without a locale).
/// Unset values keep the client's defaults (CFieldDataCli / CFieldData constructors).
/// </summary>
public sealed class FieldCamera
{
    public float Distance = 1100f;              // <camera distance>: the farthest zoom
    public float HAngleDeg = 45f;               // <camera hangle>: fixed yaw; the eye sits at hangle + 180 about the target
    public float VAngleDeg = 30f;               // <camera vangle>: angle from the up axis at the farthest zoom
    public float ZoomOutHeightOffset = 60f;     // target height above the player at the farthest zoom
    public float ZoomInHeightOffset = 95f;      // ... at the nearest zoom
    public float FovDeg = 40f;                  // <fov value>: vertical field of view
}

/// <summary>The &lt;hdr&gt; bloom settings (CFieldDataCli defaults when the entry has none).</summary>
public sealed class FieldHdr
{
    public float Threshold = 0.27f;             // bright pass: max(c - threshold, 0)
    public float Offset = 30f;                  // bright pass: x / (x + offset)
    public float BloomScale = 2f;               // final: scene + bloomscale * bloom
    public float GlowScale = 0.1f;              // glow-map share of the bright pass
}

public sealed class FieldData
{
    public const string Archive = "Xml";
    public const string Entry = "table/fielddata.xml";

    public int Id;
    public string XBlock;
    public FieldCamera Camera = new();
    public FieldHdr Hdr = new();
    /// <summary>&lt;property checkClimb&gt;: wall climbing allowed (CFieldData default 1).</summary>
    public bool CheckClimb = true;

    /// <summary>The entry whose default environment uses the given xblock (case-insensitive), or null.</summary>
    public static FieldData FindByXBlock(PackSet packs, string xblockName)
    {
        var doc = XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read(Archive, Entry)));
        foreach (var fd in doc.Root.Elements("fieldData"))
        {
            var env = fd.Elements("environment").FirstOrDefault(e => e.Attribute("locale") == null);
            var xb = (string)env?.Element("xblock")?.Attribute("name");
            if (xb == null || !xb.Equals(xblockName, StringComparison.OrdinalIgnoreCase)) continue;
            var o = new FieldData { Id = (int?)fd.Attribute("id") ?? 0, XBlock = xb };
            var cam = env.Element("camera");
            if (cam != null)
            {
                Read(cam, "distance", ref o.Camera.Distance);
                Read(cam, "hangle", ref o.Camera.HAngleDeg);
                Read(cam, "vangle", ref o.Camera.VAngleDeg);
                Read(cam, "zoomOutHeightOffset", ref o.Camera.ZoomOutHeightOffset);
                Read(cam, "zoomInHeightOffset", ref o.Camera.ZoomInHeightOffset);
            }
            var prop = env.Element("property") ?? fd.Element("property");
            if ((string)prop?.Attribute("checkClimb") is { } cc) o.CheckClimb = cc != "0";
            var fov = env.Element("fov");
            if (fov != null) Read(fov, "value", ref o.Camera.FovDeg);
            var hdr = env.Element("hdr");
            if (hdr != null)
            {
                Read(hdr, "threshold", ref o.Hdr.Threshold);
                Read(hdr, "offset", ref o.Hdr.Offset);
                Read(hdr, "bloomscale", ref o.Hdr.BloomScale);
                Read(hdr, "glowscale", ref o.Hdr.GlowScale);
            }
            return o;
        }
        return null;
    }

    /// <summary>The xblock name of a field id (its default environment), or null.</summary>
    public static string XBlockOf(PackSet packs, int fieldId)
    {
        var doc = XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read(Archive, Entry)));
        var fd = doc.Root.Elements("fieldData").FirstOrDefault(f => (int?)f.Attribute("id") == fieldId);
        var env = fd?.Elements("environment").FirstOrDefault(e => e.Attribute("locale") == null);
        return (string)env?.Element("xblock")?.Attribute("name");
    }

    static void Read(XElement e, string name, ref float v)
    {
        var a = (string)e.Attribute(name);
        if (a != null && float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) v = f;
    }
}
