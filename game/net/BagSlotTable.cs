using Ms2.Net.Packets;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2Godot.Ui;
using LobbyFeatures = Ms2.Ui.LobbyFeatures;
using Ms2.Formats.Text;

namespace Ms2Godot.Net;

/// <summary>
/// The client's per-tab base slot count (FUN_140a66510) from table/constants.xml: the bagSlotTab*Count entries
/// (Constants +0x74d8..), each used when its feature attribute is on (IsFeatureEnabledByName); BagRenewal01 (id 116) gates the whole table.
/// </summary>
public static class BagSlotTable {
    public static Func<int, int> Load(PackSet packs) => Read(packs, BagSlots.Base);

    /// <summary>The expand maximum per tab (FUN_140a66750), from the same entries.</summary>
    public static Func<int, int> LoadMax(PackSet packs) => Read(packs, BagSlots.Max);

    /// <summary>The bagSlotTab* entries whose feature is on and BagRenewal01, read once per pack set (the feature levels
    /// they depend on are kept per pack set too, LobbyFeatures).</summary>
    sealed record Entries(Dictionary<string, int[]> Values, bool Renewal);

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PackSet, Entries> sets = new();

    static Func<int, int> Read(PackSet packs, Func<int, bool, Func<string, int[]>, int> pick) {
        var e = sets.GetValue(packs, Parse);
        return tab => pick(tab, e.Renewal, k => e.Values.GetValueOrDefault(k));
    }

    static Entries Parse(PackSet packs) {
        var values = new Dictionary<string, int[]>();
        if (packs.Exists("Xml", "table/constants.xml")) {
            var doc = PackXml.Parse(packs.Read("Xml", "table/constants.xml"));
            foreach (var v in doc.Descendants("v")) {
                var key = (string) v.Attribute("key");
                if (key == null || !key.StartsWith("bagSlotTab") || !LobbyFeatures.IsEnabledByName(packs, (string) v.Attribute("feature"))) continue;
                values[key] = ((string) v.Attribute("value") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(x => int.TryParse(x, out var n) ? n : 0).ToArray();
            }
        }
        return new Entries(values, LobbyFeatures.IsEnabled(packs, "BagRenewal01"));
    }
}
