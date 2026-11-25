using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// <see cref="ObjectData"/>: GetObjectData (@0x140704ec0, objectdata.cpp:91-378), Object/%08d.xml's &lt;FunctionCube&gt; and
/// &lt;ConfigurableCube&gt;.
/// </summary>
public class ObjectDataTests
{
    static ObjectData P(string xml) => ObjectData.Parse(XDocument.Parse(xml), 1);

    [Test]
    public void AFileWithoutTheMs2RootHasNoData() => Assert.That(P("<root/>"), Is.Null);

    [Test]
    public void AFileWithoutAFunctionCubeHasItDisabled()
    {
        var d = P("<ms2><switch/></ms2>");
        Assert.That(d.Cube.Enabled, Is.False);
        Assert.That(d.Config.Enabled, Is.EqualTo(0));
    }

    [TestCase("Sensor", 2)]
    [TestCase("Switch", 3)]
    [TestCase("FunctionUI", 3)]
    [TestCase("Ride", 3)]
    [TestCase("OpenWeb", 3)]
    [TestCase("SpawnNPC", 4)]
    [TestCase("Skill", 5)]
    [TestCase("Portal", 6)]
    [TestCase("Breeding", 7)]
    [TestCase("Farming", 8)]
    [TestCase("PVP", 9)]
    [TestCase("SpawnPoint", 10)]
    [TestCase("InstallNPC", 0xb)]
    [TestCase("Notice", 0xc)]
    [TestCase("Nurturing", 0xd)]
    [TestCase("", 1)]
    [TestCase("switch", 1)]
    public void TheControlTypeIsTheClientsNumber(string type, int kind) =>
        Assert.That(P($"<ms2><FunctionCube ControlType=\"{type}\"/></ms2>").Cube.ControlType, Is.EqualTo(kind));

    [TestCase("FunctionUI", "TriggerTool", 1)]
    [TestCase("FunctionUI", "FittingDoll", 2)]
    [TestCase("FunctionUI", "itemFusion", 3)]
    [TestCase("FunctionUI", "bank", 4)]
    [TestCase("FunctionUI", "BeautyMirror", 5)]
    [TestCase("FunctionUI", "Password", 6)]
    [TestCase("FunctionUI", "Summon", 7)]
    [TestCase("FunctionUI", "Bank", 0)]
    [TestCase("Switch", "bank", 0)]
    public void TheUiFunctionIsReadForAFunctionUiCubeOnly(string type, string ui, int expected) =>
        Assert.That(P($"<ms2><FunctionCube ControlType=\"{type}\" UseUiFunction=\"{ui}\"/></ms2>").Cube.UiFunction, Is.EqualTo(expected));

    [Test]
    public void TheStatesSequencesAndAutoChangeAreRead()
    {
        var c = P("<ms2><FunctionCube StateChangeAction=\"Attack\" AutoStateChange=\"1-0\" AutoStateChangeTime=\"7000\" DefaultState=\"2\" " +
                  "ZeroSeq=\"Idle_A\" FirstSeq=\"Dead_A\" SecondIngSeq=\"Regen_A\" ZeroPhysics=\"1\" FirstEffect=\"fx\"/></ms2>").Cube;
        Assert.That((c.StateChangeAction, c.AutoStateA, c.AutoStateB, c.AutoStateTime, c.DefaultState), Is.EqualTo((2, 1, 0, 7000, 2)));
        Assert.That(c.Sequences, Is.EqualTo(new[] { "Idle_A", "Dead_A", "", "", "", "Regen_A" }));
        Assert.That(c.Effects, Is.EqualTo(new[] { "", "fx", "", "", "", "" }));
        Assert.That(c.Physics, Is.EqualTo(new[] { 1, 0, 0 }));
    }

    [TestCase("ActionKey", 1)]
    [TestCase("Attack", 2)]
    [TestCase("", 0)]
    [TestCase("actionkey", 0)]
    public void TheStateChangeActionIsKeyOrAttack(string action, int expected) =>
        Assert.That(P($"<ms2><FunctionCube StateChangeAction=\"{action}\"/></ms2>").Cube.StateChangeAction, Is.EqualTo(expected));

    [TestCase("1-0-2")]
    [TestCase("1")]
    [TestCase("")]
    public void OnlyATwoPartAutoStateChangeSetsTheStates(string auto)
    {
        var c = P($"<ms2><FunctionCube AutoStateChange=\"{auto}\"/></ms2>").Cube;
        Assert.That((c.AutoStateA, c.AutoStateB), Is.EqualTo((0, 0)));
    }

    [Test]
    public void TheBootyPopupFlagIsTheFirstAdditionalEffectsPopup()
    {
        var c = P("<ms2><FunctionCube><firstAdditionalEffect firstAdditionalEffectID=\"50000011\" firstAdditionalEffectLevel=\"3\" " +
                  "firstAdditionalPopup=\"1\" firstAdditionalRange=\"150.5\"/></FunctionCube></ms2>").Cube;
        Assert.That((c.FirstEffect.Enabled, c.FirstEffect.EffectId, c.FirstEffect.Level, c.FirstEffect.Popup, c.FirstEffect.Range),
            Is.EqualTo((true, 50000011, (short)3, 1, 150.5f)));
        Assert.That(P("<ms2><FunctionCube/></ms2>").Cube.FirstEffect.Enabled, Is.False);
    }

    [TestCase("1", "", "FittingDoll_F_Default01")]
    [TestCase("0", "", "FittingDoll_M_Default01")]
    [TestCase("2", "", "FittingDoll_M_Default01")]
    [TestCase("1", "Doll_A", "Doll_A")]
    public void AFittingDollWithoutAnAssetTakesTheGendersDefault(string gender, string asset, string expected) =>
        Assert.That(P($"<ms2><FunctionCube><fittingdoll fittingdollGender=\"{gender}\" fittingdollAsset=\"{asset}\"/></FunctionCube></ms2>")
            .Cube.Doll.Asset, Is.EqualTo(expected));

    [Test]
    public void TheNurturingListsAreKeyedFromOne()
    {
        var n = P("<ms2><FunctionCube><nurturing pluralNpcID=\"11,,12\" requiredGrowth=\"10,20\" rewardItem=\"1,2,3\" " +
                  "rewardItemByFeeding=\"1,2\" rewardItemByGrowth=\"4,5,6,7,8,9\"/></FunctionCube></ms2>").Cube.Nurture;
        Assert.That(n.PluralNpcIds, Is.EqualTo(new Dictionary<short, int> { [1] = 11, [2] = 12 }));
        Assert.That(n.RequiredGrowth, Is.EqualTo(new Dictionary<short, ulong> { [1] = 10, [2] = 20 }));
        Assert.That(n.Reward, Is.EqualTo((1, 2, 3)));
        Assert.That(n.RewardByFeeding, Is.EqualTo((0, 0, 0)), "a two-part list is not a reward");
        Assert.That(n.RewardByGrowth[1], Is.EqualTo((4, 5, 6)));
        Assert.That(n.RewardByGrowth[2], Is.EqualTo((7, 8, 9)));
    }

    [Test]
    public void TheWebLinkAndConfigurableCubeAreRead()
    {
        var d = P("<ms2><FunctionCube ControlType=\"OpenWeb\"><openWeb webURL=\"http://x\" opentype=\"1\" target=\"t\"/></FunctionCube>" +
                  "<ConfigurableCube enable=\"1\" id=\"cfg\"/></ms2>");
        Assert.That((d.Cube.WebUrl, d.Cube.WebOpenType, d.Cube.WebTarget), Is.EqualTo(("http://x", 1, "t")));
        Assert.That((d.Config.Enabled, d.Config.Id), Is.EqualTo((1, "cfg")));
    }

    /// <summary>
    /// Every Object/*.xml of MS2_DATA and MS2_GMS2_DATA: it loads, the cube is enabled exactly when the file has a
    /// &lt;FunctionCube&gt;, its kind is the client's number for its ControlType name, a FunctionUI cube's UseUiFunction gives
    /// its sub-kind, and the popup flag follows firstAdditionalPopup. Then the rows both data sets share: the bank cubes
    /// 60000001 and 60000002 (sub-kind 4, the object store) and the switch 10000001. Prints the kinds by data set.
    /// </summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryObjectFileReadsItsFunctionCube(string variable)
    {
        using var packs = new PackSet(RcGap.Data(variable));
        var files = packs.List("Xml").Where(e => e.StartsWith("object/", StringComparison.Ordinal) && e.EndsWith(".xml", StringComparison.Ordinal)).ToList();
        Assert.That(files, Is.Not.Empty);
        var kinds = new SortedDictionary<int, int>();
        int popups = 0, enabled = 0;
        var bad = new List<string>();
        var ui = new Dictionary<string, int> { ["TriggerTool"] = 1, ["FittingDoll"] = 2, ["itemFusion"] = 3, ["bank"] = 4, ["BeautyMirror"] = 5, ["Password"] = 6, ["Summon"] = 7 };
        foreach (var f in files)
        {
            uint id = uint.Parse(f.Substring(7, 8), CultureInfo.InvariantCulture);
            var doc = PackXml.Parse(packs.Read("Xml", f));
            var d = ObjectData.Load(packs, id);
            if (d == null) { bad.Add($"{f}: no data"); continue; }
            var fc = doc.Root.Element("FunctionCube");
            if (d.Cube.Enabled != (fc != null)) bad.Add($"{f}: enabled {d.Cube.Enabled}");
            if (fc == null) continue;
            enabled++;
            kinds[d.Cube.ControlType] = kinds.GetValueOrDefault(d.Cube.ControlType) + 1;
            string type = (string)fc.Attribute("ControlType") ?? "";
            if (type != d.Cube.ControlTypeName) bad.Add($"{f}: type {d.Cube.ControlTypeName}");
            int wantUi = type == "FunctionUI" && ui.TryGetValue((string)fc.Attribute("UseUiFunction") ?? "", out int u) ? u : 0;
            if (d.Cube.UiFunction != wantUi) bad.Add($"{f}: ui {d.Cube.UiFunction} want {wantUi}");
            bool popup = (string)fc.Element("firstAdditionalEffect")?.Attribute("firstAdditionalPopup") == "1";
            if ((d.Cube.FirstEffect.Popup == 1) != popup) bad.Add($"{f}: popup {d.Cube.FirstEffect.Popup}");
            if (popup) popups++;
        }
        TestContext.Progress.WriteLine($"{variable}: {files.Count} files, {enabled} function cubes, {popups} booty popups, kinds " +
            string.Join(" ", kinds.Select(k => $"{k.Key}:{k.Value}")));
        Assert.That(bad, Is.Empty);
        Assert.That(enabled, Is.GreaterThan(400));
        Assert.That(kinds.Keys, Is.SupersetOf(new[] { 2, 3, 4, 5, 6, 7, 8, 9, 10, 0xb, 0xc, 0xd }), "every control type has a cube in the data");
        foreach (uint bank in new uint[] { 60000001, 60000002 })
        {
            var b = ObjectData.Load(packs, bank);
            Assert.That((b.Cube.ControlType, b.Cube.UiFunction), Is.EqualTo((3, 4)), $"bank cube {bank}");
        }
        var sw = ObjectData.Load(packs, 10000001).Cube;
        Assert.That((sw.ControlType, sw.StateChangeAction, sw.AutoStateA, sw.AutoStateB, sw.AutoStateTime, sw.Sequences[0], sw.Sequences[1]),
            Is.EqualTo((3, 2, 1, 0, 7000, "Idle_A", "Dead_A")));
        Assert.That(ObjectData.Load(packs, 99999999), Is.Null, "no file, no data");
    }
}
