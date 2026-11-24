using System.Numerics;
using Ms2.Formats.Model;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>A drop's effects and sounds (CDropItem::SetState 1, 0 and 2, the dtor and OnPickedUp, dropitem.cpp:431-1036).</summary>
public class DropEffectsTests {
    sealed class Fx : IRegionSkillEffects {
        public readonly List<(string Name, Xform At)> Spawned = new();
        public readonly List<(int Handle, float Seconds)> Faded = new();
        public bool Has(string effect) => true;
        public int Spawn(string effect, Xform at, bool always, int randomIndex) { Spawned.Add((effect, at)); return Spawned.Count; }
        public void Insert(string effect, Xform at, bool always, int randomIndex) { }
        public void FadeOut(int handle, float seconds) => Faded.Add((handle, seconds));
    }

    static DropStyle Row(string be = "", string drop = "", string get = "") => new() { BeEffect = be, DropSoundId = drop, GetSoundId = get };

    [Test] public void TheThrowPlaysTheRowsDropSoundWithTheSourcesKind() {
        var sounds = new List<(string, string, int, Vector3)>();
        var e = new DropEffects(new Fx(), (g, n, k, at) => sounds.Add((g, n, k, at)));
        Assert.That(e.Thrown(Row(drop: "Item_Drop_Coin"), 5, new Vector3(1, 2, 3)), Is.True);
        Assert.That(sounds, Is.EqualTo(new[] { ("ms2sound/sound/item", "Item_Drop_Coin", 5, new Vector3(1, 2, 3)) }));
        Assert.That(e.Thrown(Row(), 5, Vector3.Zero), Is.False, "a row with no dropSoundID plays nothing");
        Assert.That(sounds, Has.Count.EqualTo(1));
    }

    [Test] public void RestingSpawnsTheGradeEffectThenTheBeEffectAtTheDropWithNoRotation() {
        var fx = new Fx();
        var e = new DropEffects(fx, null);
        e.Rested("Eff_Item_Grade3", Row(be: "Eff_Item_Shine"), new Vector3(10, 20, 30));
        Assert.That(fx.Spawned.Select(s => s.Name), Is.EqualTo(new[] { "Eff_Item_Grade3", "Eff_Item_Shine" }));
        foreach (var (_, at) in fx.Spawned) {
            Assert.That((at.T.X, at.T.Y, at.T.Z, at.S), Is.EqualTo((10d, 20d, 30d, 1d)));
            Assert.That(at.R, Is.EqualTo(new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }), "DAT_143a09600");
        }
        Assert.That((e.EffectA, e.EffectB), Is.EqualTo((1, 2)));
    }

    [Test] public void NoBeEffectAndNoGradeEffectSpawnNothing() {
        var fx = new Fx();
        var e = new DropEffects(fx, null);
        e.Rested("Eff_Item_Grade1", Row(), Vector3.Zero);
        Assert.That(fx.Spawned.Select(s => s.Name), Is.EqualTo(new[] { "Eff_Item_Grade1" }));
        e.Stop();
        fx.Spawned.Clear();
        e.Rested("", Row(), Vector3.Zero);
        Assert.That(fx.Spawned, Is.Empty);
    }

    [Test] public void StopEndsBothKeptEffectsAtOnceAndOnlyOnce() {
        var fx = new Fx();
        var e = new DropEffects(fx, null);
        e.Rested("Eff_Item_Grade4", Row(be: "Eff_Item_Shine"), Vector3.Zero);
        e.Stop();
        Assert.That(fx.Faded, Is.EqualTo(new[] { (1, 0f), (2, 0f) }), "FUN_1411a6060 on m_nEffectIdA and m_nEffectIdB");
        e.Stop();
        Assert.That(fx.Faded, Has.Count.EqualTo(2));
    }

    [Test] public void APickupByTheLocalPlayerPlaysTheGetSoundAtThePickerAsKindOne() {
        var sounds = new List<(string, string, int, Vector3)>();
        var e = new DropEffects(new Fx(), (g, n, k, at) => sounds.Add((g, n, k, at)));
        Assert.That(e.PickedUp(Row(get: "Item_Get_Coin"), new Vector3(7, 8, 9)), Is.True);
        Assert.That(sounds, Is.EqualTo(new[] { ("ms2sound/sound/item", "Item_Get_Coin", 1, new Vector3(7, 8, 9)) }));
        Assert.That(e.PickedUp(Row(), Vector3.Zero), Is.False);
    }
}
