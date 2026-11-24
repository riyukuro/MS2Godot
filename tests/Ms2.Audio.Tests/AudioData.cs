using System.Collections.Concurrent;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Audio.Tests;

/// <summary>Shared PackSet cache and KR/GMS2 region check for Ms2.Audio.Tests (one PackSet per data folder).</summary>
static class AudioData
{
    static readonly ConcurrentDictionary<string, PackSet> packsByDir = new(StringComparer.Ordinal);

    public static PackSet Packs(string dir) => packsByDir.GetOrAdd(dir, d => new PackSet(d));

    public static PackSet DataPacks()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data) || !Directory.Exists(data)) Assert.Ignore("MS2_DATA is not set");
        return Packs(data);
    }

    public static bool IsKr(PackSet packs)
    {
        ArgumentNullException.ThrowIfNull(packs);
        return TablePath.Region(e => packs.Exists("Xml", e)) == "kr";
    }

    public static bool IsKr() => IsKr(DataPacks());
}
