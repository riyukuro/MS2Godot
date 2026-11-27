using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// Every gem damage font movie of the data (uidamagefont_&lt;id&gt;, DamageTextCommon.loadDamageFont) against what the port
/// builds from it, with the class names <see cref="DamageLook"/> makes: the MonsterNormal&lt;id&gt; and MonsterCritical&lt;id&gt;
/// digits (Nor0_&lt;id&gt; .. Nor9_&lt;id&gt;, Cri0_&lt;id&gt; .. Cri9_&lt;id&gt;) are in the font movie, and a critical's text
/// resolves: the font's dmg_mc_monstercri_&lt;id&gt; when the movie has it, else getCriticalTxt's fallback, the damage text
/// movie's dmg_mc_monstercri (from its commondamagefont import). Miss_&lt;id&gt; and Block_&lt;id&gt; are in no font movie: getnerateMovieClipFromString gives null,
/// so the client's gem Miss and Block numbers are empty (createDamageMovie traces and adds nothing), as the port's.
/// </summary>
public class DamageFontMovieTests
{
    static HashSet<string> Classes(Ms2.Formats.Pack.PackSet packs, string movie) =>
        Ms2.Swf.SwfMovie.Parse(packs.Read("Resource/Gfx", packs.FindByFileName("Resource/Gfx", movie + ".gfx")))
            .SymbolClasses.Values.ToHashSet(StringComparer.Ordinal);

    [Test]
    public void EveryFontMovieHoldsItsDigitsAndItsCriticalTextResolves()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var packs = TestData.Packs(d);
        var ids = packs.List("Resource/Gfx").Select(n => Path.GetFileNameWithoutExtension(n).ToLowerInvariant())
            .Where(k => k.StartsWith("uidamagefont_", StringComparison.Ordinal) && int.TryParse(k["uidamagefont_".Length..], out _))
            .Select(k => k["uidamagefont_".Length..]).Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.That(ids, Is.Not.Empty);
        // the damage text movie and the font library it imports (commondamagefont, where dmg_mc_monstercri is)
        var own = Classes(packs, "uidamagetext");
        own.UnionWith(Classes(packs, "commondamagefont"));
        var wrong = new List<string>();
        int fontCrit = 0, missBlock = 0;
        foreach (var id in ids)
        {
            var classes = Classes(packs, "uidamagefont_" + id);
            var nor = DamageLook.For("1", "Monster", "Normal", "", id, myDamage: true, gemLoaded: true);
            var cri = DamageLook.For("1", "Monster", "Critical", "", id, myDamage: true, gemLoaded: true);
            for (int k = 0; k < 10; k++)
                foreach (var c in new[] { nor.Glyph(k), cri.Glyph(k) })
                    if (c == null || !classes.Contains(c)) wrong.Add($"uidamagefont_{id}: {c ?? "(null)"}");
            if (cri.FontCritText != null && classes.Contains(cri.FontCritText)) fontCrit++;
            else if (cri.CritText == null || !own.Contains(cri.CritText)) wrong.Add($"uidamagefont_{id}: critical text {cri.CritText ?? "(null)"}");
            if (classes.Contains(nor.Glyph(DamageLook.MissKey)) || classes.Contains(nor.Glyph(DamageLook.BlockKey))) missBlock++;
        }
        TestContext.Out.WriteLine($"{ids.Count} font movies, {fontCrit} with their own critical text, {missBlock} with Miss / Block");
        Assert.That(wrong, Is.Empty);
    }
}
