using System.Collections.Generic;
using System.Globalization;
using Ms2.Formats.Pack;

namespace Ms2.Ui;

/// <summary>
/// The swim gems of CItemGemPropertyTable (table/itemgemproperty_swim.xml, itemgempropertytable.cpp:72) and the swim effect
/// a PC plays for a swim action (PC::GetSwimEffectName, pc.cpp:3575-3592).
/// </summary>
public sealed class SwimGems
{
    /// <summary>The actions PC::GetSwimEffectName and PC::GetSwimAnimName take: 0x30 idle, 0x32 dash, any other the run.</summary>
    public const int IdleAction = 0x30, DashAction = 0x32;
    /// <summary>GEM_SWIM: the gem type PC::GetSwimGem asks the gem equipment for (FUN_141270740(6), pc.cpp:3020).</summary>
    public const int GemType = 6;
    /// <summary>The feature PC::GetSwimGem needs (IsFeatureEnabled(0x68), pc.cpp:3018): GemSwim.</summary>
    public const int Feature = 0x68;

    /// <summary>CGemSwimData (itemgem.h): its gem id and the "kf", "idleEffect", "runEffect" and "dashEffect" attributes
    /// (CGemSwimData::vfunc_1, itemgempropertytable.cpp:184-191).</summary>
    public sealed record Gem(int Id, string Kf, string IdleEffect, string RunEffect, string DashEffect);

    readonly Dictionary<int, Gem> gems = new();

    public int Count => gems.Count;

    /// <summary>CItemGemPropertyTable::GetGemData: the swim gem of that id, null when none.</summary>
    public Gem Find(int id) => gems.TryGetValue(id, out var g) ? g : null;

    /// <summary>
    /// LoadGems&lt;CGemSwimData&gt;(..., "swim") (itemgempropertytable.cpp:306-345): each available &lt;swim&gt; under &lt;ms2&gt;
    /// with a nonzero id, a later one of the same id replacing the kept one (SetAt). No file: none.
    /// </summary>
    public static SwimGems Load(PackSet packs)
    {
        var t = new SwimGems();
        const string path = "table/itemgemproperty_swim.xml";
        if (!packs.Exists("Xml", path)) return t;
        var root = Ms2.Formats.Text.PackXml.Parse(packs.Read("Xml", path)).Root;
        if (root == null || root.Name.LocalName != "ms2") return t;
        foreach (var e in root.Elements("swim"))
        {
            if (!FeatureElements.IsAvailable(packs, e)) continue;
            if (!uint.TryParse((string)e.Attribute("id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint id) || id == 0) continue;
            t.gems[(int)id] = new Gem((int)id, (string)e.Attribute("kf") ?? "", (string)e.Attribute("idleEffect") ?? "",
                (string)e.Attribute("runEffect") ?? "", (string)e.Attribute("dashEffect") ?? "");
        }
        return t;
    }

    /// <summary>
    /// PC::GetSwimEffectName (pc.cpp:3575-3592): the worn swim gem's effect of the action (0x30 its idleEffect, 0x32 its
    /// dashEffect, any other its runEffect); without a swim gem (PC::GetSwimGem: the feature off, no usable gem of type 6
    /// or no data for it) the gender's tube effect (<see cref="DefaultEffect"/>).
    /// </summary>
    public static string EffectName(int action, Gem gem, int gender)
    {
        if (gem != null)
            return action == IdleAction ? gem.IdleEffect : action == DashAction ? gem.DashEffect : gem.RunEffect;
        return DefaultEffect(gender, action);
    }

    /// <summary>GetSwimEffectPath (itemugcmesh.cpp:947-964): the idle tube for 0x30, the run tube for any other action
    /// (the dash too), the female one for gender 1.</summary>
    public static string DefaultEffect(int gender, int action)
    {
        if (action == IdleAction)
            return gender == 1 ? "CH/Common/Eff_Tube_F_SwimIdle_A.xml" : "CH/Common/Eff_Tube_M_SwimIdle_A.xml";
        return gender == 1 ? "CH/Common/Eff_Tube_F_SwimRun_A.xml" : "CH/Common/Eff_Tube_M_SwimRun_A.xml";
    }
}
