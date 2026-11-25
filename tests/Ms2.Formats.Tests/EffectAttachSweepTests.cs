using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// Every nif part of every effect xml, by attach type (CNifEffect::vfunc_3's switch, nifeffectparam.cpp:717), and what each
/// hangs on when the effect plays on a PC (<see cref="EffectAttachRule.Where"/>), against the nodes the m_hNode GetObjectByName
/// searches on a PC: the male and female body nifs' NiNodes and the PC effect dummies (Eff_Body_World ...):
/// - types 0 and 1: a body bone, else a dummy, else the model root;
/// - type 2: the worn item mesh of that name (the PC wears it when the item's idle effect plays);
/// - types 3 / 4: the Point01 node of the weapon in the left / right hand.
/// Also the swooshes whose trail node is a node of a type 1-4 nif part of the same effect (the trail rides that part, so it
/// starts where the part hangs), and the parts the client wraps in a SeparateNode or makes relative to the target.
/// </summary>
public class EffectAttachSweepTests
{
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryAttachedPartHangsOnItsNode(string env)
    {
        var dir = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore($"{env} not set");
        using var packs = new PackSet(dir);
        var spec = new NifSpec(NifEnv.NifXml());
        const string effectArchive = "Resource/Model/Effect";
        HashSet<string> Bones(string nif) =>
            ModelData.Load(NifFile.Load(spec, packs.Read("Resource/Model/Character", nif))).Bones.Select(b => b.FileName ?? b.Name).ToHashSet(StringComparer.Ordinal);
        var male = Bones("male/m_body.nif");
        var female = Bones("female/f_body.nif");
        Assert.That(male, Does.Contain("Bip01 R Hand"));
        var bones = male.Intersect(female).ToHashSet(StringComparer.Ordinal);
        int oneBody = male.Count + female.Count - 2 * bones.Count;
        var table = EffectDummyTable.Load(packs);
        var dummyNames = table.Pc(0).Select(d => d.Name).Intersect(table.Pc(1).Select(d => d.Name)).ToHashSet(StringComparer.Ordinal);
        Assert.That(dummyNames, Does.Contain("Eff_Body_World"));

        var parts = new int[5];
        var on = new Dictionary<(int Kind, EffectHang On), int>();
        var perBone = new Dictionary<string, int>(StringComparer.Ordinal);
        var notBone = new Dictionary<string, int>(StringComparer.Ordinal);
        var worldBone = new Dictionary<string, int>(StringComparer.Ordinal);
        var worldDummy = new Dictionary<string, int>(StringComparer.Ordinal);
        var perDummy = new Dictionary<string, int>(StringComparer.Ordinal);
        var worldRoot = new Dictionary<string, int>(StringComparer.Ordinal);
        var meshNames = new Dictionary<string, int>(StringComparer.Ordinal);
        var separate = new int[5]; var relative = new int[5];
        int xmls = 0, swooshes = 0, swooshOnBody = 0, swooshOnPart = 0, swooshOnNeither = 0;
        var swooshPartOn = new Dictionary<string, int>(StringComparer.Ordinal);
        var nifNodes = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        HashSet<string> NodesOf(string nif)
        {
            string entry = nif.Replace('\\', '/').ToLowerInvariant();
            if (nifNodes.TryGetValue(entry, out var set)) return set;
            set = new HashSet<string>(StringComparer.Ordinal);
            if (packs.Exists(effectArchive, entry))
                try { foreach (var b in ModelData.Load(NifFile.Load(spec, packs.Read(effectArchive, entry))).Bones) set.Add(b.FileName ?? b.Name); }
                catch (Exception) { }    // an unreadable nif names no node
            return nifNodes[entry] = set;
        }
        foreach (var entry in packs.List("Xml").Where(e => e.StartsWith("effect/", StringComparison.Ordinal) && e.EndsWith(".xml", StringComparison.Ordinal)).OrderBy(e => e, StringComparer.Ordinal))
        {
            List<EffectPart> list;
            try { list = EffectTable.Parse(PackXml.Parse(packs.Read("Xml", entry))); } catch { continue; }
            xmls++;
            foreach (var p in list)
            {
                if (p.TypeId != EffectSkipTable.TypeNif) continue;
                int k = p.AttachKind;
                Assert.That(k, Is.InRange(0, 4), entry);
                parts[k]++;
                string name = p.ParentNode ?? "";
                bool bone = name.Length > 0 && bones.Contains(name);
                bool dummy = !bone && dummyNames.Contains(name);
                var hang = EffectAttachRule.Where(k, hasModel: true, parentIsBone: bone, parentIsDummy: dummy, ownerIsPc: true, itemMeshFound: name.Length > 0, weaponPointFound: true);
                on[(k, hang)] = on.GetValueOrDefault((k, hang)) + 1;
                if (k == 0)
                {
                    // type 0 with applyNodeTransform takes the node's world (a bone's, turned to Y up), else the model root's
                    if (!p.ApplyNodeTransform) continue;
                    var to = bone ? worldBone : dummy ? worldDummy : worldRoot;
                    to[name] = to.GetValueOrDefault(name) + 1;
                    continue;
                }
                if (EffectAttachRule.Separate(p)) separate[k]++;
                if (EffectAttachRule.Relative(p)) relative[k]++;
                if (k == 1)
                {
                    var into = bone ? perBone : dummy ? perDummy : notBone;
                    into[name] = into.GetValueOrDefault(name) + 1;
                    Assert.That(hang, Is.EqualTo(bone ? EffectHang.Bone : dummy ? EffectHang.Dummy : EffectHang.ModelRoot), entry);
                }
                if (k == 2) meshNames[name] = meshNames.GetValueOrDefault(name) + 1;
                if (k is 3 or 4) Assert.That(hang, Is.EqualTo(EffectHang.WeaponPoint), entry);
            }
            foreach (var s in list.Where(p => p.TypeId == EffectSkipTable.TypeSwoosh && p.Strip != null))
            {
                swooshes++;
                string[] trail = { s.Strip.Node1 ?? "", s.Strip.Node2 ?? "" };
                if (trail.All(n => bones.Contains(n) || dummyNames.Contains(n))) { swooshOnBody++; continue; }
                var carrier = list.FirstOrDefault(p => p.TypeId == EffectSkipTable.TypeNif && p.AttachKind is >= 1 and <= 4 && !string.IsNullOrEmpty(p.FilePath)
                    && trail.Any(n => !bones.Contains(n) && NodesOf(p.FilePath).Contains(n)));
                if (carrier == null) { swooshOnNeither++; continue; }
                swooshOnPart++;
                string cp = carrier.ParentNode ?? "";
                string where = carrier.AttachKind == 1 ? (bones.Contains(cp) || dummyNames.Contains(cp) ? cp : "model root") : $"type {carrier.AttachKind}";
                swooshPartOn[where] = swooshPartOn.GetValueOrDefault(where) + 1;
            }
        }
        string Top(Dictionary<string, int> d, int n) => string.Join(", ", d.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(n).Select(kv => $"[{kv.Key}] {kv.Value}"));
        string On(int k) => string.Join(", ", on.Where(kv => kv.Key.Kind == k).OrderBy(kv => kv.Key.On).Select(kv => $"{kv.Key.On} {kv.Value}"));
        TestContext.Out.WriteLine($"{env}: {xmls} xmls; nif parts by attach type 0/1/2/3/4: {string.Join("/", parts)}; PC body: {bones.Count} bones in both bodies, {oneBody} in one only");
        for (int k = 0; k <= 4; k++)
            TestContext.Out.WriteLine($"{env}: type {k}: {On(k)}" + (k > 0 ? $"; SeparateNode {separate[k]}, relative to the target {relative[k]}" : ""));
        TestContext.Out.WriteLine($"{env}: PC dummies: {string.Join(", ", dummyNames.OrderBy(n => n, StringComparer.Ordinal))}");
        TestContext.Out.WriteLine($"{env}: type 0 with applyNodeTransform on bones ({worldBone.Count} bones, {worldBone.Values.Sum()} parts): {Top(worldBone, 10)}");
        TestContext.Out.WriteLine($"{env}: type 0 with applyNodeTransform on dummies ({worldDummy.Values.Sum()} parts): {Top(worldDummy, 10)}");
        TestContext.Out.WriteLine($"{env}: type 0 with applyNodeTransform on the model root ({worldRoot.Count} names, {worldRoot.Values.Sum()} parts): {Top(worldRoot, 10)}");
        TestContext.Out.WriteLine($"{env}: type 1 on dummies ({perDummy.Values.Sum()} parts): {Top(perDummy, 10)}");
        TestContext.Out.WriteLine($"{env}: type 1 on bones ({perBone.Count} bones): {Top(perBone, 12)}");
        TestContext.Out.WriteLine($"{env}: type 1 on the model root ({notBone.Count} names): {Top(notBone, 12)}");
        TestContext.Out.WriteLine($"{env}: type 2 worn meshes: {Top(meshNames, 10)}");
        TestContext.Out.WriteLine($"{env}: swooshes {swooshes}: {swooshOnBody} on body bones, {swooshOnPart} on a type 1-4 part's node ({Top(swooshPartOn, 10)}), {swooshOnNeither} on neither");

        // the client's rule, held here: type 1 never makes nothing on a model, types 2-4 make nothing without their node
        Assert.That(on.Keys.Where(key => key.Kind == 1).Select(key => key.On), Is.SubsetOf(new[] { EffectHang.Bone, EffectHang.Dummy, EffectHang.ModelRoot }));
        Assert.That(perDummy.Keys, Does.Contain("Eff_Body_World"));
        Assert.That(parts[1], Is.GreaterThan(1000), "type 1 parts");
        Assert.That(perBone.Keys, Does.Contain("Bip01 R Hand"));
        Assert.That(EffectAttachRule.Where(2, true, false, false, false, true, true), Is.EqualTo(EffectHang.None), "type 2 on an owner that is not a PC");
        Assert.That(EffectAttachRule.Where(4, true, false, false, true, false, false), Is.EqualTo(EffectHang.None), "type 4 with no weapon Point01");
        Assert.That(EffectAttachRule.Where(1, false, true, false, true, true, true), Is.EqualTo(EffectHang.None), "type 1 with no model");
        Assert.That(EffectAttachRule.Where(1, true, false, true, false, false, false), Is.EqualTo(EffectHang.Dummy), "type 1 on a dummy");
        Assert.That(EffectAttachRule.WeaponSlot(3), Is.EqualTo(4));
        Assert.That(EffectAttachRule.WeaponSlot(4), Is.EqualTo(5));
    }
}
