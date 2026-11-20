using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;

namespace Ms2.Formats.World;

/// <summary>
/// One &lt;interact&gt; row of Table/InteractObject.xml and Table/InteractObject_mastery.xml
/// (CInteractObjectData, cinteractobjectdata.h; loader CInteractObjectData::Load @0x14051ce70, interactobjectdata.cpp:135-392).
/// </summary>
public sealed class InteractObjectRow
{
    public int Id;
    /// <summary>1 Mesh, 2 Telescope, 3 UI, 4 Web, 5 DisplayImage, 6 Gathering, 7 GuildPoster, 8 BillBoard, 9 WatchTower.</summary>
    public int Type;
    public int Collection;
    public uint ReactCount;

    // <mesh>
    public int NormalCollision = 1, ReactableCollision = 1;
    public Vec3 ShapeDimension = new(150, 150, 150);
    public string ReactObjectAni = "";

    // <ui>
    public string UiSwf = "", UiValue = "";

    // <gathering>
    public int GatheringRecipeId;

    // <validate>
    public int FieldId;
    public float InteractionDistance;

    // <quest>
    public QuestMaskTable.Mask MaskUse = new();

    // <item>
    public uint ItemCode, ItemConsume;
    public int ItemRank, ItemCheckCount;

    // <conditionAdditionalEffect>
    public (uint Id, ushort Level)[] ConditionEffects = Array.Empty<(uint, ushort)>();

    // <time> (ms)
    public uint ResetTimeMs, ReactTimeMs, HideTimeMs;

    // <user>
    public string[] ReactAnis = Array.Empty<string>();
    public string SuccessAni = "", FailAni = "";

    // <effect>
    public string EffectNormal = "", EffectReactable = "", EffectSuccess = "", EffectInteraction = "";
    public QuestMaskTable.Mask MaskNormal = new(), MaskReactable = new(), MaskInteraction = new();

    // <room>, <homePrivilege>, <arena>, <display>, <mastery>
    public int PartyPortal, HomePrivilegeCheck, CanInteractTeamId;
    public string MinimapIcon = "";
    public int DisplayNotice;
    public int MasteryType, MasteryValue;
}

/// <summary>
/// CInteractObjectData::LoadAll (@0x14051f2b0, interactobjectdata.cpp:404-438): loads Table/InteractObject.xml and
/// Table/InteractObject_mastery.xml, keyed by id. Streamed via <see cref="XmlRows"/> and cached once per PackSet.
/// </summary>
public sealed class InteractObjectTable
{
    static readonly ConditionalWeakTable<PackSet, InteractObjectTable> cache = new();
    readonly Dictionary<int, InteractObjectRow> rows;

    public InteractObjectTable(Dictionary<int, InteractObjectRow> rows) => this.rows = rows;

    public int Count => rows.Count;
    public InteractObjectRow Get(int id) => rows.TryGetValue(id, out var r) ? r : null;

    /// <summary>Shared table for a PackSet (loaded once from table/interactobject.xml and table/interactobject_mastery.xml).</summary>
    public static InteractObjectTable Shared(PackSet packs) => cache.GetValue(packs, Load);

    public static InteractObjectTable Load(PackSet packs)
    {
        var map = new Dictionary<int, InteractObjectRow>();
        LoadFile(packs, "table/interactobject.xml", map);
        LoadFile(packs, "table/interactobject_mastery.xml", map);
        return new InteractObjectTable(map);
    }

    static void LoadFile(PackSet packs, string entry, Dictionary<int, InteractObjectRow> map)
    {
        if (!packs.Exists("Xml", entry)) return;
        Parse(packs.Read("Xml", entry), map);
    }

    /// <summary>Parses &lt;interact&gt; rows from an XML buffer into <paramref name="map"/> (later duplicate id overwrites,
    /// matching CAtlMap::SetAt at interactobjectdata.cpp:434).</summary>
    public static void Parse(byte[] xml, Dictionary<int, InteractObjectRow> map)
    {
        foreach (var r in XmlRows.Elements(xml, "interact", rootChildren: true))
        {
            int id = Int(r.GetAttribute("id"));
            int type = Int(r.GetAttribute("type"));
            if (type == 0) continue;   // interactobjectdata.cpp:141: m_eType == 0 rejects the row
            var row = new InteractObjectRow
            {
                Id = id,
                Type = type,
                Collection = Int(r.GetAttribute("collection")),
                ReactCount = UInt(r.GetAttribute("reactCount")),
            };
            if (!r.IsEmptyElement)
            {
                using var sub = r.ReadSubtree();
                sub.Read();
                while (sub.Read())
                {
                    if (sub.NodeType != XmlNodeType.Element || sub.NamespaceURI.Length != 0) continue;
                    if (sub.Depth == 1)
                    {
                        switch (sub.LocalName)
                        {
                            case "mesh":
                                if (sub.GetAttribute("normalCollision") is { } nc) row.NormalCollision = Int(nc, 1);
                                if (sub.GetAttribute("reactableCollision") is { } rc) row.ReactableCollision = Int(rc, 1);
                                row.ReactObjectAni = sub.GetAttribute("reactObjectAni") ?? "";
                                if (sub.GetAttribute("shapeDimension") is { Length: > 0 } sd) row.ShapeDimension = ParseVec3(sd, row.ShapeDimension);
                                break;
                            case "ui":
                                row.UiSwf = sub.GetAttribute("swf") ?? "";
                                row.UiValue = sub.GetAttribute("value") ?? "";
                                break;
                            case "gathering":
                                row.GatheringRecipeId = Int(sub.GetAttribute("receipeID"));
                                break;
                            case "validate":
                                row.FieldId = Int(sub.GetAttribute("fieldID"));
                                row.InteractionDistance = Float(sub.GetAttribute("interactionDistance"));
                                break;
                            case "quest":
                                row.MaskUse = QuestMaskTable.FromEntity(sub.GetAttribute("maskQuestID"), sub.GetAttribute("maskQuestState"));
                                break;
                            case "item":
                                row.ItemCode = UInt(sub.GetAttribute("code"));
                                row.ItemRank = Int(sub.GetAttribute("rank"));
                                row.ItemConsume = UInt(sub.GetAttribute("consume"));
                                row.ItemCheckCount = Int(sub.GetAttribute("checkCount"));
                                break;
                            case "conditionAdditionalEffect":
                                row.ConditionEffects = ParseConditionEffects(sub.GetAttribute("id"), sub.GetAttribute("level"));
                                break;
                            case "time":
                                row.ResetTimeMs = UInt(sub.GetAttribute("resetTime"));
                                row.ReactTimeMs = UInt(sub.GetAttribute("reactTime"));
                                row.HideTimeMs = UInt(sub.GetAttribute("hideTime"));
                                break;
                            case "user":
                                row.ReactAnis = SplitTokens(sub.GetAttribute("reactAni"));
                                row.SuccessAni = sub.GetAttribute("successAni") ?? "";
                                row.FailAni = sub.GetAttribute("failAni") ?? "";
                                break;
                            case "effect":
                                row.EffectNormal = sub.GetAttribute("normal") ?? "";
                                row.EffectReactable = sub.GetAttribute("reactable") ?? "";
                                row.EffectSuccess = sub.GetAttribute("success") ?? "";
                                row.EffectInteraction = sub.GetAttribute("interaction") ?? "";
                                break;
                            case "room":
                                row.PartyPortal = Int(sub.GetAttribute("partyPortal"));
                                break;
                            case "homePrivilege":
                                row.HomePrivilegeCheck = Int(sub.GetAttribute("check"));
                                break;
                            case "display":
                                row.MinimapIcon = sub.GetAttribute("minimapIcon") ?? "";
                                row.DisplayNotice = BoolOrInt(sub.GetAttribute("notice"));
                                break;
                            case "arena":
                                row.CanInteractTeamId = Int(sub.GetAttribute("canInteractTeamId"));
                                break;
                            case "mastery":
                                row.MasteryType = Int(sub.GetAttribute("type"));
                                row.MasteryValue = Int(sub.GetAttribute("value"));
                                break;
                        }
                    }
                    else if (sub.Depth == 2)
                    {
                        switch (sub.LocalName)
                        {
                            case "maskNormal":
                                row.MaskNormal = QuestMaskTable.FromEntity(sub.GetAttribute("maskQuestID"), sub.GetAttribute("maskQuestState"));
                                break;
                            case "maskReactable":
                                row.MaskReactable = QuestMaskTable.FromEntity(sub.GetAttribute("maskQuestID"), sub.GetAttribute("maskQuestState"));
                                break;
                            case "maskInteraction":
                                row.MaskInteraction = QuestMaskTable.FromEntity(sub.GetAttribute("maskQuestID"), sub.GetAttribute("maskQuestState"));
                                break;
                        }
                    }
                }
            }
            map[row.Id] = row;
        }
    }

    static int Int(string s, int def = 0) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : def;
    static uint UInt(string s, uint def = 0) => uint.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint v) ? v : def;
    static float Float(string s, float def = 0) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : def;
    static int BoolOrInt(string s) => string.IsNullOrEmpty(s) ? 0 : s.Equals("true", StringComparison.OrdinalIgnoreCase) ? 1 : Int(s);

    static string[] SplitTokens(string s) =>
        string.IsNullOrWhiteSpace(s) ? Array.Empty<string>() : s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    static Vec3 ParseVec3(string s, Vec3 def)
    {
        var p = s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return p.Length >= 3 ? new Vec3(Float(p[0]), Float(p[1]), Float(p[2])) : def;
    }

    static (uint, ushort)[] ParseConditionEffects(string idStr, string levelStr)
    {
        if (string.IsNullOrWhiteSpace(idStr) || idStr.Trim() == "0") return Array.Empty<(uint, ushort)>();
        var ids = SplitTokens(idStr);
        var levels = SplitTokens(levelStr);
        if (ids.Length != levels.Length || ids.Length == 0) return Array.Empty<(uint, ushort)>();
        var res = new (uint, ushort)[ids.Length];
        for (int i = 0; i < ids.Length; i++) res[i] = (UInt(ids[i]), (ushort)Int(levels[i]));
        return res;
    }
}
