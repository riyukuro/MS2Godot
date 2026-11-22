using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;

namespace Ms2.Formats.World;

/// <summary>
/// The home option dialog's three lists, UgcHomeOptionData::Load (@0x140b67fa0, ugchomebgdata.cpp:99-250): the backgrounds
/// (table/ugcBackground.xml, &lt;ms2&gt;&lt;list id path&gt;), the lights (ugcHousingLight.xml, &lt;list id ambientcolor="r, g, b"&gt;,
/// each channel / 255) and the cameras (ugcHousingCamera.xml, &lt;list id offset upAxis&gt;). Each list is keyed by the count of
/// rows loaded before it (the client's map key), so the key is the document position; the name is string table 0x33
/// ("stringUgc") by the row's id.
/// <para>STILL-OPEN: the vector parse of ambientcolor / offset / upAxis is read here as comma separated floats; the client's
/// parser (and the default DAT_143b01a60 of a missing upAxis) is not recovered, so a missing or bad part stays 0.</para>
/// </summary>
public sealed class UgcHomeOptionData
{
    public sealed record Background(int Id, string Path, string Name);
    public sealed record Light(int Id, (float R, float G, float B) Color, string Name);
    public sealed record Camera(int Id, (float X, float Y, float Z) Offset, (float X, float Y, float Z) UpAxis, string Name);

    public const string BackgroundFile = "ugcbackground.xml", LightFile = "ugchousinglight.xml", CameraFile = "ugchousingcamera.xml";
    /// <summary>The name table: CStringTable::FUN_141811980(0x33, id).</summary>
    public const int NameTable = 0x33;

    public List<Background> Backgrounds { get; } = new();
    public List<Light> Lights { get; } = new();
    public List<Camera> Cameras { get; } = new();

    /// <param name="name">string table 0x33 by id ("" when it has none).</param>
    public UgcHomeOptionData(PackSet packs, Func<int, string> name)
        : this(Doc(packs, BackgroundFile), Doc(packs, LightFile), Doc(packs, CameraFile), name) { }

    /// <summary>The lists from the xml itself (tests).</summary>
    public UgcHomeOptionData(XDocument bg, XDocument light, XDocument camera, Func<int, string> name)
    {
        foreach (var e in Rows(bg)) Backgrounds.Add(new Background(Id(e), (string)e.Attribute("path") ?? "", name(Id(e))));
        foreach (var e in Rows(light))
        {
            var (r, g, b) = Vec((string)e.Attribute("ambientcolor"));
            Lights.Add(new Light(Id(e), (r / 255f, g / 255f, b / 255f), name(Id(e))));
        }
        foreach (var e in Rows(camera))
            Cameras.Add(new Camera(Id(e), Vec((string)e.Attribute("offset")), Vec((string)e.Attribute("upAxis")), name(Id(e))));
    }

    static XDocument Doc(PackSet packs, string file) =>
        HousingTable.EntryOf(packs, file) is { } e ? PackXml.Parse(packs.Read("Xml", e)) : null;

    static IEnumerable<XElement> Rows(XDocument d) =>
        d?.Root is { Name.LocalName: "ms2" } root ? root.Elements("list") : Enumerable.Empty<XElement>();

    static int Id(XElement e) =>
        int.TryParse((string)e.Attribute("id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

    static (float, float, float) Vec(string s)
    {
        var p = (s ?? "").Split(',');
        float F(int i) => i < p.Length && float.TryParse(p[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;
        return (F(0), F(1), F(2));
    }
}
