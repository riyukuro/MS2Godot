using Ms2.Formats.Nif;

namespace Ms2.Formats.Model;

/// <summary>
/// The answer of <see cref="ModelData.HasShaderTexture(NifFile)"/> per asset name. The file is parsed on the first ask for an asset
/// (the colouring dialog's list), scanned and dropped; the second ask is a cache hit. A file that fails to load answers false from then on.
/// </summary>
public sealed class ModelTextureCache
{
    readonly Dictionary<string, bool> answers = new();

    /// <summary>How many files were parsed (cache misses).</summary>
    public int Parses { get; private set; }

    public bool Has(string asset, Func<NifFile> load)
    {
        if (answers.TryGetValue(asset, out bool known)) return known;
        answers[asset] = false;
        Parses++;
        var nif = load();
        return answers[asset] = nif != null && ModelData.HasShaderTexture(nif);
    }
}
