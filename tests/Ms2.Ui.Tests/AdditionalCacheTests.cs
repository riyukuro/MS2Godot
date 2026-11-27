using System.Diagnostics;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The per-install additional effect cache gives the same effects and descriptions as the XML.</summary>
[NonParallelizable]   // sets process-wide hooks the product reads
public class AdditionalCacheTests
{
    static readonly string[] Tags = { "name", "uiDescription", "mainDescription", "functionDescription", "tooltipDescription", "guideDescription" };

    [Test]
    public void CacheMatchesTheXml()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var dir = Path.Combine(Path.GetTempPath(), "ms2-additional-cache-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, AdditionalCache.FileName("test"));
        var keep = GameTables.AdditionalCacheOf;
        try
        {
            using var packs = new PackSet(d);
            var sw = Stopwatch.StartNew();
            AdditionalCache.Build(packs, path);
            TestContext.Progress.WriteLine($"additional cache: built in {sw.Elapsed.TotalSeconds:F2} s, {new FileInfo(path).Length / 1024} KB");

            var xml = new GameTables(packs, "en");
            GameTables.AdditionalCacheOf = _ => path;
            var cached = new GameTables(packs, "en");

            sw.Restart();
            var first = cached.Additional.GetLevel(10500111, 1);
            var firstName = cached.AdditionalDescription(10500111, 1, "name");
            TestContext.Progress.WriteLine($"additional cache: cold first buff {sw.Elapsed.TotalMilliseconds:F2} ms");
            Assert.That(first?.Icon, Does.EndWith("10500111_buff.png"));
            // the name is the data's language (an English or a Korean client), so it is compared with the XML read
            Assert.That(firstName, Is.Not.Empty.And.EqualTo(xml.AdditionalDescription(10500111, 1, "name")));

            // the ids as the table reads them: the KMS additional/ rows, or (the GMS2 data has no additional/) the file names
            // of additionaleffect/<id>.xml
            var ids = new SortedSet<uint>();
            foreach (var n in packs.List("Xml").Where(n => n.StartsWith("additional/", StringComparison.OrdinalIgnoreCase) && n.IndexOf('/', 11) < 0))
                foreach (var e in GameStrings.Parse(packs.Read("Xml", n)).Root?.Elements("additional") ?? Enumerable.Empty<XElement>())
                    if (uint.TryParse((string)e.Attribute("id"), out var id)) ids.Add(id);
            if (ids.Count == 0)
                foreach (var n in packs.List("Xml").Where(n => n.StartsWith(AdditionalTable.Gms2Folder, StringComparison.OrdinalIgnoreCase)
                    && n.IndexOf('/', AdditionalTable.Gms2Folder.Length) < 0 && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
                    if (uint.TryParse(Path.GetFileNameWithoutExtension(n), out var id)) ids.Add(id);
            Assert.That(ids.Count, Is.GreaterThan(1000));
            int levels = 0, balloons = 0, specials = 0, armors = 0, stuns = 0, overlaps = 0, keeps = 0, overlapKeeps = 0, dotSounds = 0;
            foreach (var id in ids.Append(0u).Append(99999999u))
            {
                var a = xml.Additional.GetAdditional(id);
                var b = cached.Additional.GetAdditional(id);
                Assert.That(b == null, Is.EqualTo(a == null), $"effect {id}");
                if (a == null) continue;
                Assert.That((b.Id, b.MainType, b.IsDebuff), Is.EqualTo((a.Id, a.MainType, a.IsDebuff)), $"effect {id}");
                for (ushort lv = 1; lv <= 30; lv++)
                {
                    var la = a.GetLevel(lv);
                    var lb = b.GetLevel(lv);
                    Assert.That(lb == null, Is.EqualTo(la == null), $"effect {id} level {lv}");
                    if (la == null) break;
                    levels++;
                    Assert.That((lb.Level, lb.StringParam, lb.DurationTick, lb.KeepCondition, lb.IconOnOff, lb.Icon, lb.Priority, lb.IsHideCooltime, lb.EventBuffType, lb.PvpOnOff, lb.DisableRevivalHere, lb.ForceApplyTarget, lb.Hide, lb.Invincible, lb.Parent == b),
                        Is.EqualTo((la.Level, la.StringParam, la.DurationTick, la.KeepCondition, la.IconOnOff, la.Icon, la.Priority, la.IsHideCooltime, la.EventBuffType, la.PvpOnOff, la.DisableRevivalHere, la.ForceApplyTarget, la.Hide, la.Invincible, true)), $"effect {id} level {lv}");
                    Assert.That((lb.SuperArmor, lb.SuperArmorApplyFieldType), Is.EqualTo((la.SuperArmor, la.SuperArmorApplyFieldType)), $"effect {id} level {lv} super armor");
                    if (la.SuperArmor != 0) armors++;
                    Assert.That(lb.IconBalloonOnOff, Is.EqualTo(la.IconBalloonOnOff), $"effect {id} level {lv} icon balloon");
                    if (la.IconBalloonOnOff) balloons++;
                    Assert.That((lb.MaxBuffCount, lb.Stun, lb.StunAnimation), Is.EqualTo((la.MaxBuffCount, la.Stun, la.StunAnimation)), $"effect {id} level {lv} overlap and stun");
                    if (la.Stun != 0) stuns++;
                    if (la.MaxBuffCount != 1) overlaps++;
                    Assert.That((lb.SpecialEffectType, lb.SpecialEffectProbability, lb.SpecialEffectValues), Is.EqualTo((la.SpecialEffectType, la.SpecialEffectProbability, la.SpecialEffectValues)), $"effect {id} level {lv} special effect");
                    Assert.That((lb.InvokeSkillId, lb.InvokeSkillGroupId, lb.InvokeEffectId, lb.InvokeEffectGroupId, lb.InvokeTypes, lb.InvokeValues, lb.InvokeRates),
                        Is.EqualTo((la.InvokeSkillId, la.InvokeSkillGroupId, la.InvokeEffectId, la.InvokeEffectGroupId, la.InvokeTypes, la.InvokeValues, la.InvokeRates)), $"effect {id} level {lv} invoke effect");
                    Assert.That(lb.SpecialAbilities, Is.EqualTo(la.SpecialAbilities), $"effect {id} level {lv} special abilities");
                    if (la.SpecialAbilities != null) specials++;
                    Assert.That((lb.StartEffect, lb.KeepEffect, lb.StopEffect, lb.InvokeEffect, lb.IgnoreOptimalHideStartEffect, lb.InvokeEvent),
                        Is.EqualTo((la.StartEffect, la.KeepEffect, la.StopEffect, la.InvokeEffect, la.IgnoreOptimalHideStartEffect, la.InvokeEvent)), $"effect {id} level {lv} motion effects");
                    Assert.That(lb.DotSoundMaterial, Is.EqualTo(la.DotSoundMaterial), $"effect {id} level {lv} dot sound material");
                    if (la.DotSoundMaterial != 0) dotSounds++;
                    Assert.That(lb.OverlapKeep == null, Is.EqualTo(la.OverlapKeep == null), $"effect {id} level {lv} overlap keep");
                    if (la.OverlapKeep != null)
                    {
                        Assert.That((lb.OverlapKeep.Start, lb.OverlapKeep.Keep, lb.OverlapKeep.End), Is.EqualTo((la.OverlapKeep.Start, la.OverlapKeep.Keep, la.OverlapKeep.End)), $"effect {id} level {lv} overlap keep rows");
                        overlapKeeps++;
                    }
                    if (la.KeepEffect.Length > 0) keeps++;
                    foreach (var t in Tags)
                        Assert.That(cached.AdditionalDescription(id, lv, t), Is.EqualTo(xml.AdditionalDescription(id, lv, t)), $"effect {id} level {lv} {t}");
                }
            }
            TestContext.Progress.WriteLine($"additional cache: {ids.Count} ids, {levels} levels compared");
            Assert.That(specials, Is.GreaterThan(1000), "levels with special abilities");
            Assert.That(armors, Is.GreaterThan(0), "levels with super armor");
            Assert.That(balloons, Is.GreaterThan(50), "levels with an icon balloon");
            TestContext.Progress.WriteLine($"additional cache: {stuns} stun levels, {overlaps} levels with a max buff count other than 1");
            Assert.That(stuns, Is.GreaterThan(300), "levels with a stun");
            Assert.That(overlaps, Is.GreaterThan(0), "levels with a max buff count");
            TestContext.Progress.WriteLine($"additional cache: {keeps} levels with a keep effect, {overlapKeeps} with overlap keep effects");
            Assert.That(keeps, Is.GreaterThan(1000), "levels with a keep effect");
            TestContext.Progress.WriteLine($"additional cache: {dotSounds} levels with a dot sound material");
            Assert.That(dotSounds, Is.GreaterThan(0), "levels with a dot sound material");
        }
        finally
        {
            GameTables.AdditionalCacheOf = keep;
            if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(dir)) Directory.Delete(dir);
        }
    }

    [Test]
    public void BadCacheFallsBackToTheXml()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var path = Path.Combine(Path.GetTempPath(), "ms2-additional-bad-" + Guid.NewGuid().ToString("N") + ".bin");
        var keep = GameTables.AdditionalCacheOf;
        try
        {
            File.WriteAllBytes(path, new byte[] { 0x4d, 0x32, 0x41, 0x44, 1, 0, 0, 0, 5 });     // the header, then cut short
            using var packs = new PackSet(d);
            var xml = new GameTables(packs, "en");
            GameTables.AdditionalCacheOf = _ => path;
            var t = new GameTables(packs, "en");
            Assert.That(t.Additional.GetLevel(10500111, 1)?.Icon, Does.EndWith("10500111_buff.png"));
            Assert.That(t.AdditionalDescription(10500111, 1, "name"), Is.Not.Empty.And.EqualTo(xml.AdditionalDescription(10500111, 1, "name")));
        }
        finally
        {
            GameTables.AdditionalCacheOf = keep;
            File.Delete(path);
        }
    }
}
