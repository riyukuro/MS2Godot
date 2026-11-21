using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/equip_swap.tscn) of EquipApplier.Reapply, the per-slot path a remote player's put-on /
/// put-off takes:
/// <list type="bullet">
/// <item>after a swap, a put-off and a put-on the character shows what a fresh Apply of the same look shows (the same
/// model nodes with the same visibility, body meshes included);</item>
/// <item>20 swaps of one slot leave the skeleton's node count, the engine's node count and the hung-node count where they were.</item>
/// </list>
/// Needs MS2_DATA and MS2_NIFXML (or local.cfg). Quits with 0 when it holds, else 1.
/// </summary>
public partial class EquipSwapCheck : Node
{
    public override async void _Ready()
    {
        var fails = new List<string>();
        try { await Run(fails); }
        catch (Exception e) { fails.Add(e.ToString()); }
        GD.Print($"equip swap: {(fails.Count == 0 ? "ok" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    static List<int> IdsOf(PackSet packs, int prefix, int n)
    {
        string entry = $"itemmodel/{prefix}.xml";
        var x = XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", entry)).TrimStart('﻿'));
        return x.Descendants("ItemModel").Select(e => (int)e.Attribute("id")).Take(n).ToList();
    }

    static EquipItem It(int id, long uid, EquipSlot slot) => new() { Id = id, Uid = uid, Slot = slot };

    async System.Threading.Tasks.Task Run(List<string> fails)
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var mb = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var md = mb.LoadModel("male/m_body.nif");
        var applier = new EquipApplier(packs, mb);
        var fresh = new EquipApplier(packs, mb);
        var cl = IdsOf(packs, 114, 400).Where(i => i / 100000 == 114).ToList();
        var cap = IdsOf(packs, 113, 400).Where(i => i / 100000 == 113).ToList();
        if (cl.Count < 3 || cap.Count < 1) { fails.Add("no item models to swap"); return; }
        EquipItem A() => It(cl[0], 1, EquipSlot.CL);
        EquipItem B() => It(cl[1], 2, EquipSlot.CL);
        EquipItem C() => It(cl[2], 3, EquipSlot.CL);
        var hat = It(cap[0], 9, EquipSlot.CP);

        var v = mb.Build(md, "Male"); AddChild(v);
        applier.Apply(v, md, 0, new[] { A(), hat });
        await Settle();
        int hung0 = applier.HungNodes(v);
        if (hung0 == 0) fails.Add("the first look hung no nodes (item data missing?)");

        void SameAsFresh(string what, params EquipItem[] look)
        {
            var f = mb.Build(md, "Male"); AddChild(f);
            fresh.Apply(f, md, 0, look);
            string want = Describe(f), got = Describe(v);
            if (want != got) fails.Add($"{what}: differs from a fresh Apply\n    fresh: {want}\n    got:   {got}");
            f.Free();
        }

        applier.Reapply(v, new[] { B(), hat }); await Settle();
        SameAsFresh("swap CL", B(), hat);
        applier.Reapply(v, new[] { hat }); await Settle();
        SameAsFresh("put off CL", hat);
        applier.Reapply(v, new[] { hat, C() }); await Settle();
        SameAsFresh("put on CL", hat, C());
        applier.Reapply(v, new[] { C() }, EquipSlot.CL); await Settle();
        SameAsFresh("item update + hat off", C());

        applier.Reapply(v, new[] { A(), hat }); await Settle();
        var skel = v.GetNode<Skeleton3D>("Model/" + ModelBuilder.SkeletonName);
        GC.Collect(); GC.WaitForPendingFinalizers(); await Settle();
        int skel0 = Count(skel), nodes0 = (int)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
        int hungStart = applier.HungNodes(v);
        long objs0 = (long)Performance.GetMonitor(Performance.Monitor.ObjectCount);
        for (int i = 0; i < 20; i++)
        {
            applier.Reapply(v, new[] { i % 2 == 0 ? B() : A(), hat });
            await Settle();
        }
        GC.Collect(); GC.WaitForPendingFinalizers(); await Settle();
        int skel1 = Count(skel), nodes1 = (int)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
        long objs1 = (long)Performance.GetMonitor(Performance.Monitor.ObjectCount);
        GD.Print($"equip swap: 20 swaps, skeleton nodes {skel0} -> {skel1}, engine nodes {nodes0} -> {nodes1}, objects {objs0} -> {objs1}, hung {hungStart} -> {applier.HungNodes(v)}");
        if (skel1 != skel0) fails.Add($"skeleton node count {skel0} -> {skel1} after 20 swaps");
        if (nodes1 != nodes0) fails.Add($"engine node count {nodes0} -> {nodes1} after 20 swaps");
        if (applier.HungNodes(v) != hungStart) fails.Add($"hung nodes {hungStart} -> {applier.HungNodes(v)} after 20 swaps");
        v.Free();
    }

    async System.Threading.Tasks.Task Settle()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    static int Count(Node n) { int c = 1; foreach (var ch in n.GetChildren()) c += Count(ch); return c; }

    /// <summary>The model nodes under the skeleton (without the unique-name suffix Godot adds) and their visibility, sorted.</summary>
    static string Describe(Node3D v)
    {
        var skel = v.GetNode<Skeleton3D>("Model/" + ModelBuilder.SkeletonName);
        var l = skel.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>()
            .Where(m => !m.IsQueuedForDeletion())
            .Select(m => $"{System.Text.RegularExpressions.Regex.Replace(m.Name.ToString(), "@.*", "")}:{(m.Visible ? 1 : 0)}").OrderBy(s => s, StringComparer.Ordinal);
        return string.Join(",", l);
    }
}
