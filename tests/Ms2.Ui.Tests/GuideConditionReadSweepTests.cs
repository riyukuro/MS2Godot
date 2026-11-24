using System.Numerics;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Ui.Guide;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// Every guide condition entry of the game files (table/guide*.xml, as GuideTable reads them) on KMS2_DEV, KMS2 and GMS2:
/// the world reads its Check makes (CGuideCondition*::vfunc_3, GuideConditions.cs, each class cites its client function)
/// against what the live session's guide world gives (LiveGuideWorld, game/net/LiveSession.Guide.cs). A read the live world
/// does not override answers with GuideWorld's stub (false, 0 or null), so every entry that makes it is decided by the
/// stub, not by the game. Such a read must be listed in <see cref="StillOpen"/> with its cause; a read listed there that
/// the live world now gives fails the test, so the list only shrinks.
/// Data sets: MS2_DATA (KMS2 or the restored set), MS2_GMS2_DATA and MS2_KMS2DEV_DATA; an unset one is skipped.
/// </summary>
public class GuideConditionReadSweepTests
{
    /// <summary>Reads whose GuideWorld body is the real value, not a stub: the tick count, the server clock
    /// (overridden too), the text field focus (InputBlock) and the application's focus.</summary>
    static readonly HashSet<string> BaseGives = new() { "Now", "ServerTime", "TextFocused", "AppActive" };

    /// <summary>The reads the live world does not give yet, each with what it needs.</summary>
    static readonly Dictionary<string, string> StillOpen = new()
    {
        ["CanObserveMode"] = "the party dialog's IsObserveModeAvailable: the observe mode is not ported",
        // the flag's getter is Unk_GcNoPoint in the client
        ["ChatEmoticonFlag"] = "the chat emoticon dialog's (0x4f80) flag: what the client reads is not recovered",
        ["ConstructionCell"] = "PC::GetConstructionGuide is not ported (CubeCraft.ConfirmPlacement holds it as none too), so the craft's "
            + "cell is never used; null is the client's answer without a guide",
        ["CubePaletteSelection"] = "GetUGCCubePalette (CubeHolderManager::GetSelectedItemId): the UGC cube palette is not ported",
        ["EarliestSaleEnd"] = "the merat market's sale list is not ported",
        ["GuildPvpSeason"] = "the guild PvP season test is not ported",      // FUN_1404c6160
        ["InstrumentHasScore"] = "the instrument dialog (0x4eec) is not ported",
        ["InstrumentTab"] = "the instrument dialog (0x4eec) is not ported",
        ["MentoringPageShown"] = "the mentoring dialog (0x4f4b) is not ported",
        ["PetSummoned"] = "the own summoned pet's uid is not kept",          // PCBase m_nUidB78, set by FUN_1416769a0
        ["SubSceneId"] = "GFxSceneService's sub scene id is not modelled; the HUD's own scene gates read the same stub",
    };

    sealed class RecordingWorld : GuideWorld
    {
        public readonly HashSet<string> Reads = new();
        void R(string name) => Reads.Add(name);
        public override uint Now { get { R("Now"); return 0u; } }
        public override long ServerTime { get { R("ServerTime"); return 0L; } }
        public override int SceneId { get { R("SceneId"); return 0; } }
        public override int SubSceneId { get { R("SubSceneId"); return 0; } }
        public override bool TextFocused { get { R("TextFocused"); return false; } }
        public override bool IsKeyDown(int key) { R("IsKeyDown"); return false; }
        public override bool IsActionKeyDown(int action) { R("IsActionKeyDown"); return false; }
        public override int FindKeyByActionAndPriority(int action, int priority) { R("FindKeyByActionAndPriority"); return 0; }
        public override bool GetPairKey(int key, out int pair) { R("GetPairKey"); pair = 0; return false; }
        public override int GamepadState { get { R("GamepadState"); return 0; } }
        public override int GuideMode { get { R("GuideMode"); return 0; } }
        public override bool Migrating { get { R("Migrating"); return false; } }
        public override bool CutScenePlaying { get { R("CutScenePlaying"); return false; } }
        public override IGuideEventPane Pane { get { R("Pane"); return default; } }
        public override bool AppActive { get { R("AppActive"); return false; } }
        public override GuideDialog FindDialog(int id) { R("FindDialog"); return default; }
        public override GuideDialog FindDialog(string name) { R("FindDialog"); return default; }
        public override int? AchieveTab { get { R("AchieveTab"); return 1; } }
        public override int? InventoryTab { get { R("InventoryTab"); return 1; } }
        public override bool? CanObserveMode { get { R("CanObserveMode"); return true; } }
        public override bool? ChatEmoticonFlag { get { R("ChatEmoticonFlag"); return true; } }
        public override int? MasteryMainTab { get { R("MasteryMainTab"); return 1; } }
        public override int? MasterySubTab { get { R("MasterySubTab"); return 1; } }
        public override bool MentoringPageShown(string page) { R("MentoringPageShown"); return false; }
        public override bool? MenuExpanded(string name) { R("MenuExpanded"); return true; }
        public override bool? InstrumentHasScore { get { R("InstrumentHasScore"); return true; } }
        public override int? InstrumentTab { get { R("InstrumentTab"); return 1; } }
        public override bool? EnchantHasItem { get { R("EnchantHasItem"); return true; } }
        public override bool UgcManagementShown { get { R("UgcManagementShown"); return false; } }
        public override int? CubePaletteSelection { get { R("CubePaletteSelection"); return 1; } }
        public override bool HasPlayer { get { R("HasPlayer"); return true; } }
        public override Vector3 PlayerPosition { get { R("PlayerPosition"); return default; } }
        public override Vector3? ConstructionCell { get { R("ConstructionCell"); return System.Numerics.Vector3.Zero; } }
        public override bool InBattle { get { R("InBattle"); return false; } }
        public override bool CubeMode { get { R("CubeMode"); return false; } }
        public override bool Constructable { get { R("Constructable"); return false; } }
        public override bool Dead { get { R("Dead"); return false; } }
        public override int FindQuickSlotBySkill(int skillId) { R("FindQuickSlotBySkill"); return 0; }
        public override int QuickSlotSkill(int slot) { R("QuickSlotSkill"); return 0; }
        public override int? StatPointsLeft { get { R("StatPointsLeft"); return 1; } }
        public override int JobGroup { get { R("JobGroup"); return 0; } }
        public override int SubJob { get { R("SubJob"); return 0; } }
        public override int Level { get { R("Level"); return 0; } }
        public override int PlayerState { get { R("PlayerState"); return 0; } }
        public override int InstrumentMotion { get { R("InstrumentMotion"); return 0; } }
        public override bool PetSummoned { get { R("PetSummoned"); return false; } }
        public override int TitleCount { get { R("TitleCount"); return 0; } }
        public override int CountItemsOfType(int itemType) { R("CountItemsOfType"); return 0; }
        public override int CountIndoorInstallItems { get { R("CountIndoorInstallItems"); return 0; } }
        public override bool HasDesignItem(int itemType) { R("HasDesignItem"); return false; }
        public override GuideEquipSlot? Equip(string slotName, int slotType) { R("Equip"); return default; }
        public override bool HasItemCount(int itemId, int count) { R("HasItemCount"); return false; }
        public override bool HasItemOfType(int itemType) { R("HasItemOfType"); return false; }
        public override bool HasItemWithIconCode(int iconCode) { R("HasItemWithIconCode"); return false; }
        public override int IconCodeOf(int itemId) { R("IconCodeOf"); return 0; }
        public override int FieldId { get { R("FieldId"); return 0; } }
        public override int? ContinentCode { get { R("ContinentCode"); return 1; } }
        public override int DungeonEnterState(int dungeonId) { R("DungeonEnterState"); return 0; }
        public override bool? NearestInteractableUsable { get { R("NearestInteractableUsable"); return true; } }
        public override (float Distance, int State)? InteractObject(int interactId) { R("InteractObject"); return default; }
        public override bool PortalNearby { get { R("PortalNearby"); return false; } }
        public override bool ReturnPortal { get { R("ReturnPortal"); return false; } }
        public override bool IsQuestRewarded(int questId) { R("IsQuestRewarded"); return false; }
        public override bool IsQuestActive(int questId) { R("IsQuestActive"); return false; }
        public override bool IsQuestComplete(int questId) { R("IsQuestComplete"); return false; }
        public override bool HasTrophyOfType(int? type) { R("HasTrophyOfType"); return false; }
        public override bool InGuild { get { R("InGuild"); return false; } }
        public override bool GuildPvpSeason { get { R("GuildPvpSeason"); return false; } }
        public override bool HasManufacture { get { R("HasManufacture"); return false; } }
        public override long Balance(int moneyType) { R("Balance"); return 0L; }
        public override int HouseMode { get { R("HouseMode"); return 0; } }
        public override bool IsMyHome { get { R("IsMyHome"); return false; } }
        public override bool HasCubeOfItem(int itemId) { R("HasCubeOfItem"); return false; }
        public override int CubeCount { get { R("CubeCount"); return 0; } }
        public override bool HasCubeWithIconCode(int iconCode) { R("HasCubeWithIconCode"); return false; }
        public override bool? CubeAttachableAt(Vector3 pos) { R("CubeAttachableAt"); return true; }
        public override long? UgcMapExpire { get { R("UgcMapExpire"); return 1L; } }
        public override bool UgcMoneyToCollect { get { R("UgcMoneyToCollect"); return false; } }
        public override long? EarliestSaleEnd { get { R("EarliestSaleEnd"); return 1L; } }
        public override Ms2.Formats.World.IRegionSkillEffects Effects { get { R("Effects"); return default; } }
        public override int CameraMode { get { R("CameraMode"); return 0; } }
    }

    static IEnumerable<(string Name, string Dir)> DataSets()
    {
        foreach (var (name, env) in new[] { ("MS2_DATA", "MS2_DATA"), ("GMS2", "MS2_GMS2_DATA"), ("KMS2_DEV", "MS2_KMS2DEV_DATA") })
        {
            var d = Environment.GetEnvironmentVariable(env);
            if (!string.IsNullOrEmpty(d) && Directory.Exists(d)) yield return (name, d);
        }
    }

    static string LiveGuideSource()
    {
        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
        {
            var f = Path.Combine(dir.FullName, "game", "net", "LiveSession.Guide.cs");
            if (File.Exists(f)) return File.ReadAllText(f);
        }
        return null;
    }

    /// <summary>The members LiveGuideWorld overrides (its class body in the source).</summary>
    static HashSet<string> LiveGives(string source)
    {
        int at = source.IndexOf("sealed class LiveGuideWorld", StringComparison.Ordinal);
        Assert.That(at, Is.GreaterThanOrEqualTo(0), "LiveGuideWorld in LiveSession.Guide.cs");
        var set = new HashSet<string>();
        foreach (Match m in Regex.Matches(source[at..], @"public override (?:\([^)]*\)\??\s*|[^=({;]*?)(\w+)\s*(?:=>|\(|\{|\r?\n)"))
            set.Add(m.Groups[1].Value);
        set.UnionWith(BaseGives);
        return set;
    }

    [Test]
    public void EveryGuideConditionReadIsGivenByTheLiveWorldOrListed()
    {
        var sets = DataSets().ToList();
        if (sets.Count == 0) Assert.Ignore("no data set (MS2_DATA, MS2_GMS2_DATA, MS2_KMS2DEV_DATA)");
        var source = LiveGuideSource();
        if (source == null) Assert.Ignore("game/net/LiveSession.Guide.cs not found above the test directory");
        var gives = LiveGives(source);
        // type -> per data set entry count; type -> reads; read -> per data set entry count
        var typeCount = new SortedDictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
        var typeReads = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        var readCount = new SortedDictionary<string, int[]>();
        var unknown = new List<string>();
        int[] total = new int[sets.Count], stubbed = new int[sets.Count];
        for (int si = 0; si < sets.Count; si++)
        {
            var table = GuideTable.Parse(new PackSet(sets[si].Dir));
            foreach (var ev in table.Events)
            foreach (var e in ev.Descendants("condition"))
            {
                string type = (string)e.Attribute("type") ?? "";
                if (!typeCount.TryGetValue(type, out var tc)) typeCount[type] = tc = new int[sets.Count];
                tc[si]++; total[si]++;
                if (type.Equals("allOf", StringComparison.OrdinalIgnoreCase) || type.Equals("anyOne", StringComparison.OrdinalIgnoreCase)) continue;   // their children are entries of their own
                GuideCondition c;
                try { c = GuideCondition.Create(e); }
                catch (GuideLoadException) { unknown.Add($"{sets[si].Name} {type}"); continue; }
                var w = new RecordingWorld();
                try { c.Check(new GuideManager(w)); } catch (Exception) { }
                if (!typeReads.TryGetValue(type, out var tr)) typeReads[type] = tr = new SortedSet<string>();
                tr.UnionWith(w.Reads);
                bool stub = false;
                foreach (var r in w.Reads)
                {
                    if (!readCount.TryGetValue(r, out var rc)) readCount[r] = rc = new int[sets.Count];
                    rc[si]++;
                    if (!gives.Contains(r)) stub = true;
                }
                if (stub) stubbed[si]++;
            }
        }
        string cols = string.Join(" | ", sets.Select(s => s.Name));
        var report = new System.Text.StringBuilder();
        report.AppendLine($"guide condition entries ({cols}): {string.Join(" | ", total)}; decided by a stub read: {string.Join(" | ", stubbed)}");
        report.AppendLine($"| type | {cols} | reads | not given |");
        foreach (var (type, n) in typeCount)
        {
            var reads = typeReads.GetValueOrDefault(type) ?? new SortedSet<string>();
            report.AppendLine($"| {type} | {string.Join(" | ", n)} | {string.Join(", ", reads)} | {string.Join(", ", reads.Where(r => !gives.Contains(r)))} |");
        }
        report.AppendLine($"| read | {cols} | given |");
        foreach (var (r, n) in readCount)
            report.AppendLine($"| {r} | {string.Join(" | ", n)} | {(gives.Contains(r) ? "yes" : "STILL-OPEN")} |");
        TestContext.Progress.WriteLine(report.ToString());
        Assert.Multiple(() =>
        {
            Assert.That(unknown, Is.Empty, "condition types the client's registry has but GuideCondition.Create does not");
            Assert.That(readCount.Keys.Where(r => !gives.Contains(r) && !StillOpen.ContainsKey(r)), Is.Empty,
                "reads the game files make that the live world does not give and StillOpen does not list");
            Assert.That(StillOpen.Keys.Where(gives.Contains), Is.Empty, "StillOpen lists a read the live world now gives");
        });
    }
}
