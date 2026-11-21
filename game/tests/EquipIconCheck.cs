using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Ui.Items;
using Ms2Godot.Ui;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/equip_icons.tscn): for each stub character (a female job group 30 and a male job group 10 among
/// them) every worn item has item data and an icon file at CItemData::GetIconPath (itemdata.cpp:2070), and the offline
/// character the equipment window shows (the HUD's OfflineMe) wears the same items the model was built from
/// (<see cref="Look.PlayerLook.OfflineWorn"/>). Needs MS2_DATA. Quits with 0 when it holds, else 1.
/// </summary>
public partial class EquipIconCheck : Node
{
    public override void _Ready()
    {
        var fails = new List<string>();
        try { Run(fails); } catch (Exception e) { fails.Add(e.ToString()); }
        GD.Print($"equip icons: {(fails.Count == 0 ? "ok" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    void Run(List<string> fails)
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var table = new ItemDataTable(packs);
        var list = Flow.Session.List();
        StubCharacterList.Dress(packs, list);
        foreach (var (gender, job) in new[] { (1, 30), (0, 10) })
        {
            var c = list.FirstOrDefault(x => x.User.Gender == gender && x.User.JobCode == job);
            if (c == null) { fails.Add($"no stub character gender {gender} job {job}"); continue; }
            if (c.Equips.Count == 0) fails.Add($"gender {gender} job {job}: no worn items");
            foreach (var e in c.Equips)
            {
                var d = table.Get(e.Id);
                if (d == null) { fails.Add($"{gender}/{job} {e.Slot} {e.Id}: no item data"); continue; }
                var path = d.IconPath;
                var entry = path.StartsWith("./Data/Resource/Image/") ? path["./Data/Resource/Image/".Length..] : path;
                if (!packs.Exists("Resource/Image", entry) && packs.FindByFileName("Resource/Image", System.IO.Path.GetFileName(entry)) == null)
                    fails.Add($"{gender}/{job} {e.Slot} {e.Id}: icon {path} not in the Image pack");
            }
        }
    }
}
