using System.Text;

namespace Ms2.Formats.Model;

/// <summary>
/// Gamebryo KFM 30.2.0.3b (binary): the nif, the root node, and the sequences with their ids. A sequence id (the KFM event code) is what the
/// server sends as an animation id; each sequence plays one kf file under a name (Idle_A, Walk_A, ...). The transition between
/// two sequences is read as NiKFMTool::ReadBinary does: a row of type DEFAULT_SYNC / DEFAULT_NONSYNC uses the file's default of
/// that kind, any other row carries its own duration, blend pairs and chain.
/// </summary>
public sealed class KfmFile
{
    public sealed record Sequence(int Id, string KfPath, string Name);

    /// <summary>NiKFMTool::TransitionType.</summary>
    public enum TransitionType { Blend = 0, Morph = 1, CrossFade = 2, Chain = 3, DefaultSync = 4, DefaultNonSync = 5 }

    /// <summary>A transition: its type and duration in seconds, and whether it has blend pairs or a chain.</summary>
    public sealed record Transition(TransitionType Type, float Duration, int BlendPairs = 0, int ChainLength = 0);

    public string Nif, Root;
    public List<Sequence> Sequences = new();
    public Transition DefaultSync, DefaultNonSync;

    // the rows that do not use a default (by source and destination id); every other row is DEFAULT_NONSYNC
    readonly Dictionary<(int, int), Transition> transitions = new();

    /// <summary>NiKFMTool::GetTransition(src, dest): the row's own transition, or the default it names.</summary>
    public Transition GetTransition(int src, int dest) => transitions.TryGetValue((src, dest), out var t) ? t : DefaultNonSync;

    public Sequence ById(int id) => Sequences.FirstOrDefault(s => s.Id == id);
    public Sequence ByName(string name) => Sequences.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    public static KfmFile Load(byte[] d)
    {
        const string magic = ";Gamebryo KFM File Version 30.2.0.3b\n";
        if (!Encoding.Latin1.GetString(d, 0, Math.Min(d.Length, magic.Length)).Equals(magic)) throw new InvalidDataException("not a 30.2.0.3b KFM");
        int o = magic.Length;
        uint U() { o += 4; return BitConverter.ToUInt32(d, o - 4); }
        float F() { o += 4; return BitConverter.ToSingle(d, o - 4); }
        string S() { int n = (int)U(); o += n; return Encoding.Latin1.GetString(d, o - n, n); }
        var k = new KfmFile();
        o++;                         // endian byte
        k.Nif = S(); k.Root = S();
        // the default sync and non-sync transitions: both types, then both durations
        var syncType = (TransitionType)U(); var nonSyncType = (TransitionType)U();
        float syncDur = F(), nonSyncDur = F();
        k.DefaultSync = new Transition(syncType, syncDur);
        k.DefaultNonSync = new Transition(nonSyncType, nonSyncDur);
        uint count = U();
        for (uint i = 0; i < count; i++)
        {
            int id = (int)U(); string kf = S(), name = S();
            uint nt = U();
            for (uint t = 0; t < nt; t++)
            {
                int dest = (int)U();
                var type = (TransitionType)U();
                if (type == TransitionType.DefaultNonSync) continue;
                if (type == TransitionType.DefaultSync) { k.transitions[(id, dest)] = k.DefaultSync; continue; }
                float dur = F();
                int pairs = (int)U();
                for (int c = 0; c < pairs; c++) { S(); S(); }      // start key, target key
                int chain = (int)U();
                o += 8 * chain;                                      // sequence id, duration
                k.transitions[(id, dest)] = new Transition(type, dur, pairs, chain);
            }
            k.Sequences.Add(new Sequence(id, kf, name));
        }
        return k;
    }
}
