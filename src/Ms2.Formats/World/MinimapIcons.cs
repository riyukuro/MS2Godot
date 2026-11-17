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

    readonly Dictionary<int, List<int>> fieldNpcs = new();
    readonly HashSet<int> indoor = new();
    readonly HashSet<int> taxiFields = new();
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

    public MinimapIcons(PackSet packs, NpcTable npcs, GameStrings text)
    {
        this.npcs = npcs;
        mapNames = text.Table("mapname", "name");
        functionNames = text.Table("npcfunctionname", "name");
        // CFieldMetaTable: <npc id><field id/></npc>, npcs listed per field in file order
        foreach (var entry in new[] { "table/fieldmetadata.xml", "table/kr/fieldmetadata.xml" })
        {
            if (!packs.Exists("Xml", entry)) continue;
            var root = GameStrings.Parse(packs.Read("Xml", entry)).Root!;
            // <taxiStation field= IsHomePrivilege=/>: the fields with a taxi station (CFieldMetaTable m_taxi)
            foreach (var t in root.Elements("taxiStation"))
                if (int.TryParse((string)t.Attribute("field"), out int tf)) taxiFields.Add(tf);
            foreach (var n in root.Elements("npc"))
            {
                if (!int.TryParse((string)n.Attribute("id"), out int id) || npcs.Find(id) == null) continue;
                foreach (var f in n.Elements("field"))
                    if (int.TryParse((string)f.Attribute("id"), out int fid))
                    {
                        if (!fieldNpcs.TryGetValue(fid, out var list)) fieldNpcs[fid] = list = new List<int>();
                        list.Add(id);
                    }
            }
            break;
        }
        // CFieldData: <indoor type="1|2"/> makes a field indoor (mask 2 or 4)
        if (packs.Exists(FieldData.Archive, FieldData.Entry))
            foreach (var fd in GameStrings.Parse(packs.Read(FieldData.Archive, FieldData.Entry)).Root!.Elements("fieldData"))
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

    static int NpcType(NpcRow r) => r.Kind == 1 || (r.Kind > 10 && r.Kind < 30) ? 1 : r.Kind is >= 30 and < 40 ? 4 : r.Kind;

    /// <summary>UIMinimapEntity::GetNpcIcon ("empty" when the npc is not on the minimap).</summary>
    public static string NpcIcon(NpcRow r)
    {
        if (!r.Minimap) return "empty";
        if (!string.IsNullOrEmpty(r.MinimapIcon)) return r.MinimapIcon;
        if (r.PropertyTags.Contains("13")) return "event_boss";
        int type = NpcType(r);
        if (type == 0 && r.IsBoss) return r.Class is 3 or 7 ? "elite_boss" : "boss";
        if (type == 0x55) return "gatekeeper";
        if (KindIcons.TryGetValue(r.Kind, out var k)) return k;
        return r.Friendly > 0 ? "friendly" : "enemy";
    }

    /// <summary>GetStateForIcon / GetLayerForNpc.</summary>
    public static string NpcFrame(string icon) => icon is "elite_boss" or "boss" or "event_boss" ? "alive" : "";

    public static string NpcLayer(NpcRow r, string icon) =>
        NpcType(r) switch { 1 => "shop", 0x55 => "interact", _ => icon == "friendly" ? "friendly" : "npc" };

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

    Dictionary<int, (string icon, int type)> interactRows;

    /// <summary>The interact object row (table/interactobject.xml): its display minimapIcon ("" when none) and type.</summary>
    public (string icon, int type) InteractRow(PackSet packs, int interactId)
    {
        if (interactRows == null)
        {
            interactRows = new Dictionary<int, (string, int)>();
            const string entry = "table/interactobject.xml";
            if (packs.Exists("Xml", entry))
                foreach (var row in GameStrings.Parse(packs.Read("Xml", entry)).Root!.Elements("interact"))
                    if (int.TryParse((string)row.Attribute("id"), out int id))
                    {
                        int.TryParse((string)row.Attribute("type"), out int type);
                        interactRows.TryAdd(id, ((string)row.Element("display")?.Attribute("minimapIcon") ?? "", type));
                    }
        }
        return interactRows.GetValueOrDefault(interactId, ("", 0));
    }

    /// <summary>
    /// AddNamedObject + UpdateNamed for an interact object that is not a quest target: none without a minimap icon; icon =
    /// the row's minimapIcon, layer interact, no frame, shown when the object's state is 1 (reactable). A watchtower
    /// (type 9) takes the watchtower layer, is always shown and gets frame enabled (state 0) or disabled
    /// (UpdateNamedFrame). The name (string table 0x10) is only the tooltip.
    /// </summary>
    public static Entity Named(string icon, int type, int state) =>
        string.IsNullOrEmpty(icon) ? null
        : type == 9 ? new Entity(icon, state == 0 ? "enabled" : "disabled", "watchtower", null, true, false)
        : new Entity(icon, "", "interact", null, state == 1, false);

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
