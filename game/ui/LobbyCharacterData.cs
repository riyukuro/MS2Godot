using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Ui;

namespace Ms2Godot.Ui;

/// <summary>The tables FUN_1418f42c0 reads, loaded once from the player's Xml.m2d.</summary>
public sealed class LobbyCharacterData
{
    /// <summary>
    /// CommonResource m_strDefaultProfileImagePath (table/commonresource.xml DEFAULT_PROFILE_IMAGE_PATH): what
    /// FUN_141028be0 (ugcimagepath.cpp) gives a character without a profile picture.
    /// </summary>
    public readonly string NoProfile;

    readonly Dictionary<string, string> mapNames, jobNames;
    readonly Func<string, string> common;
    readonly Dictionary<int, int> enterReturn = new();

    public LobbyCharacterData(PackSet packs, string lang = "en")
    {
        var strings = new GameStrings(packs, lang);
        mapNames = strings.Table("mapname", "name");     // CStringTable 3 (table/string.xml mapName)
        jobNames = strings.Table("jobname", "name");     // CStringTable 6 (jobName)
        common = strings.Common();
        var res = Xml(packs, "table/commonresource.xml");
        NoProfile = (string)res?.Root?.Element("DEFAULT_PROFILE_IMAGE_PATH")?.Attribute("value") ?? "";
        // CFieldDataCli m_nEnterReturnId: <property enterreturnid> of the field's default environment
        var fields = Xml(packs, FieldData.Entry);
        foreach (var fd in fields?.Root?.Elements("fieldData") ?? Enumerable.Empty<XElement>())
        {
            var env = fd.Elements("environment").FirstOrDefault(e => e.Attribute("locale") == null);
            if (int.TryParse((string)fd.Attribute("id"), out int id)
                && int.TryParse((string)env?.Element("property")?.Attribute("enterreturnid"), out int ret))
                enterReturn[id] = ret;
        }
    }

    static XDocument Xml(PackSet packs, string entry) => packs.Exists("Xml", entry)
        ? XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", entry)).TrimStart('﻿'))
        : null;

    /// <summary>CStringTable::FUN_141811980: the text of an id, empty when the table has none (the map table keys are 8 digits).</summary>
    static string Lookup(Dictionary<string, string> table, int id)
        => table.TryGetValue(id.ToString("D8", CultureInfo.InvariantCulture), out var s) || table.TryGetValue(id.ToString(CultureInfo.InvariantCulture), out s) ? s ?? "" : "";

    /// <summary>
    /// The map name: CStringTable 3 of the character's map, or of the map's enter-return field when that field has one
    /// (CFieldDataCli::Find(map)->m_nEnterReturnId != 0 and that field exists).
    /// </summary>
    public string LastPos(int map)
    {
        string pos = Lookup(mapNames, map);
        if (enterReturn.TryGetValue(map, out int ret) && ret != 0 && enterReturn.ContainsKey(ret)) pos = Lookup(mapNames, ret);
        return pos;
    }

    /// <summary>
    /// FUN_14083bab0(created, "/"): year / month / day, the order for every language but English (2: month / day / year)
    /// and the European ones (4-7: day / month / year). The restored client's list shows 2026/11/10, the default order.
    /// </summary>
    public static string Date(long unixSeconds)
    {
        // FUN_14083bab0(CMsTime(m_70), "/"): the calendar date in the server's zone (CMsTime::GetLocalTm)
        var t = Ms2.Net.MsTime.LocalTm(unixSeconds);
        return $"{t.Year:D4}/{t.Month:D2}/{t.Day:D2}";
    }

    /// <summary>FUN_1418f42c0 for one character.</summary>
    public LobbyCharacterRow Row(CharacterEntry e)
    {
        var u = e.User;
        // FormatStringId(2, level, CStringTable(6, job)): common string 2 is s_char_level_job ("Lv. {0} {1}")
        string format = common("s_char_level_job") ?? "";
        string levelJob = format.Replace("{0}", u.Level.ToString(CultureInfo.InvariantCulture)).Replace("{1}", Lookup(jobNames, u.Job));
        // FUN_141028be0(id, picture, 1, ""): no picture name gives the default profile image, and so does a picture not yet
        // in the download cache (bNoDownload 1; the download it starts has no server offline)
        string img = NoProfile;
        return new LobbyCharacterRow(u.Name, u.Gender, u.Job, levelJob, img, Date(u.CreationTime), LastPos(u.MapA),
            u.CharacterId.ToString(CultureInfo.InvariantCulture));
    }
}
