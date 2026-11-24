namespace Ms2.Ui;

/// <summary>
/// The end of CharacterRemoved (@0x1418f0e20, uicharacterlistdialog.cpp:1410-1413): a deleted character's folder goes.
/// The client formats RESOURCE_PATH ("./Data/Resource/" in both data sets' table/commonresource.xml) with
/// FUN_1412756c0(account, character), "../../Config/&lt;account&gt;/&lt;character&gt;" (unit_141274d10_uihelpers.cpp:424-429), so
/// the folder GetCharacterFolder names, where the dialogs' settings, the character options and the learning quest's seen
/// file are kept; when it exists, DeleteDirectoryTree (util.cpp:920-952) runs on that path.
/// <para>DeleteDirectoryTree takes a base it appends "*.*" to, and the path here has no "/" at its end, so
/// FindFirstFileA lists the account folder's entries whose name starts with the character id: the character's folder,
/// and as the client does, any other file or folder there whose name starts with the same digits. A file found is deleted;
/// a folder found, unless its name starts with '.', is emptied the same way (each file in it deleted, each such folder in
/// it emptied and removed) and then removed. A folder whose name starts with '.' is kept, and so is the folder holding it
/// (RemoveDirectoryA fails on a folder that is not empty); every failure is ignored, as the client ignores its return
/// values. The client's FindFirstFileA compares without case; the names here are digits.</para>
/// <para>Chosen here: a link (a symbolic link or junction) is removed itself and never followed, where FindFirstFileA's
/// walk would go into its target.</para>
/// </summary>
public static class CharacterFolderRemoval
{
    /// <summary>Removes <paramref name="characterId"/>'s entries under <paramref name="accountFolder"/> as above; the
    /// number of files deleted. Nothing happens when the character's own folder does not exist (DirectoryExists).</summary>
    public static int Remove(string accountFolder, ulong characterId)
    {
        string id = characterId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (string.IsNullOrEmpty(accountFolder) || !Directory.Exists(Path.Combine(accountFolder, id))) return 0;
        int deleted = 0;
        foreach (var entry in Entries(accountFolder))
        {
            string name = Path.GetFileName(entry);
            if (!name.StartsWith(id, StringComparison.OrdinalIgnoreCase)) continue;
            deleted += RemoveEntry(entry);
        }
        return deleted;
    }

    /// <summary>One entry FindFirstFileA listed: a file deleted, a folder not starting with '.' emptied and removed.</summary>
    static int RemoveEntry(string entry)
    {
        // a link is removed itself, never followed: the client's FindFirstFileA would walk into a junction's target, which
        // could hold files that are not the character's
        if (new FileInfo(entry).LinkTarget != null)
        {
            try { if (Directory.Exists(entry)) Directory.Delete(entry, false); else File.Delete(entry); return 1; }
            catch (IOException) { return 0; }
            catch (UnauthorizedAccessException) { return 0; }
        }
        if (Directory.Exists(entry))
        {
            if (Path.GetFileName(entry).StartsWith('.')) return 0;
            int deleted = 0;
            foreach (var inner in Entries(entry)) deleted += RemoveEntry(inner);
            try { Directory.Delete(entry, false); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return deleted;
        }
        try { File.Delete(entry); return 1; }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
    }

    static string[] Entries(string folder)
    {
        try { return Directory.GetFileSystemEntries(folder); }
        catch (IOException) { return Array.Empty<string>(); }
        catch (UnauthorizedAccessException) { return Array.Empty<string>(); }
    }
}
