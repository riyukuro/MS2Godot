using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Ui.Guide;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>StateTriggerEmotion (stateemotionidle.cpp:384-460): the list mode plays each name once, the timed mode replays
/// the first until the duration has passed, and the flag lasts until the state is left.</summary>
public class TriggerEmotionTests
{
    [Test]
    public void ListModePlaysEachNameOnceThenIdle()
    {
        var s = new TriggerEmotion();
        Assert.That(s.Enter(new TriggerEmotionData(new[] { "A", "B", "C" }, 0, 0, 0, false), 1000), Is.EqualTo("A"));
        Assert.That(s.EndOfSequence(1500), Is.EqualTo("B"));
        Assert.That(s.EndOfSequence(99999), Is.EqualTo("C"));
        Assert.That(s.EndOfSequence(99999), Is.Null);
    }

    [Test]
    public void TimedModeReplaysTheFirstUntilTheDuration()
    {
        var s = new TriggerEmotion();
        Assert.That(s.Enter(new TriggerEmotionData(new[] { "A" }, 1, 1, 3000, true), 1000), Is.EqualTo("A"));
        Assert.That(s.Flag, Is.EqualTo(1));
        Assert.That(s.EndOfSequence(2000), Is.EqualTo("A"));
        Assert.That(s.EndOfSequence(3999), Is.EqualTo("A"));
        Assert.That(s.EndOfSequence(4000), Is.Null);
        s.Leave();
        Assert.That(s.Flag, Is.EqualTo(0));
    }

    [Test]
    public void EnteringAgainStartsTheNewListFromItsFirst()
    {
        var s = new TriggerEmotion();
        s.Enter(new TriggerEmotionData(new[] { "A", "B" }, 0, 0, 0, false), 0);
        s.EndOfSequence(10);
        Assert.That(s.Enter(new TriggerEmotionData(new[] { "C", "D" }, 0, 0, 0, false), 20), Is.EqualTo("C"));
        Assert.That(s.EndOfSequence(30), Is.EqualTo("D"));
        Assert.That(s.Enter(new TriggerEmotionData(System.Array.Empty<string>(), 0, 0, 0, false), 40), Is.Null);
    }
    /// <summary>The sequence names the trigger xmls (Xml.m2d trigger/) give the own pc's emotion state: arg1 of
    /// SetPcEmotionSequence (a list, the Ui trigger's sub 7) and SetPcEmotionLoop (sub 8), the action name in any case;
    /// each with the first file that names it.</summary>
    static SortedDictionary<string, string> PcEmotionNames(PackSet packs)
    {
        var names = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in packs.List("Xml").Where(e => e.StartsWith("trigger/", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e, StringComparer.Ordinal))
        {
            var bytes = packs.Read("Xml", entry);
            if (bytes.AsSpan().IndexOf("motion"u8) < 0) continue;
            System.Xml.Linq.XDocument doc;
            try { doc = PackXml.Parse(bytes); } catch (System.Xml.XmlException) { continue; }
            foreach (var a in doc.Descendants("action"))
            {
                var action = (string)a.Attribute("name") ?? "";
                if (!action.Equals("SetPcEmotionSequence", StringComparison.OrdinalIgnoreCase)
                    && !action.Equals("SetPcEmotionLoop", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var n in ((string)a.Attribute("arg1") ?? "").Split(','))
                    if (n.Trim().Length != 0) names.TryAdd(n.Trim(), entry);
            }
        }
        return names;
    }

    /// <summary>The names neither body has, as "body/name (file)", and the number of names.</summary>
    static (List<string> Missing, int Names) Unresolved(string variable)
    {
        using var packs = new PackSet(RcGap.Data(variable));
        var names = PcEmotionNames(packs);
        var missing = new List<string>();
        foreach (var body in new[] { "male", "female" })
            foreach (var (name, file) in names)
                if (!BodySequences.Exists(packs, body, name)) missing.Add($"{body}/{name} ({file})");
        TestContext.WriteLine($"{variable}: {names.Count} names; not on a body: {(missing.Count == 0 ? "none" : string.Join(", ", missing))}");
        return (missing, names.Count);
    }

    /// <summary>Two names the trigger data gives that no body has (the bodies have emotion_hello_a and
    /// emotion_halloween_surprise_a): the client finds no sequence for them either.</summary>
    static readonly string[] DataLacks =
    {
        "male/Emotion_Surprise_A (trigger/52000133_qd/main.xml)", "male/Hello_A (trigger/52000132_qd/main.xml)",
        "female/Emotion_Surprise_A (trigger/52000133_qd/main.xml)", "female/Hello_A (trigger/52000132_qd/main.xml)",
    };

    [Test]
    public void EveryTriggerPcEmotionResolvesOnTheBodiesButTheTwoTheDataLacks()
    {
        var (missing, names) = Unresolved("MS2_DATA");
        Assert.That(names, Is.GreaterThan(50));
        Assert.That(missing, Is.EquivalentTo(DataLacks), "a name not listed here is one the port fails to find");
    }

    [Test]
    public void EveryGms2TriggerPcEmotionResolvesOnTheBodiesButTheTwoTheDataLacks()
    {
        var (missing, names) = Unresolved("MS2_GMS2_DATA");
        Assert.That(names, Is.GreaterThan(50));
        Assert.That(missing, Is.EquivalentTo(DataLacks), "a name not listed here is one the port fails to find");
    }
}
