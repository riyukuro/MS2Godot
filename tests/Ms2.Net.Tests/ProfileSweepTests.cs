using System.Reflection;
using System.Text;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Decodes every recording of a folder (MS2_SNIFFS) with every reader of Ms2.Net.Packets whose type is named after the opcode (the type
/// <c>Name</c>, <c>NameMessage</c> or <c>NamePacket</c> with a static <c>Read(ByteReader[, ProtocolProfile])</c>) and tallies per opcode:
/// decoded (all bytes consumed), under-read (bytes left over) and over-read (an exception). The table goes to MS2_SWEEP_OUT when set.
/// Skipped without MS2_SNIFFS. Recordings are local data and never part of the repo. MS2_SWEEP_SET picks the sniff set (gms2 or kms2).
/// </summary>
public class ProfileSweepTests {
    sealed class Tally { public int Ok, Under, Over, Total; public Dictionary<string, int> Errors = new(); public string Sample; }

    static readonly Dictionary<string, MethodBase> Readers = BuildReaders();

    static Dictionary<string, MethodBase> BuildReaders() {
        var map = new Dictionary<string, MethodBase>();
        foreach (var t in typeof(ReaderExt).Assembly.GetTypes().Where(t => t.IsPublic && t.Namespace == "Ms2.Net.Packets")) {
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name == "Read")) {
                var ps = m.GetParameters();
                if (ps.Length == 0 || ps[0].ParameterType != typeof(ByteReader)) continue;
                if (!ps.Skip(1).All(p => p.ParameterType == typeof(ProtocolProfile) || p.ParameterType == typeof(string) || p.ParameterType == typeof(Func<long, EquipItem>))) continue;
                string n = t.Name;
                foreach (var suffix in new[] { "Message", "Packet" }) if (n.EndsWith(suffix)) n = n[..^suffix.Length];
                if (t.Name == "ItemMessage") n = "ItemInventory";   // the reader is named after its messages, the opcode after the inventory
                string key = n + "|" + (ps.Any(p => p.ParameterType == typeof(ProtocolProfile)) ? "p" : "plain");
                map[key] = m;
            }
        }
        return map;
    }

    static MethodBase Find(string name, bool profile) =>
        profile && Readers.TryGetValue(name + "|p", out var a) ? a : Readers.TryGetValue(name + "|plain", out var b) ? b : null;

    static object[] Args(MethodBase m, ByteReader r, ProtocolProfile profile, Dictionary<long, EquipItem> items) =>
        m.GetParameters().Select((p, i) => i == 0 ? (object) r : p.ParameterType == typeof(ProtocolProfile) ? profile
            : p.ParameterType == typeof(Func<long, EquipItem>) ? (Func<long, EquipItem>) (uid => items.GetValueOrDefault(uid))
            : (object) (profile.IsGms2 ? "NA" : "KR")).ToArray();

    /// <summary>The items an inventory message introduced, so a later UpdateItem of the same file finds its item (the client's own item table).</summary>
    static void Remember(object msg, Dictionary<long, EquipItem> items) {
        switch (msg) {
            case ItemMessage.Add a when a.Item != null: items[a.Item.Uid] = a.Item; break;
            case ItemMessage.Load l: foreach (var i in l.Items) items[i.Uid] = i; break;
            case ItemMessage.LoadTab t: foreach (var i in t.Items) items[i.Uid] = i; break;
            case ItemMessage.Remove rm: items.Remove(rm.Uid); break;
        }
    }

    static IEnumerable<string> Recordings(string set) {
        string dir = Environment.GetEnvironmentVariable("MS2_SNIFFS");
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore("MS2_SNIFFS not set");
        string sub = Path.Combine(dir, set + "-sniffs");
        return Directory.GetFiles(Directory.Exists(sub) ? sub : dir, "*.msb", SearchOption.AllDirectories).OrderBy(p => p);
    }

    static string Sweep(string set, ProtocolProfile profile, uint minBuild) {
        var inboundNames = new Dictionary<ushort, string>();
        foreach (var o in Enum.GetValues<Inbound>()) { try { inboundNames[profile.Number(o)] = o.ToString(); } catch (KeyNotFoundException) { } }
        var outboundNames = new Dictionary<ushort, string>();
        foreach (var o in Enum.GetValues<Outbound>()) { try { outboundNames[profile.Number(o)] = o.ToString(); } catch (KeyNotFoundException) { } }
        var inT = new SortedDictionary<ushort, Tally>(); var outT = new SortedDictionary<ushort, int>(); int files = 0;
        uint.TryParse(Environment.GetEnvironmentVariable("MS2_SWEEP_MAX_BUILD"), out uint maxBuild); if (maxBuild == 0) maxBuild = uint.MaxValue;
        var builds = new SortedDictionary<uint, int>();
        foreach (var path in Recordings(set)) {
            var f = MsbFile.Read(path);
            if (f.Build < minBuild || f.Build > maxBuild) continue;
            var items = new Dictionary<long, EquipItem>();
            files++; builds[f.Build] = builds.GetValueOrDefault(f.Build) + 1;
            foreach (var p in f.Packets) {
                if (!p.ServerToClient) { outT[p.Opcode] = outT.GetValueOrDefault(p.Opcode) + 1; continue; }
                if (!inT.TryGetValue(p.Opcode, out var t)) inT[p.Opcode] = t = new Tally();
                t.Total++;
                if (!inboundNames.TryGetValue(p.Opcode, out var name) || Find(name, profile.IsGms2) is not { } m) continue;
                var r = new ByteReader(p.Data);
                try {
                    Remember(m.Invoke(null, Args(m, r, profile, items)), items);
                    if (r.Available == 0) t.Ok++; else { t.Under++; { string uk = $"under, first byte {(p.Data.Length > 0 ? p.Data[0] : -1)}"; t.Errors[uk] = t.Errors.GetValueOrDefault(uk) + 1; } t.Sample ??= $"{Path.GetFileName(path)}: {r.Available} left"; }
                } catch (TargetInvocationException e) {
                    var ie = e.InnerException;
                    if (ie is PacketLayoutException && ie.Message.Contains("bytes left over")) { t.Under++; t.Sample ??= $"{Path.GetFileName(path)}: {ie.Message}"; continue; }
                    t.Over++;
                    string key = $"first byte {(p.Data.Length > 0 ? p.Data[0] : -1)}: " + ie.GetType().Name + ": " + ie.Message[..Math.Min(32, ie.Message.Length)] + " at " + string.Join(" < ", (ie.StackTrace ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Contains("Ms2.Net")).Take(3).Select(l => l.Split('(')[0].Replace("at ", "")));
                    t.Errors[key] = t.Errors.GetValueOrDefault(key) + 1; t.Sample ??= $"{Path.GetFileName(path)}: {key}";
                }
            }
        }
        var sb = new StringBuilder();
        sb.AppendLine($"# {set} recordings ({files} files, build >= {minBuild}) with the {profile.Name} profile");
        sb.AppendLine("# files per build: " + string.Join(", ", builds.Select(kv => $"{kv.Key}x{kv.Value}")));
        sb.AppendLine("dir | op | name | packets | ok | under | over | reader | sample");
        foreach (var (op, t) in inT) {
            string name = inboundNames.GetValueOrDefault(op, "?");
            bool has = inboundNames.ContainsKey(op) && Find(name, profile.IsGms2) != null;
            sb.AppendLine($"in | 0x{op:X2} | {name} | {t.Total} | {t.Ok} | {t.Under} | {t.Over} | {(has ? "yes" : "none")} | {t.Sample}");
        }
        foreach (var (op, t) in inT) foreach (var (k, n) in t.Errors.OrderByDescending(e => e.Value).Take(8)) sb.AppendLine($"# error 0x{op:X2} x{n}: {k}");
        foreach (var (op, n) in outT) sb.AppendLine($"out | 0x{op:X2} | {outboundNames.GetValueOrDefault(op, "?")} | {n} | | | | writer |");
        return sb.ToString();
    }

    [Test, Explicit("a corpus sweep, run on purpose")]
    public void SweepGms2RecordingsWithTheGms2Profile() => Emit("gms2", ProtocolProfile.Gms2, 0);

    [Test, Explicit("a corpus sweep, run on purpose")]
    public void SweepKms2RecordingsWithTheKms2Profile() => Emit("kms2", ProtocolProfile.Kms2525, 2520);

    static void Emit(string set, ProtocolProfile profile, uint minBuild) {
        // the item body layouts follow the item data (the game sets these hooks from the player's Data folder); without MS2_DATA the table is read with none
        string data = Environment.GetEnvironmentVariable("MS2_DATA");
        var (slotOf, extraOf, known) = (ItemReader.SlotOf, ItemReader.ExtraOf, ItemReader.Known);
        Ms2.Formats.Pack.PackSet packs = null;
        string table;
        try {
            if (!string.IsNullOrEmpty(data)) {
                packs = new Ms2.Formats.Pack.PackSet(data);
                var kinds = new Ms2Godot.Net.ItemKinds(packs); var slots = new Ms2Godot.Net.ItemModelSlots(packs);
                ItemReader.SlotOf = slots.SlotOf; ItemReader.ExtraOf = kinds.ExtraOf; ItemReader.Known = null;   // the recordings come from other builds: an id the data lacks was sent with its body, so every id is read
            }
            table = Sweep(set, profile, minBuild);
        } finally { (ItemReader.SlotOf, ItemReader.ExtraOf, ItemReader.Known) = (slotOf, extraOf, known); packs?.Dispose(); }
        string outFile = Environment.GetEnvironmentVariable("MS2_SWEEP_OUT");
        if (!string.IsNullOrEmpty(outFile)) File.WriteAllText(outFile.Replace("{set}", set), table);
        TestContext.Out.WriteLine(table);
    }
}
