using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;

namespace Ms2Godot.Movement;

/// <summary>
/// A mount's row: Xml riding/%08d.xml, &lt;ms2&gt;&lt;riding&gt;&lt;basic .../&gt; with &lt;collision&gt; and &lt;shadow&gt;. Speeds are
/// in m/s like the client's (walkSpeed 1.0 when absent); the rider sequence names fall back to rideAniPC when empty.
/// </summary>
public sealed class RideData
{
    public int Id;
    public string Kfm, Kfm2, Nif, RideBone, IdleXMotion, RunXMotion;
    public float Scale = 1, WalkSpeed = 1, RunSpeed, RunXSpeed, SwimSpeed;
    public bool EnableSwim;
    public float[] RideTranslation = new float[3], RideRotation = new float[3];
    public string AniPC = "Ride_Idle_A", AniPCIdle, AniPCRun, AniPCJump;
    public float Width = 100, Depth = 100, Height = 100;

    public static RideData Load(PackSet packs, int id)
    {
        var x = XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", $"riding/{id:D8}.xml")));
        var riding = x.Root?.Element("riding") ?? throw new InvalidDataException($"riding {id}: no <riding>");
        var b = riding.Element("basic") ?? throw new InvalidDataException($"riding {id}: no <basic>");
        var d = new RideData
        {
            Id = id,
            Kfm = (string)b.Attribute("kfm"),
            Kfm2 = (string)b.Attribute("kfm2"),
            Nif = (string)b.Attribute("nif"),
            RideBone = (string)b.Attribute("rideBone"),
            IdleXMotion = (string)b.Attribute("idleXMotion"),
            RunXMotion = (string)b.Attribute("runXMotion"),
            Scale = F(b, "scale", 1),
            WalkSpeed = F(b, "walkSpeed", 1),
            RunSpeed = F(b, "runSpeed", 0),
            RunXSpeed = F(b, "runXSpeed", 0),
            SwimSpeed = F(b, "swimSpeed", 0),
            EnableSwim = F(b, "enableSwim", 0) != 0,
            RideTranslation = V(b, "rideTranslation"),
            RideRotation = V(b, "rideRotation"),
        };
        var pc = (string)b.Attribute("rideAniPC");
        if (!string.IsNullOrEmpty(pc)) d.AniPC = pc;
        d.AniPCIdle = Or((string)b.Attribute("rideAniPC_Idle"), d.AniPC);
        d.AniPCRun = Or((string)b.Attribute("rideAniPC_Run"), d.AniPC);
        d.AniPCJump = Or((string)b.Attribute("rideAniPC_Jump"), d.AniPC);
        if (riding.Element("collision") is { } c)
        {
            d.Width = F(c, "width", 100);
            d.Depth = F(c, "depth", 100);
            d.Height = F(c, "height", 100);
        }
        return d;
    }

    static string Or(string a, string b) => string.IsNullOrEmpty(a) ? b : a;

    static float F(XElement e, string name, float def) =>
        float.TryParse((string)e.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;

    static float[] V(XElement e, string name)
    {
        var r = new float[3];
        var parts = ((string)e.Attribute(name) ?? "").Split(',');
        for (int i = 0; i < 3 && i < parts.Length; i++)
            float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out r[i]);
        return r;
    }
}
