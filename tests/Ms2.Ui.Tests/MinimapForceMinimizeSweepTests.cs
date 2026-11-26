using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The minimap's forced minimize over every field of the data of MS2_DATA (KMS2_DEV or KMS2) and MS2_GMS2_DATA (GMS2),
/// against the client. CFieldDataCli makes a CMinimap only for a mapxblock with a &lt;minimap&gt; block (LoadMinimap
/// @0x140a55df0, fielddatacli.cpp:311, called at :431) and takes the image name from its &lt;image name&gt; (:356);
/// m_nMinimapInvisible is &lt;property minimapInvisible&gt; (fielddata.cpp:682). UIMinimapDialog::BuildUIData
/// (uiminimapentity.cpp:3455-3459) forces the window minimized without a CMinimap, with an empty image name or with
/// minimapInvisible; BuildField (:2697-2708) asks for the field image whenever a CMinimap is there, so a named image the
/// Image pack lacks gives an empty map that is not forced.
/// </summary>
[NonParallelizable]
public class MinimapForceMinimizeSweepTests
{
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryFieldsMinimapIsForcedMinimizedAsInTheClient(string variable)
    {
        var d = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore($"{variable} is not set");
        using var packs = new PackSet(d);
        var rows = new SortedDictionary<int, XElement>();
        foreach (var fd in FieldData.Document(packs).Root!.Elements("fieldData"))
            if (FieldData.DefaultEnvironment(fd)?.Element("xblock")?.Attribute("name") != null && (int?)fd.Attribute("id") is int id)
                rows[id] = fd;
        Assert.That(rows, Is.Not.Empty);

        int noFile = 0, noBlock = 0, emptyName = 0, missingImage = 0, invisible = 0, forced = 0, shown = 0;
        var onlyInvisible = new List<int>();     // forced by minimapInvisible alone: the fields the image name did not force
        var problems = new List<string>();
        foreach (var (id, row) in rows)
        {
            var xblock = (string)FieldData.DefaultEnvironment(row).Element("xblock").Attribute("name");
            var prop = FieldData.DefaultEnvironment(row).Element("property") ?? row.Element("property");
            bool inv = int.TryParse(((string)prop?.Attribute("minimapInvisible"))?.Trim(), out var v) && v != 0;
            XDocument doc = packs.Exists(MapXBlock.Archive, MapXBlock.Entry(xblock)) ? MapXBlock.Load(packs, xblock) : null;
            var block = doc?.Root?.Element("minimap");
            string name = (string)block?.Element("image")?.Attribute("name") ?? "";
            bool want = block == null || inv || name.Length == 0;

            if (doc == null) noFile++;
            else if (block == null) noBlock++;
            else if (name.Length == 0) emptyName++;
            else if (!packs.Exists("Resource/Image", new MinimapInfo { Image = name }.ImageEntry)) missingImage++;
            if (inv) invisible++;
            if (inv && block != null && name.Length > 0) onlyInvisible.Add(id);
            if (want) forced++; else shown++;

            // HudScene.OpenMinimap's feed
            MinimapInfo info = null;
            try { info = MinimapInfo.Parse(MapXBlock.Load(packs, xblock)); } catch (Exception) { }
            var c = new MinimapController { ImageName = info?.ImageEntry, MinimapInvisible = FieldData.FindByXBlock(packs, xblock)?.MinimapInvisible ?? false };
            if (c.ForceMinimize != want)
                problems.Add($"field {id} ({xblock}): forced {c.ForceMinimize}, the client {want} (minimap block {block != null}, image \"{name}\", minimapInvisible {inv})");
        }
        TestContext.Progress.WriteLine($"{variable}: {rows.Count} fields, {forced} forced minimized, {shown} not; no mapxblock {noFile}, " +
            $"no minimap block {noBlock}, empty image name {emptyName}, named image not in the pack {missingImage}, minimapInvisible {invisible} " +
            $"({onlyInvisible.Count} with an image: {string.Join(" ", onlyInvisible)}); {problems.Count} differences");
        Assert.That(problems, Is.Empty, string.Join("\n", problems.Take(60)));
    }
}
