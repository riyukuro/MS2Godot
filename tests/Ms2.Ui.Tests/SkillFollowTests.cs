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
    static readonly SkillFollow.ComboKeys Attack01 = new()
    {
        InputCombo = 0.2f, EndInputCombo = -1, SelfInput = 0.4f, EndSelfInput = -1, Combo = 0.3f, SelfCombo = 0.6f,
    };

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
        Assert.That(SkillFollow.MayFollow(r, other, At(0.25), false, ref chain), Is.True, "m_b4a: before m_b114 only the same skill is refused");
        var w = At(0.45);
        Assert.That(SkillFollow.MayFollow(r, other, w, false, ref chain), Is.True, "m_b4a and m_b114");
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
        Assert.That(chain, Is.EqualTo(5), "m_nChainType is not written");
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
        var k = new SkillFollow.ComboKeys { InputCombo = 0.2f, EndInputCombo = 0.5f, SelfInput = -1, EndSelfInput = 0.7f, Combo = 0.3f, SelfCombo = -1 };
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
        float Key(string text) => seq.Elements("key").Where(k => string.Equals((string)k.Attribute("text"), text, StringComparison.OrdinalIgnoreCase))
            .Select(k => float.Parse((string)k.Attribute("time")!, System.Globalization.CultureInfo.InvariantCulture)).DefaultIfEmpty(-1).First();
        var keys = new SkillFollow.ComboKeys
        {
            InputCombo = Key("inputcombo"), EndInputCombo = Key("endInputCombo"), SelfInput = Key(m.SelfInputCombo),
            EndSelfInput = Key("endSelfInputcombo"), Combo = Key("combo"), SelfCombo = Key("selfcombo"),
        };
        Assert.That(keys.SelfInput, Is.GreaterThan(0));
        var r = new CastRecord { Skill = s, Level = lv, Motion = 0 };
        var w = default(SkillFollow.Window);
        keys.Advance(ref w, -1, keys.SelfInput * 0.5, true);
        int chain = 0;
        Assert.That(SkillFollow.MayFollow(r, s, w, false, ref chain), Is.False, "a press early in the attack");
        keys.Advance(ref w, keys.SelfInput * 0.5, keys.SelfInput, true);
        Assert.That(SkillFollow.MayFollow(r, s, w, false, ref chain), Is.True, "a press after the selfInputCombo key");
    }
}
