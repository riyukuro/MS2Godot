using System.Text;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// The Ride_A node an npc bound to an owner (NpcRide mode 1, CNpc m_nF20) is placed at each frame (CNpc::FUN_141093eb0,
/// unit_14108e1e0_npcactorandvibrate.cpp:1295-1307): PCBase::vfunc_119 finds it by name on the owner's model root
/// (pcbase.cpp:3039-3041). Every npc row whose model file names a Ride_A has it as a NiNode, so it is a bone of the model
/// (<see cref="ModelData.Bones"/>, the skeleton the Godot body is built from) and the owner's node is found; no pc body has
/// one, so an npc bound to a player stays where it is, as in the client.
/// </summary>
public class NpcRideNodeSweepTests
{
    const string Npc = "Resource/Model/Npc", Character = "Resource/Model/Character";
    static readonly byte[] RideA = Encoding.ASCII.GetBytes("Ride_A");

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryNpcModelWithARideANodeHasItAsABone(string env)
    {
        var dir = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore($"{env} is not set");
        using var packs = new PackSet(dir);
        var spec = new NifSpec(NifEnv.NifXml());
        // NpcLayer.ModelFor: the row's kfm by file name in the Npc pack, its nif beside it
        var kfms = new Dictionary<string, string>();
        foreach (var e in packs.List(Npc))
            if (e.EndsWith(".kfm", StringComparison.OrdinalIgnoreCase)) kfms[Path.GetFileNameWithoutExtension(e).ToLowerInvariant()] = e;
        var table = NpcTable.For(packs);
        var models = new Dictionary<string, bool>();      // nif entry -> has the bone
        var rows = new List<string>();
        var missing = new List<string>();
        int withModel = 0;
        foreach (var row in table.Rows.OrderBy(r => r.Id))
        {
            if (row.Kfm == null || !kfms.TryGetValue(row.Kfm.ToLowerInvariant(), out var kfmEntry)) continue;
            string folder = kfmEntry[..kfmEntry.LastIndexOf('/')];
            string nif = $"{folder}/{KfmFile.Load(packs.Read(Npc, kfmEntry)).Nif.Replace('\\', '/').TrimStart('.', '/').ToLowerInvariant()}";
            if (!packs.Exists(Npc, nif)) continue;
            withModel++;
            if (!models.TryGetValue(nif, out bool has))
            {
                var bytes = packs.Read(Npc, nif);
                if (bytes.AsSpan().IndexOf(RideA) < 0) has = false;
                else
                {
                    has = ModelData.Load(NifFile.Load(spec, bytes)).BoneIndex("Ride_A") >= 0;
                    if (!has) missing.Add($"{row.Id} {nif}");
                }
                models[nif] = has;
            }
            if (has) rows.Add($"{row.Id}:{row.Kfm}");
        }
        var bodies = packs.List(Character).Where(e => e.EndsWith(".nif", StringComparison.OrdinalIgnoreCase))
            .Where(e => packs.Read(Character, e).AsSpan().IndexOf(RideA) >= 0).ToList();
        TestContext.WriteLine($"{env}: {withModel} npc rows with a model, {rows.Count} with a Ride_A node " +
            $"({models.Count(m => m.Value)} models); pc bodies with one: {bodies.Count}");
        foreach (var r in rows) TestContext.WriteLine("  " + r);
        foreach (var m in missing) TestContext.WriteLine("  NOT A BONE " + m);
        Assert.That(rows, Is.Not.Empty);
        Assert.That(rows, Has.Some.StartsWith("23000097:"), "the parade car carries a Ride_A on both data sets");
        Assert.That(missing, Is.Empty, "a model names Ride_A but has no such node");
        Assert.That(bodies, Is.Empty, "a pc body has no Ride_A: an npc bound to a player is not moved");
    }
}
