using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using Ms2.Audio;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/drop_data.tscn) over the data of MS2_DATA: every drop the field can make resolves the way
/// <see cref="DropLayer"/> loads it.
/// <list type="bullet">
/// <item>The two drop styles (1 an item, 2 a pet item) exist.</item>
/// <item>Every grade nif and pet grade nif is a model that loads, and the item one has the "Item_Icon" mesh the icon goes on
/// (CDropItem::SetIconTexture).</item>
/// <item>Every dropItemAsset row (the meso piles by amount, the special items) has a style whose model loads.</item>
/// <item>Every grade effect (the rarity glow) and every style's beEffect is an effect xml.</item>
/// <item>Every style's dropSoundID and getSoundID is an event of "ms2sound/sound/item" with a wave.</item>
/// <item>Every item with data gets a drop look and, unless a meso, an icon in the Image pack.</item>
/// <item>The bag-full warning sound and the notices 0x43 and 0x45 resolve.</item>
/// </list>
/// Prints the counts; quits with 0 when it holds, else 1.
/// </summary>
public partial class DropDataCheck : Node
{
    static readonly string[] ModelArchives = { "Resource/Model/Item", "Resource/Model/Effect", "Resource/Model/Map" };

    public override void _Ready()
    {
        var fails = new List<string>();
        try { Run(fails); } catch (Exception e) { fails.Add(e.ToString()); }
        GD.Print($"drop data: {(fails.Count == 0 ? "ok" : $"FAIL ({fails.Count})\n  " + string.Join("\n  ", fails.Take(80)))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    void Run(List<string> fails)
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var table = new ItemDropTable(packs);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var effects = new EffectTable(packs);
        var soundDir = System.IO.Path.Combine(Settings.DataDir, "Sound");
        var sounds = System.IO.File.Exists(System.IO.Path.Combine(soundDir, "MS2Sound.fev")) ? new SoundLibrary(soundDir, GameOptions.Current != null ? GameOptions.Voice : 0) : null;   // the voice of the options (the region's on a fresh file)
        if (sounds == null) fails.Add("no Sound/MS2Sound.fev");

        var root = GameStrings.Parse(packs.Read("Xml", "table/itemdata.xml")).Root;
        var styleIds = table.Styles.Select(st => st.TypeId).OrderBy(i => i).ToList();
        bool gms2Layout = root.Element("dropStyle") == null && root.Element("dropitem") != null;
        foreach (int s in new[] { ItemDropTable.ItemStyle, ItemDropTable.PetStyle })
            if (table.Style(s) == null) fails.Add($"drop style {s} is missing");

        // the models: grade nifs, pet grade nifs, the asset rows
        var loaded = new Dictionary<string, Node3D>(StringComparer.OrdinalIgnoreCase);
        Node3D Load(string asset)
        {
            if (string.IsNullOrEmpty(asset)) return null;
            if (loaded.TryGetValue(asset, out var n)) return n;
            n = null;
            string file = asset + ".nif";
            foreach (var archive in ModelArchives)
            {
                string entry = packs.Exists(archive, file.ToLowerInvariant()) ? file.ToLowerInvariant() : packs.FindByFileName(archive, file);
                if (entry == null) continue;
                try { n = builder.Build(builder.LoadModel(archive, entry), "DropItemNIF"); break; }
                catch (Exception e) { fails.Add($"model {asset}: {e.Message}"); }
            }
            return loaded[asset] = n;
        }
        var grades = root.Elements("gradeNif").Select(e => ((int?)e.Attribute("id") ?? 0, (string)e.Attribute("nif") ?? "")).ToList();
        var petGrades = root.Elements("gradePetNif").Select(e => ((int?)e.Attribute("id") ?? 0, (string)e.Attribute("nif") ?? "")).ToList();
        int gradeOk = 0, iconMesh = 0, petOk = 0;
        foreach (var (g, nif) in grades)
        {
            var n = Load(nif);
            if (n == null) { fails.Add($"grade {g} nif {nif} does not load"); continue; }
            gradeOk++;
            if (FindIconMesh(n)) iconMesh++; else fails.Add($"grade {g} nif {nif} has no Item_Icon mesh");
        }
        foreach (var (g, nif) in petGrades)
            if (Load(nif) != null) petOk++; else fails.Add($"pet grade {g} nif {nif} does not load");

        var assetRows = table.AssetRows.ToList();
        int rowsOk = 0;
        var mesoPiles = new List<string>();
        foreach (var (item, type, min) in assetRows)
        {
            var st = table.Style(type);
            if (st == null) { fails.Add($"dropItemAsset {item} min {min}: no style {type}"); continue; }
            if (Load(st.AssetName) == null) { fails.Add($"dropItemAsset {item} min {min}: style {type} model {st.AssetName} does not load"); continue; }
            rowsOk++;
            if (item == 90000001) mesoPiles.Add($"{min}:{st.AssetName}");
        }
        // every meso amount the rows split reaches a row (amount 1 up)
        if (table.ForItem(90000001, 1) == null) fails.Add("a meso of amount 1 has no row");
        foreach (int sp in new[] { 90000008, 90000009, 90000010, 90000019 })
            if (table.Look(sp, 1, 1, false) == null) fails.Add($"special item {sp} has no look (InitDrop makes no drop)");

        // the effects
        int gradeFx = 0, gradeFxNamed = 0, beFx = 0, beFxNamed = 0;
        foreach (var e in root.Elements("gradeEffect"))
        {
            string name = (string)e.Attribute("effect") ?? "";
            if (name.Length == 0) continue;
            gradeFxNamed++;
            if (effects.Get(name) != null) gradeFx++; else fails.Add($"grade effect {e.Attribute("id")} {name} is no effect xml");
        }
        var soundIds = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (int s in styleIds)
        {
            var st = table.Style(s);
            if (!string.IsNullOrEmpty(st.BeEffect))
            {
                beFxNamed++;
                if (effects.Get(st.BeEffect) != null) beFx++; else fails.Add($"style {s} beEffect {st.BeEffect} is no effect xml");
            }
            if (!string.IsNullOrEmpty(st.DropSoundId)) soundIds.Add(st.DropSoundId);
            if (!string.IsNullOrEmpty(st.GetSoundId)) soundIds.Add(st.GetSoundId);
        }
        int soundOk = 0;
        foreach (var id in soundIds)
            if (sounds?.ResolveEvent(DropEffects.SoundGroup + "/" + id) != null) soundOk++; else fails.Add($"item sound {id} has no event with a wave");
        var system = SystemSoundTable.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", "table/systemsound.xml")));
        // the bag-full sound, as PlaySystemSound gives it to a male and a female beginner (job group 1): byGender and byJob rows
        // take the gender's category and the job's name; the waves come from the voice language's banks
        var warns = new[] { system.Resolve(DropPickup.BagFullSound, 0, 1), system.Resolve(DropPickup.BagFullSound, 1, 1) };
        bool warnOk = warns.All(w => w != null && sounds?.ResolveEvent(w) != null);
        if (!warnOk) fails.Add($"bag-full sound {string.Join(" ", warns)} has no event with a wave");
        var warn = string.Join(" ", warns);
        var strings = Ms2.Ui.GameTables.Shared(packs, GameOptions.StringLanguage);
        foreach (int n in new[] { DropPickup.BagFullNotice, DropPickup.TooFarNotice })
            if (Ms2.Formats.Text.CommonStringIds.Key(n) is not { } key || string.IsNullOrEmpty(strings.Common(key))) fails.Add($"notice 0x{n:x} has no common string");

        // every item with data: its look and its icon
        var kinds = new ItemKinds(packs);
        var ids = new SortedSet<int>();
        var perFile = new Regex(@"^item/\d+/\d+/(\d+)\.xml$");
        foreach (var entry in packs.List("Xml"))
        {
            if (entry.StartsWith("itemdata/") && entry.EndsWith(".xml") && int.TryParse(entry[9..^4], out _))
                foreach (var e in Ms2.Formats.Text.PackXml.Parse(packs.Read("Xml", entry)).Root.Elements("item"))
                    { if (int.TryParse((string)e.Attribute("id"), out int i)) ids.Add(i); }
            else if (perFile.Match(entry) is { Success: true } m) ids.Add(int.Parse(m.Groups[1].Value));
        }
        int known = 0, pets = 0, looks = 0, icons = 0, iconByName = 0, noIcon = 0, noIconPath = 0, mesos = 0;
        var noIconSample = new List<int>();
        var noIconGroups = new SortedDictionary<int, int>();
        foreach (int id in ids)
        {
            if (!kinds.Known(id)) continue;
            known++;
            bool meso = FieldAddItemIsMeso(id);
            bool pet = !meso && kinds.ExtraOf(id) is { } x && x.HasFlag(Ms2.Net.Packets.ItemExtra.Pet);
            if (pet) pets++;
            if (meso) mesos++;
            if (table.Look(id, 1, 1, pet) != null) looks++; else fails.Add($"item {id}: no drop look");
            if (meso) continue;
            string path = kinds.IconPath(id) ?? "";
            string e2 = path.ToLowerInvariant();
            int at = e2.IndexOf("resource/image/", StringComparison.Ordinal);
            if (at >= 0) e2 = e2[(at + "resource/image/".Length)..];
            if (e2.Length > 0 && packs.Exists("Resource/Image", e2)) icons++;
            else if (e2.Length > 0 && packs.FindByFileName("Resource/Image", System.IO.Path.GetFileName(e2)) != null) iconByName++;
            else if (e2.Length == 0) noIconPath++;
            else
            {
                noIcon++;
                if (noIconSample.Count < 12) noIconSample.Add(id);
                noIconGroups[id / 100000] = noIconGroups.GetValueOrDefault(id / 100000) + 1;
            }
        }

        GD.Print($"drop data {Settings.DataDir}:");
        GD.Print($"  {(gms2Layout ? "GMS2 layout (dropitem rows, item xmlData)" : "KMS layout (dropStyle, dropItemAsset)")}: styles {styleIds.Count} ({string.Join(" ", styleIds)})");
        GD.Print($"  grade nifs {gradeOk}/{grades.Count} load, {iconMesh} with Item_Icon; pet grade nifs {petOk}/{petGrades.Count}");
        GD.Print($"  dropItemAsset rows {rowsOk}/{assetRows.Count} load; meso piles {string.Join(" ", mesoPiles)}");
        GD.Print($"  grade effects {gradeFx}/{gradeFxNamed} named; beEffects {beFx}/{beFxNamed}");
        GD.Print($"  item sounds {soundOk}/{soundIds.Count} ({string.Join(" ", soundIds)}); bag-full sound {(warnOk ? warn : "MISSING")}");
        GD.Print($"  items {ids.Count} ids, {known} with data ({pets} pets, {mesos} meso ids), looks {looks}, icons {icons} by path + {iconByName} by name only, {noIconPath} with no icon path, {noIcon} whose file is not in the pack{(noIconSample.Count > 0 ? " e.g. " + string.Join(" ", noIconSample) : "")}");
        GD.Print($"  no icon file by id prefix (id / 100000): {string.Join(" ", noIconGroups.OrderByDescending(g => g.Value).Take(15).Select(g => $"{g.Key}:{g.Value}"))}");
        // an icon missing from the pack: the client shows the drop with the grade nif's own texture (SetIconTexture loads nothing); reported, not failed
    }

    static bool FieldAddItemIsMeso(int id) => Ms2.Net.Packets.FieldAddItem.IsMesoId(id);

    static bool FindIconMesh(Node n)
    {
        if (n is MeshInstance3D m && m.Name.ToString().StartsWith("Item_Icon", StringComparison.OrdinalIgnoreCase)) return true;
        for (int i = 0, count = n.GetChildCount(); i < count; i++) if (FindIconMesh(n.GetChild(i))) return true;
        return false;
    }
}
