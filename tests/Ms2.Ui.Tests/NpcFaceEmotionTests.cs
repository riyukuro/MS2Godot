using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The trigger's Ui sub 9 on npcs (CTriggerService::FUN_1418641e0, triggerservice.cpp:1052-1068): the face emotion of an npc
/// plays from the Npc library's group (CNpc::vfunc_70, unit_14108e1e0_npcactorandvibrate.cpp:663) of the row's emotionID, or of
/// the npc id when it has none (CNpc::vfunc_69, :1427-1436; CNpcData::LoadXml, npcdata.cpp:1492).
/// </summary>
public class NpcFaceEmotionTests
{
    sealed class Face : IFaceTexture
    {
        public readonly List<string> Shows = new();
        public bool Has(string target) => true;
        public bool Show(string target, string baseEntry, string controlEntry) { Shows.Add(baseEntry); return true; }
        public void Stop() { }
    }

    static string Attr(XElement e, string name) =>
        (string)e.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? "";

    /// <summary>The emotionID attribute of the environment: rows that carry one play another group than their own id's.</summary>
    static void EmotionIdIsReadFromTheEnvironment(string variable)
    {
        using var packs = new PackSet(RcGap.Data(variable));
        var npcs = new NpcTable(packs);
        int raw = 0, other = 0, mismatched = 0;
        var files = packs.List("Xml").Where(e => e.StartsWith("npcdata/", StringComparison.OrdinalIgnoreCase) && e.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).ToList();
        // KMS keeps the rows in npcdata/*.xml, GMS2 one file per npc (npc/<aa>/<bb>/<id>.xml)
        var elements = files.Count > 0
            ? files.SelectMany(f => PackXml.Parse(packs.Read("Xml", f)).Descendants("npc"))
            : PerFileTable.Rows(packs, "npc/", "npc").Root.Elements("npc");
        var seen = new Dictionary<int, int>();
        foreach (var n in elements)
        {
            if (!int.TryParse((string)n.Attribute("id"), out int id) || n.Element("environment") is not { } env) continue;
            string at = (string)env.Attribute("emotionID") ?? (string)env.Element("basic")?.Attribute("emotionID");
            seen[id] = int.TryParse(at, out int v) ? v : 0;     // the last row of an id wins, as in the table
        }
        foreach (var (id, emotionId) in seen)
        {
            if (npcs.Find(id) is not { } row) continue;
            if (emotionId != 0) raw++;
            if (row.EmotionId != emotionId) mismatched++;
            if (row.EmotionGroupId != id) other++;
        }
        TestContext.WriteLine($"{variable}: {raw} rows with an emotionID, {other} play another id's group, {mismatched} mismatched");
        Assert.That(raw, Is.GreaterThan(100));
        Assert.That(other, Is.GreaterThan(0));
        Assert.That(mismatched, Is.Zero);
    }

    [Test] public void KmsEmotionIdIsReadFromTheEnvironment() => EmotionIdIsReadFromTheEnvironment("MS2_DATA");
    [Test] public void Gms2EmotionIdIsReadFromTheEnvironment() => EmotionIdIsReadFromTheEnvironment("MS2_GMS2_DATA");

    /// <summary>
    /// Every FaceEmotion of the trigger xmls with a spawnPointID (the Maple2 server sends sub 9 to each npc of that spawn
    /// point): the field's spawn point (the EventSpawnPointNPC entity of that SpawnPointID), each npc of its NpcList, the npc's
    /// group and the named emotion in it. A found emotion must play on the npc's face and its frames must be in the packs.
    /// The causes of the rest are counted and printed.
    /// </summary>
    static void EveryNpcFaceUse(string variable)
    {
        using var packs = new PackSet(RcGap.Data(variable));
        var flats = new FlatLibrary(packs);
        var npcs = new NpcTable(packs);
        var table = new EmotionTable(packs, EmotionTable.Npc);
        var maps = new Dictionary<string, FieldMap>(StringComparer.OrdinalIgnoreCase);
        int uses = 0, rest = 0, plays = 0, byEmotionId = 0;
        var causes = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        void Cause(string why, string what) { if (!causes.TryGetValue(why, out var l)) causes[why] = l = new(); l.Add(what); }
        var missingFrames = new List<string>();
        var played = new List<string>();
        foreach (var entry in packs.List("Xml").Where(e => e.StartsWith("trigger/", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e, StringComparer.Ordinal))
        {
            var bytes = packs.Read("Xml", entry);
            if (bytes.AsSpan().IndexOf("FaceEmotion"u8) < 0) continue;
            XDocument doc;
            try { doc = PackXml.Parse(bytes); } catch (System.Xml.XmlException) { continue; }
            string dir = entry.Split('/')[1];
            foreach (var a in doc.Descendants("action"))
            {
                if (!Attr(a, "name").Equals("FaceEmotion", StringComparison.OrdinalIgnoreCase)) continue;
                if (!int.TryParse(Attr(a, "spawnPointID"), out int sp) || sp == 0) continue;
                uses++;
                string name = Attr(a, "emotionName").Trim();
                string where = $"{dir} {sp} '{name}'";
                if (!maps.TryGetValue(dir, out var map))
                    maps[dir] = map = packs.Exists(XBlock.Archive, $"xblock/{dir}.xblock") ? FieldMap.Load(packs, flats, dir) : null;
                if (map == null) { Cause("no field", where); continue; }
                var point = map.Entities.FirstOrDefault(e => e.HasMixin(FieldSpawnPoints.Model) && FieldSpawnPoints.Id(e) == sp);
                if (point == null) { Cause("no spawn point", where); continue; }
                var ids = (point.Raw.Sets.GetValueOrDefault("NpcList") ?? new()).Select(s => int.TryParse(s.Item1, out int v) ? v : 0).Where(v => v > 0).ToList();
                if (ids.Count == 0) { Cause("spawn point without npcs", where); continue; }
                foreach (int id in ids)
                {
                    string at = $"{where} npc {id}";
                    if (npcs.Find(id) is not { } row) { Cause("no npc row", at); continue; }
                    var group = table.Group((uint)row.EmotionGroupId);
                    if (group == null) { Cause("no group", $"{at} group {row.EmotionGroupId}"); continue; }
                    var fe = new FaceEmotion(new Face(), own: false);
                    fe.UseGroup(group);
                    if (name.Length == 0)
                    {
                        // an empty name rests the face: "default" when the group has it
                        rest++;
                        fe.Trigger(name, 0);
                        Assert.That(fe.TextKeys, Is.True, at);
                        continue;
                    }
                    if (group.Find(name) is not { } data) { Cause("not in the npc's group", $"{at} group {row.EmotionGroupId}"); continue; }
                    plays++;
                    played.Add($"{at} {data.Texture?.Target ?? "-"}");
                    if (row.EmotionId != 0 && row.EmotionId != row.Id) byEmotionId++;
                    var face = new Face();
                    fe = new FaceEmotion(face, own: false);
                    fe.UseGroup(group);
                    fe.Tick(0);
                    Assert.That(face.Shows, Is.Empty, $"{at}: the face shows nothing before an emotion plays");
                    fe.Trigger(name, 0);
                    Assert.That((fe.Current, fe.TextKeys), Is.EqualTo((name, false)), at);
                    if (data.Texture == null) { Cause("emotion without a texture animation (body only)", at); continue; }
                    // an npc waits for its textures (m_nTime adds the time since the start at each tick), then shows the first frame
                    fe.Tick(16);
                    Assert.That(face.Shows, Is.Empty, $"{at}: shown before its textures");
                    fe.Tick(1000);
                    Assert.That(face.Shows, Is.Not.Empty, $"{at}: no frame after the wait");
                    foreach (var f in data.Texture.Frames)
                        if (!packs.Exists(EmotionTextureFrame.Archive, f.BaseEntry(group.ItemCode))) missingFrames.Add($"{at}: {f.BaseEntry(group.ItemCode)}");
                }
            }
        }
        TestContext.WriteLine($"{variable}: {uses} npc face uses, {plays} play (via emotionID {byEmotionId}), {rest} rest");
        TestContext.WriteLine($"  played: {string.Join("; ", played.Take(16))}");
        foreach (var (why, list) in causes) TestContext.WriteLine($"  {why}: {list.Count}: {string.Join("; ", list.Take(12))}");
        if (missingFrames.Count > 0) TestContext.WriteLine($"  frames not in the packs: {missingFrames.Count}: {string.Join("; ", missingFrames.Take(8))}");
        Assert.That(uses, Is.GreaterThan(40));
        Assert.That(plays, Is.GreaterThan(uses / 2));
        Assert.That(missingFrames, Is.Empty);
    }

    /// <summary>
    /// CTextureAnimation::FUN_1418484a0 (textureanimationdata.cpp:303-309): an actor that is no MyPC waits only while the frames
    /// are not loaded (AreAllFramesLoaded); once it has waited for an emotion's frames, the next start shows frame 0 at once,
    /// so a repeat of the emotion does not show the bare face first.
    /// </summary>
    static void RepeatShowsAtOnce(string variable)
    {
        using var packs = new PackSet(RcGap.Data(variable));
        var npcs = new NpcTable(packs);
        var row = npcs.Find(11003364);
        Assume.That(row, Is.Not.Null, "npc 11003364 not in this data");
        var group = new EmotionTable(packs, EmotionTable.Npc).Group((uint)row!.EmotionGroupId);
        Assume.That(group?.Find("Angry")?.Texture, Is.Not.Null, "no Angry texture emotion here");
        var face = new Face();
        var fe = new FaceEmotion(face, own: false);
        fe.UseGroup(group);
        fe.Trigger("Angry", 0);
        Assert.That(face.Shows, Is.Empty, "the first start waits for the frames");
        fe.Tick(16);
        fe.Tick(1000);
        Assert.That(face.Shows, Is.Not.Empty, "no frame after the wait");
        fe.Trigger("", 1100);
        face.Shows.Clear();
        fe.Trigger("Angry", 1200);
        Assert.That(face.Shows, Is.Not.Empty, "the frames already waited for did not show at once");
    }

    [Test] public void KmsRepeatShowsAtOnce() => RepeatShowsAtOnce("MS2_DATA");
    [Test] public void Gms2RepeatShowsAtOnce() => RepeatShowsAtOnce("MS2_GMS2_DATA");

    [Test] public void EveryKmsNpcFaceUse() => EveryNpcFaceUse("MS2_DATA");
    [Test] public void EveryGms2NpcFaceUse() => EveryNpcFaceUse("MS2_GMS2_DATA");
}
