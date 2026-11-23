using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Ui;

namespace Ms2.Formats.World;

/// <summary>
/// One texture frame of a <see cref="EmotionTextureAnimation"/> (Unk_TextureFrame::Load @0x141847560,
/// textureanimationdata.cpp:90-96): the base texture file, the control texture file and the delay in ms.
/// </summary>
public sealed record EmotionTextureFrame(string File, string Control, int Delay)
{
    /// <summary>The archive of Data/Resource/Model/Textures.</summary>
    public const string Archive = "Resource/Model/Textures";
    const string DataPrefix = "./Data/Resource/Model/Textures/";

    /// <summary>GetBaseTexturePath (@0x141847960, :131-142) as an entry of <see cref="Archive"/> (lower case): the file
    /// under Data/Resource/Model/Textures/, with "{0}" replaced by <paramref name="dir"/> when that is set. A file
    /// starting with "." is a path of its own: under the textures folder it is that entry, elsewhere it is returned as
    /// it is.</summary>
    public string BaseEntry(string dir) => Entry(File, dir);

    /// <summary>GetControlTexturePath (@0x141847a60, :145-158): as <see cref="BaseEntry"/> for the control file; "" without one.</summary>
    public string ControlEntry(string dir) => Control.Length == 0 ? "" : Entry(Control, dir);

    static string Entry(string file, string dir)
    {
        if (file.Length != 0 && file[0] == '.')
            return file.StartsWith(DataPrefix, StringComparison.OrdinalIgnoreCase) ? file[DataPrefix.Length..].Replace('\\', '/').ToLowerInvariant() : file;
        string f = string.IsNullOrEmpty(dir) ? file : file.Replace("{0}", dir);
        return f.Replace('\\', '/').ToLowerInvariant();
    }
}

/// <summary>
/// CTextureAnimationData (textureanimationdata.cpp:175-236): the &lt;textureani target&gt; of an emotion, its frames
/// and the frame it loops back to.
/// </summary>
public sealed class EmotionTextureAnimation
{
    /// <summary>The node whose texture changes ("target"; FUN_141847e30 falls back to "FA", DAT_143ae51d8,
    /// worldfadeoutrenderstep.cpp:278).</summary>
    public string Target { get; private init; } = "FA";

    /// <summary>The frame a "loop" texture marks (the last one so marked), -1 when none: the animation stays on its last
    /// frame after it ends.</summary>
    public int LoopFrame { get; private init; } = -1;

    public IReadOnlyList<EmotionTextureFrame> Frames { get; private init; } = Array.Empty<EmotionTextureFrame>();

    /// <summary>FUN_141847e30 (@0x141847e30, :218-236): the target and the &lt;texture file control delay loop&gt;
    /// frames; null without a frame (the emotion keeps no texture animation).</summary>
    public static EmotionTextureAnimation Read(XElement e)
    {
        var target = (string)e.Attribute("target") ?? "";
        var frames = new List<EmotionTextureFrame>();
        int loop = -1;
        foreach (var t in e.Elements("texture"))
        {
            frames.Add(new EmotionTextureFrame((string)t.Attribute("file") ?? "", (string)t.Attribute("control") ?? "",
                EmotionTable.Int(t, "delay")));
            if (EmotionTable.Flag(t, "loop")) loop = frames.Count - 1;
        }
        if (frames.Count == 0) return null;
        return new EmotionTextureAnimation { Target = target.Length != 0 ? target : "FA", LoopFrame = loop, Frames = frames };
    }

    /// <summary>
    /// FUN_141847c90 (@0x141847c90, :181-215): the frame shown <paramref name="timeMs"/> after the start. Frames take
    /// their delays in turn (a time equal to a frame's end is still that frame); past the last one the frame count
    /// comes back without a loop frame, and with one the time runs round the frames from the loop frame on.
    /// </summary>
    public int FrameAt(int timeMs)
    {
        int count = Frames.Count, index = 0, sum = 0, loopStart = 0;
        for (int i = 0; i < count; i++)
        {
            if (i == LoopFrame) loopStart = sum;
            sum += Frames[i].Delay;
            if (sum < timeMs) index++;
            else break;
        }
        if (index < count || LoopFrame == -1) return index;
        int loopLength = sum - loopStart;
        if (loopLength == 0) return LoopFrame;
        timeMs = (timeMs - loopStart) % loopLength;
        sum = 0;
        index = LoopFrame;
        for (int j = LoopFrame; j < count; j++)
        {
            sum += Frames[j].Delay;
            if (sum < timeMs) index++;
            else break;
        }
        return index;
    }
}

/// <summary>
/// CEmotionData (emotiondata.h, Load @0x1411bccd0, emotiondata.cpp:82-121): one &lt;emotion&gt;: its name, face name,
/// equiphide and isTool flags, the animations (&lt;anim&gt;, &lt;idleAnim&gt; and up to three &lt;boreAnim&gt; names
/// split at ","), the texture animation and the effect files.
/// </summary>
public sealed class EmotionData
{
    public string Name { get; private init; } = "";
    public string FaceName { get; private init; } = "";
    public bool EquipHide { get; private init; }
    public bool IsTool { get; private init; }
    string[] anims = { "", "", "", "", "" };
    public EmotionTextureAnimation Texture { get; private init; }
    public IReadOnlyList<string> Effects { get; private init; } = Array.Empty<string>();

    /// <summary>GetAnimation (@0x1411bd0c0): 0 anim, 1 idleAnim, 2-4 the bore animations; "" outside 0..4.</summary>
    public string Animation(int index) => index is >= 0 and < 5 ? anims[index] : "";

    /// <summary>Load: null when the name is empty or the element is not available (IsElementAvailable).</summary>
    public static EmotionData Read(PackSet packs, XElement e)
    {
        var name = (string)e.Attribute("name") ?? "";
        if (name.Length == 0 || !FeatureElements.IsAvailable(packs, e)) return null;
        var anims = new[] { ChildName(e, "anim"), ChildName(e, "idleAnim"), "", "", "" };
        var bore = ChildName(e, "boreAnim");
        if (bore.Length != 0)
        {
            var parts = bore.Split(',');
            for (int i = 0; i < parts.Length && i < 3; i++) anims[2 + i] = parts[i];
        }
        var effects = new List<string>();
        foreach (var f in e.Elements("effect")) effects.Add((string)f.Attribute("file") ?? "");
        return new EmotionData
        {
            Name = name,
            FaceName = (string)e.Attribute("faceName") ?? "",
            EquipHide = EmotionTable.Bool(e, "equiphide"),
            IsTool = EmotionTable.Bool(e, "isTool"),
            anims = anims,
            Texture = e.Element("textureani") is { } t ? EmotionTextureAnimation.Read(t) : null,
            Effects = effects,
        };
    }

    /// <summary>XmlGetChildAttr: the first child's attribute, "" without one.</summary>
    static string ChildName(XElement e, string child) => (string)e.Element(child)?.Attribute("name") ?? "";
}

/// <summary>
/// CEmotionGroup (emotiondata.cpp:162-226) and CEmotionGroupImport (:228-251): the emotions of one model by name (the
/// name in any case, CStringElementTraitsI); an imported group finds in the common group it names and carries the
/// file's item code.
/// </summary>
public sealed class EmotionGroup
{
    readonly Dictionary<string, EmotionData> emotions;

    /// <summary>GetItemCode: the import's "itemCode", "" for a group of its own.</summary>
    public string ItemCode { get; }

    EmotionGroup(Dictionary<string, EmotionData> emotions, string itemCode)
    {
        this.emotions = emotions;
        ItemCode = itemCode;
    }

    /// <summary>Load (@0x1411bd5c0, :167-176): the available &lt;emotion&gt; elements one per name
    /// (<see cref="FeatureElements.Collect(PackSet, XElement, string, string)"/>, FUN_14028faa0), each that loads set
    /// under its name (CAtlMap::SetAt: a later one of the same name in another case wins).</summary>
    internal static EmotionGroup Read(PackSet packs, XElement root)
    {
        var map = new Dictionary<string, EmotionData>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in FeatureElements.Collect(packs, root, "emotion", "name"))
            if (EmotionData.Read(packs, e) is { } d) map[d.Name] = d;
        return new EmotionGroup(map, "");
    }

    /// <summary>The CEmotionGroupImport of a common group: its emotions, the file's item code.</summary>
    internal static EmotionGroup Import(EmotionGroup common, string itemCode) => new(common.emotions, itemCode);

    /// <summary>Find (@0x1411bd920 / the import's @0x1411bdb80): null when the group has no such emotion.</summary>
    public EmotionData Find(string name) => name != null && emotions.TryGetValue(name, out var d) ? d : null;

    /// <summary>GetNames: the emotion names.</summary>
    public IEnumerable<string> Names => emotions.Keys;
}

/// <summary>
/// CEmotionTable (emotiondata.cpp:254-410): one emotion library, "Item" (the face items' groups) or "Npc", read from
/// Xml.m2d emotion/&lt;type&gt;/&lt;id, eight digits&gt;.xml on first use of an id, with the common groups of
/// emotion/common/&lt;name&gt;.xml that a file's &lt;import name itemCode&gt; points to. A file that is missing, has no
/// &lt;ms2&gt; root or imports an unknown group gives no group (null). The client's hot reload (the file caches) and
/// the Item library's load of every file at start (LoadAll, CEmotionManager::Init) are not modelled: a group reads the
/// same either way. STILL-OPEN: GetXmlPath("Emotion", ..) is read as the plain path; the data has no locale folder
/// under emotion/.
/// </summary>
public sealed class EmotionTable
{
    readonly PackSet packs;
    readonly string type;
    readonly Dictionary<uint, EmotionGroup> groups = new();
    Dictionary<string, EmotionGroup> common;

    /// <summary>The CEmotionManager libraries (@0x1411bf010): "Item" and "Npc".</summary>
    public const string Item = "Item", Npc = "Npc";

    public EmotionTable(PackSet packs, string type)
    {
        this.packs = packs;
        this.type = type;
    }

    /// <summary>The library's file of an id (ReadEmotionGroup: "%s/%08u.xml" under Emotion).</summary>
    public string Entry(uint id) => string.Format(CultureInfo.InvariantCulture, "emotion/{0}/{1:D8}.xml", type.ToLowerInvariant(), id);

    /// <summary>GetGroup (@0x1411bde10): the group of an id, read once; null when it has none.</summary>
    public EmotionGroup Group(uint id)
    {
        lock (groups)
        {
            if (!groups.TryGetValue(id, out var g)) groups[id] = g = ReadGroup(id);
            return g;
        }
    }

    /// <summary>ReadEmotionGroup (@0x1411be610, :349-385).</summary>
    EmotionGroup ReadGroup(uint id)
    {
        if (packs == null || !packs.Exists("Xml", Entry(id))) return null;
        XElement root;
        try { root = PackXml.Parse(packs.Read("Xml", Entry(id))).Root; }
        catch (System.Xml.XmlException) { return null; }
        if (root == null || root.Name != "ms2") return null;
        if (root.Element("import") is { } import)
        {
            var name = (string)import.Attribute("name") ?? "";
            return CommonGroup(name) is { } c ? EmotionGroup.Import(c, (string)import.Attribute("itemCode") ?? "") : null;
        }
        return EmotionGroup.Read(packs, root);
    }

    /// <summary>GetCommonGroup (@0x1411beb50): a common group by file name (any case), null when none.</summary>
    public EmotionGroup CommonGroup(string name)
    {
        lock (groups)
        {
            common ??= LoadCommon();
            return common.TryGetValue(name ?? "", out var g) ? g : null;
        }
    }

    /// <summary>LoadCommon (@0x1411bdf80, :288-313): every emotion/common/*.xml under its file name; the load stops at
    /// a file that fails to read or has no &lt;ms2&gt; root, keeping those read before it.</summary>
    Dictionary<string, EmotionGroup> LoadCommon()
    {
        var map = new Dictionary<string, EmotionGroup>(StringComparer.OrdinalIgnoreCase);
        if (packs == null) return map;
        foreach (var entry in packs.List("Xml").Where(e => e.StartsWith("emotion/common/", StringComparison.OrdinalIgnoreCase)
                     && e.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && e.IndexOf('/', 15) < 0).OrderBy(e => e, StringComparer.OrdinalIgnoreCase))
        {
            XElement root;
            try { root = PackXml.Parse(packs.Read("Xml", entry)).Root; }
            catch (System.Xml.XmlException) { break; }
            if (root == null || root.Name != "ms2") break;
            map[entry.Substring(15, entry.Length - 19)] = EmotionGroup.Read(packs, root);
        }
        return map;
    }

    /// <summary>XmlGetAttr of a bool (TiXmlElement::QueryBoolAttribute): "true", "yes" or "1" in any case; otherwise false.</summary>
    internal static bool Bool(XElement e, string name) => ((string)e.Attribute(name))?.ToLowerInvariant() is "true" or "yes" or "1";

    /// <summary>XmlGetBool: "1" or "true".</summary>
    internal static bool Flag(XElement e, string name) =>
        (string)e.Attribute(name) is { } v && (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));

    /// <summary>XmlGetAttr of an int: 0 when missing or not a number.</summary>
    internal static int Int(XElement e, string name) =>
        int.TryParse((string)e.Attribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;
}
