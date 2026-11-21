using System.Globalization;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2.Ui.ChatEmoticons;

/// <summary>ChatEmoticonCategoryData (chatemoticontable.cpp:222): a category's id and its tab icon path.</summary>
public sealed class ChatEmoticonCategory
{
    public uint Id;
    public string IconPath = "";
}

/// <summary>ChatEmoticonData (chatemoticontable.cpp:143-149, 186-187): one emoticon of a pack.</summary>
public sealed class ChatEmoticon
{
    public uint Id, CategoryId, GroupId;
    public string ImagePath = "", Name = "", Command = "";
    public int Height, Width, VSpace = -3;
}

/// <summary>
/// ChatEmoticonCachedData (@0x1401e5910, commonfiles/chatemoticontable.cpp): table/chatEmoticonCategory.xml
/// (LoadCategories @0x1401e6570: &lt;chatEmoticonCategory id icon_path&gt;), table/chatEmoticon.xml (LoadEmoticons @0x1401e5980:
/// &lt;chatEmoticon id category_id group_id image_path height width vspace&gt;, vspace -3 when absent), then the string file
/// stringChatEmoticon.xml of the string folder (LoadStrings @0x1401e5f70: &lt;key id name command&gt;; a row of an unknown
/// emoticon is skipped; a non-empty command becomes ":command:" and is indexed by it). Rows are picked by FUN_14028faa0
/// (FeatureElements.Collect keyed by id). The category file is read first and the emoticon file's category is checked by
/// Validate (@0x1401e6a40: an emoticon whose category is unknown is logged). STILL-OPEN: the decompiled Load returns 0 on a
/// true Validate and 1 on a false one, which is inverted against Validate's own logging; the tables are kept whole here.
/// </summary>
public sealed class ChatEmoticonTable
{
    readonly Dictionary<uint, ChatEmoticon> emoticons = new();
    readonly Dictionary<uint, List<ChatEmoticon>> byGroup = new();
    readonly Dictionary<string, List<ChatEmoticon>> byCommand = new();
    readonly Dictionary<uint, ChatEmoticonCategory> categories = new();

    public IEnumerable<ChatEmoticon> Emoticons => emoticons.Values;
    public IEnumerable<ChatEmoticonCategory> Categories => categories.Values;

    public ChatEmoticon GetEmoticon(uint id) => emoticons.TryGetValue(id, out var e) ? e : null;
    public ChatEmoticonCategory GetCategory(uint id) => categories.TryGetValue(id, out var c) ? c : null;
    /// <summary>The group's emoticons in file order; empty when none.</summary>
    public IReadOnlyList<ChatEmoticon> GetEmoticonsByGroup(uint group) => byGroup.TryGetValue(group, out var l) ? l : Array.Empty<ChatEmoticon>();
    public IReadOnlyList<ChatEmoticon> GetEmoticonsByCommand(string command) => byCommand.TryGetValue(command ?? "", out var l) ? l : Array.Empty<ChatEmoticon>();

    /// <summary>Emoticons whose category is not in the category table (what Validate logs).</summary>
    public IEnumerable<ChatEmoticon> InvalidCategory() => emoticons.Values.Where(e => !categories.ContainsKey(e.CategoryId));

    static uint UInt(System.Xml.Linq.XElement e, string name) => unchecked((uint)ReservedWords.Atoi((string)e.Attribute(name) ?? "0"));
    static int Int(System.Xml.Linq.XElement e, string name, int def) =>
        e.Attribute(name) == null ? def : ReservedWords.Atoi((string)e.Attribute(name));

    public static ChatEmoticonTable Load(PackSet packs, IEnumerable<string> languages)
    {
        var t = new ChatEmoticonTable();
        var cats = Root(packs, "table/chatemoticoncategory.xml");
        if (cats == null) return t;
        foreach (var e in FeatureElements.Collect(packs, cats, "chatEmoticonCategory", "id"))
        {
            var c = new ChatEmoticonCategory { Id = UInt(e, "id"), IconPath = (string)e.Attribute("icon_path") ?? "" };
            t.categories[c.Id] = c;
        }
        var ems = Root(packs, "table/chatemoticon.xml");
        if (ems == null) return t;
        foreach (var e in FeatureElements.Collect(packs, ems, "chatEmoticon", "id"))
        {
            var m = new ChatEmoticon
            {
                Id = UInt(e, "id"), CategoryId = UInt(e, "category_id"), GroupId = UInt(e, "group_id"),
                ImagePath = (string)e.Attribute("image_path") ?? "",
                Height = Int(e, "height", 0), Width = Int(e, "width", 0), VSpace = Int(e, "vspace", -3),
            };
            t.emoticons[m.Id] = m;
            if (!t.byGroup.TryGetValue(m.GroupId, out var l)) t.byGroup[m.GroupId] = l = new List<ChatEmoticon>();
            l.Add(m);
        }
        foreach (var lang in languages ?? Array.Empty<string>())
        {
            var strs = Root(packs, $"string/{lang}/stringchatemoticon.xml");
            if (strs == null) continue;      // the client loads one language's file
            foreach (var e in FeatureElements.Collect(packs, strs, "key", "id"))
            {
                if (!t.emoticons.TryGetValue(UInt(e, "id"), out var m)) continue;
                m.Name = (string)e.Attribute("name") ?? "";
                m.Command = (string)e.Attribute("command") ?? "";
                if (m.Command.Length == 0) continue;
                m.Command = ":" + m.Command + ":";
                if (!t.byCommand.TryGetValue(m.Command, out var l)) t.byCommand[m.Command] = l = new List<ChatEmoticon>();
                l.Add(m);
            }
            break;
        }
        return t;
    }

    static System.Xml.Linq.XElement Root(PackSet packs, string entry)
    {
        if (!packs.Exists("Xml", entry)) return null;
        var root = GameStrings.Parse(packs.Read("Xml", entry)).Root;
        return root != null && root.Name.LocalName == "ms2" ? root : null;
    }
}
