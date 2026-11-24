using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The two game-file lists behind the revival dialog's paid inputs, every entry over the data of MS2_DATA (KMS2_DEV or KMS2)
/// and MS2_GMS2_DATA (GMS2), through the game's wiring (<see cref="RevivalWiring"/> with <see cref="RevivalPaidInputs"/>):
/// <list type="bullet">
/// <item>The buff levels that carry the revival fee reductions, reduce_merat_revival_fee (SAType 0x42) and
/// reduce_meso_revival_fee (0xa4). The client data holds them only there (no item option table lists them; a worn item's
/// options come in its packet). CEquipment::GetAbilityRate (equipment.cpp:731) is GetAbilityTotal / 1000, and the buffs' part
/// is AdditionalCliList::GetSpecialAbility (additionalcli_2.cpp:844). The dialog scales CalcRevivalMerat / CalcRevivalMeso by
/// 1 - min(cap, rate) (uirevivaldialog.cpp:687, :723), with the caps MeratRevivalFeeReduceLimit / MesoRevivalFeeReduceLimit
/// (Constants +0x8cc0 / +0x90f8, default 0.5).</item>
/// <item>The items tagged FreeReviveCoupon, the tag 4 Unk_Inventory::FindItemByTag (inventory.cpp:630) looks for in the coupon
/// box refresh (FUN_141abd640, uirevivaldialog.cpp:754-761). GetTagByName compares the name without case
/// (itemdatatable.cpp:578); PC::CanUseItem keeps the PC bang, VIP and wedding items out for a player without them.</item>
/// </list>
/// The expected value of each entry is read from its raw XML here, not through the port's loaders.
/// </summary>
[NonParallelizable]
public class RevivalDefaultsSweepTests
{
    static PackSet Open(string variable)
    {
        var d = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore($"{variable} is not set");
        return new PackSet(d);
    }

    static readonly (string Name, int Type)[] FeeAbilities =
    {
        ("reduce_merat_revival_fee", RevivalPaidInputs.MeratFeeAbility),
        ("reduce_meso_revival_fee", RevivalPaidInputs.MesoFeeAbility),
    };

    /// <summary>The raw fee values of one level element: KMS &lt;SpecialAbility&gt;&lt;v type value/&gt; (the value is the per-mille
    /// int), GMS2 StatusProperty/SpecialAbility "&lt;name&gt;rate" (a rate, times 1000; see <see cref="Gms2SpecialAbilities"/>).</summary>
    static Dictionary<int, int> RawFees(XElement level)
    {
        var fees = new Dictionary<int, int>();
        foreach (var (name, type) in FeeAbilities)
        {
            foreach (var v in level.Elements("SpecialAbility").Elements("v"))
                if (string.Equals((string)v.Attribute("type"), name, StringComparison.OrdinalIgnoreCase))
                    fees[type] = fees.GetValueOrDefault(type) + int.Parse((string)v.Attribute("value"), CultureInfo.InvariantCulture);
            foreach (var sa in level.Elements("StatusProperty").Elements("SpecialAbility"))
                if (sa.Attribute(name + "rate") is { } a)
                    fees[type] = fees.GetValueOrDefault(type) + (int)Math.Round(double.Parse(a.Value, CultureInfo.InvariantCulture) * 1000);
        }
        return fees;
    }

    static ushort LevelOf(XElement level, bool gms2) =>
        ushort.Parse(gms2 ? (string)level.Element("BasicProperty")?.Attribute("level") ?? "1" : (string)level.Attribute("level") ?? "1", CultureInfo.InvariantCulture);

    static long Scaled(long cost, float cap, int perMille) => Math.Max(0, (long)(cost * (double)(1f - Math.Min(cap, perMille / 1000.0f))));

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryRevivalFeeBuffScalesTheDialogsCostAsTheClient(string variable)
    {
        using var packs = Open(variable);
        var table = GameTables.NewAdditionalTable(packs);
        var names = packs.List("Xml").ToList();
        bool gms2 = !names.Any(n => n.StartsWith("additional/", StringComparison.OrdinalIgnoreCase));
        string folder = gms2 ? AdditionalTable.Gms2Folder : "additional/";

        // every (effect id, level) whose raw XML lists a fee: the values of each row that has it (a feature-gated id can have more)
        var raw = new Dictionary<(uint, ushort), List<Dictionary<int, int>>>();
        foreach (var n in names.Where(n => n.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && n.IndexOf('/', folder.Length) < 0))
        {
            var text = PackXml.Text(packs.Read("Xml", n));
            if (text.IndexOf("revival_fee", StringComparison.OrdinalIgnoreCase) < 0) continue;
            var root = XDocument.Parse(text.TrimStart('﻿')).Root!;
            var rows = gms2 ? new[] { (uint.Parse(Path.GetFileNameWithoutExtension(n), CultureInfo.InvariantCulture), root) }
                            : root.Elements("additional").Select(a => ((uint)a.Attribute("id"), a)).ToArray();
            foreach (var (id, row) in rows)
                foreach (var level in row.Elements("level"))
                    if (RawFees(level) is { Count: > 0 } fees)
                    {
                        var key = (id, LevelOf(level, gms2));
                        if (!raw.TryGetValue(key, out var list)) raw[key] = list = new();
                        list.Add(fees);
                    }
        }
        Assert.That(raw, Is.Not.Empty);

        var problems = new List<string>();
        int merat = 0, meso = 0, several = 0;
        foreach (var ((id, lv), candidates) in raw.OrderBy(k => k.Key))
        {
            if (candidates.Count > 1) several++;
            var data = table.GetLevel(id, lv);
            if (data == null) { problems.Add($"{id} level {lv}: not loaded"); continue; }
            var r = new RevivalDialogTests.PaidRig(new AdditionalBuffList((i, l) => i == id && l == lv ? data : null));
            r.Me.Meso = 1_000_000;
            r.Me.MesoRevivalsUsed = 3;
            r.Rule = 1;                                    // the meso revive at the flat 10000, with no daily count left
            r.Buff(20, id, 1, 1, lv);
            foreach (var (name, type) in FeeAbilities)
            {
                int got = r.Buffs.SpecialAbility(type);
                if (!candidates.Any(c => c.GetValueOrDefault(type) == got))
                    problems.Add($"{id} level {lv}: {name} {got}, the XML has {string.Join(" or ", candidates.Select(c => c.GetValueOrDefault(type)))}");
            }
            int meratFee = r.Buffs.SpecialAbility(RevivalPaidInputs.MeratFeeAbility), mesoFee = r.Buffs.SpecialAbility(RevivalPaidInputs.MesoFeeAbility);
            if (meratFee != 0) merat++;
            if (mesoFee != 0) meso++;
            r.C.Refresh();
            long wantMeso = Scaled(10000, 0.5f, mesoFee);
            if (r.C.MesoCost != wantMeso) problems.Add($"{id} level {lv}: meso cost {r.C.MesoCost}, the client's {wantMeso}");
            r.Rule = 0;
            r.C.Refresh();
            long wantMerat = Scaled(RevivalDialogController.CalcMerat(r.C.Env.Level(), 0), 0.5f, meratFee);
            if (r.C.PaidMode != 1 || r.C.MeratCost != wantMerat)
                problems.Add($"{id} level {lv}: mode {r.C.PaidMode} merat cost {r.C.MeratCost}, the client's 1 and {wantMerat}");
        }
        TestContext.Progress.WriteLine($"{variable} ({folder}): {raw.Count} buff levels with a revival fee reduction, {merat} merat, {meso} meso, " +
            $"{several} in more than one row, {problems.Count} differences");
        Assert.That(problems, Is.Empty, string.Join("\n", problems.Take(60)));
    }

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryReviveCouponFillsTheCouponBoxAsTheClient(string variable)
    {
        using var packs = Open(variable);
        var items = new ItemDataTable(packs);
        // every item element whose XML names the tag in any case: KMS itemdata/<n>.xml rows, GMS2 item/<a>/<bb>/<id>.xml files
        var raw = new Dictionary<int, XElement>();
        foreach (var n in packs.List("Xml").ToList())
        {
            bool perItem = n.StartsWith("item/", StringComparison.Ordinal);
            if (!perItem && !n.StartsWith("itemdata/", StringComparison.Ordinal)) continue;
            var text = PackXml.Text(packs.Read("Xml", n));
            if (text.IndexOf("FreeReviveCoupon", StringComparison.OrdinalIgnoreCase) < 0) continue;
            var root = XDocument.Parse(text.TrimStart('﻿')).Root!;
            if (perItem) raw[int.Parse(Path.GetFileNameWithoutExtension(n), CultureInfo.InvariantCulture)] = root;
            else
                foreach (var e in root.Elements("item"))
                    if (e.ToString().Contains("FreeReviveCoupon", StringComparison.OrdinalIgnoreCase)) raw[(int)e.Attribute("id")] = e;
        }
        Assert.That(raw, Is.Not.Empty);

        var problems = new List<string>();
        int found = 0, gated = 0;
        foreach (var (id, e) in raw.OrderBy(k => k.Key))
        {
            var tags = e.Descendants().Attributes("stringTag").Select(a => a.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            bool tagged = tags.Count == 1 && string.Equals(tags[0], "FreeReviveCoupon", StringComparison.OrdinalIgnoreCase);
            if (!tagged) { problems.Add($"item {id}: stringTag {string.Join("/", tags)}"); continue; }
            var data = items.Get(id);
            if (data == null) { problems.Add($"item {id}: not loaded"); continue; }
            bool needs = data.PCBang || data.Vip || data.Wedding;
            if (needs) gated++;
            foreach (bool privileged in new[] { false, true })
            {
                var pc = new PcItemUse.Pc { PcBangGrade = (byte)(privileged ? 1 : 0), PremiumTime = privileged ? long.MaxValue : 0, WeddingState = (short)(privileged ? 2 : 0), CharacterId = 7 };
                var bag = new[] { new EquipItem { Id = id, Amount = 1 } };
                var r = new RevivalDialogTests.PaidRig(tag => InventoryCount.HasItemWithTag(bag, items, tag, (i, d) => PcItemUse.CanUseItem(i, d, pc)));
                r.Me.Meso = 1_000_000;
                r.C.Refresh();
                bool want = privileged || !needs;
                if (r.C.CouponSelected != want) problems.Add($"item {id}{(privileged ? " (with the PC bang, VIP and wedding)" : "")}: coupon box {r.C.CouponSelected}, the client's {want}");
                else if (want && !privileged) found++;
            }
        }
        var other = new[] { new EquipItem { Id = raw.Keys.First(), Amount = 1 } };
        if (InventoryCount.HasItemWithTag(other, items, 3, (_, _) => true)) problems.Add("tag 3 finds a revive coupon");
        TestContext.Progress.WriteLine($"{variable}: {raw.Count} FreeReviveCoupon items ({string.Join(", ", raw.Keys.OrderBy(k => k))}), " +
            $"{found} found for any player, {gated} only with the PC bang, VIP or wedding, {problems.Count} differences");
        Assert.That(problems, Is.Empty, string.Join("\n", problems.Take(60)));
    }
}
