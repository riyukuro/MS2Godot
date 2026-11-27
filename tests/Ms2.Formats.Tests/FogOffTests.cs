using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// The three tests of the client's fog-off walk (FUN_141567b60 @0x141567b60, unit_141567000.cpp:346-390), on one object
/// built in memory: a "nofog" UserPropBuffer (case ignored), NiAVObject flag 0x400 (FUN_14098dfc0), or the object's own
/// NiAlphaProperty blending with destination ONE (FUN_1415671f0: blending on, dest bits 5-8 = 0). The map data has only
/// the last, so <see cref="NoFogTests"/> leaves the first two unchecked.
/// </summary>
public class FogOffTests
{
    static readonly string[] Names = { "", "UserPropBuffer", "OtherData", "NoFog", "NoShadow", "x nofog y" };

    // block 0: the object; block 1: its extra data (when named); block 2: its alpha property (when given)
    static bool FogOff(long flags = 0, string extraName = null, string extraValue = null, long? alpha = null)
    {
        var nif = new NifFile();
        nif.Strings.AddRange(Names);
        var obj = new NifStruct
        {
            Type = "NiMesh", ["Flags"] = flags,
            ["Extra Data List"] = extraName != null ? new long[] { 1 } : new long[0],
            ["Properties"] = alpha != null ? new long[] { 2 } : new long[0],
        };
        nif.Blocks.Add(obj);
        nif.Blocks.Add(new NifStruct
        {
            Type = "NiStringExtraData",
            ["Name"] = (long)Array.IndexOf(Names, extraName ?? ""),
            ["String Data"] = (long)Array.IndexOf(Names, extraValue ?? ""),
        });
        nif.Blocks.Add(new NifStruct { Type = "NiAlphaProperty", ["Flags"] = alpha ?? 0L });
        nif.Roots.Add(0);
        return ModelData.FogOff(nif, 0);
    }

    [Test]
    public void APlainObjectKeepsTheFog() => Assert.That(FogOff(), Is.False);

    [Test]
    public void ANofogUserPropBufferTurnsItOff()
    {
        Assert.That(FogOff(extraName: "UserPropBuffer", extraValue: "NoFog"), Is.True, "case ignored");
        Assert.That(FogOff(extraName: "UserPropBuffer", extraValue: "x nofog y"), Is.True, "contained");
        Assert.That(FogOff(extraName: "UserPropBuffer", extraValue: "NoShadow"), Is.False, "another tag");
        Assert.That(FogOff(extraName: "OtherData", extraValue: "NoFog"), Is.False, "not the UserPropBuffer");
    }

    [Test]
    public void Flag0x400TurnsItOff()
    {
        Assert.That(FogOff(flags: 0x400), Is.True);
        Assert.That(FogOff(flags: 0x40E), Is.True, "with other flags");
        Assert.That(FogOff(flags: 0x200), Is.False);
    }

    [Test]
    public void OnlyAnAdditiveBlendTurnsItOff()
    {
        // src SRCALPHA (6 << 1); dest ONE is 0 in bits 5-8, INVSRCALPHA is 7
        Assert.That(FogOff(alpha: 1 | 6 << 1), Is.True, "blend on, dest ONE");
        Assert.That(FogOff(alpha: 1 | 6 << 1 | 7 << 5), Is.False, "blend on, dest INVSRCALPHA");
        Assert.That(FogOff(alpha: 6 << 1), Is.False, "blend off, dest ONE");
        Assert.That(FogOff(alpha: 0x200 | 6 << 1), Is.False, "alpha test only, dest ONE");
    }
}
