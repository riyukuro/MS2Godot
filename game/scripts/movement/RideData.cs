using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Formats.World;

namespace Ms2Godot.Movement;

/// <summary>
/// A mount's row: Xml riding/%08d.xml, &lt;ms2&gt;&lt;riding&gt;&lt;basic .../&gt; with &lt;collision&gt; and &lt;shadow&gt;. Speeds are
/// in m/s like the client's (walkSpeed 1.0 when absent); the rider sequence names fall back to rideAniPC when empty.
/// </summary>
public sealed class RideData
{
    public int Id;
    public string Kfm, Kfm2, Nif, RideBone;
    /// <summary>basic idleXMotion / runXMotion, the mount's RunX sequences: the client keeps "Attack_01_A" / "Attack_01_B"
    /// unless the attribute is non-empty (unit_140781d80_ridingtable.cpp:368-373).</summary>
    public string IdleXMotion = "Attack_01_A", RunXMotion = "Attack_01_B";
    /// <summary>basic runXConsumeEp: the EP a RunX takes each second; 0 is free (Ridee::FUN_1417060d0).</summary>
    public int RunXConsumeEp;
    /// <summary>basic pressXEffectIdle / pressXEffectRun (played once as RunX starts) and pressXEffectIdleNonstop /
    /// pressXEffectRunNonstop (kept alive while it lasts), Ridee::UpdateRunEffects (ridee.cpp:717-799).</summary>
    public string PressXEffectIdle, PressXEffectRun, PressXEffectIdleNonstop, PressXEffectRunNonstop;
    public float Scale = 1, WalkSpeed = 1, RunSpeed, RunXSpeed, SwimSpeed;
    public bool EnableSwim;
    /// <summary>basic disablePickUp (CRideeData m_basic.m_nDisablePickUp): a rider cannot pick up drops (mypc.cpp:4716-4721).</summary>
    public bool DisablePickUp;
    /// <summary>basic type (CRideeData m_basic.m_nType, FUN_140785190): 0 default, 1 battle, 2 object
    /// (<see cref="Ms2.Ui.RideOff.TypeOf"/>).</summary>
    public int Type;
    /// <summary>basic enableRideOffUI (m_basic.m_nEnableRideOffUI, XmlGetBool): the ride-off button and the 0xf ride-off action.</summary>
    public bool EnableRideOffUI;
    /// <summary>rideOffBySkillEffect: the effect PCBase::vfunc_121 plays at the rider after an UnRideSkill (type 24)
    /// ride off (pcbase.cpp:3397-3401, :3423-3429); null when the row has none.</summary>
    public string RideOffBySkillEffect;
    /// <summary>basic hideRider (m_basic.m_nHideRider): the rider is hidden while it rides, AddHideReason(2) in PCBase::vfunc_120
    /// and RemoveHideReason(2) in vfunc_121 (pcbase.cpp:3331-3333, :3402-3404).</summary>
    public bool HideRider;
    /// <summary>rideSummonCastTime in seconds, null without it (RideeData::Basic, ridingtable.cpp:360-361).</summary>
    public float? SummonCastTime;
    /// <summary>basic hidePCInfo (m_basic.m_nHidePCInfo, an int query): non-zero hides the own player info tag, the exp
    /// bars, the minimap's players and the game menu, and refuses three window keys; 2 also hides the main player info,
    /// and above 1 the ride-on closes six windows (<see cref="Ms2.Ui.RideeRow"/>).</summary>
    public int HidePCInfo;
    /// <summary>basic useRidingUI (m_basic.m_nUseRidingUI, XmlGetBool): the main player info's riding slot mode
    /// (UIMainPlayerInfo::vfunc_oa0_20, uimainplayerinfo.cpp:820).</summary>
    public bool UseRidingUI;
    /// <summary>basic showRideeNameTag (m_basic.m_nShowRideeNameTag, XmlGetBool): the rider's name tag shows the mount's
    /// name (PC::vfunc_77, pc.cpp:1528-1538).</summary>
    public bool ShowRideeNameTag;
    /// <summary>basic skillSetID (m_basic.m_nSkillSetId): the skill set MyPC::vfunc_120 adds (mypc.cpp:3301).</summary>
    public int SkillSetId;
    /// <summary>basic loopEffect (m_basic.m_strLoopEffect): the effect Ridee::vfunc_12 starts at the mount once its model
    /// is in (ridee.cpp:527-534); null without one.</summary>
    public string LoopEffect;
    /// <summary>The default rider's nameTagOffsetY / hpBarOffsetY / nameTagHide / useRidingNavi (RideeData::Rider read
    /// from &lt;basic&gt;, ridingtable.cpp:171-177): Ridee::vfunc_89 / GetHpBarOffsetY (ridee.cpp:1127-1143), the name tag
    /// and HP bar hide of a rider (uinametag.cpp:331, uihpbar.cpp:209) and the quest beam (questnavigator.cpp:479).</summary>
    public float NameTagOffsetY, HpBarOffsetY;
    public bool NameTagHide, UseRidingNavi;
    public float[] RideTranslation = new float[3], RideRotation = new float[3];
    public string AniPC = "Ride_Idle_A", AniPCIdle, AniPCRun, AniPCJump, AniPCSPIdle, AniPCSPRun, AniPCSPJump;
    /// <summary>RideeData::Collision (@0x140783800): the box size, 100 each when absent (ctor @0x1407836e0), and whether the
    /// mount carries the kinematic box (Ridee::MakeNxBoxController).</summary>
    public float Width = 100, Depth = 100, Height = 100;
    public bool Physic;
    /// <summary>RideeData::Shadow (@0x140783980): &lt;shadow scale&gt;, 150 when absent (ctor @0x1407838e0); the side of the
    /// mount's CObjectShadow (Ridee::vfunc_49, ridee.cpp:848-858).</summary>
    public float ShadowScale = 150;
    /// <summary>CRideeData m_seats (std::map&lt;int, Rider&gt; by passengerid): &lt;passengers&gt;&lt;passenger&gt; (Passengers::FUN_140784010).</summary>
    public readonly SortedDictionary<int, RideSeat> Seats = new();

    /// <summary>Passengers::FUN_1407841b0 (CRideeData::GetPassenger): null when the map holds <paramref name="seat"/> or fewer
    /// entries, else the seat of that passengerid (null when absent: map::operator[] makes an empty one).</summary>
    public RideSeat Passenger(int seat) => Seats.Count <= seat ? null : Seats.GetValueOrDefault(seat);

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
            RunXConsumeEp = (int)F(b, "runXConsumeEp", 0),
            PressXEffectIdle = (string)b.Attribute("pressXEffectIdle"),
            PressXEffectRun = (string)b.Attribute("pressXEffectRun"),
            PressXEffectIdleNonstop = (string)b.Attribute("pressXEffectIdleNonstop"),
            PressXEffectRunNonstop = (string)b.Attribute("pressXEffectRunNonstop"),
            Scale = F(b, "scale", 1),
            WalkSpeed = F(b, "walkSpeed", 1),
            RunSpeed = F(b, "runSpeed", 0),
            RunXSpeed = F(b, "runXSpeed", 0),
            SwimSpeed = F(b, "swimSpeed", 0),
            EnableSwim = F(b, "enableSwim", 0) != 0,
            DisablePickUp = F(b, "disablePickUp", 0) != 0,
            Type = Ms2.Ui.RideOff.TypeOf((string)b.Attribute("type")),
            EnableRideOffUI = QueryBool((string)b.Attribute("enableRideOffUI"), false),
            RideOffBySkillEffect = (string)b.Attribute("rideOffBySkillEffect"),
            HideRider = QueryBool((string)b.Attribute("hideRider"), false),
            HidePCInfo = (int)F(b, "hidePCInfo", 0),
            UseRidingUI = QueryBool((string)b.Attribute("useRidingUI"), false),
            ShowRideeNameTag = QueryBool((string)b.Attribute("showRideeNameTag"), false),
            SkillSetId = (int)U(b, "skillSetID"),     // XmlGetAttr as unsigned (unit_140781d80_ridingtable.cpp:357); a float loses the last digits
            LoopEffect = (string)b.Attribute("loopEffect") is { Length: > 0 } loop ? loop : null,
            NameTagOffsetY = F(b, "nameTagOffsetY", 0),
            HpBarOffsetY = F(b, "hpBarOffsetY", 0),
            NameTagHide = F(b, "nameTagHide", 0) != 0,
            UseRidingNavi = F(b, "useRidingNavi", 0) != 0,
            RideTranslation = V(b, "rideTranslation"),
            RideRotation = V(b, "rideRotation"),
        };
        if ((string)b.Attribute("idleXMotion") is { Length: > 0 } ixm) d.IdleXMotion = ixm;
        if ((string)b.Attribute("runXMotion") is { Length: > 0 } rxm) d.RunXMotion = rxm;
        if (b.Attribute("rideSummonCastTime") is { } sct && float.TryParse((string)sct, NumberStyles.Float,
                CultureInfo.InvariantCulture, out float sec))
            d.SummonCastTime = sec;
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
        if (riding.Element("shadow") is { } sh) d.ShadowScale = F(sh, "scale", 150);
        foreach (var p in riding.Element("passengers")?.Elements("passenger") ?? Enumerable.Empty<XElement>())
        {
            var seat = RideSeat.Read(p);
            d.Seats[seat.Id] = seat;
        }
        // The GMS2 data keeps a mount's passengers apart, in riding/passenger/%08d.xml (&lt;ms2&gt;&lt;ridepassenger
        // passengerid rideBone ...&gt;, one element per seat, the attributes of a KMS &lt;passenger&gt;); the KMS reader
        // (Passengers::FUN_140784010) only knows the row's own &lt;passengers&gt;, so this is the GMS2 layout, read when the
        // row has none.
        if (d.Seats.Count == 0 && packs.Exists("Xml", $"riding/passenger/{id:D8}.xml"))
            foreach (var p in PackXml.Parse(packs.Read("Xml", $"riding/passenger/{id:D8}.xml")).Root?.Elements("ridepassenger")
                         ?? Enumerable.Empty<XElement>())
            {
                var seat = RideSeat.Read(p);
                d.Seats[seat.Id] = seat;
            }
        d.EffectDummies = LoadEffectDummies(packs, id);
        return d;
    }

    /// <summary>
    /// CRideeData::m_effectDummies, LoadRidingEffectDummy @0x140784de0 (ridingtable.cpp:784-829): a copy of the common
    /// rows (CommonEffectDummy, table/defaulteffectdummy.xml), then each &lt;ms2&gt;&lt;effectdummy&gt;&lt;dummy&gt; of
    /// Xml riding/effectdummy/%08d.xml parsed over the first row of its name or added at the end, as an npc's
    /// (<see cref="EffectDummyTable.MergeNpc"/>). Ridee::vfunc_12 attaches them before it looks the seat up by name
    /// (ridee.cpp:496-500), so a rideBone such as Eff_Head or Eff_UI is one of these.
    /// </summary>
    public IReadOnlyList<EffectDummy> EffectDummies = Array.Empty<EffectDummy>();

    static IReadOnlyList<EffectDummy> LoadEffectDummies(PackSet packs, int id)
    {
        var defaults = CommonDummies.GetValue(packs, p => EffectDummyTable.Load(p).Default);
        string entry = $"riding/effectdummy/{id:D8}.xml";
        return packs.Exists("Xml", entry) ? EffectDummyTable.MergeNpc(defaults, PackXml.Parse(packs.Read("Xml", entry)).Root) : defaults;
    }

    /// <summary>The common rows per pack set, read once.</summary>
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PackSet, IReadOnlyList<EffectDummy>> CommonDummies = new();

    /// <summary>CRideeData::GetKfmName (ridingtable.cpp:684-689): kfm2 for a rider of gender 0 when the row has one, else
    /// kfm. Ridee::FUN_1417043a0 passes the PC rider's GetGender, 1 for any other rider (ridee.cpp:417-420).</summary>
    public string KfmFor(int gender) => gender == 0 && !string.IsNullOrEmpty(Kfm2) ? Kfm2 : Kfm;

    /// <summary>The row's UI part (<see cref="Ms2.Ui.RideeRow"/>), what the HUD reads of the own mount.</summary>
    public Ms2.Ui.RideeRow UiRow => uiRow ??= new(Id, HidePCInfo, UseRidingUI, EnableRideOffUI, ShowRideeNameTag, NameTagHide, UseRidingNavi);
    Ms2.Ui.RideeRow uiRow;

    /// <summary>The rider sequences StateRide picks from (<see cref="Ms2.Net.Replay.StateRideAnim"/>).</summary>
    public Ms2.Net.Replay.RiderSequences Rider => new(AniPC, AniPCIdle, AniPCRun, AniPCJump, AniPCSPIdle, AniPCSPRun, AniPCSPJump);

    internal static string Or(string a, string b) => string.IsNullOrEmpty(a) ? b : a;

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

    /// <summary>XmlGetAttr of an unsigned: the number, else 0.</summary>
    internal static uint U(XElement e, string name) =>
        uint.TryParse((string)e.Attribute(name), NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : 0;

    internal static float F(XElement e, string name, float def) =>
        float.TryParse((string)e.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;

    internal static float[] V(XElement e, string name)
    {
        var r = new float[3];
        var parts = ((string)e.Attribute(name) ?? "").Split(',');
        for (int i = 0; i < 3 && i < parts.Length; i++)
            float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out r[i]);
        return r;
    }
}

/// <summary>
/// A passenger seat of a riding row: RideeData::Rider (FUN_140782110, ridingtable.cpp:171) read from a &lt;passenger&gt;:
/// passengerid, rideBone, rideTranslation, rideRotation and the rider sequences, each falling back to rideAniPC.
/// </summary>
public sealed class RideSeat
{
    public int Id;
    public string RideBone;
    public float[] Translation = new float[3], Rotation = new float[3];
    public Ms2.Net.Replay.RiderSequences Sequences;

    public static RideSeat Read(XElement p)
    {
        var pc = (string)p.Attribute("rideAniPC");
        if (string.IsNullOrEmpty(pc)) pc = "Ride_Idle_A";
        string A(string name) => RideData.Or((string)p.Attribute(name), pc);
        return new RideSeat
        {
            Id = int.TryParse((string)p.Attribute("passengerid"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) ? id : -1,
            RideBone = (string)p.Attribute("rideBone") ?? "",
            Translation = RideData.V(p, "rideTranslation"),
            Rotation = RideData.V(p, "rideRotation"),
            Sequences = new(pc, A("rideAniPC_Idle"), A("rideAniPC_Run"), A("rideAniPC_Jump"), A("rideAniPC_SP_Idle"),
                A("rideAniPC_SP_Run"), A("rideAniPC_SP_Jump")),
        };
    }
}
