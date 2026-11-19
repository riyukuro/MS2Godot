using System.Xml.Linq;
using Ms2.Formats.Options;
using Ms2.Formats.Pack;

namespace Ms2Godot;

/// <summary>
/// The shadow option level of the field and what it turns on. The level is the larger of the graphics option's
/// shadowLevel and the field's &lt;shadow optionHighLevel&gt; (FUN_140ae1c10, breakableobject.cpp:92-95: DAT_1439f8208 from
/// CMainSystem::ApplyFrameLimit mainsystem.cpp:587-591, DAT_143ae2960 from FUN_141610450 unit_14160fb10.cpp:280-286).
/// The thresholds come from Table/graphicOption.xml's &lt;shadow&gt; rows (CGraphicOptionTable).
/// </summary>
public static class FieldShadows
{
    static GraphicOptionTable table;

    /// <summary>The shadow level of the field being built.</summary>
    public static int Level { get; private set; } = 2;

    /// <summary>Sets <see cref="Level"/> for a field (its fielddata optionHighLevel, 0 without one).</summary>
    public static void Begin(PackSet packs, int fieldHighLevel)
    {
        table ??= packs.Exists("Xml", "table/graphicoption.xml")
            ? GraphicOptionTable.Parse(XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", "table/graphicoption.xml")).TrimStart('﻿')))
            : new GraphicOptionTable();
        Level = Math.Max(GameOptions.Current.GraphicOption.ShadowLevel, fieldHighLevel);
    }

    /// <summary>The sun casts shadows and the field's objects are casters: level &gt;= GetShadowOption(5), mapObject
    /// (MS2LightService::UpdateLightShadow ms2lightservice.cpp:356-385; WorldService::FUN_141bbd490 worldservice_1.cpp:3412-3432
    /// clears an entity's casters below it, SetShadowEnabled worldservice_1.cpp:6990-7005).</summary>
    public static bool MapObjects => Level >= (table?.ShadowOption(GraphicOptionTable.MapObject) ?? 5);

    /// <summary>The kind's simple ground shadow shows: GetShadowOptionOffset &lt;= level &lt; GetShadowOption ("visible",
    /// CObjectShadow's update @0x1416105c0, unit_14160fb10.cpp:310-333; at or above GetShadowOption the real shadow
    /// replaces it).</summary>
    public static bool SimpleShadow(string kind) =>
        table != null && Level >= table.ShadowOptionOffset(kind) && Level < table.ShadowOption(kind);
}
