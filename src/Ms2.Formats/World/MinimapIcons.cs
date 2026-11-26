using System.Xml.Linq;
using Ms2.Formats.Pack;

namespace Ms2.Formats.World;

/// <summary>
/// What the client's minimap (UIMinimapEntity / UIMinimapDialog) puts on the map for a field's portals and npcs, from the
/// player's data. Each entity has an icon (the movie class "minimap_entity_" + icon), a frame, a layer and an optional label.
/// - Npcs (SetFromNpcData): no icon unless the npc is on the minimap (environment minimap, default on). The icon is
///   minimapIconName, else event_boss / elite_boss / boss / gatekeeper by type, else the icon of the npc's function kind,
///   else friendly or enemy. They have no label, only a tooltip.
/// - Portals (SetFromPortal / UpdatePortal): a portal to the field itself is left out. The icon is event_portal (type 4),
///   dungeon (type 8) or portal. The label is the target map's name. The portal shows when MinimapIconVisible is set and the
///   portal is shown. A portal into an indoor field instead shows the icon of that field's first npc with a function
///   (FieldMetaData npc list, a function kind icon and an npcFunctionName), still labelled with the map name.
/// </summary>
public sealed class MinimapIcons
{
    public sealed record Entity(string Icon, string Frame, string Layer, string Label, bool Visible, bool InFieldNpc);

    readonly IReadOnlyDictionary<int, List<int>> fieldNpcs;
    readonly HashSet<int> indoor = new();
    readonly ICollection<int> taxiFields;
    readonly Dictionary<string, string> mapNames, functionNames;
    readonly NpcTable npcs;

    /// <summary>GetNpcMovieName: the icon of an npc function kind (UIEmoticon's npc movie names).</summary>
    static readonly Dictionary<int, string> KindIcons = new()
    {
        [2] = "emoticon_movie_npc_warehouse", [3] = "emoticon_movie_npc_mail", [11] = "emoticon_movie_npc_equip",
        [12] = "emoticon_movie_npc_equip", [13] = "emoticon_movie_npc_shopetc", [14] = "emoticon_movie_npc_ugc",
        [15] = "emoticon_movie_npc_shopetc", [80] = "emoticon_movie_npc_headhunter", [81] = "emoticon_movie_npc_doctor",
        [82] = "emoticon_movie_npc_trans", [83] = "emoticon_movie_npc_container", [84] = "emoticon_movie_npc_landagent",
    };

    /// <summary>
    /// <paramref name="fieldNpcs"/> and <paramref name="taxiFields"/> are CFieldMetaTable's m_fieldNpcs (field -> its npcs, in
    /// file order) and m_taxi fields: the caller passes the field meta table it already holds, so the 1.8 MB
    /// fieldmetadata.xml is not read a second time here. The indoor fields come from the shared FieldData document.
    /// </summary>
    public MinimapIcons(PackSet packs, NpcTable npcs, GameStrings text, IReadOnlyDictionary<int, List<int>> fieldNpcs, ICollection<int> taxiFields)
    {
        this.npcs = npcs;
        this.fieldNpcs = fieldNpcs;
        this.taxiFields = taxiFields;
        mapNames = text.Table("mapname", "name");
        functionNames = text.Table("npcfunctionname", "name");
        // CFieldData: <indoor type="1|2"/> makes a field indoor (mask 2 or 4)
        if (FieldData.Exists(packs))
            foreach (var fd in FieldData.Document(packs).Root!.Elements("fieldData"))
            {
                if (!int.TryParse((string)fd.Attribute("id"), out int fid)) continue;
                var env = fd.Elements("environment").FirstOrDefault(e => e.Attribute("locale") == null) ?? fd.Element("environment");
                var ind = env?.Element("indoor") ?? fd.Element("indoor");
                if (ind != null && (string)ind.Attribute("type") is "1" or "2") indoor.Add(fid);
            }
    }

    public bool IsIndoor(int fieldId) => indoor.Contains(fieldId);

    public string MapName(int fieldId) => Lookup(mapNames, fieldId);

    static string Lookup(Dictionary<string, string> d, int id) =>
        d.TryGetValue(id.ToString(), out var s) || d.TryGetValue(id.ToString("D8"), out s) ? s : null;

    /// <summary>UIMinimapEntity::GetNpcIcon ("empty" when the npc is not on the minimap).</summary>
    public static string NpcIcon(NpcRow r)
    {
        if (!r.Minimap) return "empty";
        if (!string.IsNullOrEmpty(r.MinimapIcon)) return r.MinimapIcon;
        if (r.HasTag("event_icon")) return "event_boss";      // HasPropertyTag(0xd): tag 13 is event_icon (npcdata.cpp:1355)
        int type = r.NpcType;
        if (type == 0 && r.IsBoss) return r.Class is 3 or 7 ? "elite_boss" : "boss";
        if (type == 0x55) return "gatekeeper";
        if (KindIcons.TryGetValue(r.Kind, out var k)) return k;
        return r.Friendly > 0 ? "friendly" : "enemy";
    }

    /// <summary>GetStateForIcon / GetLayerForNpc.</summary>
    public static string NpcFrame(string icon) => icon is "elite_boss" or "boss" or "event_boss" ? "alive" : "";

    public static string NpcLayer(NpcRow r, string icon) =>
        r.NpcType switch { 1 => "shop", 0x55 => "interact", _ => icon == "friendly" ? "friendly" : "npc" };

    /// <summary>A spawned npc (UpdateNpc without quests).</summary>
    public static Entity Npc(NpcRow r)
    {
        var icon = NpcIcon(r);
        return new Entity(icon, NpcFrame(icon), NpcLayer(r, icon), null, icon != "empty", false);
    }

    /// <summary>HasNpcMinimapInfo: a function kind with an icon and a function name.</summary>
    public bool HasNpcMinimapInfo(NpcRow r) =>
        r.Kind != 0 && KindIcons.ContainsKey(r.Kind) && !string.IsNullOrEmpty(Lookup(functionNames, r.Id));

    /// <summary>FindPortalNpc without quests: the first npc of the field with minimap info.</summary>
    public NpcRow PortalNpc(int targetField)
    {
        if (!fieldNpcs.TryGetValue(targetField, out var list)) return null;
        foreach (var id in list)
            if (npcs.Find(id) is { } r && HasNpcMinimapInfo(r)) return r;
        return null;
    }

    /// <summary>CTaxiManager::FUN_14085f6e0: the field has a taxi station (fieldmetadata taxiStation), so BuildTaxi runs.</summary>
    public bool HasTaxi(int fieldId) => taxiFields.Contains(fieldId);

    /// <summary>
    /// UIMinimapEntityTaxi::Initialize: frame enabled when the field is in the server's found-taxi list, else disabled; icon
    /// taxibest when the player's level reaches TaxiCallBestDriverLevel (FUN_141b71c00), else taxi; layer taxi. Its name
    /// (s_word_taxistation) is only the tooltip: showtext stays off. The home-privilege branch (IsHomePrivilege) is not read.
    /// </summary>
    public static Entity Taxi(bool found, bool best) => new(best ? "taxibest" : "taxi", found ? "enabled" : "disabled", "taxi", null, true, false);

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PackSet, Dictionary<int, (string icon, int type)>> interactRows = new();

    /// <summary>The interact object row (table/interactobject.xml): its display minimapIcon ("" when none) and type.</summary>
    public (string icon, int type) InteractRow(PackSet packs, int interactId) => InteractRowOf(packs, interactId);

    /// <summary><see cref="InteractRow"/> without a minimap: the same table, read once per pack set.</summary>
    public static (string icon, int type) InteractRowOf(PackSet packs, int interactId) =>
        interactRows.GetValue(packs, InteractRows).GetValueOrDefault(interactId, ("", 0));

    /// <summary>
    /// Each &lt;interact&gt; row's id, type and first &lt;display&gt; child's minimapIcon (the first row of an id wins), read
    /// once per pack set. The table is 5-6 MB of about 3600 rows with 25 children each and only these three values are
    /// read, so it is streamed (<see cref="Text.XmlRows"/>, the text and settings XDocument.Parse uses) instead of parsed
    /// into a tree.
    /// </summary>
    public static Dictionary<int, (string icon, int type)> InteractRows(PackSet packs)
    {
        var rows = new Dictionary<int, (string, int)>();
        const string entry = "table/interactobject.xml";
        if (!packs.Exists("Xml", entry)) return rows;
        foreach (var r in Text.XmlRows.Elements(packs.Read("Xml", entry), "interact", rootChildren: true))
        {
            bool ok = int.TryParse(r.GetAttribute("id"), out int id);
            int.TryParse(r.GetAttribute("type"), out int type);
            string icon = null;
            if (!r.IsEmptyElement)
            {
                using var sub = r.ReadSubtree();
                sub.Read();
                while (sub.Read())
                    if (sub.NodeType == System.Xml.XmlNodeType.Element && sub.Depth == 1 && sub.LocalName == "display" && sub.NamespaceURI.Length == 0)
                    {
                        icon = sub.GetAttribute("minimapIcon") ?? "";
                        break;
                    }
            }
            if (ok) rows.TryAdd(id, (icon ?? "", type));
        }
        return rows;
    }

    /// <summary>BuildMapObjects: a world object tagged MS2Minimap; icon and frame = its MinimapIconFrame lower-cased, layer interact.</summary>
    public static Entity MapObject(string iconFrame)
    {
        var icon = (iconFrame ?? "").ToLowerInvariant();
        return new Entity(icon, icon, "interact", null, true, false);
    }

    /// <summary>CPortal::FUN_14169c5f0: the portal icon by type (the dungeon room's boss and time-up variants are not read).</summary>
    public static string PortalIcon(int portalType) => portalType switch { 4 => "event_portal", 8 => "dungeon", _ => "portal" };

    /// <summary>
    /// UpdatePortal(init) for a portal of <paramref name="fieldId"/>: null for a portal into the field itself.
    /// <paramref name="shown"/> is the portal's shown state (after the quest masks); <paramref name="minimapVisible"/> its
    /// MinimapIconVisible.
    /// </summary>
    public Entity Portal(int fieldId, int targetField, int portalType, bool shown, bool minimapVisible)
    {
        if (targetField == fieldId) return null;
        bool visible = minimapVisible && shown;
        string label = targetField > 0 ? MapName(targetField) : null;
        if (targetField > 0 && IsIndoor(targetField) && portalType != 4 && PortalNpc(targetField) is { } npc)
        {
            var icon = NpcIcon(npc);     // FUN_141a44c00: the npc's icon, frame = icon, layer npc, label shown
            return new Entity(icon, icon, "npc", label, visible, true);
        }
        return new Entity(PortalIcon(portalType), "", "portal", label, visible, false);
    }
}
