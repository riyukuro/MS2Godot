using System.Collections.Generic;
using System.Linq;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/remote_weapon.tscn, game data) of another pc's weapon rig after its worn items change
/// (RemoteLayer.Redress): a put-on of a weapon (PC::vfunc_127, pc.cpp:1400) gives the pc the rig of the weapon it now wears,
/// and the put-off (PC::vfunc_128, pc.cpp:1415) takes that rig away, as the own body's redress does (PlayerLook.Redress).
/// The pc wears the female knight's starter look without its right-hand weapon, then puts the weapon on and off.
/// Before the fix the pc kept the rig its first build gave (none, then a rig of a weapon no longer worn).
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class RemoteWeaponCheck : Node3D
{
    public override void _Ready()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var field = new FieldStub(packs);
        AddChild(field);
        var layer = new RemoteLayer(field, builder, 1) { ItemExists = _ => true };
        var fails = new List<string>();
        void Check(bool ok, string what) { if (!ok) fails.Add(what); }

        var (items, _) = Look.StarterLook.Build(packs, new Look.ColorPalettes(packs), 1, 10, new Dictionary<string, int>(), 0);
        for (int i = 0; i < items.Count; i++) if (items[i].Uid == 0) items[i].Uid = 2001 + i;
        var rh = items.FirstOrDefault(i => i.Slot == EquipSlot.RH);
        if (rh == null) fails.Add("the female knight's starter look has no right-hand weapon");
        else
        {
            var u = new FieldAddUser { ObjectId = 5, User = new UserBlock { Name = "r5", CharacterId = 105, Gender = 1, Job = 10 } };
            u.Stats[0].Health = 100; u.Stats[2].Health = 100;
            u.Equips.AddRange(items.Where(i => i.Slot is not (EquipSlot.RH or EquipSlot.LH)));
            layer.Tracker.Events.Enqueue(new UserAddedEvent(0, u, false));
            layer._Process(0);
            var bare = layer.WeaponsOf(5);
            Check(bare == null, "the pc without weapons has a weapon rig");

            Check(layer.PutOn(new ItemPutOn(5, 0, rh), _ => 0), $"the put-on of {rh.Id} was not taken");
            var armed = layer.WeaponsOf(5);
            Check(armed != null, $"after the put-on of {rh.Id} the pc has no weapon rig");

            Check(layer.PutOff(new ItemPutOff(5, rh.Uid), _ => 0), $"the put-off of {rh.Id} was not taken");
            var after = layer.WeaponsOf(5);
            Check(after == null || after != armed, $"after the put-off of {rh.Id} the pc keeps the weapon's rig");
            GD.Print($"remote weapon: weapon {rh.Id}: rig {bare != null} -> {armed != null} -> {after != null && after == armed}");
        }
        GD.Print($"remote weapon: {(fails.Count == 0 ? "ok" : "FAIL " + string.Join("; ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
