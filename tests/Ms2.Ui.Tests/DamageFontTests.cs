using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The gem damage fonts (GFxMovieDamage::LoadDamageFont / UnloadExpiredFonts, DamageTextCommon.loadDamageFont), the bGem
/// test of UIDamageText::InsertDamageInfo (uidamagetext.cpp:354), and the per-hit paths that must not allocate.
/// </summary>
public class DamageFontTests
{
    [Test]
    public void AGemFontTakesTheMonsterNormalAndCriticalArrays()
    {
        var nor = DamageLook.For("120", "Monster", "Normal", "", "7", myDamage: true, gemLoaded: true);
        Assert.That((nor.Glyphs, nor.Font), Is.EqualTo(("Nor", "7")));
        Assert.That(nor.Glyph(3), Is.EqualTo("Nor3_7"));
        Assert.That(nor.Glyph(DamageLook.MissKey), Is.EqualTo("Miss_7"));
        Assert.That(nor.Glyph(DamageLook.BlockKey), Is.EqualTo("Block_7"));
        Assert.That(nor.Glyph(DamageLook.PlusKey), Is.Null, "loadDamageFont sets no Plus");
        var cri = DamageLook.For("120", "Monster", "Critical", "", "7", myDamage: true, gemLoaded: true);
        Assert.That((cri.Glyphs, cri.CritText, cri.Glyph(0)), Is.EqualTo(("Cri", "dmg_mc_monstercri_7", "Cri0_7")));
        var user = DamageLook.For("120", "User", "Normal", "", "7", myDamage: true, gemLoaded: true);
        Assert.That((user.Glyphs, user.Font, user.Glyph(1)), Is.EqualTo(("User", (string)null, "User1")), "no UserNormal<id> array");
        var notLoaded = DamageLook.For("120", "Monster", "Normal", "", "7", myDamage: true);
        Assert.That((notLoaded.Glyphs, notLoaded.Glyph(5)), Is.EqualTo(("Monster", "Monster5")));
    }

    [Test]
    public void GemShowsOnlyForDamageNotAtMe()
    {
        Assert.That(new[] { 0, 1, 2, 3, 4 }.All(g => DamageTextNames.GemShows(g, false)), Is.True);
        Assert.That(new[] { 5, 6, 7 }.Any(g => DamageTextNames.GemShows(g, false)), Is.False, "the recover grades");
        Assert.That(DamageTextNames.GemShows(0, true), Is.False);
    }

    [Test]
    public void FontsLoadRestampAndExpireAfterSixtySeconds()
    {
        var f = new DamageFonts();
        Assert.That(f.Load(0, 10), Is.EqualTo(DamageFonts.LoadResult.Rejected));
        Assert.That(f.Load(7, 10), Is.EqualTo(DamageFonts.LoadResult.Loaded));
        Assert.That(f.Load(8, 10), Is.EqualTo(DamageFonts.LoadResult.Loaded));
        Assert.That(f.Load(7, 20), Is.EqualTo(DamageFonts.LoadResult.Restamped));
        Assert.That(f.Loaded(7) && f.Loaded(8) && !f.Loaded(0), Is.True);
        var gone = new List<int>();
        f.Expire(70, gone);
        Assert.That(gone, Is.Empty, "10 + 60 < 70 is false");
        f.Expire(71, gone);
        Assert.That(gone, Is.EqualTo(new[] { 8 }));
        Assert.That((f.Loaded(8), f.Count), Is.EqualTo((false, 1)));
        f.Load(9, 0);
        gone.Clear();
        f.Expire(1000, gone);
        Assert.That(gone, Is.EqualTo(new[] { 7 }), "a zero stamp never expires");
        Assert.That(f.Name(7), Is.SameAs(f.Name(7)));
        Assert.That(f.Name(7), Is.EqualTo("7"));
    }

    [Test]
    public void ValueCharsMatchValueTextWithNoAllocation()
    {
        Span<char> buf = stackalloc char[24];
        foreach (int g in new[] { 0, 1, 2, 3, 4, 5, 6, 7 })
            foreach (long v in new long[] { 0, 7, 120, 99999999, long.MaxValue })
                Assert.That(buf[..DamageTextNames.ValueChars(v, g, buf)].ToString(), Is.EqualTo(DamageTextNames.ValueText(v, g)), $"{v} grade {g}");
        long bytes = Alloc.Least(() =>
        {
            Span<char> b = stackalloc char[24];
            for (int i = 0; i < 1000; i++) DamageTextNames.ValueChars(i * 37, i % 8, b);
        });
        Assert.That(bytes, Is.EqualTo(0));
    }

    [Test]
    public void LooksAndGlyphNamesAreMadeOncePerKind()
    {
        var c = new DamageLookCache();
        var a = c.Get("", "Monster", "Critical", "7", true);
        Assert.That(c.Get("", "Monster", "Critical", "7", true), Is.SameAs(a));
        Assert.That(a.Font, Is.EqualTo("7"));
        Assert.That(c.Get("", "Monster", "Critical", null, true).Font, Is.Null);
        a.Glyph(1);
        long bytes = Alloc.Least(() =>
        {
            for (int i = 0; i < 1000; i++)
            {
                var l = c.Get("", "Monster", "Critical", "7", true);
                l.Glyph(i % 10);
            }
        });
        Assert.That(bytes, Is.EqualTo(0));
        Assert.That(c.Count, Is.EqualTo(2));
    }

    sealed class Info : DamageInfo { }

    [Test]
    public void TheListReusesItsNodesAndCounters()
    {
        var l = new DamageList<Info>();
        var owners = new[] { new object(), new object(), new object() };
        var infos = Enumerable.Range(0, 64).Select(i => new Info { Owner = owners[i % 3] }).ToArray();
        void Round()
        {
            foreach (var x in infos) l.Insert((x.Owner == owners[0]), x, 100);
            while (l.First is { } n)
            {
                l.Decrement(n.Value.Owner, 1);
                l.Remove(n);
            }
        }
        Round();
        Assert.That((l.Count, l.HasCounters), Is.EqualTo((0, false)));
        Assert.That(Alloc.Least(() => { for (int i = 0; i < 20; i++) Round(); }), Is.EqualTo(0));
    }
}
