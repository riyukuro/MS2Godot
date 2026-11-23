using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>A pc's face emotion (PCBase's m_str9e0 / m_timer9e8 / CTextureAnimation, pcbase.cpp, textureanimationdata.cpp)
/// and the trigger's Ui sub 9 on the own pc (CTriggerService::FUN_1418641e0).</summary>
public class FaceEmotionTests
{
    sealed class Face : IFaceTexture
    {
        public bool Found = true;
        public readonly List<(string Base, string Control)> Shows = new();
        public int Stops;
        public bool Has(string target) => Found && target == "FA";
        public bool Show(string target, string baseEntry, string controlEntry) { Shows.Add((baseEntry, controlEntry)); return true; }
        public void Stop() => Stops++;
    }

    static void PlaysTheRestFaceAndTheTriggersFaces(string variable)
    {
        using var packs = new PackSet(RcGap.Data(variable));
        Assert.That(FaceEmotion.DefaultFace(packs, 0), Is.EqualTo(10300001), "the male base face");
        Assert.That(FaceEmotion.DefaultFace(packs, 1), Is.EqualTo(10300003), "the female base face");
        Assert.That(FaceEmotion.FaceItem(10300099, true, packs, 1), Is.EqualTo(10300099), "a worn face with parts");
        Assert.That(FaceEmotion.FaceItem(10300099, false, packs, 1), Is.EqualTo(10300003));

        var group = new EmotionTable(packs, EmotionTable.Item).Group(10300003);
        Assert.That(group, Is.Not.Null);
        var face = new Face();
        var fe = new FaceEmotion(face);

        // the face model on: nothing played yet, so nothing shows; the next tick rests the face on "default"
        fe.SetGroup(group, 0);
        Assert.That(face.Shows, Is.Empty);
        fe.Tick(16);
        Assert.That(fe.Current, Is.EqualTo("default"));
        Assert.That(fe.Frame, Is.Zero);
        string idle = $"item_face/{group.ItemCode}/01_idle.dds";
        Assert.That(face.Shows[^1], Is.EqualTo((idle, $"item_face/{group.ItemCode}/02_idle_c.dds")));
        Assert.That(packs.Exists(EmotionTextureFrame.Archive, idle), Is.True, idle);

        // the frames follow the time since the start; the same frame is not shown twice
        fe.Tick(16 + 800);
        Assert.That(face.Shows, Has.Count.EqualTo(1));
        fe.Tick(16 + 801);
        Assert.That(fe.Frame, Is.EqualTo(1));
        Assert.That(face.Shows, Has.Count.EqualTo(2));

        // past its last frame the texture stops and the rest face starts over at once
        var rest = group.Find("default").Texture;
        int length = rest.Frames.Sum(f => f.Delay);
        fe.Tick(16 + length + 1);
        Assert.That(face.Stops, Is.EqualTo(1));
        Assert.That(fe.Current, Is.EqualTo("default"));
        Assert.That(fe.Frame, Is.Zero);
        Assert.That(face.Shows[^1].Base, Is.EqualTo(idle));

        // sub 9 with a name: the text keys off, the face played; the same name again does not start it over
        long t = 10_000;
        fe.Trigger("Trigger_panic", t);
        Assert.That(fe.Current, Is.EqualTo("Trigger_panic"));
        Assert.That(fe.TextKeys, Is.False);
        int shows = face.Shows.Count;
        fe.Trigger("Trigger_panic", t + 100);
        Assert.That(face.Shows, Has.Count.EqualTo(shows));

        // battle: the rest face becomes defaultBattle, a face not resting stays; sub 9 with no name rests it
        fe.SetBattle(true, t + 200);
        Assert.That((fe.Rest, fe.Current), Is.EqualTo(("defaultBattle", "Trigger_panic")));
        fe.Trigger("", t + 300);
        Assert.That((fe.Current, fe.TextKeys), Is.EqualTo(("defaultBattle", true)));
        fe.SetBattle(false, t + 400);
        Assert.That(fe.Current, Is.EqualTo("default"), "a face resting on defaultBattle goes back to default");

        // a name the group lacks: the text keys still go off, the face stays
        fe.Trigger("no such face", t + 500);
        Assert.That((fe.Current, fe.TextKeys), Is.EqualTo(("default", false)));

        // the flag off (the avatar copies): nothing plays
        fe.Enabled = false;
        fe.Trigger("Angry", t + 600);
        Assert.That(fe.Current, Is.EqualTo("default"));

        // a model without the target: the emotion plays, but no frame shows, and the next tick rests the face
        var none = new Face { Found = false };
        var bare = new FaceEmotion(none);
        bare.SetGroup(group, 0);
        bare.Trigger("Angry", 0);
        Assert.That((bare.Current, bare.Frame, none.Shows.Count), Is.EqualTo(("Angry", -1, 0)));
        bare.Tick(1);
        Assert.That(bare.Current, Is.EqualTo("default"));
    }

    [Test]
    public void TheKmsFacePlaysTheRestFaceAndTheTriggersFaces() => PlaysTheRestFaceAndTheTriggersFaces("MS2_DATA");

    [Test]
    public void TheGms2FacePlaysTheRestFaceAndTheTriggersFaces() => PlaysTheRestFaceAndTheTriggersFaces("MS2_GMS2_DATA");

    /// <summary>The names the trigger xmls give the own pc's face (FaceEmotion with spawnPointID 0, which the server sends as
    /// sub 9 to the first player), with the first file that names each; the empty name (rest) left out.</summary>
    static SortedDictionary<string, string> OwnFaceNames(PackSet packs)
    {
        static string Attr(System.Xml.Linq.XElement e, string name) =>
            (string)e.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? "";
        var names = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in packs.List("Xml").Where(e => e.StartsWith("trigger/", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e, StringComparer.Ordinal))
        {
            var bytes = packs.Read("Xml", entry);
            if (bytes.AsSpan().IndexOf("FaceEmotion"u8) < 0) continue;
            System.Xml.Linq.XDocument doc;
            try { doc = PackXml.Parse(bytes); } catch (System.Xml.XmlException) { continue; }
            foreach (var a in doc.Descendants("action"))
                if (Attr(a, "name").Equals("FaceEmotion", StringComparison.OrdinalIgnoreCase) && Attr(a, "spawnPointID") is "0" or ""
                    && Attr(a, "emotionName").Trim() is { Length: > 0 } n)
                    names.TryAdd(n, entry);
        }
        return names;
    }

    static void EveryOwnFaceNameResolves(string variable)
    {
        using var packs = new PackSet(RcGap.Data(variable));
        var names = OwnFaceNames(packs);
        var table = new EmotionTable(packs, EmotionTable.Item);
        var missing = new List<string>();
        foreach (int item in new[] { FaceEmotion.DefaultFace(packs, 0), FaceEmotion.DefaultFace(packs, 1) })
            foreach (var (name, file) in names)
                if (table.Group((uint)item)?.Find(name) == null) missing.Add($"{item}/{name} ({file})");
        TestContext.WriteLine($"{variable}: {names.Count} names; not in a base face's group: {(missing.Count == 0 ? "none" : string.Join(", ", missing))}");
        Assert.That(names, Has.Count.GreaterThan(10));
        Assert.That(missing, Is.Empty);
    }

    [Test]
    public void EveryKmsOwnFaceNameResolvesOnBothBaseFaces() => EveryOwnFaceNameResolves("MS2_DATA");

    [Test]
    public void EveryGms2OwnFaceNameResolvesOnBothBaseFaces() => EveryOwnFaceNameResolves("MS2_GMS2_DATA");

    /// <summary>The emo_ keys of the body's companion xml (&lt;bodyDir&gt;/&lt;bodyDir&gt;.xml), name without the prefix -> the
    /// first sequence that has it.</summary>
    static SortedDictionary<string, string> BodyEmoKeys(PackSet packs, string bodyDir)
    {
        var keys = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var doc = PackXml.Parse(packs.Read("Resource/Model/Character", $"{bodyDir}/{bodyDir}.xml"));
        foreach (var seq in doc.Root.Elements())
            foreach (var k in seq.Elements("key"))
                if ((string)k.Attribute("text") is { Length: > 4 } text && text.StartsWith("emo_", StringComparison.Ordinal))
                    keys.TryAdd(text[4..], seq.Name.LocalName);
        return keys;
    }

    static void EmoKeysPlayTheirFaces(string variable)
    {
        using var packs = new PackSet(RcGap.Data(variable));
        var table = new EmotionTable(packs, EmotionTable.Item);
        foreach (var (gender, bodyDir) in new[] { (0, "male"), (1, "female") })
        {
            var group = table.Group((uint)FaceEmotion.DefaultFace(packs, gender));
            var keys = BodyEmoKeys(packs, bodyDir);
            var found = keys.Keys.Where(n => group.Find(n) != null).ToList();
            TestContext.WriteLine($"{variable} {bodyDir}: {keys.Count} emo_ names, {found.Count} in the base face's group; e.g. {string.Join(", ", found.Take(12))}");
            Assert.That(keys, Has.Count.GreaterThan(500), "the body's sequences carry emo_ keys");
            Assert.That(found, Has.Count.GreaterThan(keys.Count / 2));
            Assert.That(found, Does.Contain("boreA").And.Contain("Dead"), "the idle bore and the death faces");
        }

        var face = new Face();
        var fe = new FaceEmotion(face);
        fe.SetGroup(table.Group((uint)FaceEmotion.DefaultFace(packs, 1)), 0);
        fe.Tick(1);
        Assert.That(fe.Current, Is.EqualTo("default"));
        // not an emo_ key, the bare prefix, a name the group lacks: nothing plays
        Assert.That(fe.TextKey("effect_angry", 10), Is.False);
        Assert.That(fe.TextKey("emo_", 10), Is.False);
        Assert.That(fe.TextKey("emo_no such face", 10), Is.False);
        Assert.That(fe.Current, Is.EqualTo("default"));
        // Emotion_Angry_A's key plays the angry face; the same key again does not start it over
        Assert.That(fe.TextKey("emo_angry", 20), Is.True);
        Assert.That(fe.Current, Is.EqualTo("angry").IgnoreCase);
        int shows = face.Shows.Count;
        Assert.That(fe.TextKey("emo_angry", 30), Is.False);
        Assert.That(face.Shows, Has.Count.EqualTo(shows));
        // a trigger's face turns the keys off until the face rests
        fe.Trigger("Trigger_panic", 40);
        Assert.That(fe.TextKey("emo_angry", 50), Is.False);
        Assert.That(fe.Current, Is.EqualTo("Trigger_panic"));
        fe.Trigger("", 60);
        Assert.That(fe.TextKey("emo_angry", 70), Is.True);
    }

    [Test]
    public void KmsEmoKeysPlayTheirFaces() => EmoKeysPlayTheirFaces("MS2_DATA");

    [Test]
    public void Gms2EmoKeysPlayTheirFaces() => EmoKeysPlayTheirFaces("MS2_GMS2_DATA");
}
