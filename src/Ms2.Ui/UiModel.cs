namespace Ms2.Ui;

/// <summary>
/// The status events the client's UIStatusEventDispatcher sends its listeners (UIStatusEventDispatcher::NotifyStatusChanged,
/// uistatuseventlistener.cpp:257; listener slot vfunc_oa0_3 + the event number). A HUD controller redraws its part on these.
/// </summary>
public enum StatusEvent
{
    Hp = 0,         // stat 4 current / total
    Sp = 1,         // stat 7
    Ep = 2,         // stat 10 (stamina)
    Exp = 3,        // the exp counter (ServerEnter / ExpUp) against the next-level exp of nextexp.xml
    Job = 4,        // sub job code + job name (string table 6)
    Name = 5,
    Level = 6,
    Guild = 7,
    Meso = 8,       // the meso counter shown in the upper HUD
    State = 9,
    Unknown10 = 10,
    Merat = 11,     // the merat balance
}

/// <summary>The local player's numbers the HUD movies read (the client keeps these in PC stats and a few globals).</summary>
public sealed class PlayerState
{
    public string Name = "";
    /// <summary>The equipped title, shown before the name (GetPlayerNameWithTitles); empty = none.</summary>
    public string Title = "";
    public int Level = 1;
    /// <summary>The sub job code (PC::GetSubJob), the id in string/&lt;lang&gt;/jobname.xml (e.g. 100 Knight).</summary>
    public int SubJob;
    public long HpCur, HpMax, SpCur, SpMax, EpCur, EpMax;
    /// <summary>The exp counter in this level (DAT_143ab28a0) and the rest exp bonus pool (DAT_143ab28a8).</summary>
    public long Exp, RestExp;
    public long Mesos, Merats;
    /// <summary>The prestige counters (CSceneStateGame::OnPrestige, scenestategame.cpp:8815): the exp in this level
    /// (DAT_143ab2958), the player's adventure level (PC vfunc_141), the level at or below which a level shows full
    /// (DAT_143ab2960) and the exp shown for the level above it (DAT_143ab2968).</summary>
    public long AdventureExp, AdventureNextExp;
    public int AdventureLevel, AdventureFilledLevel;
    public int Channel;
    public bool Battle, Riding;
    public string NameWithTitle => string.IsNullOrEmpty(Title) ? Name : Title + " " + Name;
}

/// <summary>Field context of the HUD (minimap and upper HUD).</summary>
public sealed class FieldState
{
    /// <summary>The map id (mapxblock / minimap image name, e.g. 02000001).</summary>
    public int MapId;
    /// <summary>The map's xblock name (mapxblock/&lt;name&gt;.xml).</summary>
    public string XBlock = "";
    /// <summary>The map's display name (string table mapname).</summary>
    public string Name = "";
    public int Channel = 1;
    public int[] Channels = { 1 };
}

/// <summary>
/// The MS2DataModel surface: what the client's UI controllers read from game state when they push data into the movies.
/// One instance per session; the game side (network, field) fills it and raises <see cref="Changed"/> the way the
/// client's packet handlers call UIStatusEventDispatcher.
/// </summary>
public sealed class UiModel
{
    public readonly PlayerState Player = new();
    public readonly FieldState Field = new();
    public readonly GameTables Tables;
    /// <summary>CKeyTable: the keys and their bound actions (key captions on the quick slots).</summary>
    public KeyTable Keys = KeyTable.Empty;
    /// <summary>The server's KeyTable packet state (Ms2.Net): the bindings applied to <see cref="Keys"/> and the quick slot pages
    /// with the current page. Null = none received.</summary>
    public Ms2.Net.Packets.ServerKeyTable ServerKeys;
    /// <summary>The client's feature switches (CFeatureManager::IsFeatureEnabled id) the HUD code tests.</summary>
    public readonly HashSet<int> Features = new() { Feature.SearchTooltip, Feature.InputMode, Feature.GamePad };
    /// <summary>The server clock the upper HUD shows (GetServerTime); null = local time.</summary>
    public Func<DateTime> ServerTime;
    /// <summary>A label the host shows when the values are not from a live character (fixture data); null = live.</summary>
    public string FixtureLabel;

    /// <summary>The rest exp bonus in percent shown in the exp tooltip: a client constant (ClientConstants m_6b00) / 100.
    /// The constant's key in the client's tables is not identified yet; 100 until it is.</summary>
    public int RestExpRatePercent = 100;

    public event Action<StatusEvent> Changed;

    public UiModel(GameTables tables) { Tables = tables ?? GameTables.Empty; }

    public void Notify(StatusEvent e) => Changed?.Invoke(e);
    public DateTime Now => ServerTime?.Invoke() ?? DateTime.Now;
    /// <summary>
    /// CFeatureManager::IsFeatureEnabled (featuremanager.cpp:845): 0 is off, 1 is on, any other id is its name's level
    /// within the server type's (<see cref="FeatureEnabledByName"/>, names by <see cref="FeatureTable"/>). A host without
    /// the feature table uses <see cref="Features"/>.
    /// </summary>
    public bool IsFeatureEnabled(int id) => FeatureEnabledByName == null ? Features.Contains(id)
        : id == 1 || (id > 1 && id <= FeatureTable.Names.Length && FeatureEnabledByName(FeatureTable.Names[id - 1]));
    /// <summary>
    /// CFeatureManager by name (table/feature.xml level within the server type's level): what the movies' Common class is
    /// told (<see cref="FeatureTable.EnabledString"/>). Null: the host has no feature table and the movies see none on.
    /// </summary>
    public Func<string, bool> FeatureEnabledByName;

    // ---- what the controllers compute from the state (the client's helper functions) ----

    /// <summary>CExpTable::GetNextExp(level) (UIExpBar::GetNextLevelExp, uiexpbar.cpp:137).</summary>
    public long NextLevelExp => Tables.NextExp(Player.Level);
}

/// <summary>Feature ids the HUD controllers test (CFeatureManager::IsFeatureEnabled).</summary>
public static class Feature
{
    /// <summary>0x2f: the search button's dictionary tooltip (uiupperhuddialog.cpp:270).</summary>
    public const int SearchTooltip = 0x2f;
    /// <summary>0xaf: keyboard / mouse input mode buttons (uiupperhuddialog.cpp:280).</summary>
    public const int InputMode = 0xaf;
    /// <summary>0xb5: game pad buttons (uiupperhuddialog.cpp:292).</summary>
    public const int GamePad = 0xb5;
    /// <summary>0xc9: the client version text (uiupperhuddialog.cpp:300).</summary>
    public const int VersionText = 0xc9;
    /// <summary>0x3d: merats (UIUpperHudDialog vfunc_46 hides m_marat without it, uiupperhuddialog.cpp:462; vfunc_oa0_14).</summary>
    public const int Merat = 0x3d;
}
