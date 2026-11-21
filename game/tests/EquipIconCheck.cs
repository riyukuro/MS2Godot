using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
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
        // a character made in the lobby (the user's looks: its items are not the starter's) is what the model and the window show
        var made = new[]
        {
            (1, 30, new[] { (EquipSlot.HR, 10200006), (EquipSlot.FA, 10300003), (EquipSlot.ER, 10500001), (EquipSlot.CL, 12200395), (EquipSlot.PA, 11500531), (EquipSlot.SH, 11700845), (EquipSlot.RH, 15200223) }),
            (0, 10, new[] { (EquipSlot.CL, 11400743), (EquipSlot.PA, 11500650), (EquipSlot.SH, 11700848), (EquipSlot.RH, 13200220), (EquipSlot.LH, 14100190) }),
        };
        var nifXml = Settings.NifXml;
        foreach (var (g, j, items) in made)
        {
            var e = new CharacterEntry { User = new UserBlock { CharacterId = 3000 + g, Name = "made", Level = 1, JobCode = j, Gender = (byte)g } };
            foreach (var (slot, id) in items) e.Equips.Add(new EquipItem { Id = id, Slot = slot });
            list.Add(e);
            Flow.Session.Chosen = e;
            Look.PlayerLook.Build(new ModelBuilder(ModelBuilder.SharedSpec(nifXml), packs), packs);
            var worn = Look.PlayerLook.OfflineWorn;
            if (worn == null || !worn.Select(w => (w.Slot, w.Id)).SequenceEqual(items))
                fails.Add($"made {g}/{j}: the offline look wears {string.Join(" ", worn?.Select(w => $"{w.Slot}:{w.Id}") ?? new[] { "nothing" })}, not the chosen character's items");
            foreach (var w in worn ?? new List<EquipItem>())
            {
                var d = table.Get(w.Id);
                if (d == null) { fails.Add($"made {g}/{j} {w.Slot} {w.Id}: no item data"); continue; }
                var path = d.IconPath; var entry = path["./Data/Resource/Image/".Length..];
                if (!packs.Exists("Resource/Image", entry) && packs.FindByFileName("Resource/Image", System.IO.Path.GetFileName(entry)) == null)
                    fails.Add($"made {g}/{j} {w.Slot} {w.Id}: icon {path} not in the Image pack");
            }
        }
        Flow.Session.Chosen = null;
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
