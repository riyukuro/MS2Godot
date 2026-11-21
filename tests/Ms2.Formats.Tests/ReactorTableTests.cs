using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>CReactorDataTable (commonfiles/reactordata.cpp): LoadStates @0x14076b560, LoadReactors @0x14076b2e0.</summary>
public class ReactorTableTests
{
    static XDocument X(string s) => XDocument.Parse(s);

    const string States = """
        <ms2>
          <reactorState id="100" type="connect" expireTimeSec="60" kfmName="Eff_Fpot_01" startAni="s0" idleAni="i0" actionStringKey="k0">
            <condItem code="30001095" consume="1" />
            <condItem code="5" />
          </reactorState>
          <reactorState id="101" type="SLEEP" nextTimeSec="15" kfmName="Eff_Fpot_01" idleAni="i1"><condItem /></reactorState>
          <reactorState id="102" type="reward" kfmName="Eff_Fpot_01" />
          <reactorState id="103" type="other" />
        </ms2>
        """;

    [Test]
    public void StatesReadTheirFieldsAndOnlyTheFirstCondItem()
    {
        var t = new ReactorTable(X(States), X("<ms2/>"));
        var s = t.GetState(100);
        Assert.That((s.Type, s.ExpireTimeSec, s.KfmName, s.StartAni, s.IdleAni, s.ActionStringKey),
            Is.EqualTo((1, 60, "Eff_Fpot_01", "s0", "i0", "k0")));
        // the loader takes FirstChild("condItem") only; rank 1 and checkCount 0 by default
        Assert.That(s.Conditions, Is.EqualTo(new[] { new ReactorTable.Condition(30001095, 1, 1, 0) }));
        Assert.That(t.GetState(101).Type, Is.EqualTo(2));
        Assert.That(t.GetState(101).Conditions, Is.Empty);   // a condItem without a code gives no condition
        Assert.That(t.GetState(102).Type, Is.EqualTo(3));
        Assert.That(t.GetState(103).Type, Is.EqualTo(0));
    }

    [Test]
    public void AReactorResolvesItsStateListInOrderAndItsDefaults()
    {
        var t = new ReactorTable(X(States), X("""
            <ms2>
              <reactor id="7" reactDistance="155.5" reactorStateList="[102, 100,101]" connectedEffect="Item/Eff.xml" gameEventId="900051"
                rewardItemListXml='&lt;items&gt;&#x0A;&lt;v itemID=&quot;30001097&quot; count=&quot;2&quot; grade=&quot;3&quot; /&gt;&#x0A;&lt;/items&gt;'
                createAnimation="A,B" reactAnimation="C" />
            </ms2>
            """));
        var r = t.Get(7);
        Assert.That(r.States.Select(s => s.Id), Is.EqualTo(new[] { 102, 100, 101 }));
        Assert.That((r.ReactDistance, r.ReactTimeMSec, r.ConnectedEffect, r.GameEventId), Is.EqualTo((155.5f, 15000, "Item/Eff.xml", 900051u)));
        Assert.That(r.CreateAnimations, Is.EqualTo(new[] { "A", "B" }));
        Assert.That(r.ReactAnimations, Is.EqualTo(new[] { "C" }));
        Assert.That(r.Rewards, Is.EqualTo(new[] { new ReactorTable.Reward(30001097, 2, 3) }));
        // GetStateData is an index into the list: null below 0 and past the end
        Assert.That(r.StateAt(1).Id, Is.EqualTo(100));
        Assert.That(r.StateAt(-1), Is.Null);
        Assert.That(r.StateAt(3), Is.Null);
    }

    [Test]
    public void AMissingStateDropsTheReactorAndTheFilterSkipsRows()
    {
        var t = new ReactorTable(X(States), X("""
            <ms2>
              <reactor id="1" reactorStateList="[100, 999]" />
              <reactor id="2" reactorStateList="[100]" locale="CN" />
              <reactor id="3" reactorStateList="[100]" locale="KR" reactTimeMSec="1500" />
            </ms2>
            """), e => (string)e.Attribute("locale") != "CN");
        Assert.That(t.Get(1), Is.Null);
        Assert.That(t.Get(2), Is.Null);
        Assert.That(t.Get(3).ReactTimeMSec, Is.EqualTo(1500));
    }

    [Test]
    public void TheDataHasTheFlowerPotReactor()
    {
        var data = System.Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data) || !System.IO.Directory.Exists(data)) Assert.Ignore("MS2_DATA not set");
        using var packs = new PackSet(data);
        var t = new ReactorTable(packs, e => (string)e.Attribute("locale") is null or "KR");
        var r = t.Get(10000000);
        Assert.That(r, Is.Not.Null);
        Assert.That(r.States.Select(s => s.Id), Is.EqualTo(new[] { 100, 101, 102, 103 }));
        Assert.That((r.ReactDistance, r.ReactTimeMSec, r.ConnectedEffect), Is.EqualTo((155f, 1500, "Item/Eff_Flower_MyPin.xml")));
        Assert.That(r.States[0].Conditions[0].Code, Is.EqualTo(30001095));
    }
}
