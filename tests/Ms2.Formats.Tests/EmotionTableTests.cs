using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>The emotion library (CEmotionTable / CEmotionGroup / CEmotionData, emotiondata.cpp) and the texture
/// animation's frames (CTextureAnimationData, textureanimationdata.cpp).</summary>
public class EmotionTableTests
{
    static EmotionTextureAnimation Anim(string xml) => EmotionTextureAnimation.Read(XElement.Parse(xml));

    [Test]
    public void TheTextureAnimationReadsItsTargetFramesAndLoopFrame()
    {
        var a = Anim("""<textureani target="FA"><texture file="Item_Face/{0}/01_Idle.dds" control="Item_Face/{0}/02_Idle_C.dds" delay="400" /><texture file="Item_Face\FC.dds" delay="900" loop="true" /></textureani>""");
        Assert.That(a.Target, Is.EqualTo("FA"));
        Assert.That(a.LoopFrame, Is.EqualTo(1));
        Assert.That(a.Frames.Select(f => f.Delay), Is.EqualTo(new[] { 400, 900 }));
        Assert.That(a.Frames[0].BaseEntry("FA_00001"), Is.EqualTo("item_face/fa_00001/01_idle.dds"), "{0} takes the folder");
        Assert.That(a.Frames[0].ControlEntry(""), Is.EqualTo("item_face/{0}/02_idle_c.dds"), "no folder: the name as written");
        Assert.That(a.Frames[1].BaseEntry(""), Is.EqualTo("item_face/fc.dds"));
        Assert.That(a.Frames[1].ControlEntry(""), Is.Empty);
        Assert.That(Anim("""<textureani><texture file="a.dds" /></textureani>""").Target, Is.EqualTo("FA"), "no target: FA");
        Assert.That(Anim("""<textureani target="FA" />"""), Is.Null, "no frame: no texture animation");
    }

    [Test]
    public void TheFrameAtATimeFollowsTheDelaysAndTheLoop()
    {
        // no loop frame: a time on a frame's end is still that frame; past the last one the frame count
        var plain = Anim("""<textureani><texture file="a" delay="100" /><texture file="b" delay="200" /></textureani>""");
        Assert.That(new[] { 0, 100, 101, 300, 301, 5000 }.Select(plain.FrameAt), Is.EqualTo(new[] { 0, 0, 1, 1, 2, 2 }));

        // femalecustom Barbecue: happy 3500 (loop), expect 900, happy 500; the time runs round all three
        var loop0 = Anim("""<textureani><texture file="h" delay="3500" loop="true" /><texture file="e" delay="900" /><texture file="h" delay="500" /></textureani>""");
        Assert.That(new[] { 3500, 3501, 4400, 4401, 4900, 4901, 8400, 8401 }.Select(loop0.FrameAt), Is.EqualTo(new[] { 0, 1, 1, 2, 2, 0, 0, 1 }));

        // a loop frame after the first: the first frame plays once
        var loop1 = Anim("""<textureani><texture file="a" delay="100" /><texture file="b" delay="50" loop="1" /><texture file="c" delay="50" /></textureani>""");
        Assert.That(new[] { 100, 101, 201, 251, 301 }.Select(loop1.FrameAt), Is.EqualTo(new[] { 0, 1, 1, 2, 1 }));

        // a loop of zero length stays on the loop frame
        var zero = Anim("""<textureani><texture file="a" delay="100" /><texture file="b" loop="true" /></textureani>""");
        Assert.That(zero.FrameAt(500), Is.EqualTo(1));
    }

    static void ChecksItsLibrary(string variable)
    {
        using var packs = new PackSet(RcGap.Data(variable));
        var items = new EmotionTable(packs, EmotionTable.Item);

        // a face item's file imports a common group with its item code: emotion/item/10300001.xml, MaleCustom
        var face = items.Group(10300001);
        Assert.That(face, Is.Not.Null);
        Assert.That(face.ItemCode, Is.EqualTo("00300001"));
        Assert.That(face.Names, Is.Not.Empty);
        Assert.That(face.Find("ANGRY"), Is.SameAs(face.Find("angry")).And.Not.Null, "a name in any case");
        Assert.That(face.Find("no such emotion"), Is.Null);
        Assert.That(items.Group(99999999), Is.Null, "no file: no group");
        Assert.That(items.Group(10300001), Is.SameAs(face), "read once");

        // femalecustom angry: anim, the FA texture of one frame, one effect, equiphide
        var female = items.CommonGroup("FemaleCustom");
        Assert.That(female, Is.Not.Null);
        var angry = female.Find("angry");
        Assert.That(angry.Animation(0), Is.EqualTo("Emotion_Angry_A"));
        Assert.That(angry.Animation(5), Is.Empty);
        Assert.That(angry.EquipHide, Is.True);
        Assert.That(angry.Texture.Target, Is.EqualTo("FA"));
        Assert.That(angry.Texture.Frames[0].BaseEntry(""), Is.EqualTo("item_face/fc_face_angry.dds"));
        Assert.That(angry.Effects, Is.EqualTo(new[] { "CH/Emotion/Eff_Female_Angry.xml" }));

        // every frame of every common group names a texture the textures archive holds (with the {0} frames left out)
        int frames = 0;
        var missing = new List<string>();
        foreach (var name in new[] { "FemaleCustom", "MaleCustom" })
            foreach (var n in items.CommonGroup(name).Names)
                foreach (var f in items.CommonGroup(name).Find(n).Texture?.Frames ?? Array.Empty<EmotionTextureFrame>())
                {
                    if (f.File.Contains("{0}")) continue;
                    frames++;
                    if (!packs.Exists(EmotionTextureFrame.Archive, f.BaseEntry(""))) missing.Add($"{name}/{n}: {f.File}");
                }
        TestContext.WriteLine($"{variable}: {frames} frames, not in the archive: {(missing.Count == 0 ? "none" : string.Join(", ", missing.Distinct()))}");
        Assert.That(frames, Is.GreaterThan(20));
        Assert.That(missing, Is.Empty);

        // an npc's own file: 02020001 (orange mushroom), its default face loops from the first frame
        var npc = new EmotionTable(packs, EmotionTable.Npc).Group(2020001);
        Assert.That(npc?.Find("default")?.Texture, Is.Not.Null);
        Assert.That(npc.Find("default").Texture.LoopFrame, Is.Zero);
        Assert.That(npc.ItemCode, Is.Empty);
    }

    /// <summary>Every face item's group (emotion/item/*.xml) and the frames its item code names ({0}, the folder
    /// CTextureAnimation takes from CEmotionGroup's item code): the base and control textures, as "item: entry".</summary>
    static void ChecksEveryFaceItemsFrames(string variable)
    {
        using var packs = new PackSet(RcGap.Data(variable));
        var items = new EmotionTable(packs, EmotionTable.Item);
        var seen = new HashSet<string>();
        var missing = new List<string>();
        int groups = 0;
        foreach (var entry in packs.List("Xml").Where(e => e.StartsWith("emotion/item/", StringComparison.Ordinal) && e.EndsWith(".xml", StringComparison.Ordinal)))
        {
            if (!uint.TryParse(Path.GetFileNameWithoutExtension(entry), out uint id) || items.Group(id) is not { } g) continue;
            groups++;
            foreach (var n in g.Names)
                foreach (var f in g.Find(n).Texture?.Frames ?? Array.Empty<EmotionTextureFrame>())
                    foreach (var e in new[] { f.BaseEntry(g.ItemCode), f.ControlEntry(g.ItemCode) })
                        if (e.Length != 0 && seen.Add(e) && !packs.Exists(EmotionTextureFrame.Archive, e)) missing.Add($"{id}: {e}");
        }
        TestContext.WriteLine($"{variable}: {groups} face item groups, {seen.Count} textures, not in the archive: {(missing.Count == 0 ? "none" : string.Join(", ", missing))}");
        Assert.That(groups, Is.GreaterThan(100));
        Assert.That(missing, Is.Empty);
    }

    [Test]
    public void EveryKmsFaceItemsFramesAreInTheArchive() => ChecksEveryFaceItemsFrames("MS2_DATA");

    [Test]
    public void EveryGms2FaceItemsFramesAreInTheArchive() => ChecksEveryFaceItemsFrames("MS2_GMS2_DATA");

    [Test]
    public void TheKmsLibraryReadsTheFaceItemsCommonGroupsAndNpcs() => ChecksItsLibrary("MS2_DATA");

    [Test]
    public void TheGms2LibraryReadsTheFaceItemsCommonGroupsAndNpcs() => ChecksItsLibrary("MS2_GMS2_DATA");
}
