namespace Ms2.Formats.Pack;

/// <summary>
/// GetXmlPath("Table", file) as the client opens it: the file system's read-only open tries the locale folder in front of
/// the file name first (CFileSystem::GetLocalePath, filesystem.cpp:928: dirname + g_strLocaleDir + filename), then the
/// plain path (ms2memfile.cpp:392). g_strLocaleDir is a build constant: "kr/" in the KMS exe
/// (meratmarketbasketinfo.cpp:211); the GMS2 data keeps its tables in table/na/. The exe of the GMS2 build is packed, so
/// the region folder is taken from the data: the first folder of <see cref="RegionFolders"/> that holds the file.
/// </summary>
public static class TablePath
{
    /// <summary>The region folders the data uses, in the order they are tried.</summary>
    public static readonly string[] RegionFolders = { "kr", "na" };

    /// <summary>Entry names to try for a table file, locale folder first, the plain path last (lower case, as the packs hold them).</summary>
    public static IEnumerable<string> Candidates(string file)
    {
        file = file.Replace('\\', '/').ToLowerInvariant();
        foreach (var r in RegionFolders) yield return "table/" + r + "/" + file;
        yield return "table/" + file;
    }

    /// <summary>The first existing entry of <see cref="Candidates"/>, null when none exists.</summary>
    public static string Resolve(Func<string, bool> exists, string file)
    {
        foreach (var c in Candidates(file)) if (exists(c)) return c;
        return null;
    }

    /// <summary>The table's entry in the Xml archive, null when the data has none.</summary>
    public static string Resolve(PackSet packs, string file) => Resolve(e => packs.Exists("Xml", e), file);
}
