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

    /// <summary>The options, loaded on first use.</summary>
    public static GameOption Current
    {
        get
        {
            if (current == null) Load();
            return current;
        }
    }

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
            if (Settings.DataDir is { } data && System.IO.File.Exists(System.IO.Path.Combine(data, "Xml.m2d"))) packs = new PackSet(data);
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
        finally { packs?.Dispose(); }
        var g = current.GraphicOption;
        GD.Print($"options: {(FromDefault ? "default" : "GameOption.xml")}, {g.Width}x{g.Height} mode {g.ScreenMode} row {g.ResolutionIndex}, music {current.SoundOption.CategoryVolume(2):F2}");
    }

    /// <summary>SaveGameSettings: the blocks into the kept document, the version, then the file (gameoption.cpp:1852-1875).</summary>
    public static void Save()
    {
        var o = Current;
        o.Save(doc);
        var path = FilePath;
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            var settings = new XmlWriterSettings { Indent = true, IndentChars = "    ", Encoding = new System.Text.UTF8Encoding(false) };
            using var w = XmlWriter.Create(path, settings);
            doc.Save(w);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { GD.PushWarning("options: " + ex.Message); }
    }

    static XDocument ReadFile(string path)
    {
        try { return System.IO.File.Exists(path) ? Parse(System.IO.File.ReadAllBytes(path)) : null; }
        catch (XmlException) { return null; }
    }

    static XDocument Parse(byte[] bytes) => XDocument.Parse(System.Text.Encoding.UTF8.GetString(bytes).TrimStart('﻿'));
}
