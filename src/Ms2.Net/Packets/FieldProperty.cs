// Inbound 0xCA FieldProperty: CSceneStateGame::OnFieldProperty (scenestategame.cpp:3895) -> WorldService (worldservice_1.cpp:924).
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>
/// One field property record: the u8 kind, then that kind's fields (CreateFieldProperty, fieldpropertycli.cpp:117;
/// each kind's Decode in fieldproperty.cpp). Kinds 1..11; any other kind makes the factory return null.
/// </summary>
public abstract record FieldProperty(byte Kind)
{
    /// <summary>1: float gravity.</summary>
    public sealed record Gravity(float Value) : FieldProperty(1);
    /// <summary>2: i64, i32.</summary>
    public sealed record MusicConcert(long A, int B) : FieldProperty(2);
    /// <summary>3: no fields.</summary>
    public sealed record HideMyPC() : FieldProperty(3);
    /// <summary>4: no fields.</summary>
    public sealed record LockMyPC() : FieldProperty(4);
    /// <summary>5: two unicode strings.</summary>
    public sealed record UserTagSymbol(string A, string B) : FieldProperty(5);
    /// <summary>6: float range, three more floats, bool, u8 (alpha in percent), bool. The client's default is
    /// 60000, 30000, 30000, 115, true, 45, false (fieldproperty.cpp:150).</summary>
    public sealed record SightRange(float Range, float B, float C, float D, bool E, byte AlphaPercent, bool G) : FieldProperty(6);
    /// <summary>7: u8 weather kind.</summary>
    public sealed record Weather(byte Value) : FieldProperty(7);
    /// <summary>8: rgb (3 u8 in r, g, b order, unit_1406c3700_fieldrgbcolor.cpp:71), bool, float (FieldPropertyAmbientLight::vfunc_2,
    /// fieldproperty.cpp:276). GMS2 sends the rgb only; the record then keeps the constructor's defaults (flag false, value 0,
    /// fieldproperty.cpp:256), so the colour is set at once with no fade (FieldPropertyAmbientLightCli::vfunc_4, fieldpropertycli.cpp:202).
    /// INFERRED for GMS2: the GMS2 client's Decode is not in this decomp; the recordings show 3 bytes and the Maple2 server
    /// writes type + rgb.</summary>
    public sealed record AmbientLight(byte R, byte G, byte B, bool Flag, float Value) : FieldProperty(8);
    /// <summary>9: two rgb, bool, float. GMS2 sends the two rgb only; the same defaults as <see cref="AmbientLight"/> apply
    /// (fieldproperty.cpp:296, INFERRED for GMS2).</summary>
    public sealed record DirectionalLight(byte R1, byte G1, byte B1, byte R2, byte G2, byte B2, bool Flag, float Value) : FieldProperty(9);
    /// <summary>10: bool.</summary>
    public sealed record EnableLocalCamera(bool Value) : FieldProperty(10);
    /// <summary>11: bool.</summary>
    public sealed record PhotoStudio(bool Value) : FieldProperty(11);

    /// <summary>Reads one record's fields for <paramref name="kind"/>; null for a kind the client's factory does not know
    /// (nothing more is read). Kinds 8 and 9 carry no trailing bool + float on GMS2 (<paramref name="gms2"/>).</summary>
    public static FieldProperty ReadBody(ByteReader r, byte kind, bool gms2 = false) => kind switch
    {
        1 => new Gravity(r.ReadFloat()),
        2 => new MusicConcert(r.ReadLong(), r.ReadInt()),
        3 => new HideMyPC(),
        4 => new LockMyPC(),
        5 => new UserTagSymbol(r.ReadUnicodeString(), r.ReadUnicodeString()),
        6 => new SightRange(r.ReadFloat(), r.ReadFloat(), r.ReadFloat(), r.ReadFloat(), r.ReadBool(), r.ReadByte(), r.ReadBool()),
        7 => new Weather(r.ReadByte()),
        8 when gms2 => new AmbientLight(r.ReadByte(), r.ReadByte(), r.ReadByte(), false, 0f),
        8 => new AmbientLight(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadBool(), r.ReadFloat()),
        9 when gms2 => new DirectionalLight(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte(), false, 0f),
        9 => new DirectionalLight(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadBool(), r.ReadFloat()),
        10 => new EnableLocalCamera(r.ReadBool()),
        11 => new PhotoStudio(r.ReadBool()),
        _ => null,
    };
}

/// <summary>
/// Inbound 0xCA: u8 mode (WorldService dispatch, worldservice_1.cpp:924).
/// <list type="bullet">
/// <item>0 (<see cref="List"/>): u32 count, then count records (u8 kind + fields). An unknown kind stops the loop and the
/// rest is left unread (ReadFieldProperties, worldservice_1.cpp:5322). The list does not clear the field's properties
/// first; each record replaces the same kind's entry.</item>
/// <item>1 (<see cref="Add"/>): one record; an unknown kind reads nothing more and does nothing (null).</item>
/// <item>2 (<see cref="Remove"/>): u8 kind.</item>
/// </list>
/// Other modes read nothing more and do nothing (null).
/// </summary>
public abstract record FieldPropertyMessage
{
    public sealed record List(FieldProperty[] Properties) : FieldPropertyMessage;
    public sealed record Add(FieldProperty Property) : FieldPropertyMessage;
    public sealed record Remove(byte Kind) : FieldPropertyMessage;

    public static FieldPropertyMessage Read(ByteReader r) => Read(r, ProtocolProfile.Kms2525);

    public static FieldPropertyMessage Read(ByteReader r, ProtocolProfile profile)
    {
        bool gms2 = profile.IsGms2;
        int mode = r.ReadByte();
        switch (mode)
        {
            case 0:
            {
                uint count = r.ReadUInt();
                var props = new System.Collections.Generic.List<FieldProperty>();
                for (uint i = 0; i < count; i++)
                {
                    var p = FieldProperty.ReadBody(r, r.ReadByte(), gms2);
                    if (p == null) return new List(props.ToArray());   // the client breaks out; the rest stays unread
                    props.Add(p);
                }
                r.ExpectEnd("FieldProperty list");
                return new List(props.ToArray());
            }
            case 1:
            {
                var p = FieldProperty.ReadBody(r, r.ReadByte(), gms2);
                if (p == null) return null;
                r.ExpectEnd("FieldProperty add");
                return new Add(p);
            }
            case 2:
            {
                byte kind = r.ReadByte();
                r.ExpectEnd("FieldProperty remove");
                return new Remove(kind);
            }
            default: return null;
        }
    }
}
