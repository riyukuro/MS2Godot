using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;

namespace Ms2.Formats.World;

/// <summary>
/// The camera part of a field's entry in Xml.m2d : table/fielddata.xml (&lt;environment&gt; without a locale).
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

public sealed class FieldData
{
    public const string Archive = "Xml";
    public const string Entry = "table/fielddata.xml";

    public int Id;
    public string XBlock;
    public FieldCamera Camera = new();

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
            var fov = env.Element("fov");
            if (fov != null) Read(fov, "value", ref o.Camera.FovDeg);
            return o;
        }
        return null;
    }

    static void Read(XElement e, string name, ref float v)
    {
        var a = (string)e.Attribute(name);
        if (a != null && float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) v = f;
    }
}
