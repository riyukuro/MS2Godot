using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>The game data and nif.xml the NIF-reading tests open: MS2_DATA, and MS2_NIFXML or else external/nifxml/nif.xml (the git submodule).</summary>
static class NifEnv
{
    public static (PackSet, NifSpec) Open()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA is not set");
        return (new PackSet(data), new NifSpec(NifXml()));
    }

    /// <summary>The path of nif.xml: MS2_NIFXML, or else external/nifxml/nif.xml.</summary>
    public static string NifXml()
    {
        var nx = Environment.GetEnvironmentVariable("MS2_NIFXML");
        if (string.IsNullOrEmpty(nx)) nx = SubmoduleNifXml();
        if (string.IsNullOrEmpty(nx) || !File.Exists(nx)) Assert.Ignore("MS2_NIFXML is not set and external/nifxml is not checked out");
        return nx;
    }

    /// <summary>external/nifxml/nif.xml of the repo holding the test assembly, or null.</summary>
    static string SubmoduleNifXml()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "MS2Godot.sln")))
            {
                var f = Path.Combine(d.FullName, "external", "nifxml", "nif.xml");
                return File.Exists(f) ? f : null;
            }
        return null;
    }
}
