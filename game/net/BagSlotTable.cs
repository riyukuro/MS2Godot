using Ms2.Net.Packets;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2Godot.Ui;
using LobbyFeatures = Ms2.Ui.LobbyFeatures;

namespace Ms2Godot.Net;

/// <summary>
/// The client's per-tab base slot count (FUN_140a66510) from table/constants.xml: the bagSlotTab*Count entries
/// (Constants +0x74d8..), each used when its feature attribute is on (IsFeatureEnabledByName); BagRenewal01 (id 116) gates the whole table.
/// </summary>
public static class BagSlotTable {
    public static Func<int, int> Load(PackSet packs) => Read(packs, BagSlots.Base);

    /// <summary>The expand maximum per tab (FUN_140a66750), from the same entries.</summary>
    public static Func<int, int> LoadMax(PackSet packs) => Read(packs, BagSlots.Max);

    static Func<int, int> Read(PackSet packs, Func<int, bool, Func<string, int[]>, int> pick) {
        var values = new Dictionary<string, int[]>();
        if (packs.Exists("Xml", "table/constants.xml")) {
            var doc = XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", "table/constants.xml")).TrimStart('﻿'));
            foreach (var v in doc.Descendants("v")) {
                var key = (string) v.Attribute("key");
                if (key == null || !key.StartsWith("bagSlotTab") || !LobbyFeatures.IsEnabledByName(packs, (string) v.Attribute("feature"))) continue;
                values[key] = ((string) v.Attribute("value") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(x => int.TryParse(x, out var n) ? n : 0).ToArray();
            }
        }
        bool renewal = LobbyFeatures.IsEnabled(packs, "BagRenewal01");
        return tab => pick(tab, renewal, k => values.GetValueOrDefault(k));
    }
}
