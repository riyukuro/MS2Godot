using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// A press while the own skill state runs and the move input under it: StateSkill::FUN_1417eeb10 @0x1417eeb10,
/// FUN_1417eed70 @0x1417eed70, FUN_1417ef4a0 @0x1417ef4a0 and TextKeyEvent's combo keys (stateskill.cpp:999-1027, 2996-3184),
/// and StateSkill::vfunc_8's move (:642).
/// </summary>
public class SkillFollowTests
{
    static readonly Func<XElement, string, string, List<XElement>> NoParts = (_, _, _) => new List<XElement>();
    static readonly Func<XElement, string, string, List<XElement>> Levels = (e, name, _) => e.Elements(name).ToList();

    /// <summary>A one-motion skill like the basic attack 1 (keyIsDown, a selfInputCombo key) unless told otherwise.</summary>
    static CastRecord Running(uint id = 1, bool keyIsDown = true, string selfInput = "selfinputcombo", string kinds = "",
        string combo = "", int motions = 1, int motion = 0)
    {
        var s = Skill(id, keyIsDown, kinds, combo, selfInput, motions);
        return new CastRecord { Skill = s, Level = s.GetLevelData(1), Motion = (byte)motion };
    }

    static SkillData Skill(uint id, bool keyIsDown = true, string kinds = "", string combo = "", string selfInput = "", int motions = 1,
        uint origin = 0)
    {
        var m = string.Concat(Enumerable.Repeat($"<motion sequenceName=\"Attack_01_A\" selfInputCombo=\"{selfInput}\"/>", motions));
        var e = XElement.Parse($"<skill id=\"{id}\" originID=\"{origin}\"><basic><kinds {kinds}/><combo {combo}/>"
            + $"<stateAttr keyIsDown=\"{(keyIsDown ? 1 : 0)}\"/></basic><level value=\"1\">{m}</level></skill>");
        return SkillData.Load(e, Levels);
    }

    /// <summary>Attack_01_A's combo keys in the body xml (inputcombo 0.2, combo 0.3, selfinputcombo 0.4, selfcombo 0.6).</summary>
    static readonly SkillFollow.ComboKeys Attack01 = Keys((0.2f, SkillFollow.ComboKey.InputCombo), (0.3f, SkillFollow.ComboKey.Combo),
        (0.4f, SkillFollow.ComboKey.SelfInput), (0.6f, SkillFollow.ComboKey.SelfCombo));

    static SkillFollow.ComboKeys Keys(params (float t, SkillFollow.ComboKey k)[] keys)
    {
        var c = new SkillFollow.ComboKeys();
        foreach (var (t, k) in keys) c.Add(t, k);
        return c;
    }

    static SkillFollow.Window At(double pos)
    {
        var w = default(SkillFollow.Window);
        Attack01.Advance(ref w, -1, pos, true);
        return w;
    }

    [Test]
    public void TheBasicAttackPressedAgainBeforeItsSelfInputComboKeyIsDropped()
    {
        // FUN_1417eed70 (:3053-3061): the motion has a selfInputCombo key and m_b114 is not set, so the same skill is refused
        var r = Running();
        int chain = 0;
        foreach (var t in new[] { 0.0, 0.1, 0.25, 0.35 })
            Assert.That(SkillFollow.MayFollow(r, r.Skill, At(t), false, ref chain), Is.False, $"press at {t}");
        Assert.That(SkillFollow.MayFollow(r, r.Skill, At(0.45), false, ref chain), Is.True);
    }

    [Test]
    public void AnAcceptedRepeatPressStartsAtSelfComboNotBefore()
    {
        // FUN_1417ef4a0 (:3107-3121): after "combo" but before "selfcombo" the same skill waits; at "selfcombo" it goes
        var r = Running();
        int chain = 0;
        Assert.That(SkillFollow.MayFollow(r, r.Skill, At(0.45), false, ref chain), Is.True);
        Assert.That(SkillFollow.ChainsNow(r, r.Skill, chain, At(0.45), false), Is.False);
        Assert.That(SkillFollow.ChainsNow(r, r.Skill, chain, At(0.59), false), Is.False);
        Assert.That(SkillFollow.ChainsNow(r, r.Skill, chain, At(0.6), false), Is.True);
    }

    [Test]
    public void TheSequenceEndStartsTheWaitingPress()
    {
        // EndOfSequence (:1062-1070) sets m_b116 / m_b49 / m_b112: neither self combo branch vetoes
        var r = Running();
        var w = At(0.45);
        Assert.That(SkillFollow.ChainsNow(r, r.Skill, 0, w, false), Is.False);
        w.EndOfSequence();
        Assert.That(SkillFollow.ChainsNow(r, r.Skill, 0, w, false), Is.True);
    }

    [Test]
    public void AnotherSkillMayFollowInTheInputComboWindowButNotAfterSelfComboAlone()
    {
        // FUN_1417eed70 (:3053-3061): with m_b114 and without m_b4a a different skill is refused; FUN_1417ef4a0 (:3122-3133):
        // after selfcombo without combo only the same skill chains
        var r = Running();
        var other = Skill(2);
        int chain = 0;
        Assert.That(SkillFollow.MayFollow(r, other, At(0.1), false, ref chain), Is.False, "no window yet");
        Assert.That(SkillFollow.MayFollow(r, other, At(0.25), false, ref chain), Is.True, "in the input window, before the self input key, only the same skill is refused");
        var w = At(0.45);
        Assert.That(SkillFollow.MayFollow(r, other, w, false, ref chain), Is.True, "the input window and the self input key");
        var selfOnly = new SkillFollow.Window { SelfInputCombo = true, InputCombo = true, SelfCombo = true };
        Assert.That(SkillFollow.ChainsNow(r, other, 0, selfOnly, false), Is.False);
        Assert.That(SkillFollow.ChainsNow(r, r.Skill, 0, selfOnly, false), Is.True);
    }

    [Test]
    public void APressSkillIsNeverTakenAgainWhileItRuns()
    {
        // FUN_1417eed70 (:3026-3027): not a key-down skill and the same id
        var r = Running(keyIsDown: false, selfInput: "");
        var w = new SkillFollow.Window { InputCombo = true, SelfInputCombo = true, Combo = true, SelfCombo = true, Ended = true };
        int chain = 5;
        Assert.That(SkillFollow.MayFollow(r, r.Skill, w, false, ref chain), Is.False);
        Assert.That(chain, Is.EqualTo(5), "the chain type is not written");
    }

    [Test]
    public void TheImmediateActiveChainTypes()
    {
        // :3029-3034: 1 any other id, 2 another origin; ChainsNow then needs no combo key, only the last motion (:3093-3096)
        var r = Running(id: 10, selfInput: "");
        int chain = 0;
        Assert.That(SkillFollow.MayFollow(r, Skill(11, kinds: "immediateActive=\"1\""), default, false, ref chain), Is.True);
        Assert.That(chain, Is.EqualTo(1));
        Assert.That(SkillFollow.ChainsNow(r, Skill(11), chain, default, false), Is.True);
        Assert.That(SkillFollow.MayFollow(r, Skill(10, kinds: "immediateActive=\"1\""), default, false, ref chain), Is.False);
        Assert.That(SkillFollow.MayFollow(r, Skill(12, kinds: "immediateActive=\"2\"", origin: 10), default, false, ref chain), Is.False);
        Assert.That(SkillFollow.MayFollow(r, Skill(12, kinds: "immediateActive=\"2\"", origin: 13), default, false, ref chain), Is.True);
        var first = Running(id: 10, selfInput: "", motions: 2, motion: 0);
        Assert.That(SkillFollow.ChainsNow(first, Skill(11), 1, default, false), Is.False, "not the last motion");
    }

    [Test]
    public void ChainTypeZeroNeedsTheLastMotionAndAComboWindow()
    {
        // :3035-3037
        int chain = 0;
        var open = new SkillFollow.Window { InputCombo = true };
        Assert.That(SkillFollow.MayFollow(Running(id: 10, selfInput: ""), Skill(11), default, false, ref chain), Is.False);
        Assert.That(SkillFollow.MayFollow(Running(id: 10, selfInput: ""), Skill(11), open, false, ref chain), Is.True);
        Assert.That(SkillFollow.MayFollow(Running(id: 10, selfInput: "", motions: 2), Skill(11), open, false, ref chain), Is.False);
        Assert.That(SkillFollow.ChainsNow(Running(id: 10, selfInput: ""), Skill(11), 0, open, false), Is.False, "no chain flag yet");
    }

    [Test]
    public void AContinueSkillWithItsKeyHeldRefusesTheNextSkill()
    {
        // :3038-3045: a continue skill (a charging one only at charge time 0) whose key is held
        var w = new SkillFollow.Window { InputCombo = true, KeyHeld = true, ChargeTimeZero = false };
        int chain = 0;
        Assert.That(SkillFollow.MayFollow(Running(id: 10, selfInput: "", kinds: "continue=\"1\""), Skill(11), w, false, ref chain), Is.False);
        var charging = Running(id: 10, selfInput: "", kinds: "continue=\"1\"", combo: "chargingSkill=\"1\"");
        Assert.That(SkillFollow.MayFollow(charging, Skill(11), w, false, ref chain), Is.True, "charging, charge time not 0");
        w.ChargeTimeZero = true;
        Assert.That(SkillFollow.MayFollow(charging, Skill(11), w, false, ref chain), Is.False);
        w.KeyHeld = false;
        Assert.That(SkillFollow.MayFollow(charging, Skill(11), w, false, ref chain), Is.True);
    }

    [Test]
    public void EndSelfInputComboVetoesTheSameSkillAndItsComboOutput()
    {
        // FUN_1417eed70 (:3074-3080) and FUN_1417ef4a0 (:3143-3149); IsOutputSkill (skillleveldata.cpp:516)
        var r = Running(id: 10, combo: "inputSkill=\"10\" outputSkill=\"11\"");
        var w = At(0.45);
        w.EndSelfInput = true;
        int chain = 0;
        Assert.That(SkillFollow.MayFollow(r, r.Skill, w, false, ref chain), Is.False);
        Assert.That(SkillFollow.MayFollow(r, Skill(11), w, false, ref chain), Is.False);
        Assert.That(SkillFollow.MayFollow(r, Skill(12), w, false, ref chain), Is.True);
        Assert.That(SkillFollow.ChainsNow(r, Skill(11), 0, w, false), Is.False);
    }

    [Test]
    public void TheComboKeysLatchInTimeOrder()
    {
        // TextKeyEvent (:1009-1012): inputcombo sets m_b4a, endInputCombo clears it; the later key of one frame wins
        var k = Keys((0.5f, SkillFollow.ComboKey.EndInputCombo), (0.2f, SkillFollow.ComboKey.InputCombo), (0.7f, SkillFollow.ComboKey.EndSelfInput),
            (0.3f, SkillFollow.ComboKey.Combo));
        var w = default(SkillFollow.Window);
        k.Advance(ref w, -1, 0.25, true);
        Assert.That((w.InputCombo, w.Combo), Is.EqualTo((true, false)));
        k.Advance(ref w, 0.25, 0.35, false);
        Assert.That(w.Combo, Is.False, "CanChainCombo false at the combo key");
        k.Advance(ref w, 0.35, 0.8, true);
        Assert.That((w.InputCombo, w.EndSelfInput, w.SelfInputCombo, w.SelfCombo), Is.EqualTo((false, true, false, false)));
        var both = default(SkillFollow.Window);
        k.Advance(ref both, -1, 1.0, true);
        Assert.That(both.InputCombo, Is.False);
        k.Advance(ref both, 0.1, 0.2, true);   // a loop's seek back passes inputcombo again
        Assert.That(both.InputCombo, Is.True);
    }

    [Test]
    public void TheSkillStateHoldsTheWalkUnlessTheSkillMovesByInput()
    {
        // StateSkill::vfunc_8 (:642) / FUN_1417edc20 (:2866): only moveType 2 / 3 read the move input
        foreach (var (mt, holds) in new[] { (0, true), (1, true), (2, false), (3, false) })
        {
            var s = SkillData.Load(XElement.Parse($"<skill id=\"1\"><basic><stateAttr moveType=\"{mt}\"/></basic></skill>"), NoParts);
            Assert.That(SkillFollow.HoldsMove(s), Is.EqualTo(holds), $"moveType {mt}");
        }
        Assert.That(SkillFollow.HoldsMove(null), Is.False);
    }

    [Test]
    public void TheSkillXmlLoadsImmediateActiveAndTheMotionsSelfInputComboKey()
    {
        // <kinds immediateActive> (skilloverlapcompare.cpp:845), the motion's selfInputCombo (skillleveldata.cpp:1165)
        var s = Skill(5, kinds: "immediateActive=\"2\"", selfInput: "myKey");
        Assert.That((s.ImmediateActive, s.GetLevelData(1).Motions[0].SelfInputCombo), Is.EqualTo((2, "myKey")));
    }

    [Test]
    public void TheComboKeyAdvanceAllocatesNothing()
    {
        var r = Running();
        long Window()   // measured twice, the second asserted (the first can take one-time JIT allocations)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            int chain = 0;
            for (int i = 0; i < 1000; i++)
            {
                var w = default(SkillFollow.Window);
                Attack01.Advance(ref w, -1, i * 0.001, true);
                Attack01.Next(-1, i * 0.001);
                SkillFollow.MayFollow(r, r.Skill, w, false, ref chain);
                SkillFollow.ChainsNow(r, r.Skill, chain, w, false);
            }
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Window();
        Assert.That(Window(), Is.LessThan(512));
    }

    [Test]
    public void TheKms2BasicAttackIsHeldAgainstARepeatPressAndStopsTheWalk()
    {
        // the data the user's report ran on: the basic attack 1 is a key-down skill whose motion's selfInputCombo key comes
        // after its start, so a press in its first part is dropped; it does not move by input, so the walk input is held
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var packs = new PackSet(d);
        var s = new GameTables(packs, "en").Skills.GetSkillData(1);
        Assert.That(s, Is.Not.Null);
        var lv = s.GetLevelData(1);
        var m = lv.Motions[0];
        Assert.That(s.KeyIsDown && !string.IsNullOrEmpty(m.SelfInputCombo), Is.True);
        Assert.That(SkillFollow.HoldsMove(s), Is.True);
        var x = Ms2.Formats.Text.PackXml.Read(packs, "Resource/Model/Character", "female/female.xml");   // KMS2_DEV has a BOM
        var seq = x.Root!.Elements().First(e => string.Equals(e.Name.LocalName, m.SequenceName, StringComparison.OrdinalIgnoreCase));
        var keys = new SkillFollow.ComboKeys();
        keys.Set(TextKeys(seq), m.SelfInputCombo);
        float selfInput = seq.Elements("key").Where(k => (string)k.Attribute("text") == m.SelfInputCombo)
            .Select(k => float.Parse((string)k.Attribute("time")!, System.Globalization.CultureInfo.InvariantCulture)).DefaultIfEmpty(-1).First();
        Assert.That(selfInput, Is.GreaterThan(0));
        var r = new CastRecord { Skill = s, Level = lv, Motion = 0 };
        var w = default(SkillFollow.Window);
        keys.Advance(ref w, -1, selfInput * 0.5, true);
        int chain = 0;
        Assert.That(SkillFollow.MayFollow(r, s, w, false, ref chain), Is.False, "a press early in the attack");
        keys.Advance(ref w, selfInput * 0.5, selfInput, true);
        Assert.That(SkillFollow.MayFollow(r, s, w, false, ref chain), Is.True, "a press after the selfInputCombo key");
    }

    static List<(double time, string text)> TextKeys(XElement seq) => seq.Elements("key")
        .Select(k => ((double)float.Parse((string)k.Attribute("time")!, System.Globalization.CultureInfo.InvariantCulture), (string)k.Attribute("text")))
        .ToList();

    [Test]
    public void TheComboKeysTestTheirNamesAsTheClientDoes()
    {
        // TextKeyEvent (:999-1027): combo, inputcombo, selfcombo and the motion's key exactly; the two end keys in any case
        Assert.That(SkillFollow.ComboKeys.Classify("combo", null), Is.EqualTo(SkillFollow.ComboKey.Combo));
        Assert.That(SkillFollow.ComboKeys.Classify("Combo", null), Is.Null);
        Assert.That(SkillFollow.ComboKeys.Classify("InputCombo", null), Is.Null);
        Assert.That(SkillFollow.ComboKeys.Classify("endinputcombo", null), Is.EqualTo(SkillFollow.ComboKey.EndInputCombo));
        Assert.That(SkillFollow.ComboKeys.Classify("ENDSELFINPUTCOMBO", null), Is.EqualTo(SkillFollow.ComboKey.EndSelfInput));
        Assert.That(SkillFollow.ComboKeys.Classify("SelfCombo", null), Is.Null);
        Assert.That(SkillFollow.ComboKeys.Classify("myKey", "myKey"), Is.EqualTo(SkillFollow.ComboKey.SelfInput));
        Assert.That(SkillFollow.ComboKeys.Classify("mykey", "myKey"), Is.Null);
        Assert.That(SkillFollow.ComboKeys.Classify("", ""), Is.Null);
    }

    [Test]
    public void EveryKeyOfANameCountsSoASecondWindowReopens()
    {
        // each text key is its own event: a second inputcombo after endInputCombo opens the window again
        var k = new SkillFollow.ComboKeys();
        k.Set(new List<(double, string)> { (0.3, "inputcombo"), (0.1, "inputcombo"), (0.2, "endInputCombo"), (0.5, "move0") }, null);
        Assert.That(k.Count, Is.EqualTo(3));
        var w = default(SkillFollow.Window);
        k.Advance(ref w, -1, 0.25, true);
        Assert.That(w.InputCombo, Is.False, "closed at 0.2");
        k.Advance(ref w, 0.25, 0.35, true);
        Assert.That(w.InputCombo, Is.True, "the second window");
        var one = default(SkillFollow.Window);
        k.Advance(ref one, -1, 0.4, true);
        Assert.That(one.InputCombo, Is.True, "one frame over all three: the latest key wins");
    }

    [Test]
    public void TheComboKeysComeInTimeOrderAndTellTheChainKeys()
    {
        // the HUD reads the keys one time at a time (Next), so a point before a chain key fires first; Advance is true at an
        // accepted combo or a selfcombo, where TextKeyEvent calls FUN_1417ef4a0 (:1004, :1027)
        var k = Keys((0.6f, SkillFollow.ComboKey.SelfCombo), (0.2f, SkillFollow.ComboKey.InputCombo), (0.3f, SkillFollow.ComboKey.Combo));
        Assert.That(k.Next(-1, 1), Is.EqualTo(0.2f));
        Assert.That(k.Next(0.2f, 1), Is.EqualTo(0.3f));
        Assert.That(k.Next(0.3f, 0.5), Is.EqualTo(-1));
        var w = default(SkillFollow.Window);
        Assert.That(k.Advance(ref w, -1, 0.2f, true), Is.False);
        Assert.That(k.Advance(ref w, 0.2f, 0.3f, false), Is.False, "a combo key CanChainCombo refuses");
        Assert.That(k.Advance(ref w, -1, 0.3f, true), Is.True, "an accepted combo key");
        Assert.That(k.Advance(ref w, 0.3f, 0.6f, true), Is.True, "selfcombo");
    }

    [Test]
    public void TheEndCastsTheNextWithoutTheSelfInputVeto()
    {
        // FUN_1417ee120 (:2911-2928) at the end of the last motion: vfunc_102 directly, not FUN_1417ef4a0 (whose
        // endSelfInputcombo test, :3150, refuses the same skill); only the hold attack flag sends the combo output to idle
        var r = Running(combo: "inputSkill=\"1\" outputSkill=\"7\"");
        var w = At(0.45);
        w.EndSelfInput = true;
        w.EndOfSequence();
        Assert.That(SkillFollow.ChainsNow(r, r.Skill, 0, w, false), Is.False);
        Assert.That(SkillFollow.CastsAtEnd(r, r.Skill, false), Is.True);
        Assert.That(SkillFollow.CastsAtEnd(r, Skill(7), false), Is.True);
        Assert.That(SkillFollow.CastsAtEnd(r, Skill(7), true), Is.False, "the combo output with the hold attack flag");
        Assert.That(SkillFollow.CastsAtEnd(r, Skill(8), true), Is.True);
    }

    [Test]
    public void TheWindowFlagsCompareWithoutBoxing()
    {
        var a = At(0.45);
        var b = At(0.45);
        Assert.That(a.Same(b), Is.True);
        b.KeyHeld = true;
        Assert.That(a.Same(b), Is.False);
        Assert.That(Alloc.Least(() => { for (int i = 0; i < 1000; i++) a.Same(b); }), Is.LessThan(512));
    }

    [Test]
    public void TheBodyXmlComboWindowsAllLoad()
    {
        // the data's sequences with a combo key name twice (two windows): every key loads, so each window opens and closes
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var packs = new PackSet(d);
        int twice = 0, withKeys = 0;
        foreach (var file in new[] { "female/female.xml", "male/male.xml" })
        {
            var x = Ms2.Formats.Text.PackXml.Read(packs, "Resource/Model/Character", file);
            foreach (var seq in x.Root!.Elements())
            {
                var keys = TextKeys(seq);
                var k = new SkillFollow.ComboKeys();
                k.Set(keys, null);
                int n = keys.Count(t => SkillFollow.ComboKeys.Classify(t.text, null) != null);
                Assert.That(k.Count, Is.EqualTo(n), seq.Name.LocalName);
                if (n != 0) withKeys++;
                if (keys.Where(t => SkillFollow.ComboKeys.Classify(t.text, null) != null).GroupBy(t => t.text).Any(g => g.Count() > 1))
                {
                    twice++;
                    TestContext.Progress.WriteLine($"{file} {seq.Name.LocalName}: " + string.Join(" ", keys.Select(t => $"{t.text}@{t.time:0.###}")));
                }
            }
        }
        TestContext.Progress.WriteLine($"sequences with a combo key twice: {twice} of {withKeys}");
        Assert.That(withKeys, Is.GreaterThan(0));
    }
}
