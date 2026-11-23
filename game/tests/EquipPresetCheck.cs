using System;
using System.Collections.Generic;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/equip_preset.tscn): a weapon whose item row names another item's model (tool itemPreset;
/// the Tria sword and shield of the GMS2 data) is drawn. With the item rows unread, no model is found and nothing hangs
/// on the character. Needs MS2_DATA and MS2_NIFXML. Quits with 0 when it holds, else 1.
/// </summary>
public partial class EquipPresetCheck : Node
{
    public override async void _Ready()
    {
        var fails = new List<string>();
        try { await Run(fails); }
        catch (Exception e) { fails.Add(e.ToString()); }
        GD.Print($"equip preset: {(fails.Count == 0 ? "ok" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    async System.Threading.Tasks.Task Run(List<string> fails)
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var mb = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var md = mb.LoadModel("male/m_body.nif");
        var applier = new EquipApplier(packs, mb);
        // Tria sword / shield (GMS2 ids); on KMS data they are absent and the check has nothing to say.
        var ids = new (int id, EquipSlot slot)[] { (14100190, EquipSlot.LH), (13200220, EquipSlot.RH) };
        if (!packs.Exists("Xml", "item/1/41/14100190.xml")) { GD.Print("equip preset: ok (no per-item data set)"); GetTree().Quit(0); return; }
        var v = mb.Build(md, "Male"); AddChild(v);
        var items = new List<EquipItem>();
        long uid = 1;
        foreach (var (id, slot) in ids) items.Add(new EquipItem { Id = id, Uid = uid++, Slot = slot });
        applier.Apply(v, md, 0, items);
        for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (applier.HungNodes(v) == 0) fails.Add("the sword and shield hung no node");
        // the file of 15000041 (a 150 weapon) says subtype 16: the kind is its id group, 50, as the Ctrl attack reads it
        foreach (var (id, kind) in new[] { (15000041, 50), (14100190, 41), (13200220, 32) })
            if (packs.Exists("Xml", $"item/{id / 10000000}/{id / 100000 % 100}/{id}.xml") && applier.WeaponSubtype(id) != kind)
                fails.Add($"item {id}: weapon kind {applier.WeaponSubtype(id)}, expected {kind}");
        if (applier.MissingItemIds != 0) fails.Add($"{applier.MissingItemIds} item ids had no item model");
    }
}
