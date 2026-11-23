using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Formats.World;
using Ms2.Quest;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The revive dialog over every entry of the game files that steer it, in the data of MS2_DATA (KMS2_DEV or KMS2) and of
/// MS2_GMS2_DATA (GMS2):
/// - every field (table/fielddata.xml, or GMS2's map/&lt;id&gt;.xml): its death-state properties as the client reads them
///   (CFieldData ctor and load, fielddata.cpp:543-556, :665-699, :729; XmlGetAttr of an int is TiXmlElement::QueryIntAttribute,
///   tinyxmlhelper.cpp:151), whether the dialog opens (mypc.cpp:1347-1355), the free button's send or return box
///   (FUN_141abb600), the paid mode (FUN_141abcb90), the no-revive-here refusal (FUN_141abdab0) and the death text
///   (SetDeathMessage, FUN_141abae20);
/// - every dungeon room (table/dungeonroom.xml): its revival limits (dungeonroom.cpp:411, :412, :431), the first button's
///   label and leave (GetFirstButtonLabel, uirevivaldialog.cpp:775-797) and the paid mode under the limits (FUN_141abc7f0
///   with feature 0x5d, FUN_141abc930).
/// The expected values come from the client's rule written out here, not from the port's code; the dialog runs through
/// RevivalWiring, the wiring the game uses.
/// </summary>
[NonParallelizable]
public class RevivalDataSweepTests
{
    static PackSet Packs(string variable)
    {
        var d = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore($"{variable} is not set");
        return new PackSet(d);
    }

    /// <summary>A field's death-state properties as the client loads them.</summary>
    sealed record ClientField(int ReturnId, int DeathPenalty, int AutoType, int AutoTime, int DoNotRevivalHere, int InfinityMerat, int MapType);

    /// <summary>The CFieldData ctor defaults, then XmlGetAttr (QueryIntAttribute: a value that does not start with a number keeps
    /// the default) of the selected environment's &lt;property&gt;, then revivalreturnid 0 becomes the field's own id (:729).</summary>
    static ClientField ClientRead(int id, XElement prop)
    {
        int ret = 0, penalty = 1, autoType = 0, autoTime = 0, noHere = 0, infinity = 1, mapType = 0;
        if (prop != null)
        {
            QuestTable.QueryInt(prop, "revivalreturnid", ref ret);
            QuestTable.QueryInt(prop, "deathPenalty", ref penalty);
            QuestTable.QueryInt(prop, "autoRevivalType", ref autoType);
            QuestTable.QueryInt(prop, "autoRevivalTime", ref autoTime);
            QuestTable.QueryInt(prop, "doNotRevivalHere", ref noHere);
            QuestTable.QueryInt(prop, "mapType", ref mapType);
            QuestTable.QueryInt(prop, "infinityMeratRevival", ref infinity);
        }
        if (ret == 0) ret = id;
        return new ClientField(ret, penalty, autoType, autoTime, noHere, infinity, mapType);
    }

    static MyCharacterRig Dead() => new();

    sealed class MyCharacterRig
    {
        public readonly Ms2.Net.Packets.MyCharacter Me = RevivalDialogTests.DeadPlayer();
        public readonly List<string> Sent = new(), Notices = new();
        public readonly List<MessageBoxData> Boxes = new();
        public readonly RevivalDialogController C = new() { CommonById = id => $"#{id:x}", CommonByKey = k => k + "|{0}" };

        public void Connect(int fieldId, Func<int, FieldData> fieldOf, RevivalRoomInputs room = null)
        {
            var k = new ClientConstants(Array.Empty<KeyValuePair<string, string>>());
            var buffs = new Ms2.Ui.Skills.AdditionalBuffList((_, _) => new Ms2.Ui.Skills.AdditionalLevelData { Level = 1 });
            RevivalWiring.Connect(C.Env, Me, () => fieldId, fieldOf, p => Sent.Add($"{p.Op}:{Convert.ToHexString(RevivalDialogTests.Bytes(p))}"),
                Notices.Add, id => $"notice {id:x}", id => $"map {id}", new RevivalPaidInputs(() => 1_000_000, buffs, k), room);
            C.Env.ShowBox = Boxes.Add;
        }

        public void Clear() { Sent.Clear(); Notices.Clear(); Boxes.Clear(); }
    }

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryFieldGivesTheClientsRevivalDialog(string variable)
    {
        using var packs = Packs(variable);
        var doc = FieldData.Document(packs);
        var rows = new Dictionary<int, XElement>();
        foreach (var fd in doc.Root!.Elements("fieldData"))
        {
            // CFieldData::LoadFieldData (fielddata.cpp:583, :591): an entry without a selected environment is skipped, a later id replaces
            var env = FieldData.DefaultEnvironment(fd);
            if (env?.Element("xblock")?.Attribute("name") == null) continue;
            if ((int?)fd.Attribute("id") is int id) rows[id] = fd;
        }
        Assert.That(rows, Is.Not.Empty, "the data has fields");

        var problems = new List<string>();
        int elsewhere = 0, noPenalty = 0, noHere = 0, meratLimited = 0, home = 0, auto = 0;
        foreach (var (id, fd) in rows.OrderBy(r => r.Key))
        {
            var env = FieldData.DefaultEnvironment(fd);
            var prop = env.Element("property") ?? fd.Element("property");
            var want = ClientRead(id, prop);
            var ours = FieldData.FindById(packs, id);
            string Raw(string name) => prop?.Attribute(name) is { } a ? $"{name}=\"{a.Value}\"" : $"{name} (none)";
            void Check<T>(string what, T got, T expected)
            {
                if (!EqualityComparer<T>.Default.Equals(got, expected)) problems.Add($"field {id}: {what} is {got}, the client reads {expected}");
            }
            Check(Raw("revivalreturnid"), ours.RevivalReturnId, want.ReturnId);
            Check(Raw("deathPenalty"), ours.DeathPenalty, want.DeathPenalty);
            Check(Raw("autoRevivalType"), ours.AutoRevivalType, want.AutoType);
            Check(Raw("autoRevivalTime"), ours.AutoRevivalTime, want.AutoTime);
            Check(Raw("doNotRevivalHere"), ours.DoNotRevivalHere, want.DoNotRevivalHere != 0);
            Check(Raw("infinityMeratRevival"), ours.InfiniteMeratRevival, want.InfinityMerat != 0);
            Check(Raw("mapType"), ours.MapType, want.MapType);

            bool autoRevives = (want.AutoType == 1 || want.AutoType == 2) && want.AutoTime > 0;
            Check("the dialog opens", RevivalWiring.OpensDialog(ours), !autoRevives);
            if (want.ReturnId != id) elsewhere++;
            if (want.DeathPenalty == 0) noPenalty++;
            if (want.DoNotRevivalHere != 0) noHere++;
            if (want.InfinityMerat == 0) meratLimited++;
            if (want.MapType == 3) home++;
            if (autoRevives) auto++;

            // the meso revive with a daily count left, then with none (the merat revive, or meso again on a home)
            foreach (int used in new[] { 0, RevivalWiring.MesoRevivalDailyMax })
            {
                var r = Dead();
                r.Me.Meso = 1_000_000;
                r.Me.MesoRevivalsUsed = used;
                r.Connect(id, _ => ours);
                r.C.Refresh();
                int mode = want.MapType == 3 || used < RevivalWiring.MesoRevivalDailyMax ? 2 : 1;
                Check($"the paid mode ({used} meso revives used)", r.C.PaidMode, mode);
                if (used == 0)
                {
                    string text = want.DeathPenalty == 0 ? "#27c" : "s_msg_die_meso|{0}";
                    if (want.ReturnId != id) text += "\n#27d";
                    Check("the death text", r.C.DeathMessage, text);
                    Check("the meso cost", r.C.MesoCost, want.MapType == 3 ? 0 : RevivalDialogController.CalcMeso(r.C.Env.Level(), 3, 0));   // min(cap 0.5, ability rate 0): no reduction
                }

                r.C.OnRevival(1);
                if (want.ReturnId == id) Check("the free button", string.Join(" ", r.Sent) + $" boxes {r.Boxes.Count}", "Revival:00 boxes 0");
                else Check("the free button", $"{r.Sent.Count} {r.Boxes.FirstOrDefault()?.Msg}", "0 #287");
                r.Clear();

                r.C.OnRevival(2);
                string paid = want.DoNotRevivalHere != 0 ? "notice 282"
                    : mode == 2 ? "Revival:0200"
                    : "box #283";
                string got = r.Notices.LastOrDefault() ?? (r.Sent.Count > 0 ? r.Sent[0] : r.Boxes.Count > 0 ? "box " + r.Boxes[0].Msg : "nothing");
                Check($"the paid button ({used} meso revives used)", got, paid);
            }
        }
        TestContext.Progress.WriteLine($"{variable}: {rows.Count} fields, {problems.Count} differences; return elsewhere {elsewhere}, " +
            $"deathPenalty 0 {noPenalty}, doNotRevivalHere {noHere}, infinityMeratRevival 0 {meratLimited}, home (mapType 3) {home}, auto revival {auto}");
        Assert.That(problems, Is.Empty, string.Join("\n", problems.Take(40)));
    }

    /// <summary>A room's revival limits as the client loads them: XmlGetAttr of a bool (QueryBoolAttribute) and of an unsigned.</summary>
    sealed record ClientRoom(bool LimitMeso, bool LimitMerat, uint Revivals);

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryDungeonRoomGivesTheClientsRevivalLimits(string variable)
    {
        using var packs = Packs(variable);
        var entry = DungeonRooms.EntryOf(e => packs.Exists(DungeonRooms.Archive, e));
        if (entry == null) Assert.Ignore($"{variable}: no dungeonroom.xml");
        bool Available(XElement e) => FeatureElements.IsAvailable(packs, e);
        var rooms = DungeonRooms.Load(packs, Available);
        var want = new Dictionary<int, ClientRoom>();
        foreach (var e in PackXml.Parse(packs.Read(DungeonRooms.Archive, entry)).Root!.Elements("dungeonRoom"))
        {
            if (!Available(e)) continue;
            int id = 0; bool meso = false, merat = false; uint n = 0;
            QuestTable.QueryUInt(e, "dungeonRoomID", ref id);
            QuestTable.QueryBool(e, "limitMesoRevival", ref meso);
            QuestTable.QueryBool(e, "limitMeratRevival", ref merat);
            QuestTable.QueryUInt(e, "defaultRevivalLimitCount", ref n);
            want[id] = new ClientRoom(meso, merat, n);       // DungeonRoomTable::LoadRooms: a later row replaces (dungeonroom.cpp:1068)
        }
        Assert.That(want, Is.Not.Empty);

        var field = new FieldData { Id = 100, RevivalReturnId = 100 };
        var problems = new List<string>();
        int limited = 0, noMeso = 0, noMerat = 0;
        foreach (var (id, w) in want.OrderBy(p => p.Key))
        {
            var row = rooms.GetRoom(id);
            if (row == null) { problems.Add($"room {id}: not loaded"); continue; }
            if ((row.LimitMesoRevival, row.LimitMeratRevival, (uint)row.DefaultRevivalLimitCount) != (w.LimitMeso, w.LimitMerat, w.Revivals))
                problems.Add($"room {id}: limits {(row.LimitMesoRevival, row.LimitMeratRevival, row.DefaultRevivalLimitCount)}, the client reads {w}");
            if (w.Revivals > 0) limited++;
            if (w.LimitMeso) noMeso++;
            if (w.LimitMerat) noMerat++;

            var counts = w.Revivals == 0 ? new uint[] { 0, 1 } : new uint[] { 0, w.Revivals - 1, w.Revivals, w.Revivals + 1 };
            foreach (bool feature in new[] { true, false })
            foreach (uint used in counts)
            {
                var r = Dead();
                r.Me.Meso = 1_000_000;
                r.Me.DungeonRevivalsUsed = (int)used;
                r.Connect(100, _ => field, new RevivalRoomInputs(() => id, rooms.GetRoom, () => feature));
                r.C.Refresh();
                // GetFirstButtonLabel: nLeft = limit - used, unsigned (wraps above the limit), 0 leaves
                uint left = w.Revivals - used;
                string label = w.Revivals == 0 ? "#27e" : left == 0 ? "s_room_exit_dungeon|{0}" : $"s_msg_revival_count_btn|{left}";
                if (r.C.Label1 != label) problems.Add($"room {id} used {used}: the first label is {r.C.Label1}, the client's {label}");
                if (r.C.FirstButtonLeaves != (w.Revivals > 0 && left == 0)) problems.Add($"room {id} used {used}: leaves {r.C.FirstButtonLeaves}");
                // FUN_141abcb90: meso (a daily count is left) unless feature 0x5d and limitMesoRevival; then merat unless limitMeratRevival
                int mode = !(feature && w.LimitMeso) ? 2 : !w.LimitMerat ? 1 : 0;
                if (r.C.PaidMode != mode) problems.Add($"room {id} feature {feature}: the paid mode is {r.C.PaidMode}, the client's {mode}");
                r.C.OnRevival(1);
                string first = w.Revivals > 0 && left == 0 ? $"box {r.Boxes.FirstOrDefault()?.Extra}" : string.Join(" ", r.Sent);
                string firstWant = w.Revivals > 0 && left == 0 ? "box leaveInField" : "Revival:00";
                if (first != firstWant) problems.Add($"room {id} used {used}: the first button gives {first}, the client's {firstWant}");
            }
        }
        TestContext.Progress.WriteLine($"{variable}: {want.Count} rooms, {problems.Count} differences; revival limit {limited}, " +
            $"limitMesoRevival {noMeso}, limitMeratRevival {noMerat}");
        Assert.That(problems, Is.Empty, string.Join("\n", problems.Take(40)));
    }
}
