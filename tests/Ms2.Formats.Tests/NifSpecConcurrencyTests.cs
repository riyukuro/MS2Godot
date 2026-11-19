using Ms2.Formats.Nif;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// One NifSpec serves the whole process (the field's navmesh loads on a worker while the main thread builds models): its
/// field and expression lists, filled on first use, give the same answers when many threads fill them at once.
/// </summary>
public class NifSpecConcurrencyTests
{
    [Test]
    public void FieldListsFilledConcurrentlyMatchASerialFill()
    {
        var nx = NifEnv.NifXml();
        var root = System.Xml.Linq.XDocument.Load(nx).Root!;
        var names = root.Elements().Where(e => e.Name.LocalName is "struct" or "niobject")
            .Select(e => (string)e.Attribute("name")).Where(n => n != null).Distinct().ToArray();
        Assert.That(names, Has.Length.GreaterThan(100));

        static string Describe(List<NifField> f) => string.Join("|", f.Select(x => $"{x.Name}:{x.Type}:{x.Template}:{x.Cond != null}:{x.Length != null}:{x.Args.Length}"));
        static string Fields(NifSpec spec, string n) { try { return Describe(spec.Fields(n)); } catch (KeyNotFoundException) { return "missing"; } }

        var serial = new NifSpec(nx);
        var want = names.ToDictionary(n => n, n => Fields(serial, n));

        var shared = new NifSpec(nx);
        var got = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
        // every thread walks the whole list from a different start, so the same names are filled at the same time
        Parallel.For(0, 8, new ParallelOptions { MaxDegreeOfParallelism = 8 }, t =>
        {
            for (int i = 0; i < names.Length; i++)
            {
                var n = names[(i + t * names.Length / 8) % names.Length];
                var d = Fields(shared, n);
                got.AddOrUpdate(n, d, (_, old) => old == d ? d : "MISMATCH " + old + " / " + d);
            }
        });
        foreach (var n in names)
            Assert.That(got[n], Is.EqualTo(want[n]), n);
    }
}
