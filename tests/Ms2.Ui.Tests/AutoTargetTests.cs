using System.Numerics;
using System.Xml.Linq;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The auto target: PCBase::vfunc_101 @0x141672de0 (pcbase.cpp:2609-2690) with its picks FUN_141679800 @0x141679800 and
/// FUN_141679ad0 @0x141679ad0 (:3747, :3808) and NormalizeDegrees (commonutil.cpp:1518), over hand-made skill rows.
/// Client space: z up, rotation z faces (sin z, -cos z).
/// </summary>
public class AutoTargetTests
{
    static CastRecord Record(string stateAttr, string autoTargeting, string range, float rotZ, string attack = "")
    {
        var doc = XDocument.Parse("<ms2><skill id=\"100\"><basic mainType=\"1\"><stateAttr " + stateAttr + "/><autoTargeting "
            + autoTargeting + "/></basic><level value=\"1\"><motion sequenceName=\"A\"><attack point=\"Hit_01\" " + attack + ">"
            + range + "</attack></motion></level></skill></ms2>");
        var t = new SkillDataTable(() => new[] { doc }, (e, n, _) => e.Elements(n).ToList());
        var d = t.GetSkillData(100);
        return new CastRecord { Skill = d, Level = d.GetLevelData(1), Rotation = new Vector3(0, 0, rotZ) };
    }

    const string Cylinder = "<range rangeType=\"cylinder\" distance=\"200\" height=\"100\" applyTarget=\"1\"/>";

    static HitCandidate Mob(int id, float x, float y, float z = 0) => new()
    {
        Id = id, Position = new Vector3(x, y, z), Body = SkillHit.PlayerBody(new Vector3(x, y, z), 0),
        Kind = HitKind.Npc, Team = 0, Hp = 100,
    };

    static readonly HitCandidate Owner = new()
    {
        Id = 42, Position = Vector3.Zero, Body = SkillHit.PlayerBody(Vector3.Zero, 0), IsOwner = true, Kind = HitKind.Pc, Team = 1, Hp = 1,
    };

    readonly List<HitCandidate> queried = new();
    readonly List<int> hits = new();

    (bool, float, float) Run(CastRecord r, float ownerRotZ, params HitCandidate[] mobs)
    {
        var all = mobs.Append(Owner).ToArray();
        bool picked = SkillCast.AutoTarget(r, Vector3.Zero, ownerRotZ, all, queried, hits, false, out float yaw);
        return (picked, yaw, r.Rotation.Z);
    }

    // A ahead and far (dot .995, 301.5 away, yaw 95.7), B off to the side and near (dot .83, 180 away, yaw 56.3); the owner faces 90
    static readonly HitCandidate A = Mob(1, 300, 30), B = Mob(2, 150, -100);

    [TestCase(0u, 95f)]
    [TestCase(3u, 56f)]
    [TestCase(1u, 56f)]
    [TestCase(2u, 95f)]
    public void TheTypePicksByFacingOrDistance(uint type, float yaw)
    {
        var r = Record($"autoTargeting=\"1\" autoTargetingType=\"{type}\"",
            "autoTargetingMaxDegree=\"180\" autoTargetingMaxDistance=\"400\"", Cylinder, 90);
        Assert.That(Run(r, 90, A, B), Is.EqualTo((true, yaw, yaw)));
    }

    [Test]
    public void WithoutAutoTargetingOrAnUnknownTypeNothingTurns()
    {
        var off = Record("autoTargeting=\"0\"", "autoTargetingMaxDegree=\"180\" autoTargetingMaxDistance=\"400\"", Cylinder, 90);
        Assert.That(Run(off, 90, A, B), Is.EqualTo((false, 0f, 90f)));
        var odd = Record("autoTargeting=\"1\" autoTargetingType=\"4\"", "autoTargetingMaxDegree=\"180\" autoTargetingMaxDistance=\"400\"", Cylinder, 90);
        Assert.That(Run(odd, 90, A, B), Is.EqualTo((false, 0f, 90f)));
    }

    [Test]
    public void TheConeIsHalfTheMaxDegreeEachSideOfTheRecordsRotation()
    {
        var r = Record("autoTargeting=\"1\" autoTargetingType=\"3\"", "autoTargetingMaxDegree=\"60\" autoTargetingMaxDistance=\"400\"", Cylinder, 90);
        Assert.That(Run(r, 90, A, B), Is.EqualTo((true, 95f, 95f)), "B (33.7 degrees off) is outside 30");
        var behind = Record("autoTargeting=\"1\" autoTargetingType=\"3\"", "autoTargetingMaxDegree=\"60\" autoTargetingMaxDistance=\"400\"", Cylinder, 270);
        Assert.That(Run(behind, 90, A, B), Is.EqualTo((false, 0f, 270f)), "the cone turns with the record, not the owner");
    }

    [Test]
    public void WithoutAMaxDistanceTheAttackRangeIsTheRadius()
    {
        var r = Record("autoTargeting=\"1\" autoTargetingType=\"0\"", "autoTargetingMaxDegree=\"180\"", Cylinder, 90);
        Assert.That(Run(r, 90, A, B), Is.EqualTo((true, 56f, 56f)), "distance 200: A is out");
        var hole = Record("autoTargeting=\"1\" autoTargetingType=\"0\"", "autoTargetingMaxDegree=\"180\"",
            "<range rangeType=\"hole_cylinder\" distance=\"100\" endWidth=\"400\" applyTarget=\"1\"/>", 90);
        Assert.That(Run(hole, 90, A, B), Is.EqualTo((true, 95f, 95f)), "a hole cylinder's radius is its endWidth");
    }

    [Test]
    public void AMaxHeightLowersTheCylinderByHalfOfIt()
    {
        var high = Mob(3, 0, 150, 120);   // body 120 .. 270, straight behind a rotation of 180
        var r = Record("autoTargeting=\"1\" autoTargetingType=\"0\"", "autoTargetingMaxDegree=\"180\" autoTargetingMaxDistance=\"400\"", Cylinder, 180);
        Assert.That(Run(r, 180, high), Is.EqualTo((true, 180f, 180f)), "150 high from the feet");
        var low = Record("autoTargeting=\"1\" autoTargetingType=\"0\"",
            "autoTargetingMaxDegree=\"180\" autoTargetingMaxDistance=\"400\" autoTargetingMaxHeight=\"100\"", Cylinder, 180);
        Assert.That(Run(low, 180, high), Is.EqualTo((false, 0f, 180f)), "-50 .. 50");
    }

    [Test]
    public void TheRangeOffsetMovesTheCylinderWithTheOwnersFacing()
    {
        const string Offset = "<range rangeType=\"cylinder\" distance=\"100\" rangeOffset=\"0,300,0\" applyTarget=\"1\"/>";
        var far = Mob(4, 300, 0);
        var r = Record("autoTargeting=\"1\"", "autoTargetingMaxDegree=\"180\"", Offset, 90);
        Assert.That(Run(r, 90, far), Is.EqualTo((true, 90f, 90f)), "300 ahead of the owner facing 90");
        var away = Record("autoTargeting=\"1\"", "autoTargetingMaxDegree=\"180\"", Offset, 90);
        Assert.That(Run(away, 0, far).Item1, Is.False, "the owner facing 0 puts the centre at (0, -300)");
        var method1 = Record("autoTargeting=\"1\"", "autoTargetingMaxDegree=\"180\"", Offset, 90, "attackMethodType=\"1\"");
        Assert.That(Run(method1, 90, far).Item1, Is.False, "attackMethodType 1 takes no offset");
    }

    [Test]
    public void ADeadPcIsPickedOnlyWithoutALivingTarget()
    {
        var dead = new HitCandidate
        {
            Id = 5, Position = new Vector3(300, 0, 0), Body = SkillHit.PlayerBody(new Vector3(300, 0, 0), 0),
            Kind = HitKind.Pc, Team = 0, Hp = 50, Dead = true,
        };
        var r = Record("autoTargeting=\"1\" autoTargetingType=\"0\"", "autoTargetingMaxDegree=\"180\" autoTargetingMaxDistance=\"400\"", Cylinder, 90);
        Assert.That(Run(r, 90, dead, B), Is.EqualTo((true, 56f, 56f)), "the dead PC is straight ahead, B still wins");
        r.Rotation = new Vector3(0, 0, 90);
        Assert.That(Run(r, 90, dead), Is.EqualTo((true, 90f, 90f)));
    }

    [Test]
    public void TheYawIsCutToAWholeDegreeInZeroTo360()
    {
        // a target at yaw -45.7: atan2 gives -45.7, NormalizeDegrees casts to -45 and adds 360
        float a = -45.7f * MathF.PI / 180f;
        var m = Mob(6, 200 * MathF.Sin(a), -200 * MathF.Cos(a));
        var r = Record("autoTargeting=\"1\"", "autoTargetingMaxDegree=\"180\" autoTargetingMaxDistance=\"400\"", Cylinder, 315);
        Assert.That(Run(r, 315, m), Is.EqualTo((true, 315f, 315f)));
        Assert.That(r.TargetDistance, Is.EqualTo(0f), "no autoTargetUseMove");
    }

    [Test]
    public void UseMoveKeepsTheDistanceLess100Above100()
    {
        var r = Record("autoTargeting=\"1\"", "autoTargetingMaxDegree=\"180\" autoTargetingMaxDistance=\"400\" autoTargetUseMove=\"1\"", Cylinder, 90);
        Run(r, 90, Mob(7, 250, 0));
        Assert.That(r.TargetDistance, Is.EqualTo(150f).Within(1e-3f));
        Run(r, 90, Mob(8, 80, 0));
        Assert.That(r.TargetDistance, Is.EqualTo(80f).Within(1e-3f));
    }

    [Test]
    public void ARunAllocatesNothing()
    {
        var r = Record("autoTargeting=\"1\" autoTargetingType=\"1\"", "autoTargetingMaxDegree=\"180\" autoTargetingMaxDistance=\"400\"", Cylinder, 90);
        var all = new[] { A, B, Owner };
        SkillCast.AutoTarget(r, Vector3.Zero, 90, all, queried, hits, false, out _);
        long bytes = Alloc.Least(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                r.Rotation = new Vector3(0, 0, 90);
                SkillCast.AutoTarget(r, Vector3.Zero, 90, all, queried, hits, false, out _);
            }
        });
        Assert.That(bytes, Is.EqualTo(0));
    }
}
