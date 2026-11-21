using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Ms2Godot.Tests;

/// <summary>Godot-bound code cannot run in the test host, so these read the game sources and fail when the wiring of a reviewed
/// cost fix is reverted: the helper tests prove the helper, these prove the game still calls it in the right order.</summary>
public class WiringGuardTests
{
    static string Source(string rel)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, rel);
            if (File.Exists(p)) return Regex.Replace(File.ReadAllText(p), @"\s+", " ");
        }
        Assert.Fail("source not found above the test directory: " + rel);
        return null;
    }

    [Test]
    public void As3ViewSkipsAnUnchangedChildOrderAndRemembersAChangedOne()
    {
        var s = Source("game/ui/As3View.cs");
        Assert.That(s, Does.Match(@"applied\.IsApplied\(into, want\)\) return; applied\.Set\(into, want\);"),
            "the sync must return before touching the node tree when the order is already applied, and record it otherwise");
        Assert.That(s, Does.Contain("var wanted = applied.Rent();"), "the per-container list is rented");
        Assert.That(s, Does.Contain("applied.Return(wanted);"), "and given back");
        Assert.That(s, Does.Contain("clipped = applied.Rent()"), "clip lists are rented too");
        Assert.That(s, Does.Contain("applied.Return(kids);"));
    }

    [Test]
    public void As3ViewHolderNameTableStopsGrowingAtItsCap()
    {
        var s = Source("game/ui/As3View.cs");
        Assert.That(s, Does.Contain("internal const int HolderNameCap = 4096;"));
        Assert.That(s, Does.Contain("if (holderNames.Count < HolderNameCap) holderNames[name] = sn;"),
            "a name past the cap is not stored (AS3 sets instance names at runtime)");
        Assert.That(s, Does.Not.Match(@"holderNames\[name\] = sn = "), "no unconditional store");
    }

    [Test]
    public void DummyAnchorAsksTheMemoBeforeTheMatricesAndStoresTheAnswer()
    {
        var s = Source("game/scripts/world/DummyAnchor.cs");
        int get = s.IndexOf("memo.TryGet(rows, name, rootName, at, out var r)", StringComparison.Ordinal);
        int find = s.IndexOf("DummyWorld.Find(", StringComparison.Ordinal);
        int set = s.IndexOf("memo.Set(rows, name, rootName, at, r);", StringComparison.Ordinal);
        Assert.That(get, Is.GreaterThan(-1), "the memo lookup");
        Assert.That(find, Is.GreaterThan(get), "the matrices are walked only after a memo miss");
        Assert.That(set, Is.GreaterThan(find), "and the answer, a miss included, is stored");
        Assert.That(s, Does.Contain("memos.GetValue(model"), "one memo per model");
    }

    [Test]
    public void FieldProcessUpdatesBackdropWithoutALocalPlayer()
    {
        var s = Source("game/scripts/Field.cs");
        int proc = s.IndexOf("public override void _Process(double delta)", StringComparison.Ordinal);
        Assert.That(proc, Is.GreaterThan(-1));
        string body = s.Substring(proc);
        int upd = body.IndexOf("backdrop?.Update(camera, player);", StringComparison.Ordinal);
        int ret = body.IndexOf("if (player == null) return;", StringComparison.Ordinal);
        Assert.That(upd, Is.GreaterThan(-1), "Field._Process must update the backdrop");
        Assert.That(ret, Is.GreaterThan(upd), "backdrop.Update must run before the null-player early return (GFxMovieBackground::vfunc_8)");
    }

    [Test]
    public void FieldBackdropValidatesBeforeViewportAndCachesContentAndPosition()
    {
        var s = Source("game/scripts/FieldBackdrop.cs");
        int upd = s.IndexOf("public void Update(Camera3D camera, Node3D player)", StringComparison.Ordinal);
        Assert.That(upd, Is.GreaterThan(-1));
        string body = s.Substring(upd);
        int valid = body.IndexOf("IsInstanceValid(rect)", StringComparison.Ordinal);
        int vp = body.IndexOf("rect.GetViewportRect()", StringComparison.Ordinal);
        Assert.That(valid, Is.GreaterThan(-1));
        Assert.That(vp, Is.GreaterThan(valid), "IsInstanceValid(rect) must run before rect.GetViewportRect()");
        Assert.That(body, Does.Not.Contain("rect.Size"), "Update must use cached content size instead of reading rect.Size each frame");
        Assert.That(s, Does.Contain("FieldBackground.ResizeNoScroll(content.X, content.Y,"), "ResizeNoScroll must pass the bitmap's current width and height");
        Assert.That(s, Does.Contain("if (pos == appliedPos) return;"), "rect.Position must only be written when it changes");
    }

    [Test]
    public void FieldTriggersDropsStaleDrawsWhenLaterEntityReplacesId()
    {
        var s = Source("game/scripts/world/FieldTriggers.cs");
        Assert.That(s, Does.Contain("else meshes.Remove(id);"), "a later entity with the same id must drop an earlier entity's draws");
    }

    /// <summary>SetHdr @0x14125e300 (gameoptionstruct.cpp:2081-2090) reaches the running field: the option dialog's switch
    /// calls Field.ApplyHdr, which switches the bloom step and the glow click (SetSoftParticlesOption, applyeffect.cpp:488-490)
    /// and rescales the lights (ScaleLightIntensity, ms2lightservice.cpp:395-403).</summary>
    [Test]
    public void OptionHdrSwitchReachesTheRunningField()
    {
        var h = Source("game/ui/GameOptionHost.cs");
        Assert.That(h, Does.Contain("Field.Active?.ApplyHdr(state);"), "SetHdr must apply the switch to the running field");
        var f = Source("game/scripts/Field.cs");
        Assert.That(f, Does.Contain("bloom.ConvertOnly = !hdrOn;"), "HDR off: the bloom step only converts the frame out");
        Assert.That(f, Does.Contain("glow?.SetActive(hdrOn);"), "HDR off: the glow click stops drawing and processing");
        Assert.That(f, Does.Contain("FieldLights.Rescale(k);"), "the switch rescales the point and spot lights");
        Assert.That(f, Does.Contain("SetSunAndAmbient(k);"), "the switch rescales the ambient and sun dimmers");
        var g = Source("game/scripts/GlowPass.cs");
        Assert.That(g, Does.Contain("SetProcess(active);"), "a switched-off glow pass must not run its _Process");
        var l = Source("game/scripts/FieldLights.cs");
        Assert.That(l, Does.Contain("PutLight(i, current.Lights[i]);"), "Rescale re-puts the field lights' columns");
        Assert.That(l, Does.Contain("Refresh();"), "Rescale re-puts the effect lights' columns too (they are scaled as well)");
        Assert.That(l, Does.Contain("PutColourRows(data, w, i, l, scale);"), "every put light carries the HDR scale");
        Assert.That(f, Does.Not.Contain("bloom.Glow = hdrOn"), "Glow stays set: the render thread reads it");
    }
    [Test]
    public void RegionSkillLightPartsGoThroughTheFieldLights()
    {
        var r = Source("game/scripts/world/RegionSkills.cs");
        Assert.That(r, Does.Contain("part.Type == \"light\" && part.Light != null"), "an effect xml's light part is played");
        Assert.That(r, Does.Contain("l.Light = FieldLights.Start(l.Param, l.Pos);"), "it starts at its delay as CombatLayer's lights");
        Assert.That(r, Does.Contain("FieldLights.End(l.Light);"), "its end frees the effect light slot");
        var f = Source("game/scripts/Field.cs");
        Assert.That(f, Does.Contain("FieldLights.Clear();"), "a field's build drops the previous field's light set (tests/field_lights_swap.tscn)");
        Assert.That(f, Does.Contain("FieldLights.Clear(lightSet);"), "a leaving field drops its own light set");
    }
}
