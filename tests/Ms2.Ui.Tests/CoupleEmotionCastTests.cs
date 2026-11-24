using System.Numerics;
using Ms2.Net.Packets;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>StateSkill::FUN_1417eaa50's couple branch (stateskill.cpp:1914-1990) and its helpers in statecoupleemotion.cpp.</summary>
public class CoupleEmotionCastTests
{
    static readonly SkillData Couple = new() { Id = 90220001, MainType = EmotionCast.MainTypeCouple };
    static readonly CouplePeer Peer = new(77, 4002, "Partner", new Vector3(100, 0, 0), new Vector3(0, 0, 135));

    static CoupleEmotionCast.Input Ready() => new()
    {
        Selected = 4002,
        FindPeer = id => id == 4002 ? Peer : null,
        StateId = 1,
        ObjectId = 55,
        Position = Vector3.Zero,
        Placeable = _ => true,
    };

    [Test]
    public void AReadyCastEntersTheRequestSubAndSendsModeZero()
    {
        var o = CoupleEmotionCast.Use(Couple, Ready());
        Assert.That(o.Handled, Is.True);
        Assert.That(o.Notice, Is.EqualTo(0));
        Assert.That(o.Start, Is.EqualTo(new CoupleEmotionStart(90220001, 77, 55, new Vector3(100, 0, 0), new Vector3(0, 0, 135))));
        Assert.That(o.Send, Is.EqualTo(new BuddyEmoteInviteRequest(90220001, 4002)), "the partner's character id, not the object id");
    }

    [Test]
    public void OnlyMainTypeFifteenIsTheCoupleBranch()
    {
        Assert.That(CoupleEmotionCast.Use(new SkillData { Id = 1, MainType = EmotionCast.MainTypeEmotion }, Ready()), Is.EqualTo(default(CoupleEmotionCast.Outcome)));
        Assert.That(CoupleEmotionCast.Use(null, Ready()), Is.EqualTo(default(CoupleEmotionCast.Outcome)));
    }

    [Test]
    public void TheBreaksBeforeTheChecksAreSilent()
    {
        // :1880-1885 jumping returns before the switch; :1922 / :1931 the asserts break without a notice or *pResult
        var i = Ready(); i.Jumping = true;
        Assert.That(CoupleEmotionCast.Use(Couple, i), Is.EqualTo(default(CoupleEmotionCast.Outcome)));
        i = Ready(); i.HasDialog = false;
        Assert.That(CoupleEmotionCast.Use(Couple, i), Is.EqualTo(default(CoupleEmotionCast.Outcome)));
        i = Ready(); i.Selected = 4003;
        Assert.That(CoupleEmotionCast.Use(Couple, i), Is.EqualTo(default(CoupleEmotionCast.Outcome)));
    }

    [Test]
    public void EachRefusalHasItsNoticeAndSendsNothing()
    {
        void Refused(Action<CoupleEmotionCast.Input> set, int notice, string name)
        {
            var i = Ready();
            set(i);
            var o = CoupleEmotionCast.Use(Couple, i);
            Assert.That((o.Handled, o.Notice, o.NoticeName, o.Start, o.Send), Is.EqualTo((false, notice, name, (CoupleEmotionStart)null, (BuddyEmoteInviteRequest)null)));
        }
        Refused(i => i.Blocked = id => id == 4002, 0xcef, "Partner");
        Refused(i => i.ShownBoxExtra = "coupleEmotion", 0xced, null);
        Refused(i => i.InCubeMode = true, 0xcec, null);
        Refused(i => i.OnWater = true, 0xcec, null);
        Refused(i => i.StateId = 3, 0xcec, null);
        Refused(i => i.Placeable = _ => false, 0xcec, null);
        Refused(i => i.Position = new Vector3(-201, 0, 0), 0xcee, "Partner");
    }

    [Test]
    public void TheChecksRunInTheClientsOrder()
    {
        // the block list before the box, the box before the place, the place before the distance
        var i = Ready();
        i.Blocked = _ => true; i.ShownBoxExtra = "coupleEmotion"; i.StateId = 3; i.Position = new Vector3(1000, 0, 0);
        Assert.That(CoupleEmotionCast.Use(Couple, i).Notice, Is.EqualTo(0xcef));
        i.Blocked = _ => false;
        Assert.That(CoupleEmotionCast.Use(Couple, i).Notice, Is.EqualTo(0xced));
        i.ShownBoxExtra = null;
        Assert.That(CoupleEmotionCast.Use(Couple, i).Notice, Is.EqualTo(0xcec));
        bool probed = false;
        i.StateId = 2; i.Placeable = _ => probed = true;
        Assert.That(CoupleEmotionCast.Use(Couple, i).Notice, Is.EqualTo(0xcee));
        Assert.That(probed, Is.True);
        // a refused state never probes the ground
        probed = false; i.StateId = 0xb;
        CoupleEmotionCast.Use(Couple, i);
        Assert.That(probed, Is.False);
    }

    [Test]
    public void TheHelpersMatchTheClient()
    {
        // FUN_140b83ec0: any other box (or none) lets the cast through
        Assert.That(CoupleEmotionCast.NoCoupleBox(null), Is.True);
        Assert.That(CoupleEmotionCast.NoCoupleBox(""), Is.True);
        Assert.That(CoupleEmotionCast.NoCoupleBox("CoupleEmotion"), Is.True, "compared with case");
        Assert.That(CoupleEmotionCast.NoCoupleBox("coupleEmotion"), Is.False);
        // FUN_140b83f60
        var ok = Enumerable.Range(0, 0x40).Where(CoupleEmotionCast.MayStart);
        Assert.That(ok, Is.EqualTo(new[] { 1, 2, 4, 6, 7 }));
        // FUN_140b83fa0: fLen > fMax fails, equal passes
        Assert.That(CoupleEmotionCast.WithinDistance(Vector3.Zero, new Vector3(300, 0, 0), 300), Is.True);
        Assert.That(CoupleEmotionCast.WithinDistance(Vector3.Zero, new Vector3(300.01f, 0, 0), 300), Is.False);
        Assert.That(CoupleEmotionCast.WithinDistance(Vector3.Zero, new Vector3(0, 0, 301), 300), Is.False, "z counts");
    }

    sealed class Probe : ICouplePlaceProbe
    {
        public readonly List<string> Calls = new();
        public bool Ground = true, Overlap, Entity = true, Blocked;
        public bool GroundWithin(Vector3 p, float len) { Calls.Add($"ground {p.Z} {len}"); return Ground; }
        public bool CapsuleOverlaps(Vector3 p) { Calls.Add($"capsule {p.Z}"); return Overlap; }
        public bool EntityBelow(Vector3 p, out bool blocked) { Calls.Add($"ray {p.Z}"); blocked = Blocked; return Entity; }
    }

    [Test]
    public void PlaceableRaisesByFiveThenGroundCapsuleAndRay()
    {
        var p = new Probe();
        Assert.That(CoupleEmotionCast.Placeable(p, new Vector3(0, 0, 100)), Is.True);
        Assert.That(p.Calls, Is.EqualTo(new[] { "ground 105 10", "capsule 105", "ray 105" }));
        Assert.That(CoupleEmotionCast.Placeable(new Probe { Ground = false }, Vector3.Zero), Is.False);
        Assert.That(CoupleEmotionCast.Placeable(new Probe { Overlap = true }, Vector3.Zero), Is.False);
        Assert.That(CoupleEmotionCast.Placeable(new Probe { Entity = false }, Vector3.Zero), Is.False);
        Assert.That(CoupleEmotionCast.Placeable(new Probe { Blocked = true }, Vector3.Zero), Is.False);
        p = new Probe { Ground = false };
        CoupleEmotionCast.Placeable(p, Vector3.Zero);
        Assert.That(p.Calls, Has.Count.EqualTo(1), "no ground: nothing more is probed");
    }
}
