using System.Collections.Generic;
using System.Globalization;
using Ms2.Formats.Pack;

namespace Ms2.Ui;

/// <summary>
/// The character effect gems of CItemGemPropertyTable (table/itemgemproperty_chareffect.xml, itemgempropertytable.cpp:73): the
/// buddy badges' own effect by the wearer's gender and the couple effect by both genders.
/// </summary>
public sealed class CharEffectGems
{
    /// <summary>GEM_CHAREFFECT (itemgem.h): the gem type of a buddy badge (OnBuddyBadge's GetItemByGemType(7)).</summary>
    public const int GemType = 7;

    /// <summary>CGemCharEffectData (CGemCharEffectData::vfunc_1, itemgempropertytable.cpp:236-244): its gem id and the
    /// "EffectMale", "EffectFemale", "EffectCouple_Diff", "EffectCouple_Same_M" and "EffectCouple_Same_F" attributes.</summary>
    public sealed record Gem(int Id, string Male, string Female, string CoupleDiff, string CoupleSameM, string CoupleSameF)
    {
        /// <summary>CGemCharEffectData::GetEffectByGender (@0x1405acbd0): gender 0 the male effect, 1 the female one, else "".</summary>
        public string EffectByGender(int gender) => gender == 0 ? Male : gender == 1 ? Female : "";

        /// <summary>CGemCharEffectData::GetCoupleEffect (@0x1405acc80): two males the Same_M effect, two females the Same_F
        /// one, any other pair the Diff one.</summary>
        public string CoupleEffect(int ownerGender, int partnerGender) =>
            ownerGender == 0 && partnerGender == 0 ? CoupleSameM : ownerGender == 1 && partnerGender == 1 ? CoupleSameF : CoupleDiff;
    }

    readonly Dictionary<int, Gem> gems = new();

    public int Count => gems.Count;
    public IEnumerable<Gem> All => gems.Values;

    /// <summary>CItemGemPropertyTable::GetGemData as a CGemCharEffectData: the gem of that id, null when none.</summary>
    public Gem Find(int id) => gems.TryGetValue(id, out var g) ? g : null;

    /// <summary>
    /// LoadGems&lt;CGemCharEffectData&gt;(..., "charEffect") (itemgempropertytable.cpp:306-345): each available &lt;charEffect&gt;
    /// under &lt;ms2&gt; with a nonzero id, a later one of the same id replacing the kept one (SetAt). No file: none.
    /// </summary>
    public static CharEffectGems Load(PackSet packs)
    {
        var t = new CharEffectGems();
        const string path = "table/itemgemproperty_chareffect.xml";
        if (packs == null || !packs.Exists("Xml", path)) return t;
        var root = Ms2.Formats.Text.PackXml.Parse(packs.Read("Xml", path)).Root;
        if (root == null || root.Name.LocalName != "ms2") return t;
        foreach (var e in root.Elements("charEffect"))
        {
            if (!FeatureElements.IsAvailable(packs, e)) continue;
            if (!uint.TryParse((string)e.Attribute("id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint id) || id == 0) continue;
            t.gems[(int)id] = new Gem((int)id, (string)e.Attribute("EffectMale") ?? "", (string)e.Attribute("EffectFemale") ?? "",
                (string)e.Attribute("EffectCouple_Diff") ?? "", (string)e.Attribute("EffectCouple_Same_M") ?? "",
                (string)e.Attribute("EffectCouple_Same_F") ?? "");
        }
        return t;
    }

    /// <summary>A table made from rows (the tests).</summary>
    public static CharEffectGems Of(params Gem[] rows)
    {
        var t = new CharEffectGems();
        foreach (var g in rows) t.gems[g.Id] = g;
        return t;
    }
}
