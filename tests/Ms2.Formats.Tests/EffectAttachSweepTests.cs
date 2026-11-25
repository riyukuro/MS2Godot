using System.Xml.Linq;
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
/// - type 2: the node of that name in the worn item's model (FUN_14154d410 walks the owner's nodes for the name on a worn
///   item), checked against the model of every item whose characterIdle effect has the part: each finds it, as a mesh,
///   as a node of a kfm asset (whose own skeleton the Godot item keeps), or as a nif asset's self node (which it keeps);
/// - types 3 / 4: the Point01 node of the weapon in the left / right hand (FindPoint01Node), checked against the model of
///   every weapon (every RH / LH asset of item models 130-159).
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
        var meshParts = new List<(string Xml, EffectPart Part)>();
        var weaponParts = new List<(string Xml, EffectPart Part)>();
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
                if (k >= 2)
                {
                    if (EffectAttachRule.Separate(p)) separate[k]++;
                    if (EffectAttachRule.Relative(p)) relative[k]++;
                    (k == 2 ? meshParts : weaponParts).Add((entry, p));    // resolved against the items below
                    continue;
                }
                var hang = EffectAttachRule.Where(k, hasModel: true, parentIsBone: bone, parentIsDummy: dummy, ownerIsPc: true, itemMeshFound: false, weaponPointFound: false);
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
                var into = bone ? perBone : dummy ? perDummy : notBone;
                into[name] = into.GetValueOrDefault(name) + 1;
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
        var items = new ItemModels(packs, spec);

        // type 2: each item whose characterIdle effect has the part, against the nodes of that item's model
        var idleOf = items.CharacterIdle();
        int meshPairs = 0, asMesh = 0, asNode = 0, meshNone = 0, meshNoItem = 0;
        var meshMissing = new List<string>();
        var meshAsNode = new List<string>();
        foreach (var (xml, p) in meshParts)
        {
            var wearers = idleOf.Where(kv => kv.Value == xml).Select(kv => kv.Key).ToList();
            if (wearers.Count == 0) { meshNoItem++; continue; }
            foreach (int id in wearers)
            {
                meshPairs++;
                var models = items.Models(id).ToList();
                bool mesh = models.Any(m => m.Model.Meshes.Any(x => (x.FileName ?? x.Name) == p.ParentNode));
                var nodeIn = mesh ? default : models.FirstOrDefault(m => m.Model.Bones.Any(b => (b.FileName ?? b.Name) == p.ParentNode));
                bool node = nodeIn.Model != null;
                // a rigid (nif) item keeps only its self nodes besides its meshes (EquipApplier.SelfNodes)
                if (node && !nodeIn.Kfm) Assert.That(nodeIn.Self != null && p.ParentNode.StartsWith(nodeIn.Self, StringComparison.Ordinal),
                    $"item {id}: [{p.ParentNode}] is a node of a nif asset that is not its self node [{nodeIn.Self}]");
                var hang = EffectAttachRule.Where(2, true, false, false, ownerIsPc: true, itemMeshFound: mesh || node, weaponPointFound: false);
                on[(2, hang)] = on.GetValueOrDefault((2, hang)) + 1;
                if (mesh) asMesh++; else if (node) { asNode++; meshAsNode.Add($"{id} [{p.ParentNode}]"); } else { meshNone++; meshMissing.Add($"{id} [{p.ParentNode}] {xml}"); }
            }
        }

        // types 3 / 4: every weapon asset, by hand and item group (id / 100000)
        var point = new Dictionary<(string Hand, int Group), (int With, int Without)>();
        var noPoint = new List<(int Id, string Hand)>();
        foreach (var (id, hand, md) in items.Weapons())
        {
            bool has = md.Bones.Any(b => (b.FileName ?? b.Name) == EffectAttachRule.WeaponPointNode);
            if (!has) noPoint.Add((id, hand));
            var c = point.GetValueOrDefault((hand, id / 100000));
            point[(hand, id / 100000)] = has ? (c.With + 1, c.Without) : (c.With, c.Without + 1);
        }
        foreach (var (xml, p) in weaponParts)
        {
            string hand = p.AttachKind == 3 ? "LH" : "RH";
            foreach (var (key, c) in point.Where(kv => kv.Key.Hand == hand))
            {
                if (c.With > 0) on[(p.AttachKind, EffectHang.WeaponPoint)] = on.GetValueOrDefault((p.AttachKind, EffectHang.WeaponPoint)) + c.With;
                if (c.Without > 0) on[(p.AttachKind, EffectHang.None)] = on.GetValueOrDefault((p.AttachKind, EffectHang.None)) + c.Without;
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
        TestContext.Out.WriteLine($"{env}: type 2: {meshParts.Count} parts, {meshNoItem} on no item's characterIdle; {meshPairs} item / part pairs: the name is a mesh of the item's model {asMesh}, a node {asNode} ({string.Join(", ", meshAsNode.Take(8))}), neither {meshNone}" + (meshMissing.Count > 0 ? $" ({string.Join(", ", meshMissing.Take(8))})" : ""));
        TestContext.Out.WriteLine($"{env}: types 3 / 4: {weaponParts.Count} parts (types 3 / 4 count weapon assets); weapon assets with / without Point01 by hand and group: {string.Join(", ", point.OrderBy(kv => kv.Key.Hand, StringComparer.Ordinal).ThenBy(kv => kv.Key.Group).Select(kv => $"{kv.Key.Hand} {kv.Key.Group} {kv.Value.With}/{kv.Value.Without}"))}");
        TestContext.Out.WriteLine($"{env}: swooshes {swooshes}: {swooshOnBody} on body bones, {swooshOnPart} on a type 1-4 part's node ({Top(swooshPartOn, 10)}), {swooshOnNeither} on neither");

        // npc dummies (DummyWorld: the parent's scale counts only with applyLocalScale), on the rows whose model is scaled
        var npcRows = new NpcTable(packs, table).Rows.Where(r => !string.IsNullOrEmpty(r.Kfm)).ToList();
        var scaled = npcRows.Where(r => Math.Abs(r.Scale - 1f) > 1e-3f).ToList();
        var modes = scaled.SelectMany(r => r.EffectDummies).GroupBy(d => (d.ApplyWorldTransform, d.ApplyLocalScale)).OrderBy(g => g.Key.ApplyWorldTransform).ThenBy(g => g.Key.ApplyLocalScale)
            .Select(g => $"{(g.Key.ApplyWorldTransform ? "world" : "plain")}{(g.Key.ApplyLocalScale ? " local scale" : "")} {g.Count()}");
        TestContext.Out.WriteLine($"{env}: npc rows with a model {npcRows.Count}, scaled {scaled.Count}; their dummies: {string.Join(", ", modes)}");

        // the data, held here: every type 2 part finds its node on every item whose idle effect it is; the weapons without a
        // Point01 node (a type 3 / 4 part makes none on them) are the shields (left hand, 141xxxxx) and GMS2's throwing knife
        // model 13400000
        Assert.That(meshNoItem + meshNone, Is.Zero, "type 2 parts with no item, or no node on an item");
        Assert.That(meshPairs, Is.GreaterThan(10), "type 2 item / part pairs");
        var odd = noPoint.Where(w => !(w.Hand == "LH" && w.Id / 100000 == 141) && w.Id != 13400000).Select(w => $"{w.Id} {w.Hand}").ToList();
        Assert.That(odd, Is.Empty, "weapons without a Point01 node besides the shields and the throwing knife");
        Assert.That(point.Where(kv => kv.Key.Hand == "RH").Sum(kv => kv.Value.With), Is.GreaterThan(800), "right hand weapons with a Point01 node");

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

    /// <summary>
    /// The item rows and models the sweep resolves types 2-4 against, read as EquipApplier reads them: itemdata/{id / 100000}.xml
    /// rows (GMS2: one item/... file per item), the tool itemPreset redirect, &lt;effect characterIdle&gt;, and the ItemModel's
    /// slot assets (itemmodel/{id / 100000}.xml, GMS2 itempreset/...) in the Item pack.
    /// </summary>
    sealed class ItemModels
    {
        const string ItemArchive = "Resource/Model/Item";
        readonly PackSet packs;
        readonly NifSpec spec;
        readonly Dictionary<int, int> preset = new();
        readonly Dictionary<int, Dictionary<int, XElement>> models = new();
        readonly Dictionary<string, ModelData> nifs = new(StringComparer.Ordinal);

        public ItemModels(PackSet packs, NifSpec spec) { this.packs = packs; this.spec = spec; }

        /// <summary>item id -> its characterIdle effect, as an Xml pack entry (effect/....xml); fills the preset redirects.</summary>
        public Dictionary<int, string> CharacterIdle()
        {
            var idle = new Dictionary<int, string>();
            foreach (var entry in packs.List("Xml").Where(e => (e.StartsWith("itemdata/", StringComparison.Ordinal) || e.StartsWith("item/", StringComparison.Ordinal)) && e.EndsWith(".xml", StringComparison.Ordinal)))
            {
                XDocument x;
                try { x = PackXml.Parse(packs.Read("Xml", entry)); } catch (System.Xml.XmlException) { continue; }
                bool perFile = entry.StartsWith("item/", StringComparison.Ordinal);
                var rows = perFile ? new[] { x.Root } : x.Root?.Elements("item") ?? Enumerable.Empty<XElement>();
                foreach (var e in rows)
                {
                    if (e == null) continue;
                    string idText = perFile ? Path.GetFileNameWithoutExtension(entry) : (string)e.Attribute("id");
                    if (!int.TryParse(idText, out int id)) continue;
                    if (int.TryParse((string)e.Descendants("tool").FirstOrDefault()?.Attribute("itemPreset"), out int p) && p != 0) preset[id] = p;
                    if ((string)e.Descendants("effect").FirstOrDefault()?.Attribute("characterIdle") is { Length: > 0 } fx)
                        idle[id] = "effect/" + fx.Trim().Replace('\\', '/').ToLowerInvariant();
                }
            }
            return idle;
        }

        XElement Model(int id)
        {
            int prefix = id / 100000;
            if (!models.TryGetValue(prefix, out var byId))
            {
                byId = new Dictionary<int, XElement>();
                string entry = $"itemmodel/{prefix}.xml";
                var x = packs.Exists("Xml", entry) ? PackXml.Parse(packs.Read("Xml", entry)) : PerFileTable.ItemModels(packs, prefix);
                foreach (var e in x?.Descendants("ItemModel") ?? Enumerable.Empty<XElement>())
                    if (int.TryParse((string)e.Attribute("id"), out int i)) byId[i] = e;
                models[prefix] = byId;
            }
            return byId.GetValueOrDefault(id);
        }

        /// <summary>A nif asset, or a kfm asset's nif (its name is not a .nif: the kfm of that name in the Item pack, whose nif
        /// sits beside it, as EquipApplier.AttachKfm reads it).</summary>
        ModelData Nif(string asset)
        {
            const string prefix = "Data/Resource/Model/Item/";
            string name = asset.Trim();
            string entry;
            if (name.EndsWith(".nif", StringComparison.OrdinalIgnoreCase))
                entry = (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? name[prefix.Length..] : name).ToLowerInvariant();
            else
            {
                string kfmName = name.Replace("urn:gamebryo-animation:", "").Replace("urn:", "");
                string kfmEntry = kfmName.Length > 0 ? packs.FindByFileName(ItemArchive, kfmName.ToLowerInvariant() + ".kfm") : null;
                if (kfmEntry == null) return null;
                try { entry = kfmEntry[..(kfmEntry.LastIndexOf('/') + 1)] + Path.GetFileName(KfmFile.Load(packs.Read(ItemArchive, kfmEntry)).Nif.Replace('\\', '/')).ToLowerInvariant(); }
                catch (Exception) { return null; }    // an unreadable kfm has no model
            }
            if (nifs.TryGetValue(entry, out var md)) return md;
            md = null;
            if (packs.Exists(ItemArchive, entry))
                try { md = ModelData.Load(NifFile.Load(spec, packs.Read(ItemArchive, entry))); } catch (Exception) { }    // an unreadable nif has no node
            return nifs[entry] = md;
        }

        /// <summary>The loaded nif models of every asset (all genders) of an item's model.</summary>
        public IEnumerable<(ModelData Model, bool Kfm, string Self)> Models(int id)
        {
            var im = Model(preset.GetValueOrDefault(id, id));
            foreach (var a in im?.Element("slots")?.Elements("slot").SelectMany(s => s.Elements("asset")) ?? Enumerable.Empty<XElement>())
            {
                string name = (string)a.Attribute("name") ?? "";
                if (Nif(name) is { } md) yield return (md, !name.Trim().EndsWith(".nif", StringComparison.OrdinalIgnoreCase), (string)a.Attribute("selfnode"));
            }
        }

        /// <summary>Every RH / LH nif asset of the item models 130-159 (the weapons), by item id and hand.</summary>
        public IEnumerable<(int Id, string Hand, ModelData Model)> Weapons()
        {
            for (int prefix = 130; prefix < 160; prefix++)
            {
                Model(prefix * 100000);
                foreach (var (id, im) in models[prefix].OrderBy(kv => kv.Key))
                    foreach (var slot in im.Element("slots")?.Elements("slot") ?? Enumerable.Empty<XElement>())
                    {
                        string hand = (string)slot.Attribute("name");
                        if (hand is not ("RH" or "LH")) continue;
                        foreach (var a in slot.Elements("asset"))
                            if (Nif((string)a.Attribute("name") ?? "") is { } md) yield return (id, hand, md);
                    }
            }
        }
    }
}
