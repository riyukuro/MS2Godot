using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// FieldSpawnPoints against WorldService::FUN_141bcfff0 (worldservice_1.cpp:5872-5897), on made-up entities and on every spawn
/// point of the field data.
/// </summary>
public class FieldSpawnPointsTests
{
    static readonly FlatModel PointFlat = new()
    {
        Name = "EventSpawnPointNPC",
        Mixins = { "SpawnPointNPC", "SpawnPoint" },
        Props = { ["SpawnAnimation"] = "Regen_A", ["SpawnPointID"] = "101" },
    };

    static FieldEntity Point(string id, string anim, FlatModel flat = null)
    {
        var raw = new XEntity { ModelName = (flat ?? PointFlat).Name };
        if (id != null) raw.Props["SpawnPointID"] = id;
        if (anim != null) raw.Props["SpawnAnimation"] = anim;
        return new FieldEntity { Raw = raw, Flat = flat ?? PointFlat };
    }

    [Test]
    public void TheFirstPointOfTheIdGivesItsSequenceAndAnEmptyOneIsWalkA()
    {
        var group = new FlatModel { Name = "EventSpawnGroupNPC", Mixins = { "EventSpawnPointNPC", "SpawnPointNPC" }, Props = { ["SpawnAnimation"] = "Regen_A", ["SpawnPointID"] = "101" } };
        var region = new FlatModel { Name = "MS2RegionSpawn", Mixins = { "MS2RegionSpawnBase" }, Props = { ["SpawnPointID"] = "0" } };
        var points = new FieldSpawnPoints(new[]
        {
            Point("5", "Idle_A"),
            Point("5", "Run_A"),          // a later point of the same id is not reached
            Point("6", ""),
            Point(null, null),            // the model's defaults: 101, Regen_A
            Point("7", null, group),      // a model that mixes the point in
            Point("8", "Fly_A", region),  // not a point: the lookup lists EventSpawnPointNPC entities only
            Point("0", "Bore_A"),         // id 0 is no point (:5883)
            Point("x", "Bore_A"),
        });
        Assert.That(points.Find(5), Is.EqualTo("Idle_A"));
        Assert.That(points.Find(6), Is.EqualTo("Walk_A"));
        Assert.That(points.Find(101), Is.EqualTo("Regen_A"));
        Assert.That(points.Find(7), Is.EqualTo("Regen_A"));
        Assert.That(points.Find(8), Is.Null);
        Assert.That(points.Find(0), Is.Null);
        Assert.That(points.Find(9), Is.Null);
        Assert.That(points.Count, Is.EqualTo(4));
    }

    [Test] public void KmsSpawnPoints() => Sweep("MS2_DATA");
    [Test] public void Gms2SpawnPoints() => Sweep("MS2_GMS2_DATA");
    [Test] public void KmsDevSpawnPoints() => Sweep("MS2_KMS2_DEV_DATA");

    /// <summary>
    /// Every spawn point of every field (the EventSpawnPointNPC entities of each xblock): its id and sequence as the spawn state
    /// finds them, and for each npc of its NpcList whether that npc's model has the sequence. The lookup must give each point
    /// id its first point's sequence. MS2_SPAWNPOINTS_OUT names a file the counts are added to.
    /// </summary>
    static void Sweep(string variable)
    {
        string data = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(data) || !Directory.Exists(data)) Assert.Ignore($"{variable} not set");
        using var packs = new PackSet(data);
        var flats = new FlatLibrary(packs);
        var npcs = new NpcTable(packs);
        var kfmEntries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in packs.List("Resource/Model/Npc"))
            if (e.EndsWith(".kfm", StringComparison.OrdinalIgnoreCase)) kfmEntries.TryAdd(Path.GetFileNameWithoutExtension(e), e);
        var kfms = new Dictionary<string, KfmFile>(StringComparer.OrdinalIgnoreCase);
        KfmFile Kfm(string name)
        {
            if (name == null) return null;
            if (kfms.TryGetValue(name, out var k)) return k;
            k = kfmEntries.TryGetValue(name, out var entry) ? KfmFile.Load(packs.Read("Resource/Model/Npc", entry)) : null;
            return kfms[name] = k;
        }

        int fields = 0, points = 0, ownAnim = 0, modelAnim = 0, emptyAnim = 0, noId = 0, sameIdLater = 0, noNpcs = 0;
        int pairs = 0, noRow = 0, noModel = 0, hasSeq = 0, lacksSeq = 0;
        var bySequence = new SortedDictionary<string, (int points, int has, int lacks)>(StringComparer.Ordinal);
        foreach (var x in packs.List(XBlock.Archive))
        {
            if (!x.StartsWith("xblock/", StringComparison.OrdinalIgnoreCase) || !x.EndsWith(".xblock", StringComparison.OrdinalIgnoreCase)) continue;
            var map = FieldMap.Load(packs, flats, Path.GetFileNameWithoutExtension(x));
            fields++;
            var table = new FieldSpawnPoints(map.Entities);
            var seen = new HashSet<int>();
            foreach (var e in map.Entities)
            {
                if (!e.HasMixin(FieldSpawnPoints.Model)) continue;
                points++;
                if (e.Raw.Props.ContainsKey("SpawnAnimation")) ownAnim++; else modelAnim++;
                if (string.IsNullOrEmpty(e.Get("SpawnAnimation"))) emptyAnim++;
                int id = FieldSpawnPoints.Id(e);
                string seq = FieldSpawnPoints.Animation(e);
                if (id == 0) { noId++; Assert.That(table.Find(id), Is.Null); continue; }
                if (!seen.Add(id)) sameIdLater++;
                else Assert.That(table.Find(id), Is.EqualTo(seq), $"{x} point {id}");
                var c = bySequence.GetValueOrDefault(seq);
                c.points++;
                var list = e.Raw.Sets.GetValueOrDefault("NpcList");
                if (list == null || list.Count == 0) noNpcs++;
                foreach (var (index, _) in list ?? new())
                {
                    if (!int.TryParse(index, out int npcId)) continue;
                    pairs++;
                    var row = npcs.Find(npcId);
                    if (row == null) { noRow++; continue; }
                    var kfm = Kfm(row.Kfm);
                    if (kfm == null) { noModel++; continue; }
                    if (kfm.ByName(seq) != null) { hasSeq++; c.has++; }
                    else { lacksSeq++; c.lacks++; }
                }
                bySequence[seq] = c;
            }
        }
        string report = $"{variable}: {fields} fields, {points} spawn points ({ownAnim} with their own SpawnAnimation, {modelAnim} with the model's, {emptyAnim} empty -> Walk_A; " +
                        $"{noId} without an id, {sameIdLater} behind an earlier point of the same id, {noNpcs} without npcs); " +
                        $"{pairs} point-npc pairs: {noRow} npc without a row, {noModel} without a model, {hasSeq} model has the sequence, {lacksSeq} lacks it\n" +
                        string.Join("\n", bySequence.Select(k => $"  {k.Key}: {k.Value.points} points, npc model has it {k.Value.has}, lacks it {k.Value.lacks}")) + "\n";
        TestContext.Out.WriteLine(report);
        if (Environment.GetEnvironmentVariable("MS2_SPAWNPOINTS_OUT") is { Length: > 0 } outFile) File.AppendAllText(outFile, report);
        Assert.That(fields, Is.GreaterThan(1000));
        Assert.That(points, Is.GreaterThan(10000));
        Assert.That(hasSeq, Is.GreaterThan(lacksSeq));
    }
}
