using System.Xml;
using System.Xml.Linq;
using Godot;
using Ms2.Formats.Options;
using Ms2.Formats.Pack;

namespace Ms2Godot;

/// <summary>
/// The client's option store (CGameOption, gameoption.cpp), loaded once at startup. The client keeps the game options in
/// "../../Config/GameOption.xml" next to its exe (GetOptionFolder unit_141274d10_uihelpers.cpp:401-404, GetOptionFilePath
/// gameoption.cpp:1693-1716); ours is user://Config/GameOption.xml in the same shape. When that file is missing, unreadable or
/// not version 0x15 it is deleted and the shipped default, table/default/defaultgameoption.xml in Xml.m2d
/// (GetDefaultOptionFolder "../../Data/Xml/Table/Default", unit_141274d10_uihelpers.cpp:433-436), is read in its place
/// (LoadGameOption gameoption.cpp:1720-1745; IsOptionFileMissingOrOutdated gameoption.cpp:2039-2065). The loaded document is
/// kept and written back on <see cref="Save"/>, as SaveGameSettings / WriteGameAndAccountFiles do (gameoption.cpp:1852-1875).
/// </summary>
public static class GameOptions
{
    public const string UserFile = "user://Config/GameOption.xml";

    /// <summary>The file in use: <see cref="UserFile"/>, or the one named by the user argument --options-file (test runs
    /// that must not read or write the shared user folder).</summary>
    public static string FilePath
    {
        get
        {
            var args = OS.GetCmdlineUserArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--options-file") return System.IO.Path.GetFullPath(args[i + 1]);
            return ProjectSettings.GlobalizePath(UserFile);
        }
    }
    const string DefaultEntry = "table/default/defaultgameoption.xml";

    /// <summary>The KMS2 client's region index, DAT_143ab0248 = 0 (KR, localepath.cpp:505-542).</summary>
    public const int RegionIndex = 0;

    // published last by Load (volatile): a thread that sees it sees the language, the voice and the document it came with
    static volatile GameOption current;
    static readonly object loadGate = new();
    /// <summary>How many times the options were read (a test checks that threads asking at once read them once).</summary>
    internal static int Loads;

    /// <summary>The options of the first use, read once: the field prefetch (a pool thread) and the UI (the main thread) ask
    /// at the same time, and the second waits for the first's load instead of reading the file again over it.</summary>
    static void EnsureLoaded()
    {
        if (current != null) return;
        lock (loadGate) if (current == null) Load();
    }

    static XDocument doc;

    // ---- the account and character files (LoadAccountOption gameoption.cpp:1757, LoadCharacterOption :1785) ----------

    /// <summary>The character file's sound flag (Unk_SoundFlag, "sound"/"disableJobSound", FUN_1412603d0).</summary>
    public static SoundFlag SoundFlag { get { EnsureCharacter(); return soundFlag; } }
    /// <summary>The character file's "interface"/"EnableMouseControl" (GetInterfaceChar).</summary>
    public static GameOption.CharacterFlag MouseControl { get { EnsureCharacter(); return mouseControl; } }
    /// <summary>The character file's "status"/"displayLearningQuestV2" (GetStatusChar).</summary>
    public static GameOption.CharacterFlag LearningQuest { get { EnsureCharacter(); return learningQuest; } }
    /// <summary>The account file's community block (m_account).</summary>
    public static GameOption.AccountCommunity AccountCommunity
    {
        get { EnsureCharacter(); return accountCommunity; }
        set { EnsureCharacter(); accountCommunity = value; }
    }

    /// <summary>The character file's "gamepad" block (CGameOption +0x1a0, LoadGameOptionGamepad @0x14125f330).</summary>
    public static GameOption.GamePad GamePad { get { EnsureCharacter(); return gamePad; } }
    /// <summary>CGameOption +0x1b4, the stored personal info agreement. The server's answer is not read yet, so it starts empty.</summary>
    public static PersonalInfoAgreement Agreement { get; } = new();

    static bool FeatureById(int id) => id >= 1 && id <= Ms2.Ui.FeatureTable.Names.Length && Settings.DataDir is { } data
        && Ms2.Ui.LobbyFeatures.IsEnabledByName(PackSet.Shared(data), Ms2.Ui.FeatureTable.Names[id - 1]);

    static GameOption.GamePad gamePad = new();
    static SoundFlag soundFlag = new();
    static GameOption.CharacterFlag mouseControl = GameOption.CharacterFlag.MouseControl(), learningQuest = GameOption.CharacterFlag.LearningQuest();
    static GameOption.AccountCommunity accountCommunity = new();
    static XDocument accountDoc, characterDoc;
    static string accountPath, characterPath, loadedFor;

    /// <summary>GetAccountFolder "../../Config/&lt;account id&gt;" and GetCharacterFolder "…/&lt;account id&gt;/&lt;character
    /// id&gt;" (unit_141274d10_uihelpers.cpp:408-420), beside the game file. Null without a chosen character.</summary>
    public static (string Account, string Character)? Folders()
    {
        if (Ms2Godot.Flow.Session.Chosen?.User is not { } u) return null;
        var root = System.IO.Path.GetDirectoryName(FilePath);
        var account = System.IO.Path.Combine(root, ((ulong)u.AccountId).ToString());
        return (account, System.IO.Path.Combine(account, ((ulong)u.CharacterId).ToString()));
    }

    /// <summary>The account and character files of the chosen character, read when it changes. Without one the blocks keep
    /// their reset values over the default documents and nothing is written.</summary>
    static void EnsureCharacter()
    {
        var f = Folders();
        var key = f?.Character ?? "";
        if (characterDoc != null && key == loadedFor) return;
        loadedFor = key;
        accountPath = f == null ? null : System.IO.Path.Combine(f.Value.Account, "AccountOption.xml");
        characterPath = f == null ? null : System.IO.Path.Combine(f.Value.Character, "CharacterOption.xml");
        accountDoc = ReadOrDefault(accountPath, "table/default/defaultaccountoption.xml");
        characterDoc = ReadOrDefault(characterPath, "table/default/defaultcharacteroption.xml");
        accountCommunity = new GameOption.AccountCommunity();
        accountCommunity.Load(accountDoc);
        soundFlag = new SoundFlag();
        soundFlag.Load(characterDoc);
        mouseControl = GameOption.CharacterFlag.MouseControl();
        mouseControl.Load(characterDoc);
        learningQuest = GameOption.CharacterFlag.LearningQuest();
        gamePad = new GameOption.GamePad();
        gamePad.Load(characterDoc, FeatureById(0xc3), FeatureById(0xd6));
        // FUN_14125c970: a file without the switch gets it from the character's creation time (on only before 2017-06-29;
        // every character here is created by this server, so off), written back at once.
        if (!learningQuest.Load(characterDoc) && f != null)
        {
            learningQuest.Value = false;
            learningQuest.Save(characterDoc);
            WriteCharacterFile();
        }
    }

    /// <summary>IsOptionFileMissingOrOutdated then the user file, else the file deleted and the default read
    /// (gameoption.cpp:1760-1768, :1788-1796).</summary>
    static XDocument ReadOrDefault(string path, string entry)
    {
        XDocument d = path == null ? null : ReadFile(path);
        if (d != null && GameOption.IsCurrent(d)) return d;
        if (path != null && System.IO.File.Exists(path)) System.IO.File.Delete(path);
        return ReadDefault(entry) ?? new XDocument(new XElement("ms2"));
    }

    static XDocument ReadDefault(string entry)
    {
        try
        {
            if (Settings.DataDir is not { } data || !System.IO.File.Exists(System.IO.Path.Combine(data, "Xml.m2d"))) return null;
            var packs = PackSet.Shared(data);     // the session's archives, open once (not an index read per call)
            return packs.Exists("Xml", entry) ? Parse(packs.Read("Xml", entry)) : null;
        }
        catch (Exception ex) { GD.PushWarning("options: " + ex.Message); return null; }
    }

    /// <summary>SaveCharacterSettings (gameoption.cpp:1879-1889): the character blocks, the version, the file. The blocks
    /// not modelled here (autoLooting, etc) stay as they were read.</summary>
    public static void SaveCharacterSettings()
    {
        EnsureCharacter();
        soundFlag.Save(characterDoc);
        mouseControl.Save(characterDoc);
        learningQuest.Save(characterDoc);
        gamePad.Save(characterDoc, FeatureById(0xc3), FeatureById(0xd6));
        WriteCharacterFile();
    }

    static void WriteCharacterFile()
    {
        GameOption.SetOptionVersion(characterDoc);
        Write(characterPath, characterDoc);
    }

    static void Write(string path, XDocument d)
    {
        if (path == null) { GD.Print("options: no character chosen, " + "the account and character files are not written"); return; }
        try { GameOption.WriteFile(path, d); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { GD.PushWarning("options: " + ex.Message); }
    }

    /// <summary>The options, loaded on first use.</summary>
    public static GameOption Current
    {
        get
        {
            EnsureLoaded();
            return current;
        }
    }

    /// <summary>SetVoice at load (CGameOption::LoadLanguageOption, gameoption.cpp:1847): the voice in use until a restart.</summary>
    public static int Voice { get; private set; }

    /// <summary>
    /// SetLanguage at load (CGameOption::LoadLanguageOption, gameoption.cpp:1821-1849): the language in use until a restart.
    /// The command line's "-language &lt;code&gt;" (unit_1414b1230.cpp:334-339, SetLanguageByCode and SetVoiceByCode, then
    /// g_bLanguageSet) makes LoadLanguageOption return before the option file is read; ours is the user argument
    /// --language. Otherwise it is GameOption.xml's language/languageIndex, which defaults to the region's language (the
    /// Unk_LgState reset, gameoptionstruct.cpp:2466-2490: region KR gives 0, KR). The NA region's fresh-file pick from the
    /// Windows UI language (gameoption.cpp:1840-1844) is not ported: <see cref="RegionIndex"/> is KR.
    /// IsNexonPlatform (unit_1406c3230_atlbytearrayglue.cpp:88) is taken as 0, so the argument wins over the file.
    /// </summary>
    public static int Language { get { EnsureLoaded(); return language; } }
    static int language;

    /// <summary>The string folder of <see cref="Language"/> in Xml.m2d (string/&lt;folder&gt;/*.xml, g_strLocaleDir2;
    /// GameOptionLocale.StringDirectory). Every string table reads from it.</summary>
    public static string StringLanguage => GameOptionLocale.StringDirectory(Language);

    /// <summary>The --language user argument (the client's "-language"), or null.</summary>
    static string LanguageArgument()
    {
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--language") return args[i + 1];
        return null;
    }

    /// <summary>True when the user file was missing or outdated and the shipped default was read instead.</summary>
    public static bool FromDefault { get; private set; }

    /// <summary>Reads the user file or the default (see the class summary). With no data folder the options stay at their
    /// reset values over an empty document, as the client's do when it cannot read either file. One load at a time; the
    /// options read by another thread meanwhile are the previous load's until this one is complete.</summary>
    public static void Load()
    {
        lock (loadGate) LoadLocked();
    }

    static void LoadLocked()
    {
        System.Threading.Interlocked.Increment(ref Loads);
        var o = new GameOption(RegionIndex);
        XDocument doc = null;
        var path = FilePath;
        var user = ReadFile(path);
        bool fromDefault = user == null || !GameOption.IsCurrent(user);
        PackSet packs = null;
        try
        {
            if (Settings.DataDir is { } data && System.IO.File.Exists(System.IO.Path.Combine(data, "Xml.m2d"))) packs = PackSet.Shared(data);
            if (fromDefault)
            {
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                if (packs != null && packs.Exists("Xml", DefaultEntry)) doc = Parse(packs.Read("Xml", DefaultEntry));
            }
            else doc = user;
            if (doc == null)
            {
                GD.PushWarning("options: no GameOption.xml and no default, using reset values");
                doc = new XDocument(new XElement("ms2"));
            }
            o.Load(doc, name => packs != null && Ms2.Ui.LobbyFeatures.IsEnabled(packs, name));
        }
        catch (Exception ex)
        {
            GD.PushWarning("options: " + ex.Message);
            doc ??= new XDocument(new XElement("ms2"));
        }
        int voice = o.LanguageOption.VoiceIndex, lang = o.LanguageOption.LanguageIndex;
        if (LanguageArgument() is { } code)
        {
            lang = GameOptionLocale.LanguageByCode(code, 0);    // DAT_143ab0240 starts at 0 (KR)
            voice = GameOptionLocale.LanguageByCode(code, 0);
        }
        Voice = voice;
        language = lang;
        FromDefault = fromDefault;
        GameOptions.doc = doc;
        current = o;
        var g = o.GraphicOption;
        GD.Print($"options: {(fromDefault ? "default" : "GameOption.xml")}, {g.Width}x{g.Height} mode {g.ScreenMode} row {g.ResolutionIndex}, music {o.SoundOption.CategoryVolume(2):F2}");
    }

    /// <summary>SaveGameSettings: the blocks into the kept documents (the community request switches into the account
    /// one), the version, then both files (gameoption.cpp:1852-1875).</summary>
    public static void Save()
    {
        var o = Current;
        EnsureCharacter();
        // a scene started on its own shows its window in the option but keeps the file's (ScreenMode.StoredWindow)
        var g = o.GraphicOption;
        var shown = (g.ScreenMode, g.ResolutionIndex, g.Width, g.Height);
        if (ScreenMode.StoredWindow is { } stored) (g.ScreenMode, g.ResolutionIndex, g.Width, g.Height) = stored;
        try { o.Save(doc); }
        finally { (g.ScreenMode, g.ResolutionIndex, g.Width, g.Height) = shown; }
        accountCommunity.Save(accountDoc);
        GameOption.SetOptionVersion(accountDoc);
        try { GameOption.WriteFile(FilePath, doc); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { GD.PushWarning("options: " + ex.Message); }
        Write(accountPath, accountDoc);
    }

    static XDocument ReadFile(string path)
    {
        try { return System.IO.File.Exists(path) ? Parse(System.IO.File.ReadAllBytes(path)) : null; }
        catch (XmlException) { return null; }
    }

    static XDocument Parse(byte[] bytes) => XDocument.Parse(System.Text.Encoding.UTF8.GetString(bytes).TrimStart('﻿'));
}
