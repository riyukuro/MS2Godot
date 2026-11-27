using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// Every set_gravity use in the trigger data. The server adds a field property of kind 1 with the script's value
/// (AddFieldProperty, 0xCA kind 1, f32), and the client writes it to the field's MS2PhysXScene Gravity z
/// (FieldPropertyGravityCli::vfunc_4, fieldpropertycli.cpp:140; WorldService::SetGravity, worldservice_1.cpp:5268-5289; nothing
/// without that entity). The jump then starts at sqrt(2 * 2h * -g) (GameObjectController::StartJump,
/// gameobjectcontrollerphysxhelpers.cpp:314-319), so each use's field must hold the scene entity and each value must give a
/// real jump. Values of zero or more give the client no jump speed; they are listed (none is in a main quest field).
/// </summary>
public class TriggerGravitySweepTests
{
    static (List<string> Bad, List<string> NonNegative, int Uses, int Fields) Sweep(string variable)
    {
        using var packs = new PackSet(RcGap.Data(variable));
        var bad = new List<string>();
        var nonNegative = new List<string>();
        var names = new SortedSet<string>(StringComparer.Ordinal);
        var byField = new Dictionary<string, List<(string File, string Value)>>();
        int uses = 0;
        foreach (var entry in packs.List("Xml").Where(e => e.StartsWith("trigger/", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e, StringComparer.Ordinal))
        {
            var parts = entry.Split('/');
            if (parts.Length != 3) continue;
            System.Xml.Linq.XDocument doc;
            try { doc = PackXml.Parse(packs.Read("Xml", entry)); } catch (System.Xml.XmlException) { continue; }
            foreach (var a in doc.Descendants("action"))
            {
                var name = (string)a.Attribute("name") ?? "";
                var value = (string)a.Attribute("gravity");
                if (value == null && !name.Equals("SetGravity", StringComparison.OrdinalIgnoreCase)) continue;
                names.Add(name);
                uses++;
                if (!byField.TryGetValue(parts[1], out var list)) byField[parts[1]] = list = new();
                list.Add((entry, value ?? ""));
            }
        }
        var flats = new FlatLibrary(packs);
        const float JumpHeight = 1.15f;   // any positive height: the speed is real exactly when g is negative
        foreach (var (field, list) in byField)
        {
            FieldMap map;
            try { map = FieldMap.Load(packs, flats, field); }
            catch (Exception ex) when (ex is FileNotFoundException or KeyNotFoundException or InvalidDataException)
            {
                bad.Add($"{field}: no field ({ex.GetType().Name})");
                continue;
            }
            if (!map.OfModel("MS2PhysXScene").Any()) bad.Add($"{field}: no MS2PhysXScene entity, SetGravity does nothing");
            foreach (var (file, value) in list)
            {
                if (!float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float g) || !float.IsFinite(g))
                { bad.Add($"{file}: gravity '{value}' is no number"); continue; }
                double speed = Math.Sqrt(2 * (2 * JumpHeight) * -g);
                if (!(speed > 0)) nonNegative.Add($"{file} {g}");
            }
        }
        TestContext.WriteLine($"{variable}: {uses} uses in {byField.Count} fields (action names: {string.Join(", ", names)}); bad: {(bad.Count == 0 ? "none" : string.Join("; ", bad))}; zero or more: {string.Join("; ", nonNegative)}");
        return (bad, nonNegative, uses, byField.Count);
    }

    /// <summary>The main quest's gravity fields (02000390, 52100013, 52000092, 52000091): each value must give a jump.</summary>
    static readonly string[] MainQuest = { "02000390_bf", "52100013_qd", "52000092_qd", "52000091_qd" };

    static void Check(string variable)
    {
        var (bad, nonNegative, uses, fields) = Sweep(variable);
        Assert.That(uses, Is.GreaterThan(10));
        Assert.That(fields, Is.GreaterThanOrEqualTo(MainQuest.Length));
        Assert.That(bad, Is.Empty);
        Assert.That(nonNegative.Where(n => MainQuest.Any(m => n.Contains("/" + m + "/"))), Is.Empty);
    }

    [Test]
    public void EveryKmsSetGravityFieldHasAPhysXSceneAndEveryMainQuestValueGivesAJump() => Check("MS2_DATA");

    [Test]
    public void EveryGms2SetGravityFieldHasAPhysXSceneAndEveryMainQuestValueGivesAJump() => Check("MS2_GMS2_DATA");
}
