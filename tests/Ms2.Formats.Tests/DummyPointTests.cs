using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// The by-value dummy point (<see cref="DummyWorld.FindPoint"/>) that DummyAnchor.Point runs on a memo miss: the same doubles
/// as <see cref="DummyWorld.Find(IReadOnlyList{EffectDummy}, string, Xform, string, Func{string, Xform?})"/>'s translate for
/// every row of the game's dummy tables, and no allocation while the model moves (a miss every frame).
/// </summary>
public class DummyPointTests
{
    static readonly List<EffectDummy> Rows = new()
    {
        new() { Name = "Eff_UI", ParentNode = "Scene Root", ApplyWorldTransform = true, ApplyLocalScale = false, Translate = new Vector3(3, -4, 170) },
        new() { Name = "Eff_Damage", ParentNode = "Eff_UI", Translate = new Vector3(0, 0, 20), Rotation = new Vector3(10, 20, 30), Scale = 1.5f },
        new() { Name = "Eff_Hand", ParentNode = "Scene Root", Translate = new Vector3(12, 5, 90), Rotation = new Vector3(0, 0, 45) },
        new() { Name = "Eff_Lost", ParentNode = "Bip01 Nowhere", Translate = new Vector3(1, 1, 1) },
    };

    // a model root turned about z and scaled, at x along the walk
    static Xform Root(double x, double degrees = 37, double scale = 1.25) =>
        new(new Vec3(x, -0.5 * x, 12), DummyWorld.EulerXyz(0, 0, degrees), scale);

    static void SameAsFind(IReadOnlyList<EffectDummy> rows, string root, Xform at, string what)
    {
        foreach (var d in rows)
        {
            var want = DummyWorld.Find(rows, d.Name, at, root);
            bool has = DummyWorld.FindPoint(rows, d.Name, DummyWorld.Pose.Of(at), root, out var got);
            Assert.That(has, Is.EqualTo(want.HasValue), $"{what} {d.Name}");
            if (has) Assert.That(got, Is.EqualTo(want.Value.T), $"{what} {d.Name}");
        }
    }

    [Test]
    public void ThePointIsFindsTranslate()
    {
        for (int i = 0; i < 8; i++) SameAsFind(Rows, "Scene Root", Root(100 * i, 45 * i, 0.5 + i), $"walk {i}");
        Assert.That(DummyWorld.FindPoint(Rows, "Eff_Lost", DummyWorld.Pose.Of(Root(0)), "Scene Root", out _), Is.False, "a missing parent");
        Assert.That(DummyWorld.FindPoint(Rows, "Eff_None", DummyWorld.Pose.Of(Root(0)), "Scene Root", out _), Is.False, "a missing row");
    }

    /// <summary>Every row of the default and both player tables, on both data sets, on a moving turned root.</summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryGameDummyRowGivesFindsTranslate(string env)
    {
        var dir = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore($"{env} not set");
        using var packs = new PackSet(dir);
        var t = EffectDummyTable.Load(packs);
        int rows = 0;
        foreach (var (name, list) in new[] { ("default", t.Default), ("pc 0", t.Pc(0)), ("pc 1", t.Pc(1)) })
        {
            Assert.That(list, Is.Not.Empty, name);
            for (int i = 0; i < 4; i++) SameAsFind(list, "Scene Root", Root(250 * i, 90 * i + 13), $"{name} walk {i}");
            rows += list.Count;
        }
        TestContext.Out.WriteLine($"{env}: {rows} rows x 4 poses");
    }

    [Test]
    public void AMovingModelsLookupAllocatesNothing()
    {
        var memo = new DummyMemo();
        double x = 0;
        var moved = DummyWorld.Pose.Of(Root(0));
        void Lookup()
        {
            x += 1;   // the model moved: the memo misses every time
            var root = moved;
            root.T = new Vec3(x, 0, 0);
            var at = new DummyMemo.Key(1, 0, 0, 0, 1, 0, 0, 0, 1, (float)x, 0, 0);
            if (memo.TryGet(Rows, "Eff_Damage", "Scene Root", at, out _)) Assert.Fail("a moved model hit the memo");
            var r = DummyWorld.FindPoint(Rows, "Eff_Damage", root, "Scene Root", out var p)
                ? new DummyMemo.Point((float)p.X, (float)p.Y, (float)p.Z, true) : default;
            memo.Set(Rows, "Eff_Damage", "Scene Root", at, r);
        }
        Lookup();
        long bytes = Alloc.Least(() => { for (int i = 0; i < 1000; i++) Lookup(); });
        Assert.That(bytes, Is.LessThan(64), "1000 lookups of a moving model");
    }
}
