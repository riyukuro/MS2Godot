using System.Xml.Linq;

namespace Ms2.Ui.Hud;

/// <summary>
/// CSmartPushTable (smartpushtable.cpp, Load @0x140810730): Table/smartPush.xml &lt;ms2&gt;&lt;push id content actionType actionValue
/// requireMerat requireItem imgPath&gt;. A row failing IsElementAvailable is skipped; a later row with the same id replaces
/// an earlier one (m_data.SetAt); a requireItem that does not split into three comma parts drops the row.
/// </summary>
public sealed class SmartPushTable
{
    /// <summary>CSmartPushData::m_nActionType: Parse_140219650 (commonenum.cpp:535) maps "additionalEffect" to 1 and
    /// "autoInteraction" to 2, case-insensitive; anything else is 0.</summary>
    public const int ActionAdditionalEffect = 1, ActionAutoInteraction = 2;

    public sealed class Data
    {
        public int Id;
        public string Content = "";
        public int ActionType, ActionValue, RequireMerat;
        /// <summary>m_requireItem {id, tag, count}: "id:tag,x,count" (id and tag split by ':').</summary>
        public uint RequireItemId, RequireItemCount;
        public string RequireItemTag = "";
        public string ImgPath = "";
    }

    readonly Dictionary<int, Data> rows = new();

    public static readonly SmartPushTable Empty = new(null, _ => true);

    public SmartPushTable(XDocument xml, Func<XElement, bool> available)
    {
        if (xml?.Root is not { Name.LocalName: "ms2" } root) return;
        foreach (var e in root.Elements("push"))
        {
            if (!available(e)) continue;
            var d = new Data
            {
                Id = Int(e, "id"),
                Content = (string)e.Attribute("content") ?? "",
                ActionType = ParseActionType((string)e.Attribute("actionType") ?? ""),
                ActionValue = Int(e, "actionValue"),
                RequireMerat = Int(e, "requireMerat"),
                ImgPath = (string)e.Attribute("imgPath") ?? "",
            };
            var item = (string)e.Attribute("requireItem") ?? "";
            if (item.Length != 0)
            {
                var parts = item.Split(',');
                if (parts.Length != 3) continue;
                var idTag = parts[0].Split(':');
                if (idTag.Length > 0) uint.TryParse(idTag[0], out d.RequireItemId);
                if (idTag.Length > 1) d.RequireItemTag = idTag[1];
                uint.TryParse(parts[2], out d.RequireItemCount);
            }
            rows[d.Id] = d;
        }
    }

    public int Count => rows.Count;

    /// <summary>GetData (@0x140810ca0): the row, null without one.</summary>
    public Data Get(int id) => rows.TryGetValue(id, out var d) ? d : null;

    public IEnumerable<Data> All => rows.Values;

    public static int ParseActionType(string s) =>
        s.Equals("additionalEffect", StringComparison.OrdinalIgnoreCase) ? ActionAdditionalEffect
        : s.Equals("autoInteraction", StringComparison.OrdinalIgnoreCase) ? ActionAutoInteraction : 0;

    static int Int(XElement e, string a) => int.TryParse((string)e.Attribute(a), out int v) ? v : 0;
}
