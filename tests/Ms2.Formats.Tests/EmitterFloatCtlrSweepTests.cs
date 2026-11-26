using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// The particle emitters' float controllers (NiPSEmitterFloatCtlr: speed, radius = particle size, declination, planar angle,
/// life span, rotation, each with its variance) and the gravity strength controller (NiPSGravityStrengthCtlr).
/// - The client: each controller finds its emitter or force by name (NiPSEmitterCtlr.cpp:67, GetEmitterByName;
///   NiPSForceCtlr, GetForceByName), runs only when active (NiTimeController::DontDoUpdate, NiTimeController.inl:152) and
///   stores its interpolator's value at its scaled time through the value's plain setter (SetTargetFloatValue,
///   e.g. NiPSEmitterRadiusCtlr.cpp:40 SetSize, NiPSGravityStrengthCtlr.cpp:72 SetStrength).
/// - Here: <see cref="EffectScene"/> binds each one to its emitter's <see cref="EmitterDef.Anims"/> slot or its force's
///   <see cref="ForceDef.StrengthAnim"/>, and <see cref="EmitterDef.At"/> / <see cref="ForceDef.StrengthAt"/> give the value.
/// The sweep reads every such controller of every effect nif in both data sets on its own and checks that the scene took
/// it as the client would, the value at each key, that none of these types is left unsupported, and that reading the
/// values allocates nothing.
/// </summary>
public class EmitterFloatCtlrSweepTests
{
    const string Archive = "Resource/Model/Effect";

    static readonly Dictionary<string, EmitterField> Fields = new()
    {
        ["NiPSEmitterSpeedCtlr"] = EmitterField.Speed, ["NiPSEmitterSpeedVarCtlr"] = EmitterField.SpeedVar, ["NiPSEmitterSpeedFlipRatioCtlr"] = EmitterField.SpeedFlipRatio,
        ["NiPSEmitterDeclinationCtlr"] = EmitterField.Declination, ["NiPSEmitterDeclinationVarCtlr"] = EmitterField.DeclinationVar,
        ["NiPSEmitterPlanarAngleCtlr"] = EmitterField.PlanarAngle, ["NiPSEmitterPlanarAngleVarCtlr"] = EmitterField.PlanarAngleVar,
        ["NiPSEmitterRadiusCtlr"] = EmitterField.Size, ["NiPSEmitterRadiusVarCtlr"] = EmitterField.SizeVar,
        ["NiPSEmitterLifeSpanCtlr"] = EmitterField.Lifespan, ["NiPSEmitterLifeSpanVarCtlr"] = EmitterField.LifespanVar,
        ["NiPSEmitterRotAngleCtlr"] = EmitterField.RotAngle, ["NiPSEmitterRotAngleVarCtlr"] = EmitterField.RotAngleVar,
        ["NiPSEmitterRotSpeedCtlr"] = EmitterField.RotSpeed, ["NiPSEmitterRotSpeedVarCtlr"] = EmitterField.RotSpeedVar,
    };

    static double Get(in EmitterValues v, EmitterField f) => f switch
    {
        EmitterField.Speed => v.Speed, EmitterField.SpeedVar => v.SpeedVar, EmitterField.SpeedFlipRatio => v.SpeedFlipRatio,
        EmitterField.Declination => v.Declination, EmitterField.DeclinationVar => v.DeclinationVar,
        EmitterField.PlanarAngle => v.PlanarAngle, EmitterField.PlanarAngleVar => v.PlanarAngleVar,
        EmitterField.Size => v.Size, EmitterField.SizeVar => v.SizeVar, EmitterField.Lifespan => v.Lifespan, EmitterField.LifespanVar => v.LifespanVar,
        EmitterField.RotAngle => v.RotAngle, EmitterField.RotAngleVar => v.RotAngleVar, EmitterField.RotSpeed => v.RotSpeed, _ => v.RotSpeedVar,
    };

    static double Stored(EmitterDef e, EmitterField f) => Get(new EmitterValues
    {
        Speed = e.Speed, SpeedVar = e.SpeedVar, SpeedFlipRatio = e.SpeedFlipRatio, Declination = e.Declination, DeclinationVar = e.DeclinationVar,
        PlanarAngle = e.PlanarAngle, PlanarAngleVar = e.PlanarAngleVar, Size = e.Size, SizeVar = e.SizeVar, Lifespan = e.Lifespan, LifespanVar = e.LifespanVar,
        RotAngle = e.RotAngle, RotAngleVar = e.RotAngleVar, RotSpeed = e.RotSpeed, RotSpeedVar = e.RotSpeedVar,
    }, f);

    /// <summary>A controller's interpolator keys (time, value, interpolation), or the pose as one key, or null when it gives no value.</summary>
    static (double t, double v)[] Keys(NifFile n, long interp, out long kind)
    {
        kind = -1;
        if (interp < 0) return null;
        var e = n.Blocks[(int)interp];
        if (e.Type != "NiFloatInterpolator") { kind = -2; return null; }
        long di = e.IntOr("Data", -1);
        if (di >= 0 && n.Blocks[(int)di].TryGetValue("Data", out var g) && g is NifStruct grp && grp.IntOr("Num Keys", 0) > 0)
        {
            kind = grp.Int("Interpolation");
            return grp.Structs("Keys").Select(k => ((double)k.Float("Time"), k["Value"] is float f ? (double)f : Convert.ToDouble(k["Value"]))).ToArray();
        }
        kind = 0;
        float pose = e.Float("Value");
        return pose > -float.MaxValue ? new[] { (0.0, (double)pose) } : null;
    }

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryEmitterFloatAndGravityStrengthControllerDrivesItsValue(string env)
    {
        var dir = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore($"{env} not set");
        using var packs = new PackSet(dir);
        var spec = new NifSpec(NifEnv.NifXml());
        var marker = System.Text.Encoding.ASCII.GetBytes("NiPS");
        int nifs = 0, ctlrs = 0, bound = 0, inactive = 0, noName = 0, noValue = 0, keyChecks = 0;
        var byType = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var kinds = new SortedDictionary<long, int>();
        var bad = new List<string>();
        var anims = new List<EmitterDef>();
        var forces = new List<ForceDef>();
        foreach (var entry in packs.List(Archive).Where(e => e.EndsWith(".nif", StringComparison.Ordinal)).OrderBy(e => e, StringComparer.Ordinal))
        {
            var bytes = packs.Read(Archive, entry);
            if (bytes.AsSpan().IndexOf(marker) < 0) continue;
            NifFile nif;
            EffectScene sc;
            try { nif = NifFile.Load(spec, bytes); sc = EffectScene.Load(nif); }
            catch (Exception) { continue; }   // the nifs that do not load are listed by the particle census, not here
            nifs++;
            foreach (var t in sc.Unsupported)
                if (Fields.ContainsKey(t) || t == "NiPSGravityStrengthCtlr") bad.Add($"{entry}: {t} listed unsupported");
            var B = nif.Blocks;
            // the last controller of a value in its object's chain is the one that sets it last in a frame
            var last = new Dictionary<(int sys, string name, string field), int>();
            var chains = new List<(int owner, int ctl)>();
            for (int i = 0; i < B.Count; i++)
            {
                if (!B[i].TryGetValue("Controller", out var cv) || cv is not long first || first < 0) continue;
                long ci = first; int guard = 0;
                while (ci >= 0 && guard++ < 64) { chains.Add((i, (int)ci)); ci = B[(int)ci].IntOr("Next Controller", -1); }
            }
            foreach (var (owner, ci) in chains)
            {
                var c = B[ci];
                bool gravity = c.Type == "NiPSGravityStrengthCtlr";
                if (!gravity && !Fields.ContainsKey(c.Type)) continue;
                ctlrs++;
                byType[c.Type] = byType.GetValueOrDefault(c.Type) + 1;
                int target = (int)c.IntOr("Target", owner);
                string name = nif.Str(c.IntOr(gravity ? "Force Name" : "Emitter Name", -1));
                if ((c.IntOr("Flags", 0) & 8) == 0) { inactive++; continue; }
                var keys = Keys(nif, c.IntOr("Interpolator", -1), out long kind);
                kinds[kind] = kinds.GetValueOrDefault(kind) + 1;
                if (kind == -2) bad.Add($"{entry}: {c.Type} has a {B[(int)c.IntOr("Interpolator", -1)].Type}");
                if (keys == null) { noValue++; continue; }
                if (kind == 3) bad.Add($"{entry}: {c.Type} has TCB keys, read here as linear");
                last[(target, name, c.Type)] = ci;
            }
            foreach (var ((target, name, type), ci) in last)
            {
                var c = B[ci];
                var sys = sc.NodeOfBlock.TryGetValue(target, out int node) ? sc.Systems.FirstOrDefault(s => s.Node == node) : null;
                if (sys == null) { bad.Add($"{entry}: {type} targets block {target}, not a particle system"); continue; }
                var ctl = TimeCtl.Of(c);
                var keys = Keys(nif, c.IntOr("Interpolator", -1), out long kind);
                EmitterAnim anim;
                string at = $"{entry} {type} '{name}'";
                if (type == "NiPSGravityStrengthCtlr")
                {
                    var f = sys.Forces.FirstOrDefault(f => f.Name == name && f.Kind == ForceKind.Gravity);
                    if (f == null) { noName++; continue; }
                    if (f.StrengthAnim == null) { bad.Add($"{at}: not bound"); continue; }
                    anim = f.StrengthAnim; forces.Add(f);
                }
                else
                {
                    var em = sys.Emitters.Where(e => e.Name == name).ToList();
                    if (em.Count == 0) { noName++; continue; }
                    var e = em[0];
                    anim = e.Anims?[(int)Fields[type]];
                    if (anim == null) { bad.Add($"{at}: not bound"); continue; }
                    anims.Add(e);
                    // At gives the controller's value in its slot and the stored value in every slot without one
                    double ts = ctl.Start + 0.37 * (ctl.Stop - ctl.Start);
                    var v = e.At(ts);
                    foreach (EmitterField fld in Enum.GetValues<EmitterField>())
                    {
                        double want = e.Anims[(int)fld] is { } a ? a.At(ts) : Stored(e, fld);
                        if (Math.Abs(Get(v, fld) - want) > 1e-9) bad.Add($"{at}: At({ts}) {fld} {Get(v, fld)}, want {want}");
                    }
                }
                bound++;
                if (anim.Ctl.Start != ctl.Start || anim.Ctl.Stop != ctl.Stop || anim.Ctl.Freq != ctl.Freq || anim.Ctl.Phase != ctl.Phase || anim.Ctl.Cycle != ctl.Cycle)
                    bad.Add($"{at}: timing not the controller's");
                // the value at each key: the key's value (a step key holds until the next one)
                for (int k = 0; k < keys.Length; k++)
                {
                    double kt = keys[k].t + (kind == 5 && k + 1 < keys.Length ? 1e-6 : 0);
                    double got = anim.Curve(kt), want = keys[k].v;
                    keyChecks++;
                    if (Math.Abs(got - want) > 1e-4 * Math.Max(1, Math.Abs(want))) bad.Add($"{at}: key {k} at {keys[k].t}: {got}, want {want}");
                }
            }
        }
        TestContext.Out.WriteLine($"{env}: {nifs} particle nifs, {ctlrs} float controllers ({string.Join(", ", byType.Select(kv => $"{kv.Key} {kv.Value}"))}), " +
                                  $"{bound} bound, {inactive} inactive, {noValue} without a value, {noName} naming no emitter or force; " +
                                  $"interpolation {string.Join(" ", kinds.Select(kv => $"{kv.Key}:{kv.Value}"))}; {keyChecks} key values checked");
        foreach (var b in bad.Take(40)) TestContext.Out.WriteLine(b);
        Assert.That(bad, Is.Empty);
        Assert.That(bound, Is.GreaterThan(300), "the shipped effects use these controllers");

        // reading every bound value over a second at 60 frames allocates nothing
        double Run()
        {
            double s = 0;
            for (int fr = 0; fr < 60; fr++)
            {
                double t = fr / 60.0;
                foreach (var e in anims) s += e.At(t).Size;
                foreach (var f in forces) s += f.StrengthAt(t);
            }
            return s;
        }
        Run();
        long before = GC.GetAllocatedBytesForCurrentThread();
        Run();
        long bytesUsed = GC.GetAllocatedBytesForCurrentThread() - before;
        TestContext.Out.WriteLine($"{env}: {anims.Count} emitters and {forces.Count} forces read 60 times: {bytesUsed} B");
        Assert.That(bytesUsed, Is.Zero);
    }

    /// <summary>ch/common/eff_fire_breath_01.nif: its radius and speed controllers drive the emitter's particle size and speed.</summary>
    [Test]
    public void FireBreathsRadiusAndSpeedControllersDriveSizeAndSpeed()
    {
        var (packs, spec) = NifEnv.Open();
        var nif = NifFile.Load(spec, packs.Read(Archive, "ch/common/eff_fire_breath_01.nif"));
        var sc = EffectScene.Load(nif);
        Assert.That(sc.Unsupported, Has.None.StartsWith("NiPSEmitter"));
        var driven = sc.Systems.SelectMany(s => s.Emitters).Where(e => e.Anims != null).ToList();
        Assert.That(driven, Is.Not.Empty);
        bool size = false, speed = false, changes = false;
        foreach (var e in driven)
        {
            foreach (var fld in new[] { EmitterField.Size, EmitterField.Speed })
            {
                var a = e.Anims[(int)fld];
                if (a == null) continue;
                if (fld == EmitterField.Size) size = true; else speed = true;
                // the value is the controller's curve at each time (a flat curve too), not the stored value
                var seen = new HashSet<double>();
                for (int k = 0; k <= 20; k++)
                {
                    double t = a.Ctl.Start + k * (a.Ctl.Stop - a.Ctl.Start) / 20;
                    double x = Get(e.At(t), fld);
                    Assert.That(x, Is.EqualTo(a.At(t)), $"{e.Name} {fld} at {t} is the controller's value");
                    seen.Add(Math.Round(x, 6));
                }
                TestContext.Out.WriteLine($"{e.Name} {fld}: stored {Stored(e, fld)}, values {string.Join(" ", seen.Take(8))}");
                changes |= seen.Count > 1;
            }
        }
        Assert.That(size && speed, Is.True, "the radius controller drives the size and the speed controller the speed");
        Assert.That(changes, Is.True, "a driven value changes over its controller's window");
    }
}
