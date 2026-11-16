using System.Text;

namespace Ms2.Formats.Model;

/// <summary>
/// Gamebryo KFM 30.2.0.3b (binary): the nif, the root node, and the sequences with their ids. A sequence id (the KFM event code) is what the
/// server sends as an animation id; each sequence plays one kf file under a name (Idle_A, Walk_A, ...). Transitions are skipped.
/// </summary>
public sealed class KfmFile
{
    public sealed record Sequence(int Id, string KfPath, string Name);

    public string Nif, Root;
    public List<Sequence> Sequences = new();

    public Sequence ById(int id) => Sequences.FirstOrDefault(s => s.Id == id);
    public Sequence ByName(string name) => Sequences.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    public static KfmFile Load(byte[] d)
    {
        const string magic = ";Gamebryo KFM File Version 30.2.0.3b\n";
        if (!Encoding.Latin1.GetString(d, 0, Math.Min(d.Length, magic.Length)).Equals(magic)) throw new InvalidDataException("not a 30.2.0.3b KFM");
        int o = magic.Length;
        uint U() { o += 4; return BitConverter.ToUInt32(d, o - 4); }
        string S() { int n = (int)U(); o += n; return Encoding.Latin1.GetString(d, o - n, n); }
        var k = new KfmFile();
        o++;                         // endian byte
        k.Nif = S(); k.Root = S();
        o += 16;                     // default sync / transition
        uint count = U();
        for (uint i = 0; i < count; i++)
        {
            int id = (int)U(); string kf = S(), name = S();
            uint nt = U();
            for (uint t = 0; t < nt; t++)
            {
                U();
                if (U() != 5)
                {
                    o += 4;
                    for (uint c = U(); c > 0; c--) { U(); S(); }
                    int nc = (int)U();     // (o += 8 * U() would evaluate o before U advances it)
                    o += 8 * nc;
                }
            }
            k.Sequences.Add(new Sequence(id, kf, name));
        }
        return k;
    }
}
