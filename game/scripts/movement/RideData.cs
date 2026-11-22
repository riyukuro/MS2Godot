using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;

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
    /// <summary>basic disablePickUp (CRideeData m_basic.m_nDisablePickUp): a rider cannot pick up drops (mypc.cpp:4716-4721).</summary>
    public bool DisablePickUp;
    /// <summary>basic type (CRideeData m_basic.m_nType, FUN_140785190): 0 default, 1 battle, 2 object
    /// (<see cref="Ms2.Ui.RideOff.TypeOf"/>).</summary>
    public int Type;
    /// <summary>basic enableRideOffUI (m_basic.m_nEnableRideOffUI, XmlGetBool): the ride-off button and the 0xf ride-off action.</summary>
    public bool EnableRideOffUI;
    public float[] RideTranslation = new float[3], RideRotation = new float[3];
    public string AniPC = "Ride_Idle_A", AniPCIdle, AniPCRun, AniPCJump, AniPCSPIdle, AniPCSPRun, AniPCSPJump;
    /// <summary>RideeData::Collision (@0x140783800): the box size, 100 each when absent (ctor @0x1407836e0), and whether the
    /// mount carries the kinematic box (Ridee::MakeNxBoxController).</summary>
    public float Width = 100, Depth = 100, Height = 100;
    public bool Physic;

    public static RideData Load(PackSet packs, int id)
    {
        var x = PackXml.Parse(packs.Read("Xml", $"riding/{id:D8}.xml"));
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
            DisablePickUp = F(b, "disablePickUp", 0) != 0,
            Type = Ms2.Ui.RideOff.TypeOf((string)b.Attribute("type")),
            EnableRideOffUI = QueryBool((string)b.Attribute("enableRideOffUI"), false),
            RideTranslation = V(b, "rideTranslation"),
            RideRotation = V(b, "rideRotation"),
        };
        var pc = (string)b.Attribute("rideAniPC");
        if (!string.IsNullOrEmpty(pc)) d.AniPC = pc;
        d.AniPCIdle = Or((string)b.Attribute("rideAniPC_Idle"), d.AniPC);
        d.AniPCRun = Or((string)b.Attribute("rideAniPC_Run"), d.AniPC);
        d.AniPCJump = Or((string)b.Attribute("rideAniPC_Jump"), d.AniPC);
        d.AniPCSPIdle = Or((string)b.Attribute("rideAniPC_SP_Idle"), d.AniPC);
        d.AniPCSPRun = Or((string)b.Attribute("rideAniPC_SP_Run"), d.AniPC);
        d.AniPCSPJump = Or((string)b.Attribute("rideAniPC_SP_Jump"), d.AniPC);
        if (riding.Element("collision") is { } c)
        {
            d.Width = F(c, "width", 100);
            d.Depth = F(c, "depth", 100);
            d.Height = F(c, "height", 100);
            d.Physic = QueryBool((string)c.Attribute("physic"), false);
        }
        return d;
    }

    /// <summary>The rider sequences StateRide picks from (<see cref="Ms2.Net.Replay.StateRideAnim"/>).</summary>
    public Ms2.Net.Replay.RiderSequences Rider => new(AniPC, AniPCIdle, AniPCRun, AniPCJump, AniPCSPIdle, AniPCSPRun, AniPCSPJump);

    static string Or(string a, string b) => string.IsNullOrEmpty(a) ? b : a;

    /// <summary>TiXmlElement::QueryBoolAttribute: StringEqual (a case-insensitive prefix test) on true / yes / 1 and
    /// false / no / 0; anything else, or an empty value, leaves the default.</summary>
    static bool QueryBool(string v, bool def)
    {
        if (string.IsNullOrEmpty(v)) return def;
        bool Is(string tag) => v.StartsWith(tag, StringComparison.OrdinalIgnoreCase);
        if (Is("true") || Is("yes") || Is("1")) return true;
        if (Is("false") || Is("no") || Is("0")) return false;
        return def;
    }

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
