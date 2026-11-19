using System.Xml.Linq;
using Godot;
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

    /// <summary>The kind's model casts the sun's shadow: level &gt;= GetShadowOption. CObjectShadow's update pushes its "shown"
    /// state (true only from that level, UpdateVisibility unit_14160fb10.cpp:310-333) to the owner's scene node as the caster
    /// flag (UpdateShadow unit_14160fb10.cpp:238-243, FUN_141567540 unit_141567000.cpp:229-262), and a new shadow starts dirty
    /// (unit_14160fb10.cpp:411), so below that level the character casts nothing even with the sun's shadow on.</summary>
    public static bool RealShadow(string kind) => Level >= (table?.ShadowOption(kind) ?? 5);

    /// <summary>The client's CObjectShadow for a character of the kind (MyPC::vfunc_49 kind 0, PC::vfunc_49 kind 1,
    /// CNpc::vfunc_49 kind 2/3): the simple shadow under <paramref name="owner"/> where <see cref="SimpleShadow"/> holds, and the
    /// model's meshes kept out of the sun's shadow below <see cref="RealShadow"/>. Call after the model is complete (equips
    /// included). STILL-OPEN: FUN_141567540 skips nodes tagged "NoShadow", which keep their own flag; here every mesh is cleared.</summary>
    public static void Character(Node3D owner, Node3D model, string kind, float radius, float lift)
    {
        if (SimpleShadow(kind)) BlobShadow.Add(owner, radius, lift);
        if (!RealShadow(kind)) NoCasters(model);
    }

    /// <summary>An npc's row: CNpc::vfunc_49 (unit_14108e1e0_npcactorandvibrate.cpp:2240-2249) takes bossMonster when IsBoss
    /// (CNpc::IsBoss = CNpcData::IsBoss), else monster; its shadow is sized by the model's shadowScale and lifted 1.</summary>
    public static string NpcKind(Ms2.Formats.World.NpcRow row) => row.IsBoss ? GraphicOptionTable.BossMonster : GraphicOptionTable.Monster;

    static void NoCasters(Node n)
    {
        if (n is GeometryInstance3D g) g.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        foreach (var c in n.GetChildren()) NoCasters(c);
    }
}
