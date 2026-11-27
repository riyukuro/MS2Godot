using System.Xml.Linq;
using Maple2.PacketLib.Tools;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The trigger's VisibleMyPC: the Maple2 server adds field property 3 (HideMyPC) to hide the own player and removes it to show
/// it again. The client applies kind 3 with FieldPropertyHideMyPCCli::vfunc_4 (AddHideReason(1) on the local player and its
/// ridee) and unapplies it with vfunc_5 (fieldpropertycli.cpp:146-171), from AddFieldProperty, RemoveFieldProperty and
/// ClearFieldProperties (worldservice_1.cpp:5340-5378).
/// </summary>
public class VisibleMyPcTests
{
    /// <summary>A local player's hide reason 1 as the property's apply and unapply set it.</summary>
    sealed class Own
    {
        public bool Hidden;
        public int Calls;
        public Own(FieldProperties f) => f.HideMyPCApplied += on => { Hidden = on; Calls++; };
    }

    static void Recv(FieldProperties f, string hex, ProtocolProfile profile) =>
        f.Apply(FieldPropertyMessage.Read(new ByteReader(Convert.FromHexString(hex)), profile));

    [Test]
    public void KindThreeHidesTheOwnPlayerUntilItsRemoveOrAFieldLeave()
    {
        foreach (var profile in new[] { ProtocolProfile.Kms2525, ProtocolProfile.Gms2 })
        {
            var f = new FieldProperties();
            var own = new Own(f);
            Recv(f, "0203", profile);                       // not registered: RemoveFieldProperty returns before vfunc_5
            Assert.That((own.Calls, f.HidesMyPC), Is.EqualTo((0, false)), profile.Name);
            Recv(f, "0103", profile);
            Assert.That((own.Hidden, f.HidesMyPC), Is.EqualTo((true, true)), profile.Name);
            Recv(f, "0203", profile);
            Assert.That((own.Hidden, f.HidesMyPC, own.Calls), Is.EqualTo((false, false, 2)), profile.Name);
            Recv(f, "000200000003" + "07" + "02", profile);  // the field's list on entry: kind 3 with a weather record
            Assert.That((own.Hidden, f.Weather), Is.EqualTo((true, (byte)2)), profile.Name);
            Recv(f, "0103", profile);                       // a second add applies again (SetAt does not unapply the old)
            Assert.That((own.Hidden, own.Calls), Is.EqualTo((true, 4)), profile.Name);
            f.Clear();                                      // ClearFieldProperties unapplies every registered record
            Assert.That((own.Hidden, f.HidesMyPC, f.Count, own.Calls), Is.EqualTo((false, false, 0, 5)), profile.Name);
            f.Clear();
            Assert.That(own.Calls, Is.EqualTo(5), profile.Name);
        }
    }

    [Test]
    public void OtherKindsDoNotTouchTheOwnPlayer()
    {
        var f = new FieldProperties();
        var own = new Own(f);
        f.Apply(new FieldPropertyMessage.Add(new FieldProperty.LockMyPC()));
        f.Apply(new FieldPropertyMessage.Add(new FieldProperty.Weather(1)));
        f.Apply(new FieldPropertyMessage.Remove(4));
        f.Clear();
        Assert.That(own.Calls, Is.Zero);
    }

    static string Attr(XElement e, string name) =>
        (string)e.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? "";

    /// <summary>
    /// Every VisibleMyPC of the trigger xmls: its isVisible decodes as the Maple2 server's bool ("1"/"true" shows), the
    /// packet it sends (an add of kind 3 to hide, a remove of kind 3 to show) reads on both protocols, and applied in the
    /// script's order from both starting states it leaves the own player hidden exactly when the use hides it.
    /// </summary>
    static void EveryUse(string variable)
    {
        using var packs = new PackSet(RcGap.Data(variable));
        int uses = 0, hides = 0, shows = 0, files = 0;
        var shapes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var fields = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in packs.List("Xml").Where(e => e.StartsWith("trigger/", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e, StringComparer.Ordinal))
        {
            var bytes = packs.Read("Xml", entry);
            if (bytes.AsSpan().IndexOf("VisibleMyPC"u8) < 0) continue;
            XDocument doc;
            try { doc = PackXml.Parse(bytes); } catch (System.Xml.XmlException) { continue; }
            var acts = doc.Descendants("action").Where(a => Attr(a, "name").Equals("VisibleMyPC", StringComparison.OrdinalIgnoreCase)).ToList();
            if (acts.Count == 0) continue;
            files++;
            fields.Add(entry.Split('/')[1]);
            foreach (var profile in new[] { ProtocolProfile.Kms2525, ProtocolProfile.Gms2 })
                foreach (bool start in new[] { false, true })
                {
                    var f = new FieldProperties();
                    var own = new Own(f);
                    if (start) Recv(f, "0103", profile);
                    foreach (var a in acts)
                    {
                        string raw = Attr(a, "isVisible").Trim();
                        bool visible = raw == "1" || raw.Equals("true", StringComparison.OrdinalIgnoreCase);
                        Assert.That(raw is "0" or "1" || bool.TryParse(raw, out _), Is.True, $"{entry}: isVisible '{raw}'");
                        Recv(f, visible ? "0203" : "0103", profile);
                        Assert.That((own.Hidden, f.HidesMyPC), Is.EqualTo((!visible, !visible)), $"{entry} {profile.Name}: isVisible '{raw}'");
                        if (profile == ProtocolProfile.Kms2525 && !start)
                        {
                            uses++;
                            if (visible) shows++; else hides++;
                            shapes[raw] = shapes.GetValueOrDefault(raw) + 1;
                        }
                    }
                    f.Clear();                                // the field left: the own player is shown again
                    Assert.That(own.Hidden, Is.False, entry);
                }
        }
        TestContext.WriteLine($"{variable}: {uses} VisibleMyPC uses in {files} files of {fields.Count} fields, {hides} hide, {shows} show; "
            + $"isVisible {string.Join(", ", shapes.Select(kv => $"'{kv.Key}' {kv.Value}"))}");
        Assert.That(uses, Is.GreaterThan(30));
        Assert.That(hides, Is.GreaterThan(0));
        Assert.That(shows, Is.GreaterThan(0));
    }

    [Test] public void EveryKmsUse() => EveryUse("MS2_DATA");
    [Test] public void EveryGms2Use() => EveryUse("MS2_GMS2_DATA");
}
