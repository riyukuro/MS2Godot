using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2.Ui.Hud;
using Ms2Godot.Net;
using Ms2Godot.World;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/remote_invincible.tscn, game data) of another player's invincible time after a revival:
/// OnRevival (scenestategame.cpp:7389-7409) starts PC::FUN_141661370 on whichever PC the packet names, and PC's update
/// (pc.cpp:4468) runs FUN_1416615f0's blink for each PC.
/// - a FieldAddUser then a revival of that object start the remote's invincible with the revival timer, with the common
///   resource's Revival_Invincible effect when the data has that exact key (the KMS2 xml spells it revival_Invincible, so the
///   client's case-sensitive lookup gives no effect there);
/// - the next frame's blink sets 0.9 on the remote's meshes, and a blink frame allocates 0 B;
/// - past the end the meshes are back at 1;
/// - with the effect xml named directly the effect is kept at the actor, and it goes at the end and when the remote leaves
///   while invincible.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class RemoteInvincibleCheck : Node3D
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    readonly List<string> fails = new();
    void Check(bool ok, string what) { if (!ok) fails.Add(what); }

    static float Param(MeshInstance3D mi) => mi.GetInstanceShaderParameter(ShaderParam.NodeAlpha).VariantType == Variant.Type.Nil ? 1f : mi.GetInstanceShaderParameter(ShaderParam.NodeAlpha).AsSingle();

    static MeshInstance3D FirstMarked(Node n)
    {
        if (n is MeshInstance3D mi && mi.HasMeta(NodeFade.Meta) && mi.Visible) return mi;
        for (int i = 0; i < n.GetChildCount(); i++)
            if (FirstMarked(n.GetChild(i)) is { } m) return m;
        return null;
    }

    public override void _Ready()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var field = new FieldStub(packs);
        AddChild(field);
        var fx = new RegionSkills(packs, builder);
        field.AddChild(fx);
        var layer = new RemoteLayer(field, builder, 1);
        string common = Ms2.Ui.GameTables.Shared(packs, GameOptions.StringLanguage).CommonResource("Revival_Invincible");
        var kept = (IDictionary)typeof(RegionSkills).GetField("kept", Private).GetValue(fx);

        FieldAddUser User(int id) => new() { ObjectId = id, User = new UserBlock { Name = "r" + id, CharacterId = 100 + id, Gender = 1 } };
        layer.Tracker.Events.Enqueue(new UserAddedEvent(0, User(5), false));
        layer.Tracker.Events.Enqueue(new UserAddedEvent(0, User(6), false));
        layer._Process(0);
        var inv = layer.InvincibleOf(5);
        Check(inv != null && !inv.Blink.Active, "the added remote has no idle invincible");
        layer.Tracker.Events.Enqueue(new UserRevivalEvent(0, 5, 0));
        layer.Tracker.Events.Enqueue(new UserRevivalEvent(0, 6, 0));
        layer._Process(0);
        Check(inv != null && inv.Blink.Active && inv.Blink.TimerId == InvincibleBlink.RevivalTimer,
            $"the revival did not start the remote's invincible (active {inv?.Blink.Active}, timer {inv?.Blink.TimerId})");
        // the effect name is the common resource's Revival_Invincible, looked up by its exact key (CRBMap<CString, CString>,
        // commonresource.cpp:303/332); a data whose xml spells the key otherwise gives "" and no effect, as in the client
        Check(inv != null && (common == "" ? inv.Effect == 0 : inv.Effect != 0 && kept.Contains(inv.Effect)),
            $"the revival's effect handle {inv?.Effect} for the common resource '{common}'");

        var visual = FindVisual(layer, inv);
        var mesh = visual != null ? FirstMarked(visual) : null;
        Check(mesh != null, "no marked mesh on the remote's body");
        layer._Process(0);
        Check(mesh != null && Param(mesh) == 0.9f, $"the remote's blink frame left node_alpha {(mesh != null ? Param(mesh) : -1)}, want 0.9");

        // a blink frame: 0 B
        uint now = (uint)Time.GetTicksMsec();
        inv?.Tick(now, visual, fx);
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10; i++) inv?.Tick(now, visual, fx);
        long bytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
        Check(bytes == 0, $"10 remote blink frames allocated {bytes} B");

        // past the end: the effect goes, the meshes are back at 1
        int handle = inv?.Effect ?? 0;
        inv?.Blink.Start(1, InvincibleBlink.RevivalTimer, (uint)Time.GetTicksMsec());
        OS.DelayMsec(5);
        layer._Process(0);
        Check(inv != null && !inv.Blink.Active && inv.Effect == 0 && (handle == 0 || !kept.Contains(handle)), $"the end left the invincible (active {inv?.Blink.Active}, effect {inv?.Effect})");
        Check(mesh != null && Param(mesh) == 1f && mesh.MaterialOverride == null, $"the end left node_alpha {(mesh != null ? Param(mesh) : -1)}");

        // the kept effect at the actor (the data's effect xml, named directly): spawned on the start, gone at the end and when
        // the remote leaves while invincible
        const string Xml = "CH/Common/Eff_Revival_Invincible.xml";
        Check(fx.Has(Xml), $"no {Xml} in the data");
        var direct = new RemoteInvincible();
        var actor = new Node3D();
        AddChild(actor);
        direct.Start(10000, InvincibleBlink.RevivalTimer, (uint)Time.GetTicksMsec(), actor, fx, Xml);
        int h = direct.Effect;
        Check(h != 0 && kept.Contains(h), $"the start kept no effect (handle {h})");
        direct.Blink.Start(1, InvincibleBlink.RevivalTimer, (uint)Time.GetTicksMsec());
        OS.DelayMsec(5);
        direct.Tick((uint)Time.GetTicksMsec(), actor, fx);
        Check(!direct.Blink.Active && direct.Effect == 0 && !kept.Contains(h), $"the end left the effect {h}");
        var inv6 = layer.InvincibleOf(6);
        inv6?.Start(10000, InvincibleBlink.RevivalTimer, (uint)Time.GetTicksMsec(), FindVisual(layer, inv6), fx, Xml);
        int h6 = inv6?.Effect ?? 0;
        layer.Tracker.Events.Enqueue(new UserRemovedEvent(0, 6));
        layer._Process(0);
        Check(h6 != 0 && !kept.Contains(h6) && inv6 is { Blink.Active: false, Effect: 0 }, $"the leaving remote's effect {h6} stayed (kept {kept.Contains(h6)}, active {inv6?.Blink.Active}, effect {inv6?.Effect})");

        foreach (var f in fails) GD.PrintErr("FAIL " + f);
        GD.Print(fails.Count == 0 ? "remote invincible: ok" : $"remote invincible: {fails.Count} FAILED");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    /// <summary>The remote's body: the private Remote record's Visual.</summary>
    static Node3D FindVisual(RemoteLayer layer, RemoteInvincible inv)
    {
        var users = (IDictionary)typeof(RemoteLayer).GetField("users", Private).GetValue(layer);
        foreach (var r in users.Values)
            if (r.GetType().GetField("Invincible").GetValue(r) == inv) return (Node3D)r.GetType().GetField("Visual").GetValue(r);
        return null;
    }
}
