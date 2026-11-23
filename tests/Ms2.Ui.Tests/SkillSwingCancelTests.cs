using System.Numerics;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The own skill state's swing cancel (StateSkill::FUN_1417ed7f0 @0x1417ed7f0, stateskill.cpp:1505-1563): a move held or a
/// jump pressed in the motion's input window leaves the attack for the walk or the jump from its "combo" / "selfcombo" key
/// or its end, with the attack key let go. Before, the own attack always played to its end.
/// </summary>
public class SkillSwingCancelTests
{
    static readonly Vector2 Forward = new(0, 1);

    static SwingInput In(Vector2 held = default, bool jump = false, bool keyHeld = false) => new()
    {
        CanAct = true, Held = held, Pressed = held, KeyIsDown = true, JumpPressed = jump, ExitKeyHeld = keyHeld,
    };

    static SkillFollow.Window Win(bool input = false, bool selfInput = false, bool combo = false, bool selfCombo = false, bool ended = false) =>
        new() { InputCombo = input, SelfInputCombo = selfInput, Combo = combo, SelfCombo = selfCombo, Ended = ended };

    [Test]
    public void AMoveHeldInTheWindowLeavesForTheWalkAtTheSelfComboKey()
    {
        var c = default(SkillSwingCancel);
        Assert.That(c.Step(Win(selfInput: true), In(Forward)), Is.EqualTo(SwingExit.None), "in the window, before selfcombo");
        Assert.That(c.Step(Win(selfInput: true, selfCombo: true), In(Forward)), Is.EqualTo(SwingExit.Walk));
        Assert.That(c.Push, Is.EqualTo(Forward));
    }

    [Test]
    public void AMoveBeforeTheWindowDoesNotCutTheAttack()
    {
        var c = default(SkillSwingCancel);
        Assert.That(c.Step(Win(), In(Forward)), Is.EqualTo(SwingExit.None), "no window yet: not kept");
        Assert.That(c.Step(Win(selfInput: true), In()), Is.EqualTo(SwingExit.None), "let go before the window");
        Assert.That(c.Step(Win(selfInput: true, selfCombo: true), In()), Is.EqualTo(SwingExit.None));
        Assert.That(c.Step(Win(selfInput: true, selfCombo: true, ended: true), In()), Is.EqualTo(SwingExit.None), "the end goes to idle");
    }

    [Test]
    public void AMoveLetGoInTheWindowStillLeavesForTheWalk()
    {
        // m_ptPush keeps the last direction: a zero one does not clear it (:1528-1529)
        var c = default(SkillSwingCancel);
        c.Step(Win(input: true), In(Forward));
        Assert.That(c.Step(Win(input: true, combo: true), In()), Is.EqualTo(SwingExit.Walk));
    }

    [Test]
    public void TheInputComboWindowEndsAtItsEndKey()
    {
        // endInputCombo clears m_b4a: a move after it is not kept
        var c = default(SkillSwingCancel);
        c.Step(Win(input: true), In());
        Assert.That(c.Step(Win(combo: true), In(Forward)), Is.EqualTo(SwingExit.None));
    }

    [Test]
    public void AJumpPressedInTheWindowLeavesForTheJumpBeforeTheWalk()
    {
        var c = default(SkillSwingCancel);
        c.Step(Win(selfInput: true), In(Forward, jump: true));
        Assert.That(c.Step(Win(selfInput: true, selfCombo: true), In(Forward)), Is.EqualTo(SwingExit.Jump));
        Assert.That(c.Push, Is.EqualTo(Forward), "the jump goes the push's way");
    }

    [Test]
    public void AJumpPressedOutsideTheWindowDoesNotCutTheAttack()
    {
        var c = default(SkillSwingCancel);
        c.Step(Win(), In(jump: true));
        Assert.That(c.Step(Win(selfCombo: true, ended: true), In()), Is.EqualTo(SwingExit.None));
    }

    [Test]
    public void TheAttackKeyHeldKeepsTheAttack()
    {
        // the next cast's key (or the running one's) down: no exit (:1543-1547), the repeat attack chains instead
        var c = default(SkillSwingCancel);
        c.Step(Win(selfInput: true), In(Forward, jump: true));
        Assert.That(c.Step(Win(selfInput: true, selfCombo: true), In(Forward, keyHeld: true)), Is.EqualTo(SwingExit.None));
        Assert.That(c.Step(Win(selfInput: true, selfCombo: true), In(Forward)), Is.EqualTo(SwingExit.Jump), "let go");
    }

    [Test]
    public void NoExitWhileFallingOrHeldOrWithMagicControl()
    {
        var w = Win(selfInput: true, selfCombo: true);
        var c = default(SkillSwingCancel);
        var i = In(Forward); i.Falling = true;
        Assert.That(c.Step(w, i), Is.EqualTo(SwingExit.None), "vfunc_99(0)");
        Assert.That(c.Push, Is.EqualTo(Vector2.Zero), "nothing kept while falling");
        i = In(Forward); i.CanAct = false;
        Assert.That(c.Step(w, i), Is.EqualTo(SwingExit.None), "vfunc_63");
        i = In(Forward); i.MagicControl = true;
        Assert.That(c.Step(w, i), Is.EqualTo(SwingExit.None), "m_n10 0x3c");
        Assert.That(c.Step(w, In(Forward)), Is.EqualTo(SwingExit.Walk));
    }

    [Test]
    public void AContinueSkillWithItsKeyHeldOrAQueuedChargeHasNoPush()
    {
        var w = Win(selfInput: true, selfCombo: true);
        var c = default(SkillSwingCancel);
        var i = In(Forward); i.ContinueKeyHeld = true;
        Assert.That(c.Step(w, i), Is.EqualTo(SwingExit.None));
        c = default;
        i = In(Forward); i.Charging = true;
        Assert.That(c.Step(w, i), Is.EqualTo(SwingExit.None));
    }

    [Test]
    public void WithoutKeyIsDownOnlyAPressCounts()
    {
        var c = default(SkillSwingCancel);
        var i = In(Forward); i.KeyIsDown = false; i.Pressed = Vector2.Zero;
        c.Step(Win(selfInput: true), i);
        Assert.That(c.Step(Win(selfInput: true, selfCombo: true), In()), Is.EqualTo(SwingExit.None), "a held key is not a press");
    }

    [Test]
    public void AMotionStartResetsWhatWasKept()
    {
        var c = default(SkillSwingCancel);
        c.Step(Win(selfInput: true), In(Forward, jump: true));
        c.Reset();
        Assert.That(c.Step(Win(selfInput: true, selfCombo: true), In()), Is.EqualTo(SwingExit.None));
    }

    /// <summary>On the data: the Wizard's Magic Claw (10300001) through its motion's real text keys ("inputcombo" at 0.3,
    /// "combo" and its selfInputCombo key at 0.5, "selfcombo" at 0.7 in both data sets). A move or a jump before 0.3 is not
    /// kept; one in the window cuts the attack at the "combo" key, and so does another skill pressed in it. The Teleport (10300081, immediateActive 1) chains at any
    /// time, before the window too (FUN_1417eed70 / FUN_1417ef4a0).</summary>
    [TestCase("MS2_GMS2_DATA")]
    [TestCase("MS2_DATA")]
    public void TheMagicClawIsCutAfterItsInputKeyOnly(string env)
    {
        var d = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore(env + " is not set");
        using var packs = new PackSet(d);
        var t = GameTables.Shared(packs, "en").Skills.Skills;
        var s = t.GetSkillData(10300001);
        var tele = t.GetSkillData(10300081);
        Assert.That(s, Is.Not.Null); Assert.That(tele, Is.Not.Null);
        var lv = s.GetLevelData(1);
        var m = lv.Motions[0];
        var x = Ms2.Formats.Text.PackXml.Read(packs, "Resource/Model/Character", "female/female.xml");
        var seq = x.Root!.Elements().First(e => string.Equals(e.Name.LocalName, m.SequenceName, StringComparison.OrdinalIgnoreCase));
        var keys = new SkillFollow.ComboKeys();
        keys.Set(seq.Elements("key").Select(k => ((double)float.Parse((string)k.Attribute("time")!, System.Globalization.CultureInfo.InvariantCulture),
            (string)k.Attribute("text"))).ToList(), m.SelfInputCombo);
        double Key(string text) => seq.Elements("key").Where(k => (string)k.Attribute("text") == text)
            .Select(k => (double)float.Parse((string)k.Attribute("time")!, System.Globalization.CultureInfo.InvariantCulture)).DefaultIfEmpty(-1).First();
        double open = Key("inputcombo"), combo = Key("combo");
        Assert.That(open, Is.GreaterThan(0));
        Assert.That(combo, Is.GreaterThan(open), "the window opens before the combo key");
        var r = new CastRecord { Skill = s, Level = lv, Motion = 0 };

        // before the window: a move and a jump are not kept, the attack is not cut
        var w = default(SkillFollow.Window);
        var c = default(SkillSwingCancel);
        keys.Advance(ref w, -1, open * 0.5, true);
        Assert.That(c.Step(w, In(Forward, jump: true)), Is.EqualTo(SwingExit.None));
        keys.Advance(ref w, open * 0.5, combo, true);
        Assert.That(c.Step(w, In()), Is.EqualTo(SwingExit.None), "the early move and jump are not kept");

        // the Teleport chains before the window (immediateActive 1)
        int chain = 0;
        w = default;
        keys.Advance(ref w, -1, open * 0.5, true);
        Assert.That(SkillFollow.MayFollow(r, tele, w, false, ref chain), Is.True);
        Assert.That(SkillFollow.ChainsNow(r, tele, chain, w, false), Is.True, "the dodge cuts the attack at once");

        // skill into skill: another Wizard skill without immediateActive is refused before the window, kept in it and
        // started at the combo key (the chain comes before the swing cancel in the frame)
        SkillData other = null;
        for (uint id = 10300002; id < 10300300 && other == null; id++)
            if (t.GetSkillData(id) is { ImmediateActive: 0 } o && o.Id != s.Id) other = o;
        Assert.That(other, Is.Not.Null);
        TestContext.Out.WriteLine($"{env}: skill into skill {other.Id}");
        w = default; chain = 0;
        keys.Advance(ref w, -1, open * 0.5, true);
        Assert.That(SkillFollow.MayFollow(r, other, w, false, ref chain), Is.False, "a press before the window is dropped");
        w = default; chain = 0;
        keys.Advance(ref w, -1, open, true);
        Assert.That(SkillFollow.MayFollow(r, other, w, false, ref chain), Is.True, "a press in the window is kept");
        Assert.That(SkillFollow.ChainsNow(r, other, chain, w, false), Is.False, "the combo key not reached");
        keys.Advance(ref w, open, combo, true);
        Assert.That(SkillFollow.ChainsNow(r, other, chain, w, false), Is.True);

        // in the window: the move cuts the attack at the combo key, not before
        w = default; c = default;
        keys.Advance(ref w, -1, open, true);
        Assert.That(c.Step(w, In(Forward)), Is.EqualTo(SwingExit.None), "the window is open, the combo key not reached");
        keys.Advance(ref w, open, combo, true);
        Assert.That(c.Step(w, In(Forward)), Is.EqualTo(SwingExit.Walk));

        // in the window: a jump
        w = default; c = default;
        keys.Advance(ref w, -1, open, true);
        c.Step(w, In(jump: true));
        keys.Advance(ref w, open, combo, true);
        Assert.That(c.Step(w, In()), Is.EqualTo(SwingExit.Jump));
    }
}
