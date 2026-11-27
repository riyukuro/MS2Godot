using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// Every set_breakable use in the trigger data ("SetBreakable", the KMS scripts' Korean name of it: arg1 the trigger ids,
/// arg2 on/off). The server finds each id's MS2BreakableNIF / MS2BreakableActor by its TriggerBreakableID and sends the
/// marker by its entity id (Breakable 0x4F mode 1); the client's marker of that entity (worldservice_1.cpp:6757-6768)
/// then runs CBreakableObject::Update's cycle (breakableobject.cpp:359-445). So each id must name a breakable entity of
/// the script's field, the field's markers must hold it, its NIF's PhysX meshes must stay out of the scene (only its box
/// moves with it), and a moving one's cycle must stay on its path.
/// </summary>
public class TriggerBreakableSweepTests
{
    /// <summary>The KMS scripts' name of SetBreakable ("set the moving platform").</summary>
    const string KoreanName = "\uC6C0\uC9C1\uC774\uB294\uBC1C\uD310\uC744\uC124\uC815\uD55C\uB2E4";

    internal sealed record Use(string Field, string File, int Id, bool On);

    /// <summary>Every use, one row per trigger id, in entry order.</summary>
    internal static List<Use> Uses(PackSet packs, out int actions)
    {
        var uses = new List<Use>();
        actions = 0;
        foreach (var entry in packs.List("Xml").Where(e => e.StartsWith("trigger/", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e, StringComparer.Ordinal))
        {
            var parts = entry.Split('/');
            if (parts.Length != 3) continue;
            System.Xml.Linq.XDocument doc;
            try { doc = PackXml.Parse(packs.Read("Xml", entry)); } catch (System.Xml.XmlException) { continue; }
            foreach (var a in doc.Descendants("action"))
            {
                var name = (string)a.Attribute("name") ?? "";
                if (name != KoreanName && !name.Equals("SetBreakable", StringComparison.OrdinalIgnoreCase)) continue;
                actions++;
                bool on = ((string)a.Attribute("arg2") ?? "0").Trim() == "1";
                foreach (var id in ((string)a.Attribute("arg1") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    uses.Add(new(parts[1], entry, int.TryParse(id, out int n) ? n : int.MinValue, on));
            }
        }
        return uses;
    }

    /// <summary>The uses whose id names an entity that is no breakable, or whose marker is missing, leaves its path or falls short
    /// of its end. Ids that name no entity of the field at all, and scripts whose field the data set lacks, are listed apart (nothing moves).</summary>
    static (List<string> Bad, int Actions, int Ids, int Moving, int Fields) Sweep(string variable)
    {
        using var packs = new PackSet(RcGap.Data(variable));
        var uses = Uses(packs, out int actions);
        var bad = new List<string>();
        var unnamed = new List<string>();
        int moving = 0;
        var flats = new FlatLibrary(packs);
        var byField = uses.GroupBy(u => u.Field).ToList();
        foreach (var g in byField)
        {
            FieldMap map;
            try { map = FieldMap.Load(packs, flats, g.Key); }
            catch (Exception ex) when (ex is FileNotFoundException or KeyNotFoundException or InvalidDataException)
            {
                // a script of a field this data set does not ship (GMS2 has the scripts of 52020008/52020014, not the fields):
                // nobody enters it, so nothing moves
                unnamed.Add($"{g.Key}: no field ({ex.GetType().Name})");
                continue;
            }
            var b = new BreakableObjects();
            b.AttachField(map.Entities);
            var byId = new Dictionary<int, FieldEntity>();
            foreach (var e in map.Entities)
                if (e.HasMixin("MS2Breakable") && e.Has("TriggerBreakableID")) byId[(int)e.Number("TriggerBreakableID")] = e;
            foreach (var u in g.DistinctBy(u => u.Id))
            {
                if (!byId.TryGetValue(u.Id, out var e))
                {
                    // no entity of the field has the id: the server skips it (TryGetBreakable) and the client gets nothing
                    if (!map.Entities.Any(x => x.Has("TriggerBreakableID") && (int)x.Number("TriggerBreakableID") == u.Id)) unnamed.Add($"{u.File} {u.Id}");
                    else bad.Add($"{u.File}: id {u.Id} names an entity that is no breakable");
                    continue;
                }
                var m = b.Find(e.Raw.Id);
                if (m == null) { bad.Add($"{u.File}: id {u.Id} ({e.Raw.Id}) has no marker"); continue; }
                // its NIF's PhysX meshes stay out of the scene (no PhysXProp model): only the box that follows the marker
                // collides, so nothing at rest holds a rider back
                if (BreakableObjects.NifPhysX(e)) bad.Add($"{u.File}: id {u.Id} ({e.Flat?.Name}) keeps its NIF PhysX meshes");
                if (!m.Moves) continue;
                moving++;
                // the cycle (wait, go, wait, back) at 50 ms steps and at the end of the go: the marker stays on its segment and
                // reaches its end; a wait of a minute or more is sampled for a minute only (some hold for days)
                double dist = Math.Abs(e.Number("moveDistance"));
                b.Apply(new BreakableUpdate(1, new[] { new BreakableEntry(m.Name, 2, 1, 0, 0) }));
                // the cycle time is the step's server tick plus the delay; the go ends at cycle time `go`, and a cycle that
                // ends there (mode 1 without an end wait) wraps to 0 at it, so its last millisecond is the farthest
                long go = (long)(int)(long)(m.WaitStart * 1000f) + (int)(long)(m.MoveTime * 1000f);
                long delay = (int)(long)(m.MoveDelay * 1000f);
                double peak = go - delay;
                while (peak < 1) peak += go + (m.MoveMode == 1 ? (int)(long)(m.WaitEnd * 1000f) : (int)(long)(m.WaitEnd * 1000f) + (int)(long)(m.MoveTime * 1000f));
                double end = m.MoveMode == 1 || m.WaitEnd >= 60 ? peak + 100 + Math.Min(m.WaitEnd, 60) * 1000.0 : peak + (m.WaitEnd + m.MoveTime) * 1000.0 + 100;
                var times = new SortedSet<double> { peak, peak - 1 };
                for (double t = 0; t <= end; t += 50) times.Add(t);
                double far = 0;
                foreach (double t in times)
                {
                    b.Update(1000 + (long)t, (int)t);   // the cycle time is the server tick: 0 at the first step
                    var d = m.Position - m.PosBase;
                    double len = Math.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
                    if (!double.IsFinite(len) || len > dist + 0.01) { bad.Add($"{u.File}: id {u.Id} off its path at {t} ms ({len:0.##} of {dist})"); break; }
                    far = Math.Max(far, len);
                }
                // the go's last millisecond: one step of the speed short at most
                if (far < dist - Math.Max(0.5, Math.Abs(m.Speed) * 0.001 + 0.01)) bad.Add($"{u.File}: id {u.Id} reaches {far:0.##} of {dist}");
            }
        }
        int ids = uses.Select(u => (u.Field, u.Id)).Distinct().Count();
        TestContext.WriteLine($"{variable}: {actions} actions, {uses.Count} id uses, {ids} distinct ids in {byField.Count} fields, {moving} moving; bad: {(bad.Count == 0 ? "none" : string.Join("; ", bad))}; {unnamed.Count} ids name no entity of their field, or their field is not in the data: {string.Join("; ", unnamed)}");
        return (bad, actions, ids, moving, byField.Count);
    }

    [Test]
    public void EveryKmsSetBreakableIdNamesAMarkerOfItsFieldAndItsCycleStaysOnItsPath()
    {
        var (bad, actions, ids, moving, _) = Sweep("MS2_DATA");
        Assert.That(actions, Is.GreaterThan(1000));
        Assert.That(moving, Is.GreaterThan(100));
        Assert.That(bad, Is.Empty);
    }

    [Test]
    public void EveryGms2SetBreakableIdNamesAMarkerOfItsFieldAndItsCycleStaysOnItsPath()
    {
        var (bad, actions, ids, moving, _) = Sweep("MS2_GMS2_DATA");
        Assert.That(actions, Is.GreaterThan(1000));
        Assert.That(moving, Is.GreaterThan(100));
        Assert.That(bad, Is.Empty);
    }
}
