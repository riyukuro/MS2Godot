using System.Xml.Linq;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The own player's key pick with bAuto, MyPC::FUN_141526730 @0x141526730 (mypc.cpp:1767-1859), and the key-down skill's hold
/// limit, MyPC::vfunc_104 (:1711-1726) and vfunc_53 (:2998-3004): holding the attack key gives its skill each poll.
/// </summary>
public class HeldKeyPollTests
{
    const int KeyA = 0x1e, KeyS = 0x1f, KeyD = 0x20;

    static SkillData Skill(uint id, bool keyIsDown, string kinds = "") => SkillData.Load(
        XElement.Parse($"<skill id=\"{id}\"><basic><kinds {kinds}/><stateAttr keyIsDown=\"{(keyIsDown ? 1 : 0)}\"/></basic></skill>"),
        (_, _, _) => new List<XElement>());

    static readonly SkillData Attack = Skill(1, true), Plain = Skill(2, false), Instant = Skill(3, false, "immediateActive=\"1\""),
        InstantHeld = Skill(4, true, "immediateActive=\"1\"");

    readonly HashSet<int> down = new(), pressed = new();
    readonly Dictionary<int, SkillData> skills = new();
    readonly List<int> bound = new() { KeyA, KeyS, KeyD };
    Func<int, bool> isDown, wasPressed;
    Func<int, SkillData> skillOf;

    [SetUp]
    public void SetUp()
    {
        down.Clear(); pressed.Clear(); skills.Clear();
        isDown = down.Contains;
        wasPressed = pressed.Contains;
        skillOf = k => skills.TryGetValue(k, out var s) ? s : null;
    }

    int Pick(HeldKeyPoll p, uint tick, bool auto = true) => p.Pick(bound, down.Count != 0, isDown, wasPressed, skillOf, auto, tick);

    [Test]
    public void AHeldKeyGivesItsSkillEachPoll()
    {
        // :1832-1858: with bAuto a key-down skill's key counts while down, not only in the frame it is pressed
        var p = new HeldKeyPoll();
        skills[KeyA] = Attack;
        down.Add(KeyA);
        pressed.Add(KeyA);
        Assert.That(Pick(p, 100), Is.EqualTo(KeyA));
        pressed.Clear();
        Assert.That(Pick(p, 116), Is.EqualTo(KeyA), "held");
        Assert.That(Pick(p, 132, auto: false), Is.EqualTo(0), "without bAuto only a press counts");
        down.Clear();
        Assert.That(Pick(p, 148), Is.EqualTo(0));
    }

    [Test]
    public void APressWinsThenTheLastKeyDown()
    {
        var p = new HeldKeyPoll();
        skills[KeyA] = Attack; skills[KeyS] = Plain; skills[KeyD] = Plain;
        down.Add(KeyS);
        Assert.That(Pick(p, 100), Is.EqualTo(KeyS));
        down.Add(KeyD);
        Assert.That(Pick(p, 200), Is.EqualTo(KeyD), "the key that went down last (the largest first-down tick)");   // m_map17c0
        Assert.That(Pick(p, 300), Is.EqualTo(KeyD), "the map keeps each key's first tick");
        down.Add(KeyA); pressed.Add(KeyA);
        Assert.That(Pick(p, 400), Is.EqualTo(KeyA), "a key just pressed first (:1794-1808)");
        pressed.Clear(); down.Remove(KeyA); down.Remove(KeyD);
        Assert.That(Pick(p, 500), Is.EqualTo(KeyS));
        Assert.That(p.HeldCount, Is.EqualTo(1), "keys let go leave the map");
        down.Clear();
        Pick(p, 600);
        Assert.That(p.HeldCount, Is.EqualTo(0), "no key down empties it");
    }

    [Test]
    public void AnImmediateSkillCountsOnlyWhenPressedUnlessItIsAKeyDownSkill()
    {
        var p = new HeldKeyPoll();
        skills[KeyA] = Instant; skills[KeyS] = InstantHeld;
        down.Add(KeyA);
        Assert.That(Pick(p, 100), Is.EqualTo(0));
        down.Add(KeyS);
        Assert.That(Pick(p, 200), Is.EqualTo(KeyS));
        skills.Remove(KeyS);
        Assert.That(Pick(p, 300), Is.EqualTo(0), "a key with no skill row");
    }

    [Test]
    public void AKeyDownSkillHeldPastTheLimitIsRefusedUntilLetGo()
    {
        var p = new HeldKeyPoll();
        Assert.That(p.HeldTooLong(Attack, KeyA, 1000, 30000), Is.False, "the hold starts");
        Assert.That(p.HeldTooLong(Attack, KeyA, 31000, 30000), Is.False);
        Assert.That(p.HeldTooLong(Attack, KeyA, 31001, 30000), Is.True);
        p.Release(k => false, true);
        Assert.That(p.HeldTooLong(Attack, KeyA, 31002, 30000), Is.True, "not reset inside the skill state");
        p.Release(k => true, false);
        Assert.That(p.HeldTooLong(Attack, KeyA, 31003, 30000), Is.True, "not reset while the key is down");
        p.Release(k => false, false);
        Assert.That(p.HeldTooLong(Attack, KeyA, 40000, 30000), Is.False, "let go: a new hold");
        Assert.That(p.HeldTooLong(Attack, KeyS, 80000, 30000), Is.False, "another key starts its own hold");
        Assert.That(p.HeldTooLong(Plain, KeyS, 200000, 30000), Is.False, "not a key-down skill");
        Assert.That(p.HeldTooLong(Attack, 0, 200000, 30000), Is.False, "a click");
    }

    [Test]
    public void TheHeldPollAllocatesNothing()
    {
        var p = new HeldKeyPoll();
        skills[KeyA] = Attack; skills[KeyS] = Plain;
        down.Add(KeyA); down.Add(KeyS);
        uint tick = 0;
        Pick(p, tick);
        Assert.That(Alloc.Least(() =>
        {
            for (int i = 0; i < 1000; i++)
            {
                Pick(p, tick += 16);
                p.HeldTooLong(Attack, KeyA, tick, 30000);
                p.Release(isDown, false);
            }
        }), Is.LessThan(512));
    }
}
