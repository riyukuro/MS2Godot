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

    static GameOption current;

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

    static SoundFlag soundFlag = new();
    static GameOption.CharacterFlag mouseControl = GameOption.CharacterFlag.MouseControl(), learningQuest = GameOption.CharacterFlag.LearningQuest();
    static GameOption.AccountCommunity accountCommunity = new();
    static XDocument accountDoc, characterDoc;
    static string accountPath, characterPath, loadedFor;

    /// <summary>GetAccountFolder "../../Config/&lt;account id&gt;" and GetCharacterFolder "…/&lt;account id&gt;/&lt;character
    /// id&gt;" (unit_141274d10_uihelpers.cpp:408-420), beside the game file. Null without a chosen character.</summary>
    static (string Account, string Character)? Folders()
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
    /// not modelled here (autoLooting, etc, gamepad) stay as they were read.</summary>
    public static void SaveCharacterSettings()
    {
        EnsureCharacter();
        soundFlag.Save(characterDoc);
        mouseControl.Save(characterDoc);
        learningQuest.Save(characterDoc);
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
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            var settings = new XmlWriterSettings { Indent = true, IndentChars = "    ", Encoding = new System.Text.UTF8Encoding(false) };
            using var w = XmlWriter.Create(path, settings);
            d.Save(w);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { GD.PushWarning("options: " + ex.Message); }
    }

    /// <summary>The options, loaded on first use.</summary>
    public static GameOption Current
    {
        get
        {
            if (current == null) Load();
            return current;
        }
    }

    /// <summary>SetVoice at load (CGameOption::LoadLanguageOption, gameoption.cpp:1847): the voice in use until a restart.</summary>
    public static int Voice { get; private set; }

    /// <summary>True when the user file was missing or outdated and the shipped default was read instead.</summary>
    public static bool FromDefault { get; private set; }

    /// <summary>Reads the user file or the default (see the class summary). With no data folder the options stay at their
    /// reset values over an empty document, as the client's do when it cannot read either file.</summary>
    public static void Load()
    {
        current = new GameOption(RegionIndex);
        doc = null;
        var path = FilePath;
        var user = ReadFile(path);
        FromDefault = user == null || !GameOption.IsCurrent(user);
        PackSet packs = null;
        try
        {
            if (Settings.DataDir is { } data && System.IO.File.Exists(System.IO.Path.Combine(data, "Xml.m2d"))) packs = PackSet.Shared(data);
            if (FromDefault)
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
            current.Load(doc, name => packs != null && Ms2.Ui.LobbyFeatures.IsEnabled(packs, name));
        }
        catch (Exception ex)
        {
            GD.PushWarning("options: " + ex.Message);
            doc ??= new XDocument(new XElement("ms2"));
        }
        Voice = current.LanguageOption.VoiceIndex;
        var g = current.GraphicOption;
        GD.Print($"options: {(FromDefault ? "default" : "GameOption.xml")}, {g.Width}x{g.Height} mode {g.ScreenMode} row {g.ResolutionIndex}, music {current.SoundOption.CategoryVolume(2):F2}");
    }

    /// <summary>SaveGameSettings: the blocks into the kept documents (the community request switches into the account
    /// one), the version, then both files (gameoption.cpp:1852-1875).</summary>
    public static void Save()
    {
        var o = Current;
        EnsureCharacter();
        o.Save(doc);
        accountCommunity.Save(accountDoc);
        GameOption.SetOptionVersion(accountDoc);
        var path = FilePath;
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            var settings = new XmlWriterSettings { Indent = true, IndentChars = "    ", Encoding = new System.Text.UTF8Encoding(false) };
            using var w = XmlWriter.Create(path, settings);
            doc.Save(w);
        }
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
