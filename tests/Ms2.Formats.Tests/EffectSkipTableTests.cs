using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>EffectSkipMgr's view distances (LoadOption, IsOutOfView) and FUN_1411a5110's sound checks.</summary>
public class EffectSkipTableTests
{
    const string Xml = """
        <ms2>
          <option2>
            <sound>
              <note caster="PC" viewDistance="9"/>
              <element caster="myPC" viewDistance="0"/>
              <element caster="PC" viewDistance="3000"/>
              <other caster="monster" viewDistance="1500"/>
              <element caster="Boss" viewDistance="7"/>
              <element caster="background"/>
            </sound>
            <nif><element caster="PC" viewDistance="2500"/></nif>
          </option2>
          <option10><sound><element caster="PC" viewDistance="111"/></sound></option10>
        </ms2>
        """;

    [Test]
    public void TheOptionIsTheQualityPlusOneInBaseEightAndItsElementsFromTheFirstElementOn()
    {
        var t = EffectSkipTable.Parse(Xml, 1);
        Assert.That(t.ViewDistance(EffectSkipTable.RowSound, 3), Is.EqualTo(3000f));
        Assert.That(t.ViewDistance(EffectSkipTable.RowSound, 5), Is.EqualTo(1500f), "any sibling element after the first element");
        Assert.That(t.ViewDistance(EffectSkipTable.RowSound, 6), Is.EqualTo(0f), "an unknown caster name");
        Assert.That(t.ViewDistance(EffectSkipTable.RowSound, 7), Is.EqualTo(0f), "no viewDistance");
        Assert.That(t.ViewDistance(EffectSkipTable.RowNif, 3), Is.EqualTo(2500f));
        Assert.That(EffectSkipTable.Parse(Xml, 7).ViewDistance(EffectSkipTable.RowSound, 3), Is.EqualTo(111f), "_itoa(8, 8) is \"10\"");
        Assert.That(EffectSkipTable.Parse(Xml, 2).ViewDistance(EffectSkipTable.RowSound, 3), Is.EqualTo(0f), "no option3");
    }

    [Test]
    public void ThePartIsOutOfViewPastANonZeroDistanceOfItsCaster()
    {
        var t = EffectSkipTable.Parse(Xml, 1);
        var eye = new Vector3(100, 200, 0);
        Assert.That(t.IsOutOfView(2, 3, eye + new Vector3(0, 2999, 0), eye), Is.False);
        Assert.That(t.IsOutOfView(2, 3, eye + new Vector3(0, 3000, 0), eye), Is.True, "not nearer than the distance");
        Assert.That(t.IsOutOfView(2, 1, eye + new Vector3(0, 99999, 0), eye), Is.False, "myPC's 0 never skips");
        Assert.That(t.IsOutOfView(2, 0, eye + new Vector3(0, 99999, 0), eye), Is.False, "caster 0");
        Assert.That(t.IsOutOfView(2, 3, eye + new Vector3(0, 99999, 0), null), Is.False, "no camera actor");
        Assert.That(t.IsOutOfView(2, 3, eye + new Vector3(0, 50, 0), eye, 40), Is.True, "the check's own distance");
    }

    [Test]
    public void ASoundEventIsHiddenByThePetOptionsOrOutOfViewAndScaledForOtherPlayers()
    {
        var t = EffectSkipTable.Parse(Xml, 1);
        var eye = Vector3.Zero;
        var near = new Vector3(10, 0, 0);
        Assert.That(EffectSkipTable.SoundEventVolume(t, 1, near, eye, 0.5f, 0.9f, false, false), Is.EqualTo(0.5f));
        Assert.That(EffectSkipTable.SoundEventVolume(t, 3, near, eye, 0.5f, 0.9f, false, false), Is.EqualTo(0.45f).Within(1e-6f));
        Assert.That(EffectSkipTable.SoundEventVolume(t, 3, new Vector3(5000, 0, 0), eye, 1, 0.9f, false, false), Is.EqualTo(-1f));
        Assert.That(EffectSkipTable.SoundEventVolume(t, 4, near, eye, 1, 1, true, false), Is.EqualTo(-1f), "DisplayShowOnlyMyPet hides 4");
        Assert.That(EffectSkipTable.SoundEventVolume(t, 2, near, eye, 1, 1, false, true), Is.EqualTo(-1f), "DisplayHideMyPet hides 2");
        Assert.That(EffectSkipTable.SoundEventVolume(t, 2, near, eye, 1, 1, true, false), Is.EqualTo(1f));
        Assert.That(EffectSkipTable.SoundEventVolume(null, 3, new Vector3(5000, 0, 0), eye, 1, 1, false, false), Is.EqualTo(1f));
    }

    /// <summary>VolumeOtherPc's Define (constantsblocks.cpp:794) is 0.9, the other players' scale without a constants value.</summary>
    [Test]
    public void TheOtherPlayersVolumeDefaultsToTheClientsDefine()
    {
        Assert.That(EffectSkipTable.VolumeOtherPcDefine, Is.EqualTo(0.9f));
        var t = EffectSkipTable.Parse(Xml, 1);
        Assert.That(EffectSkipTable.SoundEventVolume(t, 3, new Vector3(10, 0, 0), Vector3.Zero, 1, EffectSkipTable.VolumeOtherPcDefine,
            false, false), Is.EqualTo(0.9f));
    }

    /// <summary>The shipped table never skips an own sound: myPC's sound distance is 0 for every quality.</summary>
    [Test]
    public void TheShippedTableNeverSkipsAnOwnSound()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA not set");
        string xml = System.Text.Encoding.UTF8.GetString(new PackSet(data).Read("Xml", "table/effectoption.xml"));
        for (int q = 0; q <= 3; q++)
        {
            var t = EffectSkipTable.Parse(xml, q);
            Assert.That(t.ViewDistance(EffectSkipTable.RowSound, 1), Is.EqualTo(0f), $"quality {q}");
            Assert.That(t.ViewDistance(EffectSkipTable.RowSound, 3), Is.GreaterThan(0f), $"quality {q}: the option is read");
        }
    }

    [Test]
    public void EffectTypesParseAsTheClientsKindsCaseBlind()
    {
        Assert.That(EffectSkipTable.ParseEffectType("nif"), Is.EqualTo(1));
        Assert.That(EffectSkipTable.ParseEffectType("SOUNDEVENT"), Is.EqualTo(2));
        Assert.That(EffectSkipTable.ParseEffectType("swoosh"), Is.EqualTo(EffectSkipTable.RowSwoosh));
        Assert.That(EffectSkipTable.ParseEffectType("random"), Is.EqualTo(EffectSkipTable.RowRandom));
        Assert.That(EffectSkipTable.ParseEffectType("chaosLight"), Is.EqualTo(EffectSkipTable.RowChaosLight));
        Assert.That(EffectSkipTable.ParseEffectType("lightning"), Is.EqualTo(EffectSkipTable.RowLightning));
        Assert.That(EffectSkipTable.ParseEffectType("afterImage"), Is.EqualTo(22));
        Assert.That(EffectSkipTable.ParseEffectType("sound"), Is.EqualTo(0));
        Assert.That(EffectSkipTable.ParseEffectType(null), Is.EqualTo(0));
    }

    static EffectPart Part(string type, bool top = true, bool offSkip = false, float view = -1, float hear = 0) => new()
    {
        Type = type, TypeId = EffectSkipTable.ParseEffectType(type), TopLevel = top, OffSkip = offSkip, ViewDistance = view, ForceHearDistance = hear,
    };

    /// <summary>ShouldSkipByView (effectskipmgr.cpp:157-176): any top level part without offSkip out of view on its row skips
    /// the effect; a nif part with its own viewDistance; a random part's children are not params.</summary>
    [Test]
    public void AnEffectIsSkippedByViewWhenOneOfItsPartsIsOutOfView()
    {
        var t = EffectSkipTable.Parse(Xml, 1);   // option2: sound PC 3000, monster 1500; nif PC 2500
        var eye = Vector3.Zero;
        var far = new Vector3(2600, 0, 0);
        Assert.That(t.ShouldSkipByView(new[] { Part("nif") }, 3, far, eye), Is.True);
        Assert.That(t.ShouldSkipByView(new[] { Part("nif") }, 3, new Vector3(2400, 0, 0), eye), Is.False);
        Assert.That(t.ShouldSkipByView(new[] { Part("nif") }, 1, far * 10, eye), Is.False, "myPC has no nif distance");
        Assert.That(t.ShouldSkipByView(new[] { Part("nif") }, 0, far, eye), Is.False, "kind 0");
        Assert.That(t.ShouldSkipByView(new[] { Part("nif") }, 3, far, null), Is.False, "no camera actor");
        Assert.That(t.ShouldSkipByView(new[] { Part("nif", offSkip: true) }, 3, far, eye), Is.False, "offSkip");
        Assert.That(t.ShouldSkipByView(new[] { Part("nif", view: 0) }, 3, far, eye), Is.False, "the part's viewDistance 0");
        Assert.That(t.ShouldSkipByView(new[] { Part("nif", view: 100) }, 3, new Vector3(200, 0, 0), eye), Is.True, "the part's own distance");
        Assert.That(t.ShouldSkipByView(new[] { Part("nif", top: false) }, 3, far, eye), Is.False, "a random part's child");
        Assert.That(t.ShouldSkipByView(new[] { Part("light") }, 3, far * 10, eye), Is.False, "no light row");
        Assert.That(t.ShouldSkipByView(new[] { Part("light"), Part("soundEvent") }, 5, new Vector3(1600, 0, 0), eye), Is.True,
            "one part decides; the sound row of a monster");
        Assert.That(t.ShouldSkipByView(new[] { Part("soundEvent", view: 99999) }, 5, new Vector3(1600, 0, 0), eye), Is.True,
            "only a nif part has its own distance");
        Assert.That(t.ShouldSkipByView(null, 3, far, eye), Is.False);
    }

    /// <summary>The part tests as a part is made: CNifEffect::vfunc_3 (nifeffectparam.cpp:757-766) for a nif part, CreateEffect
    /// (effectservice.cpp:291-330) for a soundEvent part, forceHearDistance only for a monster or boss caster.</summary>
    [Test]
    public void APartOutOfViewIsNotMade()
    {
        var t = EffectSkipTable.Parse(Xml, 1);
        var eye = Vector3.Zero;
        Assert.That(t.PartOutOfView(Part("nif"), 3, new Vector3(2500, 0, 0), eye), Is.True);
        Assert.That(t.PartOutOfView(Part("nif"), 3, new Vector3(2500, 0, 0), eye, noCull: true), Is.False);
        Assert.That(t.PartOutOfView(Part("nif", offSkip: true), 3, new Vector3(2500, 0, 0), eye), Is.False);
        Assert.That(t.PartOutOfView(Part("soundEvent"), 5, new Vector3(1600, 0, 0), eye), Is.True);
        Assert.That(t.PartOutOfView(Part("soundEvent", hear: 2000), 5, new Vector3(1600, 0, 0), eye), Is.False, "forceHearDistance, a monster");
        Assert.That(t.PartOutOfView(Part("soundEvent", hear: 10), 3, new Vector3(1600, 0, 0), eye), Is.False, "not for a PC: the table's 3000");
        Assert.That(t.PartOutOfView(Part("light"), 3, new Vector3(99999, 0, 0), eye), Is.False);
        Assert.That(t.PartOutOfView(Part("swoosh"), 3, new Vector3(99999, 0, 0), eye), Is.False, "not tested as it is made");
    }

    /// <summary>
    /// CNifEffect::vfunc_3's view test (nifeffectparam.cpp:757-766) as every effect caller meets it: row 1 at the part's own
    /// viewDistance, the row's distance for the caster when it has none (-1, CNifEffectParam's default, :356), never with
    /// offSkip, never for kind 0 and never for a target with the no-cull flag (m_b30).
    /// </summary>
    [Test]
    public void ANifPartIsTestedAtItsOwnDistanceElseItsCastersRow()
    {
        var t = EffectSkipTable.Parse(Xml, 1);
        var eye = Vector3.Zero;
        Assert.That(t.PartOutOfView(Part("nif"), 3, new Vector3(2499, 0, 0), eye), Is.False, "the PC row's 2500");
        Assert.That(t.PartOutOfView(Part("nif"), 3, new Vector3(2500, 0, 0), eye), Is.True);
        Assert.That(t.PartOutOfView(Part("nif", view: 500), 3, new Vector3(500, 0, 0), eye), Is.True, "its own distance");
        Assert.That(t.PartOutOfView(Part("nif", view: 500), 1, new Vector3(500, 0, 0), eye), Is.True, "its own distance for the own PC too");
        Assert.That(t.PartOutOfView(Part("nif"), 1, new Vector3(99999, 0, 0), eye), Is.False, "no row distance for the own PC");
        Assert.That(t.PartOutOfView(Part("nif", view: 0), 3, new Vector3(99999, 0, 0), eye), Is.False, "its own 0 never skips");
        Assert.That(t.PartOutOfView(Part("nif", view: 500), 0, new Vector3(99999, 0, 0), eye), Is.False, "kind 0");
        Assert.That(t.PartOutOfView(Part("nif", offSkip: true), 3, new Vector3(99999, 0, 0), eye), Is.False, "offSkip");
        Assert.That(t.PartOutOfView(Part("nif"), 3, new Vector3(99999, 0, 0), eye, noCull: true), Is.False, "the target's no-cull flag");
        Assert.That(t.PartOutOfView(Part("nif", top: false), 3, new Vector3(2500, 0, 0), eye), Is.True, "a random part's child is made by CreateEffect too");
        Assert.That(t.PartOutOfView(Part("nif"), 3, new Vector3(99999, 0, 0), null), Is.False, "no camera actor");
    }

    /// <summary>
    /// Every nif part of every effect xml (Xml.m2d effect/*.xml, a random part's children too), every caster kind, every
    /// graphic option and distances around each view distance: the client's rule written out from the raw XML
    /// (CNifEffectParam::vfunc_1's viewDistance, nifeffectparam.cpp:376; CEffectParam::vfunc_1's offSkip, effectservice.cpp:660;
    /// IsOutOfView, effectskipmgr.cpp:132-151) against <see cref="EffectSkipTable.PartOutOfView"/>.
    /// </summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryNifPartsViewTestIsCNifEffects(string env)
    {
        var data = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(data) || !Directory.Exists(data)) Assert.Ignore(env + " is not set");
        using var packs = new PackSet(data);
        var table = new EffectTable(packs);
        string optionXml = System.Text.Encoding.UTF8.GetString(packs.Read("Xml", "table/effectoption.xml"));
        var options = XDocument.Parse(optionXml).Root.Elements().Where(e => e.Name.LocalName.StartsWith("option")).ToList();
        Assert.That(options, Is.Not.Empty);
        var casterNames = new Dictionary<string, int> { ["myPC"] = 1, ["PC"] = 3, ["monster"] = 5, ["bossMonster"] = 6, ["background"] = 7 };
        int[] kinds = { 1, 2, 3, 5, 6, 7 };
        var entries = packs.List("Xml").Where(e => e.StartsWith("effect/", StringComparison.Ordinal) && e.EndsWith(".xml", StringComparison.Ordinal))
            .OrderBy(e => e, StringComparer.Ordinal).ToList();
        long checks = 0, outOfView = 0;
        int nifParts = 0, ownDistance = 0, offSkip = 0, children = 0;
        var wrong = new List<string>();
        bool first = true;
        foreach (var opt in options)
        {
            int quality = Convert.ToInt32(opt.Name.LocalName["option".Length..], 8) - 1;
            var skip = EffectSkipTable.Parse(optionXml, quality);
            var row = new Dictionary<int, float>();
            foreach (var el in opt.Element("nif")?.Elements("element") ?? Enumerable.Empty<XElement>())
                if (casterNames.TryGetValue((string)el.Attribute("caster") ?? "", out int c))
                    row[c] = float.TryParse((string)el.Attribute("viewDistance"), NumberStyles.Float, CultureInfo.InvariantCulture, out float vd) ? vd : 0;
            foreach (var entry in entries)
            {
                var parts = table.Get(entry["effect/".Length..]);
                if (parts == null) continue;
                var raw = Ms2.Formats.Text.PackXml.Parse(packs.Read("Xml", entry)).Descendants("effect").ToList();
                if (raw.Count != parts.Count) { if (wrong.Count < 20) wrong.Add($"{entry}: {raw.Count} effect elements, {parts.Count} parts"); continue; }
                for (int i = 0; i < raw.Count; i++)
                {
                    var e = raw[i];
                    if (!string.Equals((string)e.Attribute("type"), "nif", StringComparison.OrdinalIgnoreCase)) continue;
                    float own = float.TryParse((string)e.Attribute("viewDistance"), NumberStyles.Float, CultureInfo.InvariantCulture, out float o) ? o : -1;
                    string off = (string)e.Attribute("offSkip");
                    bool rawOff = !string.IsNullOrEmpty(off) && (off.StartsWith("true", StringComparison.OrdinalIgnoreCase) ||
                        off.StartsWith("yes", StringComparison.OrdinalIgnoreCase) || off[0] == '1');
                    if (first)
                    {
                        nifParts++;
                        if (own != -1) ownDistance++;
                        if (rawOff) offSkip++;
                        if (e.Parent != e.Document.Root) children++;
                    }
                    foreach (int kind in kinds)
                    {
                        float view = MathF.Abs(own + 1) <= 1e-4f ? row.GetValueOrDefault(kind) : own;
                        foreach (float d in new[] { 0f, view - 1, view, view + 1, 1499, 1501, 1999, 2001, 2999, 3001, 9000 })
                        {
                            if (d < 0) continue;
                            bool client = !rawOff && !(MathF.Abs(view) <= 1e-4f || d < view);
                            bool ours = skip.PartOutOfView(parts[i], kind, new Vector3(0, d, 0), Vector3.Zero);
                            checks++;
                            if (client) outOfView++;
                            if (client != ours && wrong.Count < 20) wrong.Add($"{opt.Name.LocalName} {entry} part {i} kind {kind} at {d}: client {client} ours {ours}");
                        }
                        if (skip.PartOutOfView(parts[i], kind, new Vector3(0, 9000, 0), null) && wrong.Count < 20)
                            wrong.Add($"{entry} part {i} kind {kind}: out of view without a viewer");
                        if (skip.PartOutOfView(parts[i], kind, new Vector3(0, 9000, 0), Vector3.Zero, noCull: true) && wrong.Count < 20)
                            wrong.Add($"{entry} part {i} kind {kind}: out of view with the no-cull flag");
                    }
                    if (skip.PartOutOfView(parts[i], 0, new Vector3(0, 9000, 0), Vector3.Zero) && wrong.Count < 20)
                        wrong.Add($"{entry} part {i}: kind 0 out of view");
                }
            }
            first = false;
        }
        TestContext.Out.WriteLine($"{env}: {entries.Count} effect xmls, {nifParts} nif parts ({children} in a random part, {ownDistance} with their own " +
            $"viewDistance, {offSkip} offSkip), {options.Count} graphic options, {checks} view tests, {outOfView} out of view");
        Assert.That(nifParts, Is.GreaterThan(1000));
        Assert.That(wrong, Is.Empty, string.Join("\n", wrong));
    }
}
