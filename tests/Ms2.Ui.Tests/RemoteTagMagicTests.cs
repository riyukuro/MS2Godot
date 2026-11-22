using System.Numerics;
using System.Xml.Linq;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// Another player's arrow magics from SkillDamage mode 0 (CSceneStateGame::FUN_14171c1d0, scenestategame.cpp:5511) and the
/// start ticks its cast records give them (FUN_1417ecce0, SetTargetStartTicks, StateSkill's exit). Client space: z up.
/// </summary>
public class RemoteTagMagicTests
{
    const int OwnerId = 5, T = 1000;
    const long Uid = 0x1122334455L;

    static (SkillData Skill, SkillAttackData Attack) Bow()
    {
        var doc = XDocument.Parse("<ms2><skill id=\"100\"><basic mainType=\"1\"/><level value=\"1\"><motion sequenceName=\"A\">"
            + "<attack point=\"Hit_01\" targetCount=\"1\" attackMethodType=\"1\" magicPathID=\"600\">"
            + "<range rangeType=\"box\" distance=\"200\" height=\"100\" width=\"100\" applyTarget=\"1\"/>"
            + "<arrow nonTarget=\"1\" collision=\"25,100,15\" collisionAdd=\"0,25,15\"/></attack>"
            + "</motion></level></skill></ms2>");
        var t = new SkillDataTable(() => new[] { doc }, (e, n, _) => e.Elements(n).ToList());
        var d = t.GetSkillData(100);
        return (d, d.GetLevelData(1).Motions[0].Attacks[0]);
    }

    static MagicPathTable Paths(string extra = "") =>
        new(XDocument.Parse("<ms2><type id=\"600\"><move vel=\"1800\" distance=\"810\"" + extra + "/></type></ms2>"));

    static readonly HitCandidate[] Objects =
    {
        new() { Id = OwnerId, Position = Vector3.Zero, Body = SkillHit.PlayerBody(Vector3.Zero, 0), IsOwner = true, Kind = HitKind.Pc, Team = 1, Hp = 1 },
    };

    static readonly MagicOwner Owner = new(Vector3.Zero, 0, false, false, false);

    static SkillDamage Damage(bool server, byte motion = 0, int roots = 1)
    {
        var d = new SkillDamage
        {
            Mode = 0, OwnerId = OwnerId, CastUid = Uid, Motion = motion, AttackPoint = 0, Position = new Vector3S(0, 0, 0),
            MagicDirection = new Vector3(0, -1, 0), FromServer = server, Projectiles = new List<SkillDamageTarget>(),
        };
        for (int i = 0; i < roots; i++) d.Projectiles.Add(new SkillDamageTarget(7 + i, 0, 0, Array.Empty<SkillDamageLink>()));
        return d;
    }

    static float Y(RemoteTagMagics r, int index = 0) => r.MagicsOf(OwnerId).Flight(index).Current.Y;

    static void Tick(RemoteTagMagics r, int now, bool inSkill = true) =>
        r.Tick(OwnerId, inSkill, now, Owner, Objects, () => 0, null, null);

    [Test]
    public void ARootOfTheCastRecordWaitsUntilItsAttackFires()
    {
        // FUN_14171c1d0: not bServer, so Init's start tick is 0 and the model joins spCast's m_listModels; the tick
        // (magicdata.cpp:758) does nothing until FUN_1417ecce0 sets the attack's tick and SetTargetStartTicks starts it
        var (skill, attack) = Bow();
        var r = new RemoteTagMagics();
        Assert.That(r.BeginCast(OwnerId, Uid, 0), Is.True);
        r.SkillEntered(OwnerId);
        Assert.That(r.OnDamage(Damage(false), skill, attack, Paths(), T, null, Objects), Is.EqualTo(1));
        Assert.That(r.Pending(OwnerId), Is.EqualTo(1));
        Tick(r, T + 100);
        Tick(r, T + 200);
        Assert.That(Y(r), Is.EqualTo(0f), "still waiting");
        r.AttackFired(OwnerId, 0, T + 200);
        Assert.That(r.Pending(OwnerId), Is.EqualTo(0), "the list is emptied once every model has its tick");
        Tick(r, T + 300);
        Tick(r, T + 400);
        Assert.That(Y(r), Is.LessThan(-100f), "flies along the direction once started");
    }

    [Test]
    public void AServerPacketStartsAtOnceWithoutARecord()
    {
        var (skill, attack) = Bow();
        var r = new RemoteTagMagics();
        Assert.That(r.OnDamage(Damage(true), skill, attack, Paths(), T, null, Objects), Is.EqualTo(1), "bServer makes it without spCast");
        Tick(r, T + 100, inSkill: false);
        Tick(r, T + 200, inSkill: false);
        Assert.That(Y(r), Is.LessThan(-100f));
    }

    [Test]
    public void WithoutAMatchingRecordANonServerPacketMakesNothing()
    {
        var (skill, attack) = Bow();
        var r = new RemoteTagMagics();
        Assert.That(r.OnDamage(Damage(false), skill, attack, Paths(), T, null, Objects), Is.EqualTo(0), "no record at all");
        r.BeginCast(OwnerId, Uid, 1);
        Assert.That(r.OnDamage(Damage(false, motion: 0), skill, attack, Paths(), T, null, Objects), Is.EqualTo(0),
            "FUN_141673bf0 matches the motion too");
        Assert.That(r.BeginCast(OwnerId, Uid, 1), Is.False, "the same uid and motion keep the record");
    }

    [Test]
    public void TheSkillsExitStartsTheRootsStillWaitingAndDropsTheCancellableOnes()
    {
        // stateskill.cpp:512-535: RemoveCasterModels first (CanCancel: a waiting model is in state 0, so a delay-0 one goes
        // even with ignoreCancelAtSpawnTime), then the models still listed get the exit's tick
        var (skill, attack) = Bow();
        foreach (var (extra, kept) in new[] { (" delayTime=\"0.1\"", true), (" ignoreCancelAtSpawnTime=\"1\"", false) })
        {
            var r = new RemoteTagMagics();
            r.BeginCast(OwnerId, Uid, 0);
            r.SkillEntered(OwnerId);
            Assert.That(r.OnDamage(Damage(false), skill, attack, Paths(extra), T, null, Objects), Is.EqualTo(1));
            Assert.That(r.SkillExited(OwnerId, T + 50), Is.EqualTo(kept ? 0 : 1), extra);
            Assert.That(r.Count(OwnerId), Is.EqualTo(kept ? 1 : 0), extra);
            Assert.That(r.Pending(OwnerId), Is.EqualTo(0), extra);
            if (!kept) continue;
            for (int t = T + 100; t <= T + 400; t += 100) Tick(r, t, inSkill: false);
            Assert.That(Y(r), Is.LessThan(-100f), "started at the exit's tick");
        }
    }

    [Test]
    public void ACastStartedAgainKeepsTheStateOnItsOldRecord()
    {
        // PlayCastStartEffect replaces the PCBase list before the state is entered again: the attack fired before the entry
        // is still the old record's, so its waiting root starts
        var (skill, attack) = Bow();
        var r = new RemoteTagMagics();
        r.BeginCast(OwnerId, Uid, 0);
        r.SkillEntered(OwnerId);
        r.OnDamage(Damage(false), skill, attack, Paths(), T, null, Objects);
        r.BeginCast(OwnerId, Uid + 1, 0);
        r.AttackFired(OwnerId, 0, T + 100);
        Assert.That(r.Pending(OwnerId), Is.EqualTo(0));
        Assert.That(r.OnDamage(Damage(false), skill, attack, Paths(), T, null, Objects), Is.EqualTo(0),
            "the packet's uid is no longer the current record's");
    }
}
