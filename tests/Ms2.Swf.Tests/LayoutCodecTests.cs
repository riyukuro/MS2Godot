using System.Text.Json.Nodes;
using Ms2.Formats.Pack;
using Ms2.Swf;
using NUnit.Framework;

namespace Ms2.Swf.Tests;

/// <summary>
/// The stored layout (<see cref="LayoutCodec"/>) of every movie of the player's Gfx.m2d is the built one: read back it
/// equals <see cref="LayoutJson.Build"/> (every symbol decoded and compared, the class lists too), read without its
/// library it equals the build a movie used as a library gets, and writing the read layout gives the same bytes.
/// Skipped when MS2_DATA is not set.
/// </summary>
public class LayoutCodecTests
{
    [Test]
    public void EveryMovieRoundTrips()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        using var packs = new PackSet(d);
        var parsed = new Dictionary<string, SwfMovie>(StringComparer.OrdinalIgnoreCase);
        SwfMovie Movie(string k)
        {
            if (parsed.TryGetValue(k, out var m)) return m;
            var e = packs.FindByFileName("Resource/Gfx", k + ".gfx");
            return parsed[k] = e == null ? null : SwfMovie.Parse(packs.Read("Resource/Gfx", e));
        }
        var keys = packs.List("Resource/Gfx").Where(n => n.EndsWith(".gfx", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileNameWithoutExtension).ToList();
        Assert.That(keys, Is.Not.Empty);
        var bad = new List<string>();
        foreach (var k in keys)
        {
            var m = Movie(k);
            var built = LayoutJson.Build(m, k, Movie);
            var bin = LayoutCodec.Write(built);
            var back = LayoutCodec.Read(bin);
            if (LayoutCodec.Diff(built, back) is { } d1) bad.Add($"{k}: {d1}");
            if (LayoutCodec.Diff(LayoutJson.Build(m, k, null), LayoutCodec.Read(bin, false)) is { } d2) bad.Add($"{k} (as a library): {d2}");
            if (!LayoutCodec.Write(back).AsSpan().SequenceEqual(bin)) bad.Add($"{k}: written again, the bytes differ");
            // a clone is a layout of its own: decoding or changing it leaves the template as it was
            var c = (JsonObject)LayoutCodec.Clone(back);
            if (LayoutCodec.Diff(back, c) is { } d3) bad.Add($"{k} (clone): {d3}");
            if (LayoutSymbols.Of(c["symbols"]) is { Count: > 0 } cs && LayoutSymbols.Of(back["symbols"]) is { } bs)
            {
                var first = cs.First().Key;
                cs.Get(first)["probe"] = 1;
                if (bs.Get(first)["probe"] != null) bad.Add($"{k}: a clone's symbol change reached the template");
            }
        }
        Assert.That(bad, Is.Empty, string.Join("\n", bad.Take(20)));
    }

    [Test]
    public void DamagedBodyIsRejected()
    {
        var layout = new JsonObject
        {
            ["movie"] = "probe",
            ["symbols"] = new JsonObject { ["1"] = new JsonObject { ["kind"] = "sprite", ["class"] = "A" } },
        };
        var bin = LayoutCodec.Write(layout);
        Assert.That(LayoutCodec.Diff(layout, LayoutCodec.Read(bin)), Is.Null);
        Assert.Throws<InvalidDataException>(() => LayoutCodec.Read(bin[..^1]));
        var wrongMagic = (byte[])bin.Clone();
        wrongMagic[0] ^= 1;
        Assert.Throws<InvalidDataException>(() => LayoutCodec.Read(wrongMagic));
    }
}
