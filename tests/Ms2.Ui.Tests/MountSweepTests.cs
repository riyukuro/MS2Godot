using System.Xml.Linq;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Formats.World;
using Ms2Godot.Movement;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// Every riding row (Xml riding/%08d.xml with a &lt;riding&gt; root) of a data set, as Mount.Load and StateRide use it
/// (Ridee::FUN_1417043a0, ridee.cpp:411-445): the kfm (kfm2 too) found by name, its nif and the seat in it (a node or one of
/// the ridee's effect dummies, ridee.cpp:496-500), the
/// mount's action sequences (pcactiondata groups 0 and 1001) and idleX/runX motions in the kfm, and the rider's sequences
/// (rideAniPC*, passengers' too) in both pc bodies. Rows with no kfm are the object mounts (the nif as a collide prop,
/// type object), counted apart. Needs MS2_DATA and MS2_NIFXML; prints the counts per check. Fails on a model that is not
/// found or does not load, and on a seat neither node nor dummy names unless the client has none either (the rows below).
/// The sequences missing from the data (a mount kfm without Jump_*, a rideAniPC no body has) are only counted: the client
/// plays nothing for them either.
/// </summary>
public class MountSweepTests
{
    const string Npc = "Resource/Model/Npc";

    sealed class Tally
    {
        public readonly SortedDictionary<string, (int ok, List<string> bad)> Checks = new();
        public void Add(string check, bool ok, string what)
        {
            var t = Checks.GetValueOrDefault(check, (ok: 0, bad: new List<string>()));
            if (ok) t.ok++; else t.bad.Add(what);
            Checks[check] = t;
        }
        public int Bad(string check) => Checks.TryGetValue(check, out var t) ? t.bad.Count : 0;
        public void Print(string set)
        {
            foreach (var (k, (ok, bad)) in Checks)
                TestContext.WriteLine($"{set} {k}: {ok} ok, {bad.Count} missing{(bad.Count > 0 ? " [" + string.Join(", ", bad.Take(60)) + (bad.Count > 60 ? ", ..." : "") + "]" : "")}");
        }
    }

    static NifSpec spec;

    /// <summary>Mount.ModelArchives: the archives a model file is found in by name, in the port's order.</summary>
    static readonly string[] Archives = { Npc, "Resource/Model/Map", "Resource/Model/Item", "Resource/Model/Effect" };

    static string NifName(string nif)
    {
        nif = Path.GetFileName(nif.Replace('\\', '/')).ToLowerInvariant();
        return nif.EndsWith(".nif") ? nif : nif + ".nif";
    }

    static Tally Sweep(PackSet packs)
    {
        spec ??= new NifSpec(Nif());
        var t = new Tally();
        (string a, string e) Find(string file)
        {
            foreach (var a in Archives) if (packs.FindByFileName(a, file) is { } e) return (a, e);
            return (null, null);
        }
        var actions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ax = PackXml.Parse(packs.Read("Xml", "table/pcactiondata.xml"));
        foreach (var gid in new[] { "0", "1001" })
            foreach (var v in ax.Root?.Elements("group").FirstOrDefault(g => (string)g.Attribute("groupID") == gid)?.Elements("v") ?? Enumerable.Empty<XElement>())
                actions[(string)v.Attribute("type")] = ((string)v.Attribute("names") ?? "").Split(',')[0].Trim();

        var ids = packs.List("Xml").Where(e => e.StartsWith("riding/") && e.Count(c => c == '/') == 1 && e.EndsWith(".xml"))
            .Select(e => int.TryParse(e[7..^4], out var id) ? id : -1).Where(id => id > 0).OrderBy(id => id).ToList();
        foreach (var id in ids)
        {
            if (PackXml.Parse(packs.Read("Xml", $"riding/{id:D8}.xml")).Root?.Element("riding")?.Element("basic") == null)
            { t.Add("not a riding row", false, id.ToString()); continue; }
            var d = RideData.Load(packs, id);
            if (string.IsNullOrEmpty(d.Kfm))
            {
                t.Add("object row (no kfm)", d.Type == Ms2.Ui.RideOff.TypeObject, id + (d.Type == Ms2.Ui.RideOff.TypeObject ? "" : ":not type object"));
                var (oa, oe) = Find(NifName(d.Nif ?? ""));
                t.Add("object nif found", oe != null, $"{id}:{d.Nif}");
                if (oe == null) continue;
                t.Add("object nif in " + oa[(oa.LastIndexOf('/') + 1)..], true, "");
                ModelData om = null;
                try { om = ModelData.Load(NifFile.Load(spec, packs.Read(oa, oe))); } catch (Exception) { }
                t.Add("object nif loads", om != null, $"{id}:{oe}");
                continue;
            }
            foreach (var g in d.Kfm2 is { Length: > 0 } ? new[] { 0, 1 } : new[] { 1 })
            {
                string name = d.KfmFor(g);
                var (ka, entry) = Find(name.ToLowerInvariant() + ".kfm");
                t.Add("kfm found", entry != null, $"{id}:{name}");
                if (entry == null) continue;
                if (ka != Npc) t.Add("kfm in " + ka[(ka.LastIndexOf('/') + 1)..], true, "");
                var kfm = KfmFile.Load(packs.Read(ka, entry));
                string folder = entry[..entry.LastIndexOf('/')];
                string nif = NifName(kfm.Nif.Replace('\\', '/').TrimStart('.', '/'));
                ModelData md = null; NifFile nf = null;
                try { nf = NifFile.Load(spec, packs.Read(ka, $"{folder}/{nif}")); md = ModelData.Load(nf); } catch (Exception) { }
                t.Add("nif loads", md != null, $"{id}:{folder}/{nif}");
                if (md == null) continue;
                if (!string.IsNullOrEmpty(d.RideBone))
                {
                    bool bone = md.BoneIndex(d.RideBone) >= 0, dummy = !bone && EffectDummyTable.Find(d.EffectDummies, d.RideBone) != null;
                    t.Add("rider seat (node or dummy)", bone || dummy, $"{id}:{d.RideBone}{(d.HideRider ? " hidden" : " shown")}{Why(md, nf, d.RideBone)}");
                    if (!bone) t.Add("rider seat on a dummy", dummy, $"{id}:{d.RideBone}");
                }
                foreach (var (sid, s) in d.Seats)
                    if (!string.IsNullOrEmpty(s.RideBone)) t.Add("passenger bone", md.BoneIndex(s.RideBone) >= 0, $"{id}/{sid}:{s.RideBone}");
                bool Has(string seq)
                {
                    var s = kfm.ByName(seq);
                    return s != null && packs.Exists(ka, $"{folder}/{Path.GetFileName(s.KfPath.Replace('\\', '/')).ToLowerInvariant()}");
                }
                foreach (var type in new[] { "IDLE", "WALK", "JUMP_UP", "JUMP_DOWN", "JUMP_LAND" })
                    if (actions.TryGetValue(type, out var seq)) t.Add("mount " + type, Has(seq), $"{id}:{seq}");
                if (!string.IsNullOrEmpty(d.IdleXMotion)) t.Add("mount idleXMotion", Has(d.IdleXMotion), $"{id}:{d.IdleXMotion}");
                if (!string.IsNullOrEmpty(d.RunXMotion)) t.Add("mount runXMotion", Has(d.RunXMotion), $"{id}:{d.RunXMotion}");
            }
            if (!d.HideRider)
                foreach (var (who, r) in d.Seats.Select(s => ($"{id}/{s.Key}", s.Value.Sequences)).Prepend(($"{id}", d.Rider)))
                    foreach (var seq in new[] { r.Pc, r.Idle, r.Run, r.Jump, r.SpIdle, r.SpRun, r.SpJump }.Where(s => !string.IsNullOrEmpty(s)).Distinct())
                        foreach (var body in new[] { "male", "female" })
                            t.Add("rider " + body, BodySequences.Exists(packs, body, seq), $"{who}:{seq}");
        }
        return t;
    }

    static string Why(ModelData md, NifFile nf, string bone)
    {
        if (md.Bones.FirstOrDefault(b => string.Equals(b.Name, bone, StringComparison.OrdinalIgnoreCase)) is { } c) return "(case " + c.Name + ")";
        if (string.Equals(md.RootName, bone, StringComparison.OrdinalIgnoreCase)) return "(root)";
        if (md.Bones.FirstOrDefault(b => string.Equals(b.FileName, bone, StringComparison.OrdinalIgnoreCase)) is { } f) return "(file name of " + f.Name + ")";
        for (int i = 0; i < nf.Blocks.Count; i++)
            if (string.Equals(nf.Name(i), bone, StringComparison.OrdinalIgnoreCase)) return $"(block {i} {nf.Blocks[i].Type} {nf.Name(i)})";
        return "(" + md.Bones.Count + " bones: " + string.Join("/", md.Bones.Take(6).Select(b => b.Name)) + ")";
    }

    static string Nif()
    {
        var nx = Environment.GetEnvironmentVariable("MS2_NIFXML");
        if (string.IsNullOrEmpty(nx) || !File.Exists(nx)) Assert.Ignore("MS2_NIFXML is not set");
        return nx;
    }

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    [TestCase("MS2_KMS2_DEV_DATA")]
    public void EveryMountHasItsModelAnimsAndSeats(string env)
    {
        using var packs = new PackSet(RcGap.Data(env));
        var t = Sweep(packs);
        t.Print(env);
        foreach (var check in new[] { "kfm found", "nif loads", "object nif found", "object nif loads", "passenger bone", "object row (no kfm)" })
            Assert.That(t.Bad(check), Is.Zero, check);
        Assert.That(t.Checks["rider seat (node or dummy)"].bad.Select(b => b[..b.IndexOf(':')]), Is.SubsetOf(NoSeatInTheClient), "a seat the client finds");
        Assert.That(t.Checks["rider seat on a dummy"].ok, Is.GreaterThanOrEqualTo(31), "Eff_Head / Eff_UI seats");
    }

    /// <summary>The rows whose rideBone is neither a node of the model nor a dummy, so the client makes no RideeInterpolator:
    /// Ride_A (50600231, 50600323, and GMS2's 50620006 and 50620010), Bip01 of a slime (99910060), and the Archeon's
    /// Seat_Dummy01 (99910390-99910397, 99910401).</summary>
    static readonly string[] NoSeatInTheClient =
    {
        "50600231", "50600323", "50620006", "50620010", "99910060",
        "99910390", "99910391", "99910392", "99910393", "99910394", "99910395", "99910396", "99910397", "99910401",
    };
}
